namespace CoHAnalytics.Models;

/// <summary>
/// Aggregate UI read model for Live Session identity presentation.
/// </summary>
public sealed record GameplaySessionIdentityReadModelSnapshot
{
    private GameplaySessionIdentityReadModelSnapshot(
        IReadOnlyList<LiveMonitoringContextIdentityReadModel> contexts,
        DateTimeOffset observedAt,
        long revision)
    {
        Contexts = contexts;
        ObservedAt = observedAt;
        Revision = revision;
    }

    public IReadOnlyList<LiveMonitoringContextIdentityReadModel> Contexts { get; }

    public DateTimeOffset ObservedAt { get; }

    public long Revision { get; }

    public static GameplaySessionIdentityReadModelSnapshot Empty { get; } =
        new([], default, 0);

    public static GameplaySessionIdentityReadModelSnapshot Create(
        IReadOnlyList<LiveMonitoringContextIdentityReadModel> contexts,
        DateTimeOffset observedAt,
        long revision) =>
        new(
            contexts.OrderBy(context => context.ContextId.Value).ToArray(),
            observedAt,
            revision);
}
