using CoHAnalytics.Models;
using CoHAnalytics.Services;
using CoHAnalytics.Tests.Orchestration;

namespace CoHAnalytics.Tests.Services;

public sealed class MonitoringSessionProcessInstanceOwnershipTests
{
    private static readonly DateTimeOffset StartA = new(2026, 8, 14, 1, 15, 39, TimeSpan.Zero);
    private static readonly DateTimeOffset StartB = new(2026, 8, 14, 1, 16, 7, TimeSpan.Zero);
    private static readonly string ExecutablePath =
        @"C:\Games\Homecoming\bin\win64\live\cityofheroes.exe";

    [Fact]
    public async Task Single_process_instance_binds_to_unambiguous_TestAccount_context()
    {
        var (manager, runtime, logActivity, time) = CreateStartedManager(
            [CreateClient(3_524, StartA)]);

        var primarySource = TestLogCandidates.SourceId("TestAccount", "TestAccount");
        logActivity.Current = TestLogCandidates.Snapshot(
            time.GetUtcNow(),
            TestLogCandidates.Create(primarySource, LogSourceActivityState.Growing, time.GetUtcNow()));

        logActivity.RaiseActivityChanged();

        var context = Assert.Single(manager.Current.Contexts);
        Assert.Equal("TestAccount", context.AccountStableId);
        Assert.NotNull(context.ProcessInstance);
        Assert.Equal(3_524, context.ProcessInstance!.ProcessId);
        Assert.Equal(StartA, context.ProcessInstance.ProcessStartTime);
        Assert.Equal(ExecutablePath, context.ProcessInstance.ExecutablePath);
    }

    [Fact]
    public async Task Zero_process_instances_prevent_process_instance_binding()
    {
        var (manager, runtime, logActivity, time) = CreateStartedManager([]);

        var primarySource = TestLogCandidates.SourceId("TestAccount", "TestAccount");
        logActivity.Current = TestLogCandidates.Snapshot(
            time.GetUtcNow(),
            TestLogCandidates.Create(primarySource, LogSourceActivityState.Growing, time.GetUtcNow()));

        logActivity.RaiseActivityChanged();

        var context = Assert.Single(manager.Current.Contexts);
        Assert.Null(context.ProcessInstance);
    }

    [Fact]
    public async Task Two_process_instances_do_not_guess_pid_ownership_for_TestAccount_or_AltAccount()
    {
        var (manager, runtime, logActivity, time) = CreateStartedManager(
            [CreateClient(3_524, StartA), CreateClient(6_356, StartB)]);

        var primarySource = TestLogCandidates.SourceId("TestAccount", "TestAccount");
        var altSource = TestLogCandidates.SourceId("AltAccount", "AltAccount");
        logActivity.Current = TestLogCandidates.Snapshot(
            time.GetUtcNow(),
            TestLogCandidates.Create(primarySource, LogSourceActivityState.Growing, time.GetUtcNow()),
            TestLogCandidates.Create(altSource, LogSourceActivityState.Growing, time.GetUtcNow()));

        logActivity.RaiseActivityChanged();

        var primaryContext = Assert.Single(
            manager.Current.Contexts,
            context => string.Equals(context.AccountStableId, "TestAccount", StringComparison.Ordinal));
        var altContext = Assert.Single(
            manager.Current.Contexts,
            context => string.Equals(context.AccountStableId, "AltAccount", StringComparison.Ordinal));

        Assert.Null(primaryContext.ProcessInstance);
        Assert.Null(altContext.ProcessInstance);
    }

    [Fact]
    public async Task Two_process_instances_still_allow_both_monitoring_contexts()
    {
        var (manager, _, logActivity, time) = CreateStartedManager(
            [CreateClient(3_524, StartA), CreateClient(6_356, StartB)]);

        var primarySource = TestLogCandidates.SourceId("TestAccount", "TestAccount");
        var altSource = TestLogCandidates.SourceId("AltAccount", "AltAccount");
        logActivity.Current = TestLogCandidates.Snapshot(
            time.GetUtcNow(),
            TestLogCandidates.Create(primarySource, LogSourceActivityState.Growing, time.GetUtcNow()),
            TestLogCandidates.Create(altSource, LogSourceActivityState.Growing, time.GetUtcNow()));

        logActivity.RaiseActivityChanged();

        Assert.Equal(2, manager.Current.Contexts.Count(context => context.State != MonitoringContextState.Stopped));
        Assert.Contains(manager.Current.Contexts, context => context.AccountStableId == "TestAccount");
        Assert.Contains(manager.Current.Contexts, context => context.AccountStableId == "AltAccount");
    }

    [Fact]
    public async Task Existing_unbound_context_acquires_process_instance_when_runtime_becomes_unambiguous()
    {
        var (manager, runtime, logActivity, time) = CreateStartedManager(
            [CreateClient(3_524, StartA), CreateClient(6_356, StartB)]);

        var primarySource = TestLogCandidates.SourceId("TestAccount", "TestAccount");
        logActivity.Current = TestLogCandidates.Snapshot(
            time.GetUtcNow(),
            TestLogCandidates.Create(primarySource, LogSourceActivityState.Growing, time.GetUtcNow()));

        logActivity.RaiseActivityChanged();

        var context = Assert.Single(manager.Current.Contexts);
        Assert.Null(context.ProcessInstance);

        var soleClient = CreateClient(3_524, StartA);
        runtime.RaiseStatusChanged(
            GameRuntimeStatus.Running,
            GameRuntimeStatus.Running,
            [soleClient]);

        context = Assert.Single(manager.Current.Contexts);
        Assert.NotNull(context.ProcessInstance);
        Assert.Equal(soleClient, context.ProcessInstance);
    }

    [Fact]
    public async Task Same_pid_and_start_time_preserves_existing_process_instance_ownership()
    {
        var soleClient = CreateClient(3_524, StartA);
        var (manager, runtime, logActivity, time) = CreateStartedManager([soleClient]);

        var primarySource = TestLogCandidates.SourceId("TestAccount", "TestAccount");
        logActivity.Current = TestLogCandidates.Snapshot(
            time.GetUtcNow(),
            TestLogCandidates.Create(primarySource, LogSourceActivityState.Growing, time.GetUtcNow()));

        logActivity.RaiseActivityChanged();

        var before = Assert.Single(manager.Current.Contexts).ProcessInstance;
        Assert.NotNull(before);

        runtime.RaiseStatusChanged(
            GameRuntimeStatus.Running,
            GameRuntimeStatus.Running,
            [soleClient]);

        var after = Assert.Single(manager.Current.Contexts).ProcessInstance;
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task Pid_reuse_with_different_start_time_does_not_reassign_existing_process_instance()
    {
        var originalClient = CreateClient(3_524, StartA);
        var (manager, runtime, logActivity, time) = CreateStartedManager([originalClient]);

        var primarySource = TestLogCandidates.SourceId("TestAccount", "TestAccount");
        logActivity.Current = TestLogCandidates.Snapshot(
            time.GetUtcNow(),
            TestLogCandidates.Create(primarySource, LogSourceActivityState.Growing, time.GetUtcNow()));

        logActivity.RaiseActivityChanged();

        var context = Assert.Single(manager.Current.Contexts);
        Assert.Equal(originalClient, context.ProcessInstance);

        var reusedPidClient = CreateClient(3_524, StartB);
        Assert.NotEqual(originalClient, reusedPidClient);

        runtime.RaiseStatusChanged(
            GameRuntimeStatus.Running,
            GameRuntimeStatus.Running,
            [reusedPidClient]);

        context = Assert.Single(manager.Current.Contexts);
        Assert.Equal(originalClient, context.ProcessInstance);
    }

    [Fact]
    public async Task Starting_second_homecoming_client_does_not_remove_existing_contexts()
    {
        var (manager, runtime, logActivity, time) = CreateStartedManager([CreateClient(3_524, StartA)]);

        var primarySource = TestLogCandidates.SourceId("TestAccount", "TestAccount");
        logActivity.Current = TestLogCandidates.Snapshot(
            time.GetUtcNow(),
            TestLogCandidates.Create(primarySource, LogSourceActivityState.Growing, time.GetUtcNow()));

        logActivity.RaiseActivityChanged();
        var primaryContextId = Assert.Single(manager.Current.Contexts).ContextId;

        var altSource = TestLogCandidates.SourceId("AltAccount", "AltAccount");
        logActivity.Current = TestLogCandidates.Snapshot(
            revision: 2,
            time.GetUtcNow(),
            TestLogCandidates.Create(primarySource, LogSourceActivityState.Growing, time.GetUtcNow()),
            TestLogCandidates.Create(altSource, LogSourceActivityState.Growing, time.GetUtcNow()));

        runtime.RaiseStatusChanged(
            GameRuntimeStatus.Running,
            GameRuntimeStatus.Running,
            [CreateClient(3_524, StartA), CreateClient(6_356, StartB)]);

        logActivity.RaiseActivityChanged();

        Assert.Contains(
            manager.Current.Contexts,
            context => context.ContextId == primaryContextId && context.AccountStableId == "TestAccount");
        Assert.Contains(manager.Current.Contexts, context => context.AccountStableId == "AltAccount");
    }

    [Fact]
    public async Task Multi_client_ambiguity_preserves_existing_proven_process_instance_binding()
    {
        var soleClient = CreateClient(3_524, StartA);
        var (manager, runtime, logActivity, time) = CreateStartedManager([soleClient]);

        var primarySource = TestLogCandidates.SourceId("TestAccount", "TestAccount");
        logActivity.Current = TestLogCandidates.Snapshot(
            time.GetUtcNow(),
            TestLogCandidates.Create(primarySource, LogSourceActivityState.Growing, time.GetUtcNow()));

        logActivity.RaiseActivityChanged();

        var primaryContext = Assert.Single(manager.Current.Contexts);
        Assert.Equal(soleClient, primaryContext.ProcessInstance);

        var altSource = TestLogCandidates.SourceId("AltAccount", "AltAccount");
        logActivity.Current = TestLogCandidates.Snapshot(
            revision: 2,
            time.GetUtcNow(),
            TestLogCandidates.Create(primarySource, LogSourceActivityState.Growing, time.GetUtcNow()),
            TestLogCandidates.Create(altSource, LogSourceActivityState.Growing, time.GetUtcNow()));

        runtime.RaiseStatusChanged(
            GameRuntimeStatus.Running,
            GameRuntimeStatus.Running,
            [soleClient, CreateClient(6_356, StartB)]);

        logActivity.RaiseActivityChanged();

        primaryContext = Assert.Single(
            manager.Current.Contexts,
            context => context.AccountStableId == "TestAccount");
        var altContext = Assert.Single(
            manager.Current.Contexts,
            context => context.AccountStableId == "AltAccount");

        Assert.Equal(soleClient, primaryContext.ProcessInstance);
        Assert.Null(altContext.ProcessInstance);
    }

    private static HomecomingProcessInstance CreateClient(int processId, DateTimeOffset processStartTime) =>
        FakeGameRuntimeService.CreateClient(processId, processStartTime, ExecutablePath);

    private static (
        MonitoringSessionManager Manager,
        FakeGameRuntimeService Runtime,
        FakeLogActivityService LogActivity,
        ManualTimeProvider Time) CreateStartedManager(IReadOnlyList<HomecomingProcessInstance> runningClients)
    {
        var runtime = new FakeGameRuntimeService
        {
            CurrentStatus = GameRuntimeStatus.Running,
            RunningClients = runningClients,
            RunningClientCount = runningClients.Count
        };
        var logActivity = new FakeLogActivityService();
        var time = new ManualTimeProvider();
        var manager = new MonitoringSessionManager(
            runtime,
            logActivity,
            new MonitoringSessionManagerOptions { TimeProvider = time });

        manager.StartAsync().GetAwaiter().GetResult();
        return (manager, runtime, logActivity, time);
    }
}
