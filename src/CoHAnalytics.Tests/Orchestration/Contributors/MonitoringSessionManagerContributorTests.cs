using CoHAnalytics.Models;
using CoHAnalytics.Orchestration.Contributors;
using CoHAnalytics.Orchestration.Models;
using CoHAnalytics.Services;
using CoHAnalytics.Tests.Orchestration;
using CoHAnalytics.Tests.Services;

namespace CoHAnalytics.Tests.Orchestration.Contributors;

public sealed class MonitoringSessionManagerContributorTests
{
    [Fact]
    public void Descriptor_matches_governing_architecture()
    {
        var runtime = new FakeGameRuntimeService();
        using var manager = new MonitoringSessionManager(runtime, new FakeLogActivityService());
        using var contributor = new MonitoringSessionManagerContributor(manager);
        var descriptor = contributor.Descriptor;

        Assert.Equal(ApplicationProviders.MonitoringSessionManager, descriptor.ProviderId);
        Assert.Equal("monitoring.session-manager", descriptor.ProviderId);
        Assert.Equal("Monitoring Sessions", descriptor.DisplayName);
        Assert.Equal(
            "Coordinates active Homecoming monitoring contexts and selected log sources.",
            descriptor.Description);
        Assert.Equal("monitoring", descriptor.IconKey);
        Assert.Equal(ApplicationContributorImportance.Important, descriptor.Importance);
        Assert.Equal(ApplicationContributorDescriptor.ExpectedSchemaVersion, descriptor.SchemaVersion);

        Assert.Equal(
            [ApplicationCapabilities.MonitoringContexts, ApplicationCapabilities.MonitoringSourceSelection],
            descriptor.Produces);
        Assert.Equal(
            [ApplicationCapabilities.LogDiscovery, ApplicationCapabilities.LogActivity, ApplicationCapabilities.AccountDiscovery],
            descriptor.Requires);
        Assert.Equal([ApplicationCapabilities.HomecomingRuntime], descriptor.Optional);
    }

    [Fact]
    public async Task Zero_contexts_reports_ready_and_waiting_without_issues()
    {
        var runtime = new FakeGameRuntimeService { CurrentStatus = GameRuntimeStatus.Running };
        using var manager = new MonitoringSessionManager(runtime, new FakeLogActivityService());
        await manager.StartAsync();
        using var contributor = new MonitoringSessionManagerContributor(manager);

        var contribution = await contributor.GetContributionAsync();

        Assert.Equal(ContributorHealth.Ready, contribution.Health);
        Assert.Equal(ContributorActivity.Waiting, contribution.Activity);
        Assert.Empty(contribution.Issues);
        Assert.Equal(0, IntegerFact(contribution, ContributorFactKeys.MonitoringSessionManager.ContextCount));
    }

    [Fact]
    public async Task Zero_contexts_with_runtime_offline_still_reports_ready_and_waiting()
    {
        var runtime = new FakeGameRuntimeService { CurrentStatus = GameRuntimeStatus.Off };
        using var manager = new MonitoringSessionManager(runtime, new FakeLogActivityService());
        await manager.StartAsync();
        using var contributor = new MonitoringSessionManagerContributor(manager);

        var contribution = await contributor.GetContributionAsync();

        Assert.Equal(ContributorHealth.Ready, contribution.Health);
        Assert.Equal(ContributorActivity.Waiting, contribution.Activity);
        // No-context state is not an error and produces no suspension issue (nothing is suspended).
        Assert.Empty(contribution.Issues);
    }

    [Fact]
    public async Task All_contexts_runtime_suspended_reports_ready_and_waiting_with_informational_issue()
    {
        var runtime = new FakeGameRuntimeService { CurrentStatus = GameRuntimeStatus.Running };
        using var manager = new MonitoringSessionManager(runtime, new FakeLogActivityService());
        await manager.StartAsync();
        manager.AddContext();
        manager.AddContext(sourceId: CreateSourceId());
        using var contributor = new MonitoringSessionManagerContributor(manager);

        runtime.RaiseStatusChanged(GameRuntimeStatus.Running, GameRuntimeStatus.Off);
        var contribution = await contributor.GetContributionAsync();

        Assert.Equal(ContributorHealth.Ready, contribution.Health);
        Assert.Equal(ContributorActivity.Waiting, contribution.Activity);
        Assert.Equal(2, IntegerFact(contribution, ContributorFactKeys.MonitoringSessionManager.SuspendedContextCount));

        var issue = Assert.Single(
            contribution.Issues,
            issue => issue.Code == ContributorIssueCodes.MonitoringSessionManager.RuntimeUnavailable);
        Assert.Equal(ApplicationIssueSeverity.Information, issue.Severity);
        Assert.False(issue.RequiresUserAction);
    }

    [Fact]
    public async Task Waiting_for_source_contexts_report_ready_and_waiting()
    {
        var runtime = new FakeGameRuntimeService { CurrentStatus = GameRuntimeStatus.Running };
        using var manager = new MonitoringSessionManager(runtime, new FakeLogActivityService());
        await manager.StartAsync();
        manager.AddContext();
        using var contributor = new MonitoringSessionManagerContributor(manager);

        var contribution = await contributor.GetContributionAsync();

        Assert.Equal(ContributorHealth.Ready, contribution.Health);
        Assert.Equal(ContributorActivity.Waiting, contribution.Activity);
        Assert.Equal(1, IntegerFact(contribution, ContributorFactKeys.MonitoringSessionManager.WaitingContextCount));
    }

    [Fact]
    public async Task Ready_contexts_report_ready_and_active_but_never_receiving_data()
    {
        var runtime = new FakeGameRuntimeService { CurrentStatus = GameRuntimeStatus.Running };
        using var manager = new MonitoringSessionManager(runtime, new FakeLogActivityService());
        await manager.StartAsync();
        manager.AddContext(sourceId: CreateSourceId());
        using var contributor = new MonitoringSessionManagerContributor(manager);

        var contribution = await contributor.GetContributionAsync();

        Assert.Equal(ContributorHealth.Ready, contribution.Health);
        Assert.Equal(ContributorActivity.Active, contribution.Activity);
        Assert.NotEqual(ContributorActivity.ReceivingData, contribution.Activity);
    }

    [Fact]
    public async Task Mixed_states_report_active_because_at_least_one_context_is_ready()
    {
        var runtime = new FakeGameRuntimeService { CurrentStatus = GameRuntimeStatus.Running };
        using var manager = new MonitoringSessionManager(runtime, new FakeLogActivityService());
        await manager.StartAsync();
        manager.AddContext();
        manager.AddContext(sourceId: CreateSourceId());
        using var contributor = new MonitoringSessionManagerContributor(manager);

        var contribution = await contributor.GetContributionAsync();

        Assert.Equal(ContributorHealth.Ready, contribution.Health);
        Assert.Equal(ContributorActivity.Active, contribution.Activity);
        Assert.Equal(1, IntegerFact(contribution, ContributorFactKeys.MonitoringSessionManager.ActiveContextCount));
        Assert.Equal(1, IntegerFact(contribution, ContributorFactKeys.MonitoringSessionManager.WaitingContextCount));
    }

    [Fact]
    public async Task Context_error_degrades_health_but_never_reports_error_at_aggregate_level()
    {
        var runtime = new FakeGameRuntimeService { CurrentStatus = GameRuntimeStatus.Running };
        using var manager = new MonitoringSessionManager(runtime, new FakeLogActivityService());
        await manager.StartAsync();
        var brokenContext = manager.AddContext();
        manager.AddContext();
        manager.MarkContextErrorForTests(brokenContext);
        using var contributor = new MonitoringSessionManagerContributor(manager);

        var contribution = await contributor.GetContributionAsync();

        Assert.Equal(ContributorHealth.Degraded, contribution.Health);
        Assert.NotEqual(ContributorHealth.Error, contribution.Health);
        var issue = Assert.Single(
            contribution.Issues,
            issue => issue.Code == ContributorIssueCodes.MonitoringSessionManager.ContextError);
        Assert.Equal(ApplicationIssueSeverity.Warning, issue.Severity);
    }

    [Fact]
    public async Task Runtime_availability_fact_reflects_manager_observed_runtime_status()
    {
        var runtime = new FakeGameRuntimeService { CurrentStatus = GameRuntimeStatus.Running };
        using var manager = new MonitoringSessionManager(runtime, new FakeLogActivityService());
        await manager.StartAsync();
        using var contributor = new MonitoringSessionManagerContributor(manager);

        var whileRunning = await contributor.GetContributionAsync();
        Assert.True(BooleanFact(whileRunning, ContributorFactKeys.MonitoringSessionManager.RuntimeAvailable));

        runtime.RaiseStatusChanged(GameRuntimeStatus.Running, GameRuntimeStatus.Off);
        var whileOffline = await contributor.GetContributionAsync();
        Assert.False(BooleanFact(whileOffline, ContributorFactKeys.MonitoringSessionManager.RuntimeAvailable));
    }

    [Fact]
    public async Task Contribution_never_claims_receiving_data()
    {
        var runtime = new FakeGameRuntimeService { CurrentStatus = GameRuntimeStatus.Running };
        using var manager = new MonitoringSessionManager(runtime, new FakeLogActivityService());
        await manager.StartAsync();
        manager.AddContext(sourceId: CreateSourceId());
        using var contributor = new MonitoringSessionManagerContributor(manager);

        var contribution = await contributor.GetContributionAsync();

        Assert.NotEqual(ContributorActivity.ReceivingData, contribution.Activity);
    }

    [Fact]
    public async Task Contribution_collections_are_immutable()
    {
        var runtime = new FakeGameRuntimeService { CurrentStatus = GameRuntimeStatus.Running };
        using var manager = new MonitoringSessionManager(runtime, new FakeLogActivityService());
        await manager.StartAsync();
        manager.AddContext();
        using var contributor = new MonitoringSessionManagerContributor(manager);

        var contribution = await contributor.GetContributionAsync();

        Assert.IsAssignableFrom<IReadOnlyList<ApplicationFact>>(contribution.Facts);
        Assert.IsAssignableFrom<IReadOnlyList<ApplicationIssue>>(contribution.Issues);
        Assert.IsAssignableFrom<IReadOnlyList<ApplicationAction>>(contribution.Actions);
        Assert.Empty(contribution.Actions);
    }

    [Fact]
    public async Task Manager_state_changed_forwards_as_data_free_invalidation()
    {
        var runtime = new FakeGameRuntimeService { CurrentStatus = GameRuntimeStatus.Running };
        using var manager = new MonitoringSessionManager(runtime, new FakeLogActivityService());
        await manager.StartAsync();
        using var contributor = new MonitoringSessionManagerContributor(manager);
        var invalidations = 0;
        contributor.ContributionChanged += (_, _) => invalidations++;

        manager.AddContext();

        Assert.Equal(1, invalidations);
    }

    [Fact]
    public async Task Disposal_unsubscribes_from_manager_events()
    {
        var runtime = new FakeGameRuntimeService { CurrentStatus = GameRuntimeStatus.Running };
        using var manager = new MonitoringSessionManager(runtime, new FakeLogActivityService());
        await manager.StartAsync();
        var contributor = new MonitoringSessionManagerContributor(manager);
        var invalidations = 0;
        contributor.ContributionChanged += (_, _) => invalidations++;

        contributor.Dispose();
        manager.AddContext();

        Assert.Equal(0, invalidations);
    }

    [Fact]
    public async Task Contributor_lifecycle_starts_and_stops_the_manager_exactly_once()
    {
        var runtime = new FakeGameRuntimeService { CurrentStatus = GameRuntimeStatus.Running };
        using var manager = new MonitoringSessionManager(runtime, new FakeLogActivityService());
        using var contributor = new MonitoringSessionManagerContributor(manager);

        await contributor.StartAsync();
        Assert.True(manager.IsRunning);

        await contributor.StopAsync();
        Assert.False(manager.IsRunning);
    }

    private static LogSourceId CreateSourceId() =>
        LogSourceId.Create("acct-1", "Alpha", @"C:\fake\Logs\chatlog 2026-08-04.txt", new DateOnly(2026, 8, 4));

    private static long IntegerFact(ApplicationContribution contribution, string key) =>
        Assert.IsType<ApplicationFactValue.Integer>(
            contribution.Facts.Single(fact => fact.Key == key).Value).Value;

    private static bool BooleanFact(ApplicationContribution contribution, string key) =>
        Assert.IsType<ApplicationFactValue.Boolean>(
            contribution.Facts.Single(fact => fact.Key == key).Value).Value;
}
