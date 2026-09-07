using System.Windows;
using System.Windows.Threading;
using CoHAnalytics.Models;
using CoHAnalytics.Services;
using CoHAnalytics.Tests.Services;
using CoHAnalytics.ViewModels.Workspaces;

namespace CoHAnalytics.Tests.Workspaces;

[Collection(WpfDispatcherCollection.Name)]
public sealed class AnalyticsOverviewEarningsTests
{
    [Fact]
    public void No_selected_character_shows_no_character_state()
    {
        using var history = new HistoricalFixture();
        using var viewModel = CreateViewModel(
            new FakeIdentityReadService(),
            new TestGameplaySessionContextSupport.FakeViewedContextService(
                TestGameplaySessionContextSupport.FollowingLive()),
            history.ReadService);

        Assert.True(viewModel.Overview.ShowNoCharacter);
        Assert.False(viewModel.Overview.ShowNoHistory);
        Assert.False(viewModel.Overview.ShowHistoricalMetrics);
    }

    [Fact]
    public void Selected_character_without_history_shows_clean_no_history_state()
    {
        var character = CharacterRecordId.CreateNew();
        using var history = new HistoricalFixture();
        using var viewModel = CreateViewModel(
            new FakeIdentityReadService(),
            ViewedCharacter(character),
            history.ReadService);

        Assert.True(viewModel.Overview.ShowNoHistory);
        Assert.False(viewModel.Overview.ShowNoCharacter);
        Assert.Equal("—", viewModel.Overview.HistoricalDpsLabel);
        Assert.Equal("—", viewModel.Overview.ExperiencePerHourLabel);
    }

    [Fact]
    public void Offline_character_displays_aggregated_history_and_coverage()
    {
        var character = CharacterRecordId.CreateNew();
        using var history = new HistoricalFixture();
        Assert.True(history.Repository.Persist(CreateObservation(character, experience: 1_000) with
        {
            DamageDealt = new CombatScaledAmount(600_000),
            Attempts = 10,
            Hits = 8,
            RolledAttempts = 10,
            DisplayedChanceSumHundredths = 75_000,
            RollSumHundredths = 50_000,
            TotalDefeated = 5,
            MyDefeats = 4,
            GameplayInfluenceGained = 200
        }).IsSuccess);
        Assert.True(history.Repository.Persist(CreateObservation(
            character,
            experience: 500,
            session: GameplaySessionId.CreateNew()) with
        {
            GameplayInfluenceGained = 100
        }).IsSuccess);

        using var viewModel = CreateViewModel(
            new FakeIdentityReadService(),
            ViewedCharacter(character),
            history.ReadService);

        Assert.False(viewModel.HasActiveSession);
        Assert.True(viewModel.Overview.ShowHistoricalMetrics);
        Assert.Equal("2", viewModel.Overview.ObservationCountLabel);
        Assert.Equal("01:00:00", viewModel.Overview.ObservedDurationLabel);
        Assert.Equal("80.0%", viewModel.Overview.HitPercentLabel);
        Assert.Equal(
            GameplaySessionTelemetryPresentation.FormatCompactRate(1_500),
            viewModel.Overview.ExperiencePerHourLabel);
        Assert.Equal("1,500", viewModel.Overview.TotalExperienceLabel);
        Assert.Equal("300/hr", viewModel.Overview.InfluencePerHourLabel);
    }

    [Fact]
    public void Live_character_shows_history_while_live_combat_context_remains_available()
    {
        var character = CharacterRecordId.CreateNew();
        var contextId = MonitoringContextId.CreateNew();
        var startedAt = DateTimeOffset.UtcNow.AddMinutes(-2);
        var combat = new CombatSnapshot
        {
            DamageDealt = new CombatScaledAmount(2_000 * CombatScaledAmount.Scale),
            SessionDamagePerSecondHundredths = 2_000
        };
        var context = TestGameplaySessionContextSupport.CreateContext(
            contextId,
            "acct",
            character,
            "Hero",
            startedAt,
            experience: 12_000,
            influence: 4_500,
            combat: combat,
            retainedCombatEventCount: 1);
        var identity = new FakeIdentityReadService
        {
            Current = GameplaySessionIdentityReadModelSnapshot.Create([context], DateTimeOffset.UtcNow, 1)
        };
        using var history = new HistoricalFixture();
        Assert.True(history.Repository.Persist(CreateObservation(character, experience: 600)).IsSuccess);

        using var viewModel = CreateViewModel(
            identity,
            ViewedCharacter(character, contextId, isFollowingLive: true),
            history.ReadService);

        Assert.True(viewModel.HasActiveSession);
        Assert.True(viewModel.Overview.ShowHistoricalMetrics);
        Assert.Equal(
            GameplaySessionTelemetryPresentation.FormatCompactRate(1_200),
            viewModel.Overview.ExperiencePerHourLabel);

        viewModel.SelectChipCommand.Execute(AnalyticsChipId.Combat);
        DrainDispatcher();
        Assert.NotEqual("—", viewModel.SessionDpsLabel);
        Assert.True(viewModel.ShowCombatContent);
        Assert.DoesNotContain(viewModel.Chips, chip => chip.ChipId == AnalyticsChipId.Earnings);
    }

    [Fact]
    public void Character_switch_replaces_all_historical_values_without_stale_metrics()
    {
        var characterA = CharacterRecordId.CreateNew();
        var characterB = CharacterRecordId.CreateNew();
        using var history = new HistoricalFixture();
        Assert.True(history.Repository.Persist(CreateObservation(characterA, experience: 100)).IsSuccess);
        Assert.True(history.Repository.Persist(CreateObservation(characterB, experience: 900)).IsSuccess);
        var viewed = ViewedCharacter(characterA);
        using var viewModel = CreateViewModel(
            new FakeIdentityReadService(),
            viewed,
            history.ReadService);

        Assert.Equal("100", viewModel.Overview.TotalExperienceLabel);

        viewed.SelectViewedCharacter("acct", characterB);
        DrainDispatcher();

        Assert.Equal("900", viewModel.Overview.TotalExperienceLabel);
        Assert.Equal(
            GameplaySessionTelemetryPresentation.FormatCompactRate(1_800),
            viewModel.Overview.ExperiencePerHourLabel);
    }

    [Fact]
    public void Persist_notifications_refresh_Clear_and_terminal_observation_boundaries()
    {
        var character = CharacterRecordId.CreateNew();
        using var history = new HistoricalFixture();
        using var viewModel = CreateViewModel(
            new FakeIdentityReadService(),
            ViewedCharacter(character),
            history.ReadService);
        Assert.True(viewModel.Overview.ShowNoHistory);

        Assert.True(history.Repository.Persist(CreateObservation(character, experience: 100)).IsSuccess);
        DrainDispatcher();
        Assert.Equal("1", viewModel.Overview.ObservationCountLabel);
        Assert.Equal("100", viewModel.Overview.TotalExperienceLabel);

        Assert.True(history.Repository.Persist(CreateObservation(
            character,
            experience: 300,
            session: GameplaySessionId.CreateNew())).IsSuccess);
        DrainDispatcher();
        Assert.Equal("2", viewModel.Overview.ObservationCountLabel);
        Assert.Equal("400", viewModel.Overview.TotalExperienceLabel);
    }

    [Fact]
    public void Valid_history_with_zero_earnings_is_not_presented_as_no_history()
    {
        var character = CharacterRecordId.CreateNew();
        using var history = new HistoricalFixture();
        Assert.True(history.Repository.Persist(CreateObservation(character)).IsSuccess);
        using var viewModel = CreateViewModel(
            new FakeIdentityReadService(),
            ViewedCharacter(character),
            history.ReadService);

        Assert.True(viewModel.Overview.ShowHistoricalMetrics);
        Assert.Equal("—", viewModel.Overview.HitPercentLabel);
        Assert.Equal("0/hr", viewModel.Overview.ExperiencePerHourLabel);
        Assert.Equal("0", viewModel.Overview.TotalExperienceLabel);
        Assert.Equal("0/hr", viewModel.Overview.InfluencePerHourLabel);
    }

    private static AnalyticsViewModel CreateViewModel(
        IGameplaySessionIdentityReadService identityReadService,
        TestGameplaySessionContextSupport.FakeViewedContextService viewedContextService,
        ICharacterHistoricalPerformanceReadService historicalReadService)
    {
        var resolver = new GameplaySessionContextResolver(identityReadService, viewedContextService);
        return new AnalyticsViewModel(
            new TestGameplaySessionContextSupport.FakeApplicationOrchestrator(),
            new FakeGameRuntimeService
            {
                CurrentStatus = GameRuntimeStatus.Off
            },
            identityReadService,
            resolver,
            viewedContextService,
            historicalPerformanceReadService: historicalReadService);
    }

    private static TestGameplaySessionContextSupport.FakeViewedContextService ViewedCharacter(
        CharacterRecordId character,
        MonitoringContextId? contextId = null,
        bool isFollowingLive = false) =>
        new(new ViewedContextState
        {
            AccountStableId = "acct",
            CharacterRecordId = character,
            IsFollowingLive = isFollowingLive,
            LiveFollowContextId = contextId
        });

    private static CharacterPerformanceObservation CreateObservation(
        CharacterRecordId character,
        long experience = 0,
        GameplaySessionId? session = null) =>
        new()
        {
            CharacterRecordId = character,
            GameplaySessionId = session ?? GameplaySessionId.CreateNew(),
            SegmentOrdinal = 0,
            StartedAtUtc = Start,
            EndedAtUtc = Start.AddMinutes(30),
            ExperienceGained = experience
        };

    private static void DrainDispatcher()
    {
        if (Application.Current?.Dispatcher is { } dispatcher)
        {
            dispatcher.Invoke(() => { }, DispatcherPriority.Background);
            dispatcher.Invoke(() => { }, DispatcherPriority.Background);
        }
    }

    private sealed class HistoricalFixture : IDisposable
    {
        private readonly string _dataDirectory = Path.Combine(
            Path.GetTempPath(),
            "coh-historical-overview-tests",
            Guid.NewGuid().ToString("n"));

        public HistoricalFixture()
        {
            Repository = new CharacterPerformanceObservationRepository(_dataDirectory);
            ReadService = new CharacterHistoricalPerformanceReadService(Repository);
        }

        public CharacterPerformanceObservationRepository Repository { get; }

        public CharacterHistoricalPerformanceReadService ReadService { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_dataDirectory, recursive: true);
            }
            catch
            {
            }
        }
    }

    private sealed class FakeIdentityReadService : IGameplaySessionIdentityReadService
    {
        public GameplaySessionIdentityReadModelSnapshot Current { get; set; } =
            GameplaySessionIdentityReadModelSnapshot.Empty;

        public event EventHandler<GameplaySessionIdentityReadModelChangedEventArgs>? Changed
        {
            add { }
            remove { }
        }
    }

    private static readonly DateTimeOffset Start =
        new(2026, 8, 18, 12, 0, 0, TimeSpan.Zero);
}
