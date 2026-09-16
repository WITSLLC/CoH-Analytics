namespace CoHAnalytics.Models;

/// <summary>Immutable capture header. Display names are frozen descriptive metadata only.</summary>
public sealed record SegmentCaptureMetadata
{
    public int SegmentSchemaVersion { get; init; } = Models.SegmentSchemaVersion.Current;

    public int AnalyticsSemanticVersion { get; init; } = Models.AnalyticsSemanticVersion.Current;

    public int GrammarSetVersion { get; init; } = EventProvenance.CurrentGrammarSetVersion;

    public int DedupPolicyVersion { get; init; } = Models.DedupPolicyVersion.Current;

    public int AttributionPolicyVersion { get; init; } = Models.AttributionPolicyVersion.Current;

    public int SpineSchemaVersion { get; init; } = Models.SpineSchemaVersion.Current;

    public required GameplaySessionId GameplaySessionId { get; init; }

    public required int SegmentOrdinal { get; init; }

    public CharacterRecordId? CharacterRecordId { get; init; }

    public string? AccountStableId { get; init; }

    public string? AccountDisplayNameAtCapture { get; init; }

    public string? CharacterDisplayNameAtCapture { get; init; }

    public int? LevelAtCapture { get; init; }

    public string? Archetype { get; init; }

    public string? PrimaryPowerSet { get; init; }

    public string? SecondaryPowerSet { get; init; }

    public required DateTimeOffset CaptureStartUtc { get; init; }

    public required DateTimeOffset CaptureEndUtc { get; init; }

    public DateTimeOffset? FinalizedAtUtc { get; init; }

    public string? AppVersion { get; init; }

    public string? BuildManifestHash { get; init; }

    public string? BuildCatalogFingerprint { get; init; }

    public required SegmentFileHashes FileHashes { get; init; }
}

/// <summary>SHA-256 (lowercase hex) of each immutable published file. Annotations are mutable.</summary>
public sealed record SegmentFileHashes
{
    public required string Coverage { get; init; }

    public required string Aggregates { get; init; }

    public required string Spine { get; init; }

    public string? Manifest { get; init; }
}

/// <summary>Mutable sidecar. Never rewrite immutable capture files to rename a segment.</summary>
public sealed record SegmentAnnotations
{
    public string? UserDisplayName { get; init; }

    public bool IsBeta { get; init; }

    public bool IncludeInOverview { get; init; } = true;

    public string? Note { get; init; }
}

/// <summary>Coverage descriptor plus truncation counts and the replay matrix.</summary>
public sealed record SegmentCoverageDescriptor
{
    public required long LogicalEventCount { get; init; }

    public required long DuplicateOccurrencesIgnored { get; init; }

    public required int RetainedSpineEventCount { get; init; }

    public required int SpineRetentionLimit { get; init; }

    public required bool SpineTruncated { get; init; }

    public bool CoverageLimited { get; init; }

    public required LosslessReplayCoverageMatrix Replay { get; init; }
}

/// <summary>One retained logical canonical event. No raw log text.</summary>
public sealed record PersistedSpineEvent
{
    public required long Sequence { get; init; }

    public required CombatEventFamily Family { get; init; }

    public required CombatGrammarId GrammarId { get; init; }

    public required ActorRef Actor { get; init; }

    public ActorRef? Target { get; init; }

    public string? PowerName { get; init; }

    public CombatScaledAmount Amount { get; init; }

    public MagnitudeKind Magnitude { get; init; }

    public DamageType? DamageType { get; init; }

    public DeliveryFlags Delivery { get; init; }

    public string? EffectSuffix { get; init; }

    public CombatAttackOutcome? Outcome { get; init; }

    public long? DisplayedChanceHundredths { get; init; }

    public long? RollHundredths { get; init; }

    public EventFacets Facets { get; init; }

    public string? StatusName { get; init; }

    public PowerStateTransition? PowerStateTransition { get; init; }

    public required MirrorClassification MirrorClass { get; init; }

    public required DateTimeOffset ObservedAt { get; init; }

    public DateTime? SourceTimestamp { get; init; }

    public required EventProvenance Provenance { get; init; }

    public PowerAttribution? Attribution { get; init; }

    public static PersistedSpineEvent FromLogical(
        CanonicalCombatEvent canonical,
        PowerAttribution? attribution)
    {
        ArgumentNullException.ThrowIfNull(canonical);
        if (canonical.DuplicateOf is not null)
        {
            throw new ArgumentException("Duplicate physical occurrences are not spine events.", nameof(canonical));
        }

        return new PersistedSpineEvent
        {
            Sequence = canonical.Sequence,
            Family = canonical.Family,
            GrammarId = canonical.GrammarId,
            Actor = canonical.Actor,
            Target = canonical.Target,
            PowerName = canonical.PowerName,
            Amount = canonical.Amount,
            Magnitude = canonical.Magnitude,
            DamageType = canonical.DamageType,
            Delivery = canonical.Delivery,
            EffectSuffix = canonical.EffectSuffix,
            Outcome = canonical.Outcome,
            DisplayedChanceHundredths = canonical.DisplayedChanceHundredths,
            RollHundredths = canonical.RollHundredths,
            Facets = canonical.Facets,
            StatusName = canonical.StatusName,
            PowerStateTransition = canonical.PowerStateTransition,
            MirrorClass = canonical.MirrorClass,
            ObservedAt = canonical.ObservedAt,
            SourceTimestamp = canonical.SourceTimestamp,
            Provenance = canonical.Provenance,
            Attribution = attribution
        };
    }
}

/// <summary>In-memory assembled Segment prior to or after durable publication.</summary>
public sealed record AnalyticalSegment
{
    public required SegmentCaptureMetadata Metadata { get; init; }

    public required SegmentCoverageDescriptor Coverage { get; init; }

    public required CombatAnalyticsProjection Aggregates { get; init; }

    public required IReadOnlyList<PersistedSpineEvent> Spine { get; init; }

    public FrozenBuildManifest? FrozenManifest { get; init; }

    public required SegmentAnnotations Annotations { get; init; }

    public string? PublishedDirectory { get; init; }
}

/// <summary>Publication input. File hashes are computed during atomic commit.</summary>
public sealed record SegmentDraft
{
    public required GameplaySessionId GameplaySessionId { get; init; }

    public int SegmentOrdinal { get; init; }

    public CharacterRecordId? CharacterRecordId { get; init; }

    public string? AccountStableId { get; init; }

    public string? AccountDisplayNameAtCapture { get; init; }

    public string? CharacterDisplayNameAtCapture { get; init; }

    public int? LevelAtCapture { get; init; }

    public string? Archetype { get; init; }

    public string? PrimaryPowerSet { get; init; }

    public string? SecondaryPowerSet { get; init; }

    public required DateTimeOffset CaptureStartUtc { get; init; }

    public required DateTimeOffset CaptureEndUtc { get; init; }

    public DateTimeOffset? FinalizedAtUtc { get; init; }

    public string? AppVersion { get; init; }

    public required CombatAnalyticsProjection Aggregates { get; init; }

    public required IReadOnlyList<PersistedSpineEvent> Spine { get; init; }

    public FrozenBuildManifest? FrozenManifest { get; init; }

    public required SegmentCoverageDescriptor Coverage { get; init; }

    public SegmentAnnotations Annotations { get; init; } = new();
}

public enum SegmentPersistOutcome
{
    Persisted = 0,
    Duplicate = 1,
    Conflict = 2,
    InvalidSegment = 3,
    PersistenceFailed = 4,
    Skipped = 5
}

public sealed record SegmentPersistResult
{
    public required SegmentPersistOutcome Outcome { get; init; }

    public string? DirectoryPath { get; init; }

    public string? Detail { get; init; }

    public bool IsSuccess =>
        Outcome is SegmentPersistOutcome.Persisted or SegmentPersistOutcome.Duplicate;
}

public enum SegmentLoadOutcome
{
    Loaded = 0,
    NotFound = 1,
    IncompletePublication = 2,
    HashMismatch = 3,
    UnsupportedSchema = 4,
    Corrupt = 5
}

public sealed record SegmentLoadResult
{
    public required SegmentLoadOutcome Outcome { get; init; }

    public AnalyticalSegment? Segment { get; init; }

    public string? DirectoryPath { get; init; }

    public string? Detail { get; init; }

    public bool IsSuccess => Outcome == SegmentLoadOutcome.Loaded;
}
