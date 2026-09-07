using CoHAnalytics.Orchestration;
using CoHAnalytics.Orchestration.Diagnostics;
using CoHAnalytics.Orchestration.Models;

namespace CoHAnalytics.Tests.Orchestration;

public sealed class ContributionRefreshTests
{
    [Fact]
    public async Task Timed_out_refresh_retains_previous_contribution_and_marks_it_stale()
    {
        var manual = new ManualTimeProvider();
        var descriptor = TestDescriptors.Create(
            "accounts",
            "Accounts",
            produces: ["account.discovery"],
            pullTimeout: TimeSpan.FromMilliseconds(20));
        var initial = TestDescriptors.Ready("accounts", manual.GetUtcNow());
        var contributor = new FakeContributor(descriptor, initial);
        await using var orch = CreateOrchestrator(manual, TimeSpan.FromMilliseconds(10));
        orch.Register(contributor);
        await orch.StartAsync();

        contributor.SetPullOverride(async cancellationToken =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return initial;
        });
        await orch.RefreshAsync("accounts");

        var provider = Assert.Single(orch.Current.Providers);
        Assert.Equal(initial.ObservedAt, provider.ObservedAt);
        Assert.True(provider.IsStale);
        Assert.Equal(1, orch.GetDiagnostics().ContributorTimeoutCounts["accounts"]);
        Assert.Contains(orch.GetEventHistory(), e => e.Kind == ApplicationOrchestrationEventKind.ContributionTimedOut);
    }

    [Fact]
    public async Task First_pull_timeout_synthesizes_unknown_contribution()
    {
        var manual = new ManualTimeProvider();
        var descriptor = TestDescriptors.Create(
            "accounts",
            "Accounts",
            produces: ["account.discovery"],
            pullTimeout: TimeSpan.FromMilliseconds(20));
        var contributor = new FakeContributor(descriptor);
        contributor.SetPullOverride(async cancellationToken =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return ApplicationContribution.Empty("accounts", manual.GetUtcNow());
        });
        await using var orch = CreateOrchestrator(manual, TimeSpan.FromMilliseconds(10));
        orch.Register(contributor);

        await orch.StartAsync();

        var provider = Assert.Single(orch.Current.Providers);
        Assert.Equal(ContributorHealth.Unknown, provider.Health);
        var issue = Assert.Single(orch.Current.ActiveIssues);
        Assert.Equal("orchestrator.contributor_timeout", issue.Code);
        Assert.Equal(ApplicationIssueSeverity.Warning, issue.Severity);
    }

    [Fact]
    public async Task Multiple_invalidations_are_debounced_into_one_targeted_pull()
    {
        var manual = new ManualTimeProvider();
        var contributor = new FakeContributor(TestDescriptors.Create(
            "accounts", "Accounts", produces: ["account.discovery"]));
        await using var orch = CreateOrchestrator(manual, TimeSpan.FromMilliseconds(30));
        orch.Register(contributor);
        await orch.StartAsync();
        var initialPulls = contributor.PullCount;

        contributor.RaiseChanged();
        contributor.RaiseChanged();
        contributor.RaiseChanged();
        await WaitUntilAsync(
            () => contributor.PullCount == initialPulls + 1,
            TimeSpan.FromSeconds(5));

        Assert.Equal(initialPulls + 1, contributor.PullCount);
        Assert.Equal(3, orch.GetEventHistory().Count(e => e.Kind == ApplicationOrchestrationEventKind.ContributionInvalidated));
        Assert.Equal(1, orch.GetEventHistory().Count(e => e.Kind == ApplicationOrchestrationEventKind.RefreshStarted
            && e.Summary == "Targeted refresh started"));
    }

    private static ApplicationOrchestrator CreateOrchestrator(
        ManualTimeProvider manual,
        TimeSpan debounceInterval) =>
        new(new ApplicationOrchestratorOptions
        {
            DebounceInterval = debounceInterval,
            DefaultPullTimeout = TimeSpan.FromMilliseconds(50),
            TimeProvider = manual,
            CycleValidationMode = CycleValidationMode.Throw
        });

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        while (!condition())
        {
            if (Environment.TickCount64 >= deadline)
            {
                break;
            }

            await Task.Yield();
            await Task.Delay(1);
        }
    }
}
