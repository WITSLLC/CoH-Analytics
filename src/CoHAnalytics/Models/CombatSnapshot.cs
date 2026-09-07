namespace CoHAnalytics.Models;

/// <summary>Immutable session-scope combat projection attached to <see cref="GameplaySessionSnapshot"/>.</summary>
public sealed record CombatSnapshot
{
    public static CombatSnapshot Empty { get; } = new();

    public CombatScaledAmount DamageDealt { get; init; } = CombatScaledAmount.Zero;

    public CombatScaledAmount DamageReceived { get; init; } = CombatScaledAmount.Zero;

    public CombatScaledAmount HealingDealt { get; init; } = CombatScaledAmount.Zero;

    public CombatScaledAmount HealingReceived { get; init; } = CombatScaledAmount.Zero;

    public long TotalDefeated { get; init; }

    public long MyDefeats { get; init; }

    public long PowerActivations { get; init; }

    public bool IsInCombat { get; init; }

    public DateTimeOffset? LastCombatAt { get; init; }

    public TimeSpan CurrentEngagementDuration { get; init; }

    /// <summary>
    /// Session damage per second in hundredths (same scale as <see cref="CombatScaledAmount"/>).
    /// Uses wall-clock elapsed time aligned with session XP/hour semantics.
    /// </summary>
    public long SessionDamagePerSecondHundredths { get; init; }

    public TrackedCombatScopeSnapshot Tracked { get; init; } = TrackedCombatScopeSnapshot.Empty;

    public RollingCombatScopeSnapshot Rolling { get; init; } = RollingCombatScopeSnapshot.Empty;

    public CombatAccuracyScopeSnapshot Accuracy { get; init; } = CombatAccuracyScopeSnapshot.Empty;
}
