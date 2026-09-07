namespace CoHAnalytics.Models;

/// <summary>Availability state for rolling earnings rate projections.</summary>
public enum RollingEarningsAvailability
{
    Available,
    NoEarningsData,
    WarmingUp,
    UnavailableTimestampPrecision,
    UnavailableFrozen
}
