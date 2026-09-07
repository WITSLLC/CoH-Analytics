using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

/// <summary>Presentation helpers for rolling session earnings rates.</summary>
public static class RollingEarningsPresentation
{
    public static RollingEarningsHeroPresentation BuildHero(
        RollingEarningsScopeSnapshot rolling,
        int selectedWindowMinutes)
    {
        var window = rolling.GetPreset(selectedWindowMinutes);
        var windowLabel = $"{selectedWindowMinutes} min";

        if (window.Availability == RollingEarningsAvailability.UnavailableTimestampPrecision)
        {
            return new RollingEarningsHeroPresentation
            {
                WindowLabel = windowLabel,
                ExperienceRateValue = "—",
                InfluenceRateValue = "—",
                Availability = window.Availability,
                Detail = "Rolling earnings unavailable for this log timestamp precision."
            };
        }

        if (window.Availability == RollingEarningsAvailability.UnavailableFrozen)
        {
            return new RollingEarningsHeroPresentation
            {
                WindowLabel = windowLabel,
                ExperienceRateValue = "—",
                InfluenceRateValue = "—",
                Availability = window.Availability,
                Detail = "Rolling earnings unavailable after session finalization."
            };
        }

        if (window.Availability == RollingEarningsAvailability.NoEarningsData)
        {
            return new RollingEarningsHeroPresentation
            {
                WindowLabel = windowLabel,
                ExperienceRateValue = "—",
                InfluenceRateValue = "—",
                Availability = window.Availability,
                Detail = "No qualifying earnings data yet."
            };
        }

        var experienceRateValue = window.Availability is RollingEarningsAvailability.WarmingUp
                or RollingEarningsAvailability.Available
            ? GameplaySessionTelemetryPresentation.FormatRatePerHour(
                window.ExperienceGained,
                window.EffectiveDenominator)
            : "—";
        var influenceRateValue = window.Availability is RollingEarningsAvailability.WarmingUp
                or RollingEarningsAvailability.Available
            ? GameplaySessionTelemetryPresentation.FormatRatePerHour(
                window.InfluenceGained,
                window.EffectiveDenominator)
            : "—";

        return new RollingEarningsHeroPresentation
        {
            WindowLabel = windowLabel,
            ExperienceRateValue = experienceRateValue,
            InfluenceRateValue = influenceRateValue,
            Availability = window.Availability
        };
    }

    public static RollingEarningsHeroPresentation BuildUnavailableHero(int selectedWindowMinutes) =>
        new()
        {
            WindowLabel = $"{selectedWindowMinutes} min",
            ExperienceRateValue = "—",
            InfluenceRateValue = "—",
            Detail = "No active gameplay session."
        };
}

public sealed record RollingEarningsHeroPresentation
{
    public string WindowLabel { get; init; } = "10 min";

    public string ExperienceRateValue { get; init; } = "—";

    public string InfluenceRateValue { get; init; } = "—";

    public RollingEarningsAvailability Availability { get; init; } =
        RollingEarningsAvailability.NoEarningsData;

    public string? Detail { get; init; }
}
