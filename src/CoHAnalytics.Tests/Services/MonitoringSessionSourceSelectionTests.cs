using CoHAnalytics.Models;
using CoHAnalytics.Services;
using CoHAnalytics.Tests.Orchestration;

namespace CoHAnalytics.Tests.Services;

public sealed class MonitoringSessionSourceSelectionTests
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

    // ---- Automatic first context -------------------------------------------------------------

    [Fact]
    public async Task Exactly_one_growing_candidate_auto_creates_context()
    {
        var (manager, _, logActivity, time) = CreateManager();
        var sourceId = TestLogCandidates.SourceId();
        var candidate = TestLogCandidates.Create(sourceId, LogSourceActivityState.Growing, time.GetUtcNow());
        logActivity.Current = TestLogCandidates.Snapshot(time.GetUtcNow(), candidate);

        await manager.StartAsync();

        var context = Assert.Single(manager.Current.Contexts);
        Assert.Equal(MonitoringContextState.Ready, context.State);
        Assert.Equal(sourceId, context.CurrentSourceId);
        Assert.Equal(MonitoringContextChangeReason.AutomaticUnambiguousSelection, context.LastSourceTransitionReason);
    }

    [Fact]
    public async Task Historical_only_candidate_does_not_create_context()
    {
        var (manager, _, logActivity, time) = CreateManager();
        var sourceId = TestLogCandidates.SourceId(logDate: new DateOnly(2026, 8, 1));
        var candidate = TestLogCandidates.Create(sourceId, LogSourceActivityState.Historical, time.GetUtcNow());
        logActivity.Current = TestLogCandidates.Snapshot(time.GetUtcNow(), candidate);

        await manager.StartAsync();

        Assert.Empty(manager.Current.Contexts);
    }

    [Fact]
    public async Task Waiting_candidate_does_not_create_context()
    {
        var (manager, _, logActivity, time) = CreateManager();
        var sourceId = TestLogCandidates.SourceId();
        var candidate = TestLogCandidates.Create(sourceId, LogSourceActivityState.Waiting, time.GetUtcNow());
        logActivity.Current = TestLogCandidates.Snapshot(time.GetUtcNow(), candidate);

        await manager.StartAsync();

        Assert.Empty(manager.Current.Contexts);
    }

    [Fact]
    public async Task Two_growing_candidates_in_distinct_accounts_are_both_monitored()
    {
        var (manager, _, logActivity, time) = CreateManager();
        var first = TestLogCandidates.Create(TestLogCandidates.SourceId("acct-1", "Alpha"), LogSourceActivityState.Growing, time.GetUtcNow());
        var second = TestLogCandidates.Create(TestLogCandidates.SourceId("acct-2", "Beta"), LogSourceActivityState.Growing, time.GetUtcNow());
        logActivity.Current = TestLogCandidates.Snapshot(time.GetUtcNow(), first, second);

        await manager.StartAsync();

        Assert.Equal(2, manager.Current.ContextCount);
        Assert.Empty(manager.Current.PendingOffers);
        Assert.All(
            manager.Current.Contexts,
            context => Assert.Equal(MonitoringContextChangeReason.AutomaticUnambiguousSelection, context.LastSourceTransitionReason));
        Assert.Equal(
            [first.SourceId, second.SourceId],
            manager.Current.Contexts.Select(context => context.CurrentSourceId).OrderBy(id => id!.Value, StringComparer.Ordinal));
    }

    [Fact]
    public async Task Two_growing_candidates_in_one_account_stay_ambiguous()
    {
        var (manager, _, logActivity, time) = CreateManager();
        var first = TestLogCandidates.Create(TestLogCandidates.SourceId("acct-1", "Alpha"), LogSourceActivityState.Growing, time.GetUtcNow());
        var second = TestLogCandidates.Create(
            TestLogCandidates.SourceId("acct-1", "Alpha", fileNameSuffix: "-second"),
            LogSourceActivityState.Growing,
            time.GetUtcNow());
        logActivity.Current = TestLogCandidates.Snapshot(time.GetUtcNow(), first, second);

        await manager.StartAsync();

        Assert.Empty(manager.Current.Contexts);
        Assert.Equal(2, manager.Current.PendingOfferCount);
        Assert.Equal(2, manager.Current.AmbiguousSourceCount);
    }

    [Fact]
    public async Task Runtime_offline_does_not_auto_create_first_context()
    {
        var (manager, _, logActivity, time) = CreateManager(GameRuntimeStatus.Off);
        var candidate = TestLogCandidates.Create(TestLogCandidates.SourceId(), LogSourceActivityState.Growing, time.GetUtcNow());
        logActivity.Current = TestLogCandidates.Snapshot(time.GetUtcNow(), candidate);

        await manager.StartAsync();

        Assert.Empty(manager.Current.Contexts);
    }

    [Fact]
    public async Task Repeated_identical_snapshot_does_not_duplicate_context()
    {
        var (manager, _, logActivity, time) = CreateManager();
        var candidate = TestLogCandidates.Create(TestLogCandidates.SourceId(), LogSourceActivityState.Growing, time.GetUtcNow());
        logActivity.Current = TestLogCandidates.Snapshot(time.GetUtcNow(), candidate);
        await manager.StartAsync();

        logActivity.RaiseActivityChanged();
        logActivity.RaiseActivityChanged();

        Assert.Single(manager.Current.Contexts);
    }

    // ---- Claims ------------------------------------------------------------------------------

    [Fact]
    public async Task ClaimSource_claims_unclaimed_source()
    {
        var (manager, _, logActivity, time) = CreateManager();
        var sourceId = TestLogCandidates.SourceId();
        var candidate = TestLogCandidates.Create(sourceId, LogSourceActivityState.Waiting, time.GetUtcNow());
        logActivity.Current = TestLogCandidates.Snapshot(time.GetUtcNow(), candidate);
        await manager.StartAsync();
        var contextId = manager.AddContext();

        var result = manager.ClaimSource(contextId, sourceId);

        Assert.True(result.IsSuccess);
        var context = Assert.Single(manager.Current.Contexts, c => c.ContextId == contextId);
        Assert.Equal(sourceId, context.CurrentSourceId);
        Assert.Equal(MonitoringContextState.Ready, context.State);
        Assert.Equal(MonitoringContextChangeReason.ManualClaim, context.LastSourceTransitionReason);
    }

    [Fact]
    public async Task ClaimSource_rejects_duplicate_claim()
    {
        var (manager, _, logActivity, time) = CreateManager();
        var sourceId = TestLogCandidates.SourceId();
        var candidate = TestLogCandidates.Create(sourceId, LogSourceActivityState.Waiting, time.GetUtcNow());
        logActivity.Current = TestLogCandidates.Snapshot(time.GetUtcNow(), candidate);
        await manager.StartAsync();
        var first = manager.AddContext();
        var second = manager.AddContext();
        Assert.True(manager.ClaimSource(first, sourceId).IsSuccess);

        var result = manager.ClaimSource(second, sourceId);

        Assert.Equal(MonitoringSourceClaimOutcome.SourceAlreadyClaimed, result.Outcome);
    }

    [Fact]
    public async Task Context_cannot_claim_second_source_without_release()
    {
        var (manager, _, logActivity, time) = CreateManager();
        var firstSource = TestLogCandidates.SourceId("acct-1", "Alpha");
        var secondSource = TestLogCandidates.SourceId("acct-1", "Alpha", fileNameSuffix: "-b");
        logActivity.Current = TestLogCandidates.Snapshot(
            time.GetUtcNow(),
            TestLogCandidates.Create(firstSource, LogSourceActivityState.Waiting, time.GetUtcNow()),
            TestLogCandidates.Create(secondSource, LogSourceActivityState.Waiting, time.GetUtcNow()));
        await manager.StartAsync();
        var contextId = manager.AddContext();
        Assert.True(manager.ClaimSource(contextId, firstSource).IsSuccess);

        var result = manager.ClaimSource(contextId, secondSource);

        Assert.Equal(MonitoringSourceClaimOutcome.ContextAlreadyHasSource, result.Outcome);
    }

    [Fact]
    public async Task ReleaseSource_makes_source_eligible_for_new_claim()
    {
        var (manager, _, logActivity, time) = CreateManager();
        var sourceId = TestLogCandidates.SourceId();
        logActivity.Current = TestLogCandidates.Snapshot(time.GetUtcNow(), TestLogCandidates.Create(sourceId, LogSourceActivityState.Waiting, time.GetUtcNow()));
        await manager.StartAsync();
        var first = manager.AddContext();
        var second = manager.AddContext();
        Assert.True(manager.ClaimSource(first, sourceId).IsSuccess);

        Assert.True(manager.ReleaseSource(first).IsSuccess);
        var claimResult = manager.ClaimSource(second, sourceId);

        Assert.True(claimResult.IsSuccess);
        var firstContext = Assert.Single(manager.Current.Contexts, c => c.ContextId == first);
        Assert.Null(firstContext.CurrentSourceId);
        Assert.Equal(MonitoringContextState.WaitingForSource, firstContext.State);
    }

    [Fact]
    public async Task Two_contexts_may_share_account_but_not_source()
    {
        var (manager, _, logActivity, time) = CreateManager();
        var firstSource = TestLogCandidates.SourceId("acct-1", "Alpha", fileNameSuffix: "-a");
        var secondSource = TestLogCandidates.SourceId("acct-1", "Alpha", fileNameSuffix: "-b");
        logActivity.Current = TestLogCandidates.Snapshot(
            time.GetUtcNow(),
            TestLogCandidates.Create(firstSource, LogSourceActivityState.Waiting, time.GetUtcNow()),
            TestLogCandidates.Create(secondSource, LogSourceActivityState.Waiting, time.GetUtcNow()));
        await manager.StartAsync();
        var first = manager.AddContext(accountStableId: "acct-1");
        var second = manager.AddContext(accountStableId: "acct-1");

        Assert.True(manager.ClaimSource(first, firstSource).IsSuccess);
        Assert.True(manager.ClaimSource(second, secondSource).IsSuccess);

        Assert.Equal(2, manager.Current.ClaimedSourceCount);
    }

    [Fact]
    public async Task ClaimSource_missing_source_returns_clear_result()
    {
        var (manager, _, _, _) = CreateManager();
        await manager.StartAsync();
        var contextId = manager.AddContext();

        var result = manager.ClaimSource(contextId, TestLogCandidates.SourceId());

        Assert.Equal(MonitoringSourceClaimOutcome.SourceNotFound, result.Outcome);
    }

    // ---- Automatic concurrent enrollment -------------------------------------------------------

    [Fact]
    public async Task Second_account_growing_later_is_monitored_without_user_enrollment()
    {
        var (manager, _, logActivity, time) = CreateManager();
        var firstSource = TestLogCandidates.SourceId("acct-1", "Alpha");
        logActivity.Current = TestLogCandidates.Snapshot(time.GetUtcNow(), TestLogCandidates.Create(firstSource, LogSourceActivityState.Growing, time.GetUtcNow()));
        await manager.StartAsync();
        var firstContext = Assert.Single(manager.Current.Contexts);

        var secondSource = TestLogCandidates.SourceId("acct-2", "Beta");
        logActivity.Current = TestLogCandidates.Snapshot(
            time.GetUtcNow(),
            TestLogCandidates.Create(firstSource, LogSourceActivityState.Growing, time.GetUtcNow()),
            TestLogCandidates.Create(secondSource, LogSourceActivityState.Growing, time.GetUtcNow()));
        logActivity.RaiseActivityChanged();

        Assert.Empty(manager.Current.PendingOffers);
        Assert.Equal(2, manager.Current.ContextCount);

        // The first context keeps its identity and source: enrolling another session never
        // disturbs an already-monitored one.
        var retained = Assert.Single(manager.Current.Contexts, c => c.ContextId == firstContext.ContextId);
        Assert.Equal(firstSource, retained.CurrentSourceId);

        var added = Assert.Single(manager.Current.Contexts, c => c.ContextId != firstContext.ContextId);
        Assert.Equal(secondSource, added.CurrentSourceId);
        Assert.Equal("acct-2", added.AccountStableId);
        Assert.Equal(MonitoringContextState.Ready, added.State);
        Assert.Equal(MonitoringContextChangeReason.AutomaticUnambiguousSelection, added.LastSourceTransitionReason);
    }

    [Fact]
    public async Task Repeated_scans_do_not_duplicate_automatic_contexts()
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
        logActivity.RaiseActivityChanged();
        logActivity.RaiseActivityChanged();

        Assert.Equal(2, manager.Current.ContextCount);
        Assert.Empty(manager.Current.PendingOffers);
    }

    // ---- Ambiguous-source offers ---------------------------------------------------------------

    [Fact]
    public async Task Second_growing_source_in_a_monitored_account_creates_one_offer()
    {
        var (manager, _, logActivity, time) = CreateManager();
        var firstSource = TestLogCandidates.SourceId("acct-1", "Alpha");
        logActivity.Current = TestLogCandidates.Snapshot(time.GetUtcNow(), TestLogCandidates.Create(firstSource, LogSourceActivityState.Growing, time.GetUtcNow()));
        await manager.StartAsync();
        Assert.Single(manager.Current.Contexts);

        var secondSource = TestLogCandidates.SourceId("acct-1", "Alpha", fileNameSuffix: "-second");
        logActivity.Current = TestLogCandidates.Snapshot(
            time.GetUtcNow(),
            TestLogCandidates.Create(firstSource, LogSourceActivityState.Growing, time.GetUtcNow()),
            TestLogCandidates.Create(secondSource, LogSourceActivityState.Growing, time.GetUtcNow()));
        logActivity.RaiseActivityChanged();

        Assert.Single(manager.Current.Contexts);
        var offer = Assert.Single(manager.Current.PendingOffers);
        Assert.Equal(secondSource, offer.SourceId);
        Assert.Equal(MonitoringSourceOfferReason.AmbiguousAccountSource, offer.Reason);
    }

    [Fact]
    public async Task Repeated_scans_do_not_duplicate_offer()
    {
        var (manager, _, logActivity, time) = SeedManagerWithOneContextAndOneOffer();
        await Task.CompletedTask;

        logActivity.RaiseActivityChanged();
        logActivity.RaiseActivityChanged();

        Assert.Single(manager.Current.PendingOffers);
    }

    [Fact]
    public async Task AcceptOffer_creates_another_context()
    {
        var (manager, _, logActivity, time) = SeedManagerWithOneContextAndOneOffer();
        await Task.CompletedTask;
        var offer = Assert.Single(manager.Current.PendingOffers);

        var decision = manager.AcceptOffer(offer.OfferId);

        Assert.True(decision.IsSuccess);
        Assert.NotNull(decision.ContextId);
        Assert.Equal(2, manager.Current.ContextCount);
        Assert.Empty(manager.Current.PendingOffers);
        var newContext = Assert.Single(manager.Current.Contexts, c => c.ContextId == decision.ContextId);
        Assert.Equal(offer.SourceId, newContext.CurrentSourceId);
        Assert.Equal(MonitoringContextState.Ready, newContext.State);
    }

    [Fact]
    public async Task DeclineOffer_leaves_source_unclaimed()
    {
        var (manager, _, logActivity, time) = SeedManagerWithOneContextAndOneOffer();
        await Task.CompletedTask;
        var offer = Assert.Single(manager.Current.PendingOffers);

        var decision = manager.DeclineOffer(offer.OfferId);

        Assert.True(decision.IsSuccess);
        Assert.Single(manager.Current.Contexts);
        Assert.Empty(manager.Current.PendingOffers);
        Assert.Equal(1, manager.Current.DeclinedSourceCount);
    }

    [Fact]
    public async Task Decline_suppression_prevents_immediate_reoffer()
    {
        var (manager, _, logActivity, time) = SeedManagerWithOneContextAndOneOffer();
        var offer = Assert.Single(manager.Current.PendingOffers);
        var declinedSourceId = offer.SourceId;
        manager.DeclineOffer(offer.OfferId);

        // Same growth episode continues; the source must not be re-offered.
        logActivity.RaiseActivityChanged();

        Assert.Empty(manager.Current.PendingOffers);
        Assert.Equal(1, manager.Current.DeclinedSourceCount);
    }

    [Fact]
    public async Task Later_distinct_activity_episode_reoffers()
    {
        var (manager, _, logActivity, time) = SeedManagerWithOneContextAndOneOffer();
        var offer = Assert.Single(manager.Current.PendingOffers);
        var declinedSourceId = offer.SourceId;
        var firstSourceId = manager.Current.Contexts[0].CurrentSourceId!;
        manager.DeclineOffer(offer.OfferId);

        var firstCandidate = TestLogCandidates.Create(firstSourceId, LogSourceActivityState.Growing, time.GetUtcNow());
        logActivity.Current = TestLogCandidates.Snapshot(
            time.GetUtcNow(),
            firstCandidate,
            TestLogCandidates.Create(declinedSourceId, LogSourceActivityState.Inactive, time.GetUtcNow()));
        logActivity.RaiseActivityChanged();
        Assert.Empty(manager.Current.PendingOffers);

        logActivity.Current = TestLogCandidates.Snapshot(
            time.GetUtcNow(),
            firstCandidate,
            TestLogCandidates.Create(declinedSourceId, LogSourceActivityState.Growing, time.GetUtcNow()));
        logActivity.RaiseActivityChanged();

        var newOffer = Assert.Single(manager.Current.PendingOffers);
        Assert.Equal(declinedSourceId, newOffer.SourceId);
        Assert.Equal(0, manager.Current.DeclinedSourceCount);
    }

    [Fact]
    public async Task ReconsiderSource_reoffers_manually()
    {
        var (manager, _, logActivity, time) = SeedManagerWithOneContextAndOneOffer();
        var offer = Assert.Single(manager.Current.PendingOffers);
        var declinedSourceId = offer.SourceId;
        manager.DeclineOffer(offer.OfferId);
        Assert.Empty(manager.Current.PendingOffers);

        var decision = manager.ReconsiderSource(declinedSourceId);

        Assert.True(decision.IsSuccess);
        var newOffer = Assert.Single(manager.Current.PendingOffers);
        Assert.Equal(declinedSourceId, newOffer.SourceId);
        Assert.Equal(0, manager.Current.DeclinedSourceCount);
    }

    [Fact]
    public async Task Offer_invalidated_when_source_disappears()
    {
        var (manager, _, logActivity, time) = SeedManagerWithOneContextAndOneOffer();
        var offer = Assert.Single(manager.Current.PendingOffers);
        var firstSourceId = manager.Current.Contexts[0].CurrentSourceId!;

        logActivity.Current = TestLogCandidates.Snapshot(
            time.GetUtcNow(),
            TestLogCandidates.Create(firstSourceId, LogSourceActivityState.Growing, time.GetUtcNow()),
            TestLogCandidates.Create(offer.SourceId, LogSourceActivityState.Unavailable, time.GetUtcNow(), exists: false));
        logActivity.RaiseActivityChanged();

        Assert.Empty(manager.Current.PendingOffers);
    }

    [Fact]
    public async Task Offer_invalidated_when_claimed_elsewhere()
    {
        var (manager, _, logActivity, time) = SeedManagerWithOneContextAndOneOffer();
        var offer = Assert.Single(manager.Current.PendingOffers);
        var contextId = manager.AddContext();

        var result = manager.ClaimSource(contextId, offer.SourceId);

        Assert.True(result.IsSuccess);
        Assert.Empty(manager.Current.PendingOffers);
    }

    // ---- Initial ambiguity --------------------------------------------------------------------

    [Fact]
    public async Task Multiple_growing_candidates_with_no_contexts_create_no_silent_selection()
    {
        var (manager, _, logActivity, time) = CreateManager();
        var first = TestLogCandidates.Create(TestLogCandidates.SourceId("acct-1", "Alpha"), LogSourceActivityState.Growing, time.GetUtcNow());
        var second = TestLogCandidates.Create(
            TestLogCandidates.SourceId("acct-1", "Alpha", fileNameSuffix: "-second"),
            LogSourceActivityState.Growing,
            time.GetUtcNow());
        logActivity.Current = TestLogCandidates.Snapshot(time.GetUtcNow(), first, second);

        await manager.StartAsync();

        Assert.Empty(manager.Current.Contexts);
        Assert.Equal(2, manager.Current.PendingOffers.Count);
        Assert.All(manager.Current.PendingOffers, offer => Assert.Equal(MonitoringSourceOfferReason.AmbiguousInitialSelection, offer.Reason));
    }

    [Fact]
    public async Task Accepting_one_ambiguous_offer_creates_first_context_remaining_stays_available()
    {
        var (manager, _, logActivity, time) = CreateManager();
        var first = TestLogCandidates.Create(TestLogCandidates.SourceId("acct-1", "Alpha"), LogSourceActivityState.Growing, time.GetUtcNow());
        var second = TestLogCandidates.Create(
            TestLogCandidates.SourceId("acct-1", "Alpha", fileNameSuffix: "-second"),
            LogSourceActivityState.Growing,
            time.GetUtcNow());
        logActivity.Current = TestLogCandidates.Snapshot(time.GetUtcNow(), first, second);
        await manager.StartAsync();
        var offers = manager.Current.PendingOffers;
        var chosen = offers[0];
        var remaining = offers[1];

        var decision = manager.AcceptOffer(chosen.OfferId);

        Assert.True(decision.IsSuccess);
        Assert.Single(manager.Current.Contexts);
        var remainingOffer = Assert.Single(manager.Current.PendingOffers);
        Assert.Equal(remaining.SourceId, remainingOffer.SourceId);
    }

    // ---- Runtime behavior ----------------------------------------------------------------------

    [Fact]
    public async Task Runtime_offline_preserves_claims()
    {
        var (manager, runtime, logActivity, time) = CreateManager();
        var sourceId = TestLogCandidates.SourceId();
        logActivity.Current = TestLogCandidates.Snapshot(time.GetUtcNow(), TestLogCandidates.Create(sourceId, LogSourceActivityState.Growing, time.GetUtcNow()));
        await manager.StartAsync();
        var context = Assert.Single(manager.Current.Contexts);

        runtime.RaiseStatusChanged(GameRuntimeStatus.Running, GameRuntimeStatus.Off);

        var suspended = Assert.Single(manager.Current.Contexts, c => c.ContextId == context.ContextId);
        Assert.Equal(MonitoringContextState.RuntimeSuspended, suspended.State);
        Assert.Equal(sourceId, suspended.CurrentSourceId);
    }

    [Fact]
    public async Task AcceptOffer_while_offline_creates_suspended_context()
    {
        var (manager, runtime, logActivity, time) = SeedManagerWithOneContextAndOneOffer();
        var offer = Assert.Single(manager.Current.PendingOffers);
        runtime.RaiseStatusChanged(GameRuntimeStatus.Running, GameRuntimeStatus.Off);

        var decision = manager.AcceptOffer(offer.OfferId);

        Assert.True(decision.IsSuccess);
        var newContext = Assert.Single(manager.Current.Contexts, c => c.ContextId == decision.ContextId);
        Assert.Equal(MonitoringContextState.RuntimeSuspended, newContext.State);
        Assert.Equal(MonitoringContextState.Ready, newContext.PreviousStateBeforeSuspension);
        Assert.Equal(offer.SourceId, newContext.CurrentSourceId);
    }

    [Fact]
    public async Task Resume_retains_source_claim()
    {
        var (manager, runtime, logActivity, time) = CreateManager();
        var sourceId = TestLogCandidates.SourceId();
        logActivity.Current = TestLogCandidates.Snapshot(time.GetUtcNow(), TestLogCandidates.Create(sourceId, LogSourceActivityState.Growing, time.GetUtcNow()));
        await manager.StartAsync();

        runtime.RaiseStatusChanged(GameRuntimeStatus.Running, GameRuntimeStatus.Off);
        runtime.RaiseStatusChanged(GameRuntimeStatus.Off, GameRuntimeStatus.Running);

        var context = Assert.Single(manager.Current.Contexts);
        Assert.Equal(MonitoringContextState.Ready, context.State);
        Assert.Equal(sourceId, context.CurrentSourceId);
    }

    // ---- Source loss ---------------------------------------------------------------------------

    [Fact]
    public async Task Claimed_source_becomes_unavailable_moves_to_waiting_and_retains_claim()
    {
        var (manager, _, logActivity, time) = CreateManager();
        var sourceId = TestLogCandidates.SourceId();
        logActivity.Current = TestLogCandidates.Snapshot(time.GetUtcNow(), TestLogCandidates.Create(sourceId, LogSourceActivityState.Growing, time.GetUtcNow()));
        await manager.StartAsync();

        logActivity.Current = TestLogCandidates.Snapshot(time.GetUtcNow(), TestLogCandidates.Create(sourceId, LogSourceActivityState.Unavailable, time.GetUtcNow(), exists: false));
        logActivity.RaiseActivityChanged();

        var context = Assert.Single(manager.Current.Contexts);
        Assert.Equal(MonitoringContextState.WaitingForSource, context.State);
        Assert.Equal(sourceId, context.CurrentSourceId);
        Assert.NotNull(context.SourceLostAt);
    }

    [Fact]
    public async Task Recovery_restores_ready_state()
    {
        var (manager, _, logActivity, time) = CreateManager();
        var sourceId = TestLogCandidates.SourceId();
        logActivity.Current = TestLogCandidates.Snapshot(time.GetUtcNow(), TestLogCandidates.Create(sourceId, LogSourceActivityState.Growing, time.GetUtcNow()));
        await manager.StartAsync();
        logActivity.Current = TestLogCandidates.Snapshot(time.GetUtcNow(), TestLogCandidates.Create(sourceId, LogSourceActivityState.Unavailable, time.GetUtcNow(), exists: false));
        logActivity.RaiseActivityChanged();

        logActivity.Current = TestLogCandidates.Snapshot(time.GetUtcNow(), TestLogCandidates.Create(sourceId, LogSourceActivityState.Growing, time.GetUtcNow()));
        logActivity.RaiseActivityChanged();

        var context = Assert.Single(manager.Current.Contexts);
        Assert.Equal(MonitoringContextState.Ready, context.State);
        Assert.Null(context.SourceLostAt);
    }

    [Fact]
    public async Task Source_loss_while_runtime_suspended_preserves_suspension()
    {
        var (manager, runtime, logActivity, time) = CreateManager();
        var sourceId = TestLogCandidates.SourceId();
        logActivity.Current = TestLogCandidates.Snapshot(time.GetUtcNow(), TestLogCandidates.Create(sourceId, LogSourceActivityState.Growing, time.GetUtcNow()));
        await manager.StartAsync();
        runtime.RaiseStatusChanged(GameRuntimeStatus.Running, GameRuntimeStatus.Off);

        logActivity.Current = TestLogCandidates.Snapshot(time.GetUtcNow(), TestLogCandidates.Create(sourceId, LogSourceActivityState.Unavailable, time.GetUtcNow(), exists: false));
        logActivity.RaiseActivityChanged();

        var context = Assert.Single(manager.Current.Contexts);
        Assert.Equal(MonitoringContextState.RuntimeSuspended, context.State);
        Assert.Equal(sourceId, context.CurrentSourceId);
    }

    // ---- Context removal -----------------------------------------------------------------------

    [Fact]
    public async Task RemoveContext_releases_source_and_allows_new_claim()
    {
        var (manager, _, logActivity, time) = CreateManager();
        var sourceId = TestLogCandidates.SourceId();
        logActivity.Current = TestLogCandidates.Snapshot(time.GetUtcNow(), TestLogCandidates.Create(sourceId, LogSourceActivityState.Waiting, time.GetUtcNow()));
        await manager.StartAsync();
        var contextId = manager.AddContext();
        manager.ClaimSource(contextId, sourceId);

        var removeResult = manager.RemoveContext(contextId);

        Assert.True(removeResult.IsSuccess);
        var stopped = Assert.Single(manager.Current.Contexts, c => c.ContextId == contextId);
        Assert.Equal(MonitoringContextState.Stopped, stopped.State);
        Assert.Null(stopped.CurrentSourceId);

        var newContextId = manager.AddContext();
        Assert.True(manager.ClaimSource(newContextId, sourceId).IsSuccess);
    }

    [Fact]
    public async Task RemoveContext_publishes_exactly_once()
    {
        var (manager, _, _, _) = CreateManager();
        await manager.StartAsync();
        var contextId = manager.AddContext();
        var notifications = 0;
        manager.StateChanged += (_, _) => notifications++;

        manager.RemoveContext(contextId);

        Assert.Equal(1, notifications);
    }

    // ---- Concurrency ---------------------------------------------------------------------------

    [Fact]
    public async Task Concurrent_accept_cannot_double_claim_the_same_offer()
    {
        var (manager, _, logActivity, time) = SeedManagerWithOneContextAndOneOffer();
        var offer = Assert.Single(manager.Current.PendingOffers);

        var results = await Task.WhenAll(
            Task.Run(() => manager.AcceptOffer(offer.OfferId)),
            Task.Run(() => manager.AcceptOffer(offer.OfferId)));

        Assert.Single(results, r => r.IsSuccess);
        Assert.Single(results, r => !r.IsSuccess);
        Assert.Equal(2, manager.Current.ContextCount);
    }

    [Fact]
    public async Task No_callback_after_stop_for_log_activity_changes()
    {
        var (manager, _, logActivity, time) = CreateManager();
        await manager.StartAsync();
        await manager.StopAsync();
        var notifications = 0;
        manager.StateChanged += (_, _) => notifications++;

        logActivity.Current = TestLogCandidates.Snapshot(time.GetUtcNow(), TestLogCandidates.Create(TestLogCandidates.SourceId(), LogSourceActivityState.Growing, time.GetUtcNow()));
        logActivity.RaiseActivityChanged();

        Assert.Equal(0, notifications);
        Assert.Empty(manager.Current.Contexts);
    }

    private static (MonitoringSessionManager Manager, FakeGameRuntimeService Runtime, FakeLogActivityService LogActivity, ManualTimeProvider Time)
        SeedManagerWithOneContextAndOneOffer()
    {
        // Ambiguity, not enrollment: a second growing source under the already-monitored account
        // is the case the manager still refuses to attribute on its own.
        var (manager, runtime, logActivity, time) = CreateManager();
        var firstSource = TestLogCandidates.SourceId("acct-1", "Alpha");
        var secondSource = TestLogCandidates.SourceId("acct-1", "Alpha", fileNameSuffix: "-second");
        logActivity.Current = TestLogCandidates.Snapshot(time.GetUtcNow(), TestLogCandidates.Create(firstSource, LogSourceActivityState.Growing, time.GetUtcNow()));
        manager.StartAsync().GetAwaiter().GetResult();

        logActivity.Current = TestLogCandidates.Snapshot(
            time.GetUtcNow(),
            TestLogCandidates.Create(firstSource, LogSourceActivityState.Growing, time.GetUtcNow()),
            TestLogCandidates.Create(secondSource, LogSourceActivityState.Growing, time.GetUtcNow()));
        logActivity.RaiseActivityChanged();

        return (manager, runtime, logActivity, time);
    }
}
