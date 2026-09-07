namespace CoHAnalytics.Models;

/// <summary>Aggregate immutable view of all active gameplay sessions.</summary>
public sealed record GameplaySessionManagerSnapshot
{
    private GameplaySessionManagerSnapshot(
        IReadOnlyList<GameplaySessionSnapshot> sessions,
        DateTimeOffset observedAt,
        long revision)
    {
        Sessions = sessions;
        ObservedAt = observedAt;
        Revision = revision;
    }

    public IReadOnlyList<GameplaySessionSnapshot> Sessions { get; }

    public DateTimeOffset ObservedAt { get; }

    public long Revision { get; }

    public static GameplaySessionManagerSnapshot Empty { get; } =
        new([], default, 0);

    public static GameplaySessionManagerSnapshot Create(
        IReadOnlyList<GameplaySessionSnapshot> sessions,
        DateTimeOffset observedAt,
        long revision) =>
        new(
            sessions.OrderBy(session => session.ContextId.Value).ToArray(),
            observedAt,
            revision);
}
