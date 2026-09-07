namespace CoHAnalytics.Models;

/// <summary>Immutable tracked-scope combat projection within <see cref="CombatSnapshot"/>.</summary>
public sealed record TrackedCombatScopeSnapshot
{
    public static TrackedCombatScopeSnapshot Empty { get; } = new();

    public bool IsTracking { get; init; }

    public bool IsPaused { get; init; }

    public DateTimeOffset? StartedAt { get; init; }

    public TimeSpan ActiveElapsed { get; init; }

    public CombatScaledAmount DamageDealt { get; init; } = CombatScaledAmount.Zero;

    public CombatScaledAmount DamageReceived { get; init; } = CombatScaledAmount.Zero;

    public CombatScaledAmount HealingDealt { get; init; } = CombatScaledAmount.Zero;

    public CombatScaledAmount HealingReceived { get; init; } = CombatScaledAmount.Zero;

    public long TotalDefeated { get; init; }

    public long MyDefeats { get; init; }

    public long PowerActivations { get; init; }

    /// <summary>
    /// Tracked damage per second in hundredths using tracked active elapsed time only.
    /// </summary>
    public long DamagePerSecondHundredths { get; init; }

    public CombatAccuracyScopeSnapshot Accuracy { get; init; } = CombatAccuracyScopeSnapshot.Empty;
}
