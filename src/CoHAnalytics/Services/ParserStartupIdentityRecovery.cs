using System.Runtime.InteropServices;
using System.Text;
using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

/// <summary>
/// Scans pre-attachment log content for the most recent welcome identity evidence.
/// Gameplay telemetry in the scanned range is intentionally not emitted.
/// </summary>
/// <remarks>
/// <para>
/// Startup recovery seeks from the attach offset (typically EOF) backward in chunks until the
/// first valid Welcome line is found or BOF is reached. The returned welcome is necessarily the
/// most recent one in the file; callers separately decide whether it belongs to the current
/// runtime lifetime.
/// </para>
/// <para>
/// The backward chunk scanner is structured for reuse by future build-import correlation.
/// </para>
/// </remarks>
internal static class ParserStartupIdentityRecovery
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly ParserClassifier Classifier = new();

    /// <summary>
    /// Returns whether a Welcome from a predecessor daily file can belong to the currently bound
    /// Homecoming process. Predecessor recovery is rejected when process ownership or a source
    /// timestamp is unavailable, because cross-file identity must never be inferred by guesswork.
    /// </summary>
    internal static bool IsWelcomeWithinCurrentRuntime(
        ParserRawEvent welcome,
        HomecomingProcessInstance? processInstance)
    {
        if (processInstance is null)
        {
            return false;
        }

        var classified = Classifier.Classify(welcome);
        if (!CharacterIdentityResolver.IsWelcomeEvidence(classified)
            || classified.SourceTimestamp is not { } sourceTimestamp)
        {
            return false;
        }

        var processStart = processInstance.ProcessStartTime.DateTime;
        if (welcome.RawLine.StartsWith('['))
        {
            // Bracket timestamps carry minute precision only. Compare to the beginning of the
            // process-start minute so a valid Welcome written during that minute is not rejected.
            processStart = new DateTime(
                processStart.Year,
                processStart.Month,
                processStart.Day,
                processStart.Hour,
                processStart.Minute,
                0,
                DateTimeKind.Unspecified);
        }
        else
        {
            processStart = DateTime.SpecifyKind(processStart, DateTimeKind.Unspecified);
        }

        return sourceTimestamp >= processStart;
    }

    /// <summary>
    /// Scans backward from <paramref name="endOffset"/> in bounded chunks until a welcome is found
    /// or BOF is reached. Does not load the entire file into memory.
    /// </summary>
    internal static ParserRawEvent? FindMostRecentWelcomeBeforeOffset(
        Stream stream,
        long endOffset,
        int chunkSize,
        MonitoringContextId contextId,
        LogSourceId sourceId,
        ParserSourceSegmentId sourceSegmentId,
        long bindingGeneration,
        MonitoringSourceTransitionKind transitionKind,
        DateTimeOffset observedAt,
        int maximumLineBytes,
        ref long nextSequence)
    {
        if (endOffset <= 0 || chunkSize <= 0)
        {
            return null;
        }

        var position = endOffset;
        var lineBuffer = new List<byte>();
        var reversedLineEndOffset = endOffset;
        while (position > 0)
        {
            var chunkLength = (int)Math.Min(chunkSize, position);
            var scanStart = position - chunkLength;
            position = scanStart;

            var buffer = new byte[chunkLength];
            stream.Position = scanStart;
            var read = 0;
            while (read < chunkLength)
            {
                var chunk = stream.Read(buffer, read, chunkLength - read);
                if (chunk <= 0)
                {
                    break;
                }

                read += chunk;
            }

            if (read <= 0)
            {
                continue;
            }

            for (var index = read - 1; index >= 0; index--)
            {
                var value = buffer[index];
                if (value == (byte)'\n')
                {
                    if (lineBuffer.Count > 0)
                    {
                        var welcome = TryCreateWelcomeFromReversedLineBuffer(
                            lineBuffer,
                            contextId,
                            sourceId,
                            sourceSegmentId,
                            bindingGeneration,
                            transitionKind,
                            observedAt,
                            reversedLineEndOffset,
                            maximumLineBytes,
                            ref nextSequence);
                        if (welcome is not null)
                        {
                            return welcome;
                        }

                        lineBuffer.Clear();
                    }

                    reversedLineEndOffset = scanStart + index + 1;

                    continue;
                }

                if (lineBuffer.Count >= maximumLineBytes)
                {
                    lineBuffer.Clear();
                }

                lineBuffer.Add(value);
            }

            if (scanStart == 0 && lineBuffer.Count > 0)
            {
                var welcome = TryCreateWelcomeFromReversedLineBuffer(
                    lineBuffer,
                    contextId,
                    sourceId,
                    sourceSegmentId,
                    bindingGeneration,
                    transitionKind,
                    observedAt,
                    reversedLineEndOffset,
                    maximumLineBytes,
                    ref nextSequence);
                if (welcome is not null)
                {
                    return welcome;
                }

                lineBuffer.Clear();
            }
        }

        return null;
    }

    private static ParserRawEvent? TryCreateWelcomeFromReversedLineBuffer(
        List<byte> reversedLineBuffer,
        MonitoringContextId contextId,
        LogSourceId sourceId,
        ParserSourceSegmentId sourceSegmentId,
        long bindingGeneration,
        MonitoringSourceTransitionKind transitionKind,
        DateTimeOffset observedAt,
        long lineEndOffset,
        int maximumLineBytes,
        ref long nextSequence)
    {
        reversedLineBuffer.Reverse();
        while (reversedLineBuffer.Count > 0 && reversedLineBuffer[^1] == (byte)'\r')
        {
            reversedLineBuffer.RemoveAt(reversedLineBuffer.Count - 1);
        }

        if (reversedLineBuffer.Count == 0 || reversedLineBuffer.Count > maximumLineBytes)
        {
            reversedLineBuffer.Clear();
            return null;
        }

        var lineStartOffset = lineEndOffset - reversedLineBuffer.Count;
        var welcome = TryCreateWelcomeEvent(
            CollectionsMarshal.AsSpan(reversedLineBuffer),
            maximumLineBytes,
            contextId,
            sourceId,
            sourceSegmentId,
            bindingGeneration,
            transitionKind,
            observedAt,
            lineStartOffset,
            lineEndOffset,
            ref nextSequence,
            out var welcomeEvent);
        reversedLineBuffer.Clear();
        return welcome ? welcomeEvent : null;
    }

    internal static ParserRawEvent? FindLastWelcome(
        ReadOnlySpan<byte> content,
        long contentStartOffset,
        MonitoringContextId contextId,
        LogSourceId sourceId,
        ParserSourceSegmentId sourceSegmentId,
        long bindingGeneration,
        MonitoringSourceTransitionKind transitionKind,
        DateTimeOffset observedAt,
        int maximumLineBytes,
        ref long nextSequence)
    {
        if (content.IsEmpty)
        {
            return null;
        }

        ParserRawEvent? lastWelcome = null;
        var lineBuffer = new List<byte>();
        var lineTooLarge = false;
        var lineStartOffset = contentStartOffset;

        for (var index = 0; index < content.Length; index++)
        {
            var absoluteOffset = contentStartOffset + index;
            var value = content[index];
            if (value != (byte)'\n')
            {
                if (!lineTooLarge)
                {
                    if (lineBuffer.Count < maximumLineBytes)
                    {
                        lineBuffer.Add(value);
                    }
                    else
                    {
                        lineBuffer.Clear();
                        lineTooLarge = true;
                    }
                }

                continue;
            }

            if (TryCreateWelcomeEvent(
                    lineBuffer,
                    lineTooLarge,
                    contextId,
                    sourceId,
                    sourceSegmentId,
                    bindingGeneration,
                    transitionKind,
                    observedAt,
                    lineStartOffset,
                    absoluteOffset + 1,
                    ref nextSequence,
                    out var welcome))
            {
                lastWelcome = welcome;
            }

            lineBuffer.Clear();
            lineTooLarge = false;
            lineStartOffset = absoluteOffset + 1;
        }

        return lastWelcome;
    }

    private static bool TryCreateWelcomeEvent(
        ReadOnlySpan<byte> lineBytes,
        int maximumLineBytes,
        MonitoringContextId contextId,
        LogSourceId sourceId,
        ParserSourceSegmentId sourceSegmentId,
        long bindingGeneration,
        MonitoringSourceTransitionKind transitionKind,
        DateTimeOffset observedAt,
        long lineStartOffset,
        long lineEndOffset,
        ref long nextSequence,
        out ParserRawEvent welcomeEvent)
    {
        welcomeEvent = null!;
        if (lineBytes.Length > maximumLineBytes)
        {
            return false;
        }

        if (lineBytes.Length > 0 && lineBytes[^1] == (byte)'\r')
        {
            lineBytes = lineBytes[..^1];
        }

        if (lineBytes.IsEmpty)
        {
            return false;
        }

        string rawLine;
        try
        {
            rawLine = StrictUtf8.GetString(lineBytes);
        }
        catch (DecoderFallbackException)
        {
            return false;
        }

        return TryCreateWelcomeFromRawLine(
            rawLine,
            contextId,
            sourceId,
            sourceSegmentId,
            bindingGeneration,
            transitionKind,
            observedAt,
            lineStartOffset,
            lineEndOffset,
            ref nextSequence,
            out welcomeEvent);
    }

    private static bool TryCreateWelcomeEvent(
        List<byte> lineBuffer,
        bool lineTooLarge,
        MonitoringContextId contextId,
        LogSourceId sourceId,
        ParserSourceSegmentId sourceSegmentId,
        long bindingGeneration,
        MonitoringSourceTransitionKind transitionKind,
        DateTimeOffset observedAt,
        long lineStartOffset,
        long lineEndOffset,
        ref long nextSequence,
        out ParserRawEvent welcomeEvent)
    {
        welcomeEvent = null!;
        if (lineTooLarge)
        {
            return false;
        }

        if (lineBuffer.Count > 0 && lineBuffer[^1] == (byte)'\r')
        {
            lineBuffer.RemoveAt(lineBuffer.Count - 1);
        }

        if (lineBuffer.Count == 0)
        {
            return false;
        }

        string rawLine;
        try
        {
            rawLine = StrictUtf8.GetString(CollectionsMarshal.AsSpan(lineBuffer));
        }
        catch (DecoderFallbackException)
        {
            return false;
        }

        return TryCreateWelcomeFromRawLine(
            rawLine,
            contextId,
            sourceId,
            sourceSegmentId,
            bindingGeneration,
            transitionKind,
            observedAt,
            lineStartOffset,
            lineEndOffset,
            ref nextSequence,
            out welcomeEvent);
    }

    private static bool TryCreateWelcomeFromRawLine(
        string rawLine,
        MonitoringContextId contextId,
        LogSourceId sourceId,
        ParserSourceSegmentId sourceSegmentId,
        long bindingGeneration,
        MonitoringSourceTransitionKind transitionKind,
        DateTimeOffset observedAt,
        long lineStartOffset,
        long lineEndOffset,
        ref long nextSequence,
        out ParserRawEvent welcomeEvent)
    {
        var rawEvent = new ParserRawEvent
        {
            ContextId = contextId,
            SourceId = sourceId,
            SourceSegmentId = sourceSegmentId,
            BindingGeneration = bindingGeneration,
            SourceTransitionKind = transitionKind,
            Sequence = 0,
            ObservedAt = observedAt,
            RawLine = rawLine,
            SourceByteStart = lineStartOffset,
            SourceByteEnd = lineEndOffset,
            LineStatus = ParserLineStatus.Complete
        };

        var classified = Classifier.Classify(rawEvent);
        if (!CharacterIdentityResolver.IsWelcomeEvidence(classified))
        {
            welcomeEvent = null!;
            return false;
        }

        nextSequence++;
        welcomeEvent = rawEvent with { Sequence = nextSequence };
        return true;
    }
}
