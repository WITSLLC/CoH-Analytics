using CoHAnalytics.Orchestration;
using CoHAnalytics.Orchestration.Diagnostics;
using CoHAnalytics.Orchestration.Models;
using CoHAnalytics.Tests.Services;

namespace CoHAnalytics.Tests.Orchestration;

public sealed class ExtendedCoverageTests
{
    [Fact]
    public void Descriptor_Consumes_is_union_of_Requires_and_Optional()
    {
        var descriptor = TestDescriptors.Create(
            "accounts",
            "Accounts",
            produces: ["account.discovery"],
            requires: ["installation.homecoming"],
            optional: ["runtime.homecoming"]);

        Assert.Equal(
            new[] { "installation.homecoming", "runtime.homecoming" },
            descriptor.Consumes.ToArray());
    }

    [Fact]
    public async Task Missing_required_capability_marks_dependent_unavailable()
    {
        var manual = new ManualTimeProvider();
        await using var orch = Create(manual);
        orch.Register(new FakeContributor(TestDescriptors.Create(
            "accounts",
            "Accounts",
            produces: ["account.discovery"],
            requires: ["installation.homecoming"])));

        await orch.StartAsync();

        var summary = Assert.Single(orch.Current.Providers);
        Assert.Equal(ContributorHealth.Unavailable, summary.Health);
        Assert.Contains("installation.homecoming", summary.UnmetRequiredCapabilities);
        Assert.Contains(orch.Current.ActiveIssues, i => i.Code == "orchestrator.dependency_unresolved");
    }

    [Fact]
    public async Task Optional_missing_capability_does_not_degrade_health()
    {
        var manual = new ManualTimeProvider();
        await using var orch = Create(manual);
        orch.Register(new FakeContributor(
            TestDescriptors.Create(
                "accounts",
                "Accounts",
                produces: ["account.discovery"],
                optional: ["runtime.homecoming"]),
            TestDescriptors.Ready("accounts", manual.GetUtcNow())));

        await orch.StartAsync();

        Assert.Equal(ContributorHealth.Ready, orch.Current.Providers.Single().Health);
        Assert.Equal(OverallApplicationState.Ready, orch.Current.State);
        Assert.Contains(orch.Current.Facts, f => f.Key.Contains("optional_capability_absent", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Timeout_retains_previous_contribution_and_marks_stale()
    {
        var manual = new ManualTimeProvider();
        var contributor = new FakeContributor(
            TestDescriptors.Create(
                "accounts",
                "Accounts",
                produces: ["account.discovery"],
                pullTimeout: TimeSpan.FromMilliseconds(40)),
            TestDescriptors.Ready("accounts", manual.GetUtcNow()));
        await using var orch = Create(manual);
        orch.Register(contributor);
        await orch.StartAsync();

        contributor.SetPullOverride(async ct =>
        {
            await Task.Delay(TimeSpan.FromSeconds(2), ct);
            return TestDescriptors.Ready("accounts", manual.GetUtcNow());
        });

        await orch.RefreshAsync();

        Assert.True(orch.Current.Providers.Single().IsStale);
        Assert.Contains(orch.GetEventHistory(), e => e.Kind == ApplicationOrchestrationEventKind.ContributionTimedOut);
        Assert.Contains(orch.GetEventHistory(), e => e.Kind == ApplicationOrchestrationEventKind.ContributionMarkedStale);
    }

    [Fact]
    public async Task Timeout_without_prior_contribution_synthesizes_unknown()
    {
        var manual = new ManualTimeProvider();
        var contributor = new FakeContributor(TestDescriptors.Create(
            "accounts",
            "Accounts",
            produces: ["account.discovery"],
            pullTimeout: TimeSpan.FromMilliseconds(30)));
        contributor.SetPullOverride(async ct =>
        {
            await Task.Delay(TimeSpan.FromSeconds(2), ct);
            return TestDescriptors.Ready("accounts", manual.GetUtcNow());
        });
        await using var orch = Create(manual);
        orch.Register(contributor);

        await orch.StartAsync();

        Assert.Equal(ContributorHealth.Unknown, orch.Current.Providers.Single().Health);
        Assert.Contains(orch.Current.ActiveIssues, i => i.Code == "orchestrator.contributor_timeout");
    }

    [Fact]
    public async Task Contributor_exception_is_isolated()
    {
        var manual = new ManualTimeProvider();
        var good = new FakeContributor(
            TestDescriptors.Create("runtime.homecoming", "Runtime", produces: ["runtime.status"]),
            TestDescriptors.Ready("runtime.homecoming", manual.GetUtcNow()));
        var bad = new FakeContributor(TestDescriptors.Create(
            "accounts", "Accounts", produces: ["account.discovery"]));
        bad.SetPullOverride(_ => throw new InvalidOperationException("boom"));
        await using var orch = Create(manual);
        orch.Register(good);
        orch.Register(bad);

        await orch.StartAsync();

        Assert.Equal(ContributorHealth.Ready, orch.Current.Providers.Single(p => p.ProviderId == "runtime.homecoming").Health);
        Assert.Equal(ContributorHealth.Error, orch.Current.Providers.Single(p => p.ProviderId == "accounts").Health);
        Assert.Contains(orch.Current.ActiveIssues, i => i.Code == "orchestrator.contributor_failed");
    }

    [Fact]
    public async Task Contribution_ValidFor_marks_stale_when_expired()
    {
        var manual = new ManualTimeProvider();
        var contribution = TestDescriptors.Ready("accounts", manual.GetUtcNow()) with
        {
            ValidFor = TimeSpan.FromMinutes(1)
        };
        var contributor = new FakeContributor(
            TestDescriptors.Create("accounts", "Accounts", produces: ["account.discovery"]),
            contribution);
        await using var orch = Create(manual);
        orch.Register(contributor);
        await orch.StartAsync();

        manual.Advance(TimeSpan.FromMinutes(2));
        await orch.RefreshAsync();

        Assert.True(orch.Current.Providers.Single().IsStale);
    }

    [Fact]
    public async Task Descriptor_MaxAge_marks_stale()
    {
        var manual = new ManualTimeProvider();
        var contributor = new FakeContributor(
            TestDescriptors.Create(
                "accounts",
                "Accounts",
                produces: ["account.discovery"],
                maxAge: TimeSpan.FromMinutes(5)),
            TestDescriptors.Ready("accounts", manual.GetUtcNow()));
        await using var orch = Create(manual);
        orch.Register(contributor);
        await orch.StartAsync();

        manual.Advance(TimeSpan.FromMinutes(6));
        await orch.RefreshAsync();

        Assert.True(orch.Current.Providers.Single().IsStale);
    }

    [Fact]
    public async Task All_fact_value_types_round_trip_into_snapshot()
    {
        var manual = new ManualTimeProvider();
        var now = manual.GetUtcNow();
        var facts = new ApplicationFact[]
        {
            new("accounts.flag", new ApplicationFactValue.Boolean(true), ApplicationFactScope.Provider, now),
            new("accounts.count", new ApplicationFactValue.Integer(3), ApplicationFactScope.Provider, now),
            new("accounts.rate", new ApplicationFactValue.Decimal(1.5m), ApplicationFactScope.Provider, now),
            new("accounts.name", new ApplicationFactValue.Text("Alpha"), ApplicationFactScope.Account, now),
            new("accounts.latest", new ApplicationFactValue.Timestamp(now), ApplicationFactScope.Provider, now),
            new("accounts.age", new ApplicationFactValue.Duration(TimeSpan.FromHours(1)), ApplicationFactScope.Provider, now),
            new("accounts.id", new ApplicationFactValue.Identifier("abc"), ApplicationFactScope.Account, now)
        };
        await using var orch = Create(manual);
        orch.Register(new FakeContributor(
            TestDescriptors.Create("accounts", "Accounts", produces: ["account.discovery"]),
            TestDescriptors.Ready("accounts", now, facts: facts)));

        await orch.StartAsync();

        Assert.Equal(7, orch.Current.Facts.Count);
    }

    [Fact]
    public async Task Issue_disappearance_resolves_issue()
    {
        var manual = new ManualTimeProvider();
        var contributor = new FakeContributor(
            TestDescriptors.Create("accounts", "Accounts", produces: ["account.discovery"]),
            TestDescriptors.Ready(
                "accounts",
                manual.GetUtcNow(),
                issues:
                [
                    new ApplicationIssue(
                        "account.warning",
                        ApplicationIssueSeverity.Warning,
                        "Temporary",
                        "accounts",
                        manual.GetUtcNow())
                ]));
        await using var orch = Create(manual);
        orch.Register(contributor);
        await orch.StartAsync();
        Assert.Single(orch.Current.ActiveIssues);

        contributor.SetContribution(TestDescriptors.Ready("accounts", manual.GetUtcNow()));
        await orch.RefreshAsync();

        Assert.Empty(orch.Current.ActiveIssues);
    }

    [Fact]
    public async Task Expired_issue_is_removed()
    {
        var manual = new ManualTimeProvider();
        var created = manual.GetUtcNow();
        var contributor = new FakeContributor(
            TestDescriptors.Create("accounts", "Accounts", produces: ["account.discovery"]),
            TestDescriptors.Ready(
                "accounts",
                created,
                issues:
                [
                    new ApplicationIssue(
                        "account.temp",
                        ApplicationIssueSeverity.Warning,
                        "Expires",
                        "accounts",
                        created)
                    {
                        ExpiresAt = created.AddMinutes(1)
                    }
                ]));
        await using var orch = Create(manual);
        orch.Register(contributor);
        await orch.StartAsync();

        manual.Advance(TimeSpan.FromMinutes(2));
        contributor.SetContribution(TestDescriptors.Ready(
            "accounts",
            manual.GetUtcNow(),
            issues:
            [
                new ApplicationIssue(
                    "account.temp",
                    ApplicationIssueSeverity.Warning,
                    "Expires",
                    "accounts",
                    created)
                {
                    ExpiresAt = created.AddMinutes(1)
                }
            ]));
        await orch.RefreshAsync();

        Assert.Empty(orch.Current.ActiveIssues);
    }

    [Fact]
    public async Task Snapshot_ordering_is_independent_of_registration_order()
    {
        var manual = new ManualTimeProvider();
        await using var first = Create(manual);
        first.Register(new FakeContributor(TestDescriptors.Create(
            "runtime.homecoming", "Runtime", produces: ["runtime.status"], priority: 20),
            TestDescriptors.Ready("runtime.homecoming", manual.GetUtcNow())));
        first.Register(new FakeContributor(TestDescriptors.Create(
            "accounts", "Accounts", produces: ["account.discovery"], priority: 10),
            TestDescriptors.Ready("accounts", manual.GetUtcNow())));
        await first.StartAsync();

        await using var second = Create(manual);
        second.Register(new FakeContributor(TestDescriptors.Create(
            "accounts", "Accounts", produces: ["account.discovery"], priority: 10),
            TestDescriptors.Ready("accounts", manual.GetUtcNow())));
        second.Register(new FakeContributor(TestDescriptors.Create(
            "runtime.homecoming", "Runtime", produces: ["runtime.status"], priority: 20),
            TestDescriptors.Ready("runtime.homecoming", manual.GetUtcNow())));
        await second.StartAsync();

        Assert.Equal(
            first.Current.Providers.Select(p => p.ProviderId).ToArray(),
            second.Current.Providers.Select(p => p.ProviderId).ToArray());
    }

    [Fact]
    public async Task Burst_invalidations_coalesce_into_follow_up_refresh()
    {
        var manual = new ManualTimeProvider();
        var contributor = new FakeContributor(
            TestDescriptors.Create("accounts", "Accounts", produces: ["account.discovery"]),
            TestDescriptors.Ready("accounts", manual.GetUtcNow()));
        await using var orch = new ApplicationOrchestrator(new ApplicationOrchestratorOptions
        {
            DebounceInterval = TimeSpan.FromMilliseconds(40),
            TimeProvider = manual,
            CycleValidationMode = CycleValidationMode.Throw
        });
        orch.Register(contributor);
        await orch.StartAsync();
        var pullsAfterStart = contributor.PullCount;

        contributor.RaiseChanged();
        contributor.RaiseChanged();
        contributor.RaiseChanged();
        var finalInvalidationSequence = orch.GetEventHistory()
            .Where(item => item.Kind == ApplicationOrchestrationEventKind.ContributionInvalidated)
            .Max(item => item.Sequence);
        await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
            orch.GetEventHistory().Any(item =>
                item.Kind == ApplicationOrchestrationEventKind.RefreshCompleted
                && item.Sequence > finalInvalidationSequence));

        Assert.True(contributor.PullCount > pullsAfterStart);
        Assert.True(contributor.PullCount <= pullsAfterStart + 2);
    }

    [Fact]
    public async Task Lifecycle_fault_preserves_unknown_health_when_never_pulled()
    {
        var manual = new ManualTimeProvider();
        var contributor = new LifecycleFakeContributor(
            TestDescriptors.Create("accounts", "Accounts", produces: ["account.discovery"]),
            TestDescriptors.Ready("accounts", manual.GetUtcNow()));
        contributor.StartException = new InvalidOperationException("start failed");
        await using var orch = Create(manual);
        orch.Register(contributor);
        await orch.StartAsync();

        var summary = orch.Current.Providers.Single(p => p.ProviderId == "accounts");
        Assert.Equal(ApplicationContributorLifecycleState.Faulted, summary.LifecycleState);
        Assert.Equal(ContributorHealth.Unknown, summary.Health);
        Assert.Contains(orch.Current.ActiveIssues, i => i.Code == "orchestrator.contributor_lifecycle_failed");

        // Overall state must reflect the Error-severity lifecycle-failure issue via rule 4
        // (non-critical provider) rather than being masked by the "all Unknown" fallback,
        // since the only provider's Health happens to be Unknown.
        Assert.Equal(OverallApplicationState.Degraded, orch.Current.State);
    }

    [Fact]
    public async Task Current_is_non_null_before_startup()
    {
        await using var orch = Create(new ManualTimeProvider());
        Assert.NotNull(orch.Current);
        Assert.Equal(OverallApplicationState.Unknown, orch.Current.State);
        Assert.Equal(0, orch.Current.Revision);
    }

    [Fact]
    public async Task Rejects_requires_and_optional_overlap()
    {
        var descriptor = TestDescriptors.Create(
            "accounts",
            "Accounts",
            produces: ["account.discovery"],
            requires: ["installation.homecoming"],
            optional: ["installation.homecoming"]);
        await using var orch = Create(new ManualTimeProvider());

        var ex = Assert.Throws<ArgumentException>(() => orch.Register(new FakeContributor(descriptor)));
        Assert.Contains("both Requires and Optional", ex.Message);
    }

    [Fact]
    public async Task Rejects_self_dependency_on_produced_capability()
    {
        var descriptor = TestDescriptors.Create(
            "accounts",
            "Accounts",
            produces: ["account.discovery"],
            requires: ["account.discovery"]);
        await using var orch = Create(new ManualTimeProvider());

        var ex = Assert.Throws<ArgumentException>(() => orch.Register(new FakeContributor(descriptor)));
        Assert.Contains("requires capability", ex.Message);
    }

    private static ApplicationOrchestrator Create(ManualTimeProvider manual) =>
        new(new ApplicationOrchestratorOptions
        {
            DebounceInterval = TimeSpan.FromMilliseconds(10),
            TimeProvider = manual,
            CycleValidationMode = CycleValidationMode.Throw
        });
}
