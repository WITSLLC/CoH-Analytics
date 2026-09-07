using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

/// <summary>Shared session and tracked earnings rate presentation for Live Session and Analytics.</summary>
public static class SessionEarningsPresentation
{
    public static string FormatSessionExperienceRatePerHour(
        long sessionExperienceGained,
        DateTimeOffset sessionStartedAt,
        DateTimeOffset referenceAt,
        DateTimeOffset? timingEndAt) =>
        GameplaySessionTelemetryPresentation.FormatRatePerHour(
            sessionExperienceGained,
            GameplaySessionTelemetryPresentation.GetElapsedDuration(
                sessionStartedAt,
                referenceAt,
                timingEndAt));

    public static string FormatSessionInfluenceRatePerHour(
        long sessionGameplayInfluenceGained,
        DateTimeOffset sessionStartedAt,
        DateTimeOffset referenceAt,
        DateTimeOffset? timingEndAt) =>
        GameplaySessionTelemetryPresentation.FormatRatePerHour(
            sessionGameplayInfluenceGained,
            GameplaySessionTelemetryPresentation.GetElapsedDuration(
                sessionStartedAt,
                referenceAt,
                timingEndAt));

    public static string FormatTrackedExperienceRatePerHour(TrackedEarningsScopeSnapshot tracked) =>
        FormatTrackedRate(tracked.ExperienceGained, tracked);

    public static string FormatTrackedInfluenceRatePerHour(TrackedEarningsScopeSnapshot tracked) =>
        FormatTrackedRate(tracked.InfluenceGained, tracked);

    private static string FormatTrackedRate(long amount, TrackedEarningsScopeSnapshot tracked)
    {
        if (!tracked.IsTracking)
        {
            return "—";
        }

        return GameplaySessionTelemetryPresentation.FormatRatePerHour(
            amount,
            tracked.ActiveElapsed);
    }
}
