namespace CoHAnalytics.Models;

/// <summary>Availability state for rolling combat DPS projections.</summary>
public enum RollingCombatAvailability
{
    Available,
    NoCombatData,
    WarmingUp,
    UnavailableTimestampPrecision,
    UnavailableFrozen
}
