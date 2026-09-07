namespace CoHAnalytics.Models;

/// <summary>
/// Domain-only state for one monitoring context. These values belong to
/// <c>MonitoringSessionManager</c> and its context model; they are never added to
/// <c>ContributorHealth</c>, <c>ContributorActivity</c>, <c>ApplicationContributorLifecycleState</c>,
/// or <c>OverallApplicationState</c> (Revision 6 §3.6.17, Revision 7 §3.6.20.5).
/// </summary>
public enum MonitoringContextState
{
    /// <summary>The context exists but no chat-log source is currently assigned to it.</summary>
    WaitingForSource,

    /// <summary>A source is assigned and the context is considered configured and active.</summary>
    Ready,

    /// <summary>
    /// Homecoming Runtime is offline. The context is preserved, including its account and
    /// source bindings, and awaits runtime return. This is a healthy, expected state, not a
    /// failure (Revision 7 §3.6.20.5).
    /// </summary>
    RuntimeSuspended,

    /// <summary>The context was intentionally stopped or removed. Not application shutdown.</summary>
    Stopped,

    /// <summary>The context has failed in a way that is specific to that context alone.</summary>
    Error
}
