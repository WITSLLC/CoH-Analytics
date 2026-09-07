using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

/// <summary>
/// Narrow lifecycle surface for tracked combat accumulation. UI and tests signal start/pause/resume/stop
/// without owning tracked combat totals or elapsed-time math.
/// </summary>
public interface ITrackedCombatLifecycle
{
    GameplaySessionOperationResult StartTrackedCombat(MonitoringContextId contextId);

    GameplaySessionOperationResult PauseTrackedCombat(MonitoringContextId contextId);

    GameplaySessionOperationResult ResumeTrackedCombat(MonitoringContextId contextId);

    GameplaySessionOperationResult StopTrackedCombat(MonitoringContextId contextId);

    GameplaySessionOperationResult ResetTrackedCombat(MonitoringContextId contextId);
}
