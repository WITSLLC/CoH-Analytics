using System.Windows;
using System.Windows.Threading;
using CoHAnalytics.Models;
using CoHAnalytics.Navigation;
using CoHAnalytics.Services;
using CoHAnalytics.Tests.Services;
using CoHAnalytics.ViewModels.Workspaces;

namespace CoHAnalytics.Tests.Workspaces;

[Collection(WpfDispatcherCollection.Name)]
public sealed class AnalyticsCombatShellTests
{
    [Fact]
    public void Analytics_navigation_uses_workspace_id_without_combat_sidebar_item()
    {
        Assert.Contains(WorkspaceId.Analytics, Enum.GetValues<WorkspaceId>());
        Assert.DoesNotContain(
            Enum.GetNames<WorkspaceId>(),
            name => string.Equals(name, "Combat", StringComparison.Ordinal));

        var analyticsItem = NavigationItem.CreateDefaultNavigation(Themes.ThemeId.Hero)
            .Single(item => item.WorkspaceId == WorkspaceId.Analytics);
        Assert.Equal("ANALYTICS", analyticsItem.Label);
        Assert.DoesNotContain(
            NavigationItem.CreateDefaultNavigation(Themes.ThemeId.Hero),
            item => item.Label.Contains("COMBAT", StringComparison.OrdinalIgnoreCase)
                    && item.WorkspaceId != WorkspaceId.Analytics);
    }

    [Fact]
    public void Chip_selection_switches_active_content()
    {
        using var viewModel = TestGameplaySessionContextSupport.CreateAnalyticsViewModel(
            new FakeIdentityReadService());
        DrainDispatcher();

        Assert.True(viewModel.IsOverviewSelected);
        Assert.True(viewModel.ShowOverviewContent);
        Assert.False(viewModel.ShowCombatContent);
        Assert.False(viewModel.ShowEarningsContent);

        viewModel.SelectChipCommand.Execute(AnalyticsChipId.Combat);
        DrainDispatcher();

        Assert.True(viewModel.IsCombatSelected);
        Assert.True(viewModel.ShowCombatContent);
        Assert.False(viewModel.ShowOverviewContent);
        Assert.False(viewModel.ShowEarningsContent);

        viewModel.SelectChipCommand.Execute(AnalyticsChipId.Overview);
        DrainDispatcher();

        Assert.True(viewModel.IsOverviewSelected);
        Assert.True(viewModel.ShowOverviewContent);
    }

    [Fact]
    public void Hidden_earnings_chip_is_not_visible_and_selecting_it_falls_back_to_overview()
    {
        using var viewModel = TestGameplaySessionContextSupport.CreateAnalyticsViewModel(
            new FakeIdentityReadService());
        DrainDispatcher();

        Assert.DoesNotContain(viewModel.Chips, chip => chip.ChipId == AnalyticsChipId.Earnings);
        Assert.Equal(2, viewModel.Chips.Count);

        viewModel.SelectChipCommand.Execute(AnalyticsChipId.Combat);
        DrainDispatcher();
        Assert.True(viewModel.IsCombatSelected);

        viewModel.SelectChipCommand.Execute(AnalyticsChipId.Earnings);
        DrainDispatcher();

        Assert.True(viewModel.IsOverviewSelected);
        Assert.True(viewModel.ShowOverviewContent);
        Assert.False(viewModel.ShowEarningsContent);
    }

    [Fact]
    public void Default_chip_is_overview()
    {
        using var viewModel = TestGameplaySessionContextSupport.CreateAnalyticsViewModel(
            new FakeIdentityReadService());
        DrainDispatcher();

        Assert.Equal(AnalyticsChipId.Overview, viewModel.SelectedChip);
        Assert.True(viewModel.Chips.Single(chip => chip.ChipId == AnalyticsChipId.Overview).IsActive);
        Assert.Equal(2, viewModel.Chips.Count);
    }

    [Fact]
    public void Context_resolver_projects_selected_context_combat_values()
    {
        var contextA = MonitoringContextId.CreateNew();
        var contextB = MonitoringContextId.CreateNew();
        var recordA = CharacterRecordId.CreateNew();
        var recordB = CharacterRecordId.CreateNew();
        var sessionStartedAt = DateTimeOffset.UtcNow.AddMinutes(-2);
        var combatA = CreateCombat(dpsHundredths: 1_000, damage: 1_000);
        var combatB = CreateCombat(dpsHundredths: 8_000, damage: 8_000);
        var identity = new FakeIdentityReadService
        {
            Current = Snapshot(
                [
                    CreateContext(contextA, recordA, "Hero A", combatA),
                    CreateContext(contextB, recordB, "Hero B", combatB)
                ],
                sessionStartedAt)
        };

        using var viewModel = TestGameplaySessionContextSupport.CreateAnalyticsViewModel(
            identity,
            TestGameplaySessionContextSupport.FollowingLive(contextB));
        viewModel.SelectChipCommand.Execute(AnalyticsChipId.Combat);
        DrainDispatcher();

        Assert.Equal("Hero B", viewModel.ContextCharacterLabel);
        Assert.Equal("8K", viewModel.SessionDamageDealtLabel);
        Assert.Equal(
            PrimaryPerformancePresentation.FormatDamagePerSecond(8_000),
            viewModel.SessionDpsLabel);
    }

    [Fact]
    public void Navigating_from_live_session_to_analytics_keeps_the_active_live_session_when_another_character_is_pinned()
    {
        var contextId = MonitoringContextId.CreateNew();
        var liveRecordId = CharacterRecordId.CreateNew();
        var browseRecordId = CharacterRecordId.CreateNew();
        var combat = CreateCombat(dpsHundredths: 11_400, damage: 68_283);
        var identity = new FakeIdentityReadService
        {
            Current = Snapshot(
                CreateContext(contextId, liveRecordId, "AlphaTest2", combat),
                DateTimeOffset.UtcNow.AddMinutes(-10))
        };
        var pinnedCharacter = TestGameplaySessionContextSupport.PinnedCharacter(
            "other-account",
            browseRecordId);

        using var liveSession = TestGameplaySessionContextSupport.CreateLiveSessionViewModel(
            identity,
            pinnedCharacter);
        using var analytics = TestGameplaySessionContextSupport.CreateAnalyticsViewModel(
            identity,
            pinnedCharacter);
        analytics.SelectChipCommand.Execute(AnalyticsChipId.Combat);
        DrainDispatcher();

        Assert.True(analytics.HasActiveSession);
        Assert.Equal("AlphaTest2", analytics.ContextCharacterLabel);
        Assert.Equal(liveSession.SessionPrimaryPerformanceRateLabel, analytics.SessionDpsLabel);
        Assert.Equal(liveSession.SessionPrimaryPerformanceTotalLabel, analytics.SessionDamageDealtLabel);
    }

    [Fact]
    public void Session_becoming_active_after_analytics_initialization_refreshes_combat_presentation()
    {
        var identity = new FakeIdentityReadService();
        var browseRecordId = CharacterRecordId.CreateNew();
        using var viewModel = TestGameplaySessionContextSupport.CreateAnalyticsViewModel(
            identity,
            TestGameplaySessionContextSupport.PinnedCharacter("other-account", browseRecordId));
        viewModel.SelectChipCommand.Execute(AnalyticsChipId.Combat);
        DrainDispatcher();

        Assert.False(viewModel.HasActiveSession);
        Assert.Equal("No active session", viewModel.CombatStatusHeadline);

        var contextId = MonitoringContextId.CreateNew();
        var liveRecordId = CharacterRecordId.CreateNew();
        identity.Current = Snapshot(
            CreateContext(
                contextId,
                liveRecordId,
                "AlphaTest2",
                CreateCombat(dpsHundredths: 12_500, damage: 75_000)),
            DateTimeOffset.UtcNow.AddMinutes(-5));
        identity.RaiseChanged();
        DrainDispatcher();

        Assert.True(viewModel.HasActiveSession);
        Assert.Equal("AlphaTest2", viewModel.ContextCharacterLabel);
        Assert.Equal("75K", viewModel.SessionDamageDealtLabel);
        Assert.Equal(
            PrimaryPerformancePresentation.FormatDamagePerSecond(12_500),
            viewModel.SessionDpsLabel);
    }

    [Fact]
    public void Active_session_identity_change_refreshes_analytics_to_the_new_live_context()
    {
        var contextA = MonitoringContextId.CreateNew();
        var contextB = MonitoringContextId.CreateNew();
        var identity = new FakeIdentityReadService
        {
            Current = Snapshot(
                CreateContext(
                    contextA,
                    CharacterRecordId.CreateNew(),
                    "Hero A",
                    CreateCombat(dpsHundredths: 5_000, damage: 10_000)),
                DateTimeOffset.UtcNow.AddMinutes(-3))
        };
        using var viewModel = TestGameplaySessionContextSupport.CreateAnalyticsViewModel(identity);
        DrainDispatcher();

        Assert.Equal("Hero A", viewModel.ContextCharacterLabel);

        identity.Current = Snapshot(
            CreateContext(
                contextB,
                CharacterRecordId.CreateNew(),
                "Hero B",
                CreateCombat(dpsHundredths: 9_000, damage: 20_000)),
            DateTimeOffset.UtcNow.AddMinutes(-1));
        identity.RaiseChanged();
        DrainDispatcher();

        Assert.True(viewModel.HasActiveSession);
        Assert.Equal("Hero B", viewModel.ContextCharacterLabel);
        Assert.Equal("20K", viewModel.SessionDamageDealtLabel);
    }

    [Fact]
    public void Live_session_and_analytics_combat_values_match_domain_snapshot()
    {
        var contextId = MonitoringContextId.CreateNew();
        var sessionStartedAt = DateTimeOffset.UtcNow.AddMinutes(-2);
        var combat = CreateCombat(dpsHundredths: 2_000, damage: 2_000);
        combat = combat with
        {
            Tracked = new TrackedCombatScopeSnapshot
            {
                IsTracking = true,
                ActiveElapsed = TimeSpan.FromMinutes(2),
                DamageDealt = new CombatScaledAmount(500 * CombatScaledAmount.Scale),
                DamagePerSecondHundredths = 417
            }
        };
        var identity = new FakeIdentityReadService
        {
            Current = Snapshot(
                CreateContext(contextId, CharacterRecordId.CreateNew(), "Hero A", combat),
                sessionStartedAt)
        };

        using var liveSession = TestGameplaySessionContextSupport.CreateLiveSessionViewModel(identity);
        using var analytics = TestGameplaySessionContextSupport.CreateAnalyticsViewModel(identity);
        DrainDispatcher();

        liveSession.StartTrackedSessionCommand.Execute(null);
        analytics.SelectChipCommand.Execute(AnalyticsChipId.Combat);
        identity.RaiseChanged();
        DrainDispatcher();

        Assert.Equal(liveSession.SessionPrimaryPerformanceRateLabel, analytics.SessionDpsLabel);
        Assert.Equal(liveSession.SessionPrimaryPerformanceTotalLabel, analytics.SessionDamageDealtLabel);
        Assert.Equal(liveSession.TrackedSessionPrimaryPerformanceRateLabel, analytics.TrackedDpsLabel);
        Assert.Equal(liveSession.TrackedSessionPrimaryPerformanceTotalLabel, analytics.TrackedDamageDealtLabel);
    }

    [Fact]
    public void No_active_session_renders_structural_combat_layout_with_unavailable_values()
    {
        using var viewModel = TestGameplaySessionContextSupport.CreateAnalyticsViewModel(
            new FakeIdentityReadService());
        viewModel.SelectChipCommand.Execute(AnalyticsChipId.Combat);
        DrainDispatcher();

        Assert.True(viewModel.ShowCombatContent);
        Assert.Equal("—", viewModel.SessionDpsLabel);
        Assert.Equal("—", viewModel.SessionDamageDealtLabel);
        Assert.Equal("—", viewModel.TrackedDpsLabel);
        Assert.Equal("—", viewModel.TrackedDamageDealtLabel);
        Assert.Equal("—", viewModel.RollingDpsLabel);
        Assert.Equal("10 min", viewModel.RollingWindowLabel);
        Assert.True(viewModel.RollingDeltaIsUnavailable);
        Assert.Equal("No active session", viewModel.CombatStatusHeadline);
        Assert.False(viewModel.AccuracyShowMetrics);
        Assert.Contains("combat telemetry", viewModel.AccuracyDetail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Session_without_combat_keeps_structure_visible_without_false_zeroes()
    {
        var contextId = MonitoringContextId.CreateNew();
        var sessionStartedAt = DateTimeOffset.UtcNow.AddMinutes(-2);
        var identity = new FakeIdentityReadService
        {
            Current = Snapshot(
                TestGameplaySessionContextSupport.CreateContext(
                    contextId,
                    "acct-HeroA",
                    CharacterRecordId.CreateNew(),
                    "Hero A",
                    sessionStartedAt,
                    combat: CombatSnapshot.Empty,
                    retainedCombatEventCount: 0),
                sessionStartedAt)
        };

        using var viewModel = TestGameplaySessionContextSupport.CreateAnalyticsViewModel(identity);
        viewModel.SelectChipCommand.Execute(AnalyticsChipId.Combat);
        DrainDispatcher();

        Assert.True(viewModel.ShowCombatContent);
        Assert.Equal("—", viewModel.SessionDpsLabel);
        Assert.Equal("—", viewModel.RollingDpsLabel);
        Assert.Equal("Idle", viewModel.CombatStatusHeadline);
        Assert.Equal("No recent combat activity", viewModel.CombatStatusDetail);
        Assert.False(viewModel.AccuracyShowMetrics);
    }

    [Fact]
    public void Chip_switching_preserves_combat_structure_when_context_changes()
    {
        using var viewModel = TestGameplaySessionContextSupport.CreateAnalyticsViewModel(
            new FakeIdentityReadService());
        viewModel.SelectChipCommand.Execute(AnalyticsChipId.Combat);
        DrainDispatcher();
        Assert.True(viewModel.ShowCombatContent);

        viewModel.SelectChipCommand.Execute(AnalyticsChipId.Overview);
        DrainDispatcher();
        Assert.False(viewModel.ShowCombatContent);
        Assert.True(viewModel.ShowOverviewContent);

        viewModel.SelectChipCommand.Execute(AnalyticsChipId.Combat);
        DrainDispatcher();
        Assert.True(viewModel.ShowCombatContent);
        Assert.Equal("—", viewModel.RollingDpsLabel);
    }

    [Fact]
    public void Overview_displays_summary_performance_without_detailed_accuracy()
    {
        var contextId = MonitoringContextId.CreateNew();
        var sessionStartedAt = DateTimeOffset.UtcNow.AddMinutes(-2);
        var combat = CreateCombat(dpsHundredths: 2_000, damage: 2_000) with
        {
            Accuracy = new CombatAccuracyScopeSnapshot
            {
                Attempts = 10,
                Hits = 9,
                Misses = 1,
                RolledAttempts = 8,
                DisplayedChanceSumHundredths = 72_000,
                RollSumHundredths = 40_000
            }
        };
        var identity = new FakeIdentityReadService
        {
            Current = Snapshot(
                CreateContext(contextId, CharacterRecordId.CreateNew(), "Hero A", combat),
                sessionStartedAt)
        };

        using var viewModel = TestGameplaySessionContextSupport.CreateAnalyticsViewModel(identity);
        DrainDispatcher();

        Assert.True(viewModel.ShowOverviewContent);
        Assert.True(viewModel.Overview.ShowNoCharacter);

        viewModel.SelectChipCommand.Execute(AnalyticsChipId.Combat);
        DrainDispatcher();

        Assert.Equal("10", viewModel.AccuracyAttemptsLabel);
        Assert.Equal("9", viewModel.AccuracyHitsLabel);
        Assert.Equal("90.0%", viewModel.AccuracyHitPercentLabel);
    }

    [Fact]
    public void Overview_without_selected_character_uses_historical_no_character_state()
    {
        using var viewModel = TestGameplaySessionContextSupport.CreateAnalyticsViewModel(
            new FakeIdentityReadService());
        DrainDispatcher();

        Assert.True(viewModel.ShowOverviewContent);
        Assert.True(viewModel.Overview.ShowNoCharacter);
        Assert.False(viewModel.Overview.ShowHistoricalMetrics);
    }

    [Fact]
    public void Combat_context_follows_shared_gameplay_session_resolver()
    {
        var contextA = MonitoringContextId.CreateNew();
        var contextB = MonitoringContextId.CreateNew();
        var recordA = CharacterRecordId.CreateNew();
        var recordB = CharacterRecordId.CreateNew();
        var sessionStartedAt = DateTimeOffset.UtcNow.AddMinutes(-2);
        var combatA = CreateCombat(dpsHundredths: 1_000, damage: 1_000);
        var combatB = CreateCombat(dpsHundredths: 8_000, damage: 8_000);
        var identity = new FakeIdentityReadService
        {
            Current = Snapshot(
                [
                    CreateContext(contextA, recordA, "Hero A", combatA),
                    CreateContext(contextB, recordB, "Hero B", combatB)
                ],
                sessionStartedAt)
        };

        using var viewModel = TestGameplaySessionContextSupport.CreateAnalyticsViewModel(
            identity,
            TestGameplaySessionContextSupport.FollowingLive(contextB));
        DrainDispatcher();

        Assert.Equal(
            PrimaryPerformancePresentation.FormatDamagePerSecond(8_000),
            viewModel.SessionDpsLabel);
        Assert.Equal("Hero B", viewModel.ContextCharacterLabel);
    }

    [Fact]
    public void Accuracy_unavailable_keeps_card_message_without_metric_grid()
    {
        var contextId = MonitoringContextId.CreateNew();
        var sessionStartedAt = DateTimeOffset.UtcNow.AddMinutes(-2);
        var combat = CreateCombat(dpsHundredths: 1_000, damage: 1_000);
        var identity = new FakeIdentityReadService
        {
            Current = Snapshot(
                CreateContext(contextId, CharacterRecordId.CreateNew(), "Hero A", combat),
                sessionStartedAt)
        };

        using var viewModel = TestGameplaySessionContextSupport.CreateAnalyticsViewModel(identity);
        viewModel.SelectChipCommand.Execute(AnalyticsChipId.Combat);
        DrainDispatcher();

        Assert.False(viewModel.AccuracyShowMetrics);
        Assert.Contains("Detailed hit-roll data has not been observed", viewModel.AccuracyDetail, StringComparison.Ordinal);
        Assert.Equal("—", viewModel.AccuracyAttemptsLabel);
    }

    [Fact]
    public void No_active_session_shows_intentional_empty_state_without_zero_benchmarks()
    {
        using var viewModel = TestGameplaySessionContextSupport.CreateAnalyticsViewModel(
            new FakeIdentityReadService());
        viewModel.SelectChipCommand.Execute(AnalyticsChipId.Combat);
        DrainDispatcher();

        Assert.True(viewModel.ShowCombatContent);
        Assert.Equal("—", viewModel.SessionDpsLabel);
        Assert.Equal("—", viewModel.SessionDamageDealtLabel);
    }

    [Fact]
    public void Combat_status_maps_domain_snapshot_fields()
    {
        var contextId = MonitoringContextId.CreateNew();
        var sessionStartedAt = DateTimeOffset.UtcNow.AddMinutes(-2);
        var referenceAt = DateTimeOffset.UtcNow;
        var combat = new CombatSnapshot
        {
            IsInCombat = true,
            LastCombatAt = referenceAt.AddSeconds(-12),
            CurrentEngagementDuration = TimeSpan.FromSeconds(30),
            DamageDealt = new CombatScaledAmount(100 * CombatScaledAmount.Scale),
            SessionDamagePerSecondHundredths = 100
        };
        var identity = new FakeIdentityReadService
        {
            Current = Snapshot(
                CreateContext(contextId, CharacterRecordId.CreateNew(), "Hero A", combat),
                sessionStartedAt)
        };

        using var viewModel = TestGameplaySessionContextSupport.CreateAnalyticsViewModel(identity);
        viewModel.SelectChipCommand.Execute(AnalyticsChipId.Combat);
        DrainDispatcher();

        Assert.Equal("ACTIVE", viewModel.CombatStatusHeadline);
        Assert.Equal("Engaged 00:00:30", viewModel.CombatStatusDetail);
    }

    [Fact]
    public void Tracked_idle_state_uses_unavailable_convention()
    {
        var contextId = MonitoringContextId.CreateNew();
        var sessionStartedAt = DateTimeOffset.UtcNow.AddMinutes(-2);
        var combat = CreateCombat(dpsHundredths: 1_000, damage: 1_000);
        var identity = new FakeIdentityReadService
        {
            Current = Snapshot(
                CreateContext(contextId, CharacterRecordId.CreateNew(), "Hero A", combat),
                sessionStartedAt)
        };

        using var viewModel = TestGameplaySessionContextSupport.CreateAnalyticsViewModel(identity);
        viewModel.SelectChipCommand.Execute(AnalyticsChipId.Combat);
        DrainDispatcher();

        Assert.Equal("—", viewModel.TrackedDpsLabel);
        Assert.Equal("—", viewModel.TrackedDamageDealtLabel);
        Assert.Equal("Not running", viewModel.TrackedStateLabel);
    }

    [Fact]
    public void Shared_rolling_window_selection_updates_dps_accuracy_and_enemies_together()
    {
        var contextId = MonitoringContextId.CreateNew();
        var sessionStartedAt = DateTimeOffset.UtcNow.AddMinutes(-12);
        var combat = CreateCombat(dpsHundredths: 1_000, damage: 1_000) with
        {
            Rolling = new RollingCombatScopeSnapshot
            {
                OneMinute = RollingWindow(
                    minutes: 1,
                    dpsHundredths: 100,
                    damage: 100,
                    attempts: 10,
                    hits: 9,
                    misses: 1,
                    totalDefeated: 1,
                    myDefeats: 1),
                TenMinutes = RollingWindow(
                    minutes: 10,
                    dpsHundredths: 200,
                    damage: 2_000,
                    attempts: 20,
                    hits: 10,
                    misses: 10,
                    totalDefeated: 20,
                    myDefeats: 5)
            }
        };
        var identity = new FakeIdentityReadService
        {
            Current = Snapshot(
                CreateContext(contextId, CharacterRecordId.CreateNew(), "Hero A", combat),
                sessionStartedAt)
        };

        using var viewModel = TestGameplaySessionContextSupport.CreateAnalyticsViewModel(identity);
        viewModel.SelectChipCommand.Execute(AnalyticsChipId.Combat);
        DrainDispatcher();

        Assert.Equal(10, viewModel.SelectedRollingWindowMinutes);
        Assert.Equal(PrimaryPerformancePresentation.FormatDamagePerSecond(200), viewModel.RollingDpsLabel);
        Assert.Equal("50.0%", viewModel.RollingAccuracy.Metrics.HitPercentLabel);
        Assert.Equal("20", viewModel.RollingEnemies.TotalDefeatedLabel);
        Assert.Equal("5 (25%)", viewModel.RollingEnemies.MyDefeatsLabel);

        viewModel.SelectRollingPresetCommand.Execute(1);
        DrainDispatcher();

        Assert.Equal(1, viewModel.SelectedRollingWindowMinutes);
        Assert.Equal(PrimaryPerformancePresentation.FormatDamagePerSecond(100), viewModel.RollingDpsLabel);
        Assert.Equal("90.0%", viewModel.RollingAccuracy.Metrics.HitPercentLabel);
        Assert.Equal("1", viewModel.RollingEnemies.TotalDefeatedLabel);
        Assert.Equal("1 (100%)", viewModel.RollingEnemies.MyDefeatsLabel);
        Assert.Single(viewModel.RollingPresets, preset => preset.IsActive && preset.WindowMinutes == 1);
    }

    private static CombatSnapshot CreateCombat(long dpsHundredths, long damage) =>
        new()
        {
            DamageDealt = new CombatScaledAmount(damage * CombatScaledAmount.Scale),
            LastCombatAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            SessionDamagePerSecondHundredths = dpsHundredths
        };

    private static RollingCombatWindowSnapshot RollingWindow(
        int minutes,
        long dpsHundredths,
        long damage,
        long attempts,
        long hits,
        long misses,
        long totalDefeated,
        long myDefeats) =>
        new()
        {
            Availability = RollingCombatAvailability.Available,
            WindowMinutes = minutes,
            WindowDuration = TimeSpan.FromMinutes(minutes),
            EffectiveDenominator = TimeSpan.FromMinutes(minutes),
            DamagePerSecondHundredths = dpsHundredths,
            DamageDealt = new CombatScaledAmount(damage * CombatScaledAmount.Scale),
            TotalDefeated = totalDefeated,
            MyDefeats = myDefeats,
            Accuracy = new CombatAccuracyScopeSnapshot
            {
                Attempts = attempts,
                Hits = hits,
                Misses = misses,
                RolledAttempts = attempts,
                DisplayedChanceSumHundredths = attempts * 9_000,
                RollSumHundredths = attempts * 5_000
            }
        };

    private static LiveMonitoringContextIdentityReadModel CreateContext(
        MonitoringContextId contextId,
        CharacterRecordId recordId,
        string displayName,
        CombatSnapshot combat) =>
        TestGameplaySessionContextSupport.CreateContext(
            contextId,
            "acct-" + displayName.Replace(" ", string.Empty, StringComparison.Ordinal),
            recordId,
            displayName,
            DateTimeOffset.UtcNow.AddMinutes(-2),
            combat: combat,
            retainedCombatEventCount: 1);

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
