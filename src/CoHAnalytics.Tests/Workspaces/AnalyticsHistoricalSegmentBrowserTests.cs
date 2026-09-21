using System.Windows;
using System.Windows.Threading;
using CoHAnalytics.Models;
using CoHAnalytics.Services;
using CoHAnalytics.Tests.Services;
using CoHAnalytics.ViewModels.Workspaces;

namespace CoHAnalytics.Tests.Workspaces;

[Collection(WpfDispatcherCollection.Name)]
public sealed class AnalyticsHistoricalSegmentBrowserTests
{
    [Fact]
    public void Retained_rows_map_exclusion_expand_metrics_and_toggle_overview_immediately()
    {
        using var fixture = new Fixture();
        var included = fixture.Persist(fixture.Character, experience: 6_200_000, include: true);
        var excluded = fixture.Persist(
            fixture.Character,
            experience: 1_000,
            include: false,
            session: GameplaySessionId.CreateNew(),
            startedAt: Start.AddHours(1));
        using var viewModel = fixture.CreateViewModel();

        Assert.Equal(2, viewModel.HistoricalSegments.Count);
        Assert.Equal(excluded.GameplaySessionId, viewModel.HistoricalSegments[0].GameplaySessionId);
        Assert.False(viewModel.HistoricalSegments[0].IncludeInOverview);
        Assert.True(viewModel.HistoricalSegments[0].IsExcluded);
        Assert.True(viewModel.HistoricalSegments[1].IncludeInOverview);
        Assert.False(viewModel.HistoricalSegments[1].IsExcluded);
        Assert.Equal("1 of 2", viewModel.OverviewSegmentCountLabel);
        Assert.Equal("00:30:00", viewModel.Overview.ObservedDurationLabel);

        var includedRow = viewModel.HistoricalSegments.Single(row =>
            row.GameplaySessionId == included.GameplaySessionId);
        viewModel.SelectHistoricalSegmentCommand.Execute(includedRow);
        Assert.True(includedRow.IsExpanded);
        Assert.Equal("12M", includedRow.ExperiencePerHourLabel);
        Assert.Equal("0", includedRow.InfluencePerHourLabel);
        Assert.Equal("0", includedRow.DamagePerSecondLabel);
        Assert.Equal("—", includedRow.AccuracyLabel);

        viewModel.ToggleHistoricalSegmentExclusionCommand.Execute(includedRow);
        DrainDispatcher();

        Assert.Equal(2, viewModel.HistoricalSegments.Count);
        Assert.All(viewModel.HistoricalSegments, row => Assert.False(row.IncludeInOverview));
        Assert.All(viewModel.HistoricalSegments, row => Assert.True(row.IsExcluded));
        Assert.Equal("0 of 2", viewModel.OverviewSegmentCountLabel);
        Assert.Equal("00:00:00", viewModel.IncludedObservedDurationLabel);
        Assert.False(viewModel.Overview.HasHistory);
        Assert.True(viewModel.HasHistoricalSegments);
        Assert.False(viewModel.ShowHistoricalEmptyState);
        Assert.True(viewModel.HistoricalSegments.Single(row =>
            row.GameplaySessionId == included.GameplaySessionId).IsExpanded);
        viewModel.ToggleHistoricalSegmentManagerCommand.Execute(null);
        Assert.True(viewModel.IsHistoricalSegmentManagerExpanded);

        var excludedRow = viewModel.HistoricalSegments.Single(row =>
            row.GameplaySessionId == excluded.GameplaySessionId);
        viewModel.ToggleHistoricalSegmentExclusionCommand.Execute(excludedRow);
        DrainDispatcher();

        Assert.True(viewModel.HistoricalSegments.Single(row =>
            row.GameplaySessionId == excluded.GameplaySessionId).IncludeInOverview);
        Assert.False(viewModel.HistoricalSegments.Single(row =>
            row.GameplaySessionId == excluded.GameplaySessionId).IsExcluded);
        Assert.Equal("1 of 2", viewModel.OverviewSegmentCountLabel);
        Assert.Equal("1,000", viewModel.Overview.TotalExperienceLabel);
    }

    [Fact]
    public void Segment_manager_is_collapsed_by_default_and_preserves_selection_when_reopened()
    {
        using var fixture = new Fixture();
        fixture.Persist(fixture.Character, experience: 100, include: true);
        using var viewModel = fixture.CreateViewModel(new RecordingConfirmationService(confirm: false));

        Assert.False(viewModel.IsHistoricalSegmentManagerExpanded);
        Assert.False(viewModel.DeleteSelectedHistoricalSegmentCommand.CanExecute(null));

        viewModel.ToggleHistoricalSegmentManagerCommand.Execute(null);
        Assert.True(viewModel.IsHistoricalSegmentManagerExpanded);

        var row = Assert.Single(viewModel.HistoricalSegments);
        viewModel.SelectHistoricalSegmentCommand.Execute(row);
        Assert.True(row.IsSelected);
        Assert.True(row.IsExpanded);
        Assert.True(viewModel.DeleteSelectedHistoricalSegmentCommand.CanExecute(null));

        viewModel.ToggleHistoricalSegmentManagerCommand.Execute(null);
        Assert.False(viewModel.IsHistoricalSegmentManagerExpanded);
        viewModel.ToggleHistoricalSegmentManagerCommand.Execute(null);

        Assert.Same(row, viewModel.SelectedHistoricalSegment);
        Assert.True(row.IsExpanded);
        Assert.True(viewModel.DeleteSelectedHistoricalSegmentCommand.CanExecute(null));
    }

    [Fact]
    public void Segment_manager_xaml_follows_primary_workspace_and_uses_shell_scroller()
    {
        var xaml = File.ReadAllText(Path.Combine(
            LocateRepositoryRoot(),
            "src",
            "CoHAnalytics",
            "Workspaces",
            "AnalyticsView.xaml"));

        Assert.Contains("x:Name=\"HistoricalCoveragePanel\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"AnalyticsPrimaryWorkspace\"", xaml, StringComparison.Ordinal);
        Assert.Contains(
            "MinHeight=\"{Binding ViewportHeight, RelativeSource={RelativeSource AncestorType=ScrollViewer}}\"",
            xaml,
            StringComparison.Ordinal);
        Assert.Contains("x:Name=\"HistoricalPrimaryOverview\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"HistoricalMetricPanels\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"HistoricalSegmentManager\"", xaml, StringComparison.Ordinal);
        Assert.Contains(
            "Visibility=\"{Binding IsHistoricalSegmentManagerExpanded, Converter={StaticResource BoolToVisibilityConverter}}\"",
            xaml,
            StringComparison.Ordinal);
        Assert.Contains("Text=\"Overview Segments\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"EXCLUDE\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsChecked=\"{Binding IsExcluded, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Content=\"Delete\"", xaml, StringComparison.Ordinal);
        Assert.Equal(1, xaml.Split("Content=\"Delete Selected\"", StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("x:Name=\"HistoricalOverviewScrollViewer\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("MaxHeight=\"260\"", xaml, StringComparison.Ordinal);

        var coverageIndex = xaml.IndexOf("x:Name=\"HistoricalCoveragePanel\"", StringComparison.Ordinal);
        var metricsIndex = xaml.IndexOf("x:Name=\"HistoricalMetricPanels\"", StringComparison.Ordinal);
        var managerIndex = xaml.IndexOf("x:Name=\"HistoricalSegmentManager\"", StringComparison.Ordinal);
        Assert.True(coverageIndex < metricsIndex);
        Assert.True(metricsIndex < managerIndex);
    }

    [Fact]
    public void Failed_toggle_keeps_persisted_state_and_surfaces_nonfatal_error()
    {
        using var fixture = new Fixture();
        var observation = fixture.Persist(fixture.Character, experience: 100, include: true);
        fixture.ReopenWithFailingUpdates();
        using var viewModel = fixture.CreateViewModel();

        var selected = Assert.Single(viewModel.HistoricalSegments);
        viewModel.SelectHistoricalSegmentCommand.Execute(selected);
        viewModel.ToggleHistoricalSegmentExclusionCommand.Execute(selected);
        DrainDispatcher();

        Assert.True(Assert.Single(viewModel.HistoricalSegments).IncludeInOverview);
        Assert.Equal("1 of 1", viewModel.OverviewSegmentCountLabel);
        Assert.Equal("100", viewModel.Overview.TotalExperienceLabel);
        Assert.True(viewModel.HasHistoricalSegmentError);
        Assert.NotNull(viewModel.HistoricalSegmentErrorMessage);
        Assert.True(fixture.Repository.GetByCharacter(fixture.Character)
            .Single(item => item.GameplaySessionId == observation.GameplaySessionId)
            .IncludeInOverview);
    }

    [Fact]
    public void Character_switch_replaces_rows_without_stale_history()
    {
        using var fixture = new Fixture();
        var other = fixture.CreateCharacter("Other Hero");
        fixture.Persist(fixture.Character, experience: 100);
        var otherObservation = fixture.Persist(
            other,
            experience: 900,
            session: GameplaySessionId.CreateNew());
        using var viewModel = fixture.CreateViewModel();

        fixture.Viewed.SelectViewedCharacter(fixture.AccountStableId, other);
        DrainDispatcher();

        var row = Assert.Single(viewModel.HistoricalSegments);
        Assert.Equal(otherObservation.GameplaySessionId, row.GameplaySessionId);
        Assert.Equal("Other Hero", row.CharacterLabel);
        Assert.Equal("900", viewModel.Overview.TotalExperienceLabel);
    }

    [Fact]
    public void Account_resolution_honors_anonymity_and_missing_account_falls_back_without_stable_id()
    {
        using var fixture = new Fixture();
        fixture.Persist(fixture.Character, experience: 100);
        var missingAccountCharacter = fixture.CreateCharacter("Orphan Hero", "opaque-missing-account-id");
        fixture.Persist(
            missingAccountCharacter,
            experience: 200,
            session: GameplaySessionId.CreateNew());
        using var viewModel = fixture.CreateViewModel();

        Assert.Equal("TestAccount", Assert.Single(viewModel.HistoricalSegments).AccountLabel);

        fixture.Anonymity.SetEnabled(true);
        DrainDispatcher();
        Assert.Equal(new string('█', "TestAccount".Length),
            Assert.Single(viewModel.HistoricalSegments).AccountLabel);

        fixture.Viewed.SelectViewedCharacter("opaque-missing-account-id", missingAccountCharacter);
        DrainDispatcher();
        Assert.Equal("Unknown account", Assert.Single(viewModel.HistoricalSegments).AccountLabel);
    }

    [Fact]
    public void Cancelled_delete_preserves_segment_and_uses_human_readable_anonymized_context()
    {
        using var fixture = new Fixture();
        var observation = fixture.Persist(fixture.Character, experience: 100);
        fixture.Anonymity.SetEnabled(true);
        var confirmation = new RecordingConfirmationService(confirm: false);
        using var viewModel = fixture.CreateViewModel(confirmation);
        var row = Assert.Single(viewModel.HistoricalSegments);

        viewModel.SelectHistoricalSegmentCommand.Execute(row);
        Assert.True(viewModel.DeleteSelectedHistoricalSegmentCommand.CanExecute(null));
        viewModel.DeleteSelectedHistoricalSegmentCommand.Execute(null);
        DrainDispatcher();

        var request = Assert.Single(confirmation.Requests);
        Assert.Equal(row.CharacterLabel, request.CharacterLabel);
        Assert.Equal(row.AccountLabel, request.AccountLabel);
        Assert.Equal(new string('█', "TestAccount".Length), request.AccountLabel);
        Assert.Equal(row.ConfirmationDateTimeLabel, request.DateTimeLabel);
        Assert.Contains("2026", request.DateTimeLabel, StringComparison.Ordinal);
        Assert.Single(viewModel.HistoricalSegments);
        Assert.Single(fixture.Repository.GetByCharacter(fixture.Character));
        Assert.Equal(observation.GameplaySessionId,
            Assert.Single(fixture.Repository.GetByCharacter(fixture.Character)).GameplaySessionId);
        Assert.False(viewModel.HasHistoricalSegmentError);
    }

    [Fact]
    public void Confirmed_delete_removes_exact_included_row_and_recalculates_overview()
    {
        using var fixture = new Fixture();
        var deleted = fixture.Persist(fixture.Character, experience: 100);
        var retained = fixture.Persist(
            fixture.Character,
            experience: 900,
            session: GameplaySessionId.CreateNew(),
            startedAt: Start.AddHours(1));
        var confirmation = new RecordingConfirmationService(confirm: true);
        using var viewModel = fixture.CreateViewModel(confirmation);
        var deletedRow = viewModel.HistoricalSegments.Single(row =>
            row.GameplaySessionId == deleted.GameplaySessionId);
        viewModel.SelectHistoricalSegmentCommand.Execute(deletedRow);
        Assert.True(deletedRow.IsExpanded);

        viewModel.DeleteSelectedHistoricalSegmentCommand.Execute(null);
        DrainDispatcher();

        Assert.Single(confirmation.Requests);
        var row = Assert.Single(viewModel.HistoricalSegments);
        Assert.Equal(retained.GameplaySessionId, row.GameplaySessionId);
        Assert.Equal("1 of 1", viewModel.OverviewSegmentCountLabel);
        Assert.Equal("900", viewModel.Overview.TotalExperienceLabel);
        Assert.Null(viewModel.SelectedHistoricalSegment);
        Assert.False(viewModel.DeleteSelectedHistoricalSegmentCommand.CanExecute(null));
        Assert.DoesNotContain(viewModel.HistoricalSegments, item => item.IsExpanded);
        Assert.DoesNotContain(fixture.Repository.GetByCharacter(fixture.Character), item =>
            item.GameplaySessionId == deleted.GameplaySessionId &&
            item.SegmentOrdinal == deleted.SegmentOrdinal);
    }

    [Fact]
    public void Deleting_excluded_row_preserves_included_aggregate()
    {
        using var fixture = new Fixture();
        fixture.Persist(fixture.Character, experience: 100, include: true);
        var excluded = fixture.Persist(
            fixture.Character,
            experience: 900,
            include: false,
            session: GameplaySessionId.CreateNew(),
            startedAt: Start.AddHours(1));
        using var viewModel = fixture.CreateViewModel(new RecordingConfirmationService(confirm: true));
        var excludedRow = viewModel.HistoricalSegments.Single(row =>
            row.GameplaySessionId == excluded.GameplaySessionId);

        viewModel.SelectHistoricalSegmentCommand.Execute(excludedRow);
        viewModel.DeleteSelectedHistoricalSegmentCommand.Execute(null);
        DrainDispatcher();

        Assert.Single(viewModel.HistoricalSegments);
        Assert.Equal("1 of 1", viewModel.OverviewSegmentCountLabel);
        Assert.Equal("100", viewModel.Overview.TotalExperienceLabel);
    }

    [Fact]
    public void Deleting_final_row_shows_clean_empty_state()
    {
        using var fixture = new Fixture();
        fixture.Persist(fixture.Character, experience: 100);
        using var viewModel = fixture.CreateViewModel(new RecordingConfirmationService(confirm: true));
        viewModel.ToggleHistoricalSegmentManagerCommand.Execute(null);
        Assert.True(viewModel.IsHistoricalSegmentManagerExpanded);
        var row = Assert.Single(viewModel.HistoricalSegments);
        viewModel.SelectHistoricalSegmentCommand.Execute(row);

        viewModel.DeleteSelectedHistoricalSegmentCommand.Execute(null);
        DrainDispatcher();

        Assert.Empty(viewModel.HistoricalSegments);
        Assert.False(viewModel.HasHistoricalSegments);
        Assert.True(viewModel.ShowHistoricalEmptyState);
        Assert.False(viewModel.Overview.HasHistory);
        Assert.Equal("0 of 0", viewModel.OverviewSegmentCountLabel);
        Assert.Null(viewModel.SelectedHistoricalSegment);
        Assert.False(viewModel.IsHistoricalSegmentManagerExpanded);
        Assert.False(viewModel.DeleteSelectedHistoricalSegmentCommand.CanExecute(null));
    }

    [Fact]
    public void Failed_delete_keeps_row_and_aggregate_and_surfaces_nonfatal_error()
    {
        using var fixture = new Fixture();
        var observation = fixture.Persist(fixture.Character, experience: 100);
        fixture.ReopenWithFailingDeletes();
        using var viewModel = fixture.CreateViewModel(new RecordingConfirmationService(confirm: true));

        var row = Assert.Single(viewModel.HistoricalSegments);
        viewModel.SelectHistoricalSegmentCommand.Execute(row);
        viewModel.DeleteSelectedHistoricalSegmentCommand.Execute(null);
        DrainDispatcher();

        Assert.Equal(observation.GameplaySessionId,
            Assert.Single(viewModel.HistoricalSegments).GameplaySessionId);
        Assert.Equal("1 of 1", viewModel.OverviewSegmentCountLabel);
        Assert.Equal("100", viewModel.Overview.TotalExperienceLabel);
        Assert.True(viewModel.HasHistoricalSegmentError);
        Assert.Single(fixture.Repository.GetByCharacter(fixture.Character));
        Assert.NotNull(viewModel.SelectedHistoricalSegment);
        Assert.True(viewModel.DeleteSelectedHistoricalSegmentCommand.CanExecute(null));
    }

    [Fact]
    public void Empty_history_keeps_manager_and_delete_selection_inert()
    {
        using var fixture = new Fixture();
        using var viewModel = fixture.CreateViewModel(new RecordingConfirmationService(confirm: true));

        Assert.False(viewModel.HasHistoricalSegments);
        Assert.True(viewModel.ShowHistoricalEmptyState);
        Assert.False(viewModel.IsHistoricalSegmentManagerExpanded);
        Assert.Null(viewModel.SelectedHistoricalSegment);
        Assert.False(viewModel.DeleteSelectedHistoricalSegmentCommand.CanExecute(null));

        viewModel.ToggleHistoricalSegmentManagerCommand.Execute(null);
        Assert.False(viewModel.IsHistoricalSegmentManagerExpanded);
    }

    private static void DrainDispatcher()
    {
        if (Application.Current?.Dispatcher is { } dispatcher)
        {
            dispatcher.Invoke(() => { }, DispatcherPriority.Background);
            dispatcher.Invoke(() => { }, DispatcherPriority.Background);
        }
    }

    private static string LocateRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "src", "CoHAnalytics.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Repository root could not be located.");
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(),
            "coh-analytics-segment-browser",
            Guid.NewGuid().ToString("n"));
        private readonly string _installRoot;

        public Fixture()
        {
            Directory.CreateDirectory(_root);
            _installRoot = HomecomingRuntimeTestSupport.CreateInstallRoot();
            Directory.CreateDirectory(Path.Combine(_installRoot, "accounts", "TestAccount", "Logs"));
            var settings = new SettingsService(Path.Combine(_root, "settings"));
            var installation = new HomecomingInstallationService(settings);
            Assert.True(installation.TryConfigureInstallRoot(_installRoot, out var failureReason), failureReason);
            AccountDiscovery = new HomecomingAccountDiscoveryService(installation);
            AccountStableId = Assert.Single(AccountDiscovery.Discover()).StableId;
            CharacterRepository = new CharacterRepository(new CharacterRepositoryOptions
            {
                DataDirectory = _root
            });
            Character = CreateCharacter("Dawn's Vanguard");
            Repository = new CharacterPerformanceObservationRepository(_root);
            ReadService = new CharacterHistoricalPerformanceReadService(Repository, CharacterRepository);
            Viewed = new TestGameplaySessionContextSupport.FakeViewedContextService(
                TestGameplaySessionContextSupport.PinnedCharacter(AccountStableId, Character));
            Anonymity = new AccountAnonymityService(StubInternalFeatureGate.Enabled);
        }

        public string AccountStableId { get; }
        public CharacterRecordId Character { get; }
        public CharacterRepository CharacterRepository { get; }
        public CharacterPerformanceObservationRepository Repository { get; private set; }
        public CharacterHistoricalPerformanceReadService ReadService { get; private set; }
        public HomecomingAccountDiscoveryService AccountDiscovery { get; }
        public TestGameplaySessionContextSupport.FakeViewedContextService Viewed { get; }
        public AccountAnonymityService Anonymity { get; }

        public CharacterRecordId CreateCharacter(string name, string? accountStableId = null) =>
            Assert.IsType<CharacterRecordId>(CharacterRepository
                .EstablishTrustedFromWelcome(accountStableId ?? AccountStableId, name, Start)
                .RecordId);

        public CharacterPerformanceObservation Persist(
            CharacterRecordId character,
            long experience,
            bool include = true,
            GameplaySessionId? session = null,
            DateTimeOffset? startedAt = null)
        {
            var start = startedAt ?? Start;
            var observation = new CharacterPerformanceObservation
            {
                GameplaySessionId = session ?? GameplaySessionId.CreateNew(),
                SegmentOrdinal = 0,
                CharacterRecordId = character,
                StartedAtUtc = start,
                EndedAtUtc = start.AddMinutes(30),
                IncludeInOverview = include,
                ExperienceGained = experience
            };
            Assert.True(Repository.Persist(observation).IsSuccess);
            return observation;
        }

        public void ReopenWithFailingUpdates()
        {
            Repository = new CharacterPerformanceObservationRepository(
                _root,
                (_, _) => throw new IOException("Simulated replacement failure."));
            ReadService = new CharacterHistoricalPerformanceReadService(Repository, CharacterRepository);
        }

        public void ReopenWithFailingDeletes()
        {
            Repository = new CharacterPerformanceObservationRepository(
                _root,
                (temporaryPath, finalPath) => File.Move(temporaryPath, finalPath, overwrite: true),
                _ => throw new IOException("Simulated deletion failure."));
            ReadService = new CharacterHistoricalPerformanceReadService(Repository, CharacterRepository);
        }

        public AnalyticsViewModel CreateViewModel(
            IHistoricalSegmentDeleteConfirmationService? confirmationService = null)
        {
            var identity = new FakeIdentityReadService();
            return new AnalyticsViewModel(
                new TestGameplaySessionContextSupport.FakeApplicationOrchestrator(),
                new FakeGameRuntimeService { CurrentStatus = GameRuntimeStatus.Off },
                identity,
                new GameplaySessionContextResolver(identity, Viewed),
                Viewed,
                Anonymity,
                ReadService,
                Repository,
                CharacterRepository,
                AccountDiscovery,
                confirmationService);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_root, recursive: true);
                Directory.Delete(_installRoot, recursive: true);
            }
            catch
            {
            }
        }
    }

    private sealed class RecordingConfirmationService(bool confirm)
        : IHistoricalSegmentDeleteConfirmationService
    {
        public List<HistoricalSegmentDeleteConfirmationRequest> Requests { get; } = [];

        public bool ConfirmDelete(HistoricalSegmentDeleteConfirmationRequest request)
        {
            Requests.Add(request);
            return confirm;
        }
    }

    private sealed class FakeIdentityReadService : IGameplaySessionIdentityReadService
    {
        public GameplaySessionIdentityReadModelSnapshot Current =>
            GameplaySessionIdentityReadModelSnapshot.Empty;

        public event EventHandler<GameplaySessionIdentityReadModelChangedEventArgs>? Changed
        {
            add { }
            remove { }
        }
    }

    private static readonly DateTimeOffset Start =
        new(2026, 8, 24, 0, 14, 0, TimeSpan.Zero);
}
