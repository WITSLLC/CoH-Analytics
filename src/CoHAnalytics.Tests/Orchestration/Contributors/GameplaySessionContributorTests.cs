using CoHAnalytics.Models;
using CoHAnalytics.Orchestration.Contributors;
using CoHAnalytics.Orchestration.Models;
using CoHAnalytics.Services;
using CoHAnalytics.Tests.Orchestration;
using CoHAnalytics.Tests.Services;

namespace CoHAnalytics.Tests.Orchestration.Contributors;

public sealed class GameplaySessionContributorTests
{
    [Fact]
    public void Descriptor_matches_session_architecture()
    {
        using var contributor = new GameplaySessionContributor(new FakeGameplaySessionManager());
        var descriptor = contributor.Descriptor;

        Assert.Equal(ApplicationProviders.Session, descriptor.ProviderId);
        Assert.Equal("Gameplay Sessions", descriptor.DisplayName);
        Assert.Equal(
            [ApplicationCapabilities.SessionCurrent, ApplicationCapabilities.IdentityCurrent],
            descriptor.Produces);
        Assert.Equal([ApplicationCapabilities.ParserEvents], descriptor.Requires);
        Assert.Equal(
            [ApplicationCapabilities.MonitoringContexts, ApplicationCapabilities.AccountDiscovery],
            descriptor.Optional);
        Assert.Equal(ApplicationContributorImportance.Important, descriptor.Importance);
        Assert.Equal("live-session", descriptor.IconKey);
        Assert.Equal(ApplicationContributorDescriptor.ExpectedSchemaVersion, descriptor.SchemaVersion);
    }

    [Fact]
    public async Task Zero_sessions_is_ready_and_waiting()
    {
        using var contributor = new GameplaySessionContributor(new FakeGameplaySessionManager());
        var contribution = await contributor.GetContributionAsync();

        Assert.Equal(ContributorHealth.Ready, contribution.Health);
        Assert.Equal(ContributorActivity.Waiting, contribution.Activity);
        Assert.Empty(contribution.Issues);
    }

    [Fact]
    public async Task Active_session_with_recent_committed_event_is_receiving_data()
    {
        var time = new ManualTimeProvider();
        var manager = new FakeGameplaySessionManager
        {
            Current = Snapshot(
                Session(
                    CharacterIdentityConfidence.Confirmed,
                    CharacterIdentityResolutionState.Resolved,
                    GameplaySessionLifecycleState.Active)),
            Diagnostics = Diagnostics(lastCommittedAt: time.GetUtcNow())
        };

        using var contributor = new GameplaySessionContributor(
            manager,
            new GameplaySessionContributorOptions
            {
                TimeProvider = time,
                ReceivingDataWindow = TimeSpan.FromSeconds(2)
            });

        var contribution = await contributor.GetContributionAsync();
        Assert.Equal(ContributorActivity.ReceivingData, contribution.Activity);
        Assert.Equal(ContributorHealth.Ready, contribution.Health);
    }

    [Fact]
    public async Task Trusted_cross_context_conflict_axes_remain_separate_in_facts()
    {
        var manager = new FakeGameplaySessionManager
        {
            Current = Snapshot(
                Session(
                    CharacterIdentityConfidence.Unknown,
                    CharacterIdentityResolutionState.Conflicted,
                    GameplaySessionLifecycleState.Active))
        };

        using var contributor = new GameplaySessionContributor(manager);
        var contribution = await contributor.GetContributionAsync();

        Assert.Equal(1L, IntegerFact(contribution, ContributorFactKeys.Identity.ConflictedCount));
        Assert.Equal(1L, IntegerFact(contribution, ContributorFactKeys.Identity.UnknownCount));
        Assert.Equal(0L, IntegerFact(contribution, ContributorFactKeys.Identity.ConfirmedCount));
        Assert.Contains(
            contribution.Issues,
            issue => issue.Code == ContributorIssueCodes.Session.IdentityConflict);
    }

    [Fact]
    public async Task Overflow_issue_resolves_after_manual_recovery_while_incomplete_remains()
    {
        var contextId = MonitoringContextId.CreateNew();
        var manager = new FakeGameplaySessionManager
        {
            Current = Snapshot(
                Session(
                    CharacterIdentityConfidence.Confirmed,
                    CharacterIdentityResolutionState.Resolved,
                    GameplaySessionLifecycleState.Active,
                    contextId,
                    retentionOverflowed: true))
        };

        using var contributor = new GameplaySessionContributor(manager);
        var contribution = await contributor.GetContributionAsync();

        Assert.Equal(ContributorHealth.Ready, contribution.Health);
        Assert.DoesNotContain(
            contribution.Issues,
            issue => issue.Code == ContributorIssueCodes.Session.RetentionOverflow);
        Assert.Equal(1L, IntegerFact(contribution, ContributorFactKeys.Session.IncompleteCount));
    }

    [Fact]
    public async Task Inferred_resolved_session_is_healthy_without_conflict_issue()
    {
        var manager = new FakeGameplaySessionManager
        {
            Current = Snapshot(
                Session(
                    CharacterIdentityConfidence.Inferred,
                    CharacterIdentityResolutionState.Resolved,
                    GameplaySessionLifecycleState.Active))
        };

        using var contributor = new GameplaySessionContributor(manager);
        var contribution = await contributor.GetContributionAsync();

        Assert.Equal(ContributorHealth.Ready, contribution.Health);
        Assert.Equal(1L, IntegerFact(contribution, ContributorFactKeys.Identity.InferredCount));
        Assert.Equal(1L, IntegerFact(contribution, ContributorFactKeys.Identity.ResolvedCount));
        Assert.DoesNotContain(
            contribution.Issues,
            issue => issue.Code == ContributorIssueCodes.Session.IdentityConflict);
    }

    [Fact]
    public async Task Unknown_candidate_emits_selection_issue_without_names()
    {
        var contextId = MonitoringContextId.CreateNew();
        var manager = new FakeGameplaySessionManager
        {
            Current = Snapshot(
                Session(
                    CharacterIdentityConfidence.Unknown,
                    CharacterIdentityResolutionState.Candidate,
                    GameplaySessionLifecycleState.Active,
                    contextId,
                    candidateCount: 2))
        };

        using var contributor = new GameplaySessionContributor(manager);
        var contribution = await contributor.GetContributionAsync();

        Assert.Equal(1L, IntegerFact(contribution, ContributorFactKeys.Identity.UnknownCount));
        Assert.Equal(1L, IntegerFact(contribution, ContributorFactKeys.Identity.CandidateCount));
        var issue = Assert.Single(
            contribution.Issues,
            item => item.Code == ContributorIssueCodes.Session.IdentitySelectionRequired);
        Assert.Equal(contextId.ToString(), issue.RelatedEntityId);
        Assert.DoesNotContain("Hero", issue.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Retention_overflow_emits_actionable_warning()
    {
        var contextId = MonitoringContextId.CreateNew();
        var manager = new FakeGameplaySessionManager
        {
            Current = Snapshot(
                Session(
                    CharacterIdentityConfidence.Unknown,
                    CharacterIdentityResolutionState.IdentityRequired,
                    GameplaySessionLifecycleState.Active,
                    contextId,
                    retentionOverflowed: true))
        };

        using var contributor = new GameplaySessionContributor(manager);
        var contribution = await contributor.GetContributionAsync();

        Assert.Equal(ContributorHealth.Degraded, contribution.Health);
        var issue = Assert.Single(
            contribution.Issues,
            item => item.Code == ContributorIssueCodes.Session.RetentionOverflow);
        Assert.True(issue.RequiresUserAction);
        Assert.Equal(contextId.ToString(), issue.RelatedEntityId);
    }

    [Fact]
    public async Task Lifecycle_starts_and_stops_manager_once()
    {
        var manager = new FakeGameplaySessionManager();
        using var contributor = new GameplaySessionContributor(manager);

        await contributor.StartAsync();
        await contributor.StartAsync();
        Assert.Equal(1, manager.StartCount);

        await contributor.StopAsync();
        await contributor.StopAsync();
        Assert.Equal(1, manager.StopCount);
    }

    [Fact]
    public void State_changed_forwards_data_free_invalidation()
    {
        var manager = new FakeGameplaySessionManager();
        using var contributor = new GameplaySessionContributor(manager);
        var invalidations = 0;
        contributor.ContributionChanged += (_, _) => invalidations++;

        manager.RaiseStateChanged();
        Assert.Equal(1, invalidations);
    }

    private static long IntegerFact(ApplicationContribution contribution, string key) =>
        Assert.IsType<ApplicationFactValue.Integer>(
            contribution.Facts.Single(fact => fact.Key == key).Value).Value;

    private static GameplaySessionManagerSnapshot Snapshot(params GameplaySessionSnapshot[] sessions) =>
        GameplaySessionManagerSnapshot.Create(sessions, DateTimeOffset.UnixEpoch, 1);

    private static GameplaySessionSnapshot Session(
        CharacterIdentityConfidence confidence,
        CharacterIdentityResolutionState resolution,
        GameplaySessionLifecycleState lifecycle,
        MonitoringContextId? contextId = null,
        int candidateCount = 0,
        bool retentionOverflowed = false) =>
        new()
        {
            SessionId = GameplaySessionId.CreateNew(),
            ContextId = contextId ?? MonitoringContextId.CreateNew(),
            AccountStableId = "acct-1",
            LifecycleState = lifecycle,
            CharacterIdentityConfidence = confidence,
            CharacterIdentityResolutionState = resolution,
            StartedAt = DateTimeOffset.UnixEpoch,
            CandidateCount = candidateCount,
            RetentionOverflowed = retentionOverflowed,
            NeedsAttention = retentionOverflowed
                || resolution == CharacterIdentityResolutionState.Conflicted
        };

    private static GameplaySessionDiagnostics Diagnostics(
        DateTimeOffset? lastCommittedAt = null,
        bool workQueueOverflowed = false,
        int failedContextCount = 0) =>
        GameplaySessionTestInfrastructure.IdleGameplayDiagnostics(
            isRunning: true,
            snapshotRevision: 1,
            workQueueOverflowed: workQueueOverflowed,
            lastAcceptedWorkSequence: lastCommittedAt is null ? 0 : 1,
            lastCompletedWorkSequence: lastCommittedAt is null ? 0 : 1,
            activeSessionCount: 1,
            totalCommittedEvents: lastCommittedAt is null ? 0 : 1,
            lastCommittedEventAt: lastCommittedAt,
            failedContextCount: failedContextCount);

    private static GameplaySessionDiagnostics CopyDiagnostics(
        GameplaySessionDiagnostics source,
        bool isRunning) =>
        new()
        {
            IsRunning = isRunning,
            SnapshotRevision = source.SnapshotRevision,
            LifecycleEpoch = source.LifecycleEpoch,
            ActiveSessionCount = source.ActiveSessionCount,
            SuspendedSessionCount = source.SuspendedSessionCount,
            NeedsAttentionSessionCount = source.NeedsAttentionSessionCount,
            OverflowedSessionCount = source.OverflowedSessionCount,
            TotalCommittedEvents = source.TotalCommittedEvents,
            LastCommittedEventAt = source.LastCommittedEventAt,
            FailedContextCount = source.FailedContextCount,
            PendingCommittedEventCount = source.PendingCommittedEventCount,
            WorkQueue = source.WorkQueue,
            PendingCommittedEvents = source.PendingCommittedEvents,
            WorkQueueOverflowed = source.WorkQueueOverflowed,
            PreStartBufferOverflowed = source.PreStartBufferOverflowed,
            LastAcceptedWorkSequence = source.LastAcceptedWorkSequence,
            LastCompletedWorkSequence = source.LastCompletedWorkSequence,
            ActiveProcessorCallbackCount = source.ActiveProcessorCallbackCount,
            RecentOperations = source.RecentOperations
        };

    private sealed class FakeGameplaySessionManager : IGameplaySessionManager
    {
        public GameplaySessionManagerSnapshot Current { get; set; } = GameplaySessionManagerSnapshot.Empty;

        public GameplaySessionDiagnostics Diagnostics { get; set; } =
            GameplaySessionTestInfrastructure.IdleGameplayDiagnostics(isRunning: false);

        public int StartCount { get; private set; }

        public int StopCount { get; private set; }

        public event EventHandler<GameplaySessionManagerChangedEventArgs>? StateChanged;

        public event EventHandler<GameplaySessionEventsAvailableEventArgs>? CommittedEventsAvailable
        {
            add { }
            remove { }
        }

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            if (!Diagnostics.IsRunning)
            {
                StartCount++;
            }

            Diagnostics = CopyDiagnostics(Diagnostics, isRunning: true);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            if (Diagnostics.IsRunning)
            {
                StopCount++;
            }

            Diagnostics = CopyDiagnostics(Diagnostics, isRunning: false);
            return Task.CompletedTask;
        }

        public GameplaySessionOperationResult ConfirmCharacter(
            MonitoringContextId contextId,
            CharacterRecordId characterRecordId) =>
            GameplaySessionOperationResult.Success();

        public GameplaySessionOperationResult ClearIdentity(MonitoringContextId contextId) =>
            GameplaySessionOperationResult.Success();

        public GameplaySessionOperationResult StartTrackedCombat(MonitoringContextId contextId) =>
            GameplaySessionOperationResult.Success();

        public GameplaySessionOperationResult PauseTrackedCombat(MonitoringContextId contextId) =>
            GameplaySessionOperationResult.Success();

        public GameplaySessionOperationResult ResumeTrackedCombat(MonitoringContextId contextId) =>
            GameplaySessionOperationResult.Success();

        public GameplaySessionOperationResult StopTrackedCombat(MonitoringContextId contextId) =>
            GameplaySessionOperationResult.Success();

        public GameplaySessionOperationResult ResetTrackedCombat(MonitoringContextId contextId) =>
            GameplaySessionOperationResult.Success();

        public void ResetForNewRuntimeGeneration()
        {
        }

        public GameplaySessionDiagnostics GetDiagnostics() => Diagnostics;

        public void RaiseStateChanged() =>
            StateChanged?.Invoke(this, new GameplaySessionManagerChangedEventArgs(Current));
    }
}
