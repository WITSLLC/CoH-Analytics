using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using CoHAnalytics.Models;
using CoHAnalytics.Orchestration.Contracts;
using CoHAnalytics.Orchestration.Models;
using CoHAnalytics.Services;
using CoHAnalytics.Tests.Services;
using CoHAnalytics.ViewModels.Workspaces;

namespace CoHAnalytics.Tests.Workspaces;

[Collection(WpfDispatcherCollection.Name)]
public sealed class LiveSessionClearSessionPresentationTests
{
    [Fact]
    public void Clear_commands_capture_the_same_ordered_historical_boundary_before_rebasing()
    {
        var contextId = MonitoringContextId.CreateNew();
        var startedAt = DateTimeOffset.UtcNow.AddMinutes(-10);
        var identity = new FakeIdentityReadService
        {
            Current = Snapshot(contextId, startedAt, experience: 2_000, influence: 200)
        };
        var gameplay = new TestGameplaySessionContextSupport.FakeGameplaySessionManager
        {
            HistoricalPerformanceBoundaryResult = GameplaySessionOperationResult.Failure(
                GameplaySessionOutcome.ProcessingFailed,
                "simulated persistence failure")
        };
        using var viewModel = TestGameplaySessionContextSupport.CreateLiveSessionViewModel(
            identity,
            gameplay);
        DrainDispatcher();
        var presentationAtBoundaries = new List<string>();
        gameplay.HistoricalPerformanceBoundaryInvoked = _ =>
            presentationAtBoundaries.Add(viewModel.SessionExperienceLabel);

        viewModel.ClearSessionCommand.Execute(null);
        Assert.Equal("0", viewModel.SessionExperienceLabel);

        identity.Current = Snapshot(contextId, startedAt, experience: 2_500, influence: 250);
        identity.RaiseChanged();
        DrainDispatcher();
        viewModel.ClearSessionAndRewardsCommand.Execute(null);

        Assert.Equal([contextId, contextId], gameplay.HistoricalPerformanceBoundaryContexts);
        Assert.Equal(["2,000", "500"], presentationAtBoundaries);
        Assert.Equal("0", viewModel.SessionExperienceLabel);
    }

    [Fact]
    public void Save_session_persists_saved_document_without_requesting_historical_boundary()
    {
        var contextId = MonitoringContextId.CreateNew();
        var identity = new FakeIdentityReadService
        {
            Current = Snapshot(
                contextId,
                DateTimeOffset.UtcNow.AddMinutes(-5),
                experience: 2_000,
                influence: 200)
        };
        var gameplay = new TestGameplaySessionContextSupport.FakeGameplaySessionManager();
        var store = new RecordingSessionStore();
        using var viewModel = TestGameplaySessionContextSupport.CreateLiveSessionViewModel(
            identity,
            gameplay,
            sessionStore: store);
        DrainDispatcher();

        viewModel.SaveLiveSessionCommand.Execute(null);

        Assert.NotNull(store.LastSaved);
        Assert.Empty(gameplay.HistoricalPerformanceBoundaryContexts);
    }

    [Fact]
    public void Clear_session_resets_duration_and_keeps_currencies_cumulative()
    {
        var contextId = MonitoringContextId.CreateNew();
        var startedAt = DateTimeOffset.UtcNow.AddMinutes(-10);
        var identity = new FakeIdentityReadService
        {
            Current = Snapshot(
                contextId,
                startedAt,
                experience: 2_000,
                influence: 200,
                currency: ("Reward Merit", 75))
        };

        using var viewModel = TestGameplaySessionContextSupport.CreateLiveSessionViewModel(identity);
        DrainDispatcher();

        var merit = viewModel.Currencies.Single(row => row.SourceKey == "Reward Merit");
        Assert.Equal("75", merit.QuantityLabel);
        Assert.NotEqual("00:00:00", viewModel.SessionDurationLabel);

        viewModel.ClearSessionCommand.Execute(null);
        DrainDispatcher();

        Assert.Equal("00:00:00", viewModel.SessionDurationLabel);
        Assert.Equal("0", viewModel.SessionExperienceLabel);
        Assert.Equal("75", merit.QuantityLabel);
    }

    [Fact]
    public void Clear_session_does_not_alter_tracked_session_benchmark()
    {
        var contextId = MonitoringContextId.CreateNew();
        var startedAt = DateTimeOffset.UtcNow.AddMinutes(-3);
        var trackedCombat = new TrackedCombatScopeSnapshot
        {
            IsTracking = true,
            ActiveElapsed = TimeSpan.FromMinutes(2),
            DamageDealt = new CombatScaledAmount(250 * CombatScaledAmount.Scale),
            DamagePerSecondHundredths = 208
        };
        var identity = new FakeIdentityReadService
        {
            Current = Snapshot(
                contextId,
                startedAt,
                experience: 1_000,
                influence: 100,
                combat: new CombatSnapshot
                {
                    DamageDealt = new CombatScaledAmount(500 * CombatScaledAmount.Scale),
                    LastCombatAt = startedAt.AddMinutes(1),
                    SessionDamagePerSecondHundredths = 2_778,
                    Tracked = trackedCombat
                })
        };

        using var viewModel = TestGameplaySessionContextSupport.CreateLiveSessionViewModel(identity);
        DrainDispatcher();
        viewModel.StartTrackedSessionCommand.Execute(null);
        identity.RaiseChanged();
        DrainDispatcher();

        var trackedRate = viewModel.TrackedSessionPrimaryPerformanceRateLabel;
        var trackedTotal = viewModel.TrackedSessionPrimaryPerformanceTotalLabel;
        var trackedXp = viewModel.TrackedSessionExperienceLabel;
        Assert.True(viewModel.IsTrackedSessionRunning);

        viewModel.ClearSessionCommand.Execute(null);
        DrainDispatcher();

        Assert.True(viewModel.IsTrackedSessionRunning);
        Assert.Equal(trackedRate, viewModel.TrackedSessionPrimaryPerformanceRateLabel);
        Assert.Equal(trackedTotal, viewModel.TrackedSessionPrimaryPerformanceTotalLabel);
        Assert.Equal(trackedXp, viewModel.TrackedSessionExperienceLabel);
    }

    [Fact]
    public void Clear_session_preserves_special_rewards_recipes_and_enhancements()
    {
        var contextId = MonitoringContextId.CreateNew();
        var startedAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        var identity = new FakeIdentityReadService
        {
            Current = RichSnapshot(
                contextId,
                startedAt,
                experience: 1_000,
                influence: 100,
                currencies: [
                    new GameplaySessionRewardCurrencyTotal
                    {
                        CurrencyDisplayName = "Reward Merit",
                        Quantity = 10
                    },
                    new GameplaySessionRewardCurrencyTotal
                    {
                        CurrencyDisplayName = "Enhancement Converter",
                        Quantity = 3
                    }],
                recipes: [new GameplaySessionItemTotal { DisplayName = "Luck Recipe", Quantity = 2 }],
                enhancements: [new GameplaySessionItemTotal { DisplayName = "Accuracy SO", Quantity = 4 }])
        };

        using var viewModel = TestGameplaySessionContextSupport.CreateLiveSessionViewModel(identity);
        DrainDispatcher();

        var converter = viewModel.SpecialRewards.Single(row => row.SourceKey == "Enhancement Converter");
        Assert.Equal("3", converter.QuantityLabel);
        Assert.Equal("2", viewModel.Recipes[0].QuantityLabel);
        Assert.Equal("4", viewModel.EnhancementDrops[0].QuantityLabel);

        viewModel.ClearSessionCommand.Execute(null);
        DrainDispatcher();

        Assert.Equal("0", viewModel.SessionExperienceLabel);
        Assert.Equal("3", converter.QuantityLabel);
        Assert.Equal("2", viewModel.Recipes[0].QuantityLabel);
        Assert.Equal("4", viewModel.EnhancementDrops[0].QuantityLabel);
    }

    [Fact]
    public void Clear_session_and_rewards_resets_performance_and_all_reward_surfaces()
    {
        var contextId = MonitoringContextId.CreateNew();
        var startedAt = DateTimeOffset.UtcNow.AddMinutes(-8);
        var identity = new FakeIdentityReadService
        {
            Current = RichSnapshot(
                contextId,
                startedAt,
                experience: 1_000_000,
                influence: 50_000,
                currencies: [
                    new GameplaySessionRewardCurrencyTotal
                    {
                        CurrencyDisplayName = "Reward Merit",
                        Quantity = 5
                    },
                    new GameplaySessionRewardCurrencyTotal
                    {
                        CurrencyDisplayName = "Enhancement Converter",
                        Quantity = 5
                    }],
                salvage: [new GameplaySessionItemTotal { DisplayName = "Salvage A", Quantity = 3 }],
                inspirations: [
                    new GameplaySessionItemTotal { DisplayName = "Luck", Quantity = 4 },
                    new GameplaySessionItemTotal { DisplayName = "Respite", Quantity = 2 }],
                recipes: [new GameplaySessionItemTotal { DisplayName = "Luck Recipe", Quantity = 2 }],
                enhancements: [new GameplaySessionItemTotal { DisplayName = "Accuracy SO", Quantity = 4 }])
        };

        using var viewModel = TestGameplaySessionContextSupport.CreateLiveSessionViewModel(identity);
        DrainDispatcher();

        viewModel.ClearSessionAndRewardsCommand.Execute(null);
        DrainDispatcher();

        Assert.Equal("00:00:00", viewModel.SessionDurationLabel);
        Assert.Equal("0", viewModel.SessionExperienceLabel);
        Assert.Equal("0", viewModel.SessionGameplayInfluenceLabel);
        Assert.False(viewModel.HasSpecialRewards);
        Assert.Empty(viewModel.SalvageDrops);
        Assert.Empty(viewModel.Recipes);
        Assert.Empty(viewModel.EnhancementDrops);
        Assert.Empty(viewModel.InspirationDrops);
        Assert.All(viewModel.Currencies, row => Assert.Equal("0", row.QuantityLabel));
    }

    [Fact]
    public void New_rewards_after_clear_session_and_rewards_display_from_zero()
    {
        var contextId = MonitoringContextId.CreateNew();
        var startedAt = DateTimeOffset.UtcNow.AddMinutes(-4);
        var identity = new FakeIdentityReadService
        {
            Current = RichSnapshot(
                contextId,
                startedAt,
                inspirations: [new GameplaySessionItemTotal { DisplayName = "Luck", Quantity = 4 }])
        };

        using var viewModel = TestGameplaySessionContextSupport.CreateLiveSessionViewModel(identity);
        DrainDispatcher();

        viewModel.ClearSessionAndRewardsCommand.Execute(null);
        DrainDispatcher();
        Assert.Empty(viewModel.InspirationDrops);

        identity.Current = RichSnapshot(
            contextId,
            startedAt,
            inspirations: [new GameplaySessionItemTotal { DisplayName = "Luck", Quantity = 5 }]);
        identity.RaiseChanged();
        DrainDispatcher();

        Assert.Single(viewModel.InspirationDrops);
        Assert.Equal("Luck", viewModel.InspirationDrops[0].DisplayName);
        Assert.Equal("1", viewModel.InspirationDrops[0].QuantityLabel);
    }

    [Fact]
    public void Save_after_clear_session_preserves_visible_rewards_and_rebased_performance()
    {
        var contextId = MonitoringContextId.CreateNew();
        var startedAt = DateTimeOffset.UtcNow.AddMinutes(-6);
        var identity = new FakeIdentityReadService
        {
            Current = RichSnapshot(
                contextId,
                startedAt,
                experience: 2_000,
                influence: 200,
                currencies: [
                    new GameplaySessionRewardCurrencyTotal
                    {
                        CurrencyDisplayName = "Reward Merit",
                        Quantity = 75
                    }],
                salvage: [new GameplaySessionItemTotal { DisplayName = "Fortune", Quantity = 4 }])
        };
        var store = new RecordingSessionStore();

        using var viewModel = TestGameplaySessionContextSupport.CreateLiveSessionViewModel(
            identity,
            sessionStore: store);
        DrainDispatcher();

        viewModel.ClearSessionCommand.Execute(null);
        DrainDispatcher();

        identity.Current = RichSnapshot(
            contextId,
            startedAt,
            experience: 2_500,
            influence: 250,
            currencies: [
                new GameplaySessionRewardCurrencyTotal
                {
                    CurrencyDisplayName = "Reward Merit",
                    Quantity = 80
                }],
            salvage: [new GameplaySessionItemTotal { DisplayName = "Fortune", Quantity = 5 }]);
        identity.RaiseChanged();
        DrainDispatcher();

        viewModel.SaveLiveSessionCommand.Execute(null);

        Assert.NotNull(store.LastSaved);
        Assert.Equal(500, store.LastSaved!.Earnings.Experience);
        Assert.Equal(50, store.LastSaved.Earnings.Influence);
        Assert.Equal(80, store.LastSaved.Currencies.Single(c => c.Name == "Reward Merit").Quantity);
        Assert.Equal(5, store.LastSaved.Loot.Salvage.Single(s => s.Name == "Fortune").Quantity);
    }

    [Fact]
    public void Save_after_clear_session_and_rewards_saves_only_post_clear_totals()
    {
        var contextId = MonitoringContextId.CreateNew();
        var startedAt = DateTimeOffset.UtcNow.AddMinutes(-6);
        var identity = new FakeIdentityReadService
        {
            Current = RichSnapshot(
                contextId,
                startedAt,
                experience: 10_000,
                influence: 1_000,
                currencies: [
                    new GameplaySessionRewardCurrencyTotal
                    {
                        CurrencyDisplayName = "Reward Merit",
                        Quantity = 20
                    }],
                salvage: [new GameplaySessionItemTotal { DisplayName = "Fortune", Quantity = 10 }])
        };
        var store = new RecordingSessionStore();

        using var viewModel = TestGameplaySessionContextSupport.CreateLiveSessionViewModel(
            identity,
            sessionStore: store);
        DrainDispatcher();

        viewModel.ClearSessionAndRewardsCommand.Execute(null);
        DrainDispatcher();

        identity.Current = RichSnapshot(
            contextId,
            startedAt,
            experience: 10_500,
            influence: 1_050,
            currencies: [
                new GameplaySessionRewardCurrencyTotal
                {
                    CurrencyDisplayName = "Reward Merit",
                    Quantity = 22
                }],
            salvage: [new GameplaySessionItemTotal { DisplayName = "Fortune", Quantity = 11 }]);
        identity.RaiseChanged();
        DrainDispatcher();

        viewModel.SaveLiveSessionCommand.Execute(null);

        Assert.NotNull(store.LastSaved);
        Assert.Equal(500, store.LastSaved!.Earnings.Experience);
        Assert.Equal(50, store.LastSaved.Earnings.Influence);
        Assert.Equal(2, store.LastSaved.Currencies.Single(c => c.Name == "Reward Merit").Quantity);
        Assert.Equal(1, store.LastSaved.Loot.Salvage.Single(s => s.Name == "Fortune").Quantity);
    }

    [Fact]
    public void Completed_live_session_auto_persist_uses_authoritative_totals_after_clear()
    {
        var contextId = MonitoringContextId.CreateNew();
        var startedAt = DateTimeOffset.UtcNow.AddMinutes(-10);
        var identity = new FakeIdentityReadService
        {
            Current = RichSnapshot(
                contextId,
                startedAt,
                experience: 3_000,
                influence: 300,
                currencies: [
                    new GameplaySessionRewardCurrencyTotal
                    {
                        CurrencyDisplayName = "Reward Merit",
                        Quantity = 15
                    }])
        };
        var store = new RecordingSessionStore();

        using var viewModel = TestGameplaySessionContextSupport.CreateLiveSessionViewModel(
            identity,
            sessionStore: store);
        DrainDispatcher();
        identity.RaiseChanged();
        DrainDispatcher();

        viewModel.ClearSessionAndRewardsCommand.Execute(null);
        DrainDispatcher();

        identity.Current = GameplaySessionIdentityReadModelSnapshot.Create(
            Array.Empty<LiveMonitoringContextIdentityReadModel>(),
            DateTimeOffset.UtcNow,
            1);
        identity.RaiseChanged();
        DrainDispatcher();

        Assert.NotNull(store.LastCompletedLive);
        Assert.Equal(3_000, store.LastCompletedLive!.Earnings.Experience);
        Assert.Equal(300, store.LastCompletedLive.Earnings.Influence);
        Assert.Equal(15, store.LastCompletedLive.Currencies.Single(c => c.Name == "Reward Merit").Quantity);
    }

    [Fact]
    public void Clear_session_and_rewards_reward_baselines_are_isolated_per_context()
    {
        var contextA = MonitoringContextId.CreateNew();
        var contextB = MonitoringContextId.CreateNew();
        var recordA = CharacterRecordId.CreateNew();
        var recordB = CharacterRecordId.CreateNew();
        var startedAt = DateTimeOffset.UtcNow;
        var identity = new FakeIdentityReadService
        {
            Current = GameplaySessionIdentityReadModelSnapshot.Create(
                [
                    RichContext(
                        contextA,
                        recordA,
                        startedAt,
                        salvage: [new GameplaySessionItemTotal { DisplayName = "Fortune", Quantity = 4 }]),
                    RichContext(
                        contextB,
                        recordB,
                        startedAt,
                        salvage: [new GameplaySessionItemTotal { DisplayName = "Fortune", Quantity = 9 }])
                ],
                DateTimeOffset.UtcNow,
                1)
        };
        var viewed = new TestGameplaySessionContextSupport.FakeViewedContextService(
            TestGameplaySessionContextSupport.FollowingLive(contextA));
        var resolver = new GameplaySessionContextResolver(identity, viewed);
        using var live = new LiveSessionViewModel(
            new TestGameplaySessionContextSupport.FakeApplicationOrchestrator(),
            new FakeGameRuntimeService { CurrentStatus = GameRuntimeStatus.Off },
            new TestGameplaySessionContextSupport.FakeGameplaySessionManager(),
            identity,
            resolver,
            viewed);
        DrainDispatcher();

        live.ClearSessionAndRewardsCommand.Execute(null);
        DrainDispatcher();
        Assert.Empty(live.SalvageDrops);

        live.SelectSessionTabCommand.Execute(live.SessionTabs.Single(tab => tab.ContextId == contextB));
        DrainDispatcher();
        Assert.Single(live.SalvageDrops);
        Assert.Equal("9", live.SalvageDrops[0].QuantityLabel);

        live.SelectSessionTabCommand.Execute(live.SessionTabs.Single(tab => tab.ContextId == contextA));
        DrainDispatcher();
        Assert.Empty(live.SalvageDrops);
    }

    [Fact]
    public void Clear_session_and_rewards_does_not_alter_tracked_session_benchmark()
    {
        var contextId = MonitoringContextId.CreateNew();
        var startedAt = DateTimeOffset.UtcNow.AddMinutes(-3);
        var trackedCombat = new TrackedCombatScopeSnapshot
        {
            IsTracking = true,
            ActiveElapsed = TimeSpan.FromMinutes(2),
            DamageDealt = new CombatScaledAmount(250 * CombatScaledAmount.Scale),
            DamagePerSecondHundredths = 208
        };
        var identity = new FakeIdentityReadService
        {
            Current = Snapshot(
                contextId,
                startedAt,
                experience: 1_000,
                influence: 100,
                combat: new CombatSnapshot
                {
                    DamageDealt = new CombatScaledAmount(500 * CombatScaledAmount.Scale),
                    LastCombatAt = startedAt.AddMinutes(1),
                    SessionDamagePerSecondHundredths = 2_778,
                    Tracked = trackedCombat
                })
        };

        using var viewModel = TestGameplaySessionContextSupport.CreateLiveSessionViewModel(identity);
        DrainDispatcher();
        viewModel.StartTrackedSessionCommand.Execute(null);
        identity.RaiseChanged();
        DrainDispatcher();

        var trackedRate = viewModel.TrackedSessionPrimaryPerformanceRateLabel;
        var trackedTotal = viewModel.TrackedSessionPrimaryPerformanceTotalLabel;
        var trackedXp = viewModel.TrackedSessionExperienceLabel;
        Assert.True(viewModel.IsTrackedSessionRunning);

        viewModel.ClearSessionAndRewardsCommand.Execute(null);
        DrainDispatcher();

        Assert.True(viewModel.IsTrackedSessionRunning);
        Assert.Equal(trackedRate, viewModel.TrackedSessionPrimaryPerformanceRateLabel);
        Assert.Equal(trackedTotal, viewModel.TrackedSessionPrimaryPerformanceTotalLabel);
        Assert.Equal(trackedXp, viewModel.TrackedSessionExperienceLabel);
    }

    [Fact]
    public void Clear_session_and_rewards_preserves_resolved_character_identity()
    {
        var contextId = MonitoringContextId.CreateNew();
        var recordId = CharacterRecordId.CreateNew();
        var startedAt = DateTimeOffset.UtcNow;
        var identity = new FakeIdentityReadService
        {
            Current = GameplaySessionIdentityReadModelSnapshot.Create(
                [
                    RichContext(
                        contextId,
                        recordId,
                        startedAt,
                        accountStableId: "acct-hero",
                        displayName: "Hero A",
                        experience: 5_000,
                        influence: 500)
                ],
                DateTimeOffset.UtcNow,
                1)
        };

        using var viewModel = TestGameplaySessionContextSupport.CreateLiveSessionViewModel(identity);
        DrainDispatcher();

        Assert.Equal(LiveSessionWorkspaceState.Live, viewModel.WorkspaceState);
        Assert.Equal("Hero A", viewModel.ViewedCharacterLabel);
        Assert.Equal(recordId, identity.Current.Contexts[0].CharacterRecordId);

        viewModel.ClearSessionCommand.Execute(null);
        DrainDispatcher();

        Assert.Equal(LiveSessionWorkspaceState.Live, viewModel.WorkspaceState);
        Assert.Equal("Hero A", viewModel.ViewedCharacterLabel);
        Assert.Equal(recordId, identity.Current.Contexts[0].CharacterRecordId);

        viewModel.ClearSessionAndRewardsCommand.Execute(null);
        DrainDispatcher();

        Assert.Equal(LiveSessionWorkspaceState.Live, viewModel.WorkspaceState);
        Assert.Equal("Hero A", viewModel.ViewedCharacterLabel);
        Assert.Equal(recordId, identity.Current.Contexts[0].CharacterRecordId);
    }

    [Fact]
    public void Presentation_baseline_subtraction_never_displays_negative_quantities()
    {
        var contextId = MonitoringContextId.CreateNew();
        var startedAt = DateTimeOffset.UtcNow;
        var identity = new FakeIdentityReadService
        {
            Current = RichSnapshot(
                contextId,
                startedAt,
                salvage: [new GameplaySessionItemTotal { DisplayName = "Fortune", Quantity = 5 }])
        };

        using var viewModel = TestGameplaySessionContextSupport.CreateLiveSessionViewModel(identity);
        DrainDispatcher();

        viewModel.ClearSessionAndRewardsCommand.Execute(null);
        DrainDispatcher();

        identity.Current = RichSnapshot(
            contextId,
            startedAt,
            salvage: [new GameplaySessionItemTotal { DisplayName = "Fortune", Quantity = 3 }]);
        identity.RaiseChanged();
        DrainDispatcher();

        Assert.Empty(viewModel.SalvageDrops);
    }

    private static LiveMonitoringContextIdentityReadModel RichContext(
        MonitoringContextId contextId,
        CharacterRecordId recordId,
        DateTimeOffset startedAt,
        string accountStableId = "acct-test",
        string displayName = "Hero",
        long experience = 0,
        long influence = 0,
        IReadOnlyList<GameplaySessionRewardCurrencyTotal>? currencies = null,
        IReadOnlyList<GameplaySessionItemTotal>? salvage = null,
        IReadOnlyList<GameplaySessionItemTotal>? recipes = null,
        IReadOnlyList<GameplaySessionItemTotal>? enhancements = null,
        IReadOnlyList<GameplaySessionItemTotal>? inspirations = null) =>
        TestGameplaySessionContextSupport.CreateContext(
            contextId,
            accountStableId,
            recordId,
            displayName,
            startedAt,
            experience,
            influence,
            salvage: salvage) with
        {
            RewardCurrencyTotals = currencies ?? [],
            RecipeTotals = recipes ?? [],
            EnhancementTotals = enhancements ?? [],
            InspirationTotals = inspirations ?? []
        };

    private static GameplaySessionIdentityReadModelSnapshot RichSnapshot(
        MonitoringContextId contextId,
        DateTimeOffset startedAt,
        long experience = 0,
        long influence = 0,
        IReadOnlyList<GameplaySessionRewardCurrencyTotal>? currencies = null,
        IReadOnlyList<GameplaySessionItemTotal>? salvage = null,
        IReadOnlyList<GameplaySessionItemTotal>? recipes = null,
        IReadOnlyList<GameplaySessionItemTotal>? enhancements = null,
        IReadOnlyList<GameplaySessionItemTotal>? inspirations = null) =>
        GameplaySessionIdentityReadModelSnapshot.Create(
            [
                RichContext(
                    contextId,
                    CharacterRecordId.CreateNew(),
                    startedAt,
                    experience: experience,
                    influence: influence,
                    currencies: currencies,
                    salvage: salvage,
                    recipes: recipes,
                    enhancements: enhancements,
                    inspirations: inspirations)
            ],
            DateTimeOffset.UtcNow,
            1);

    private static string Format(long value) =>
        value.ToString("N0", CultureInfo.InvariantCulture);

    private static RollingEarningsScopeSnapshot BuildRollingEarningsSnapshot(DateTimeOffset sessionStartedAt)
    {
        var rolling = new RollingEarningsAccumulator();
        for (var minute = 1; minute <= 12; minute++)
        {
            var at = sessionStartedAt.AddMinutes(minute);
            rolling.Apply(ParserEvent(minute, at, at.LocalDateTime), minute * 1_000, minute * 100);
        }

        return rolling.ToSnapshot(sessionStartedAt, sessionStartedAt.AddMinutes(12), timingEndAt: null);
    }

    private static CombatSnapshot BuildCombatSnapshot(DateTimeOffset sessionStartedAt)
    {
        var aggregator = new CombatAggregator();
        var contextId = MonitoringContextId.CreateNew();
        for (var minute = 1; minute <= 12; minute++)
        {
            var at = sessionStartedAt.AddMinutes(minute);
            aggregator.Apply(new CombatEvent
            {
                ContextId = contextId,
                ParserSequence = minute,
                ObservedAt = at,
                SourceTimestamp = at.LocalDateTime,
                Kind = CombatEventKind.DamageDealt,
                GrammarId = CombatGrammarId.Dmg01YouHitWithPower,
                Amount = new CombatScaledAmount(minute * 1_000)
            });
        }

        return aggregator.ToSnapshot(
            sessionStartedAt,
            sessionStartedAt.AddMinutes(12),
            timingEndAt: null,
            CombatActivityDefaults.IdleThreshold);
    }

    private static LiveMonitoringContextIdentityReadModel CreateContext(
        DateTimeOffset sessionStartedAt,
        CombatSnapshot combat,
        RollingEarningsScopeSnapshot rollingEarnings) =>
        TestGameplaySessionContextSupport.CreateContext(
            MonitoringContextId.CreateNew(),
            "acct-HeroA",
            CharacterRecordId.CreateNew(),
            "Hero A",
            sessionStartedAt,
            experience: 12_000,
            influence: 4_500,
            combat: combat,
            retainedCombatEventCount: 12) with
        {
            RollingEarnings = rollingEarnings
        };

    private static GameplaySessionIdentityReadModelSnapshot Snapshot(
        LiveMonitoringContextIdentityReadModel context,
        DateTimeOffset sessionStartedAt) =>
        GameplaySessionIdentityReadModelSnapshot.Create(
            [context with { SessionStartedAt = sessionStartedAt }],
            DateTimeOffset.UtcNow,
            1);

    private static GameplaySessionIdentityReadModelSnapshot Snapshot(
        MonitoringContextId contextId,
        DateTimeOffset startedAt,
        long experience = 0,
        long influence = 0,
        (string DisplayName, long Quantity)? currency = null,
        CombatSnapshot? combat = null) =>
        Snapshot(
            TestGameplaySessionContextSupport.CreateContext(
                contextId,
                "acct-test",
                CharacterRecordId.CreateNew(),
                "Hero",
                startedAt,
                experience,
                influence,
                combat: combat) with
            {
                RewardCurrencyTotals = currency is { } entry
                    ? [new GameplaySessionRewardCurrencyTotal
                    {
                        CurrencyDisplayName = entry.DisplayName,
                        Quantity = entry.Quantity
                    }]
                    : []
            },
            startedAt);

    private static ParserEvent ParserEvent(long sequence, DateTimeOffset observedAt, DateTime sourceTimestamp) =>
        new()
        {
            ContextId = MonitoringContextId.CreateNew(),
            SourceId = LogSourceId.Create("acct", "acct", @"C:\fake\log.txt", new DateOnly(2026, 8, 9)),
            SourceSegmentId = ParserSourceSegmentId.CreateNew(),
            BindingGeneration = 1,
            Sequence = sequence,
            ObservedAt = observedAt,
            RawLine = "You gain experience.",
            SourceByteStart = 0,
            SourceByteEnd = 1,
            LineStatus = ParserLineStatus.Complete,
            EventKind = ParserEventKind.TimestampedLine,
            ClassificationStatus = ParserClassificationStatus.Recognized,
            ClassificationRuleId = "xp",
            SourceTimestamp = sourceTimestamp
        };

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

    private sealed class RecordingSessionStore : ISessionStore
    {
        public PersistedSessionDocument? LastCompletedLive { get; private set; }

        public PersistedSessionDocument? LastSaved { get; private set; }

        public string SessionRootDirectory => "test-sessions";

        public IReadOnlyList<string> LegacyBenchmarkFilePaths => [];

        public IReadOnlyList<string> MalformedFileReports => [];

        public IReadOnlyList<PersistedSessionDocument> GetRecentLiveSessions() =>
            LastCompletedLive is null ? [] : [LastCompletedLive];

        public IReadOnlyList<PersistedSessionDocument> GetRecentTrackedSessions() => [];

        public IReadOnlyList<PersistedSessionDocument> GetSavedSessions() =>
            LastSaved is null ? [] : [LastSaved];

        public PersistedSessionDocument? GetSession(Guid sessionId) =>
            LastSaved?.SessionId == sessionId ? LastSaved : LastCompletedLive;

        public string PersistCompletedLiveSession(PersistedSessionDocument document)
        {
            LastCompletedLive = document;
            return Path.Combine(SessionRootDirectory, "Live", $"{document.SessionId}.json");
        }

        public string PersistCompletedTrackedSession(PersistedSessionDocument document) =>
            Path.Combine(SessionRootDirectory, "Tracked", $"{document.SessionId}.json");

        public SessionSaveResult SaveSession(PersistedSessionDocument document)
        {
            LastSaved = document;
            return new SessionSaveResult(
                SessionSaveOutcome.Saved,
                Path.Combine(SessionRootDirectory, "Saved", $"{document.SessionId}.json"));
        }

        public void DeleteSavedSession(Guid sessionId)
        {
            if (LastSaved?.SessionId == sessionId)
            {
                LastSaved = null;
            }
        }
    }
}
