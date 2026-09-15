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

    public IReadOnlyList<CombatActorSummary> Actors { get; init; } = [];

    public IReadOnlyList<CombatTargetSummary> Targets { get; init; } = [];

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

    public bool CoverageLimited { get; init; }
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

    /// <summary>Tracked distinct names; a lower bound when CoverageLimited is true. Other is not a name.</summary>
    public long DistinctTargetCount { get; init; }

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
