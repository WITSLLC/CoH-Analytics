using CoHAnalytics.Models;
using CoHAnalytics.Orchestration.Contracts;
using CoHAnalytics.Orchestration.Models;
using CoHAnalytics.ReferenceData;
using CoHAnalytics.Services;
using CoHAnalytics.ViewModels.Workspaces;

namespace CoHAnalytics.Tests.Services;

internal static class TestGameplaySessionContextSupport
{
    internal static ViewedContextState FollowingLive(MonitoringContextId? liveFollowContextId = null) =>
        new()
        {
            IsFollowingLive = true,
            LiveFollowContextId = liveFollowContextId
        };

    internal static ViewedContextState PinnedCharacter(
        string accountStableId,
        CharacterRecordId characterRecordId,
        MonitoringContextId? liveFollowContextId = null) =>
        new()
        {
            AccountStableId = accountStableId,
            CharacterRecordId = characterRecordId,
            IsFollowingLive = false,
            LiveFollowContextId = liveFollowContextId
        };

    internal static GameplaySessionContextResolver CreateResolver(
        IGameplaySessionIdentityReadService identityReadService,
        ViewedContextState? viewedContext = null)
    {
        var viewedContextService = new FakeViewedContextService(viewedContext ?? FollowingLive());
        return new GameplaySessionContextResolver(identityReadService, viewedContextService);
    }

    internal static LiveSessionViewModel CreateLiveSessionViewModel(
        IGameplaySessionIdentityReadService identityReadService,
        ViewedContextState? viewedContext = null,
        ISessionStore? sessionStore = null,
        IItemReferenceCatalog? itemReferenceCatalog = null) =>
        CreateLiveSessionViewModel(
            identityReadService,
            new FakeGameplaySessionManager(),
            viewedContext,
            sessionStore,
            itemReferenceCatalog);

    internal static LiveSessionViewModel CreateLiveSessionViewModel(
        IGameplaySessionIdentityReadService identityReadService,
        IGameplaySessionManager gameplaySessionManager,
        ViewedContextState? viewedContext = null,
        ISessionStore? sessionStore = null,
        IItemReferenceCatalog? itemReferenceCatalog = null)
    {
        var viewedContextService = new FakeViewedContextService(viewedContext ?? FollowingLive());
        var resolver = new GameplaySessionContextResolver(identityReadService, viewedContextService);
        return new LiveSessionViewModel(
            new FakeApplicationOrchestrator(),
            new FakeGameRuntimeService { CurrentStatus = GameRuntimeStatus.Off },
            gameplaySessionManager,
            identityReadService,
            resolver,
            viewedContextService,
            sessionStore,
            itemReferenceCatalog);
    }

    internal static AnalyticsViewModel CreateAnalyticsViewModel(
        IGameplaySessionIdentityReadService identityReadService,
        ViewedContextState? viewedContext = null) =>
        CreateAnalyticsViewModel(
            identityReadService,
            new FakeGameplaySessionManager(),
            viewedContext);

    internal static AnalyticsViewModel CreateAnalyticsViewModel(
        IGameplaySessionIdentityReadService identityReadService,
        IGameplaySessionManager gameplaySessionManager,
        ViewedContextState? viewedContext = null)
    {
        var viewedContextService = new FakeViewedContextService(viewedContext ?? FollowingLive());
        var resolver = new GameplaySessionContextResolver(identityReadService, viewedContextService);
        return new AnalyticsViewModel(
            new FakeApplicationOrchestrator(),
            new FakeGameRuntimeService { CurrentStatus = GameRuntimeStatus.Off },
            identityReadService,
            resolver,
            viewedContextService);
    }

    internal static LiveMonitoringContextIdentityReadModel CreateContext(
        MonitoringContextId contextId,
        string accountStableId,
        CharacterRecordId? recordId,
        string displayName,
        DateTimeOffset startedAt,
        long experience = 0,
        long influence = 0,
        IReadOnlyList<GameplaySessionItemTotal>? salvage = null,
        CombatSnapshot? combat = null,
        int retainedCombatEventCount = 0) =>
        new()
        {
            ContextId = contextId,
            ContextState = MonitoringContextState.Ready,
            AccountStableId = accountStableId,
            AccountDisplayName = accountStableId,
            CharacterRecordId = recordId,
            CharacterDisplayName = displayName,
            CharacterIdentityConfidence = CharacterIdentityConfidence.Confirmed,
            CharacterIdentityResolutionState = CharacterIdentityResolutionState.Resolved,
            SessionLifecycleState = GameplaySessionLifecycleState.Active,
            HasActiveSession = true,
            NeedsAttention = false,
            CandidateCount = 0,
            IdentityStatusLabel = "Confirmed",
            IdentityDetail = "Active character: " + displayName,
            SessionStartedAt = startedAt,
            SessionExperienceGained = experience,
            SessionGameplayInfluenceGained = influence,
            SalvageTotals = salvage ?? [],
            Combat = combat ?? CombatSnapshot.Empty,
            RetainedCombatEventCount = retainedCombatEventCount
        };

    internal sealed class FakeViewedContextService : IViewedContextService
    {
        public FakeViewedContextService(ViewedContextState? initialState = null)
        {
            Current = initialState ?? FollowingLive();
        }

        public ViewedContextState Current { get; set; }

        public event EventHandler<ViewedContextChangedEventArgs>? Changed;

        public void SelectViewedAccount(string accountStableId)
        {
            Current = new ViewedContextState
            {
                AccountStableId = accountStableId,
                IsFollowingLive = false,
                LiveFollowContextId = Current.LiveFollowContextId
            };
            RaiseChanged();
        }

        public void SelectViewedCharacter(string accountStableId, CharacterRecordId characterRecordId)
        {
            Current = new ViewedContextState
            {
                AccountStableId = accountStableId,
                CharacterRecordId = characterRecordId,
                IsFollowingLive = false,
                LiveFollowContextId = Current.LiveFollowContextId
            };
            RaiseChanged();
        }

        public void ReturnToLive()
        {
            Current = Current with { IsFollowingLive = true };
            RaiseChanged();
        }

        public void SelectGameplaySessionContext(MonitoringContextId contextId)
        {
            Current = Current with
            {
                IsFollowingLive = true,
                LiveFollowContextId = contextId
            };
            RaiseChanged();
        }

        public void SetCurrent(ViewedContextState state)
        {
            Current = state;
            RaiseChanged();
        }

        public void ResetForNewRuntimeGeneration()
        {
            Current = FollowingLive();
            RaiseChanged();
        }

        public void RaiseChanged() =>
            Changed?.Invoke(this, new ViewedContextChangedEventArgs { State = Current });
    }

    internal sealed class FakeApplicationOrchestrator : IApplicationOrchestrator
    {
        public ApplicationStateSnapshot Current { get; set; } =
            ApplicationStateSnapshot.Empty(DateTimeOffset.UtcNow);

        public event EventHandler<ApplicationStateChangedEventArgs>? SnapshotChanged
        {
            add { }
            remove { }
        }

        public Task RefreshAsync(string? providerId = null, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    internal sealed class FakeGameplaySessionManager : IGameplaySessionManager
    {
        public List<MonitoringContextId> HistoricalPerformanceBoundaryContexts { get; } = [];

        public GameplaySessionOperationResult HistoricalPerformanceBoundaryResult { get; set; } =
            GameplaySessionOperationResult.Success();

        public Action<MonitoringContextId>? HistoricalPerformanceBoundaryInvoked { get; set; }

        public GameplaySessionManagerSnapshot Current { get; set; } = GameplaySessionManagerSnapshot.Empty;

        public GameplaySessionDiagnostics Diagnostics { get; set; } =
            GameplaySessionTestInfrastructure.IdleGameplayDiagnostics(isRunning: false);

        public event EventHandler<GameplaySessionManagerChangedEventArgs>? StateChanged
        {
            add { }
            remove { }
        }

        public event EventHandler<GameplaySessionEventsAvailableEventArgs>? CommittedEventsAvailable
        {
            add { }
            remove { }
        }

        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public GameplaySessionOperationResult ConfirmCharacter(
            MonitoringContextId contextId,
            CharacterRecordId characterRecordId) =>
            GameplaySessionOperationResult.Success();

        public GameplaySessionOperationResult ClearIdentity(MonitoringContextId contextId) =>
            GameplaySessionOperationResult.Success();

        public GameplaySessionOperationResult CaptureHistoricalPerformanceBoundary(
            MonitoringContextId contextId)
        {
            HistoricalPerformanceBoundaryContexts.Add(contextId);
            HistoricalPerformanceBoundaryInvoked?.Invoke(contextId);
            return HistoricalPerformanceBoundaryResult;
        }

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
    }
}
