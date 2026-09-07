using CoHAnalytics.Orchestration;
using CoHAnalytics.Orchestration.Diagnostics;
using CoHAnalytics.Orchestration.Models;

namespace CoHAnalytics.Tests.Orchestration;

public sealed class SnapshotAndIssueTests
{
    [Fact]
    public async Task Semantically_equal_refresh_suppresses_snapshot_change_and_revision()
    {
        var manual = new ManualTimeProvider();
        var contribution = TestDescriptors.Ready("accounts", manual.GetUtcNow());
        var contributor = new FakeContributor(TestDescriptors.Create(
            "accounts", "Accounts", produces: ["account.discovery"]), contribution);
        await using var orch = CreateOrchestrator(manual);
        var changeCount = 0;
        orch.SnapshotChanged += (_, _) => changeCount++;
        orch.Register(contributor);
        await orch.StartAsync();
        var revision = orch.Current.Revision;

        await orch.RefreshAsync();

        Assert.Equal(revision, orch.Current.Revision);
        Assert.Equal(1, changeCount);
        Assert.Equal(1, orch.GetDiagnostics().NoOpSuppressionCount);
        Assert.Contains(orch.GetEventHistory(), e => e.Kind == ApplicationOrchestrationEventKind.SnapshotSuppressed
            && e.SnapshotRevision == revision);
    }

    [Fact]
    public async Task Refresh_preserves_issue_created_at_for_same_dedupe_key()
    {
        var manual = new ManualTimeProvider();
        var createdAt = manual.GetUtcNow().AddHours(-1);
        var descriptor = TestDescriptors.Create("accounts", "Accounts", produces: ["account.discovery"]);
        var contributor = new FakeContributor(
            descriptor,
            TestDescriptors.Ready(
                "accounts",
                manual.GetUtcNow(),
                issues: [new ApplicationIssue(
                    "account.unavailable",
                    ApplicationIssueSeverity.Warning,
                    "Account unavailable",
                    "accounts",
                    createdAt)]));
        await using var orch = CreateOrchestrator(manual);
        orch.Register(contributor);
        await orch.StartAsync();
        manual.Advance(TimeSpan.FromMinutes(5));
        contributor.SetContribution(TestDescriptors.Ready(
            "accounts",
            manual.GetUtcNow(),
            issues: [new ApplicationIssue(
                "account.unavailable",
                ApplicationIssueSeverity.Warning,
                "Account unavailable",
                "accounts",
                manual.GetUtcNow())]));

        await orch.RefreshAsync();

        var issue = Assert.Single(orch.Current.ActiveIssues);
        Assert.Equal(createdAt, issue.CreatedAt);
        Assert.Equal(manual.GetUtcNow(), issue.UpdatedAt);
    }

    [Fact]
    public async Task Error_outranks_receiving_data_when_selecting_overall_state()
    {
        var manual = new ManualTimeProvider();
        var failed = TestDescriptors.Ready(
            "accounts",
            manual.GetUtcNow(),
            issues: [new ApplicationIssue(
                "account.failed",
                ApplicationIssueSeverity.Error,
                "Account failure",
                "accounts",
                manual.GetUtcNow())]);
        var receiving = TestDescriptors.Ready(
            "runtime.homecoming",
            manual.GetUtcNow(),
            activity: ContributorActivity.ReceivingData);
        await using var orch = CreateOrchestrator(manual);
        orch.Register(new FakeContributor(TestDescriptors.Create(
            "accounts",
            "Accounts",
            produces: ["account.discovery"],
            importance: ApplicationContributorImportance.Critical), failed));
        orch.Register(new FakeContributor(TestDescriptors.Create(
            "runtime.homecoming", "Runtime", produces: ["runtime.status"]), receiving));

        await orch.StartAsync();

        Assert.Equal(OverallApplicationState.Error, orch.Current.State);
        Assert.Equal("2: Error from Critical provider", orch.GetDiagnostics().OverallStateSelectionRule);
    }

    [Fact]
    public async Task Actionable_warning_produces_needs_attention()
    {
        var manual = new ManualTimeProvider();
        var issue = new ApplicationIssue(
            "account.action_required",
            ApplicationIssueSeverity.Warning,
            "Sign in again",
            "accounts",
            manual.GetUtcNow())
        {
            RequiresUserAction = true
        };
        await using var orch = CreateOrchestrator(manual);
        orch.Register(new FakeContributor(
            TestDescriptors.Create("accounts", "Accounts", produces: ["account.discovery"]),
            TestDescriptors.Ready("accounts", manual.GetUtcNow(), issues: [issue])));

        await orch.StartAsync();

        Assert.Equal(OverallApplicationState.NeedsAttention, orch.Current.State);
        Assert.Equal("6: Warning requiring user action", orch.GetDiagnostics().OverallStateSelectionRule);
    }

    [Fact]
    public async Task Primary_issue_orders_severity_before_actionability_and_priority()
    {
        var manual = new ManualTimeProvider();
        var warning = new ApplicationIssue(
            "account.action_required",
            ApplicationIssueSeverity.Warning,
            "Sign in again",
            "accounts",
            manual.GetUtcNow())
        {
            RequiresUserAction = true
        };
        var error = new ApplicationIssue(
            "account.failed",
            ApplicationIssueSeverity.Error,
            "Account failure",
            "accounts",
            manual.GetUtcNow());
        await using var orch = CreateOrchestrator(manual);
        orch.Register(new FakeContributor(
            TestDescriptors.Create("accounts", "Accounts", produces: ["account.discovery"]),
            TestDescriptors.Ready("accounts", manual.GetUtcNow(), issues: [warning, error])));

        await orch.StartAsync();

        Assert.Equal("account.failed", orch.Current.PrimaryIssue!.Code);
        Assert.Equal(
            "severity desc, RequiresUserAction desc, importance desc, priority asc, CreatedAt asc, DedupeKey asc",
            orch.GetDiagnostics().PrimaryIssueSelectionRule);
        Assert.Contains(orch.GetEventHistory(), e => e.Kind == ApplicationOrchestrationEventKind.PrimaryIssueSelected);
    }

    private static ApplicationOrchestrator CreateOrchestrator(ManualTimeProvider manual) =>
        new(new ApplicationOrchestratorOptions
        {
            DebounceInterval = TimeSpan.FromMilliseconds(10),
            TimeProvider = manual,
            CycleValidationMode = CycleValidationMode.Throw
        });
}
