using System.Globalization;

namespace CoHAnalytics.Services;

/// <summary>
/// Presentation math for live session XP and gameplay Influence rates and duration.
/// </summary>
public static class GameplaySessionTelemetryPresentation
{
    public static readonly TimeSpan MinimumRateElapsed = TimeSpan.FromMinutes(1);

    public static TimeSpan GetElapsedDuration(
        DateTimeOffset startedAt,
        DateTimeOffset referenceAt,
        DateTimeOffset? timingEndAt)
    {
        var end = timingEndAt ?? referenceAt;
        var elapsed = end - startedAt;
        return elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed;
    }

    public static string FormatDuration(TimeSpan elapsed) =>
        elapsed.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);

    public static bool CanShowRates(TimeSpan elapsed) => elapsed >= MinimumRateElapsed;

    public static double CalculateRatePerHour(long amount, TimeSpan elapsed)
    {
        if (elapsed <= TimeSpan.Zero)
        {
            return 0;
        }

        return amount / elapsed.TotalHours;
    }

    public static string FormatRatePerHour(long amount, TimeSpan elapsed, IFormatProvider? formatProvider = null)
    {
        if (!CanShowRates(elapsed))
        {
            return "—";
        }

        return FormatCompactRate(CalculateRatePerHour(amount, elapsed), formatProvider);
    }

    public static string FormatCompactRate(double ratePerHour, IFormatProvider? formatProvider = null)
    {
        var culture = formatProvider ?? CultureInfo.CurrentCulture;

        if (ratePerHour >= 1_000_000)
        {
            var millions = (long)Math.Round(ratePerHour / 1_000_000.0, MidpointRounding.AwayFromZero);
            return $"{millions.ToString("N0", culture)}M/hr";
        }

        if (ratePerHour >= 1_000)
        {
            var thousands = (long)Math.Round(ratePerHour / 1_000.0, MidpointRounding.AwayFromZero);
            return $"{thousands.ToString("N0", culture)}K/hr";
        }

        return $"{ratePerHour.ToString("N0", culture)}/hr";
    }
}
