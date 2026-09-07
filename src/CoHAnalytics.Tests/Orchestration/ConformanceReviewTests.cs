using CoHAnalytics.Orchestration;
using CoHAnalytics.Orchestration.Models;

namespace CoHAnalytics.Tests.Orchestration;

/// <summary>
/// Locks in behavior corrected during the Slice 1 architecture-conformance review.
/// Each test targets one confirmed deviation from Monitoring-Orchestrator-Design.md.
/// </summary>
public sealed class ConformanceReviewTests
{
    [Fact]
    public void Descriptor_defensively_copies_capability_lists_at_registration()
    {
        var producesBacking = new List<string> { "account.discovery" };
        var requiresBacking = new List<string> { "installation.homecoming" };
        var optionalBacking = new List<string> { "runtime.homecoming" };

        var descriptor = new ApplicationContributorDescriptor(
            "accounts",
            "Accounts",
            producesBacking,
            requiresBacking,
            optionalBacking,
            ApplicationContributorImportance.Important,
            10);

        var contributor = new FakeContributor(descriptor);
        var orch = Create(new ManualTimeProvider());
        orch.Register(contributor);

        // Mutate the caller-owned lists after registration. A conformant orchestrator
        // must not observe this change: the graph and evaluation logic already captured
        // a defensive copy of Produces/Requires/Optional.
        producesBacking.Add("account.extra");
        requiresBacking.Clear();
        optionalBacking.Add("runtime.extra");

        var diagnostics = orch.GetDiagnostics();
        var stored = Assert.Single(diagnostics.RegisteredDescriptors);
        Assert.Equal(new[] { "account.discovery" }, stored.Produces);
        Assert.Equal(new[] { "installation.homecoming" }, stored.Requires);
        Assert.Equal(new[] { "runtime.homecoming" }, stored.Optional);
    }

    [Fact]
    public async Task Published_snapshot_collections_cannot_be_mutated_via_downcast()
    {
        var manual = new ManualTimeProvider();
        await using var orch = Create(manual);
        orch.Register(new FakeContributor(TestDescriptors.Create("accounts", "Accounts")));
        await orch.StartAsync();

        var snapshot = orch.Current;

        // Providers/ActiveIssues/SuggestedActions must be array-backed (or otherwise
        // non-resizable), not a raw List<T> reachable via a same-assembly downcast,
        // otherwise a caller could corrupt the shared published snapshot in place.
        Assert.IsType<ProviderSummary[]>(snapshot.Providers);
        Assert.IsType<ApplicationIssue[]>(snapshot.ActiveIssues);
        Assert.IsType<ApplicationAction[]>(snapshot.SuggestedActions);
    }

    [Fact]
    public async Task Unknown_health_provider_with_a_real_issue_does_not_short_circuit_to_Unknown_state()
    {
        // Rule 12 ("no providers, or all Unknown") must never short-circuit past rules 1-11.
        // A single provider whose health happens to be Unknown but which is also carrying a
        // real Warning issue (e.g. a first-ever timeout) must still resolve via rule 8.
        var manual = new ManualTimeProvider();
        var descriptor = TestDescriptors.Create("accounts", "Accounts", importance: ApplicationContributorImportance.Important);
        var contributor = new FakeContributor(descriptor);
        // Never completes within the pull timeout on the very first pull, so the
        // orchestrator has no prior contribution to fall back on and must synthesize
        // the approved Unknown-health, Warning-severity "no prior contribution" state.
        contributor.SetPullOverride(async ct =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException("unreachable");
        });

        await using var orch = new ApplicationOrchestrator(new ApplicationOrchestratorOptions
        {
            TimeProvider = manual,
            DefaultPullTimeout = TimeSpan.FromMilliseconds(5),
            CycleValidationMode = CycleValidationMode.Throw
        });
        orch.Register(contributor);
        await orch.StartAsync();

        var summary = Assert.Single(orch.Current.Providers);
        Assert.Equal(ContributorHealth.Unknown, summary.Health);
        Assert.NotEqual(OverallApplicationState.Unknown, orch.Current.State);
        Assert.Equal(OverallApplicationState.Degraded, orch.Current.State);
    }

    [Fact]
    public void No_providers_registered_reports_Unknown_state()
    {
        var orch = Create(new ManualTimeProvider());
        Assert.Equal(OverallApplicationState.Unknown, orch.Current.State);
    }

    [Fact]
    public async Task Degraded_required_capability_synthesizes_a_warning_issue()
    {
        var manual = new ManualTimeProvider();
        var producer = new FakeContributor(TestDescriptors.Create(
            "homecoming",
            "Homecoming",
            produces: ["installation.homecoming"]));

        await using var orch = Create(manual);
        orch.Register(producer);
        orch.Register(new FakeContributor(TestDescriptors.Create(
            "accounts",
            "Accounts",
            produces: ["account.discovery"],
            requires: ["installation.homecoming"])));

        await orch.StartAsync();

        producer.SetContribution(TestDescriptors.Ready("homecoming", manual.GetUtcNow()) with
        {
            Health = ContributorHealth.Degraded
        });
        await orch.RefreshAsync();

        var accountsSummary = orch.Current.Providers.Single(p => p.ProviderId == "accounts");
        Assert.Equal(ContributorHealth.Degraded, accountsSummary.Health);
        Assert.Contains(orch.Current.ActiveIssues, i =>
            i.Code == "orchestrator.dependency_degraded"
            && i.Severity == ApplicationIssueSeverity.Warning
            && i.ProviderId == "accounts");
    }

    [Fact]
    public void Duplicate_capability_registration_is_rejected_with_a_recorded_diagnostic()
    {
        var orch = Create(new ManualTimeProvider());
        orch.Register(new FakeContributor(TestDescriptors.Create(
            "homecoming",
            "Homecoming",
            produces: ["installation.homecoming"])));

        var duplicate = new FakeContributor(TestDescriptors.Create(
            "homecoming.alt",
            "Homecoming Alt",
            produces: ["installation.homecoming"]));

        Assert.Throws<InvalidOperationException>(() => orch.Register(duplicate));

        var diagnostics = orch.GetDiagnostics();
        Assert.Contains(diagnostics.ValidationMessages, m => m.Contains("already produced by 'homecoming'"));
        Assert.Contains(diagnostics.EventHistory, e => e.Detail == "orchestrator.duplicate_capability");
    }

    private static ApplicationOrchestrator Create(ManualTimeProvider manual) =>
        new(new ApplicationOrchestratorOptions
        {
            DebounceInterval = TimeSpan.FromMilliseconds(10),
            TimeProvider = manual,
            CycleValidationMode = CycleValidationMode.Throw
        });
}
