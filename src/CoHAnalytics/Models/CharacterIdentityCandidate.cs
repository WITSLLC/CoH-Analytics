namespace CoHAnalytics.Models;

/// <summary>One first-use candidate name retained for future manual resolution.</summary>
public sealed record CharacterIdentityCandidate
{
    public required string DisplayName { get; init; }

    public required string NormalizedName { get; init; }

    public required DateTimeOffset FirstObservedAt { get; init; }

    public required DateTimeOffset LastObservedAt { get; init; }

    public required int ObservationCount { get; init; }
}
