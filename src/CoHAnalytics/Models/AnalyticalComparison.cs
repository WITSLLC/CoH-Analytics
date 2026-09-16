namespace CoHAnalytics.Models;

/// <summary>Pair of live or historical projections. Comparison never touches SegmentStore.</summary>
public sealed record AnalyticalComparisonInput
{
    public required AnalyticalProjectionView Left { get; init; }

    public required AnalyticalProjectionView Right { get; init; }
}

/// <summary>Facts about one comparison side. No filesystem paths or ranking.</summary>
public sealed record ComparisonSourceContext
{
    public required AnalyticalProjectionSourceKind SourceKind { get; init; }

    public string? SegmentId { get; init; }

    public CharacterRecordId? CharacterRecordId { get; init; }

    public DateTimeOffset? CaptureStartUtc { get; init; }

    public DateTimeOffset? CaptureEndUtc { get; init; }

    public required int AnalyticsSemanticVersion { get; init; }

    public string? BuildManifestHash { get; init; }

    public string? BuildCatalogFingerprint { get; init; }

    public required int AttributionPolicyVersion { get; init; }

    public bool CoverageLimited { get; init; }

    public HistoricalCaptureKind? CaptureKind { get; init; }
}

/// <summary>UI-agnostic comparison result. Facts only; no winner, cause, or formatting.</summary>
public sealed record AnalyticalComparison
{
    public required ComparisonSourceContext Left { get; init; }

    public required ComparisonSourceContext Right { get; init; }

    public required AnalyticalComparisonCompatibility Compatibility { get; init; }

    public required SessionMetricComparison Session { get; init; }

    public required ClockMetricComparison Clock { get; init; }

    public IReadOnlyList<PowerMetricComparison> Powers { get; init; } = [];

    public IReadOnlyList<ActorMetricComparison> Actors { get; init; } = [];

    public IReadOnlyList<DamageTypeMetricComparison> OutgoingDamageTypes { get; init; } = [];

    public IReadOnlyList<DamageTypeMetricComparison> IncomingDamageTypes { get; init; } = [];

    public IReadOnlyList<TargetMetricComparison> Targets { get; init; } = [];

    public required BuildContextComparison Build { get; init; }

    public required AttributionMetricComparison Attribution { get; init; }

    /// <summary>False when either side's distinct-target metric is Incomplete/overflow/lower-bound.</summary>
    public bool ExactTargetCardinalityComparable { get; init; }
}

public sealed record SessionMetricComparison
{
    public MetricComparison<CombatScaledAmount> DamageDealt { get; init; }

    public MetricComparison<CombatScaledAmount> DamageDealtSelf { get; init; }

    public MetricComparison<CombatScaledAmount> DamageDealtOwnedPets { get; init; }

    public MetricComparison<CombatScaledAmount> DamageReceived { get; init; }

    public MetricComparison<CombatScaledAmount> DamageReceivedOwnedPets { get; init; }

    public MetricComparison<CombatScaledAmount> HealingDealt { get; init; }

    public MetricComparison<CombatScaledAmount> HealingReceived { get; init; }

    public MetricComparison<CombatScaledAmount> EnduranceGranted { get; init; }

    public MetricComparison<CombatScaledAmount> EnduranceReceived { get; init; }

    public MetricComparison<long> DamageEventCount { get; init; }

    public MetricComparison<long> ActivationCount { get; init; }

    public MetricComparison<long> AttackResolutionCount { get; init; }

    public MetricComparison<long> DefeatCount { get; init; }

    public MetricComparison<long> MyDefeatCount { get; init; }

    public MetricComparison<long> ConfirmedRechargeCompletedCount { get; init; }

    public MetricComparison<long> ConfirmedStillRechargingCount { get; init; }

    public MetricComparison<long> UnmatchedRechargeCandidateCount { get; init; }

    public MetricComparison<long> DistinctTargetCount { get; init; }

    public MetricComparison<TimeSpan> TheoreticalRecharge { get; init; }

    public MetricComparison<TimeSpan> PermaHasten { get; init; }

    public MetricComparison<CombatScaledAmount> Overkill { get; init; }

    public MetricComparison<TimeSpan> MezDuration { get; init; }

    public MetricComparison<long> PetInstanceCount { get; init; }

    public required AccuracyMetricComparison Accuracy { get; init; }
}

public sealed record AccuracyMetricComparison
{
    public ComparisonState State { get; init; }

    public ComparisonReason Reason { get; init; }

    public MetricComparison<long> Hits { get; init; }

    public MetricComparison<long> Misses { get; init; }

    public MetricComparison<long> Attempts { get; init; }

    /// <summary>Hit-rate change in hundredths of a percentage point. 500 = +5.00 pp, not +6.7% relative.</summary>
    public Metric<long> HitRatePercentagePointDeltaHundredths { get; init; }

    public ComparisonReason HitRatePercentagePointReason { get; init; }
}

public sealed record ClockMetricComparison
{
    public MetricComparison<TimeSpan> WallClockDuration { get; init; }

    public MetricComparison<TimeSpan> ObservedAnalyticalSpan { get; init; }

    public MetricComparison<TimeSpan> TrackedPauseAdjustedDuration { get; init; }

    public MetricComparison<TimeSpan> ActiveDuration { get; init; }

    public MetricComparison<long> WallClockDamagePerSecondHundredths { get; init; }

    public MetricComparison<long> ActiveDamagePerSecondHundredths { get; init; }

    public bool WallClockDurationsEqual { get; init; }
}

public sealed record ComparisonPowerKey
{
    public required CombatAnalyticsDirection Direction { get; init; }

    public required CombatAnalyticsScope Scope { get; init; }

    public required string PowerName { get; init; }

    public string PetNormalizedName { get; init; } = "";

    /// <summary>Separates the bounded internal overflow bucket from a literal surfaced "Other" power.</summary>
    public bool IsOverflow { get; init; }
}

public sealed record PowerMetricComparison
{
    public required ComparisonPowerKey Key { get; init; }

    public required ComparisonPresence Presence { get; init; }

    public CombatPowerAnalysisRow? Left { get; init; }

    public CombatPowerAnalysisRow? Right { get; init; }

    /// <summary>Retained only when a malformed projection contains duplicate stable keys.</summary>
    public IReadOnlyList<CombatPowerAnalysisRow> LeftCandidates { get; init; } = [];

    /// <summary>Retained only when a malformed projection contains duplicate stable keys.</summary>
    public IReadOnlyList<CombatPowerAnalysisRow> RightCandidates { get; init; } = [];

    public MetricComparison<CombatScaledAmount> DamageMagnitude { get; init; }

    public MetricComparison<CombatScaledAmount> HealingMagnitude { get; init; }

    public MetricComparison<CombatScaledAmount> EnduranceMagnitude { get; init; }

    public MetricComparison<long> EventCount { get; init; }

    public MetricComparison<long> ActivationCount { get; init; }

    public MetricComparison<CombatScaledAmount> DirectAmount { get; init; }

    public MetricComparison<CombatScaledAmount> DotAmount { get; init; }

    public MetricComparison<CombatScaledAmount> LargestHit { get; init; }

    public MetricComparison<long> DistinctTargetCount { get; init; }

    public IReadOnlyList<DamageTypeMetricComparison> DamageTypes { get; init; } = [];

    public bool CoverageLimited { get; init; }
}

public sealed record ActorMetricComparison
{
    public required CombatAnalyticsScope Scope { get; init; }

    public string PetNormalizedName { get; init; } = "";

    public bool IsOverflow { get; init; }

    public required ComparisonPresence Presence { get; init; }

    public CombatActorSummary? Left { get; init; }

    public CombatActorSummary? Right { get; init; }

    public MetricComparison<CombatScaledAmount> DamageDealt { get; init; }

    public MetricComparison<CombatScaledAmount> DamageReceived { get; init; }

    public MetricComparison<CombatScaledAmount> HealingDealt { get; init; }

    public MetricComparison<CombatScaledAmount> HealingReceived { get; init; }

    public MetricComparison<CombatScaledAmount> EnduranceGranted { get; init; }

    public MetricComparison<CombatScaledAmount> EnduranceReceived { get; init; }

    public MetricComparison<long> ActivationCount { get; init; }

    public MetricComparison<long> PetInstanceCount { get; init; }

    public required AccuracyMetricComparison Accuracy { get; init; }

    public bool CoverageLimited { get; init; }
}

public sealed record DamageTypeMetricComparison
{
    public required DamageType DamageType { get; init; }

    public bool IsOverflow { get; init; }

    public required ComparisonPresence Presence { get; init; }

    public MetricComparison<CombatScaledAmount> Amount { get; init; }

    public MetricComparison<long> EventCount { get; init; }
}

public sealed record TargetMetricComparison
{
    public required string NormalizedTargetName { get; init; }

    public required ComparisonPresence Presence { get; init; }

    public CombatTargetSummary? Left { get; init; }

    public CombatTargetSummary? Right { get; init; }

    public MetricComparison<CombatScaledAmount> DamageDealt { get; init; }

    public MetricComparison<long> EventCount { get; init; }

    public bool Overflow { get; init; }
}

public sealed record BuildContextComparison
{
    public MetricAvailability LeftAvailability { get; init; }

    public MetricAvailability RightAvailability { get; init; }

    /// <summary>Null when either side lacks a manifest hash. Never implies damage causality.</summary>
    public bool? SameManifestHash { get; init; }

    /// <summary>Null when either side lacks a catalog fingerprint. Never implies damage causality.</summary>
    public bool? SameCatalogFingerprint { get; init; }
}

public sealed record AttributionMetricComparison
{
    public ComparisonState State { get; init; }

    public ComparisonReason Reason { get; init; }

    public int LeftPolicyVersion { get; init; }

    public int RightPolicyVersion { get; init; }

    public MetricComparison<CombatScaledAmount> ProcDamage { get; init; }

    public MetricComparison<CombatScaledAmount> DirectDamage { get; init; }

    public MetricComparison<CombatScaledAmount> BuildConfirmedProcDamage { get; init; }

    public MetricComparison<CombatScaledAmount> UnattributedDamage { get; init; }

    public MetricComparison<CombatScaledAmount> UnattributedProcDamage { get; init; }

    public MetricComparison<long> DirectCount { get; init; }

    public MetricComparison<long> BuildConfirmedCount { get; init; }

    public MetricComparison<long> CorrelatedCount { get; init; }

    public MetricComparison<long> UnattributedCount { get; init; }
}
