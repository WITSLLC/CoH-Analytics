namespace CoHAnalytics.Models;

/// <summary>
/// Explains why a gameplay session context was or was not selected.
/// </summary>
public enum GameplaySessionContextSelectionReason
{
    /// <summary>No applicable gameplay session context could be resolved.</summary>
    Unresolved = 0,

    /// <summary>
    /// The explicitly pinned viewed character currently has an applicable gameplay session.
    /// </summary>
    PinnedViewedContext,

    /// <summary>
    /// The existing live-follow monitoring context currently has an applicable gameplay session.
    /// </summary>
    LiveFollowContext,

    /// <summary>Exactly one applicable gameplay session context exists.</summary>
    SoleActiveSession
}
