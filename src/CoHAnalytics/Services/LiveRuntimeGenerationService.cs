using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

/// <summary>
/// Clears stale live runtime state when a fresh Homecoming <see cref="HomecomingProcessInstance"/>
/// begins after prior process instances have ended, without resetting on mere client-count changes.
/// </summary>
public sealed class LiveRuntimeGenerationService : IDisposable
{
    private readonly IGameRuntimeService _runtimeService;
    private readonly IMonitoringSessionManager _monitoringSessionManager;
    private readonly IGameplaySessionManager _gameplaySessionManager;
    private readonly IViewedContextService _viewedContextService;
    private bool _observedNonZeroClientCount;
    private bool _observedZeroClientCountAfterNonZero;
    private bool _disposed;

    public LiveRuntimeGenerationService(
        IGameRuntimeService runtimeService,
        IMonitoringSessionManager monitoringSessionManager,
        IGameplaySessionManager gameplaySessionManager,
        IViewedContextService viewedContextService)
    {
        _runtimeService = runtimeService;
        _monitoringSessionManager = monitoringSessionManager;
        _gameplaySessionManager = gameplaySessionManager;
        _viewedContextService = viewedContextService;
        _runtimeService.StatusChanged += OnRuntimeStatusChanged;
    }

    internal static bool IsRelaunchAfterFullExit(GameRuntimeStatusChangedEventArgs args) =>
        args.PreviousRunningClients.Count == 0
        && args.RunningClients.Count > 0;

    internal static bool IsCompleteProcessInstanceTurnover(GameRuntimeStatusChangedEventArgs args)
    {
        if (args.RunningClients.Count == 0 || args.PreviousRunningClients.Count == 0)
        {
            return false;
        }

        return !args.PreviousRunningClients.Any(previous =>
            args.RunningClients.Any(current =>
                current.Equals(previous)
                || current.IsMetadataRefinementOf(previous)));
    }

    internal bool ShouldResetForFreshProcessInstance(GameRuntimeStatusChangedEventArgs args) =>
        TryConsumeNewGenerationBoundary(
            args,
            ref _observedNonZeroClientCount,
            ref _observedZeroClientCountAfterNonZero);

    internal static bool TryConsumeNewGenerationBoundary(
        GameRuntimeStatusChangedEventArgs args,
        ref bool observedNonZeroClientCount,
        ref bool observedZeroClientCountAfterNonZero)
    {
        if (args.RunningClients.Count > 0 || args.RunningClientCount > 0)
        {
            if (ShouldResetRunningClients(args, observedZeroClientCountAfterNonZero))
            {
                observedZeroClientCountAfterNonZero = false;
                return true;
            }

            observedNonZeroClientCount = true;
            return false;
        }

        if ((args.RunningClients.Count == 0 && args.RunningClientCount == 0)
            && observedNonZeroClientCount)
        {
            observedZeroClientCountAfterNonZero = true;
        }

        return false;
    }

    private static bool ShouldResetRunningClients(
        GameRuntimeStatusChangedEventArgs args,
        bool observedZeroClientCountAfterNonZero)
    {
        if (args.RunningClients.Count > 0 || args.PreviousRunningClients.Count > 0)
        {
            if (observedZeroClientCountAfterNonZero && IsRelaunchAfterFullExit(args))
            {
                return true;
            }

            return IsCompleteProcessInstanceTurnover(args);
        }

        return observedZeroClientCountAfterNonZero
            && args.PreviousRunningClientCount == 0
            && args.RunningClientCount > 0;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _runtimeService.StatusChanged -= OnRuntimeStatusChanged;
    }

    private void OnRuntimeStatusChanged(object? sender, GameRuntimeStatusChangedEventArgs e)
    {
        var observedZeroClientCountAfterNonZero = _observedZeroClientCountAfterNonZero;
        if (!ShouldResetForFreshProcessInstance(e))
        {
            return;
        }

        ResetLiveRuntimeGeneration(observedZeroClientCountAfterNonZero);
    }

    internal void ResetLiveRuntimeGeneration(bool observedZeroClientCountAfterNonZero = true)
    {
        // Queue gameplay reset before monitoring reconcile so work-queue ordering is reset
        // then monitoring snapshot, not snapshot then reset.
        _gameplaySessionManager.ResetForNewRuntimeGeneration();
        _monitoringSessionManager.ResetForNewRuntimeGeneration(observedZeroClientCountAfterNonZero);
        _viewedContextService.ResetForNewRuntimeGeneration();
    }
}
