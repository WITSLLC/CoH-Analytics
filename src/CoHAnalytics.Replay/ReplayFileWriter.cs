using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace CoHAnalytics.Replay;

public sealed class ReplayFileWriter
{
    private static readonly Regex TimestampPattern = new(
        @"^(?<timestamp>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2})",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly TimeProvider _timeProvider;
    private readonly ReplayLedger _ledger;
    private readonly ReplayTimeline _timeline;

    public ReplayFileWriter(TimeProvider timeProvider, ReplayLedger ledger, ReplayTimeline timeline)
    {
        _timeProvider = timeProvider;
        _ledger = ledger;
        _timeline = timeline;
    }

    public async Task ExecuteAsync(
        ReplayPlan plan,
        ReplayWorkspace workspace,
        CancellationToken cancellationToken = default,
        Func<int, Task>? betweenSegmentsAsync = null,
        ReplayLatencyTracker? latencyTracker = null,
        bool validateOnCompletion = true,
        Func<CancellationToken, Task<bool>>? shouldContinueAfterChunkAsync = null)
    {
        var bootstrapWritten = false;
        var timingState = new ReplayTimingState(_timeProvider);
        var burstItemsRemaining = 0;
        var burstsCompleted = 0;
        var destinationIncompleteLine = false;
        var stopWriting = false;

        for (var segmentIndex = 0; segmentIndex < plan.Segments.Count; segmentIndex++)
        {
            var segment = plan.Segments[segmentIndex];
            if (segmentIndex > 0)
            {
                _ledger.RecordRollover();
            }

            _timeline.Record(
                ReplayTimelineCategory.Replay,
                "replay.segment.started",
                ReplayTimelineRetentionClass.Milestone,
                segment.Ordinal);

            var destinationPath = workspace.ResolveDestinationLogPath(segment.DestinationDate);
            await using var destination = new FileStream(
                destinationPath,
                FileMode.Append,
                FileAccess.Write,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 4096,
                FileOptions.Asynchronous);

            if (plan.InputMode == ReplayInputMode.Bootstrap && !bootstrapWritten)
            {
                _timeline.Record(
                    ReplayTimelineCategory.Replay,
                    "bootstrap.started",
                    ReplayTimelineRetentionClass.Milestone);

                await WriteChunkAsync(destination, ReplayPlan.BootstrapBytes, cancellationToken)
                    .ConfigureAwait(false);

                var bootstrapLines = ReplayLineAccounting.Analyze(ReplayPlan.BootstrapBytes);
                _ledger.RecordBootstrap(segment.Ordinal, ReplayPlan.BootstrapBytes.Length, bootstrapLines.CompleteLines);
                bootstrapWritten = true;

                _timeline.Record(
                    ReplayTimelineCategory.Replay,
                    "bootstrap.completed",
                    ReplayTimelineRetentionClass.Milestone);
            }

            await using var source = new FileStream(
                segment.SourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 4096,
                FileOptions.Asynchronous);

            var welcomeDetector = new WelcomeBoundaryDetector();
            var segmentAnalysis = await StreamSegmentAsync(
                plan,
                source,
                segment.Ordinal,
                async chunk =>
                {
                    welcomeDetector.Process(chunk);

                    cancellationToken.ThrowIfCancellationRequested();
                    _ledger.RecordPlannedChunk(segment.Ordinal);

                    if (plan.TimingMode == ReplayTimingMode.Burst)
                    {
                        if (burstItemsRemaining == 0)
                        {
                            _timeline.Record(
                                ReplayTimelineCategory.Replay,
                                "replay.burst.started",
                                ReplayTimelineRetentionClass.Milestone,
                                segment.Ordinal);
                            _timeline.Record(
                                ReplayTimelineCategory.Analytics,
                                "burst.started",
                                ReplayTimelineRetentionClass.Milestone,
                                burstsCompleted + 1);
                            burstItemsRemaining = plan.BurstSize;
                        }
                    }

                    await ApplyTimingDelayAsync(
                        plan,
                        chunk,
                        timingState,
                        cancellationToken).ConfigureAwait(false);

                    await WriteChunkAsync(destination, chunk, cancellationToken).ConfigureAwait(false);
                    ObserveLatencyMarker(chunk, latencyTracker);
                    var lineDelta = ReplayLineAccounting.CountLinesInAppend(chunk, destinationIncompleteLine);
                    destinationIncompleteLine = lineDelta.EndsWithIncompleteLine;
                    _ledger.RecordAppendedChunk(
                        segment.Ordinal,
                        chunk.Length,
                        lineDelta.CompleteLinesAdded,
                        destinationIncompleteLine);

                    if (plan.TimingMode == ReplayTimingMode.Burst)
                    {
                        burstItemsRemaining--;
                        if (burstItemsRemaining == 0)
                        {
                            burstsCompleted++;
                            _ledger.RecordBurst();
                            _timeline.Record(
                                ReplayTimelineCategory.Replay,
                                "replay.burst.completed",
                                ReplayTimelineRetentionClass.Milestone,
                                segment.Ordinal);
                            _timeline.Record(
                                ReplayTimelineCategory.Analytics,
                                "burst.completed",
                                ReplayTimelineRetentionClass.Milestone,
                                burstsCompleted);
                            if (plan.BurstQuietInterval > TimeSpan.Zero)
                            {
                                await Task.Delay(plan.BurstQuietInterval, _timeProvider, cancellationToken)
                                    .ConfigureAwait(false);
                            }
                        }
                    }

                    if (shouldContinueAfterChunkAsync is not null
                        && !await shouldContinueAfterChunkAsync(cancellationToken).ConfigureAwait(false))
                    {
                        stopWriting = true;
                    }
                },
                cancellationToken,
                () => stopWriting).ConfigureAwait(false);

            if (stopWriting)
            {
                break;
            }

            _ledger.RecordSourceAnalysis(
                segment.Ordinal,
                segmentAnalysis.SourceBytes,
                segmentAnalysis.CompleteLines,
                segmentAnalysis.HasIncompleteFinalFragment,
                segmentAnalysis.IncompleteBytes);

            if (segment.Ordinal == 1 && !welcomeDetector.SawWelcome)
            {
                _ledger.MarkBeginsMidSession();
            }

            await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
            _ledger.RecordFlush();

            _timeline.Record(
                ReplayTimelineCategory.Replay,
                "replay.segment.completed",
                ReplayTimelineRetentionClass.Milestone,
                segment.Ordinal);

            if (segmentIndex < plan.Segments.Count - 1 && betweenSegmentsAsync is not null)
            {
                await betweenSegmentsAsync(segment.Ordinal).ConfigureAwait(false);
            }
        }

        _timeline.Record(
            ReplayTimelineCategory.Replay,
            "replay.source.completed",
            ReplayTimelineRetentionClass.Milestone);

        timingState.CompleteWrite();
        var writeDuration = timingState.WriteDuration;
        var achievedRate = writeDuration.TotalSeconds > 0
            ? _ledger.OriginalSourceCompleteLines / writeDuration.TotalSeconds
            : (double?)null;
        _ledger.SetPacing(new ReplayPacingSummary
        {
            RequestedLinesPerSecond = plan.RequestedLinesPerSecond ?? (plan.TimingMode == ReplayTimingMode.FixedLines
                ? plan.LinesPerSecond
                : null),
            AchievedLinesPerSecond = achievedRate,
            UnderrunCount = timingState.UnderrunCount,
            OverrunMilliseconds = timingState.OverrunMilliseconds
        });

        if (validateOnCompletion)
        {
            _ledger.Validate(plan);
        }
    }

    internal static IReadOnlyList<byte[]> PlanChunks(ReplayPlan plan, byte[] sourceBytes) =>
        plan.ChunkMode switch
        {
            ReplayChunkMode.WholeLine => PlanWholeLineChunks(sourceBytes),
            ReplayChunkMode.FixedBytes => PlanFixedByteChunks(sourceBytes, plan.FixedChunkBytes),
            ReplayChunkMode.SeededVariable => PlanSeededVariableChunks(
                sourceBytes,
                plan.MinChunkBytes,
                plan.MaxChunkBytes,
                plan.Seed),
            ReplayChunkMode.OneByte => PlanFixedByteChunks(sourceBytes, 1),
            _ => throw new ReplayConfigurationException($"Unsupported chunk mode '{plan.ChunkMode}'.")
        };

    internal static async Task<byte[]> ReadSourceForTestsAsync(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 4096,
            FileOptions.Asynchronous);
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, cancellationToken).ConfigureAwait(false);
        return memory.ToArray();
    }

    private sealed record SegmentAnalysis(
        long SourceBytes,
        long CompleteLines,
        bool HasIncompleteFinalFragment,
        long IncompleteBytes);

    private static async Task<SegmentAnalysis> StreamSegmentAsync(
        ReplayPlan plan,
        FileStream source,
        int segmentOrdinal,
        Func<byte[], Task> onChunk,
        CancellationToken cancellationToken,
        Func<bool>? shouldStop = null)
    {
        long sourceBytes = 0;
        long completeLines = 0;
        long bytesSinceLastNewline = 0;

        await foreach (var chunk in EnumerateRawChunksAsync(plan, source, cancellationToken).ConfigureAwait(false))
        {
            if (shouldStop?.Invoke() == true)
            {
                break;
            }

            sourceBytes += chunk.Length;
            foreach (var value in chunk)
            {
                if (value == (byte)'\n')
                {
                    completeLines++;
                    bytesSinceLastNewline = 0;
                }
                else
                {
                    bytesSinceLastNewline++;
                }
            }

            await onChunk(chunk).ConfigureAwait(false);
        }

        var hasIncomplete = bytesSinceLastNewline > 0;
        _ = segmentOrdinal;
        return new SegmentAnalysis(sourceBytes, completeLines, hasIncomplete, bytesSinceLastNewline);
    }

    private static async IAsyncEnumerable<byte[]> EnumerateRawChunksAsync(
        ReplayPlan plan,
        FileStream source,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        switch (plan.ChunkMode)
        {
            case ReplayChunkMode.WholeLine:
                await foreach (var chunk in EnumerateWholeLineChunksAsync(source, cancellationToken).ConfigureAwait(false))
                {
                    yield return chunk;
                }

                yield break;

            case ReplayChunkMode.FixedBytes:
                await foreach (var chunk in EnumerateFixedByteChunksAsync(source, plan.FixedChunkBytes, cancellationToken)
                                   .ConfigureAwait(false))
                {
                    yield return chunk;
                }

                yield break;

            case ReplayChunkMode.OneByte:
                await foreach (var chunk in EnumerateFixedByteChunksAsync(source, 1, cancellationToken).ConfigureAwait(false))
                {
                    yield return chunk;
                }

                yield break;

            case ReplayChunkMode.SeededVariable:
                await foreach (var chunk in EnumerateSeededVariableChunksAsync(
                                   source,
                                   plan.MinChunkBytes,
                                   plan.MaxChunkBytes,
                                   plan.Seed,
                                   cancellationToken).ConfigureAwait(false))
                {
                    yield return chunk;
                }

                yield break;

            default:
                throw new ReplayConfigurationException($"Unsupported chunk mode '{plan.ChunkMode}'.");
        }
    }

    private static async IAsyncEnumerable<byte[]> EnumerateWholeLineChunksAsync(
        FileStream source,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var buffer = new List<byte>();
        var readBuffer = new byte[1024];
        while (true)
        {
            var read = await source.ReadAsync(readBuffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                if (buffer.Count > 0)
                {
                    yield return buffer.ToArray();
                }

                yield break;
            }

            for (var index = 0; index < read; index++)
            {
                buffer.Add(readBuffer[index]);
                if (readBuffer[index] != (byte)'\n')
                {
                    continue;
                }

                yield return buffer.ToArray();
                buffer.Clear();
            }
        }
    }

    private static async IAsyncEnumerable<byte[]> EnumerateFixedByteChunksAsync(
        FileStream source,
        int chunkSize,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var buffer = new byte[chunkSize];
        while (true)
        {
            var offset = 0;
            while (offset < chunkSize)
            {
                var read = await source.ReadAsync(buffer.AsMemory(offset, chunkSize - offset), cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    if (offset > 0)
                    {
                        var tail = new byte[offset];
                        Buffer.BlockCopy(buffer, 0, tail, 0, offset);
                        yield return tail;
                    }

                    yield break;
                }

                offset += read;
            }

            var chunk = new byte[chunkSize];
            Buffer.BlockCopy(buffer, 0, chunk, 0, chunkSize);
            yield return chunk;
        }
    }

    private static async IAsyncEnumerable<byte[]> EnumerateSeededVariableChunksAsync(
        FileStream source,
        int minChunkBytes,
        int maxChunkBytes,
        int seed,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var random = new Random(seed);
        var buffer = new byte[maxChunkBytes];
        while (true)
        {
            var remaining = source.Length - source.Position;
            if (remaining <= 0)
            {
                yield break;
            }

            var desired = remaining == 1 ? 1 : random.Next(minChunkBytes, maxChunkBytes + 1);
            var length = (int)Math.Min(desired, remaining);
            var read = await source.ReadAsync(buffer.AsMemory(0, length), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                yield break;
            }

            var chunk = new byte[read];
            Buffer.BlockCopy(buffer, 0, chunk, 0, read);
            yield return chunk;
        }
    }

    private sealed class ReplayTimingState
    {
        private readonly TimeProvider _timeProvider;
        private long _writeStartTimestamp;
        private long _writeEndTimestamp;
        private long _linesCompleted;

        public ReplayTimingState(TimeProvider timeProvider)
        {
            _timeProvider = timeProvider;
            _writeStartTimestamp = timeProvider.GetTimestamp();
        }

        public DateTimeOffset? FirstSourceTimestamp { get; set; }

        public DateTimeOffset? PreviousSourceTimestamp { get; set; }

        public long UnderrunCount { get; set; }

        public double OverrunMilliseconds { get; set; }

        public TimeSpan WriteDuration =>
            _writeEndTimestamp > _writeStartTimestamp
                ? _timeProvider.GetElapsedTime(_writeStartTimestamp, _writeEndTimestamp)
                : _timeProvider.GetElapsedTime(_writeStartTimestamp);

        public void CompleteWrite() => _writeEndTimestamp = _timeProvider.GetTimestamp();

        public long IncrementLineCount() => ++_linesCompleted;

        public long LinesCompleted => _linesCompleted;

        public long WriteStartTimestamp => _writeStartTimestamp;
    }

    private async Task ApplyTimingDelayAsync(
        ReplayPlan plan,
        byte[] chunk,
        ReplayTimingState timingState,
        CancellationToken cancellationToken)
    {
        switch (plan.TimingMode)
        {
            case ReplayTimingMode.Maximum:
                return;

            case ReplayTimingMode.FixedLines:
                if (ChunkCompletesLine(chunk))
                {
                    var lineNumber = timingState.IncrementLineCount();
                    var targetElapsed = TimeSpan.FromSeconds(lineNumber / plan.LinesPerSecond);
                    var actualElapsed = _timeProvider.GetElapsedTime(timingState.WriteStartTimestamp);
                    if (actualElapsed > targetElapsed)
                    {
                        timingState.UnderrunCount++;
                    }
                    else
                    {
                        var delayNeeded = targetElapsed - actualElapsed;
                        if (delayNeeded > TimeSpan.Zero)
                        {
                            await Task.Delay(delayNeeded, _timeProvider, cancellationToken).ConfigureAwait(false);
                        }
                        else
                        {
                            timingState.OverrunMilliseconds += -delayNeeded.TotalMilliseconds;
                        }
                    }
                }

                return;

            case ReplayTimingMode.FixedBytes:
                var byteDelay = TimeSpan.FromSeconds(chunk.Length / plan.BytesPerSecond);
                if (byteDelay > TimeSpan.Zero)
                {
                    await Task.Delay(byteDelay, _timeProvider, cancellationToken).ConfigureAwait(false);
                }

                return;

            case ReplayTimingMode.Timestamp:
                if (!ChunkCompletesLine(chunk))
                {
                    return;
                }

                var lineText = Encoding.UTF8.GetString(chunk).TrimEnd('\r', '\n');
                if (lineText.Length == 0)
                {
                    return;
                }

                var timestamp = ParseRequiredLeadingTimestamp(chunk);
                if (timingState.FirstSourceTimestamp is null)
                {
                    timingState.FirstSourceTimestamp = timestamp;
                    timingState.PreviousSourceTimestamp = timestamp;
                    return;
                }

                if (timestamp < timingState.PreviousSourceTimestamp)
                {
                    throw new ReplayTimestampException(
                        $"Timestamp '{timestamp:O}' regressed before '{timingState.PreviousSourceTimestamp:O}'.");
                }

                var delta = timestamp - timingState.PreviousSourceTimestamp!.Value;
                timingState.PreviousSourceTimestamp = timestamp;
                if (delta <= TimeSpan.Zero)
                {
                    return;
                }

                var scaled = TimeSpan.FromTicks((long)(delta.Ticks / plan.TimestampSpeedMultiplier));
                await Task.Delay(scaled, _timeProvider, cancellationToken).ConfigureAwait(false);
                return;

            case ReplayTimingMode.Burst:
                return;

            default:
                throw new ReplayConfigurationException($"Unsupported timing mode '{plan.TimingMode}'.");
        }
    }

    private static void ObserveLatencyMarker(byte[] chunk, ReplayLatencyTracker? latencyTracker)
    {
        if (latencyTracker is null || !ChunkCompletesLine(chunk))
        {
            return;
        }

        var line = Encoding.UTF8.GetString(chunk).TrimEnd('\r', '\n');
        if (ReplayLatencyTracker.TryParseMarkerId(line, out var markerId))
        {
            latencyTracker.RecordMarkerWritten(markerId);
        }
    }

    private static async Task WriteChunkAsync(
        FileStream destination,
        byte[] chunk,
        CancellationToken cancellationToken) =>
        await destination.WriteAsync(chunk.AsMemory(), cancellationToken).ConfigureAwait(false);

    private static IReadOnlyList<byte[]> PlanWholeLineChunks(byte[] sourceBytes)
    {
        var chunks = new List<byte[]>();
        var index = 0;
        while (index < sourceBytes.Length)
        {
            var start = index;
            while (index < sourceBytes.Length && sourceBytes[index] != (byte)'\n')
            {
                index++;
            }

            if (index < sourceBytes.Length)
            {
                index++;
                chunks.Add(sourceBytes[start..index]);
                continue;
            }

            if (start < sourceBytes.Length)
            {
                chunks.Add(sourceBytes[start..]);
            }

            break;
        }

        return chunks;
    }

    private static IReadOnlyList<byte[]> PlanFixedByteChunks(byte[] sourceBytes, int chunkSize)
    {
        var chunks = new List<byte[]>();
        for (var index = 0; index < sourceBytes.Length; index += chunkSize)
        {
            var length = Math.Min(chunkSize, sourceBytes.Length - index);
            chunks.Add(sourceBytes[index..(index + length)]);
        }

        return chunks;
    }

    private static IReadOnlyList<byte[]> PlanSeededVariableChunks(
        byte[] sourceBytes,
        int minChunkBytes,
        int maxChunkBytes,
        int seed)
    {
        var chunks = new List<byte[]>();
        var random = new Random(seed);
        var index = 0;
        while (index < sourceBytes.Length)
        {
            var remaining = sourceBytes.Length - index;
            var desired = remaining == 1 ? 1 : random.Next(minChunkBytes, maxChunkBytes + 1);
            var length = Math.Min(desired, remaining);
            chunks.Add(sourceBytes[index..(index + length)]);
            index += length;
        }

        return chunks;
    }

    private static bool ChunkCompletesLine(byte[] chunk) =>
        chunk.Length > 0 && chunk[^1] == (byte)'\n';

    private static DateTimeOffset ParseRequiredLeadingTimestamp(byte[] chunk)
    {
        var text = Encoding.UTF8.GetString(chunk);
        var firstLineEnd = text.IndexOf('\n');
        var firstLine = (firstLineEnd >= 0 ? text[..firstLineEnd] : text).TrimEnd('\r', '\n');
        var match = TimestampPattern.Match(firstLine);
        if (!match.Success)
        {
            throw new ReplayTimestampException("A source line is missing a required leading timestamp.");
        }

        if (!DateTimeOffset.TryParseExact(
                match.Groups["timestamp"].Value,
                "yyyy-MM-dd HH:mm:ss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out var parsed))
        {
            throw new ReplayTimestampException(
                $"Timestamp '{match.Groups["timestamp"].Value}' is invalid.");
        }

        return parsed;
    }
}

public sealed class ReplayTimestampException : Exception
{
    public ReplayTimestampException(string message) : base(message)
    {
    }
}

internal sealed class WelcomeBoundaryDetector
{
    private static readonly byte[] Pattern = "Welcome to City of Heroes"u8.ToArray();
    private static readonly int[] Failure = BuildFailureTable(Pattern);
    private int _matchedPrefixLength;

    public bool SawWelcome { get; private set; }

    public void Process(ReadOnlySpan<byte> chunk)
    {
        if (SawWelcome)
        {
            return;
        }

        foreach (var value in chunk)
        {
            while (_matchedPrefixLength > 0 && value != Pattern[_matchedPrefixLength])
            {
                _matchedPrefixLength = Failure[_matchedPrefixLength - 1];
            }

            if (value == Pattern[_matchedPrefixLength])
            {
                _matchedPrefixLength++;
                if (_matchedPrefixLength == Pattern.Length)
                {
                    SawWelcome = true;
                    return;
                }
            }
        }
    }

    private static int[] BuildFailureTable(ReadOnlySpan<byte> pattern)
    {
        var failure = new int[pattern.Length];
        for (var index = 1; index < pattern.Length; index++)
        {
            var candidate = failure[index - 1];
            while (candidate > 0 && pattern[index] != pattern[candidate])
            {
                candidate = failure[candidate - 1];
            }

            if (pattern[index] == pattern[candidate])
            {
                candidate++;
            }

            failure[index] = candidate;
        }

        return failure;
    }
}
