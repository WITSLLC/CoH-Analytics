namespace CoHAnalytics.Models;

/// <summary>Catalog resolution metadata attached to a classified badge acquisition.</summary>
public sealed record BadgeAcquisitionMetadata
{
    public string? CatalogItemId { get; init; }

    public string? CatalogVersion { get; init; }

    public string? MatchedAliasText { get; init; }
}
