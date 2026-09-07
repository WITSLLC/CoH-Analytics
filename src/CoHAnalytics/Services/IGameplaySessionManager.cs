using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

/// <summary>
/// Owns per-context gameplay sessions, runtime identity workflow, and ordered committed session
/// events (Revision 9 §3.6.22).
/// </summary>
public interface IGameplaySessionManager : ITrackedCombatLifecycle
{
    GameplaySessionManagerSnapshot Current { get; }

    event EventHandler<GameplaySessionManagerChangedEventArgs>? StateChanged;

    event EventHandler<GameplaySessionEventsAvailableEventArgs>? CommittedEventsAvailable;

    Task StartAsync(CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);

    GameplaySessionOperationResult ConfirmCharacter(
        MonitoringContextId contextId,
        CharacterRecordId characterRecordId);

    GameplaySessionOperationResult ClearIdentity(MonitoringContextId contextId);

    /// <summary>
    /// Captures a non-terminal historical performance boundary for the active gameplay session.
    /// Implementations that do not own historical persistence may decline the operation.
    /// </summary>
    GameplaySessionOperationResult CaptureHistoricalPerformanceBoundary(
        MonitoringContextId contextId) =>
        GameplaySessionOperationResult.Failure(
            GameplaySessionOutcome.ProcessingFailed,
            "Historical performance boundaries are not supported by this implementation.");

    GameplaySessionDiagnostics GetDiagnostics();

    /// <summary>
    /// Clears all live runtime state after a zero-client gap when the first new Homecoming process
    /// starts. Does not affect saved sessions.
    /// </summary>
    void ResetForNewRuntimeGeneration();
}
