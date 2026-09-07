namespace CoHAnalytics.ReferenceData;

/// <summary>Zone metadata for badge and plaque reference data.</summary>
public sealed record ZoneReferenceRecord
{
    public required string ZoneId { get; init; }

    public required string DisplayName { get; init; }

    public string? AlignmentNotes { get; init; }

    public string? LevelRange { get; init; }

    public string? ZoneType { get; init; }

    public IReadOnlyList<string> ExplorationCompletionBadgeIds { get; init; } = [];

    public IReadOnlyList<string> HistoryCompletionBadgeIds { get; init; } = [];
}
