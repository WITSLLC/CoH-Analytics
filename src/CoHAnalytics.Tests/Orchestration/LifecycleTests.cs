using CoHAnalytics.Orchestration;
using CoHAnalytics.Orchestration.Diagnostics;
using CoHAnalytics.Orchestration.Models;

namespace CoHAnalytics.Tests.Orchestration;

public sealed class LifecycleTests
{
    [Fact]
    public async Task Lifecycle_contributors_start_topologically_and_stop_in_reverse_order()
    {
        var manual = new ManualTimeProvider();
        await using var orch = CreateOrchestrator(manual);
        orch.Register(new LifecycleFakeContributor(TestDescriptors.Create(
            "accounts", "Accounts", produces: ["account.discovery"], requires: ["runtime.status"])));
        orch.Register(new LifecycleFakeContributor(TestDescriptors.Create(
            "runtime.homecoming", "Runtime", produces: ["runtime.status"], requires: ["install.homecoming"])));
        orch.Register(new LifecycleFakeContributor(TestDescriptors.Create(
            "installation.homecoming", "Installation", produces: ["install.homecoming"])));

        await orch.StartAsync();
        var startOrder = LifecycleTransitions(orch, "Starting");
        await orch.StopAsync();
        var stopOrder = LifecycleTransitions(orch, "Stopping");

        Assert.Equal(["installation.homecoming", "runtime.homecoming", "accounts"], startOrder);
        Assert.Equal(["accounts", "runtime.homecoming", "installation.homecoming"], stopOrder);
    }

    [Fact]
    public async Task Contributor_without_lifecycle_transitions_directly_from_registered_to_running_to_stopped()
    {
        var manual = new ManualTimeProvider();
        var contributor = new FakeContributor(TestDescriptors.Create(
            "accounts", "Accounts", produces: ["account.discovery"]));
        await using var orch = CreateOrchestrator(manual);
        orch.Register(contributor);

        await orch.StartAsync();
        await orch.StopAsync();

        var transitions = orch.GetEventHistory()
            .Where(e => e.Kind == ApplicationOrchestrationEventKind.ContributorLifecycleChanged)
            .Select(e => e.Detail ?? string.Empty)
            .ToArray();
        Assert.Equal(["Registered → Running", "Running → Stopped"], transitions);
        Assert.Empty(contributor.LifecycleLog);
    }

    [Fact]
    public async Task Startup_failure_faults_only_failing_contributor_and_allows_others_to_run()
    {
        var manual = new ManualTimeProvider();
        var failing = new LifecycleFakeContributor(TestDescriptors.Create(
            "accounts", "Accounts", produces: ["account.discovery"]))
        {
            StartException = new InvalidOperationException("startup failed")
        };
        var healthy = new LifecycleFakeContributor(TestDescriptors.Create(
            "runtime.homecoming", "Runtime", produces: ["runtime.status"]));
        await using var orch = CreateOrchestrator(manual);
        orch.Register(failing);
        orch.Register(healthy);

        await orch.StartAsync();

        var states = orch.GetDiagnostics().LifecycleStates;
        Assert.Equal(ApplicationContributorLifecycleState.Faulted, states["accounts"]);
        Assert.Equal(ApplicationContributorLifecycleState.Running, states["runtime.homecoming"]);
        Assert.IsType<InvalidOperationException>(orch.GetDiagnostics().LastLifecycleException);
        Assert.Contains(orch.GetEventHistory(), e => e.Kind == ApplicationOrchestrationEventKind.ContributorFaulted
            && e.ProviderId == "accounts");
    }

    private static string[] LifecycleTransitions(ApplicationOrchestrator orch, string transition) =>
        orch.GetEventHistory()
            .Where(e => e.Kind == ApplicationOrchestrationEventKind.ContributorLifecycleChanged
                && e.Detail?.EndsWith(transition, StringComparison.Ordinal) == true)
            .Select(e => e.ProviderId!)
            .ToArray();

    private static ApplicationOrchestrator CreateOrchestrator(ManualTimeProvider manual) =>
        new(new ApplicationOrchestratorOptions
        {
            DebounceInterval = TimeSpan.FromMilliseconds(10),
            TimeProvider = manual,
            CycleValidationMode = CycleValidationMode.Throw
        });
}
