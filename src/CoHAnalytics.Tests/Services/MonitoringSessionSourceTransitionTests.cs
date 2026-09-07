using CoHAnalytics.Models;
using CoHAnalytics.Services;
using CoHAnalytics.Tests.Orchestration;

namespace CoHAnalytics.Tests.Services;

public sealed class MonitoringSessionSourceTransitionTests
{
    [Fact]
    public async Task Binding_generation_and_transition_kind_participate_in_snapshot_semantics()
    {
        var (manager, _, _, _, contextId, _) = await SeedClaimedContext();
        var context = Context(manager, contextId);

        Assert.NotEqual(
            context,
            context with { SourceBindingGeneration = context.SourceBindingGeneration + 1 });
        Assert.NotEqual(
            context,
            context with { LastSourceBindingTransitionKind = MonitoringSourceTransitionKind.SourceReleased });
    }

    [Fact]
    public async Task Initial_assignment_release_and_same_source_reclaim_have_distinct_generations()
    {
        var (manager, _, logActivity, time) = CreateManager();
        var source = TestLogCandidates.SourceId();
        logActivity.Current = TestLogCandidates.Snapshot(
            time.GetUtcNow(),
            TestLogCandidates.Create(source, LogSourceActivityState.Waiting, time.GetUtcNow()));
        await manager.StartAsync();
        var contextId = manager.AddContext();

        Assert.True(manager.ClaimSource(contextId, source).IsSuccess);
        var assigned = Context(manager, contextId);
        Assert.Equal(1, assigned.SourceBindingGeneration);
        Assert.Equal(MonitoringSourceTransitionKind.SourceAssigned, assigned.LastSourceBindingTransitionKind);

        Assert.True(manager.ReleaseSource(contextId).IsSuccess);
        var released = Context(manager, contextId);
        Assert.Equal(2, released.SourceBindingGeneration);
        Assert.Equal(MonitoringSourceTransitionKind.SourceReleased, released.LastSourceBindingTransitionKind);
        Assert.Null(released.CurrentSourceId);

        Assert.True(manager.ClaimSource(contextId, source).IsSuccess);
        var reclaimed = Context(manager, contextId);
        Assert.Equal(3, reclaimed.SourceBindingGeneration);
        Assert.Equal(MonitoringSourceTransitionKind.SourceReclaimed, reclaimed.LastSourceBindingTransitionKind);
        Assert.Equal(source, reclaimed.CurrentSourceId);
    }

    [Fact]
    public async Task Newly_observed_truncation_approves_exactly_one_same_source_reset()
    {
        var (manager, _, logActivity, time, contextId, source) = await SeedClaimedContext();
        time.Advance(TimeSpan.FromSeconds(1));
        var truncated = TestLogCandidates.Create(
            source,
            LogSourceActivityState.Truncated,
            time.GetUtcNow(),
            lastChangeKind: LogSourceChangeKind.Truncated,
            isTruncated: true);
        var snapshot = TestLogCandidates.Snapshot(2, time.GetUtcNow(), truncated);
        logActivity.Current = snapshot;

        logActivity.RaiseActivityChanged();

        var approved = Context(manager, contextId);
        Assert.Equal(source, approved.CurrentSourceId);
        Assert.Null(approved.PreviousSourceId);
        Assert.Equal(2, approved.SourceBindingGeneration);
        Assert.Equal(MonitoringSourceTransitionKind.TruncationReset, approved.LastSourceBindingTransitionKind);
        Assert.Equal(MonitoringContextChangeReason.TruncationReset, approved.LastSourceTransitionReason);
        Assert.Single(manager.Current.Contexts);
        Assert.Empty(manager.Current.PendingOffers);
        var managerRevision = manager.Current.Revision;

        logActivity.RaiseActivityChanged();
        logActivity.RaiseActivityChanged();

        Assert.Equal(2, Context(manager, contextId).SourceBindingGeneration);
        Assert.Equal(managerRevision, manager.Current.Revision);
    }

    [Fact]
    public async Task New_truncation_with_same_manager_timestamp_still_publishes_a_new_generation()
    {
        var (manager, _, logActivity, time, contextId, source) = await SeedClaimedContext();
        var managerNow = time.GetUtcNow();
        var first = TestLogCandidates.Create(
            source,
            LogSourceActivityState.Truncated,
            managerNow.AddSeconds(1),
            lastChangeKind: LogSourceChangeKind.Truncated,
            isTruncated: true);
        logActivity.Current = TestLogCandidates.Snapshot(2, managerNow.AddSeconds(1), first);
        logActivity.RaiseActivityChanged();
        var revisionAfterFirst = manager.Current.Revision;

        var second = TestLogCandidates.Create(
            source,
            LogSourceActivityState.Truncated,
            managerNow.AddSeconds(2),
            lastChangeKind: LogSourceChangeKind.Truncated,
            isTruncated: true);
        logActivity.Current = TestLogCandidates.Snapshot(3, managerNow.AddSeconds(2), second);
        logActivity.RaiseActivityChanged();

        var context = Context(manager, contextId);
        Assert.Equal(3, context.SourceBindingGeneration);
        Assert.Equal(MonitoringSourceTransitionKind.TruncationReset, context.LastSourceBindingTransitionKind);
        Assert.True(manager.Current.Revision > revisionAfterFirst);
    }

    [Fact]
    public async Task Verified_same_path_successor_replaces_source_in_the_same_context()
    {
        var (manager, _, logActivity, time, contextId, oldSource) = await SeedClaimedContext();
        var replacement = oldSource.NextGeneration();
        time.Advance(TimeSpan.FromSeconds(1));
        logActivity.Current = TestLogCandidates.Snapshot(
            2,
            time.GetUtcNow(),
            ReplacementCandidate(replacement, time.GetUtcNow()));

        logActivity.RaiseActivityChanged();

        var context = Context(manager, contextId);
        Assert.Equal(contextId, context.ContextId);
        Assert.Equal(replacement, context.CurrentSourceId);
        Assert.Equal(oldSource, context.PreviousSourceId);
        Assert.Equal(2, context.SourceBindingGeneration);
        Assert.Equal(MonitoringSourceTransitionKind.SourceReplaced, context.LastSourceBindingTransitionKind);
        Assert.Equal(MonitoringContextChangeReason.SourceReplaced, context.LastSourceTransitionReason);
        Assert.Null(context.SourceLostAt);
        Assert.Single(manager.Current.Contexts);
        Assert.Empty(manager.Current.PendingOffers);

        logActivity.RaiseActivityChanged();
        Assert.Equal(2, Context(manager, contextId).SourceBindingGeneration);
    }

    [Fact]
    public async Task Replacement_with_different_account_is_rejected()
    {
        var (manager, _, logActivity, time, contextId, oldSource) = await SeedClaimedContext();
        var replacement = LogSourceId.Create(
            "acct-2",
            "Beta",
            oldSource.FilePath,
            oldSource.LogDate,
            identityGeneration: 1);

        RaiseReplacement(logActivity, time, replacement);

        AssertRejectedReplacement(manager, contextId, oldSource);
        Assert.Contains(manager.GetDiagnostics().RecentDecisions, message => message.Contains("account mismatch", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Replacement_at_different_path_is_not_correlated()
    {
        var (manager, _, logActivity, time, contextId, oldSource) = await SeedClaimedContext();
        var replacement = TestLogCandidates.SourceId(
            oldSource.AccountStableId,
            oldSource.AccountDisplayName,
            oldSource.LogDate,
            identityGeneration: 1,
            fileNameSuffix: "-other");

        RaiseReplacement(logActivity, time, replacement);

        AssertRejectedReplacement(manager, contextId, oldSource);
    }

    [Fact]
    public async Task Replacement_with_skipped_identity_generation_is_rejected()
    {
        var (manager, _, logActivity, time, contextId, oldSource) = await SeedClaimedContext();
        var replacement = LogSourceId.Create(
            oldSource.AccountStableId,
            oldSource.AccountDisplayName,
            oldSource.FilePath,
            oldSource.LogDate,
            identityGeneration: 2);

        RaiseReplacement(logActivity, time, replacement);

        AssertRejectedReplacement(manager, contextId, oldSource);
        Assert.Contains(manager.GetDiagnostics().RecentDecisions, message => message.Contains("expected identity generation 1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Replacement_without_evidence_is_rejected()
    {
        var (manager, _, logActivity, time, contextId, oldSource) = await SeedClaimedContext();
        var replacement = oldSource.NextGeneration();
        time.Advance(TimeSpan.FromSeconds(1));
        logActivity.Current = TestLogCandidates.Snapshot(
            2,
            time.GetUtcNow(),
            TestLogCandidates.Create(
                replacement,
                LogSourceActivityState.Replaced,
                time.GetUtcNow(),
                lastChangeKind: LogSourceChangeKind.Replaced,
                isReplaced: true));
        logActivity.RaiseActivityChanged();

        AssertRejectedReplacement(manager, contextId, oldSource);
        Assert.Contains(manager.GetDiagnostics().RecentDecisions, message => message.Contains("evidence is missing", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Replacement_claimed_by_another_context_is_rejected()
    {
        var (manager, _, logActivity, time, contextId, oldSource) = await SeedClaimedContext();
        var replacement = oldSource.NextGeneration();
        var otherContextId = manager.AddContext(sourceId: replacement);

        RaiseReplacement(logActivity, time, replacement);

        AssertRejectedReplacement(manager, contextId, oldSource);
        Assert.Equal(replacement, Context(manager, otherContextId).CurrentSourceId);
        Assert.Contains(manager.GetDiagnostics().RecentDecisions, message => message.Contains("already claimed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Ambiguous_replacement_candidates_are_rejected()
    {
        var (manager, _, logActivity, time, contextId, oldSource) = await SeedClaimedContext();
        var replacement = oldSource.NextGeneration();
        time.Advance(TimeSpan.FromSeconds(1));
        var firstCandidate = ReplacementCandidate(replacement, time.GetUtcNow());
        var secondCandidate = firstCandidate with { ReplacementEvidence = "File identity changed." };
        logActivity.Current = TestLogCandidates.Snapshot(
            2,
            time.GetUtcNow(),
            firstCandidate,
            secondCandidate);
        logActivity.RaiseActivityChanged();

        AssertRejectedReplacement(manager, contextId, oldSource);
        Assert.Contains(manager.GetDiagnostics().RecentDecisions, message => message.Contains("ambiguous", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Duplicate_observations_of_same_replacement_are_rejected_without_throwing()
    {
        var (manager, _, logActivity, time, contextId, oldSource) = await SeedClaimedContext();
        var replacement = oldSource.NextGeneration();
        time.Advance(TimeSpan.FromSeconds(1));
        var candidate = ReplacementCandidate(replacement, time.GetUtcNow());
        logActivity.Current = TestLogCandidates.Snapshot(2, time.GetUtcNow(), candidate, candidate);

        logActivity.RaiseActivityChanged();

        AssertRejectedReplacement(manager, contextId, oldSource);
        Assert.Contains(manager.GetDiagnostics().RecentDecisions, message => message.Contains("ambiguous", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Persistent_source_loss_while_suspended_resumes_waiting_and_recovery_restores_ready()
    {
        var (manager, runtime, logActivity, time, contextId, source) = await SeedClaimedContext();
        var initialGeneration = Context(manager, contextId).SourceBindingGeneration;

        runtime.RaiseStatusChanged(GameRuntimeStatus.Running, GameRuntimeStatus.Off);
        Assert.Equal(initialGeneration, Context(manager, contextId).SourceBindingGeneration);

        time.Advance(TimeSpan.FromSeconds(1));
        logActivity.Current = TestLogCandidates.Snapshot(
            2,
            time.GetUtcNow(),
            TestLogCandidates.Create(source, LogSourceActivityState.Unavailable, time.GetUtcNow(), exists: false));
        logActivity.RaiseActivityChanged();

        var suspended = Context(manager, contextId);
        Assert.Equal(MonitoringContextState.RuntimeSuspended, suspended.State);
        Assert.Equal(MonitoringContextState.WaitingForSource, suspended.PreviousStateBeforeSuspension);
        Assert.Equal(initialGeneration, suspended.SourceBindingGeneration);

        runtime.RaiseStatusChanged(GameRuntimeStatus.Off, GameRuntimeStatus.Running, runningClientCount: 1);

        var waiting = Context(manager, contextId);
        Assert.Equal(MonitoringContextState.WaitingForSource, waiting.State);
        Assert.Equal(source, waiting.CurrentSourceId);
        Assert.NotNull(waiting.SourceLostAt);
        Assert.Equal(initialGeneration, waiting.SourceBindingGeneration);

        time.Advance(TimeSpan.FromSeconds(1));
        logActivity.Current = TestLogCandidates.Snapshot(
            3,
            time.GetUtcNow(),
            TestLogCandidates.Create(source, LogSourceActivityState.Growing, time.GetUtcNow()));
        logActivity.RaiseActivityChanged();

        var recovered = Context(manager, contextId);
        Assert.Equal(MonitoringContextState.Ready, recovered.State);
        Assert.Null(recovered.SourceLostAt);
        Assert.Equal(initialGeneration, recovered.SourceBindingGeneration);
    }

    [Fact]
    public async Task Bound_context_removal_versions_binding_once_and_unbound_removal_does_not()
    {
        var (manager, _, _, _, contextId, _) = await SeedClaimedContext();

        manager.RemoveContext(contextId);
        var stopped = Context(manager, contextId);
        Assert.Equal(2, stopped.SourceBindingGeneration);
        Assert.Equal(MonitoringSourceTransitionKind.ContextRemoved, stopped.LastSourceBindingTransitionKind);
        Assert.Null(stopped.CurrentSourceId);

        manager.RemoveContext(contextId);
        Assert.Equal(2, Context(manager, contextId).SourceBindingGeneration);

        var unboundId = manager.AddContext();
        Assert.Equal(0, Context(manager, unboundId).SourceBindingGeneration);
        manager.RemoveContext(unboundId);
        Assert.Equal(0, Context(manager, unboundId).SourceBindingGeneration);
        Assert.Equal(MonitoringSourceTransitionKind.None, Context(manager, unboundId).LastSourceBindingTransitionKind);
    }

    private static void RaiseReplacement(
        FakeLogActivityService logActivity,
        ManualTimeProvider time,
        LogSourceId replacement)
    {
        time.Advance(TimeSpan.FromSeconds(1));
        logActivity.Current = TestLogCandidates.Snapshot(
            2,
            time.GetUtcNow(),
            ReplacementCandidate(replacement, time.GetUtcNow()));
        logActivity.RaiseActivityChanged();
    }

    private static LogSourceCandidate ReplacementCandidate(LogSourceId source, DateTimeOffset observedAt) =>
        TestLogCandidates.Create(
            source,
            LogSourceActivityState.Replaced,
            observedAt,
            lastChangeKind: LogSourceChangeKind.Replaced,
            isReplaced: true,
            replacementEvidence: "Creation timestamp changed.");

    private static void AssertRejectedReplacement(
        MonitoringSessionManager manager,
        MonitoringContextId contextId,
        LogSourceId oldSource)
    {
        var context = Context(manager, contextId);
        Assert.Equal(oldSource, context.CurrentSourceId);
        Assert.Equal(1, context.SourceBindingGeneration);
        Assert.NotEqual(MonitoringSourceTransitionKind.SourceReplaced, context.LastSourceBindingTransitionKind);
    }

    private static MonitoringContextSnapshot Context(
        MonitoringSessionManager manager,
        MonitoringContextId contextId) =>
        Assert.Single(manager.Current.Contexts, context => context.ContextId == contextId);

    private static async Task<(
        MonitoringSessionManager Manager,
        FakeGameRuntimeService Runtime,
        FakeLogActivityService LogActivity,
        ManualTimeProvider Time,
        MonitoringContextId ContextId,
        LogSourceId Source)> SeedClaimedContext()
    {
        var (manager, runtime, logActivity, time) = CreateManager();
        var source = TestLogCandidates.SourceId();
        logActivity.Current = TestLogCandidates.Snapshot(
            1,
            time.GetUtcNow(),
            TestLogCandidates.Create(source, LogSourceActivityState.Growing, time.GetUtcNow()));
        await manager.StartAsync();
        var context = Assert.Single(manager.Current.Contexts);
        Assert.Equal(1, context.SourceBindingGeneration);
        Assert.Equal(MonitoringSourceTransitionKind.SourceAssigned, context.LastSourceBindingTransitionKind);
        return (manager, runtime, logActivity, time, context.ContextId, source);
    }

    private static (
        MonitoringSessionManager Manager,
        FakeGameRuntimeService Runtime,
        FakeLogActivityService LogActivity,
        ManualTimeProvider Time) CreateManager()
    {
        var runtime = new FakeGameRuntimeService { CurrentStatus = GameRuntimeStatus.Running };
        var logActivity = new FakeLogActivityService();
        var time = new ManualTimeProvider();
        var manager = new MonitoringSessionManager(
            runtime,
            logActivity,
            new MonitoringSessionManagerOptions { TimeProvider = time });
        return (manager, runtime, logActivity, time);
    }
}
