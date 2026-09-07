using System.Globalization;
using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

/// <summary>Read-only combat presentation for Analytics surfaces.</summary>
public static class CombatAnalyticsPresentation
{
    public static bool HasSessionCombatData(LiveMonitoringContextIdentityReadModel context)
    {
        if (context.RetainedCombatEventCount > 0 || context.Combat.LastCombatAt is not null)
        {
            return true;
        }

        var combat = context.Combat;
        return combat.DamageDealt.Hundredths > 0
            || combat.SessionDamagePerSecondHundredths > 0
            || combat.IsInCombat;
    }

    public static CombatStatusPresentation BuildStatus(
        CombatSnapshot combat,
        DateTimeOffset referenceAt)
    {
        if (combat.IsInCombat)
        {
            return new CombatStatusPresentation(
                "ACTIVE",
                $"Engaged {GameplaySessionTelemetryPresentation.FormatDuration(combat.CurrentEngagementDuration)}");
        }

        if (combat.LastCombatAt is not null)
        {
            var elapsed = referenceAt - combat.LastCombatAt.Value;
            if (elapsed < TimeSpan.Zero)
            {
                elapsed = TimeSpan.Zero;
            }

            return new CombatStatusPresentation(
                "Idle",
                $"Last combat {GameplaySessionTelemetryPresentation.FormatDuration(elapsed)} ago");
        }

        return new CombatStatusPresentation("Idle", "No recent combat activity");
    }

    public static AnalyticsCombatMetricsPresentation BuildMetrics(
        LiveMonitoringContextIdentityReadModel context,
        DateTimeOffset referenceAt,
        int rollingWindowMinutes = RollingCombatPresets.DefaultMinutes)
    {
        var elapsed = GameplaySessionTelemetryPresentation.GetElapsedDuration(
            context.SessionStartedAt ?? referenceAt,
            referenceAt,
            context.SessionTimingEndAt);

        return new AnalyticsCombatMetricsPresentation
        {
            Status = BuildStatus(context.Combat, referenceAt),
            Session = PrimaryPerformancePresentation.BuildSession(
                context.Combat,
                elapsed,
                HasSessionCombatData(context)),
            Tracked = PrimaryPerformancePresentation.BuildTracked(context.Combat.Tracked),
            TrackedStateLabel = BuildTrackedStateLabel(context.Combat.Tracked),
            Rolling = RollingCombatPresentation.BuildHero(
                context.Combat,
                elapsed,
                rollingWindowMinutes),
            SessionAccuracy = BuildAccuracyScope(context.Combat.Accuracy, "Not observed"),
            TrackedAccuracy = context.Combat.Tracked.IsTracking
                ? BuildAccuracyScope(context.Combat.Tracked.Accuracy, "Not observed")
                : CombatAccuracyScopePresentation.Unavailable("Not running"),
            RollingAccuracy = BuildRollingAccuracyScope(
                context.Combat.Rolling.GetPreset(rollingWindowMinutes)),
            SessionEnemies = HasSessionCombatData(context)
                ? CombatEnemyScopePresentation.Available(
                    context.Combat.TotalDefeated,
                    context.Combat.MyDefeats)
                : CombatEnemyScopePresentation.Unavailable("No combat data"),
            TrackedEnemies = context.Combat.Tracked.IsTracking
                ? CombatEnemyScopePresentation.Available(
                    context.Combat.Tracked.TotalDefeated,
                    context.Combat.Tracked.MyDefeats)
                : CombatEnemyScopePresentation.Unavailable("Not running"),
            RollingEnemies = BuildRollingEnemyScope(
                context.Combat.Rolling.GetPreset(rollingWindowMinutes))
        };
    }

    public static AnalyticsCombatMetricsPresentation BuildUnavailableMetrics(
        int rollingWindowMinutes = RollingCombatPresets.DefaultMinutes) =>
        new()
        {
            Status = new CombatStatusPresentation("No active session", "Select a monitored gameplay session."),
            Session = PrimaryPerformanceMetric.BetaDamage with
            {
                RateValue = "—",
                TotalValue = "—"
            },
            Tracked = PrimaryPerformanceMetric.BetaDamage with
            {
                RateValue = "—",
                TotalValue = "—"
            },
            TrackedStateLabel = "Not running",
            Rolling = RollingCombatPresentation.BuildUnavailableHero(rollingWindowMinutes),
            SessionAccuracy = new CombatAccuracyScopePresentation
            {
                Metrics = CombatAccuracyPresentation.BuildNoSession(),
                StateLabel = "No active session"
            },
            TrackedAccuracy = CombatAccuracyScopePresentation.Unavailable("Not running"),
            RollingAccuracy = CombatAccuracyScopePresentation.Unavailable("No active session"),
            SessionEnemies = CombatEnemyScopePresentation.Unavailable("No active session"),
            TrackedEnemies = CombatEnemyScopePresentation.Unavailable("Not running"),
            RollingEnemies = CombatEnemyScopePresentation.Unavailable("No active session")
        };

    private static CombatAccuracyScopePresentation BuildAccuracyScope(
        CombatAccuracyScopeSnapshot accuracy,
        string unavailableState)
    {
        var metrics = CombatAccuracyPresentation.Build(accuracy);
        return new CombatAccuracyScopePresentation
        {
            Metrics = metrics,
            StateLabel = metrics.ShowMetrics ? null : unavailableState
        };
    }

    private static CombatAccuracyScopePresentation BuildRollingAccuracyScope(
        RollingCombatWindowSnapshot rolling)
    {
        if (!CanShowRollingValues(rolling.Availability))
        {
            return CombatAccuracyScopePresentation.Unavailable(
                rolling.Availability == RollingCombatAvailability.NoCombatData
                    ? "Not observed"
                    : "Unavailable");
        }

        return BuildAccuracyScope(rolling.Accuracy, "Not observed");
    }

    private static CombatEnemyScopePresentation BuildRollingEnemyScope(
        RollingCombatWindowSnapshot rolling) =>
        CanShowRollingValues(rolling.Availability)
            ? CombatEnemyScopePresentation.Available(rolling.TotalDefeated, rolling.MyDefeats)
            : CombatEnemyScopePresentation.Unavailable(
                rolling.Availability == RollingCombatAvailability.NoCombatData
                    ? "No combat data"
                    : "Unavailable");

    private static bool CanShowRollingValues(RollingCombatAvailability availability) =>
        availability is RollingCombatAvailability.WarmingUp
            or RollingCombatAvailability.Available;

    public static string BuildTrackedStateLabel(TrackedCombatScopeSnapshot tracked)
    {
        if (!tracked.IsTracking)
        {
            return "Not running";
        }

        return tracked.IsPaused ? "Paused" : "Running";
    }
}

public sealed record CombatStatusPresentation(string Headline, string Detail);

public sealed record AnalyticsCombatMetricsPresentation
{
    public CombatStatusPresentation Status { get; init; } =
        new("Idle", "No recent combat activity");

    public PrimaryPerformanceMetric Session { get; init; } = PrimaryPerformanceMetric.BetaDamage;

    public PrimaryPerformanceMetric Tracked { get; init; } = PrimaryPerformanceMetric.BetaDamage;

    public string TrackedStateLabel { get; init; } = "Not running";

    public RollingCombatHeroPresentation Rolling { get; init; } = new();

    public CombatAccuracyScopePresentation SessionAccuracy { get; init; } = new();

    public CombatAccuracyScopePresentation TrackedAccuracy { get; init; } = new();

    public CombatAccuracyScopePresentation RollingAccuracy { get; init; } = new();

    public CombatEnemyScopePresentation SessionEnemies { get; init; } = new();

    public CombatEnemyScopePresentation TrackedEnemies { get; init; } = new();

    public CombatEnemyScopePresentation RollingEnemies { get; init; } = new();

    /// <summary>Compatibility alias for the session accuracy used by Analytics Overview.</summary>
    public CombatAccuracyCardPresentation Accuracy => SessionAccuracy.Metrics;
}

public sealed record CombatAccuracyScopePresentation
{
    public CombatAccuracyCardPresentation Metrics { get; init; } = new();

    public string? StateLabel { get; init; }

    public string ForcedHitsLabel => Metrics.ForcedHitsLabel ?? "—";

    public string AutohitsLabel => Metrics.AutohitsLabel ?? "—";

    public static CombatAccuracyScopePresentation Unavailable(string stateLabel) =>
        new() { StateLabel = stateLabel };
}

public sealed record CombatEnemyScopePresentation
{
    public string TotalDefeatedLabel { get; init; } = "—";

    public string MyDefeatsLabel { get; init; } = "—";

    public string? StateLabel { get; init; }

    public static CombatEnemyScopePresentation Available(long totalDefeated, long myDefeats) =>
        new()
        {
            TotalDefeatedLabel = totalDefeated.ToString("N0", CultureInfo.InvariantCulture),
            MyDefeatsLabel = myDefeats.ToString("N0", CultureInfo.InvariantCulture)
        };

    public static CombatEnemyScopePresentation Unavailable(string stateLabel) =>
        new() { StateLabel = stateLabel };
}
