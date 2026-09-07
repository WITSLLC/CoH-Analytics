namespace CoHAnalytics.Models;

/// <summary>
/// Canonical gameplay observation that a badge award line was parsed from the chat log.
/// </summary>
public sealed record BadgeAcquiredEvent
{
    public required string ObservedBadgeTitle { get; init; }

    public string? ResolvedCatalogItemId { get; init; }

    public required AcquisitionIdentityResolutionState ResolutionState { get; init; }

    public string? CatalogVersion { get; init; }

    public string? MatchedAliasText { get; init; }
}
