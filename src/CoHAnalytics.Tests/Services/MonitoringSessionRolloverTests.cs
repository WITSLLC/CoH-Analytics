using CoHAnalytics.Models;
using CoHAnalytics.Services;
using CoHAnalytics.Tests.Orchestration;

namespace CoHAnalytics.Tests.Services;

public sealed class MonitoringSessionRolloverTests
{
    [Fact]
    public async Task Startup_after_midnight_links_only_the_previous_day_source_for_same_runtime()
    {
        var observedAt = new DateTimeOffset(2026, 8, 19, 0, 44, 0, TimeSpan.Zero);
        var time = new ManualTimeProvider(observedAt);
        var process = FakeGameRuntimeService.CreateClient(
            7_476,
            new DateTimeOffset(2026, 8, 18, 19, 53, 56, TimeSpan.Zero));
        var runtime = new FakeGameRuntimeService
        {
            CurrentStatus = GameRuntimeStatus.Running,
            RunningClientCount = 1,
            RunningClients = [process]
        };
        var predecessor = TestLogCandidates.SourceId(
            "acct-1",
            "Alpha",
            new DateOnly(2026, 8, 18));
        var current = TestLogCandidates.SourceId(
            "acct-1",
            "Alpha",
            new DateOnly(2026, 8, 19));
        var logActivity = new FakeLogActivityService
        {
            Current = TestLogCandidates.Snapshot(
                observedAt,
                TestLogCandidates.Create(
                    predecessor,
                    LogSourceActivityState.Historical,
                    observedAt),
                TestLogCandidates.Create(
                    current,
                    LogSourceActivityState.Growing,
                    observedAt))
        };
        using var manager = new MonitoringSessionManager(
            runtime,
            logActivity,
            new MonitoringSessionManagerOptions { TimeProvider = time });

        await manager.StartAsync();

        var context = Assert.Single(manager.Current.Contexts);
        Assert.Equal(current, context.CurrentSourceId);
        Assert.Equal(predecessor, context.StartupRecoveryPredecessorSourceId);
        Assert.Equal(process, context.ProcessInstance);
    }

    private static (MonitoringSessionManager Manager, FakeGameRuntimeService Runtime, FakeLogActivityService LogActivity, ManualTimeProvider Time)
        CreateManager(GameRuntimeStatus runtimeStatus = GameRuntimeStatus.Running)
    {
        var runtime = new FakeGameRuntimeService { CurrentStatus = runtimeStatus };
        var logActivity = new FakeLogActivityService();
        var time = new ManualTimeProvider();
        var manager = new MonitoringSessionManager(runtime, logActivity, new MonitoringSessionManagerOptions { TimeProvider = time });
        return (manager, runtime, logActivity, time);
    }

    private static async Task<(MonitoringSessionManager Manager, FakeGameRuntimeService Runtime, FakeLogActivityService LogActivity, ManualTimeProvider Time, MonitoringContextId ContextId, LogSourceId OldSource)>
        SeedClaimedContext(GameRuntimeStatus runtimeStatus = GameRuntimeStatus.Running)
    {
        var (manager, runtime, logActivity, time) = CreateManager(runtimeStatus);
        var oldSource = TestLogCandidates.SourceId("acct-1", "Alpha", new DateOnly(2026, 8, 4));
        logActivity.Current = TestLogCandidates.Snapshot(time.GetUtcNow(), TestLogCandidates.Create(oldSource, LogSourceActivityState.Growing, time.GetUtcNow()));
        await manager.StartAsync();
        var context = Assert.Single(manager.Current.Contexts);
        return (manager, runtime, logActivity, time, context.ContextId, oldSource);
    }

    [Fact]
    public async Task Same_account_valid_predecessor_silently_switches_source()
    {
        var (manager, _, logActivity, time, contextId, oldSource) = await SeedClaimedContext();
        var newSource = TestLogCandidates.SourceId("acct-1", "Alpha", new DateOnly(2026, 8, 5));

        logActivity.Current = TestLogCandidates.Snapshot(
            time.GetUtcNow(),
            TestLogCandidates.Create(oldSource, LogSourceActivityState.Inactive, time.GetUtcNow()),
            TestLogCandidates.Create(newSource, LogSourceActivityState.Growing, time.GetUtcNow(), isRolloverCandidate: true, rolloverPredecessor: oldSource));
        logActivity.RaiseActivityChanged();

        var context = Assert.Single(manager.Current.Contexts, c => c.ContextId == contextId);
        Assert.Equal(newSource, context.CurrentSourceId);
        Assert.Equal(oldSource, context.PreviousSourceId);
        Assert.Equal(MonitoringContextState.Ready, context.State);
        Assert.Equal(MonitoringContextChangeReason.AutomaticRollover, context.LastSourceTransitionReason);
        Assert.Equal(2, context.SourceBindingGeneration);
        Assert.Equal(MonitoringSourceTransitionKind.AutomaticRollover, context.LastSourceBindingTransitionKind);
    }

    [Fact]
    public async Task Same_context_id_is_preserved_across_rollover()
    {
        var (manager, _, logActivity, time, contextId, oldSource) = await SeedClaimedContext();
        var newSource = TestLogCandidates.SourceId("acct-1", "Alpha", new DateOnly(2026, 8, 5));

        logActivity.Current = TestLogCandidates.Snapshot(
            time.GetUtcNow(),
            TestLogCandidates.Create(oldSource, LogSourceActivityState.Inactive, time.GetUtcNow()),
            TestLogCandidates.Create(newSource, LogSourceActivityState.Growing, time.GetUtcNow(), isRolloverCandidate: true, rolloverPredecessor: oldSource));
        logActivity.RaiseActivityChanged();

        Assert.Equal(1, manager.Current.ContextCount);
        Assert.Single(manager.Current.Contexts, c => c.ContextId == contextId);
    }

    [Fact]
    public async Task Old_source_is_released_and_becomes_eligible_for_another_claim()
    {
        var (manager, _, logActivity, time, contextId, oldSource) = await SeedClaimedContext();
        var newSource = TestLogCandidates.SourceId("acct-1", "Alpha", new DateOnly(2026, 8, 5));

        logActivity.Current = TestLogCandidates.Snapshot(
            time.GetUtcNow(),
            TestLogCandidates.Create(oldSource, LogSourceActivityState.Inactive, time.GetUtcNow()),
            TestLogCandidates.Create(newSource, LogSourceActivityState.Growing, time.GetUtcNow(), isRolloverCandidate: true, rolloverPredecessor: oldSource));
        logActivity.RaiseActivityChanged();

        var otherContext = manager.AddContext();
        var claimResult = manager.ClaimSource(otherContext, oldSource);

        Assert.True(claimResult.IsSuccess);
    }

    [Fact]
    public async Task No_offer_is_generated_for_the_rollover_target()
    {
        var (manager, _, logActivity, time, _, oldSource) = await SeedClaimedContext();
        var newSource = TestLogCandidates.SourceId("acct-1", "Alpha", new DateOnly(2026, 8, 5));

        logActivity.Current = TestLogCandidates.Snapshot(
            time.GetUtcNow(),
            TestLogCandidates.Create(oldSource, LogSourceActivityState.Inactive, time.GetUtcNow()),
            TestLogCandidates.Create(newSource, LogSourceActivityState.Growing, time.GetUtcNow(), isRolloverCandidate: true, rolloverPredecessor: oldSource));
        logActivity.RaiseActivityChanged();

        Assert.Empty(manager.Current.PendingOffers);
    }

    [Fact]
    public async Task No_new_context_is_created_by_rollover()
    {
        var (manager, _, logActivity, time, _, oldSource) = await SeedClaimedContext();
        var newSource = TestLogCandidates.SourceId("acct-1", "Alpha", new DateOnly(2026, 8, 5));

        logActivity.Current = TestLogCandidates.Snapshot(
            time.GetUtcNow(),
            TestLogCandidates.Create(oldSource, LogSourceActivityState.Inactive, time.GetUtcNow()),
            TestLogCandidates.Create(newSource, LogSourceActivityState.Growing, time.GetUtcNow(), isRolloverCandidate: true, rolloverPredecessor: oldSource));
        logActivity.RaiseActivityChanged();

        Assert.Equal(1, manager.Current.ContextCount);
    }

    [Fact]
    public async Task Runtime_suspended_rollover_preserves_suspension()
    {
        var (manager, runtime, logActivity, time, contextId, oldSource) = await SeedClaimedContext();
        runtime.RaiseStatusChanged(GameRuntimeStatus.Running, GameRuntimeStatus.Off);
        var newSource = TestLogCandidates.SourceId("acct-1", "Alpha", new DateOnly(2026, 8, 5));

        logActivity.Current = TestLogCandidates.Snapshot(
            time.GetUtcNow(),
            TestLogCandidates.Create(oldSource, LogSourceActivityState.Inactive, time.GetUtcNow()),
            TestLogCandidates.Create(newSource, LogSourceActivityState.Growing, time.GetUtcNow(), isRolloverCandidate: true, rolloverPredecessor: oldSource));
        logActivity.RaiseActivityChanged();

        var context = Assert.Single(manager.Current.Contexts, c => c.ContextId == contextId);
        Assert.Equal(MonitoringContextState.RuntimeSuspended, context.State);
        Assert.Equal(newSource, context.CurrentSourceId);
        Assert.Equal(MonitoringContextState.Ready, context.PreviousStateBeforeSuspension);
    }

    [Fact]
    public async Task Different_account_rollover_candidate_is_rejected()
    {
        var (manager, _, logActivity, time, contextId, oldSource) = await SeedClaimedContext();
        var wrongAccountSource = TestLogCandidates.SourceId("acct-9", "Someone Else", new DateOnly(2026, 8, 5));

        logActivity.Current = TestLogCandidates.Snapshot(
            time.GetUtcNow(),
            TestLogCandidates.Create(oldSource, LogSourceActivityState.Inactive, time.GetUtcNow()),
            TestLogCandidates.Create(wrongAccountSource, LogSourceActivityState.Growing, time.GetUtcNow(), isRolloverCandidate: true, rolloverPredecessor: oldSource));
        logActivity.RaiseActivityChanged();

        var context = Assert.Single(manager.Current.Contexts, c => c.ContextId == contextId);
        Assert.Equal(oldSource, context.CurrentSourceId);
    }

    [Fact]
    public async Task Ambiguous_predecessor_with_multiple_claimants_is_rejected()
    {
        var (manager, _, logActivity, time, contextId, oldSource) = await SeedClaimedContext();
        var candidateA = TestLogCandidates.SourceId("acct-1", "Alpha", new DateOnly(2026, 8, 5), fileNameSuffix: "-a");
        var candidateB = TestLogCandidates.SourceId("acct-1", "Alpha", new DateOnly(2026, 8, 5), fileNameSuffix: "-b");

        logActivity.Current = TestLogCandidates.Snapshot(
            time.GetUtcNow(),
            TestLogCandidates.Create(oldSource, LogSourceActivityState.Inactive, time.GetUtcNow()),
            TestLogCandidates.Create(candidateA, LogSourceActivityState.Growing, time.GetUtcNow(), isRolloverCandidate: true, rolloverPredecessor: oldSource),
            TestLogCandidates.Create(candidateB, LogSourceActivityState.Growing, time.GetUtcNow(), isRolloverCandidate: true, rolloverPredecessor: oldSource));
        logActivity.RaiseActivityChanged();

        var context = Assert.Single(manager.Current.Contexts, c => c.ContextId == contextId);
        Assert.Equal(oldSource, context.CurrentSourceId);
    }

    [Fact]
    public async Task Already_claimed_replacement_is_rejected()
    {
        var (manager, _, logActivity, time, contextId, oldSource) = await SeedClaimedContext();
        var newSource = TestLogCandidates.SourceId("acct-1", "Alpha", new DateOnly(2026, 8, 5));

        logActivity.Current = TestLogCandidates.Snapshot(
            time.GetUtcNow(),
            TestLogCandidates.Create(oldSource, LogSourceActivityState.Inactive, time.GetUtcNow()),
            TestLogCandidates.Create(newSource, LogSourceActivityState.Waiting, time.GetUtcNow(), isRolloverCandidate: true, rolloverPredecessor: oldSource));
        logActivity.RaiseActivityChanged();
        var otherContext = manager.AddContext();
        Assert.True(manager.ClaimSource(otherContext, newSource).IsSuccess);

        // Now the "new" source is claimed elsewhere before it ever grows; if it later grows,
        // rollover must not silently reassign it.
        logActivity.Current = TestLogCandidates.Snapshot(
            time.GetUtcNow(),
            TestLogCandidates.Create(oldSource, LogSourceActivityState.Inactive, time.GetUtcNow()),
            TestLogCandidates.Create(newSource, LogSourceActivityState.Growing, time.GetUtcNow(), isRolloverCandidate: true, rolloverPredecessor: oldSource));
        logActivity.RaiseActivityChanged();

        var context = Assert.Single(manager.Current.Contexts, c => c.ContextId == contextId);
        Assert.Equal(oldSource, context.CurrentSourceId);
        var otherCtx = Assert.Single(manager.Current.Contexts, c => c.ContextId == otherContext);
        Assert.Equal(newSource, otherCtx.CurrentSourceId);
    }

    [Fact]
    public async Task Active_old_source_prevents_rollover()
    {
        var (manager, _, logActivity, time, contextId, oldSource) = await SeedClaimedContext();
        var newSource = TestLogCandidates.SourceId("acct-1", "Alpha", new DateOnly(2026, 8, 5));

        logActivity.Current = TestLogCandidates.Snapshot(
            time.GetUtcNow(),
            TestLogCandidates.Create(oldSource, LogSourceActivityState.Growing, time.GetUtcNow()),
            TestLogCandidates.Create(newSource, LogSourceActivityState.Growing, time.GetUtcNow(), isRolloverCandidate: true, rolloverPredecessor: oldSource));
        logActivity.RaiseActivityChanged();

        var context = Assert.Single(manager.Current.Contexts, c => c.ContextId == contextId);
        Assert.Equal(oldSource, context.CurrentSourceId);
    }

    [Fact]
    public async Task Rollover_is_a_semantic_no_op_on_repeated_scan()
    {
        var (manager, _, logActivity, time, contextId, oldSource) = await SeedClaimedContext();
        var newSource = TestLogCandidates.SourceId("acct-1", "Alpha", new DateOnly(2026, 8, 5));

        var snapshot = TestLogCandidates.Snapshot(
            time.GetUtcNow(),
            TestLogCandidates.Create(oldSource, LogSourceActivityState.Inactive, time.GetUtcNow()),
            TestLogCandidates.Create(newSource, LogSourceActivityState.Growing, time.GetUtcNow(), isRolloverCandidate: true, rolloverPredecessor: oldSource));
        logActivity.Current = snapshot;
        logActivity.RaiseActivityChanged();
        var revisionAfterRollover = manager.Current.Revision;

        logActivity.RaiseActivityChanged();
        logActivity.RaiseActivityChanged();

        Assert.Equal(revisionAfterRollover, manager.Current.Revision);
        Assert.Equal(1, manager.Current.ContextCount);
        Assert.Equal(2, Assert.Single(manager.Current.Contexts).SourceBindingGeneration);
    }
}
