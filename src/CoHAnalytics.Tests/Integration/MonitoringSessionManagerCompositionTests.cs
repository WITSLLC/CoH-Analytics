using CoHAnalytics.Models;
using CoHAnalytics.Orchestration;
using CoHAnalytics.Orchestration.Contributors;
using CoHAnalytics.Orchestration.Models;
using CoHAnalytics.Services;
using CoHAnalytics.Tests.Orchestration;
using CoHAnalytics.Tests.Services;

namespace CoHAnalytics.Tests.Integration;

public sealed class MonitoringSessionManagerCompositionTests
{
    [Fact]
    public async Task Monitoring_session_manager_is_the_sixth_registered_contributor_and_reaches_running()
    {
        using var environment = new LogActivityTestEnvironment();
        environment.AddAccount("Alpha");

        var manual = environment.Time;
        var runtime = new FakeGameRuntimeService { CurrentStatus = GameRuntimeStatus.Running };

        var orchestrator = new ApplicationOrchestrator(new ApplicationOrchestratorOptions
        {
            TimeProvider = manual,
            DebounceInterval = TimeSpan.FromMilliseconds(10)
        });

        var settings = new SettingsService();
        var installationService = new HomecomingInstallationService(settings);
        var accountService = new HomecomingAccountDiscoveryService(installationService);
        var midsService = new MidsInstallationService(settings);
        installationService.DiscoverAndPersist();
        accountService.Discover();
        midsService.DiscoverAndPersist();

        using var logActivityService = environment.CreateService();
        var logActivityContributor = new LogActivityContributor(logActivityService, manual);
        var homecomingRuntimeContributor = new HomecomingRuntimeContributor(runtime, manual);

        using var manager = new MonitoringSessionManager(runtime, logActivityService, new MonitoringSessionManagerOptions { TimeProvider = manual });
        var monitoringContributor = new MonitoringSessionManagerContributor(manager, manual);

        orchestrator.Register(new HomecomingInstallationContributor(installationService, manual));
        orchestrator.Register(homecomingRuntimeContributor);
        orchestrator.Register(new HomecomingAccountsContributor(accountService, manual));
        orchestrator.Register(new MidsInstallationContributor(midsService, manual));
        orchestrator.Register(logActivityContributor);
        orchestrator.Register(monitoringContributor);

        await orchestrator.StartAsync();

        var diagnostics = orchestrator.GetDiagnostics();

        Assert.Equal(
            [
                ApplicationProviders.Accounts,
                ApplicationProviders.HomecomingInstallation,
                ApplicationProviders.MidsInstallation,
                ApplicationProviders.LogActivity,
                ApplicationProviders.MonitoringSessionManager,
                ApplicationProviders.HomecomingRuntime
            ],
            diagnostics.RegisteredDescriptors
                .Select(descriptor => descriptor.ProviderId)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray());

        Assert.Contains(ApplicationCapabilities.MonitoringContexts, diagnostics.CapabilityProducers.Keys);
        Assert.Contains(ApplicationCapabilities.MonitoringSourceSelection, diagnostics.CapabilityProducers.Keys);
        Assert.Equal(
            ApplicationProviders.MonitoringSessionManager,
            diagnostics.CapabilityProducers[ApplicationCapabilities.MonitoringContexts]);

        Assert.Equal(6, orchestrator.Current.Providers.Count);
        Assert.All(
            orchestrator.Current.Providers,
            summary => Assert.Equal(ApplicationContributorLifecycleState.Running, summary.LifecycleState));

        Assert.True(manager.IsRunning);
        Assert.Equal(1, orchestrator.Current.Providers.Count(p => p.ProviderId == ApplicationProviders.MonitoringSessionManager));

        await orchestrator.StopAsync();
        Assert.False(manager.IsRunning);
        Assert.False(logActivityService.IsRunning);

        monitoringContributor.Dispose();
        logActivityContributor.Dispose();
        homecomingRuntimeContributor.Dispose();
        await orchestrator.DisposeAsync();
    }

    [Fact]
    public async Task Runtime_transition_reaches_orchestrator_without_log_activity_scan()
    {
        var manual = new ManualTimeProvider();
        var runtime = new FakeGameRuntimeService { CurrentStatus = GameRuntimeStatus.Running };

        using var manager = new MonitoringSessionManager(runtime, new FakeLogActivityService(), new MonitoringSessionManagerOptions { TimeProvider = manual });
        await manager.StartAsync();
        manager.AddContext(sourceId: LogSourceId.Create("acct-1", "Alpha", @"C:\fake\Logs\chatlog 2026-08-04.txt", new DateOnly(2026, 8, 4)));

        var contributor = new MonitoringSessionManagerContributor(manager, manual);
        var contributionChangedCount = 0;
        contributor.ContributionChanged += (_, _) => contributionChangedCount++;

        // Runtime -> manager -> contributor, with no ILogActivityService reference anywhere in
        // this chain.
        runtime.RaiseStatusChanged(GameRuntimeStatus.Running, GameRuntimeStatus.Off);

        Assert.Equal(1, contributionChangedCount);
        var contribution = await contributor.GetContributionAsync();
        Assert.Equal(ContributorActivity.Waiting, contribution.Activity);
        Assert.Equal(ContributorHealth.Ready, contribution.Health);

        await manager.StopAsync();
        contributor.Dispose();
    }

    [Fact]
    public async Task Shutdown_is_clean_and_idempotent_across_contributor_and_manager()
    {
        var runtime = new FakeGameRuntimeService { CurrentStatus = GameRuntimeStatus.Running };
        var manager = new MonitoringSessionManager(runtime, new FakeLogActivityService());
        var contributor = new MonitoringSessionManagerContributor(manager);

        await contributor.StartAsync();
        await contributor.StopAsync();
        await contributor.StopAsync();

        contributor.Dispose();
        contributor.Dispose();

        manager.Dispose();
        manager.Dispose();

        Assert.False(manager.IsRunning);
    }
}
