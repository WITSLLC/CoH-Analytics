namespace CoHAnalytics.Models;

/// <summary>One durable badge completion observed for a character.</summary>
public sealed record CharacterBadgeAcquisitionEntry
{
    public required string CatalogItemId { get; init; }

    public required DateTimeOffset FirstObservedAt { get; init; }

    public required string ObservedTitle { get; init; }
}
