using CoHAnalytics.Models;
using CoHAnalytics.ReferenceData;

namespace CoHAnalytics.Observations;

public sealed record AcquisitionObservationFilter
{
    public string? SearchText { get; init; }

    public ReferenceItemFamily? Family { get; init; }

    public AcquisitionIdentityResolutionState? ResolutionState { get; init; }

    public bool IncludeInactiveDispositions { get; init; }
}

public sealed record AcquisitionObservationListItem
{
    public required string NormalizedKey { get; init; }

    public required string ObservedText { get; init; }

    public ReferenceItemFamily? FamilyHint { get; init; }

    public required AcquisitionIdentityResolutionState ResolutionState { get; init; }

    public required ObservationDispositionStatus Disposition { get; init; }

    public long OccurrenceCount { get; init; }

    public DateTimeOffset FirstSeenUtc { get; init; }

    public DateTimeOffset LastSeenUtc { get; init; }

    public int EvidenceSampleCount { get; init; }
}

public sealed record AcquisitionObservationEvidenceSample
{
    public required string ObservedText { get; init; }

    public required DateTimeOffset CapturedAtUtc { get; init; }

    public required string GrammarSource { get; init; }

    public required string FailedCatalogVersion { get; init; }
}

public sealed record AcquisitionObservationDetail
{
    public required AcquisitionObservationListItem Summary { get; init; }

    public IReadOnlyList<string> FailedCatalogVersions { get; init; } = [];

    public string? DeveloperNotes { get; init; }

    public IReadOnlyList<AcquisitionObservationEvidenceSample> Evidence { get; init; } = [];
}

public enum AcquisitionObservationOperationOutcome
{
    Success,
    NotFound,
    CatalogUnresolved,
    PersistenceFailed
}

public sealed record AcquisitionObservationOperationResult
{
    public required AcquisitionObservationOperationOutcome Outcome { get; init; }

    public string? Detail { get; init; }

    public bool IsSuccess => Outcome is AcquisitionObservationOperationOutcome.Success;
}
