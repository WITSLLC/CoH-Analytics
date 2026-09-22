using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

/// <summary>
/// Semantic combat telemetry parser. Structural classification remains owned by <see cref="ParserClassifier"/>.
/// Grammar matching is owned by <see cref="GrammarMatcher"/>; semantic conversion by <see cref="Normalizer"/>.
/// Canonical events are adapted to legacy <see cref="CombatEvent"/> only when a truthful mapping exists.
/// </summary>
public sealed class CombatEventParser : ICombatEventParser
{
    public bool TryParse(ParserEvent parserEvent, out CombatEvent combatEvent)
    {
        combatEvent = null!;
        return TryParseCanonical(parserEvent, out var canonicalEvent)
            && TryAdaptToLegacy(canonicalEvent, out combatEvent);
    }

    public bool TryAdaptToLegacy(CanonicalCombatEvent canonicalEvent, out CombatEvent combatEvent)
    {
        combatEvent = null!;
        if (IsPetScoped(canonicalEvent) || IsIncomingResolution(canonicalEvent.GrammarId))
        {
            return false;
        }

        return CanonicalToLegacyAdapter.TryToLegacy(canonicalEvent, out combatEvent);
    }

    public bool TryParseCanonical(ParserEvent parserEvent, out CanonicalCombatEvent canonicalEvent)
    {
        canonicalEvent = null!;

        if (!TryGetCompleteBody(parserEvent, out var body)
            || !IsCanonicalCombatCandidate(parserEvent.EventKind, body))
        {
            return false;
        }

        foreach (var match in GrammarMatcher.EnumerateMatches(body))
        {
            if (Normalizer.TryNormalize(match, parserEvent, out canonicalEvent))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns true when the structural classifier would treat the body as combat-shaped
    /// but this parser does not yet emit a semantic <see cref="CombatEvent"/>.
    /// </summary>
    public static bool IsCombatShapedUnparsed(ParserEvent parserEvent)
    {
        if (!TryGetCompleteBody(parserEvent, out var body))
        {
            return false;
        }

        return IsCombatCandidate(parserEvent.EventKind, body)
            && !new CombatEventParser().TryParse(parserEvent, out _);
    }

    internal static bool IsCombatShapedUnparsed(ParserEvent parserEvent, bool legacyParsed)
    {
        if (!TryGetCompleteBody(parserEvent, out var body))
        {
            return false;
        }

        return IsCombatCandidate(parserEvent.EventKind, body) && !legacyParsed;
    }

    /// <summary>
    /// Combat-shaped canonical candidate with no <see cref="TryParseCanonical"/> result.
    /// Distinct from <see cref="IsCombatShapedUnparsed"/>, which uses legacy <see cref="CombatEvent"/> mapping.
    /// Pet-prefixed bodies are canonical candidates after prefix strip and are not legacy combat-shaped
    /// on the unstripped line.
    /// </summary>
    public static bool IsCanonicalCombatUnparsed(ParserEvent parserEvent)
    {
        var parsed = new CombatEventParser().TryParseCanonical(parserEvent, out _);
        return IsCanonicalCombatUnparsed(parserEvent, parsed);
    }

    internal static bool IsCanonicalCombatUnparsed(ParserEvent parserEvent, bool canonicalParsed) =>
        !canonicalParsed
        && TryGetCompleteBody(parserEvent, out var body)
        && IsCanonicalCombatCandidate(parserEvent.EventKind, body);

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

    internal static bool IsCanonicalCombatCandidate(ParserEventKind eventKind, string body)
    {
        if (PetCombatPrefix.TryStrip(body, out _, out var inner))
        {
            return IsCanonicalInnerCandidate(eventKind, inner);
        }

        return IsCanonicalInnerCandidate(eventKind, body);
    }

    private static bool IsCanonicalInnerCandidate(ParserEventKind eventKind, string body)
    {
        if (eventKind is not (ParserEventKind.SystemLine
            or ParserEventKind.Unknown
            or ParserEventKind.TimestampedLine
            or ParserEventKind.PotentialIdentityEvidence))
        {
            return false;
        }

        if (GrammarMatcher.TryMatch(body, out _))
        {
            return true;
        }

        // Known canonical-domain misses retain the stable grammar frame while failing a
        // current grammar/normalizer detail. Ordinary prose sharing one leading verb does not.
        if (body.StartsWith("You take ", StringComparison.Ordinal)
            && body.Contains(" points of ", StringComparison.Ordinal)
            && body.Contains(" damage from ", StringComparison.Ordinal))
        {
            return true;
        }

        if (body.StartsWith("You hit ", StringComparison.Ordinal)
            && HasAmountFrame(body, " points of ", " damage"))
        {
            return true;
        }

        if (body.StartsWith("You heal ", StringComparison.Ordinal)
            && HasAmountFrame(body, " health point", string.Empty))
        {
            return true;
        }

        if ((body.Contains(" hits you", StringComparison.Ordinal)
                || body.Contains(" heals you ", StringComparison.Ordinal))
            && body.Contains(" with ", StringComparison.Ordinal)
            && body.Contains(" for ", StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }

    private static bool HasAmountFrame(string body, string requiredAfterAmount, string requiredTail) =>
        body.Contains(" for ", StringComparison.Ordinal)
        && body.Contains(requiredAfterAmount, StringComparison.Ordinal)
        && (requiredTail.Length == 0 || body.Contains(requiredTail, StringComparison.Ordinal));

    private static bool IsPetScoped(CanonicalCombatEvent canonical) =>
        canonical.Actor.Type is ActorType.OwnPet or ActorType.OtherPet
        || canonical.Target?.Type is ActorType.OwnPet or ActorType.OtherPet;

    private static bool IsIncomingResolution(CombatGrammarId grammarId) =>
        grammarId is CombatGrammarId.Acc05SourceHitsYouRolled
            or CombatGrammarId.Acc06SourceHitsYouAutohit;

    private static bool TryGetCompleteBody(ParserEvent parserEvent, out string body)
    {
        body = null!;
        if (parserEvent.LineStatus != ParserLineStatus.Complete
            || parserEvent.EventKind is ParserEventKind.Malformed)
        {
            return false;
        }

        return ParserLineEnvelope.TryGetBody(parserEvent.RawLine, parserEvent.SourceId.LogDate, out body);
    }
}
