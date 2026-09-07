using CoHAnalytics.Models;
using CoHAnalytics.Orchestration;
using CoHAnalytics.Orchestration.Contributors;
using CoHAnalytics.Orchestration.Models;
using CoHAnalytics.Services;
using CoHAnalytics.Tests.Orchestration;
using CoHAnalytics.Tests.Services;

namespace CoHAnalytics.Tests.Integration;

public sealed class LogActivityCompositionTests
{
    [Fact]
    public async Task Log_activity_is_the_fifth_registered_contributor_and_reaches_running()
    {
        using var environment = new LogActivityTestEnvironment();
        var account = environment.AddAccount("Alpha");
        environment.WriteLog(account, environment.Today, "existing content");

        var manual = environment.Time;
        var settings = new SettingsService();
        var installationService = new HomecomingInstallationService(settings);
        var accountService = new HomecomingAccountDiscoveryService(installationService);
        var midsService = new MidsInstallationService(settings);
        var launcher = new HomecomingLauncherService(settings, installationService);
        var runtimeService = new HomecomingRuntimeService(installationService, launcher);

        installationService.DiscoverAndPersist();
        accountService.Discover();
        midsService.DiscoverAndPersist();

        var orchestrator = new ApplicationOrchestrator(new ApplicationOrchestratorOptions
        {
            TimeProvider = manual,
            DebounceInterval = TimeSpan.FromMilliseconds(10)
        });

        using var logActivityService = environment.CreateService();
        var runtimeContributor = new HomecomingRuntimeContributor(runtimeService, manual);
        var logActivityContributor = new LogActivityContributor(logActivityService, manual);

        orchestrator.Register(new HomecomingInstallationContributor(installationService, manual));
        orchestrator.Register(runtimeContributor);
        orchestrator.Register(new HomecomingAccountsContributor(accountService, manual));
        orchestrator.Register(new MidsInstallationContributor(midsService, manual));
        orchestrator.Register(logActivityContributor);

        await orchestrator.StartAsync();

        var diagnostics = orchestrator.GetDiagnostics();

        Assert.Equal(
            [
                ApplicationProviders.Accounts,
                ApplicationProviders.HomecomingInstallation,
                ApplicationProviders.MidsInstallation,
                ApplicationProviders.LogActivity,
                ApplicationProviders.HomecomingRuntime
            ],
            diagnostics.RegisteredDescriptors
                .Select(descriptor => descriptor.ProviderId)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray());

        Assert.Contains(ApplicationCapabilities.LogDiscovery, diagnostics.CapabilityProducers.Keys);
        Assert.Contains(ApplicationCapabilities.LogActivity, diagnostics.CapabilityProducers.Keys);
        Assert.Equal(
            ApplicationProviders.LogActivity,
            diagnostics.CapabilityProducers[ApplicationCapabilities.LogActivity]);

        Assert.Equal(5, orchestrator.Current.Providers.Count);
        Assert.All(
            orchestrator.Current.Providers,
            summary => Assert.Equal(ApplicationContributorLifecycleState.Running, summary.LifecycleState));

        // Account discovery is a required dependency, so log activity must start after it.
        var startupOrder = diagnostics.TopologicalOrder.ToList();
        Assert.True(
            startupOrder.IndexOf(ApplicationProviders.LogActivity)
            > startupOrder.IndexOf(ApplicationProviders.Accounts));

        // The contributor lifecycle started the domain service exactly once; no duplicate polling.
        Assert.True(logActivityService.IsRunning);
        Assert.Equal(1, logActivityService.GetDiagnostics().ScanCount);
        Assert.False(logActivityService.GetDiagnostics().IsPollingEnabled);

        await orchestrator.StopAsync();
        Assert.False(logActivityService.IsRunning);

        logActivityContributor.Dispose();
        runtimeContributor.Dispose();
        runtimeService.Dispose();
        await orchestrator.DisposeAsync();
    }

    [Fact]
    public async Task Observed_growth_surfaces_as_receiving_data_through_the_generic_wiring()
    {
        using var environment = new LogActivityTestEnvironment();
        var account = environment.AddAccount("Alpha");
        environment.WriteLog(account, environment.Today, "existing content");

        using var logActivityService = environment.CreateService();
        var contributor = new LogActivityContributor(logActivityService, environment.Time);

        await contributor.StartAsync();

        var beforeGrowth = await contributor.GetContributionAsync();
        Assert.Equal(ContributorActivity.Waiting, beforeGrowth.Activity);

        environment.Time.Advance(TimeSpan.FromSeconds(1));
        environment.Append(account, environment.Today);
        await logActivityService.ScanAsync();

        var duringGrowth = await contributor.GetContributionAsync();
        Assert.Equal(ContributorHealth.Ready, duringGrowth.Health);
        Assert.Equal(ContributorActivity.ReceivingData, duringGrowth.Activity);

        // Past the inactivity threshold the honest state returns to waiting.
        environment.Time.Advance(TimeSpan.FromMinutes(2));
        await logActivityService.ScanAsync();

        var afterIdle = await contributor.GetContributionAsync();
        Assert.Equal(ContributorActivity.Waiting, afterIdle.Activity);
        Assert.Equal(ContributorHealth.Ready, afterIdle.Health);

        await contributor.StopAsync();
        contributor.Dispose();
    }

    [Fact]
    public async Task Shutdown_is_clean_and_idempotent_across_contributor_and_service()
    {
        using var environment = new LogActivityTestEnvironment();
        environment.AddAccount("Alpha");

        var logActivityService = environment.CreateService();
        var contributor = new LogActivityContributor(logActivityService, environment.Time);

        await contributor.StartAsync();
        await contributor.StopAsync();
        await contributor.StopAsync();

        contributor.Dispose();
        contributor.Dispose();

        logActivityService.Dispose();
        logActivityService.Dispose();

        Assert.False(logActivityService.IsRunning);
    }
}
