using CoHAnalytics.Models;
using CoHAnalytics.Orchestration;
using CoHAnalytics.Orchestration.Contributors;
using CoHAnalytics.Services;
using CoHAnalytics.Tests.Orchestration;
using CoHAnalytics.Tests.Services;

namespace CoHAnalytics.Tests.Integration;

/// <summary>
/// End-to-end tests proving the manager consumes the real <see cref="LogActivityService"/>
/// instance (not a fake) through the full startup path, and that composition remains at six
/// contributors after Slice 5B.
/// </summary>
public sealed class MonitoringSessionSourceSelectionCompositionTests
{
    [Fact]
    public async Task Manager_auto_creates_context_from_real_log_activity_growth()
    {
        using var environment = new LogActivityTestEnvironment();
        var account = environment.AddAccount("Alpha");
        environment.WriteLog(account, environment.Today, "line one\n");

        using var logActivityService = environment.CreateService();
        await logActivityService.StartAsync();

        environment.Append(account, environment.Today, "line two\n");
        await logActivityService.ScanAsync();
        Assert.Equal(1, logActivityService.Current.GrowingCount);

        var runtime = new FakeGameRuntimeService { CurrentStatus = GameRuntimeStatus.Running };
        using var manager = new MonitoringSessionManager(
            runtime,
            logActivityService,
            new MonitoringSessionManagerOptions { TimeProvider = environment.Time });

        await manager.StartAsync();

        var context = Assert.Single(manager.Current.Contexts);
        Assert.Equal(MonitoringContextState.Ready, context.State);
        Assert.Equal(account.StableId, context.AccountStableId);

        await manager.StopAsync();
        await logActivityService.StopAsync();
    }

    [Fact]
    public async Task Wine_style_identity_changes_still_create_context_and_parser_worker_after_growth()
    {
        using var environment = new LogActivityTestEnvironment();
        var account = environment.AddAccount("Alpha");
        var path = environment.WriteLog(account, environment.Today, "line one\n");
        var originalCreationTime = File.GetCreationTimeUtc(path);

        using var logActivityService = environment.CreateService();
        await logActivityService.StartAsync();
        var originalSourceId = Assert.Single(logActivityService.Current.Candidates).SourceId;

        environment.Time.Advance(TimeSpan.FromSeconds(1));
        environment.Append(account, environment.Today, "line two\n");
        File.SetCreationTimeUtc(path, originalCreationTime.AddMinutes(-1));
        await logActivityService.ScanAsync();

        environment.Time.Advance(TimeSpan.FromSeconds(1));
        environment.Append(account, environment.Today, "line three\n");
        File.SetCreationTimeUtc(path, originalCreationTime.AddMinutes(-2));
        await logActivityService.ScanAsync();

        var growing = Assert.Single(logActivityService.Current.Candidates);
        Assert.Equal(originalSourceId, growing.SourceId);
        Assert.Equal(LogSourceActivityState.Growing, growing.ActivityState);
        Assert.Equal(0, growing.SourceId.IdentityGeneration);

        var runtime = new FakeGameRuntimeService { CurrentStatus = GameRuntimeStatus.Running };
        using var manager = new MonitoringSessionManager(
            runtime,
            logActivityService,
            new MonitoringSessionManagerOptions { TimeProvider = environment.Time });
        await manager.StartAsync();

        var context = Assert.Single(manager.Current.Contexts);
        Assert.Equal(MonitoringContextState.Ready, context.State);
        Assert.Equal(growing.SourceId, context.CurrentSourceId);

        await using var parser = new ParserManager(manager, ParserTestSnapshots.FastOptions());
        await parser.StartAsync();
        await ParserTestSnapshots.WaitUntilAsync(
            () => parser.Current.Workers.Any(worker => worker.ContextId == context.ContextId));

        var worker = Assert.Single(parser.Current.Workers);
        Assert.Equal(context.ContextId, worker.ContextId);
        Assert.Equal(growing.SourceId, worker.CurrentSourceId);
    }

    [Fact]
    public async Task Six_contributors_remain_registered_with_the_manager_wired_to_real_log_activity()
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

        Assert.Equal(6, orchestrator.Current.Providers.Count);
        Assert.True(manager.IsRunning);
        Assert.True(logActivityService.IsRunning);

        await orchestrator.StopAsync();
        Assert.False(manager.IsRunning);
        Assert.False(logActivityService.IsRunning);

        monitoringContributor.Dispose();
        logActivityContributor.Dispose();
        homecomingRuntimeContributor.Dispose();
        await orchestrator.DisposeAsync();
    }
}
