namespace CoHAnalytics.Models;

/// <summary>Runtime identity workflow state for one gameplay session.</summary>
public sealed record CharacterIdentitySnapshot
{
    public CharacterRecordId? CharacterRecordId { get; init; }

    public string? CharacterDisplayName { get; init; }

    public required CharacterIdentityConfidence Confidence { get; init; }

    public required CharacterIdentityResolutionState Resolution { get; init; }

    public IReadOnlyList<CharacterIdentityCandidate> Candidates { get; init; } = [];
}
