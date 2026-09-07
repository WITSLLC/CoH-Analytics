using CoHAnalytics.ReferenceData;

namespace CoHAnalytics.Models;

/// <summary>
/// Complete received-item classification result, separating player presentation from catalog identity.
/// </summary>
public sealed record ReceivedItemClassificationResult
{
    public required GameplaySessionRewardCategory PresentationCategory { get; init; }

    public required AcquisitionIdentityResolutionState ResolutionState { get; init; }

    public ReferenceItemFamily? FamilyHint { get; init; }

    public string? CatalogItemId { get; init; }

    public string? CatalogVersion { get; init; }

    public ReceivedItemClassificationMetadata? Metadata { get; init; }

    public bool HasKnownPresentation =>
        PresentationCategory is not GameplaySessionRewardCategory.ReceivedItem;
}
