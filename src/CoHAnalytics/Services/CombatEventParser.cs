using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

/// <summary>
/// Semantic combat telemetry parser. Structural classification remains owned by <see cref="ParserClassifier"/>.
/// Grammar matching is owned by <see cref="GrammarMatcher"/>; semantic conversion by <see cref="Normalizer"/>.
/// </summary>
public sealed class CombatEventParser : ICombatEventParser
{
    public bool TryParse(ParserEvent parserEvent, out CombatEvent combatEvent)
    {
        combatEvent = null!;

        if (parserEvent.LineStatus != ParserLineStatus.Complete
            || parserEvent.EventKind is ParserEventKind.Malformed)
        {
            return false;
        }

        if (!ParserLineEnvelope.TryGetBody(parserEvent.RawLine, parserEvent.SourceId.LogDate, out var body))
        {
            return false;
        }

        if (!IsCombatCandidate(parserEvent.EventKind, body))
        {
            return false;
        }

        if (!GrammarMatcher.TryMatch(body, out var match))
        {
            return false;
        }

        return Normalizer.TryNormalize(match, parserEvent, out combatEvent);
    }

    /// <summary>
    /// Returns true when the structural classifier would treat the body as combat-shaped
    /// but this parser does not yet emit a semantic <see cref="CombatEvent"/>.
    /// </summary>
    public static bool IsCombatShapedUnparsed(ParserEvent parserEvent)
    {
        if (parserEvent.LineStatus != ParserLineStatus.Complete
            || parserEvent.EventKind is ParserEventKind.Malformed)
        {
            return false;
        }

        if (!ParserLineEnvelope.TryGetBody(parserEvent.RawLine, parserEvent.SourceId.LogDate, out var body))
        {
            return false;
        }

        return IsCombatCandidate(parserEvent.EventKind, body)
            && !new CombatEventParser().TryParse(parserEvent, out _);
    }

    internal static bool IsCombatCandidate(ParserEventKind eventKind, string body) =>
        eventKind is ParserEventKind.SystemLine or ParserEventKind.Unknown or ParserEventKind.TimestampedLine
        && (body.StartsWith("You hit ", StringComparison.Ordinal)
            || body.StartsWith("You heal ", StringComparison.Ordinal)
            || body.StartsWith("You activate", StringComparison.Ordinal)
            || body.StartsWith("You activated ", StringComparison.Ordinal)
            || body.StartsWith("You have defeated ", StringComparison.Ordinal)
            || body.Contains(" has defeated ", StringComparison.Ordinal)
            || body.Contains(" hits you", StringComparison.Ordinal)
            || body.Contains(" heals you ", StringComparison.Ordinal)
            || body.StartsWith("HIT ", StringComparison.OrdinalIgnoreCase)
            || body.StartsWith("MISSED ", StringComparison.OrdinalIgnoreCase)
            || body.StartsWith("You take ", StringComparison.Ordinal));
}
