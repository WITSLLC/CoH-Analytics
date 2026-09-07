namespace CoHAnalytics.Models;

/// <summary>Authoritative catalog metadata attached to a classified received item.</summary>
public sealed record ReceivedItemClassificationMetadata
{
    public string? CatalogItemId { get; init; }

    public string? CatalogVersion { get; init; }

    public string? EnhancementSetName { get; init; }

    public int? EnhancementLevelMin { get; init; }

    public int? EnhancementLevelMax { get; init; }

    public string? EnhancementRarity { get; init; }

    public string? EnhancementTypeLabel { get; init; }

    public string? SalvageRarity { get; init; }

    public int? SalvageLevelMin { get; init; }

    public int? SalvageLevelMax { get; init; }

    public string? InspirationForm { get; init; }
}
