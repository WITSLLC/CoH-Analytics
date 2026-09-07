using System.Globalization;
using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

/// <summary>Read-only lifetime character-performance presentation for Analytics → Overview.</summary>
public static class AnalyticsOverviewPresentation
{
    public static AnalyticsOverviewMetricsPresentation BuildNoCharacter() => new();

    public static AnalyticsOverviewMetricsPresentation Build(
        CharacterHistoricalPerformanceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (!snapshot.HasHistory)
        {
            return new AnalyticsOverviewMetricsPresentation
            {
                HasSelectedCharacter = true
            };
        }

        return new AnalyticsOverviewMetricsPresentation
        {
            HasSelectedCharacter = true,
            HasHistory = true,
            HistoricalDpsLabel = PrimaryPerformancePresentation.FormatDamagePerSecond(
                snapshot.DamagePerSecondHundredths ?? 0),
            TotalDamageDealtLabel = PrimaryPerformancePresentation.FormatCombatDamageTotal(
                snapshot.DamageDealt),
            TotalDefeatedLabel = CombatAccuracyPresentation.FormatCount(snapshot.TotalDefeated),
            MyDefeatsLabel = CombatAccuracyPresentation.FormatCount(snapshot.MyDefeats),
            HitPercentLabel = FormatPercent(snapshot.HitPercent),
            AttemptsLabel = CombatAccuracyPresentation.FormatCount(snapshot.Attempts),
            HitsLabel = CombatAccuracyPresentation.FormatCount(snapshot.Hits),
            MissesLabel = CombatAccuracyPresentation.FormatCount(snapshot.Misses),
            AverageChanceLabel = FormatPercent(snapshot.AverageDisplayedChance),
            AverageRollLabel = FormatRoll(snapshot.AverageRoll),
            ForcedHitsLabel = CombatAccuracyPresentation.FormatCount(snapshot.ForcedHits),
            AutohitsLabel = CombatAccuracyPresentation.FormatCount(snapshot.Autohits),
            ExperiencePerHourLabel = FormatRate(snapshot.ExperiencePerHour),
            TotalExperienceLabel = CombatAccuracyPresentation.FormatCount(snapshot.ExperienceGained),
            InfluencePerHourLabel = FormatRate(snapshot.GameplayInfluencePerHour),
            TotalGameplayInfluenceLabel = CombatAccuracyPresentation.FormatCount(
                snapshot.GameplayInfluenceGained),
            ObservationCountLabel = snapshot.ObservationCount.ToString(
                "N0",
                CultureInfo.InvariantCulture),
            ObservedDurationLabel = FormatObservedDuration(snapshot.ObservedDuration)
        };
    }

    internal static string FormatObservedDuration(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
        {
            duration = TimeSpan.Zero;
        }

        return duration.Days > 0
            ? $"{duration.Days.ToString("N0", CultureInfo.InvariantCulture)}d "
              + duration.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture)
            : GameplaySessionTelemetryPresentation.FormatDuration(duration);
    }

    private static string FormatRate(double? rate) =>
        rate is null
            ? "—"
            : GameplaySessionTelemetryPresentation.FormatCompactRate(rate.Value);

    private static string FormatPercent(decimal? percent) =>
        percent is null
            ? "—"
            : CombatAccuracyPresentation.FormatPercentTenths(
                checked((long)(percent.Value * 10m)));

    private static string FormatRoll(decimal? roll) =>
        roll is null
            ? "—"
            : CombatAccuracyPresentation.FormatRollTenths(
                checked((long)(roll.Value * 10m)));
}

public sealed record AnalyticsOverviewMetricsPresentation
{
    public bool HasSelectedCharacter { get; init; }

    public bool HasHistory { get; init; }

    public bool ShowNoCharacter => !HasSelectedCharacter;

    public bool ShowNoHistory => HasSelectedCharacter && !HasHistory;

    public bool ShowHistoricalMetrics => HasSelectedCharacter && HasHistory;

    public string HistoricalDpsLabel { get; init; } = "—";

    public string TotalDamageDealtLabel { get; init; } = "—";

    public string TotalDefeatedLabel { get; init; } = "—";

    public string MyDefeatsLabel { get; init; } = "—";

    public string HitPercentLabel { get; init; } = "—";

    public string AttemptsLabel { get; init; } = "—";

    public string HitsLabel { get; init; } = "—";

    public string MissesLabel { get; init; } = "—";

    public string AverageChanceLabel { get; init; } = "—";

    public string AverageRollLabel { get; init; } = "—";

    public string ForcedHitsLabel { get; init; } = "—";

    public string AutohitsLabel { get; init; } = "—";

    public string ExperiencePerHourLabel { get; init; } = "—";

    public string TotalExperienceLabel { get; init; } = "—";

    public string InfluencePerHourLabel { get; init; } = "—";

    public string TotalGameplayInfluenceLabel { get; init; } = "—";

    public string ObservationCountLabel { get; init; } = "—";

    public string ObservedDurationLabel { get; init; } = "—";
}
