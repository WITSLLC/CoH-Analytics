namespace CoHAnalytics.Services;

/// <summary>Named defaults for combat activity evaluation.</summary>
public static class CombatActivityDefaults
{
    /// <summary>
    /// Inactivity period after the last qualifying combat event before combat status transitions to idle.
    /// </summary>
    public static readonly TimeSpan IdleThreshold = TimeSpan.FromSeconds(10);
}
