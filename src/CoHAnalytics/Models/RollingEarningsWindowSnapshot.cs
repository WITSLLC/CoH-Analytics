namespace CoHAnalytics.Models;

/// <summary>Immutable rolling-window earnings projection for one preset width.</summary>
public sealed record RollingEarningsWindowSnapshot
{
    public static RollingEarningsWindowSnapshot Empty { get; } = new();

    public RollingEarningsAvailability Availability { get; init; } =
        RollingEarningsAvailability.NoEarningsData;

    public int WindowMinutes { get; init; }

    public long ExperienceGained { get; init; }

    public long InfluenceGained { get; init; }

    public TimeSpan WindowDuration { get; init; }

    public TimeSpan EffectiveDenominator { get; init; }
}
