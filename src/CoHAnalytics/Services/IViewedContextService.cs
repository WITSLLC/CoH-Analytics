using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

/// <summary>
/// Shell-level viewed context for character-aware workspaces.
/// </summary>
public interface IViewedContextService
{
    ViewedContextState Current { get; }

    event EventHandler<ViewedContextChangedEventArgs>? Changed;

    void SelectViewedAccount(string accountStableId);

    void SelectViewedCharacter(string accountStableId, CharacterRecordId characterRecordId);

    void ReturnToLive();

    /// <summary>
    /// Selects which concurrently active gameplay session context session-scoped workspaces display.
    /// Live Session is the sole user-facing entry point for this operation.
    /// </summary>
    void SelectGameplaySessionContext(MonitoringContextId contextId);

    /// <summary>
    /// Clears live-follow and pinned runtime context after a zero-client gap when the first new
    /// Homecoming process starts.
    /// </summary>
    void ResetForNewRuntimeGeneration();
}
