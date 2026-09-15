namespace CoHAnalytics.Models;

/// <summary>
/// Immutable analytical projection. Live and historical both emit this DTO; UI must not
/// recompute combat formulas from it.
/// </summary>
public sealed record CombatAnalyticsProjection
{
    public static CombatAnalyticsProjection Empty { get; } = new()
    {
        Session = CombatSessionSummary.Empty
    };

    public int AnalyticsSemanticVersion { get; init; } = Models.AnalyticsSemanticVersion.Current;

    public long LogicalEventsApplied { get; init; }

    public long DuplicateOccurrencesIgnored { get; init; }

    public required CombatSessionSummary Session { get; init; }

    public IReadOnlyList<CombatPowerAnalysisRow> Powers { get; init; } = [];

    public IReadOnlyList<CombatDamageTypeTotal> DamageTypes { get; init; } = [];

    public IReadOnlyList<CombatDamageTypeTotal> IncomingDamageTypes { get; init; } = [];

    public MetricRef<IReadOnlyList<CombatDamageTypeTotal>> DamageTypeBreakdown { get; init; } =
        MetricRef<IReadOnlyList<CombatDamageTypeTotal>>.NotCaptured();

    public MetricRef<IReadOnlyList<CombatDamageTypeTotal>> IncomingDamageTypeBreakdown { get; init; } =
        MetricRef<IReadOnlyList<CombatDamageTypeTotal>>.NotCaptured();

    public IReadOnlyList<CombatActorSummary> Actors { get; init; } = [];

    public IReadOnlyList<CombatTargetSummary> Targets { get; init; } = [];

    public SegmentClock Clock { get; init; } = SegmentClock.Empty;

    public bool CoverageLimited { get; init; }
}

/// <summary>Session-level directional totals. Owner totals include local player plus owned pets.</summary>
public sealed record CombatSessionSummary
{
    public static CombatSessionSummary Empty { get; } = new();

    public CombatScaledAmount DamageDealt { get; init; }

    public CombatScaledAmount DamageDealtSelf { get; init; }

    public CombatScaledAmount DamageDealtOwnedPets { get; init; }

    public CombatScaledAmount DamageReceived { get; init; }

    public CombatScaledAmount DamageReceivedOwnedPets { get; init; }

    public CombatScaledAmount HealingDealt { get; init; }

    public CombatScaledAmount HealingDealtSelf { get; init; }

    public CombatScaledAmount HealingDealtOwnedPets { get; init; }

    public CombatScaledAmount HealingReceived { get; init; }

    public CombatScaledAmount HealingReceivedOwnedPets { get; init; }

    public CombatScaledAmount EnduranceGranted { get; init; }

    public CombatScaledAmount EnduranceGrantedSelf { get; init; }

    public CombatScaledAmount EnduranceGrantedOwnedPets { get; init; }

    public CombatScaledAmount EnduranceReceived { get; init; }

    public CombatScaledAmount EnduranceReceivedOwnedPets { get; init; }

    public long DamageEventCount { get; init; }

    public long HealEventCount { get; init; }

    public long EnduranceEventCount { get; init; }

    public long ActivationCount { get; init; }

    public long AttackResolutionCount { get; init; }

    public long DefeatCount { get; init; }

    public long MyDefeatCount { get; init; }

    public long MezCount { get; init; }

    public long KnockCount { get; init; }

    public long ConfirmedRechargeCompletedCount { get; init; }

    public long ConfirmedStillRechargingCount { get; init; }

    public long UnmatchedRechargeCandidateCount { get; init; }

    public CombatAccuracyScopeSnapshot Accuracy { get; init; } = CombatAccuracyScopeSnapshot.Empty;

    public CombatSessionMetricSet Metrics { get; init; } = CombatSessionMetricSet.Empty;

    public bool CoverageLimited { get; init; }
}

/// <summary>
/// Availability-typed session metrics. Slice 6 scalar fields remain for numeric compatibility;
/// consumers that must distinguish 0 from missing capture read this set.
/// </summary>
public sealed record CombatSessionMetricSet
{
    public static CombatSessionMetricSet Empty { get; } = Baseline();

    public Metric<CombatScaledAmount> DamageDealt { get; init; }

    public Metric<CombatScaledAmount> DamageDealtSelf { get; init; }

    public Metric<CombatScaledAmount> DamageDealtOwnedPets { get; init; }

    public Metric<CombatScaledAmount> DamageReceived { get; init; }

    public Metric<CombatScaledAmount> DamageReceivedOwnedPets { get; init; }

    public Metric<CombatScaledAmount> HealingDealt { get; init; }

    public Metric<CombatScaledAmount> HealingReceived { get; init; }

    public Metric<CombatScaledAmount> EnduranceGranted { get; init; }

    public Metric<CombatScaledAmount> EnduranceReceived { get; init; }

    public Metric<long> DamageEventCount { get; init; }

    public Metric<long> ActivationCount { get; init; }

    public Metric<long> AttackResolutionCount { get; init; }

    public Metric<long> ConfirmedRechargeCompletedCount { get; init; }

    public Metric<long> ConfirmedStillRechargingCount { get; init; }

    public Metric<long> UnmatchedRechargeCandidateCount { get; init; }

    public Metric<TimeSpan> ObservedActivationToRechargeInterval { get; init; }

    public Metric<TimeSpan> ObservedRechargeToNextActivationInterval { get; init; }

    public Metric<TimeSpan> TheoreticalRecharge { get; init; }

    public Metric<TimeSpan> PermaHasten { get; init; }

    public Metric<CombatScaledAmount> Overkill { get; init; }

    public Metric<TimeSpan> MezDuration { get; init; }

    public Metric<long> CompanionMissResolutionCount { get; init; }

    public Metric<long> PetInstanceCount { get; init; }

    public Metric<long> DistinctTargetCount { get; init; }

    public MetricRef<CombatAccuracyScopeSnapshot> Accuracy { get; init; } =
        MetricRef<CombatAccuracyScopeSnapshot>.NotCaptured();

    internal static CombatSessionMetricSet Baseline() =>
        new()
        {
            ObservedActivationToRechargeInterval = Metric<TimeSpan>.NotCaptured(),
            ObservedRechargeToNextActivationInterval = Metric<TimeSpan>.NotCaptured(),
            TheoreticalRecharge = Metric<TimeSpan>.Unsupported(),
            PermaHasten = Metric<TimeSpan>.Unsupported(),
            Overkill = Metric<CombatScaledAmount>.Unsupported(),
            MezDuration = Metric<TimeSpan>.Unsupported(),
            CompanionMissResolutionCount = Metric<long>.Unsupported(),
            PetInstanceCount = Metric<long>.Unsupported()
        };
}

/// <summary>One power cube row. Self and owned-pet rows with the same surfaced name stay distinct.</summary>
public sealed record CombatPowerAnalysisRow
{
    public CombatAnalyticsDirection Direction { get; init; }
    public required CombatAnalyticsScope Scope { get; init; }

    public required string PowerName { get; init; }

    public string? PetNormalizedName { get; init; }

    public string? PetDisplayName { get; init; }

    /// <summary>Null for mixed HP/endurance units. Zero with None means no measured magnitude.</summary>
    public CombatScaledAmount? TotalMagnitude { get; init; }
    public MagnitudeKind? TotalMagnitudeKind { get; init; }

    /// <summary>Unsupported for mixed units, NotCaptured for lifecycle-only rows.</summary>
    public Metric<CombatScaledAmount> TotalMagnitudeMetric { get; init; }
    public Metric<CombatScaledAmount> DamageMagnitudeMetric { get; init; }
    public Metric<CombatScaledAmount> HealingMagnitudeMetric { get; init; }
    public Metric<CombatScaledAmount> EnduranceMagnitudeMetric { get; init; }
    public MetricRef<IReadOnlyList<CombatDamageTypeTotal>> DamageTypeBreakdown { get; init; } =
        MetricRef<IReadOnlyList<CombatDamageTypeTotal>>.NotCaptured();

    public CombatScaledAmount DamageMagnitude { get; init; }

    public CombatScaledAmount HealingMagnitude { get; init; }

    public CombatScaledAmount EnduranceMagnitude { get; init; }

    public long EventCount { get; init; }

    public long HitResolutionCount { get; init; }

    public long ActivationCount { get; init; }

    public CombatScaledAmount? LargestHit { get; init; }

    public CombatScaledAmount DirectAmount { get; init; }

    public CombatScaledAmount DotAmount { get; init; }

    public IReadOnlyList<CombatDamageTypeTotal> DamageTypes { get; init; } = [];

    /// <summary>Tracked distinct names; consult DistinctTargetCountMetric for cardinality coverage. Overflow is not a name.</summary>
    public long DistinctTargetCount { get; init; }

    public Metric<long> DistinctTargetCountMetric { get; init; }

    public long ConfirmedRechargeCompletedCount { get; init; }

    public long ConfirmedStillRechargingCount { get; init; }

    public bool IsOverflow { get; init; }

    public bool CoverageLimited { get; init; }
}

public sealed record CombatDamageTypeTotal
{
    public required DamageType DamageType { get; init; }

    public CombatScaledAmount Amount { get; init; }

    public long EventCount { get; init; }

    public bool IsOverflow { get; init; }
}

public sealed record CombatActorSummary
{
    public required CombatAnalyticsScope Scope { get; init; }

    public string? PetNormalizedName { get; init; }

    public string? PetDisplayName { get; init; }

    public CombatScaledAmount DamageDealt { get; init; }

    public CombatScaledAmount DamageReceived { get; init; }

    public CombatScaledAmount HealingDealt { get; init; }

    public CombatScaledAmount HealingReceived { get; init; }

    public CombatScaledAmount EnduranceGranted { get; init; }

    public CombatScaledAmount EnduranceReceived { get; init; }

    public long ActivationCount { get; init; }

    public CombatAccuracyScopeSnapshot Accuracy { get; init; } = CombatAccuracyScopeSnapshot.Empty;

    /// <summary>Pet instance split is not supported; name rollup is the coverage-limited contract.</summary>
    public Metric<long> PetInstanceCount { get; init; } = Metric<long>.Unsupported();

    public bool CoverageLimited { get; init; }

    public bool IsOverflow { get; init; }
}

public sealed record CombatTargetSummary
{
    public required string NormalizedTargetName { get; init; }

    public string? DisplayName { get; init; }

    public CombatScaledAmount DamageDealt { get; init; }

    public long EventCount { get; init; }

    public bool IsOverflow { get; init; }
}
