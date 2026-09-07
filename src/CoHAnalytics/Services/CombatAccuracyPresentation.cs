using System.Globalization;
using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

/// <summary>Read-only attack-resolution presentation for Analytics → Combat.</summary>
public static class CombatAccuracyPresentation
{
    public const string NotObservedDetail =
        "Detailed hit-roll data has not been observed for this session. " +
        "Enable detailed combat logging in City of Heroes to see accuracy analytics.";

    public const string NoSessionDetail =
        "Detailed hit-roll data will appear when combat telemetry is available.";

    public static CombatAccuracyCardPresentation BuildNoSession() =>
        new()
        {
            Availability = CombatAccuracyAvailability.NoSession,
            Detail = NoSessionDetail
        };

    public static CombatAccuracyCardPresentation Build(CombatAccuracyScopeSnapshot accuracy) =>
        accuracy.HasAttempts
            ? BuildAvailable(accuracy)
            : new CombatAccuracyCardPresentation
            {
                Availability = CombatAccuracyAvailability.NotObserved,
                Detail = NotObservedDetail
            };

    /// <summary>Summary hit percentage for Analytics → Overview (no diagnostic detail).</summary>
    public static string BuildSummaryLabel(CombatAccuracyCardPresentation accuracy) =>
        accuracy.Availability == CombatAccuracyAvailability.Available
            ? accuracy.HitPercentLabel
            : "—";

    private static CombatAccuracyCardPresentation BuildAvailable(CombatAccuracyScopeSnapshot accuracy)
    {
        var hitPercentLabel = FormatPercentTenths(
            CalculatePercentTenths(accuracy.Hits, accuracy.Attempts));

        return new CombatAccuracyCardPresentation
        {
            Availability = CombatAccuracyAvailability.Available,
            AttemptsLabel = FormatCount(accuracy.Attempts),
            HitsLabel = FormatCount(accuracy.Hits),
            MissesLabel = FormatCount(accuracy.Misses),
            HitPercentLabel = hitPercentLabel,
            AverageChanceLabel = accuracy.HasRolledAttempts
                ? FormatAverageResolutionPercent(accuracy.DisplayedChanceSumHundredths, accuracy.RolledAttempts)
                : "—",
            AverageRollLabel = accuracy.HasRolledAttempts
                ? FormatAverageResolutionRoll(accuracy.RollSumHundredths, accuracy.RolledAttempts)
                : "—",
            ForcedHitsLabel = accuracy.ForcedHits > 0
                ? FormatCount(accuracy.ForcedHits)
                : null,
            AutohitsLabel = accuracy.Autohits > 0
                ? FormatCount(accuracy.Autohits)
                : null
        };
    }

    internal static long CalculatePercentTenths(long numerator, long denominator)
    {
        if (denominator <= 0)
        {
            return 0;
        }

        return checked((long)decimal.Truncate(
            ((decimal)numerator * 1_000 + denominator / 2) / denominator));
    }

    internal static long CalculateAverageTenths(long sumHundredths, long count)
    {
        if (count <= 0)
        {
            return 0;
        }

        var averageHundredths = decimal.Truncate(
            ((decimal)sumHundredths + count / 2) / count);
        return checked((long)decimal.Truncate((averageHundredths + 5) / 10));
    }

    internal static string FormatCount(long value) =>
        value.ToString("N0", CultureInfo.InvariantCulture);

    internal static string FormatPercentTenths(long tenths) =>
        (tenths / 10m).ToString("0.0", CultureInfo.InvariantCulture) + "%";

    internal static string FormatRollTenths(long tenths) =>
        (tenths / 10m).ToString("0.0", CultureInfo.InvariantCulture);

    internal static string FormatAverageResolutionPercent(long sumHundredths, long count)
        => FormatPercentTenths(CalculateAverageTenths(sumHundredths, count));

    internal static string FormatAverageResolutionRoll(long sumHundredths, long count)
        => FormatRollTenths(CalculateAverageTenths(sumHundredths, count));
}

public enum CombatAccuracyAvailability
{
    NoSession,
    NotObserved,
    Available
}

public sealed record CombatAccuracyCardPresentation
{
    public CombatAccuracyAvailability Availability { get; init; } =
        CombatAccuracyAvailability.NotObserved;

    public string? Detail { get; init; }

    public string AttemptsLabel { get; init; } = "—";

    public string HitsLabel { get; init; } = "—";

    public string MissesLabel { get; init; } = "—";

    public string HitPercentLabel { get; init; } = "—";

    public string AverageChanceLabel { get; init; } = "—";

    public string AverageRollLabel { get; init; } = "—";

    public string? ForcedHitsLabel { get; init; }

    public string? AutohitsLabel { get; init; }

    public bool ShowMetrics => Availability == CombatAccuracyAvailability.Available;

    public bool ShowSecondaryMetrics =>
        ForcedHitsLabel is not null || AutohitsLabel is not null;
}
