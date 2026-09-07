using CoHAnalytics.Models;
using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Services;

public sealed class GameplaySessionContextResolverTests
{
  [Fact]
    public void Single_active_session_selects_that_context()
    {
        var contextId = MonitoringContextId.CreateNew();
        var recordId = CharacterRecordId.CreateNew();
        var startedAt = DateTimeOffset.UtcNow;
        var identity = new FakeIdentityReadService
        {
            Current = Snapshot(
                TestGameplaySessionContextSupport.CreateContext(
                    contextId,
                    "acct-1",
                    recordId,
                    "Hero A",
                    startedAt))
        };

        var selection = GameplaySessionContextResolver.Resolve(
            identity.Current,
            TestGameplaySessionContextSupport.FollowingLive());

        Assert.True(selection.IsResolved);
        Assert.Equal(contextId, selection.Context!.ContextId);
        Assert.Equal(GameplaySessionContextSelectionReason.SoleActiveSession, selection.Reason);
    }

    [Fact]
    public void Multiple_active_sessions_select_live_follow_target()
    {
        var contextA = MonitoringContextId.CreateNew();
        var contextB = MonitoringContextId.CreateNew();
        var recordA = CharacterRecordId.CreateNew();
        var recordB = CharacterRecordId.CreateNew();
        var startedAt = DateTimeOffset.UtcNow;
        var identity = new FakeIdentityReadService
        {
            Current = Snapshot(
                TestGameplaySessionContextSupport.CreateContext(contextA, "acct-1", recordA, "Hero A", startedAt),
                TestGameplaySessionContextSupport.CreateContext(contextB, "acct-2", recordB, "Hero B", startedAt))
        };

        var selection = GameplaySessionContextResolver.Resolve(
            identity.Current,
            TestGameplaySessionContextSupport.FollowingLive(contextB));

        Assert.True(selection.IsResolved);
        Assert.Equal(contextB, selection.Context!.ContextId);
        Assert.Equal(GameplaySessionContextSelectionReason.LiveFollowContext, selection.Reason);
    }

    [Fact]
    public void Multiple_active_sessions_select_pinned_viewed_context()
    {
        var contextA = MonitoringContextId.CreateNew();
        var contextB = MonitoringContextId.CreateNew();
        var recordA = CharacterRecordId.CreateNew();
        var recordB = CharacterRecordId.CreateNew();
        var startedAt = DateTimeOffset.UtcNow;
        var identity = new FakeIdentityReadService
        {
            Current = Snapshot(
                TestGameplaySessionContextSupport.CreateContext(contextA, "acct-1", recordA, "Hero A", startedAt),
                TestGameplaySessionContextSupport.CreateContext(contextB, "acct-2", recordB, "Hero B", startedAt))
        };

        var selection = GameplaySessionContextResolver.Resolve(
            identity.Current,
            TestGameplaySessionContextSupport.PinnedCharacter("acct-1", recordA, contextB));

        Assert.True(selection.IsResolved);
        Assert.Equal(contextA, selection.Context!.ContextId);
        Assert.Equal(GameplaySessionContextSelectionReason.PinnedViewedContext, selection.Reason);
    }

    [Fact]
    public void Multiple_active_sessions_without_follow_or_pin_remain_unresolved()
    {
        var contextA = MonitoringContextId.CreateNew();
        var contextB = MonitoringContextId.CreateNew();
        var recordA = CharacterRecordId.CreateNew();
        var recordB = CharacterRecordId.CreateNew();
        var startedAt = DateTimeOffset.UtcNow;
        var identity = new FakeIdentityReadService
        {
            Current = Snapshot(
                TestGameplaySessionContextSupport.CreateContext(contextA, "acct-1", recordA, "Hero A", startedAt),
                TestGameplaySessionContextSupport.CreateContext(contextB, "acct-2", recordB, "Hero B", startedAt))
        };

        var selection = GameplaySessionContextResolver.Resolve(
            identity.Current,
            TestGameplaySessionContextSupport.FollowingLive());

        Assert.False(selection.IsResolved);
        Assert.Equal(GameplaySessionContextSelectionReason.Unresolved, selection.Reason);
    }

    [Fact]
    public void Live_follow_target_remains_stable_across_snapshot_refresh()
    {
        var contextA = MonitoringContextId.CreateNew();
        var contextB = MonitoringContextId.CreateNew();
        var recordA = CharacterRecordId.CreateNew();
        var recordB = CharacterRecordId.CreateNew();
        var startedAt = DateTimeOffset.UtcNow;
        var identity = new FakeIdentityReadService
        {
            Current = Snapshot(
                [
                    TestGameplaySessionContextSupport.CreateContext(contextA, "acct-1", recordA, "Hero A", startedAt),
                    TestGameplaySessionContextSupport.CreateContext(contextB, "acct-2", recordB, "Hero B", startedAt)
                ],
                revision: 1)
        };

        var viewed = TestGameplaySessionContextSupport.FollowingLive(contextB);
        var first = GameplaySessionContextResolver.Resolve(identity.Current, viewed);
        identity.Current = Snapshot(
            [
                TestGameplaySessionContextSupport.CreateContext(contextA, "acct-1", recordA, "Hero A", startedAt, experience: 10),
                TestGameplaySessionContextSupport.CreateContext(contextB, "acct-2", recordB, "Hero B", startedAt, experience: 20)
            ],
            revision: 2);
        var second = GameplaySessionContextResolver.Resolve(identity.Current, viewed);

        Assert.Equal(contextB, first.Context!.ContextId);
        Assert.Equal(contextB, second.Context!.ContextId);
        Assert.Equal(20, second.Context.SessionExperienceGained);
    }

    [Fact]
    public void Follow_target_disappearance_falls_back_to_sole_remaining_session()
    {
        var contextA = MonitoringContextId.CreateNew();
        var contextB = MonitoringContextId.CreateNew();
        var recordA = CharacterRecordId.CreateNew();
        var recordB = CharacterRecordId.CreateNew();
        var startedAt = DateTimeOffset.UtcNow;
        var identity = new FakeIdentityReadService
        {
            Current = Snapshot(
                TestGameplaySessionContextSupport.CreateContext(contextA, "acct-1", recordA, "Hero A", startedAt),
                TestGameplaySessionContextSupport.CreateContext(contextB, "acct-2", recordB, "Hero B", startedAt))
        };

        var viewed = TestGameplaySessionContextSupport.FollowingLive(contextB);
        identity.Current = Snapshot(
            TestGameplaySessionContextSupport.CreateContext(contextA, "acct-1", recordA, "Hero A", startedAt));
        var selection = GameplaySessionContextResolver.Resolve(identity.Current, viewed);

        Assert.True(selection.IsResolved);
        Assert.Equal(contextA, selection.Context!.ContextId);
        Assert.Equal(GameplaySessionContextSelectionReason.SoleActiveSession, selection.Reason);
    }

    [Fact]
    public void Pinned_character_without_active_session_remains_unresolved()
    {
        var contextA = MonitoringContextId.CreateNew();
        var contextB = MonitoringContextId.CreateNew();
        var recordA = CharacterRecordId.CreateNew();
        var recordB = CharacterRecordId.CreateNew();
        var browseRecordId = CharacterRecordId.CreateNew();
        var startedAt = DateTimeOffset.UtcNow;
        var identity = new FakeIdentityReadService
        {
            Current = Snapshot(
                TestGameplaySessionContextSupport.CreateContext(contextA, "acct-1", recordA, "Hero A", startedAt),
                TestGameplaySessionContextSupport.CreateContext(contextB, "acct-2", recordB, "Hero B", startedAt))
        };

        var selection = GameplaySessionContextResolver.Resolve(
            identity.Current,
            TestGameplaySessionContextSupport.PinnedCharacter("acct-1", browseRecordId, contextB));

        Assert.False(selection.IsResolved);
        Assert.Equal(GameplaySessionContextSelectionReason.Unresolved, selection.Reason);
    }

    [Fact]
    public void ResolveForLiveMonitoring_includes_ready_monitoring_before_session_starts()
    {
        var contextId = MonitoringContextId.CreateNew();
        var identity = new FakeIdentityReadService
        {
            Current = Snapshot(
                new LiveMonitoringContextIdentityReadModel
                {
                    ContextId = contextId,
                    ContextState = MonitoringContextState.Ready,
                    AccountStableId = "testaccount",
                    AccountDisplayName = "TestAccount",
                    CharacterIdentityConfidence = CharacterIdentityConfidence.Unknown,
                    CharacterIdentityResolutionState = CharacterIdentityResolutionState.Unresolved,
                    SessionLifecycleState = GameplaySessionLifecycleState.Finalized,
                    HasActiveSession = false,
                    NeedsAttention = false,
                    CandidateCount = 0,
                    IdentityStatusLabel = "Unknown",
                    IdentityDetail = "Waiting for gameplay session."
                })
        };

        var selection = GameplaySessionContextResolver.ResolveForLiveMonitoring(
            identity.Current,
            TestGameplaySessionContextSupport.FollowingLive());

        Assert.True(selection.IsResolved);
        Assert.Equal("TestAccount", selection.Context!.AccountDisplayName);
        Assert.Equal(GameplaySessionContextSelectionReason.SoleActiveSession, selection.Reason);
    }

    [Fact]
    public void ResolveForLiveMonitoring_ignores_pinned_viewed_character()
    {
        var contextId = MonitoringContextId.CreateNew();
        var liveRecordId = CharacterRecordId.CreateNew();
        var browseRecordId = CharacterRecordId.CreateNew();
        var startedAt = DateTimeOffset.UtcNow;
        var identity = new FakeIdentityReadService
        {
            Current = Snapshot(
                TestGameplaySessionContextSupport.CreateContext(
                    contextId,
                    "testaccount",
                    liveRecordId,
                    "Dawn's Vanguard",
                    startedAt))
        };

        var selection = GameplaySessionContextResolver.ResolveForLiveMonitoring(
            identity.Current,
            TestGameplaySessionContextSupport.PinnedCharacter("other-account", browseRecordId));

        Assert.True(selection.IsResolved);
        Assert.Equal(liveRecordId, selection.Context!.CharacterRecordId);
        Assert.Equal("Dawn's Vanguard", selection.Context.CharacterDisplayName);
    }

    private static GameplaySessionIdentityReadModelSnapshot Snapshot(
        params LiveMonitoringContextIdentityReadModel[] contexts) =>
        Snapshot(contexts, revision: 1);

    private static GameplaySessionIdentityReadModelSnapshot Snapshot(
        LiveMonitoringContextIdentityReadModel[] contexts,
        long revision) =>
        GameplaySessionIdentityReadModelSnapshot.Create(
            contexts,
            DateTimeOffset.UtcNow,
            revision);

    private sealed class FakeIdentityReadService : IGameplaySessionIdentityReadService
    {
        public GameplaySessionIdentityReadModelSnapshot Current { get; set; } =
            GameplaySessionIdentityReadModelSnapshot.Empty;

        public event EventHandler<GameplaySessionIdentityReadModelChangedEventArgs>? Changed;

        public void RaiseChanged() =>
            Changed?.Invoke(
                this,
                new GameplaySessionIdentityReadModelChangedEventArgs { Snapshot = Current });
    }
}
