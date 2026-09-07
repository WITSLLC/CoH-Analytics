namespace CoHAnalytics.Models;

/// <summary>
/// Raised when observed chat-log activity changed semantically.
/// </summary>
/// <remarks>
/// The snapshot is carried for the convenience of domain consumers. Orchestration
/// contributors must treat this event purely as an invalidation hint and pull current state,
/// so that the orchestrator keeps control of timing, ordering, and coalescing.
/// </remarks>
public sealed class LogActivityChangedEventArgs : EventArgs
{
    public LogActivityChangedEventArgs(LogActivitySnapshot snapshot)
    {
        Snapshot = snapshot;
    }

    public LogActivitySnapshot Snapshot { get; }
}
