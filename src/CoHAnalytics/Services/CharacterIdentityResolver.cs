using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

/// <summary>
/// Evaluates structural parser evidence for account-scoped identity without owning repository
/// trust or gameplay-session lifecycle.
/// </summary>
internal static class CharacterIdentityResolver
{
    public static bool IsWelcomeEvidence(ParserEvent parserEvent) =>
        parserEvent.EventKind == ParserEventKind.PotentialIdentityEvidence
        && parserEvent.StructuralEvidence is { EvidenceKind: ParserStructuralEvidenceKind.WelcomeAttribution } evidence
        && evidence.AttributionStrength == ParserAttributionStrength.Strong
        && CharacterNameNormalizer.IsValidDisplayName(evidence.CandidateName);

    /// <summary>
    /// <see cref="ParserStructuralEvidenceKind.SystemAttributedAction"/> names actors affecting the
    /// local player, not the local character. It is retained as structural evidence for future
    /// grammar but is not local-character identity evidence (Slice 7D).
    /// </summary>
    public static bool IsStrongAttributedEvidence(ParserEvent parserEvent) => false;

    public static string? GetStrongCandidateName(ParserEvent parserEvent) =>
        IsWelcomeEvidence(parserEvent)
            ? parserEvent.StructuralEvidence!.CandidateName
            : null;

    public static CharacterRecord? TryInferKnownCharacter(
        ICharacterRepository repository,
        string accountStableId,
        string candidateDisplayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountStableId);
        ArgumentException.ThrowIfNullOrWhiteSpace(candidateDisplayName);

        return repository.TryFindTrustedByDisplayName(accountStableId, candidateDisplayName);
    }

    public static bool NamesConflict(string normalizedLeft, string normalizedRight) =>
        !CharacterNameNormalizer.NamesMatch(normalizedLeft, normalizedRight);

    public static string NormalizeName(string displayName) => CharacterNameNormalizer.Normalize(displayName);
}
