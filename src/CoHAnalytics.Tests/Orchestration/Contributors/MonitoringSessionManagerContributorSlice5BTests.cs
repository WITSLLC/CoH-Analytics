using CoHAnalytics.Models;
using CoHAnalytics.Orchestration.Contributors;
using CoHAnalytics.Orchestration.Models;
using CoHAnalytics.Services;
using CoHAnalytics.Tests.Orchestration;
using CoHAnalytics.Tests.Services;

namespace CoHAnalytics.Tests.Orchestration.Contributors;

public sealed class MonitoringSessionManagerContributorSlice5BTests
{
    private static (MonitoringSessionManager Manager, FakeGameRuntimeService Runtime, FakeLogActivityService LogActivity, ManualTimeProvider Time)
        CreateManager(GameRuntimeStatus runtimeStatus = GameRuntimeStatus.Running)
    {
        var runtime = new FakeGameRuntimeService { CurrentStatus = runtimeStatus };
        var logActivity = new FakeLogActivityService();
        var time = new ManualTimeProvider();
        var manager = new MonitoringSessionManager(runtime, logActivity, new MonitoringSessionManagerOptions { TimeProvider = time });
        return (manager, runtime, logActivity, time);
    }

    [Fact]
    public async Task Descriptor_is_unchanged_by_slice_5b()
    {
        var (manager, _, _, _) = CreateManager();
        using var contributor = new MonitoringSessionManagerContributor(manager);

        Assert.Equal("monitoring.session-manager", contributor.Descriptor.ProviderId);
        Assert.Equal(
            [ApplicationCapabilities.MonitoringContexts, ApplicationCapabilities.MonitoringSourceSelection],
            contributor.Descriptor.Produces);
    }

    [Fact]
    public async Task Pending_offer_count_fact_reflects_offers()
    {
        var (manager, _, logActivity, time) = CreateManager();
        var firstSource = TestLogCandidates.SourceId("acct-1", "Alpha");
        var secondSource = TestLogCandidates.SourceId("acct-1", "Alpha", fileNameSuffix: "-second");
        logActivity.Current = TestLogCandidates.Snapshot(time.GetUtcNow(), TestLogCandidates.Create(firstSource, LogSourceActivityState.Growing, time.GetUtcNow()));
        await manager.StartAsync();
        logActivity.Current = TestLogCandidates.Snapshot(
            time.GetUtcNow(),
            TestLogCandidates.Create(firstSource, LogSourceActivityState.Growing, time.GetUtcNow()),
            TestLogCandidates.Create(secondSource, LogSourceActivityState.Growing, time.GetUtcNow()));
        logActivity.RaiseActivityChanged();
        using var contributor = new MonitoringSessionManagerContributor(manager);

        var contribution = await contributor.GetContributionAsync();

        Assert.Equal(1, IntegerFact(contribution, ContributorFactKeys.MonitoringSessionManager.PendingOfferCount));
        Assert.Equal(1, IntegerFact(contribution, ContributorFactKeys.MonitoringSessionManager.ClaimedSourceCount));
    }

    [Fact]
    public async Task Ambiguous_source_emits_actionable_warning()
    {
        var (manager, _, logActivity, time) = CreateManager();
        var first = TestLogCandidates.Create(TestLogCandidates.SourceId("acct-1", "Alpha"), LogSourceActivityState.Growing, time.GetUtcNow());
        var second = TestLogCandidates.Create(
            TestLogCandidates.SourceId("acct-1", "Alpha", fileNameSuffix: "-second"),
            LogSourceActivityState.Growing,
            time.GetUtcNow());
        logActivity.Current = TestLogCandidates.Snapshot(time.GetUtcNow(), first, second);
        await manager.StartAsync();
        using var contributor = new MonitoringSessionManagerContributor(manager);

        var contribution = await contributor.GetContributionAsync();

        Assert.Equal(ContributorHealth.Ready, contribution.Health);
        var issue = Assert.Single(
            contribution.Issues,
            issue => issue.Code == ContributorIssueCodes.MonitoringSessionManager.SourceSelectionRequired);
        Assert.Equal(ApplicationIssueSeverity.Warning, issue.Severity);
        Assert.True(issue.RequiresUserAction);
    }

    [Fact]
    public async Task Concurrent_monitoring_reports_state_without_requesting_enrollment()
    {
        var (manager, _, logActivity, time) = CreateManager();
        var firstSource = TestLogCandidates.SourceId("acct-1", "Alpha");
        var secondSource = TestLogCandidates.SourceId("acct-2", "Beta");
        logActivity.Current = TestLogCandidates.Snapshot(time.GetUtcNow(), TestLogCandidates.Create(firstSource, LogSourceActivityState.Growing, time.GetUtcNow()));
        await manager.StartAsync();
        logActivity.Current = TestLogCandidates.Snapshot(
            time.GetUtcNow(),
            TestLogCandidates.Create(firstSource, LogSourceActivityState.Growing, time.GetUtcNow()),
            TestLogCandidates.Create(secondSource, LogSourceActivityState.Growing, time.GetUtcNow()));
        logActivity.RaiseActivityChanged();
        using var contributor = new MonitoringSessionManagerContributor(manager);

        var contribution = await contributor.GetContributionAsync();

        Assert.Equal(0, IntegerFact(contribution, ContributorFactKeys.MonitoringSessionManager.PendingOfferCount));
        var issue = Assert.Single(
            contribution.Issues,
            issue => issue.Code == ContributorIssueCodes.MonitoringSessionManager.ConcurrentSessionsMonitored);
        Assert.Equal(ApplicationIssueSeverity.Information, issue.Severity);
        Assert.False(issue.RequiresUserAction);
        Assert.Equal("2 active Homecoming sessions are being monitored.", issue.Summary);
        Assert.DoesNotContain(
            contribution.Issues,
            issue => issue.Code == ContributorIssueCodes.MonitoringSessionManager.SourceSelectionRequired);
    }

    [Fact]
    public async Task Source_unavailable_emits_warning_when_context_cannot_proceed()
    {
        var (manager, _, logActivity, time) = CreateManager();
        var sourceId = TestLogCandidates.SourceId();
        logActivity.Current = TestLogCandidates.Snapshot(time.GetUtcNow(), TestLogCandidates.Create(sourceId, LogSourceActivityState.Growing, time.GetUtcNow()));
        await manager.StartAsync();
        logActivity.Current = TestLogCandidates.Snapshot(time.GetUtcNow(), TestLogCandidates.Create(sourceId, LogSourceActivityState.Unavailable, time.GetUtcNow(), exists: false));
        logActivity.RaiseActivityChanged();
        using var contributor = new MonitoringSessionManagerContributor(manager);

        var contribution = await contributor.GetContributionAsync();

        var issue = Assert.Single(
            contribution.Issues,
            issue => issue.Code == ContributorIssueCodes.MonitoringSessionManager.SourceUnavailable);
        Assert.Equal(ApplicationIssueSeverity.Warning, issue.Severity);
    }

    [Fact]
    public async Task Contribution_never_reports_receiving_data_with_offers_pending()
    {
        var (manager, _, logActivity, time) = CreateManager();
        var firstSource = TestLogCandidates.SourceId("acct-1", "Alpha");
        var secondSource = TestLogCandidates.SourceId("acct-1", "Alpha", fileNameSuffix: "-second");
        logActivity.Current = TestLogCandidates.Snapshot(time.GetUtcNow(), TestLogCandidates.Create(firstSource, LogSourceActivityState.Growing, time.GetUtcNow()));
        await manager.StartAsync();
        logActivity.Current = TestLogCandidates.Snapshot(
            time.GetUtcNow(),
            TestLogCandidates.Create(firstSource, LogSourceActivityState.Growing, time.GetUtcNow()),
            TestLogCandidates.Create(secondSource, LogSourceActivityState.Growing, time.GetUtcNow()));
        logActivity.RaiseActivityChanged();
        using var contributor = new MonitoringSessionManagerContributor(manager);

        var contribution = await contributor.GetContributionAsync();

        Assert.NotEqual(ContributorActivity.ReceivingData, contribution.Activity);
    }

    [Fact]
    public async Task No_character_parser_or_session_facts_are_emitted()
    {
        var (manager, _, _, _) = CreateManager();
        await manager.StartAsync();
        using var contributor = new MonitoringSessionManagerContributor(manager);

        var contribution = await contributor.GetContributionAsync();

        Assert.DoesNotContain(contribution.Facts, fact => fact.Key.Contains("character", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(contribution.Facts, fact => fact.Key.Contains("parser", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(contribution.Facts, fact => fact.Key.Contains("gameplay", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Contribution_output_is_immutable()
    {
        var (manager, _, _, _) = CreateManager();
        await manager.StartAsync();
        using var contributor = new MonitoringSessionManagerContributor(manager);

        var contribution = await contributor.GetContributionAsync();

        Assert.IsAssignableFrom<IReadOnlyList<ApplicationFact>>(contribution.Facts);
        Assert.IsAssignableFrom<IReadOnlyList<ApplicationIssue>>(contribution.Issues);
    }

    private static long IntegerFact(ApplicationContribution contribution, string key) =>
        Assert.IsType<ApplicationFactValue.Integer>(
            contribution.Facts.Single(fact => fact.Key == key).Value).Value;
}
