namespace CoHAnalytics.Models;

/// <summary>Immutable rolling-window combat projection for one preset width.</summary>
public sealed record RollingCombatWindowSnapshot
{
    public static RollingCombatWindowSnapshot Empty { get; } = new();

    public RollingCombatAvailability Availability { get; init; } =
        RollingCombatAvailability.NoCombatData;

    public int WindowMinutes { get; init; }

    public CombatScaledAmount DamageDealt { get; init; } = CombatScaledAmount.Zero;

    public long DamagePerSecondHundredths { get; init; }

    public TimeSpan WindowDuration { get; init; }

    public TimeSpan EffectiveDenominator { get; init; }

    public long TotalDefeated { get; init; }

    public long MyDefeats { get; init; }

    public CombatAccuracyScopeSnapshot Accuracy { get; init; } = CombatAccuracyScopeSnapshot.Empty;
}
