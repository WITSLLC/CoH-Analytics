using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

/// <summary>Presentation helpers for Analytics rolling combat DPS.</summary>
public static class RollingCombatPresentation
{
    public const double DeltaDeadbandFraction = 0.03;

    public static RollingCombatHeroPresentation BuildHero(
        CombatSnapshot combat,
        TimeSpan sessionElapsed,
        int selectedWindowMinutes)
    {
        var window = combat.Rolling.GetPreset(selectedWindowMinutes);
        var windowLabel = $"{selectedWindowMinutes} min";

        if (window.Availability == RollingCombatAvailability.UnavailableTimestampPrecision)
        {
            return new RollingCombatHeroPresentation
            {
                WindowLabel = windowLabel,
                RateValue = "—",
                DamageDealtValue = "—",
                Availability = window.Availability,
                Detail = "Rolling DPS unavailable for this log timestamp precision."
            };
        }

        if (window.Availability == RollingCombatAvailability.UnavailableFrozen)
        {
            return new RollingCombatHeroPresentation
            {
                WindowLabel = windowLabel,
                RateValue = "—",
                DamageDealtValue = "—",
                Availability = window.Availability,
                Detail = "Rolling DPS unavailable after session finalization."
            };
        }

        if (window.Availability == RollingCombatAvailability.NoCombatData)
        {
            return new RollingCombatHeroPresentation
            {
                WindowLabel = windowLabel,
                RateValue = "—",
                DamageDealtValue = "—",
                Availability = window.Availability,
                Detail = "No qualifying combat data yet."
            };
        }

        var rateValue = window.Availability is RollingCombatAvailability.WarmingUp
                or RollingCombatAvailability.Available
            ? PrimaryPerformancePresentation.FormatDamagePerSecond(window.DamagePerSecondHundredths)
            : "—";
        var damageDealtValue = window.DamageDealt.Hundredths <= 0
            ? "0"
            : PrimaryPerformancePresentation.FormatCombatDamageTotal(window.DamageDealt);

        return new RollingCombatHeroPresentation
        {
            WindowLabel = windowLabel,
            RateValue = rateValue,
            DamageDealtValue = damageDealtValue,
            Availability = window.Availability,
            Delta = BuildDelta(
                window.DamagePerSecondHundredths,
                combat.SessionDamagePerSecondHundredths,
                sessionElapsed,
                selectedWindowMinutes)
        };
    }

    public static RollingCombatDeltaPresentation BuildDelta(
        long rollingDamagePerSecondHundredths,
        long sessionDamagePerSecondHundredths,
        TimeSpan sessionElapsed,
        int windowMinutes)
    {
        var suppressionThreshold = TimeSpan.FromMinutes(windowMinutes * 2L);
        if (sessionElapsed < suppressionThreshold)
        {
            return RollingCombatDeltaPresentation.Unavailable;
        }

        if (rollingDamagePerSecondHundredths <= 0 && sessionDamagePerSecondHundredths <= 0)
        {
            return RollingCombatDeltaPresentation.Neutral("0");
        }

        var rollingDps = rollingDamagePerSecondHundredths / 100.0;
        var sessionDps = sessionDamagePerSecondHundredths / 100.0;
        var absoluteDeltaHundredths = rollingDamagePerSecondHundredths - sessionDamagePerSecondHundredths;
        var deadband = Math.Max(sessionDps * DeltaDeadbandFraction, 0.01);
        if (Math.Abs(rollingDps - sessionDps) <= deadband)
        {
            return RollingCombatDeltaPresentation.Neutral(FormatDeltaMagnitude(absoluteDeltaHundredths));
        }

        if (absoluteDeltaHundredths > 0)
        {
            return RollingCombatDeltaPresentation.Positive(FormatDeltaMagnitude(absoluteDeltaHundredths));
        }

        return RollingCombatDeltaPresentation.Negative(FormatDeltaMagnitude(absoluteDeltaHundredths));
    }

    public static RollingCombatHeroPresentation BuildUnavailableHero(int selectedWindowMinutes) =>
        new()
        {
            WindowLabel = $"{selectedWindowMinutes} min",
            RateValue = "—",
            DamageDealtValue = "—",
            Detail = "No active gameplay session.",
            Delta = RollingCombatDeltaPresentation.Unavailable
        };

    internal static string FormatDeltaMagnitude(long deltaHundredths)
    {
        var magnitudeHundredths = Math.Abs(deltaHundredths);
        if (magnitudeHundredths <= 0)
        {
            return "0";
        }

        return PrimaryPerformancePresentation.FormatDamagePerSecond(magnitudeHundredths)
            .Replace(" DPS", string.Empty, StringComparison.Ordinal);
    }
}

public sealed record RollingCombatHeroPresentation
{
    public string WindowLabel { get; init; } = "10 min";

    public string RateValue { get; init; } = "—";

    public string DamageDealtValue { get; init; } = "—";

    public RollingCombatAvailability Availability { get; init; } =
        RollingCombatAvailability.NoCombatData;

    public string? Detail { get; init; }

    public RollingCombatDeltaPresentation Delta { get; init; } = RollingCombatDeltaPresentation.Unavailable;
}

public sealed record RollingCombatDeltaPresentation
{
    public static RollingCombatDeltaPresentation Unavailable { get; } = new()
    {
        IsUnavailable = true,
        Label = "—"
    };

    public bool IsUnavailable { get; init; }

    public bool IsPositive { get; init; }

    public bool IsNegative { get; init; }

    public bool IsNeutral { get; init; }

    public string Label { get; init; } = "—";

    public static RollingCombatDeltaPresentation Positive(string magnitude) => new()
    {
        IsPositive = true,
        Label = $"▲ +{magnitude}"
    };

    public static RollingCombatDeltaPresentation Negative(string magnitude) => new()
    {
        IsNegative = true,
        Label = $"▼ {magnitude}"
    };

    public static RollingCombatDeltaPresentation Neutral(string magnitude) => new()
    {
        IsNeutral = true,
        Label = magnitude
    };
}
