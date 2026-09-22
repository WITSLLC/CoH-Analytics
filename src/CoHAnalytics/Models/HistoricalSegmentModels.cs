namespace CoHAnalytics.Models;

/// <summary>Where a historical capture's authoritative analytics came from.</summary>
public enum HistoricalCaptureKind
{
    DurableSegment = 0,
    LegacyObservation = 1
}

/// <summary>
/// Typed historical compatibility. Unknown future schema/semantic versions never fall through
/// to the current v1/semantic-3 meaning.
/// </summary>
public enum HistoricalCompatibility
{
    AuthoritativeAggregate = 0,
    AuthoritativeAggregateDetailDegraded = 1,
    LegacyAdapted = 2,
    UnsupportedSchema = 3,
    UnsupportedSemanticVersion = 4,
    Corrupt = 5,
    IncompletePublication = 6,
    NotFound = 7
}

/// <summary>Why retained event detail is or is not usable. Never implies cube replacement.</summary>
public enum HistoricalDetailStatus
{
    NotRequested = 0,
    AvailableComplete = 1,
    AvailablePartial = 2,
    Unavailable = 3,
    Corrupt = 4,
    UnsupportedSchema = 5
}

public enum HistoricalBuildContextStatus
{
    Present = 0,
    NotCaptured = 1,
    Missing = 2,
    HashMismatch = 3,
    NotApplicable = 4
}

public enum HistoricalAnnotationStatus
{
    Present = 0,
    Missing = 1,
    Corrupt = 2,
    NotApplicable = 3
}

/// <summary>Live vs historical projection source. Formulas stay on the engine/persisted cube.</summary>
public enum AnalyticalProjectionSourceKind
{
    Live = 0,
    HistoricalDurable = 1,
    HistoricalLegacy = 2
}

/// <summary>UI-agnostic projection plus the context needed to interpret it. No formatting.</summary>
public sealed record AnalyticalProjectionView
{
    public required AnalyticalProjectionSourceKind SourceKind { get; init; }

    public required CombatAnalyticsProjection Projection { get; init; }

    public HistoricalSegmentHeader? Header { get; init; }

    public SegmentCoverageDescriptor? Coverage { get; init; }
}

/// <summary>Cheap listing metadata. Does not include aggregates, spine, or the frozen manifest.</summary>
public sealed record HistoricalSegmentHeader
{
    public required string SegmentId { get; init; }

    public required HistoricalCaptureKind CaptureKind { get; init; }

    public required HistoricalCompatibility Compatibility { get; init; }

    public required GameplaySessionId GameplaySessionId { get; init; }

    public required int SegmentOrdinal { get; init; }

    public CharacterRecordId? CharacterRecordId { get; init; }

    public CharacterRecordId? CanonicalCharacterRecordId { get; init; }

    public string? AccountStableId { get; init; }

    public string? CharacterDisplayNameAtCapture { get; init; }

    public string? AccountDisplayNameAtCapture { get; init; }

    public int? LevelAtCapture { get; init; }

    public string? Archetype { get; init; }

    public string? PrimaryPowerSet { get; init; }

    public string? SecondaryPowerSet { get; init; }

    public required DateTimeOffset CaptureStartUtc { get; init; }

    public required DateTimeOffset CaptureEndUtc { get; init; }

    public DateTimeOffset? FinalizedAtUtc { get; init; }

    public int? SegmentSchemaVersion { get; init; }

    public int? SpineSchemaVersion { get; init; }

    public int? AnalyticsSemanticVersion { get; init; }

    public int? GrammarSetVersion { get; init; }

    public int? DedupPolicyVersion { get; init; }

    public int? AttributionPolicyVersion { get; init; }

    public int? ObservationSchemaVersion { get; init; }

    public string? BuildManifestHash { get; init; }

    public string? BuildCatalogFingerprint { get; init; }

    public string? AppVersion { get; init; }

    public long LogicalEventCount { get; init; }

    public int RetainedSpineEventCount { get; init; }

    public bool SpineTruncated { get; init; }

    public bool CoverageLimited { get; init; }

    public ReplayCoverageKind? EventTimelineReplay { get; init; }

    public bool HasLegacyObservationCounterpart { get; init; }

    public bool UsedLegacyFallback { get; init; }

    public string? Detail { get; init; }
}

/// <summary>Repository-level historical filter. Applied to headers, not decoded spines.</summary>
public sealed record HistoricalSegmentQuery
{
    public CharacterRecordId? CharacterRecordId { get; init; }

    public DateTimeOffset? RangeStartUtc { get; init; }

    public DateTimeOffset? RangeEndUtc { get; init; }

    public string? SegmentId { get; init; }

    public GameplaySessionId? GameplaySessionId { get; init; }

    public int? SegmentOrdinal { get; init; }
}

/// <summary>Optional payload selection. Spine defaults off so consumers inspect coverage first.</summary>
public sealed record HistoricalLoadOptions
{
    public static HistoricalLoadOptions Default { get; } = new();

    public bool IncludeSpine { get; init; }

    public bool IncludeManifest { get; init; } = true;

    public bool IncludeAnnotations { get; init; } = true;
}

/// <summary>Fully loaded historical capture. Cube is authoritative; spine is coverage-gated detail.</summary>
public sealed record HistoricalSegment
{
    public required HistoricalSegmentHeader Header { get; init; }

    public CombatAnalyticsProjection? Aggregates { get; init; }

    public SegmentCoverageDescriptor? Coverage { get; init; }

    public required HistoricalDetailStatus DetailStatus { get; init; }

    public IReadOnlyList<PersistedSpineEvent> Spine { get; init; } = [];

    public FrozenBuildManifest? FrozenManifest { get; init; }

    public required HistoricalBuildContextStatus BuildContextStatus { get; init; }

    public SegmentAnnotations? Annotations { get; init; }

    public required HistoricalAnnotationStatus AnnotationStatus { get; init; }

    public CharacterPerformanceObservation? LegacyObservation { get; init; }

    public Metric<long> ExperienceGained { get; init; } = Metric<long>.NotCaptured();

    public Metric<long> GameplayInfluenceGained { get; init; } = Metric<long>.NotCaptured();

    public AnalyticalProjectionView? TryAsProjectionView()
    {
        if (Aggregates is null)
        {
            return null;
        }

        return new AnalyticalProjectionView
        {
            SourceKind = Header.CaptureKind == HistoricalCaptureKind.LegacyObservation
                ? AnalyticalProjectionSourceKind.HistoricalLegacy
                : AnalyticalProjectionSourceKind.HistoricalDurable,
            Projection = Aggregates,
            Header = Header,
            Coverage = Coverage
        };
    }
}

public enum HistoricalLoadOutcome
{
    Loaded = 0,
    LoadedDetailDegraded = 1,
    LoadedLegacyAdapted = 2,
    UnsupportedSchema = 3,
    UnsupportedSemanticVersion = 4,
    Corrupt = 5,
    IncompletePublication = 6,
    NotFound = 7
}

public sealed record HistoricalLoadResult
{
    public required HistoricalLoadOutcome Outcome { get; init; }

    public HistoricalSegment? Segment { get; init; }

    public string? Detail { get; init; }

    public bool HasAuthoritativeAggregates =>
        Segment?.Aggregates is not null
        && Outcome is HistoricalLoadOutcome.Loaded
            or HistoricalLoadOutcome.LoadedDetailDegraded
            or HistoricalLoadOutcome.LoadedLegacyAdapted;
}
