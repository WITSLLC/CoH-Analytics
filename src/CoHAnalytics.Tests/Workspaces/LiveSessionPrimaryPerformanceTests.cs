using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using CoHAnalytics.Models;
using CoHAnalytics.Services;
using CoHAnalytics.Tests.Services;
using CoHAnalytics.ViewModels.Workspaces;

namespace CoHAnalytics.Tests.Workspaces;

[Collection(WpfDispatcherCollection.Name)]
public sealed class LiveSessionPrimaryPerformanceTests
{
    private static readonly DateTimeOffset StartedAt =
        new(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Session_combat_row_matches_domain_snapshot_values()
    {
        var contextId = MonitoringContextId.CreateNew();
        var sessionStartedAt = DateTimeOffset.UtcNow.AddMinutes(-2);
        var elapsed = TimeSpan.FromMinutes(2);
        var damageDealt = new CombatScaledAmount(120_000 * CombatScaledAmount.Scale);
        var combat = new CombatSnapshot
        {
            DamageDealt = damageDealt,
            LastCombatAt = sessionStartedAt.AddMinutes(1),
            SessionDamagePerSecondHundredths =
                CombatAggregator.CalculateSessionDamagePerSecondHundredths(damageDealt, elapsed)
        };
        var identity = new FakeIdentityReadService
        {
            Current = Snapshot(
                CreateContext(contextId, experience: 1_000, influence: 100, combat: combat, eventCount: 3),
                sessionStartedAt)
        };

        using var viewModel = TestGameplaySessionContextSupport.CreateLiveSessionViewModel(identity);
        DrainDispatcher();

        var expected = PrimaryPerformancePresentation.BuildSession(combat, elapsed, hasCombatData: true);
        Assert.Equal(expected.RateValue, viewModel.SessionPrimaryPerformanceRateLabel);
        Assert.Equal(expected.TotalValue, viewModel.SessionPrimaryPerformanceTotalLabel);
        Assert.Equal(
            combat.SessionDamagePerSecondHundredths,
            ParseDisplayedDpsHundredths(viewModel.SessionPrimaryPerformanceRateLabel));
    }

    [Fact]
    public void Session_warm_up_and_no_combat_states_render_expected_labels()
    {
        var contextId = MonitoringContextId.CreateNew();
        var sessionStartedAt = DateTimeOffset.UtcNow.AddSeconds(-30);
        var identity = new FakeIdentityReadService
        {
            Current = Snapshot(
                CreateContext(contextId, experience: 100, influence: 10),
                sessionStartedAt)
        };

        using var viewModel = TestGameplaySessionContextSupport.CreateLiveSessionViewModel(identity);
        DrainDispatcher();

        Assert.Equal("—", viewModel.SessionPrimaryPerformanceRateLabel);
        Assert.Equal("—", viewModel.SessionPrimaryPerformanceTotalLabel);

        var combat = new CombatSnapshot
        {
            DamageDealt = new CombatScaledAmount(500 * CombatScaledAmount.Scale),
            LastCombatAt = sessionStartedAt.AddSeconds(10),
            SessionDamagePerSecondHundredths = 0
        };
        identity.Current = Snapshot(
            CreateContext(contextId, experience: 100, influence: 10, combat: combat, eventCount: 1),
            sessionStartedAt);
        identity.RaiseChanged();
        DrainDispatcher();

        Assert.Equal("—", viewModel.SessionPrimaryPerformanceRateLabel);
        Assert.Equal("500", viewModel.SessionPrimaryPerformanceTotalLabel);
    }

    [Fact]
    public void Tracked_running_and_inactive_states_follow_domain_snapshot()
    {
        var contextId = MonitoringContextId.CreateNew();
        var sessionStartedAt = DateTimeOffset.UtcNow.AddMinutes(-2);
        var identity = new FakeIdentityReadService
        {
            Current = Snapshot(
                CreateContext(contextId, experience: 1_000, influence: 100),
                sessionStartedAt)
        };

        using var viewModel = TestGameplaySessionContextSupport.CreateLiveSessionViewModel(identity);
        DrainDispatcher();

        Assert.Equal("—", viewModel.TrackedSessionPrimaryPerformanceRateLabel);
        Assert.Equal("—", viewModel.TrackedSessionPrimaryPerformanceTotalLabel);

        viewModel.StartTrackedSessionCommand.Execute(null);
        DrainDispatcher();

        var trackedCombat = new TrackedCombatScopeSnapshot
        {
            IsTracking = true,
            ActiveElapsed = TimeSpan.FromMinutes(2),
            DamageDealt = new CombatScaledAmount(1_250 * CombatScaledAmount.Scale),
            DamagePerSecondHundredths = 1_042
        };
        identity.Current = Snapshot(
            CreateContext(
                contextId,
                experience: 1_000,
                influence: 100,
                combat: new CombatSnapshot { Tracked = trackedCombat }),
            sessionStartedAt);
        identity.RaiseChanged();
        DrainDispatcher();

        var expected = PrimaryPerformancePresentation.BuildTracked(trackedCombat);
        Assert.Equal(expected.RateValue, viewModel.TrackedSessionPrimaryPerformanceRateLabel);
        Assert.Equal(expected.TotalValue, viewModel.TrackedSessionPrimaryPerformanceTotalLabel);
        Assert.Equal(
            expected.RateValue,
            PrimaryPerformancePresentation.FormatDamagePerSecond(trackedCombat.DamagePerSecondHundredths));
    }

    [Fact]
    public void Tracked_pause_freezes_combat_row_while_session_row_continues()
    {
        var contextId = MonitoringContextId.CreateNew();
        var sessionStartedAt = DateTimeOffset.UtcNow.AddMinutes(-2);
        var runningTracked = new TrackedCombatScopeSnapshot
        {
            IsTracking = true,
            ActiveElapsed = TimeSpan.FromMinutes(2),
            DamageDealt = new CombatScaledAmount(250 * CombatScaledAmount.Scale),
            DamagePerSecondHundredths = 208
        };
        var sessionCombat = new CombatSnapshot
        {
            DamageDealt = new CombatScaledAmount(500 * CombatScaledAmount.Scale),
            LastCombatAt = sessionStartedAt.AddSeconds(10),
            SessionDamagePerSecondHundredths =
                CombatAggregator.CalculateSessionDamagePerSecondHundredths(
                    new CombatScaledAmount(500 * CombatScaledAmount.Scale),
                    TimeSpan.FromMinutes(2)),
            Tracked = runningTracked
        };
        var identity = new FakeIdentityReadService
        {
            Current = Snapshot(
                CreateContext(contextId, experience: 1_000, influence: 100, combat: sessionCombat, eventCount: 2),
                sessionStartedAt)
        };

        using var viewModel = TestGameplaySessionContextSupport.CreateLiveSessionViewModel(identity);
        DrainDispatcher();
        viewModel.StartTrackedSessionCommand.Execute(null);
        identity.RaiseChanged();
        DrainDispatcher();

        var pausedTrackedRate = viewModel.TrackedSessionPrimaryPerformanceRateLabel;
        var pausedTrackedTotal = viewModel.TrackedSessionPrimaryPerformanceTotalLabel;
        Assert.Equal("2 DPS", pausedTrackedRate);
        Assert.Equal("250", pausedTrackedTotal);

        viewModel.ToggleTrackedSessionPauseCommand.Execute(null);
        DrainDispatcher();

        var frozenTracked = runningTracked with { IsPaused = true };
        var updatedCombat = new CombatSnapshot
        {
            DamageDealt = new CombatScaledAmount(9_000 * CombatScaledAmount.Scale),
            LastCombatAt = sessionStartedAt.AddMinutes(1),
            SessionDamagePerSecondHundredths =
                CombatAggregator.CalculateSessionDamagePerSecondHundredths(
                    new CombatScaledAmount(9_000 * CombatScaledAmount.Scale),
                    TimeSpan.FromMinutes(2)),
            Tracked = frozenTracked
        };
        identity.Current = Snapshot(
            CreateContext(contextId, experience: 5_000, influence: 500, combat: updatedCombat, eventCount: 8),
            sessionStartedAt);
        identity.RaiseChanged();
        DrainDispatcher();

        Assert.Equal(pausedTrackedRate, viewModel.TrackedSessionPrimaryPerformanceRateLabel);
        Assert.Equal(pausedTrackedTotal, viewModel.TrackedSessionPrimaryPerformanceTotalLabel);
        Assert.NotEqual("—", viewModel.SessionPrimaryPerformanceRateLabel);
        Assert.Equal("9K", viewModel.SessionPrimaryPerformanceTotalLabel);
    }

    [Fact]
    public void Context_selection_projects_combat_values_from_selected_context()
    {
        var contextA = MonitoringContextId.CreateNew();
        var contextB = MonitoringContextId.CreateNew();
        var recordA = CharacterRecordId.CreateNew();
        var recordB = CharacterRecordId.CreateNew();
        var sessionStartedAt = DateTimeOffset.UtcNow.AddMinutes(-2);
        var combatA = new CombatSnapshot
        {
            DamageDealt = new CombatScaledAmount(1_000 * CombatScaledAmount.Scale),
            LastCombatAt = sessionStartedAt,
            SessionDamagePerSecondHundredths =
                CombatAggregator.CalculateSessionDamagePerSecondHundredths(
                    new CombatScaledAmount(1_000 * CombatScaledAmount.Scale),
                    TimeSpan.FromMinutes(2))
        };
        var combatB = new CombatSnapshot
        {
            DamageDealt = new CombatScaledAmount(8_000 * CombatScaledAmount.Scale),
            LastCombatAt = sessionStartedAt,
            SessionDamagePerSecondHundredths =
                CombatAggregator.CalculateSessionDamagePerSecondHundredths(
                    new CombatScaledAmount(8_000 * CombatScaledAmount.Scale),
                    TimeSpan.FromMinutes(2))
        };
        var identity = new FakeIdentityReadService
        {
            Current = Snapshot(
                [
                    CreateContext(
                        contextA,
                        recordA,
                        "Hero A",
                        experience: 2_500,
                        influence: 250,
                        combat: combatA,
                        eventCount: 1),
                    CreateContext(
                        contextB,
                        recordB,
                        "Hero B",
                        experience: 8_000,
                        influence: 800,
                        combat: combatB,
                        eventCount: 1)
                ],
                sessionStartedAt)
        };

        using var viewModel = TestGameplaySessionContextSupport.CreateLiveSessionViewModel(
            identity,
            TestGameplaySessionContextSupport.FollowingLive(contextB));
        DrainDispatcher();

        var expectedB = PrimaryPerformancePresentation.BuildSession(
            combatB,
            TimeSpan.FromMinutes(2),
            hasCombatData: true);
        Assert.Equal(Format(8_000), viewModel.SessionExperienceLabel);
        Assert.Equal(Format(800), viewModel.SessionGameplayInfluenceLabel);
        Assert.Equal(expectedB.RateValue, viewModel.SessionPrimaryPerformanceRateLabel);
        Assert.Equal(expectedB.TotalValue, viewModel.SessionPrimaryPerformanceTotalLabel);
    }

    [Fact]
    public void Descriptor_captions_expose_beta_damage_labels()
    {
        using var viewModel = TestGameplaySessionContextSupport.CreateLiveSessionViewModel(
            new FakeIdentityReadService());
        DrainDispatcher();

        Assert.Equal("DPS", viewModel.SessionPrimaryPerformanceRateCaption);
        Assert.Equal("Damage Dealt", viewModel.SessionPrimaryPerformanceTotalCaption);
        Assert.Equal("DPS", viewModel.TrackedSessionPrimaryPerformanceRateCaption);
        Assert.Equal("Damage Dealt", viewModel.TrackedSessionPrimaryPerformanceTotalCaption);
    }

    [Fact]
    public void Clear_session_resets_live_combat_presentation_without_affecting_analytics()
    {
        var contextId = MonitoringContextId.CreateNew();
        var sessionStartedAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        var authoritativeDamage = new CombatScaledAmount(1_000 * CombatScaledAmount.Scale);
        var combat = new CombatSnapshot
        {
            DamageDealt = authoritativeDamage,
            LastCombatAt = sessionStartedAt.AddMinutes(4),
            SessionDamagePerSecondHundredths =
                CombatAggregator.CalculateSessionDamagePerSecondHundredths(
                    authoritativeDamage,
                    TimeSpan.FromMinutes(5))
        };
        var identity = new FakeIdentityReadService
        {
            Current = Snapshot(
                CreateContext(contextId, experience: 5_000, influence: 500, combat: combat, eventCount: 4),
                sessionStartedAt)
        };

        using var liveSession = TestGameplaySessionContextSupport.CreateLiveSessionViewModel(
            identity,
            TestGameplaySessionContextSupport.FollowingLive(contextId));
        using var analytics = TestGameplaySessionContextSupport.CreateAnalyticsViewModel(
            identity,
            TestGameplaySessionContextSupport.FollowingLive(contextId));
        DrainDispatcher();

        var analyticsBeforeClear = CombatAnalyticsPresentation.BuildMetrics(
            identity.Current.Contexts.Single(),
            DateTimeOffset.UtcNow);
        Assert.NotEqual("—", liveSession.SessionPrimaryPerformanceRateLabel);
        Assert.NotEqual("0", liveSession.SessionPrimaryPerformanceTotalLabel);

        liveSession.ClearSessionCommand.Execute(null);
        DrainDispatcher();

        Assert.Equal("0", liveSession.SessionPrimaryPerformanceTotalLabel);
        Assert.Equal("—", liveSession.SessionPrimaryPerformanceRateLabel);
        Assert.Equal("0", liveSession.SessionExperienceLabel);
        Assert.Equal(analyticsBeforeClear.Session.RateValue, analytics.SessionDpsLabel);
        Assert.Equal(analyticsBeforeClear.Session.TotalValue, analytics.SessionDamageDealtLabel);

        var updatedDamage = new CombatScaledAmount(1_250 * CombatScaledAmount.Scale);
        var updatedCombat = combat with
        {
            DamageDealt = updatedDamage,
            SessionDamagePerSecondHundredths =
                CombatAggregator.CalculateSessionDamagePerSecondHundredths(
                    updatedDamage,
                    TimeSpan.FromMinutes(5))
        };
        identity.Current = Snapshot(
            CreateContext(contextId, experience: 5_500, influence: 550, combat: updatedCombat, eventCount: 5),
            sessionStartedAt);
        identity.RaiseChanged();
        DrainDispatcher();

        Assert.Equal("250", liveSession.SessionPrimaryPerformanceTotalLabel);
        Assert.Equal(analytics.SessionDamageDealtLabel, PrimaryPerformancePresentation.FormatCombatDamageTotal(updatedDamage));
    }

    [Fact]
    public void Combat_only_identity_refresh_updates_live_session_combat_rows()
    {
        var contextId = MonitoringContextId.CreateNew();
        var sessionStartedAt = DateTimeOffset.UtcNow.AddMinutes(-3);
        var initialCombat = new CombatSnapshot
        {
            DamageDealt = new CombatScaledAmount(1_000 * CombatScaledAmount.Scale),
            LastCombatAt = sessionStartedAt.AddMinutes(1),
            SessionDamagePerSecondHundredths =
                CombatAggregator.CalculateSessionDamagePerSecondHundredths(
                    new CombatScaledAmount(1_000 * CombatScaledAmount.Scale),
                    TimeSpan.FromMinutes(3))
        };
        var identity = new FakeIdentityReadService
        {
            Current = Snapshot(
                CreateContext(contextId, experience: 1_000, influence: 100, combat: initialCombat, eventCount: 1),
                sessionStartedAt)
        };

        using var viewModel = TestGameplaySessionContextSupport.CreateLiveSessionViewModel(
            identity,
            TestGameplaySessionContextSupport.FollowingLive(contextId));
        DrainDispatcher();

        var initialExpected = PrimaryPerformancePresentation.BuildSession(
            initialCombat,
            TimeSpan.FromMinutes(3),
            hasCombatData: true);
        Assert.Equal(initialExpected.RateValue, viewModel.SessionPrimaryPerformanceRateLabel);

        var updatedCombat = new CombatSnapshot
        {
            DamageDealt = new CombatScaledAmount(9_000 * CombatScaledAmount.Scale),
            LastCombatAt = sessionStartedAt.AddMinutes(2),
            SessionDamagePerSecondHundredths =
                CombatAggregator.CalculateSessionDamagePerSecondHundredths(
                    new CombatScaledAmount(9_000 * CombatScaledAmount.Scale),
                    TimeSpan.FromMinutes(3))
        };
        identity.Current = Snapshot(
            CreateContext(contextId, experience: 1_000, influence: 100, combat: updatedCombat, eventCount: 1),
            sessionStartedAt);
        identity.RaiseChanged();
        DrainDispatcher();

        var updatedExpected = PrimaryPerformancePresentation.BuildSession(
            updatedCombat,
            TimeSpan.FromMinutes(3),
            hasCombatData: true);
        Assert.Equal(updatedExpected.RateValue, viewModel.SessionPrimaryPerformanceRateLabel);
        Assert.Equal(updatedExpected.TotalValue, viewModel.SessionPrimaryPerformanceTotalLabel);
    }

    [Fact]
    public void Combat_only_identity_refresh_updates_tracked_dps_while_tracked_session_is_running()
    {
        var contextId = MonitoringContextId.CreateNew();
        var sessionStartedAt = DateTimeOffset.UtcNow.AddMinutes(-3);
        var initialTracked = new TrackedCombatScopeSnapshot
        {
            IsTracking = true,
            ActiveElapsed = TimeSpan.FromMinutes(2),
            DamageDealt = new CombatScaledAmount(250 * CombatScaledAmount.Scale),
            DamagePerSecondHundredths = 208
        };
        var identity = new FakeIdentityReadService
        {
            Current = Snapshot(
                CreateContext(
                    contextId,
                    experience: 1_000,
                    influence: 100,
                    combat: new CombatSnapshot { Tracked = initialTracked },
                    eventCount: 1),
                sessionStartedAt)
        };

        using var viewModel = TestGameplaySessionContextSupport.CreateLiveSessionViewModel(
            identity,
            TestGameplaySessionContextSupport.FollowingLive(contextId));
        DrainDispatcher();
        viewModel.StartTrackedSessionCommand.Execute(null);
        identity.RaiseChanged();
        DrainDispatcher();

        var updatedTracked = initialTracked with
        {
            DamageDealt = new CombatScaledAmount(1_500 * CombatScaledAmount.Scale),
            DamagePerSecondHundredths = 1_250
        };
        identity.Current = Snapshot(
            CreateContext(
                contextId,
                experience: 1_000,
                influence: 100,
                combat: new CombatSnapshot { Tracked = updatedTracked },
                eventCount: 2),
            sessionStartedAt);
        identity.RaiseChanged();
        DrainDispatcher();

        var expected = PrimaryPerformancePresentation.BuildTracked(updatedTracked);
        Assert.Equal(expected.RateValue, viewModel.TrackedSessionPrimaryPerformanceRateLabel);
        Assert.Equal(expected.TotalValue, viewModel.TrackedSessionPrimaryPerformanceTotalLabel);
    }

    [Fact]
    public void Analytics_and_live_session_session_dps_match_after_combat_only_refresh()
    {
        var contextId = MonitoringContextId.CreateNew();
        var sessionStartedAt = DateTimeOffset.UtcNow.AddMinutes(-4);
        var combat = new CombatSnapshot
        {
            DamageDealt = new CombatScaledAmount(6_000 * CombatScaledAmount.Scale),
            LastCombatAt = sessionStartedAt.AddMinutes(3),
            SessionDamagePerSecondHundredths =
                CombatAggregator.CalculateSessionDamagePerSecondHundredths(
                    new CombatScaledAmount(6_000 * CombatScaledAmount.Scale),
                    TimeSpan.FromMinutes(4))
        };
        var identity = new FakeIdentityReadService
        {
            Current = Snapshot(
                CreateContext(contextId, experience: 2_000, influence: 200, combat: combat, eventCount: 3),
                sessionStartedAt)
        };

        using var liveSession = TestGameplaySessionContextSupport.CreateLiveSessionViewModel(
            identity,
            TestGameplaySessionContextSupport.FollowingLive(contextId));
        using var analytics = TestGameplaySessionContextSupport.CreateAnalyticsViewModel(
            identity,
            TestGameplaySessionContextSupport.FollowingLive(contextId));
        DrainDispatcher();

        var updatedCombat = combat with
        {
            DamageDealt = new CombatScaledAmount(12_000 * CombatScaledAmount.Scale),
            SessionDamagePerSecondHundredths =
                CombatAggregator.CalculateSessionDamagePerSecondHundredths(
                    new CombatScaledAmount(12_000 * CombatScaledAmount.Scale),
                    TimeSpan.FromMinutes(4))
        };
        identity.Current = Snapshot(
            CreateContext(contextId, experience: 2_000, influence: 200, combat: updatedCombat, eventCount: 3),
            sessionStartedAt);
        identity.RaiseChanged();
        DrainDispatcher();

        Assert.Equal(analytics.SessionDpsLabel, liveSession.SessionPrimaryPerformanceRateLabel);
    }

    private static LiveMonitoringContextIdentityReadModel CreateContext(
        MonitoringContextId contextId,
        long experience,
        long influence,
        CombatSnapshot? combat = null,
        int eventCount = 0) =>
        CreateContext(
            contextId,
            CharacterRecordId.CreateNew(),
            "Example Hero",
            experience,
            influence,
            combat,
            eventCount);

    private static LiveMonitoringContextIdentityReadModel CreateContext(
        MonitoringContextId contextId,
        CharacterRecordId recordId,
        string displayName,
        long experience,
        long influence,
        CombatSnapshot? combat = null,
        int eventCount = 0) =>
        TestGameplaySessionContextSupport.CreateContext(
            contextId,
            "acct-" + displayName.Replace(" ", string.Empty, StringComparison.Ordinal),
            recordId,
            displayName,
            StartedAt,
            experience,
            influence,
            combat: combat,
            retainedCombatEventCount: eventCount);

    private static GameplaySessionIdentityReadModelSnapshot Snapshot(
        LiveMonitoringContextIdentityReadModel context,
        DateTimeOffset sessionStartedAt) =>
        Snapshot([context with { SessionStartedAt = sessionStartedAt }], sessionStartedAt);

    private static GameplaySessionIdentityReadModelSnapshot Snapshot(
        LiveMonitoringContextIdentityReadModel[] contexts,
        DateTimeOffset sessionStartedAt)
    {
        var adjusted = contexts
            .Select(context => context with { SessionStartedAt = sessionStartedAt })
            .ToArray();
        return GameplaySessionIdentityReadModelSnapshot.Create(adjusted, DateTimeOffset.UtcNow, 1);
    }

    private static string Format(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

    private static long ParseDisplayedDpsHundredths(string label)
    {
        Assert.EndsWith(" DPS", label, StringComparison.Ordinal);
        var valueText = label[..^4];
        if (valueText.EndsWith('K'))
        {
            var thousands = double.Parse(valueText[..^1], CultureInfo.InvariantCulture);
            return (long)Math.Round(thousands * 1_000 * 100, MidpointRounding.AwayFromZero);
        }

        if (valueText.EndsWith('M'))
        {
            var millions = double.Parse(valueText[..^1], CultureInfo.InvariantCulture);
            return (long)Math.Round(millions * 1_000_000 * 100, MidpointRounding.AwayFromZero);
        }

        return long.Parse(valueText, NumberStyles.AllowThousands, CultureInfo.InvariantCulture) * 100;
    }

    private static void DrainDispatcher()
    {
        if (Application.Current?.Dispatcher is { } dispatcher)
        {
            dispatcher.Invoke(() => { }, DispatcherPriority.Background);
            dispatcher.Invoke(() => { }, DispatcherPriority.Background);
        }
    }

    private sealed class FakeIdentityReadService : IGameplaySessionIdentityReadService
    {
        public GameplaySessionIdentityReadModelSnapshot Current { get; set; } =
            GameplaySessionIdentityReadModelSnapshot.Empty;

        public event EventHandler<GameplaySessionIdentityReadModelChangedEventArgs>? Changed;

        public void RaiseChanged() =>
            Changed?.Invoke(
                this,
                new GameplaySessionIdentityReadModelChangedEventArgs { Snapshot = Current });
    }
}
