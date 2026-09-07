using CoHAnalytics.Orchestration;
using CoHAnalytics.Orchestration.Models;

namespace CoHAnalytics.Tests.Orchestration;

public sealed class DependencyGraphTests
{
    [Fact]
    public async Task Start_builds_deterministic_topological_order()
    {
        var manual = new ManualTimeProvider();
        await using var orch = CreateOrchestrator(manual, CycleValidationMode.Throw);
        orch.Register(new FakeContributor(TestDescriptors.Create(
            "runtime.homecoming", "Runtime", produces: ["runtime.status"], requires: ["install.homecoming"])));
        orch.Register(new FakeContributor(TestDescriptors.Create(
            "accounts", "Accounts", produces: ["account.discovery"], requires: ["runtime.status"])));
        orch.Register(new FakeContributor(TestDescriptors.Create(
            "installation.homecoming", "Installation", produces: ["install.homecoming"])));

        await orch.StartAsync();

        Assert.Equal(
            ["installation.homecoming", "runtime.homecoming", "accounts"],
            orch.GetDiagnostics().TopologicalOrder);
    }

    [Fact]
    public async Task Start_throws_when_required_dependencies_form_cycle()
    {
        var manual = new ManualTimeProvider();
        await using var orch = CreateOrchestrator(manual, CycleValidationMode.Throw);
        orch.Register(new FakeContributor(TestDescriptors.Create(
            "accounts", "Accounts", produces: ["account.discovery"], requires: ["runtime.status"])));
        orch.Register(new FakeContributor(TestDescriptors.Create(
            "runtime.homecoming", "Runtime", produces: ["runtime.status"], requires: ["account.discovery"])));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => orch.StartAsync());

        Assert.Contains("Required-dependency cycle", exception.Message);
        Assert.Contains("accounts", orch.GetDiagnostics().ValidationMessages.Single());
    }

    [Fact]
    public async Task Start_degrades_by_rejecting_only_cycle_participants()
    {
        var manual = new ManualTimeProvider();
        await using var orch = CreateOrchestrator(manual, CycleValidationMode.Degrade);
        orch.Register(new FakeContributor(TestDescriptors.Create(
            "accounts", "Accounts", produces: ["account.discovery"], requires: ["runtime.status"])));
        orch.Register(new FakeContributor(TestDescriptors.Create(
            "runtime.homecoming", "Runtime", produces: ["runtime.status"], requires: ["account.discovery"])));
        orch.Register(new FakeContributor(TestDescriptors.Create(
            "installation.homecoming", "Installation", produces: ["install.homecoming"])));

        await orch.StartAsync();

        var diagnostics = orch.GetDiagnostics();
        Assert.Equal(["installation.homecoming"], diagnostics.TopologicalOrder);
        Assert.Equal(["installation.homecoming"], diagnostics.RegisteredDescriptors.Select(d => d.ProviderId));
        Assert.Contains(diagnostics.ValidationMessages, message => message.Contains("Rejected cyclic contributor 'accounts'"));
        Assert.Contains(diagnostics.ValidationMessages, message => message.Contains("Rejected cyclic contributor 'runtime.homecoming'"));
    }

    [Fact]
    public async Task Missing_required_capability_makes_critical_consumer_error()
    {
        var manual = new ManualTimeProvider();
        await using var orch = CreateOrchestrator(manual, CycleValidationMode.Throw);
        orch.Register(new FakeContributor(TestDescriptors.Create(
            "accounts",
            "Accounts",
            produces: ["account.discovery"],
            requires: ["install.homecoming"],
            importance: ApplicationContributorImportance.Critical)));

        await orch.StartAsync();

        Assert.Equal(OverallApplicationState.Error, orch.Current.State);
        var provider = Assert.Single(orch.Current.Providers);
        Assert.Equal(ContributorHealth.Unavailable, provider.Health);
        Assert.Contains("install.homecoming", provider.UnmetRequiredCapabilities);
        Assert.Contains(orch.Current.ActiveIssues, issue => issue.Code == "orchestrator.dependency_unresolved");
    }

    private static ApplicationOrchestrator CreateOrchestrator(
        ManualTimeProvider manual,
        CycleValidationMode cycleValidationMode) =>
        new(new ApplicationOrchestratorOptions
        {
            DebounceInterval = TimeSpan.FromMilliseconds(10),
            TimeProvider = manual,
            CycleValidationMode = cycleValidationMode
        });
}
