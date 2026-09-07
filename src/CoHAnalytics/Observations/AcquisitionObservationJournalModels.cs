using CoHAnalytics.Models;
using CoHAnalytics.ReferenceData;

namespace CoHAnalytics.Observations;

internal sealed class AcquisitionObservationJournalEntry
{
    public required string NormalizedKey { get; set; }

    public required string ObservedText { get; set; }

    public required string ProducerId { get; set; }

    public required string ObservationKind { get; set; }

    public long OccurrenceCount { get; set; }

    public DateTimeOffset FirstSeenUtc { get; set; }

    public DateTimeOffset LastSeenUtc { get; set; }

    public List<string> FailedCatalogVersions { get; set; } = [];

    public ObservationDispositionStatus Status { get; set; } = ObservationDispositionStatus.New;

    public string? DeveloperNotes { get; set; }

    public string? ResolvedCatalogItemId { get; set; }

    public string? ResolvingCatalogVersion { get; set; }

    public ReferenceItemFamily? FamilyHint { get; set; }

    public AcquisitionIdentityResolutionState ResolutionState { get; set; } =
        AcquisitionIdentityResolutionState.Unresolved;

    public int EvidenceSampleCount { get; set; }
}

internal sealed class AcquisitionObservationJournalDocument
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public string? LastReconciledCatalogVersion { get; set; }

    public List<AcquisitionObservationJournalEntry> Entries { get; set; } = [];
}
