namespace CoHAnalytics.Models;

/// <summary>
/// Authoritative result of resolving the gameplay session context for session-scoped workspaces.
/// </summary>
public sealed record GameplaySessionContextSelection
{
    public static GameplaySessionContextSelection Unresolved { get; } = new()
    {
        Reason = GameplaySessionContextSelectionReason.Unresolved
    };

    public GameplaySessionContextSelectionReason Reason { get; init; } =
        GameplaySessionContextSelectionReason.Unresolved;

    public LiveMonitoringContextIdentityReadModel? Context { get; init; }

    public bool IsResolved => Context is not null;

    public static GameplaySessionContextSelection FromContext(
        LiveMonitoringContextIdentityReadModel context,
        GameplaySessionContextSelectionReason reason) =>
        new()
        {
            Context = context,
            Reason = reason
        };
}
