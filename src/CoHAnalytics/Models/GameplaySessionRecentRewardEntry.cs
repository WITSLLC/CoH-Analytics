namespace CoHAnalytics.Models;

/// <summary>One recent reward or drop observed during a gameplay session.</summary>
public sealed record GameplaySessionRecentRewardEntry
{
    public required DateTimeOffset ObservedAt { get; init; }

    public required GameplaySessionRewardCategory Category { get; init; }

    public required string DisplayName { get; init; }

    public long Quantity { get; init; }

    public string? RawReceivedItemText { get; init; }

    public ReceivedItemClassificationMetadata? ClassificationMetadata { get; init; }

    public BadgeAcquisitionMetadata? BadgeAcquisitionMetadata { get; init; }
}
