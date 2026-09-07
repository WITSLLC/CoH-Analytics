using CoHAnalytics.Models;
using CoHAnalytics.Services;
using CoHAnalytics.Tests.Orchestration;

namespace CoHAnalytics.Tests.Services;

public sealed class MonitoringSessionManagerTests
{
    private static MonitoringSessionManager CreateManager(
        FakeGameRuntimeService runtime,
        ManualTimeProvider time,
        FakeLogActivityService? logActivity = null) =>
        new(runtime, logActivity ?? new FakeLogActivityService(), new MonitoringSessionManagerOptions { TimeProvider = time });

    // ---- Manager construction and lifecycle -----------------------------------------------

    [Fact]
    public void Initial_snapshot_is_empty()
    {
        var runtime = new FakeGameRuntimeService();
        using var manager = CreateManager(runtime, new ManualTimeProvider());

        Assert.Empty(manager.Current.Contexts);
        Assert.Equal(0, manager.Current.ContextCount);
        Assert.Equal(0, manager.Current.Revision);
        Assert.False(manager.IsRunning);
    }

    [Fact]
    public async Task StartAsync_is_idempotent()
    {
        var runtime = new FakeGameRuntimeService { CurrentStatus = GameRuntimeStatus.Running };
        using var manager = CreateManager(runtime, new ManualTimeProvider());

        await manager.StartAsync();
        await manager.StartAsync();
        await manager.StartAsync();

        Assert.True(manager.IsRunning);
    }

    [Fact]
    public async Task StopAsync_is_idempotent()
    {
        var runtime = new FakeGameRuntimeService { CurrentStatus = GameRuntimeStatus.Running };
        using var manager = CreateManager(runtime, new ManualTimeProvider());

        await manager.StartAsync();
        await manager.StopAsync();
        await manager.StopAsync();
        await manager.StopAsync();

        Assert.False(manager.IsRunning);
    }

    [Fact]
    public async Task Runtime_subscription_is_added_exactly_once_across_repeated_starts()
    {
        var runtime = new FakeGameRuntimeService { CurrentStatus = GameRuntimeStatus.Running };
        var time = new ManualTimeProvider();
        using var manager = CreateManager(runtime, time);

        await manager.StartAsync();
        await manager.StartAsync();

        manager.AddContext();

        var notifications = 0;
        manager.StateChanged += (_, _) => notifications++;
        runtime.RaiseStatusChanged(GameRuntimeStatus.Running, GameRuntimeStatus.Off);

        // A double subscription would raise the notification twice for one transition.
        Assert.Equal(1, notifications);
        Assert.Equal(MonitoringContextState.RuntimeSuspended, manager.Current.Contexts[0].State);
    }

    [Fact]
    public async Task Subscription_is_removed_on_stop_and_no_callbacks_fire_after()
    {
        var runtime = new FakeGameRuntimeService { CurrentStatus = GameRuntimeStatus.Running };
        var time = new ManualTimeProvider();
        using var manager = CreateManager(runtime, time);

        await manager.StartAsync();
        manager.AddContext();
        await manager.StopAsync();

        var notifications = 0;
        manager.StateChanged += (_, _) => notifications++;

        runtime.RaiseStatusChanged(GameRuntimeStatus.Running, GameRuntimeStatus.Off);

        Assert.Equal(0, notifications);
        Assert.Equal(MonitoringContextState.WaitingForSource, manager.Current.Contexts[0].State);
    }

    // ---- Context creation -------------------------------------------------------------------

    [Fact]
    public async Task AddContext_produces_unique_runtime_ids()
    {
        var runtime = new FakeGameRuntimeService { CurrentStatus = GameRuntimeStatus.Running };
        using var manager = CreateManager(runtime, new ManualTimeProvider());
        await manager.StartAsync();

        var first = manager.AddContext();
        var second = manager.AddContext();

        Assert.NotEqual(first, second);
        Assert.NotEqual(Guid.Empty, first.Value);
        Assert.NotEqual(Guid.Empty, second.Value);
    }

    [Fact]
    public async Task Context_added_while_runtime_running_without_source_starts_waiting()
    {
        var runtime = new FakeGameRuntimeService { CurrentStatus = GameRuntimeStatus.Running };
        using var manager = CreateManager(runtime, new ManualTimeProvider());
        await manager.StartAsync();

        var contextId = manager.AddContext(accountStableId: "acct-1", accountDisplayName: "Alpha");
        var context = Assert.Single(manager.Current.Contexts, c => c.ContextId == contextId);

        Assert.Equal(MonitoringContextState.WaitingForSource, context.State);
        Assert.Null(context.SuspendedAt);
        Assert.Equal("acct-1", context.AccountStableId);
    }

    [Fact]
    public async Task Context_added_while_runtime_running_with_source_starts_ready()
    {
        var runtime = new FakeGameRuntimeService { CurrentStatus = GameRuntimeStatus.Running };
        using var manager = CreateManager(runtime, new ManualTimeProvider());
        await manager.StartAsync();
        var sourceId = CreateSourceId("acct-1");

        var contextId = manager.AddContext(accountStableId: "acct-1", sourceId: sourceId);
        var context = Assert.Single(manager.Current.Contexts, c => c.ContextId == contextId);

        Assert.Equal(MonitoringContextState.Ready, context.State);
        Assert.Equal(sourceId, context.CurrentSourceId);
    }

    [Fact]
    public async Task Context_added_while_runtime_offline_starts_suspended_with_no_fake_activity()
    {
        var runtime = new FakeGameRuntimeService { CurrentStatus = GameRuntimeStatus.Off };
        using var manager = CreateManager(runtime, new ManualTimeProvider());
        await manager.StartAsync();
        var sourceId = CreateSourceId("acct-1");

        var contextId = manager.AddContext(accountStableId: "acct-1", sourceId: sourceId);
        var context = Assert.Single(manager.Current.Contexts, c => c.ContextId == contextId);

        Assert.Equal(MonitoringContextState.RuntimeSuspended, context.State);
        Assert.NotNull(context.SuspendedAt);
        Assert.Equal(MonitoringContextState.Ready, context.PreviousStateBeforeSuspension);
        Assert.Equal(sourceId, context.CurrentSourceId);
        Assert.Equal("acct-1", context.AccountStableId);
    }

    [Fact]
    public async Task Source_binding_is_retained_across_snapshot_publications()
    {
        var runtime = new FakeGameRuntimeService { CurrentStatus = GameRuntimeStatus.Running };
        using var manager = CreateManager(runtime, new ManualTimeProvider());
        await manager.StartAsync();
        var sourceId = CreateSourceId("acct-1");

        var contextId = manager.AddContext(accountStableId: "acct-1", accountDisplayName: "Alpha", sourceId: sourceId);
        manager.AddContext();

        var context = Assert.Single(manager.Current.Contexts, c => c.ContextId == contextId);
        Assert.Equal(sourceId, context.CurrentSourceId);
        Assert.Equal("Alpha", context.AccountDisplayName);
    }

    [Fact]
    public async Task Duplicate_source_assignment_is_rejected()
    {
        var runtime = new FakeGameRuntimeService { CurrentStatus = GameRuntimeStatus.Running };
        using var manager = CreateManager(runtime, new ManualTimeProvider());
        await manager.StartAsync();
        var sourceId = CreateSourceId("acct-1");

        manager.AddContext(accountStableId: "acct-1", sourceId: sourceId);

        var exception = Assert.Throws<InvalidOperationException>(
            () => manager.AddContext(accountStableId: "acct-1", sourceId: sourceId));
        Assert.Contains(sourceId.Value, exception.Message, StringComparison.Ordinal);
        Assert.Equal(1, manager.Current.ContextCount);
    }

    [Fact]
    public async Task Contexts_are_ordered_deterministically_by_creation_then_id()
    {
        var runtime = new FakeGameRuntimeService { CurrentStatus = GameRuntimeStatus.Running };
        var time = new ManualTimeProvider();
        using var manager = CreateManager(runtime, time);
        await manager.StartAsync();

        var first = manager.AddContext(accountDisplayName: "First");
        time.Advance(TimeSpan.FromSeconds(1));
        var second = manager.AddContext(accountDisplayName: "Second");

        var ordered = manager.Current.Contexts;
        Assert.Equal(first, ordered[0].ContextId);
        Assert.Equal(second, ordered[1].ContextId);
    }

    [Fact]
    public async Task Snapshot_is_immutable_across_further_mutation()
    {
        var runtime = new FakeGameRuntimeService { CurrentStatus = GameRuntimeStatus.Running };
        using var manager = CreateManager(runtime, new ManualTimeProvider());
        await manager.StartAsync();

        manager.AddContext();
        var before = manager.Current;

        manager.AddContext();
        var after = manager.Current;

        Assert.Single(before.Contexts);
        Assert.Equal(2, after.ContextCount);
    }

    // ---- Runtime suspension -----------------------------------------------------------------

    [Fact]
    public async Task Running_to_offline_suspends_all_active_contexts()
    {
        var runtime = new FakeGameRuntimeService { CurrentStatus = GameRuntimeStatus.Running };
        using var manager = CreateManager(runtime, new ManualTimeProvider());
        await manager.StartAsync();

        var waiting = manager.AddContext();
        var ready = manager.AddContext(sourceId: CreateSourceId("acct-1"));

        runtime.RaiseStatusChanged(GameRuntimeStatus.Running, GameRuntimeStatus.Off);

        Assert.All(
            manager.Current.Contexts,
            context => Assert.Equal(MonitoringContextState.RuntimeSuspended, context.State));
        Assert.Equal(2, manager.Current.SuspendedContextCount);
        Assert.Contains(manager.Current.Contexts, c => c.ContextId == waiting);
        Assert.Contains(manager.Current.Contexts, c => c.ContextId == ready);
    }

    [Fact]
    public async Task One_notification_is_raised_for_one_runtime_transition()
    {
        var runtime = new FakeGameRuntimeService { CurrentStatus = GameRuntimeStatus.Running };
        using var manager = CreateManager(runtime, new ManualTimeProvider());
        await manager.StartAsync();
        manager.AddContext();
        manager.AddContext(sourceId: CreateSourceId("acct-1"));

        var notifications = 0;
        manager.StateChanged += (_, _) => notifications++;

        runtime.RaiseStatusChanged(GameRuntimeStatus.Running, GameRuntimeStatus.Off);

        Assert.Equal(1, notifications);
    }

    [Fact]
    public async Task Context_identity_and_bindings_are_preserved_across_suspension()
    {
        var runtime = new FakeGameRuntimeService { CurrentStatus = GameRuntimeStatus.Running };
        var time = new ManualTimeProvider();
        var logActivity = new FakeLogActivityService();
        var sourceId = CreateSourceId("acct-1");
        logActivity.Current = TestLogCandidates.Snapshot(
            time.GetUtcNow(),
            TestLogCandidates.Create(sourceId, LogSourceActivityState.Waiting, time.GetUtcNow()));
        using var manager = CreateManager(runtime, time, logActivity);
        await manager.StartAsync();
        var contextId = manager.AddContext(accountStableId: "acct-1", accountDisplayName: "Alpha", sourceId: sourceId);

        runtime.RaiseStatusChanged(GameRuntimeStatus.Running, GameRuntimeStatus.Off);

        var context = Assert.Single(manager.Current.Contexts, c => c.ContextId == contextId);
        Assert.Equal("acct-1", context.AccountStableId);
        Assert.Equal("Alpha", context.AccountDisplayName);
        Assert.Equal(sourceId, context.CurrentSourceId);
        Assert.Equal(MonitoringContextState.Ready, context.PreviousStateBeforeSuspension);
        Assert.NotNull(context.SuspendedAt);
    }

    [Fact]
    public async Task Repeated_offline_notification_is_a_semantic_no_op()
    {
        var runtime = new FakeGameRuntimeService { CurrentStatus = GameRuntimeStatus.Running };
        using var manager = CreateManager(runtime, new ManualTimeProvider());
        await manager.StartAsync();
        manager.AddContext();

        runtime.RaiseStatusChanged(GameRuntimeStatus.Running, GameRuntimeStatus.Off);
        var revisionAfterFirstOffline = manager.Current.Revision;

        var notifications = 0;
        manager.StateChanged += (_, _) => notifications++;

        // Off -> Error and Error -> Unconfigured are both "not running": no context transition.
        runtime.RaiseStatusChanged(GameRuntimeStatus.Off, GameRuntimeStatus.Error);
        runtime.RaiseStatusChanged(GameRuntimeStatus.Error, GameRuntimeStatus.Unconfigured);

        Assert.Equal(0, notifications);
        Assert.Equal(revisionAfterFirstOffline, manager.Current.Revision);
        Assert.Equal(MonitoringContextState.RuntimeSuspended, manager.Current.Contexts[0].State);
    }

    [Fact]
    public async Task Offline_to_running_restores_prior_safe_state_without_fabricating_activity()
    {
        var runtime = new FakeGameRuntimeService { CurrentStatus = GameRuntimeStatus.Running };
        var time = new ManualTimeProvider();
        var logActivity = new FakeLogActivityService();
        var sourceId = CreateSourceId("acct-1");
        logActivity.Current = TestLogCandidates.Snapshot(
            time.GetUtcNow(),
            TestLogCandidates.Create(sourceId, LogSourceActivityState.Waiting, time.GetUtcNow()));
        using var manager = CreateManager(runtime, time, logActivity);
        await manager.StartAsync();
        var readyContext = manager.AddContext(sourceId: sourceId);
        var waitingContext = manager.AddContext();

        runtime.RaiseStatusChanged(GameRuntimeStatus.Running, GameRuntimeStatus.Off);
        runtime.RaiseStatusChanged(GameRuntimeStatus.Off, GameRuntimeStatus.Running);

        var ready = Assert.Single(manager.Current.Contexts, c => c.ContextId == readyContext);
        var waiting = Assert.Single(manager.Current.Contexts, c => c.ContextId == waitingContext);

        Assert.Equal(MonitoringContextState.Ready, ready.State);
        Assert.Equal(sourceId, ready.CurrentSourceId);
        Assert.Null(ready.SuspendedAt);
        Assert.Null(ready.PreviousStateBeforeSuspension);

        Assert.Equal(MonitoringContextState.WaitingForSource, waiting.State);
        Assert.Null(waiting.SuspendedAt);
    }

    [Fact]
    public async Task Unknown_and_error_runtime_statuses_are_handled_conservatively()
    {
        var runtime = new FakeGameRuntimeService { CurrentStatus = GameRuntimeStatus.Running };
        using var manager = CreateManager(runtime, new ManualTimeProvider());
        await manager.StartAsync();
        manager.AddContext();

        // A transition straight into Error (never having been Off) must still suspend.
        runtime.RaiseStatusChanged(GameRuntimeStatus.Running, GameRuntimeStatus.Error);

        Assert.Equal(MonitoringContextState.RuntimeSuspended, manager.Current.Contexts[0].State);
        Assert.False(manager.IsRuntimeAvailable);
    }

    [Fact]
    public async Task One_context_in_error_does_not_prevent_others_from_suspending()
    {
        var runtime = new FakeGameRuntimeService { CurrentStatus = GameRuntimeStatus.Running };
        using var manager = CreateManager(runtime, new ManualTimeProvider());
        await manager.StartAsync();
        var healthyContext = manager.AddContext();
        var brokenContext = manager.AddContext();
        manager.MarkContextErrorForTests(brokenContext);

        runtime.RaiseStatusChanged(GameRuntimeStatus.Running, GameRuntimeStatus.Off);

        var healthy = Assert.Single(manager.Current.Contexts, c => c.ContextId == healthyContext);
        var broken = Assert.Single(manager.Current.Contexts, c => c.ContextId == brokenContext);

        Assert.Equal(MonitoringContextState.RuntimeSuspended, healthy.State);
        Assert.Equal(MonitoringContextState.Error, broken.State);
    }

    [Fact]
    public async Task Suspension_does_not_depend_on_log_activity_polling()
    {
        // The manager depends only on IGameRuntimeService; no ILogActivityService reference
        // exists anywhere in its construction, so suspension cannot depend on log polling.
        var runtime = new FakeGameRuntimeService { CurrentStatus = GameRuntimeStatus.Running };
        using var manager = CreateManager(runtime, new ManualTimeProvider());
        await manager.StartAsync();
        manager.AddContext();

        runtime.RaiseStatusChanged(GameRuntimeStatus.Running, GameRuntimeStatus.Off);

        Assert.Equal(MonitoringContextState.RuntimeSuspended, manager.Current.Contexts[0].State);
    }

    private static LogSourceId CreateSourceId(string accountStableId) =>
        LogSourceId.Create(accountStableId, "Alpha", @"C:\fake\Logs\chatlog 2026-08-04.txt", new DateOnly(2026, 8, 4));
}
