namespace CoHAnalytics.ReferenceData;

public sealed record ItemReferenceSearchResult
{
    public required string CatalogItemId { get; init; }

    public required ReferenceItemFamily Family { get; init; }

    public required string Subtype { get; init; }

    public required string CurrentDisplayName { get; init; }

    public string? MatchedText { get; init; }

    public string? EnhancementSetId { get; init; }

    public string? EnhancementSetName { get; init; }

    public string? Variant { get; init; }
}
