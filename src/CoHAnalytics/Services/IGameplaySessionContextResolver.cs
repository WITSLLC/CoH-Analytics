using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

/// <summary>
/// Resolves the currently selected gameplay session context for session-scoped workspaces.
/// </summary>
public interface IGameplaySessionContextResolver
{
    GameplaySessionContextSelection Resolve();

    /// <summary>
    /// Resolves the monitored live session for Live Session identity presentation.
    /// Ignores Accounts viewed-character pinning and includes ready monitoring contexts
    /// before a gameplay session has started.
    /// </summary>
    GameplaySessionContextSelection ResolveForLiveMonitoring();
}
