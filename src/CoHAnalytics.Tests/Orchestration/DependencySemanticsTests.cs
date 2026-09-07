using CoHAnalytics.Orchestration;
using CoHAnalytics.Orchestration.Models;

namespace CoHAnalytics.Tests.Orchestration;

/// <summary>
/// Covers the Revision 4 dependency clarifications: capability-state precedence (§9.1,
/// known failure outranks staleness) and required-capability <c>Unknown</c> as pending
/// observation rather than failure (§9.2.1, §9.3).
/// </summary>
public sealed class DependencySemanticsTests
{
    private static readonly DateTimeOffset Start = new(2026, 8, 4, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan ProducerMaxAge = TimeSpan.FromMinutes(1);

    // §9.1 precedence, first match wins. Error/Unavailable stay Unhealthy when stale;
    // Degraded stays SatisfiedDegraded when stale; Unknown stays Unknown; only Ready
    // becomes Stale. Asserted through the dependent, which is what the rules exist to drive.
    [Theory]
    [InlineData(ContributorHealth.Ready, false, ContributorHealth.Ready, null)]
    [InlineData(ContributorHealth.Ready, true, ContributorHealth.Degraded, ApplicationIssueCodes.DependencyDegraded)]
    [InlineData(ContributorHealth.Degraded, false, ContributorHealth.Degraded, ApplicationIssueCodes.DependencyDegraded)]
    [InlineData(ContributorHealth.Degraded, true, ContributorHealth.Degraded, ApplicationIssueCodes.DependencyDegraded)]
    [InlineData(ContributorHealth.Error, false, ContributorHealth.Unavailable, ApplicationIssueCodes.DependencyUnresolved)]
    [InlineData(ContributorHealth.Error, true, ContributorHealth.Unavailable, ApplicationIssueCodes.DependencyUnresolved)]
    [InlineData(ContributorHealth.Unavailable, false, ContributorHealth.Unavailable, ApplicationIssueCodes.DependencyUnresolved)]
    [InlineData(ContributorHealth.Unavailable, true, ContributorHealth.Unavailable, ApplicationIssueCodes.DependencyUnresolved)]
    [InlineData(ContributorHealth.Unknown, false, ContributorHealth.Unavailable, ApplicationIssueCodes.DependencyPending)]
    [InlineData(ContributorHealth.Unknown, true, ContributorHealth.Unavailable, ApplicationIssueCodes.DependencyPending)]
    public async Task Capability_state_precedence_drives_dependent_health(
        ContributorHealth producerHealth,
        bool producerStale,
        ContributorHealth expectedDependentHealth,
        string? expectedIssueCode)
    {
        var manual = new ManualTimeProvider(Start);
        var producer = CreateProducer(producerHealth);
        await using var orch = Create(manual);
        orch.Register(producer);
        orch.Register(CreateDependent());
        await orch.StartAsync();

        if (producerStale)
        {
            manual.Advance(ProducerMaxAge + TimeSpan.FromSeconds(1));
            await orch.RefreshAsync();
        }

        var producerSummary = orch.Current.Providers.Single(p => p.ProviderId == "homecoming");
        Assert.Equal(producerStale, producerSummary.IsStale);

        var dependent = orch.Current.Providers.Single(p => p.ProviderId == "accounts");
        Assert.Equal(expectedDependentHealth, dependent.Health);

        var dependencyIssues = orch.Current.ActiveIssues
            .Where(i => i.ProviderId == "accounts" && i.Code.StartsWith("orchestrator.dependency", StringComparison.Ordinal))
            .ToList();

        if (expectedIssueCode is null)
        {
            Assert.Empty(dependencyIssues);
        }
        else
        {
            Assert.Equal(expectedIssueCode, Assert.Single(dependencyIssues).Code);
        }
    }

    [Fact]
    public async Task Stale_failed_producer_reports_unhealthy_not_stale()
    {
        // The precedence rule that Revision 4 exists to settle: a producer that is both
        // failed and stale must not be softened into a mere freshness problem.
        var manual = new ManualTimeProvider(Start);
        await using var orch = Create(manual);
        orch.Register(CreateProducer(ContributorHealth.Error));
        orch.Register(CreateDependent());
        await orch.StartAsync();

        manual.Advance(ProducerMaxAge + TimeSpan.FromSeconds(1));
        await orch.RefreshAsync();

        var dependent = orch.Current.Providers.Single(p => p.ProviderId == "accounts");
        Assert.Equal(ContributorHealth.Unavailable, dependent.Health);
        Assert.Contains("installation.homecoming", dependent.UnmetRequiredCapabilities);
        Assert.Contains(orch.Current.ActiveIssues, i =>
            i.ProviderId == "accounts"
            && i.Code == ApplicationIssueCodes.DependencyUnresolved
            && i.Severity == ApplicationIssueSeverity.Error);
        Assert.DoesNotContain(orch.Current.ActiveIssues, i =>
            i.ProviderId == "accounts" && i.Code == ApplicationIssueCodes.DependencyDegraded);
    }

    [Fact]
    public async Task Required_unknown_capability_is_pending_not_broken()
    {
        var manual = new ManualTimeProvider(Start);
        await using var orch = Create(manual);
        orch.Register(CreateProducer(ContributorHealth.Unknown));
        orch.Register(CreateDependent());
        await orch.StartAsync();

        var dependent = orch.Current.Providers.Single(p => p.ProviderId == "accounts");
        Assert.Equal(ContributorHealth.Unavailable, dependent.Health);
        Assert.Equal(ContributorActivity.Waiting, dependent.Activity);

        // Pending is not "unmet" for the purposes of the §11 rules.
        Assert.Empty(dependent.UnmetRequiredCapabilities);

        var pendingIssue = orch.Current.ActiveIssues.Single(i =>
            i.ProviderId == "accounts" && i.Code == ApplicationIssueCodes.DependencyPending);
        Assert.Equal(ApplicationIssueSeverity.Information, pendingIssue.Severity);
        Assert.False(pendingIssue.RequiresUserAction);
        Assert.Equal("installation.homecoming", pendingIssue.RelatedEntityId);
        Assert.Contains("waiting for installation.homecoming", pendingIssue.Summary);

        Assert.DoesNotContain(orch.Current.ActiveIssues, i =>
            i.Severity >= ApplicationIssueSeverity.Warning);
        Assert.Equal(OverallApplicationState.Waiting, orch.Current.State);
    }

    [Fact]
    public async Task Optional_unknown_capability_produces_no_issue()
    {
        var manual = new ManualTimeProvider(Start);
        await using var orch = Create(manual);
        orch.Register(CreateProducer(ContributorHealth.Unknown));
        orch.Register(new FakeContributor(
            TestDescriptors.Create(
                "accounts",
                "Accounts",
                produces: ["account.discovery"],
                optional: ["installation.homecoming"]),
            TestDescriptors.Ready("accounts", Start)));
        await orch.StartAsync();

        var dependent = orch.Current.Providers.Single(p => p.ProviderId == "accounts");
        Assert.Equal(ContributorHealth.Ready, dependent.Health);
        Assert.DoesNotContain(orch.Current.ActiveIssues, i => i.ProviderId == "accounts");
    }

    [Fact]
    public async Task Pending_cascades_as_informational_through_multiple_levels()
    {
        var manual = new ManualTimeProvider(Start);
        await using var orch = Create(manual);
        orch.Register(CreateProducer(ContributorHealth.Unknown));
        orch.Register(CreateDependent());
        orch.Register(new FakeContributor(
            TestDescriptors.Create(
                "logs",
                "Log Activity",
                produces: ["log.activity"],
                requires: ["account.discovery"]),
            TestDescriptors.Ready("logs", Start)));
        await orch.StartAsync();

        foreach (var providerId in new[] { "accounts", "logs" })
        {
            var summary = orch.Current.Providers.Single(p => p.ProviderId == providerId);
            Assert.Equal(ContributorHealth.Unavailable, summary.Health);
            Assert.Equal(ContributorActivity.Waiting, summary.Activity);
            Assert.Empty(summary.UnmetRequiredCapabilities);

            var issue = orch.Current.ActiveIssues.Single(i => i.ProviderId == providerId);
            Assert.Equal(ApplicationIssueCodes.DependencyPending, issue.Code);
            Assert.Equal(ApplicationIssueSeverity.Information, issue.Severity);
        }

        // The second-level dependent names its own direct capability, not the root's.
        Assert.Equal(
            "account.discovery",
            orch.Current.ActiveIssues.Single(i => i.ProviderId == "logs").RelatedEntityId);

        Assert.DoesNotContain(orch.Current.ActiveIssues, i => i.Severity >= ApplicationIssueSeverity.Warning);
        Assert.Equal(OverallApplicationState.Waiting, orch.Current.State);
    }

    [Fact]
    public async Task Pending_resolves_automatically_when_capability_becomes_ready()
    {
        var manual = new ManualTimeProvider(Start);
        var producer = CreateProducer(ContributorHealth.Unknown);
        await using var orch = Create(manual);
        orch.Register(producer);
        orch.Register(CreateDependent());
        await orch.StartAsync();

        Assert.Contains(orch.Current.ActiveIssues, i => i.Code == ApplicationIssueCodes.DependencyPending);

        producer.SetContribution(TestDescriptors.Ready("homecoming", manual.GetUtcNow()));
        await orch.RefreshAsync();

        Assert.DoesNotContain(orch.Current.ActiveIssues, i => i.Code == ApplicationIssueCodes.DependencyPending);
        var dependent = orch.Current.Providers.Single(p => p.ProviderId == "accounts");
        Assert.Equal(ContributorHealth.Ready, dependent.Health);
        Assert.Equal(OverallApplicationState.Ready, orch.Current.State);
    }

    [Fact]
    public async Task Pending_converts_to_unresolved_when_capability_becomes_failed()
    {
        var manual = new ManualTimeProvider(Start);
        var producer = CreateProducer(ContributorHealth.Unknown);
        await using var orch = Create(manual);
        orch.Register(producer);
        orch.Register(CreateDependent());
        await orch.StartAsync();

        Assert.Contains(orch.Current.ActiveIssues, i => i.Code == ApplicationIssueCodes.DependencyPending);

        producer.SetContribution(TestDescriptors.Ready("homecoming", manual.GetUtcNow()) with
        {
            Health = ContributorHealth.Error
        });
        await orch.RefreshAsync();

        Assert.DoesNotContain(orch.Current.ActiveIssues, i => i.Code == ApplicationIssueCodes.DependencyPending);
        Assert.Contains(orch.Current.ActiveIssues, i =>
            i.ProviderId == "accounts"
            && i.Code == ApplicationIssueCodes.DependencyUnresolved
            && i.Severity == ApplicationIssueSeverity.Error);
        Assert.Equal(OverallApplicationState.Degraded, orch.Current.State);
    }

    [Fact]
    public async Task All_providers_unobserved_and_waiting_on_nothing_reports_unknown()
    {
        var manual = new ManualTimeProvider(Start);
        await using var orch = Create(manual);
        orch.Register(new FakeContributor(
            TestDescriptors.Create("homecoming", "Homecoming", produces: ["installation.homecoming"]),
            ApplicationContribution.Empty("homecoming", Start)));
        orch.Register(new FakeContributor(
            TestDescriptors.Create("mids", "Mids", produces: ["installation.mids"]),
            ApplicationContribution.Empty("mids", Start)));
        await orch.StartAsync();

        Assert.All(orch.Current.Providers, p => Assert.Equal(ContributorHealth.Unknown, p.Health));
        Assert.Empty(orch.Current.ActiveIssues);
        Assert.Equal(OverallApplicationState.Unknown, orch.Current.State);
    }

    [Fact]
    public async Task Real_failure_outranks_a_pending_dependency()
    {
        var manual = new ManualTimeProvider(Start);
        await using var orch = Create(manual);
        orch.Register(CreateProducer(ContributorHealth.Unknown));
        orch.Register(CreateDependent());
        orch.Register(new FakeContributor(
            TestDescriptors.Create(
                "mids",
                "Mids",
                produces: ["installation.mids"],
                importance: ApplicationContributorImportance.Critical),
            TestDescriptors.Ready(
                "mids",
                Start,
                issues: [new ApplicationIssue("mids.database_missing", ApplicationIssueSeverity.Error, "Mids database is missing.", "mids", Start) { UpdatedAt = Start }])
                with { Health = ContributorHealth.Error }));
        await orch.StartAsync();

        // The pending dependency is still reported, but it does not decide the aggregate.
        Assert.Contains(orch.Current.ActiveIssues, i => i.Code == ApplicationIssueCodes.DependencyPending);
        Assert.Equal(OverallApplicationState.Error, orch.Current.State);
    }

    [Fact]
    public async Task Dependency_diagnostics_distinguish_pending_from_failure()
    {
        var manual = new ManualTimeProvider(Start);
        var producer = CreateProducer(ContributorHealth.Unknown);
        await using var orch = Create(manual);
        orch.Register(producer);
        orch.Register(CreateDependent());
        await orch.StartAsync();

        Assert.Contains(
            orch.GetEventHistory(),
            e => e.ProviderId == "accounts"
                && e.RelatedCapabilityId == "installation.homecoming"
                && e.Detail == "pending: producer not yet observed");

        producer.SetContribution(TestDescriptors.Ready("homecoming", manual.GetUtcNow()) with
        {
            Health = ContributorHealth.Error
        });
        await orch.RefreshAsync();

        Assert.Contains(
            orch.GetEventHistory(),
            e => e.ProviderId == "accounts"
                && e.RelatedCapabilityId == "installation.homecoming"
                && e.Detail == "unhealthy: producer reported failure");
    }

    private static FakeContributor CreateProducer(ContributorHealth health) =>
        new(
            TestDescriptors.Create(
                "homecoming",
                "Homecoming",
                produces: ["installation.homecoming"],
                maxAge: ProducerMaxAge),
            TestDescriptors.Ready("homecoming", Start) with { Health = health });

    private static FakeContributor CreateDependent() =>
        new(
            TestDescriptors.Create(
                "accounts",
                "Accounts",
                produces: ["account.discovery"],
                requires: ["installation.homecoming"]),
            TestDescriptors.Ready("accounts", Start));

    private static ApplicationOrchestrator Create(ManualTimeProvider manual) =>
        new(new ApplicationOrchestratorOptions
        {
            DebounceInterval = TimeSpan.FromMilliseconds(10),
            TimeProvider = manual,
            CycleValidationMode = CycleValidationMode.Throw
        });
}
