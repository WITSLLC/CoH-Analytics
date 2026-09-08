using CoHAnalytics.Models;
using CoHAnalytics.Navigation;
using CoHAnalytics.Orchestration.Contracts;
using CoHAnalytics.Orchestration.Models;
using CoHAnalytics.Services;
using CoHAnalytics.Tests.Services;
using CoHAnalytics.ViewModels.Workspaces;

namespace CoHAnalytics.Tests.Dashboard;

public sealed class DashboardViewModelSnapshotTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Constructor_applies_orchestrator_current_snapshot()
    {
        var snapshot = CreateSnapshot(OverallApplicationState.Ready, revision: 1, stateSummary: "Ready to monitor.");
        using var harness = CreateHarness(snapshot);

        Assert.Equal("Ready", harness.ViewModel.AppStatusHeadline);
        Assert.Equal("Ready to monitor.", harness.ViewModel.AppStatusDetail);
        Assert.Equal("All systems operational", harness.ViewModel.AppStatusSecondaryText);
        Assert.Equal("Ready to monitor.", harness.ViewModel.RecentActivity[0].DisplayMessage);
        Assert.Equal(4, harness.ViewModel.MonitoringTiles.Count);
    }

    [Fact]
    public void SnapshotChanged_updates_app_status_without_changing_locations()
    {
        var initial = CreateSnapshot(OverallApplicationState.Unknown, revision: 0, stateSummary: "Initializing");
        using var harness = CreateHarness(initial);
        var locations = harness.ViewModel.Locations
            .Select(location => (location.Title, location.DisplayPath))
            .ToArray();

        harness.Orchestrator.RaiseSnapshotChanged(
            CreateSnapshot(OverallApplicationState.Waiting, revision: 2, stateSummary: "Waiting for prerequisites."));

        Assert.Equal("Waiting", harness.ViewModel.AppStatusHeadline);
        Assert.Equal("Waiting for prerequisites.", harness.ViewModel.AppStatusDetail);
        Assert.Equal(
            locations,
            harness.ViewModel.Locations.Select(location => (location.Title, location.DisplayPath)));
    }

    [Fact]
    public void Older_revision_is_ignored()
    {
        var initial = CreateSnapshot(OverallApplicationState.Ready, revision: 5, stateSummary: "Current");
        using var harness = CreateHarness(initial);

        harness.Orchestrator.RaiseSnapshotChanged(
            CreateSnapshot(OverallApplicationState.Error, revision: 3, stateSummary: "Stale"));

        Assert.Equal("Ready", harness.ViewModel.AppStatusHeadline);
        Assert.Equal("Current", harness.ViewModel.AppStatusDetail);
    }

    [Fact]
    public void Equal_revision_is_ignored_after_first_application()
    {
        var initial = CreateSnapshot(OverallApplicationState.Ready, revision: 4, stateSummary: "First");
        using var harness = CreateHarness(initial);

        harness.Orchestrator.RaiseSnapshotChanged(
            CreateSnapshot(OverallApplicationState.Error, revision: 4, stateSummary: "Duplicate"));

        Assert.Equal("Ready", harness.ViewModel.AppStatusHeadline);
        Assert.Equal("First", harness.ViewModel.AppStatusDetail);
    }

    [Fact]
    public void Provider_order_does_not_alter_aggregate_presentation()
    {
        var providersA = new[]
        {
            Provider("accounts", ContributorHealth.Ready),
            Provider("runtime.homecoming", ContributorHealth.Ready, ContributorActivity.Active),
            Provider("installation.homecoming", ContributorHealth.Ready),
            Provider("installation.mids", ContributorHealth.Unknown)
        };
        var providersB = providersA.Reverse().ToArray();

        using var harnessA = CreateHarness(CreateSnapshot(OverallApplicationState.Ready, revision: 1, providers: providersA));
        using var harnessB = CreateHarness(CreateSnapshot(OverallApplicationState.Ready, revision: 1, providers: providersB));

        var valuesA = harnessA.ViewModel.MonitoringTiles.Select(tile => $"{tile.Title}:{tile.Value}:{tile.Subtitle}").ToArray();
        var valuesB = harnessB.ViewModel.MonitoringTiles.Select(tile => $"{tile.Title}:{tile.Value}:{tile.Subtitle}").ToArray();

        Assert.Equal(valuesA, valuesB);
    }

    [Fact]
    public void ApplySnapshot_does_not_mutate_snapshot_provider_collection()
    {
        var providers = new List<ProviderSummary>
        {
            Provider("installation.homecoming", ContributorHealth.Ready)
        };
        var snapshot = CreateSnapshot(OverallApplicationState.Ready, revision: 1, providers: providers);
        using var harness = CreateHarness(snapshot);

        harness.ViewModel.ApplySnapshot(snapshot);

        Assert.Single(providers);
        Assert.Equal("installation.homecoming", providers[0].ProviderId);
    }

    [Fact]
    public void Primary_issue_drives_warning_banner()
    {
        var issue = new ApplicationIssue(
            "installation.homecoming.not_found",
            ApplicationIssueSeverity.Error,
            "Homecoming installation was not discovered.",
            "installation.homecoming",
            Now);
        var snapshot = CreateSnapshot(
            OverallApplicationState.Error,
            revision: 1,
            stateSummary: "Error",
            primaryIssue: issue);
        using var harness = CreateHarness(snapshot);

        Assert.Equal("ATTENTION NEEDED", harness.ViewModel.MonitoringHeadline);
        Assert.Equal("Homecoming installation was not discovered.", harness.ViewModel.MonitoringMessage);
        Assert.Equal(DashboardStatusKind.Error, harness.ViewModel.MonitoringBannerKind);
        Assert.Equal("Homecoming installation was not discovered.", harness.ViewModel.RecentActivity[0].DisplayMessage);
    }

    [Fact]
    public void No_primary_issue_uses_calm_state_summary_for_banner()
    {
        var snapshot = CreateSnapshot(OverallApplicationState.Ready, revision: 1, stateSummary: "Ready to monitor.");
        using var harness = CreateHarness(snapshot);

        Assert.Equal("STATUS", harness.ViewModel.MonitoringHeadline);
        Assert.Equal("Ready to monitor.", harness.ViewModel.MonitoringMessage);
        Assert.Equal(DashboardStatusKind.Success, harness.ViewModel.MonitoringBannerKind);
    }

    [Fact]
    public void Dispose_unsubscribes_and_ignores_later_snapshot_events()
    {
        var initial = CreateSnapshot(OverallApplicationState.Ready, revision: 1, stateSummary: "Ready");
        var harness = CreateHarness(initial);

        harness.ViewModel.Dispose();
        harness.Orchestrator.RaiseSnapshotChanged(
            CreateSnapshot(OverallApplicationState.Error, revision: 9, stateSummary: "After dispose"));

        Assert.Equal("Ready", harness.ViewModel.AppStatusHeadline);
        harness.Dispose();
    }

    [Fact]
    public void Game_status_remains_independent_from_orchestrator_snapshot()
    {
        var runtime = new FakeGameRuntimeService { CurrentStatus = GameRuntimeStatus.Running };
        var snapshot = CreateSnapshot(OverallApplicationState.Waiting, revision: 1, stateSummary: "Waiting");
        using var harness = CreateHarness(snapshot, runtime);

        Assert.Equal(GameStatusState.Running, harness.ViewModel.GameStatus);
        Assert.Equal("Online", harness.ViewModel.GameStatusLabel);
        Assert.Equal("Waiting", harness.ViewModel.AppStatusHeadline);
    }

    [Fact]
    public void Environment_tiles_and_accounts_remain_populated_from_domain_services()
    {
        using var harness = CreateHarness(CreateSnapshot(OverallApplicationState.Ready, revision: 1));

        Assert.Equal(4, harness.ViewModel.EnvironmentTiles.Count);
        Assert.NotNull(harness.ViewModel.Accounts);
    }

    [Fact]
    public void Recent_activity_displays_only_the_newest_four_entries()
    {
        using var harness = CreateHarness(CreateSnapshot(OverallApplicationState.Ready, revision: 1));

        for (var index = 1; index <= 5; index++)
        {
            harness.ActivityLog.Record($"test.{index}", $"Message {index}", Now.AddMinutes(index));
        }

        Assert.Equal(4, harness.ViewModel.RecentActivity.Count);
        Assert.Equal("Message 5", harness.ViewModel.RecentActivity[0].DisplayMessage);
        Assert.Equal("Message 2", harness.ViewModel.RecentActivity[3].DisplayMessage);
    }

    [Fact]
    public void Locations_always_include_application_data_root()
    {
        using var harness = CreateHarness(CreateSnapshot(OverallApplicationState.Ready, revision: 1));

        var location = Assert.Single(harness.ViewModel.Locations);
        Assert.Equal("CoH Analytics Data", location.Title);
        Assert.Equal(harness.ApplicationDataRoot, location.DisplayPath);
    }

    [Fact]
    public void Locations_include_known_homecoming_installation()
    {
        using var harness = CreateHarness(
            CreateSnapshot(OverallApplicationState.Ready, revision: 1),
            configureInstallation: true);

        var location = Assert.Single(
            harness.ViewModel.Locations,
            item => item.Title == "Homecoming Installation");
        Assert.Equal(harness.InstallRoot, location.DisplayPath);
    }

    [Fact]
    public void Locations_omit_missing_homecoming_installation()
    {
        using var harness = CreateHarness(CreateSnapshot(OverallApplicationState.Ready, revision: 1));

        Assert.DoesNotContain(
            harness.ViewModel.Locations,
            location => location.Title == "Homecoming Installation");
    }

    [Fact]
    public void Account_with_logs_folder_adds_logs_location()
    {
        using var harness = CreateHarness(
            CreateSnapshot(OverallApplicationState.Ready, revision: 1),
            accounts: [new TestAccountFolder("TestAccount", HasLogs: true, HasBuilds: false)]);

        var location = Assert.Single(
            harness.ViewModel.Locations,
            item => item.Title == "City of Heroes Chat Logs");
        Assert.Equal(
            Path.Combine(harness.InstallRoot!, "accounts", "TestAccount", "Logs"),
            location.DisplayPath);
    }

    [Fact]
    public void Account_without_logs_folder_adds_no_logs_location()
    {
        using var harness = CreateHarness(
            CreateSnapshot(OverallApplicationState.Ready, revision: 1),
            accounts: [new TestAccountFolder("TestAccount", HasLogs: false, HasBuilds: true)]);

        Assert.DoesNotContain(
            harness.ViewModel.Locations,
            location => location.Title.StartsWith("City of Heroes Chat Logs", StringComparison.Ordinal));
    }

    [Fact]
    public void Account_with_builds_folder_adds_build_import_location()
    {
        using var harness = CreateHarness(
            CreateSnapshot(OverallApplicationState.Ready, revision: 1),
            accounts: [new TestAccountFolder("TestAccount", HasLogs: false, HasBuilds: true)]);

        var location = Assert.Single(
            harness.ViewModel.Locations,
            item => item.Title == "Build Import");
        Assert.Equal(
            Path.Combine(harness.InstallRoot!, "accounts", "TestAccount", "Builds"),
            location.DisplayPath);
    }

    [Fact]
    public void Account_without_builds_folder_adds_no_build_import_location()
    {
        using var harness = CreateHarness(
            CreateSnapshot(OverallApplicationState.Ready, revision: 1),
            accounts: [new TestAccountFolder("TestAccount", HasLogs: true, HasBuilds: false)]);

        Assert.DoesNotContain(
            harness.ViewModel.Locations,
            location => location.Title.StartsWith("Build Import", StringComparison.Ordinal));
    }

    [Fact]
    public void Multiple_account_locations_use_deterministic_numbered_order()
    {
        using var harness = CreateHarness(
            CreateSnapshot(OverallApplicationState.Ready, revision: 1),
            accounts:
            [
                new TestAccountFolder("Zulu", HasLogs: true, HasBuilds: false),
                new TestAccountFolder("alpha", HasLogs: true, HasBuilds: false),
                new TestAccountFolder("Bravo", HasLogs: true, HasBuilds: false)
            ]);

        var locations = harness.ViewModel.Locations
            .Where(location => location.Title.StartsWith("City of Heroes Chat Logs", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(
            [
                "City of Heroes Chat Logs (1 of 3)",
                "City of Heroes Chat Logs (2 of 3)",
                "City of Heroes Chat Logs (3 of 3)"
            ],
            locations.Select(location => location.Title));
        Assert.Equal(
            [
                Path.Combine(harness.InstallRoot!, "accounts", "alpha", "Logs"),
                Path.Combine(harness.InstallRoot!, "accounts", "Bravo", "Logs"),
                Path.Combine(harness.InstallRoot!, "accounts", "Zulu", "Logs")
            ],
            locations.Select(location => location.DisplayPath));
    }

    [Fact]
    public void Account_anonymity_masks_account_folder_segments_in_location_paths()
    {
        const string accountName = "PrivateAccount";
        var anonymity = new AccountAnonymityService(StubInternalFeatureGate.Enabled);
        using var harness = CreateHarness(
            CreateSnapshot(OverallApplicationState.Ready, revision: 1),
            accounts: [new TestAccountFolder(accountName, HasLogs: true, HasBuilds: true)],
            accountAnonymityService: anonymity);
        Assert.Contains(
            harness.ViewModel.Locations,
            location => location.DisplayPath.Contains(accountName, StringComparison.Ordinal));

        anonymity.SetEnabled(true);

        Assert.DoesNotContain(
            harness.ViewModel.Locations,
            location => location.Title.Contains(accountName, StringComparison.OrdinalIgnoreCase)
                        || location.DisplayPath.Contains(accountName, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(
            2,
            harness.ViewModel.Locations.Count(location =>
                location.DisplayPath.Contains(new string('\u2588', accountName.Length), StringComparison.Ordinal)));
    }

    private static TestHarness CreateHarness(
        ApplicationStateSnapshot initialSnapshot,
        FakeGameRuntimeService? runtime = null,
        bool configureInstallation = false,
        IReadOnlyList<TestAccountFolder>? accounts = null,
        AccountAnonymityService? accountAnonymityService = null)
    {
        var testDirectory = Path.Combine(
            Path.GetTempPath(),
            $"coh-dashboard-{Guid.NewGuid():n}");
        var applicationDataRoot = Path.Combine(testDirectory, "application-data");
        var settings = new SettingsService(applicationDataRoot);
        var installationService = new HomecomingInstallationService(settings);
        var accountDiscoveryService = new HomecomingAccountDiscoveryService(installationService);
        var midsInstallationService = new MidsInstallationService(settings);
        var orchestrator = new FakeApplicationOrchestrator(initialSnapshot);
        runtime ??= new FakeGameRuntimeService { CurrentStatus = GameRuntimeStatus.Off };
        var activityDirectory = Path.Combine(testDirectory, "activity");
        var activityLog = new ApplicationActivityLogService(activityDirectory);
        string? installRoot = null;

        if (configureInstallation || accounts is { Count: > 0 })
        {
            installRoot = CreateHomecomingInstallRoot(testDirectory);
            if (!installationService.TryConfigureInstallRoot(installRoot, out var failureReason))
            {
                throw new InvalidOperationException(failureReason);
            }
        }

        if (accounts is not null)
        {
            foreach (var account in accounts)
            {
                CreateAccountFolder(installRoot!, account);
            }

            accountDiscoveryService.Discover();
        }

        var viewModel = new DashboardViewModel(
            orchestrator,
            runtime,
            settings,
            installationService,
            accountDiscoveryService,
            midsInstallationService,
            new NoOpAccountsWorkspaceNavigation(),
            activityLog,
            accountAnonymityService);

        return new TestHarness(
            orchestrator,
            viewModel,
            activityLog,
            testDirectory,
            applicationDataRoot,
            installRoot);
    }

    private static string CreateHomecomingInstallRoot(string testDirectory)
    {
        var installRoot = Path.Combine(testDirectory, "Homecoming");
        Directory.CreateDirectory(Path.Combine(installRoot, "settings", "launcher"));
        var launcherDirectory = Path.Combine(installRoot, "bin", "win64");
        Directory.CreateDirectory(launcherDirectory);
        File.WriteAllBytes(Path.Combine(launcherDirectory, "launcher.exe"), []);
        return installRoot;
    }

    private static void CreateAccountFolder(string installRoot, TestAccountFolder account)
    {
        var accountRoot = Path.Combine(installRoot, "accounts", account.Name);
        Directory.CreateDirectory(accountRoot);
        File.WriteAllText(Path.Combine(accountRoot, "settings.json"), "{}");

        if (account.HasLogs)
        {
            Directory.CreateDirectory(Path.Combine(accountRoot, "Logs"));
        }

        if (account.HasBuilds)
        {
            Directory.CreateDirectory(Path.Combine(accountRoot, "Builds"));
        }
    }

    private static ApplicationStateSnapshot CreateSnapshot(
        OverallApplicationState state,
        long revision,
        string? stateSummary = null,
        ApplicationIssue? primaryIssue = null,
        IReadOnlyList<ProviderSummary>? providers = null) =>
        new(
            state,
            providers ?? [],
            primaryIssue is null ? [] : [primaryIssue],
            primaryIssue,
            [],
            Now)
        {
            StateSummary = stateSummary ?? state.ToString(),
            Revision = revision
        };

    private static ProviderSummary Provider(
        string providerId,
        ContributorHealth health,
        ContributorActivity activity = ContributorActivity.Inactive,
        ApplicationContributorLifecycleState lifecycle = ApplicationContributorLifecycleState.Running) =>
        new(providerId, providerId, health, activity, lifecycle, Now);

    private sealed class TestHarness(
        FakeApplicationOrchestrator orchestrator,
        DashboardViewModel viewModel,
        ApplicationActivityLogService activityLog,
        string testDirectory,
        string applicationDataRoot,
        string? installRoot) : IDisposable
    {
        public FakeApplicationOrchestrator Orchestrator { get; } = orchestrator;

        public DashboardViewModel ViewModel { get; } = viewModel;

        public ApplicationActivityLogService ActivityLog { get; } = activityLog;

        public string ApplicationDataRoot { get; } = applicationDataRoot;

        public string? InstallRoot { get; } = installRoot;

        public void Dispose()
        {
            ViewModel.Dispose();
            if (Directory.Exists(testDirectory))
            {
                Directory.Delete(testDirectory, recursive: true);
            }
        }
    }

    private sealed record TestAccountFolder(string Name, bool HasLogs, bool HasBuilds);

    private sealed class FakeApplicationOrchestrator(ApplicationStateSnapshot current) : IApplicationOrchestrator
    {
        public ApplicationStateSnapshot Current { get; private set; } = current;

        public event EventHandler<ApplicationStateChangedEventArgs>? SnapshotChanged;

        public void RaiseSnapshotChanged(ApplicationStateSnapshot snapshot)
        {
            Current = snapshot;
            SnapshotChanged?.Invoke(this, new ApplicationStateChangedEventArgs(snapshot));
        }

        public Task RefreshAsync(string? providerId = null, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FakeGameRuntimeService : IGameRuntimeService
    {
        public GameRuntimeStatus CurrentStatus { get; set; } = GameRuntimeStatus.Unconfigured;

        public int RunningClientCount { get; set; }

        public IReadOnlyList<HomecomingProcessInstance> RunningClients { get; set; } =
            Array.Empty<HomecomingProcessInstance>();

        public string? LastErrorMessage { get; set; }

        public event EventHandler<GameRuntimeStatusChangedEventArgs>? StatusChanged
        {
            add { }
            remove { }
        }

        public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task LaunchAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void Start()
        {
        }

        public void Stop()
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class NoOpAccountsWorkspaceNavigation : IAccountsWorkspaceNavigation
    {
        public void OpenAccountsWorkspace(string? accountStableId = null)
        {
        }
    }
}
