using CoHAnalytics.Models;
using CoHAnalytics.ReferenceData;

namespace CoHAnalytics.Observations;

/// <summary>Minimal observation envelope for unresolved acquisition capture.</summary>
public sealed record AcquisitionObservationRecord
{
    public required string ProducerId { get; init; }

    public required string ObservationKind { get; init; }

    public required string ObservedText { get; init; }

    public required string NormalizedLookupKey { get; init; }

    public required DateTimeOffset CapturedAtUtc { get; init; }

    public required string FailedCatalogVersion { get; init; }

    public required string GrammarSource { get; init; }

    public ReferenceItemFamily? FamilyHint { get; init; }

    public AcquisitionIdentityResolutionState ResolutionStateAtCapture { get; init; } =
        AcquisitionIdentityResolutionState.Unresolved;
}
