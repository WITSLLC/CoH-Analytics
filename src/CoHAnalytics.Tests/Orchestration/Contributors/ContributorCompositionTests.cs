using CoHAnalytics.Models;
using CoHAnalytics.Orchestration;
using CoHAnalytics.Orchestration.Contributors;
using CoHAnalytics.Orchestration.Models;
using CoHAnalytics.Services;
using CoHAnalytics.Tests.Orchestration;

namespace CoHAnalytics.Tests.Orchestration.Contributors;

public sealed class ContributorCompositionTests
{
  [Fact]
  public async Task Composition_registers_four_contributors_and_reaches_running()
  {
    var manual = new ManualTimeProvider();
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

    var runtimeContributor = new HomecomingRuntimeContributor(runtimeService, manual);
    orchestrator.Register(new MidsInstallationContributor(midsService, manual));
    orchestrator.Register(new HomecomingAccountsContributor(accountService, manual));
    orchestrator.Register(runtimeContributor);
    orchestrator.Register(new HomecomingInstallationContributor(installationService, manual));

    await orchestrator.StartAsync();

    var diagnostics = orchestrator.GetDiagnostics();
        Assert.Equal(
            [
                ApplicationProviders.Accounts,
                ApplicationProviders.HomecomingInstallation,
                ApplicationProviders.MidsInstallation,
                ApplicationProviders.HomecomingRuntime
            ],
            diagnostics.RegisteredDescriptors
                .Select(descriptor => descriptor.ProviderId)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray());

    Assert.Equal(
      [
        ApplicationCapabilities.AccountDiscovery,
        ApplicationCapabilities.AccountLogHistory,
        ApplicationCapabilities.MidsHomecomingDatabase,
        ApplicationCapabilities.HomecomingInstallation,
        ApplicationCapabilities.MidsInstallation,
        ApplicationCapabilities.HomecomingRuntime
      ],
      diagnostics.CapabilityProducers.Keys.OrderBy(id => id, StringComparer.Ordinal).ToArray());

    Assert.Equal(4, orchestrator.Current.Providers.Count);
    Assert.All(
      orchestrator.Current.Providers,
      summary => Assert.Equal(ApplicationContributorLifecycleState.Running, summary.LifecycleState));

        Assert.Equal(
            [
                ApplicationProviders.HomecomingInstallation,
                ApplicationProviders.Accounts,
                ApplicationProviders.MidsInstallation,
                ApplicationProviders.HomecomingRuntime
            ],
            diagnostics.TopologicalOrder);

    if (!orchestrator.Current.ActiveIssues.Any(issue => issue.Severity >= ApplicationIssueSeverity.Error))
    {
      Assert.NotEqual(OverallApplicationState.Error, orchestrator.Current.State);
    }

    runtimeContributor.Dispose();
    runtimeService.Dispose();
    await orchestrator.DisposeAsync();
  }

  [Fact]
  public async Task Shutdown_is_idempotent_for_runtime_adapter_and_orchestrator()
  {
    var manual = new ManualTimeProvider();
    var settings = new SettingsService();
    var installationService = new HomecomingInstallationService(settings);
    var accountService = new HomecomingAccountDiscoveryService(installationService);
    var midsService = new MidsInstallationService(settings);
    var launcher = new HomecomingLauncherService(settings, installationService);
    var runtimeService = new HomecomingRuntimeService(installationService, launcher);

    var orchestrator = new ApplicationOrchestrator(new ApplicationOrchestratorOptions { TimeProvider = manual });
    var runtimeContributor = new HomecomingRuntimeContributor(runtimeService, manual);
    orchestrator.Register(new HomecomingInstallationContributor(installationService, manual));
    orchestrator.Register(runtimeContributor);
    orchestrator.Register(new HomecomingAccountsContributor(accountService, manual));
    orchestrator.Register(new MidsInstallationContributor(midsService, manual));

    await orchestrator.StartAsync();
    await orchestrator.StopAsync();
    await orchestrator.StopAsync();

    runtimeContributor.Dispose();
    runtimeContributor.Dispose();

    await orchestrator.DisposeAsync();
    runtimeService.Dispose();
  }
}
