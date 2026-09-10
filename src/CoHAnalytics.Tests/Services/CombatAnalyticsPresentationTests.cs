using CoHAnalytics.Models;
using CoHAnalytics.Navigation;
using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Services;

public sealed class CombatAnalyticsPresentationTests
{
    private static readonly DateTimeOffset ReferenceAt =
        new(2026, 8, 9, 12, 2, 0, TimeSpan.Zero);

    [Fact]
    public void BuildStatus_active_uses_domain_combat_state()
    {
        var combat = new CombatSnapshot
        {
            IsInCombat = true,
            LastCombatAt = ReferenceAt.AddSeconds(-5),
            CurrentEngagementDuration = TimeSpan.FromSeconds(42)
        };

        var status = CombatAnalyticsPresentation.BuildStatus(combat, ReferenceAt);

        Assert.Equal("ACTIVE", status.Headline);
        Assert.Equal("Engaged 00:00:42", status.Detail);
    }

    [Fact]
    public void BuildStatus_idle_uses_last_combat_timestamp()
    {
        var combat = new CombatSnapshot
        {
            IsInCombat = false,
            LastCombatAt = ReferenceAt.AddSeconds(-72)
        };

        var status = CombatAnalyticsPresentation.BuildStatus(combat, ReferenceAt);

        Assert.Equal("Idle", status.Headline);
        Assert.Equal("Last combat 00:01:12 ago", status.Detail);
    }

    [Fact]
    public void BuildMetrics_uses_authoritative_session_and_tracked_values()
    {
        var startedAt = ReferenceAt.AddMinutes(-2);
        var context = new LiveMonitoringContextIdentityReadModel
        {
            ContextId = MonitoringContextId.CreateNew(),
            ContextState = MonitoringContextState.Ready,
            CharacterIdentityConfidence = CharacterIdentityConfidence.Confirmed,
            CharacterIdentityResolutionState = CharacterIdentityResolutionState.Resolved,
            SessionLifecycleState = GameplaySessionLifecycleState.Active,
            HasActiveSession = true,
            IdentityStatusLabel = "Confirmed",
            IdentityDetail = "Active character: Example Hero",
            SessionStartedAt = startedAt,
            RetainedCombatEventCount = 2,
            Combat = new CombatSnapshot
            {
                DamageDealt = new CombatScaledAmount(8_000 * CombatScaledAmount.Scale),
                LastCombatAt = startedAt.AddMinutes(1),
                SessionDamagePerSecondHundredths = 6_667,
                Tracked = new TrackedCombatScopeSnapshot
                {
                    IsTracking = true,
                    ActiveElapsed = TimeSpan.FromMinutes(2),
                    DamageDealt = new CombatScaledAmount(1_250 * CombatScaledAmount.Scale),
                    DamagePerSecondHundredths = 1_042
                }
            }
        };

        var metrics = CombatAnalyticsPresentation.BuildMetrics(context, ReferenceAt);

        Assert.Equal(
            PrimaryPerformancePresentation.FormatDamagePerSecond(6_667),
            metrics.Session.RateValue);
        Assert.Equal("8K", metrics.Session.TotalValue);
        Assert.Equal(
            PrimaryPerformancePresentation.FormatDamagePerSecond(1_042),
            metrics.Tracked.RateValue);
        Assert.Equal(
            PrimaryPerformancePresentation.FormatCombatDamageTotal(
                new CombatScaledAmount(1_250 * CombatScaledAmount.Scale)),
            metrics.Tracked.TotalValue);
        Assert.Equal("Running", metrics.TrackedStateLabel);
    }

    [Fact]
    public void BuildMetrics_projects_dps_accuracy_and_enemies_for_all_three_scopes()
    {
        var startedAt = ReferenceAt.AddMinutes(-2);
        var context = new LiveMonitoringContextIdentityReadModel
        {
            ContextId = MonitoringContextId.CreateNew(),
            ContextState = MonitoringContextState.Ready,
            CharacterIdentityConfidence = CharacterIdentityConfidence.Confirmed,
            CharacterIdentityResolutionState = CharacterIdentityResolutionState.Resolved,
            SessionLifecycleState = GameplaySessionLifecycleState.Active,
            HasActiveSession = true,
            IdentityStatusLabel = "Confirmed",
            IdentityDetail = "Active character: Example Hero",
            SessionStartedAt = startedAt,
            RetainedCombatEventCount = 12,
            Combat = new CombatSnapshot
            {
                DamageDealt = new CombatScaledAmount(8_000 * CombatScaledAmount.Scale),
                SessionDamagePerSecondHundredths = 6_667,
                TotalDefeated = 184,
                MyDefeats = 63,
                Accuracy = Accuracy(attempts: 10, hits: 8, misses: 2),
                Tracked = new TrackedCombatScopeSnapshot
                {
                    IsTracking = true,
                    ActiveElapsed = TimeSpan.FromMinutes(2),
                    DamageDealt = new CombatScaledAmount(1_250 * CombatScaledAmount.Scale),
                    DamagePerSecondHundredths = 1_042,
                    TotalDefeated = 42,
                    MyDefeats = 17,
                    Accuracy = Accuracy(attempts: 5, hits: 4, misses: 1)
                },
                Rolling = new RollingCombatScopeSnapshot
                {
                    TenMinutes = new RollingCombatWindowSnapshot
                    {
                        Availability = RollingCombatAvailability.WarmingUp,
                        WindowMinutes = 10,
                        DamageDealt = new CombatScaledAmount(2_000 * CombatScaledAmount.Scale),
                        DamagePerSecondHundredths = 1_667,
                        TotalDefeated = 21,
                        MyDefeats = 8,
                        Accuracy = Accuracy(attempts: 3, hits: 2, misses: 1)
                    }
                }
            }
        };

        var metrics = CombatAnalyticsPresentation.BuildMetrics(context, ReferenceAt);

        Assert.Equal(PrimaryPerformancePresentation.FormatDamagePerSecond(6_667), metrics.Session.RateValue);
        Assert.Equal(PrimaryPerformancePresentation.FormatDamagePerSecond(1_042), metrics.Tracked.RateValue);
        Assert.Equal(PrimaryPerformancePresentation.FormatDamagePerSecond(1_667), metrics.Rolling.RateValue);

        Assert.Equal("80.0%", metrics.SessionAccuracy.Metrics.HitPercentLabel);
        Assert.Equal("10", metrics.SessionAccuracy.Metrics.AttemptsLabel);
        Assert.Equal("8", metrics.SessionAccuracy.Metrics.HitsLabel);
        Assert.Equal("2", metrics.SessionAccuracy.Metrics.MissesLabel);
        Assert.Equal("90.0%", metrics.SessionAccuracy.Metrics.AverageChanceLabel);
        Assert.Equal("50.0", metrics.SessionAccuracy.Metrics.AverageRollLabel);
        Assert.Equal("1", metrics.SessionAccuracy.ForcedHitsLabel);
        Assert.Equal("1", metrics.SessionAccuracy.AutohitsLabel);
        Assert.Equal("80.0%", metrics.TrackedAccuracy.Metrics.HitPercentLabel);
        Assert.Equal("66.7%", metrics.RollingAccuracy.Metrics.HitPercentLabel);

        Assert.Equal("184", metrics.SessionEnemies.TotalDefeatedLabel);
        Assert.Equal("63 (34%)", metrics.SessionEnemies.MyDefeatsLabel);
        Assert.Equal("42", metrics.TrackedEnemies.TotalDefeatedLabel);
        Assert.Equal("17 (40%)", metrics.TrackedEnemies.MyDefeatsLabel);
        Assert.Equal("21", metrics.RollingEnemies.TotalDefeatedLabel);
        Assert.Equal("8 (38%)", metrics.RollingEnemies.MyDefeatsLabel);
    }

    [Fact]
    public void BuildMetrics_keeps_inactive_tracked_scope_structural_and_unavailable()
    {
        var startedAt = ReferenceAt.AddMinutes(-2);
        var context = new LiveMonitoringContextIdentityReadModel
        {
            ContextId = MonitoringContextId.CreateNew(),
            ContextState = MonitoringContextState.Ready,
            CharacterIdentityConfidence = CharacterIdentityConfidence.Confirmed,
            CharacterIdentityResolutionState = CharacterIdentityResolutionState.Resolved,
            SessionLifecycleState = GameplaySessionLifecycleState.Active,
            HasActiveSession = true,
            IdentityStatusLabel = "Confirmed",
            IdentityDetail = "Active character: Example Hero",
            SessionStartedAt = startedAt,
            RetainedCombatEventCount = 1,
            Combat = new CombatSnapshot
            {
                DamageDealt = new CombatScaledAmount(100 * CombatScaledAmount.Scale),
                SessionDamagePerSecondHundredths = 100,
                Tracked = TrackedCombatScopeSnapshot.Empty
            }
        };

        var metrics = CombatAnalyticsPresentation.BuildMetrics(context, ReferenceAt);

        Assert.Equal("Not running", metrics.TrackedStateLabel);
        Assert.Equal("—", metrics.Tracked.RateValue);
        Assert.Equal("—", metrics.TrackedAccuracy.Metrics.HitPercentLabel);
        Assert.Equal("Not running", metrics.TrackedAccuracy.StateLabel);
        Assert.Equal("—", metrics.TrackedEnemies.TotalDefeatedLabel);
        Assert.Equal("Not running", metrics.TrackedEnemies.StateLabel);
    }

    [Fact]
    public void HasSessionCombatData_uses_combat_snapshot_when_retained_event_count_is_stale()
    {
        var startedAt = ReferenceAt.AddMinutes(-2);
        var context = new LiveMonitoringContextIdentityReadModel
        {
            ContextId = MonitoringContextId.CreateNew(),
            ContextState = MonitoringContextState.Ready,
            CharacterIdentityConfidence = CharacterIdentityConfidence.Confirmed,
            CharacterIdentityResolutionState = CharacterIdentityResolutionState.Resolved,
            SessionLifecycleState = GameplaySessionLifecycleState.Active,
            HasActiveSession = true,
            IdentityStatusLabel = "Confirmed",
            IdentityDetail = "Active character: Example Hero",
            SessionStartedAt = startedAt,
            RetainedCombatEventCount = 0,
            Combat = new CombatSnapshot
            {
                DamageDealt = new CombatScaledAmount(500 * CombatScaledAmount.Scale),
                SessionDamagePerSecondHundredths = 833
            }
        };

        Assert.True(CombatAnalyticsPresentation.HasSessionCombatData(context));

        var metrics = CombatAnalyticsPresentation.BuildMetrics(context, ReferenceAt);
        Assert.Equal(
            PrimaryPerformancePresentation.FormatDamagePerSecond(833),
            metrics.Session.RateValue);
    }

    [Fact]
    public void BuildUnavailableMetrics_uses_structural_unavailable_states_without_fake_zeroes()
    {
        var metrics = CombatAnalyticsPresentation.BuildUnavailableMetrics();

        Assert.Equal("No active session", metrics.Status.Headline);
        Assert.Equal("—", metrics.Session.RateValue);
        Assert.Equal("—", metrics.Tracked.RateValue);
        Assert.Equal("10 min", metrics.Rolling.WindowLabel);
        Assert.Equal("—", metrics.Rolling.RateValue);
        Assert.True(metrics.Rolling.Delta.IsUnavailable);
        Assert.False(metrics.Accuracy.ShowMetrics);
        Assert.Equal(CombatAccuracyPresentation.NoSessionDetail, metrics.Accuracy.Detail);
        Assert.Equal("No active session", metrics.SessionAccuracy.StateLabel);
        Assert.Equal("—", metrics.SessionEnemies.TotalDefeatedLabel);
        Assert.Equal("—", metrics.RollingEnemies.MyDefeatsLabel);
    }

    private static CombatAccuracyScopeSnapshot Accuracy(long attempts, long hits, long misses) =>
        new()
        {
            Attempts = attempts,
            Hits = hits,
            Misses = misses,
            RolledAttempts = attempts,
            DisplayedChanceSumHundredths = attempts * 9_000,
            RollSumHundredths = attempts * 5_000,
            ForcedHits = 1,
            Autohits = 1
        };
}
