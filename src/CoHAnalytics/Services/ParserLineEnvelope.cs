using System.Globalization;
using System.Text.RegularExpressions;

namespace CoHAnalytics.Services;

/// <summary>
/// Optional game-timestamp envelope kinds recognized at the start of a chat-log line.
/// </summary>
internal enum ParserLineEnvelopeKind
{
    Untimestamped,
    FullTimestamp,
    BracketTimestamp,
    MalformedTimestamp
}

/// <summary>
/// Shared Homecoming chat-log line envelope parsing for structural and semantic parsers.
/// Semantic parsers should use <see cref="TryGetBody"/> rather than stripping timestamps locally.
/// Game timestamps are optional source metadata; absence is not malformed.
/// Live ordering and session timing use application observation time (<see cref="Models.ParserEvent.ObservedAt"/>).
/// </summary>
internal static partial class ParserLineEnvelope
{
    public const int FullTimestampLength = 19;

    public static bool TryResolve(
        string rawLine,
        DateOnly logDate,
        out ParserLineEnvelopeKind envelopeKind,
        out DateTime? sourceTimestamp,
        out int bodyStart)
    {
        envelopeKind = ParserLineEnvelopeKind.Untimestamped;
        sourceTimestamp = null;
        bodyStart = 0;

        if (TryReadFullTimestamp(rawLine, out var fullTimestamp, out bodyStart))
        {
            envelopeKind = ParserLineEnvelopeKind.FullTimestamp;
            sourceTimestamp = fullTimestamp;
            return true;
        }

        if (TryReadBracketTimestamp(rawLine, logDate, out var bracketTimestamp, out bodyStart))
        {
            envelopeKind = ParserLineEnvelopeKind.BracketTimestamp;
            sourceTimestamp = bracketTimestamp;
            return true;
        }

        if (LooksLikeBracketTimestamp(rawLine))
        {
            envelopeKind = ParserLineEnvelopeKind.MalformedTimestamp;
            return false;
        }

        if (rawLine.Length >= FullTimestampLength
            && FullTimestampShape().IsMatch(rawLine[..FullTimestampLength]))
        {
            envelopeKind = ParserLineEnvelopeKind.MalformedTimestamp;
            return false;
        }

        envelopeKind = ParserLineEnvelopeKind.Untimestamped;
        return true;
    }

    /// <summary>
    /// Returns the semantic message body after any supported optional game-timestamp envelope.
    /// </summary>
    public static bool TryGetBody(string rawLine, DateOnly logDate, out string body)
    {
        if (!TryResolve(rawLine, logDate, out _, out _, out var bodyStart))
        {
            body = string.Empty;
            return false;
        }

        body = rawLine[bodyStart..];
        return true;
    }

    private static bool TryReadFullTimestamp(
        string rawLine,
        out DateTime timestamp,
        out int bodyStart)
    {
        timestamp = default;
        bodyStart = 0;

        if (rawLine.Length < FullTimestampLength
            || !FullTimestampShape().IsMatch(rawLine[..FullTimestampLength])
            || rawLine.Length <= FullTimestampLength
            || rawLine[FullTimestampLength] != ' ')
        {
            return false;
        }

        if (!DateTime.TryParseExact(
                rawLine.AsSpan(0, FullTimestampLength),
                "yyyy-MM-dd HH:mm:ss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out timestamp))
        {
            return false;
        }

        timestamp = DateTime.SpecifyKind(timestamp, DateTimeKind.Unspecified);
        bodyStart = FullTimestampLength + 1;
        return true;
    }

    private static bool TryReadBracketTimestamp(
        string rawLine,
        DateOnly logDate,
        out DateTime timestamp,
        out int bodyStart)
    {
        timestamp = default;
        bodyStart = 0;

        var match = BracketTimestampPrefix().Match(rawLine);
        if (!match.Success)
        {
            return false;
        }

        var separatorIndex = match.Length;
        if (separatorIndex >= rawLine.Length || rawLine[separatorIndex] != ' ')
        {
            return false;
        }

        if (!int.TryParse(match.Groups["hour"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var hour)
            || !int.TryParse(match.Groups["minute"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var minute)
            || hour is < 0 or > 23
            || minute is < 0 or > 59)
        {
            return false;
        }

        timestamp = new DateTime(
            logDate.Year,
            logDate.Month,
            logDate.Day,
            hour,
            minute,
            0,
            DateTimeKind.Unspecified);
        bodyStart = separatorIndex + 1;
        return true;
    }

    private static bool LooksLikeBracketTimestamp(string rawLine)
    {
        if (!rawLine.StartsWith('['))
        {
            return false;
        }

        var closeIndex = rawLine.IndexOf(']');
        if (closeIndex is <= 0 or > 8)
        {
            return false;
        }

        if (closeIndex + 1 >= rawLine.Length || rawLine[closeIndex + 1] != ' ')
        {
            return false;
        }

        var inner = rawLine[1..closeIndex];
        var colonIndex = inner.IndexOf(':');
        return colonIndex > 0
            && colonIndex == inner.LastIndexOf(':')
            && inner[..colonIndex].All(char.IsDigit)
            && inner[(colonIndex + 1)..].All(char.IsDigit);
    }

    [GeneratedRegex(@"^\[(?<hour>\d{1,2}):(?<minute>\d{2})\]", RegexOptions.CultureInvariant)]
    private static partial Regex BracketTimestampPrefix();

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}$", RegexOptions.CultureInvariant)]
    private static partial Regex FullTimestampShape();
}
