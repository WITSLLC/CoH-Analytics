namespace CoHAnalytics.Services;

public enum AcquisitionClassificationOutcome
{
    Success,
    DeveloperModeRequired,
    SourceUnavailable,
    ObservationNotFound,
    ReferenceNotFound,
    Conflict,
    ValidationFailed,
    GenerationFailed,
    ReloadFailed,
    ReconciliationFailed
}

public sealed record AcquisitionClassificationResult
{
    public required AcquisitionClassificationOutcome Outcome { get; init; }

    public string? CatalogItemId { get; init; }

    public string? Detail { get; init; }

    public bool IsSuccess => Outcome is AcquisitionClassificationOutcome.Success;
}

public sealed record NewEnhancementReference
{
    public required string CurrentDisplayName { get; init; }

    public required string Subtype { get; init; }

    public string? EnhancementSetId { get; init; }

    public required string Variant { get; init; }
}

public sealed record NewRecipeReference
{
    public required string CurrentDisplayName { get; init; }

    public required string Subtype { get; init; }

    public required string ProducedItemId { get; init; }

    public string? EnhancementSetId { get; init; }

    public required string Rarity { get; init; }
}

public sealed record AcquisitionClassificationOptions
{
    public string? AuthoritativeCatalogPath { get; init; }

    public string? RuntimeDatabasePath { get; init; }
}
