namespace CoHAnalytics.Models;

/// <summary>
/// An immutable aggregate view of every monitoring context and pending additional-session offer
/// held by the manager, with counts by state.
/// </summary>
public sealed record MonitoringSessionManagerSnapshot
{
    private MonitoringSessionManagerSnapshot(
        IReadOnlyList<MonitoringContextSnapshot> contexts,
        IReadOnlyList<MonitoringSourceOffer> pendingOffers,
        int unclaimedGrowingSourceCount,
        int declinedSourceCount,
        DateTimeOffset observedAt,
        long revision)
    {
        Contexts = contexts;
        PendingOffers = pendingOffers;
        UnclaimedGrowingSourceCount = unclaimedGrowingSourceCount;
        DeclinedSourceCount = declinedSourceCount;
        ObservedAt = observedAt;
        Revision = revision;
    }

    /// <summary>Every context, including stopped ones, in deterministic order.</summary>
    public IReadOnlyList<MonitoringContextSnapshot> Contexts { get; }

    /// <summary>Every offer currently awaiting a user decision, in deterministic order.</summary>
    public IReadOnlyList<MonitoringSourceOffer> PendingOffers { get; }

    /// <summary>Growing sources with no active claim and no active pending offer or suppression.</summary>
    public int UnclaimedGrowingSourceCount { get; }

    /// <summary>Sources currently suppressed from re-offering because they were declined.</summary>
    public int DeclinedSourceCount { get; }

    public DateTimeOffset ObservedAt { get; }

    /// <summary>Increments only when the snapshot is semantically different from its predecessor.</summary>
    public long Revision { get; }

    public int ContextCount => Contexts.Count;

    public int ActiveContextCount => CountState(MonitoringContextState.Ready);

    public int SuspendedContextCount => CountState(MonitoringContextState.RuntimeSuspended);

    public int WaitingContextCount => CountState(MonitoringContextState.WaitingForSource);

    public int ErrorContextCount => CountState(MonitoringContextState.Error);

    public int ClaimedSourceCount =>
        Contexts.Count(context => context.State != MonitoringContextState.Stopped && context.CurrentSourceId is not null);

    public int PendingOfferCount => PendingOffers.Count;

    /// <summary>
    /// Sources that could not be attributed automatically. Every pending offer is an ambiguity:
    /// unambiguous active sources are enrolled without asking (§3.6.6).
    /// </summary>
    public int AmbiguousSourceCount => PendingOffers.Count;

    public static MonitoringSessionManagerSnapshot Empty { get; } = new([], [], 0, 0, default, 0);

    public static MonitoringSessionManagerSnapshot Create(
        IEnumerable<MonitoringContextSnapshot> contexts,
        IEnumerable<MonitoringSourceOffer> pendingOffers,
        int unclaimedGrowingSourceCount,
        int declinedSourceCount,
        DateTimeOffset observedAt,
        long revision) =>
        new(
            [.. contexts
                .OrderBy(context => context.CreatedAt)
                .ThenBy(context => context.ContextId.ToString(), StringComparer.Ordinal)],
            [.. pendingOffers
                .OrderBy(offer => offer.DetectedAt)
                .ThenBy(offer => offer.OfferId.ToString(), StringComparer.Ordinal)],
            unclaimedGrowingSourceCount,
            declinedSourceCount,
            observedAt,
            revision);

    /// <summary>
    /// Whether another snapshot describes the same observed state. <see cref="ObservedAt"/> and
    /// <see cref="Revision"/> are excluded, so re-observing an unchanged world is not treated as
    /// a change.
    /// </summary>
    public bool IsSemanticallyEquivalentTo(MonitoringSessionManagerSnapshot other)
    {
        if (Contexts.Count != other.Contexts.Count
            || PendingOffers.Count != other.PendingOffers.Count
            || UnclaimedGrowingSourceCount != other.UnclaimedGrowingSourceCount
            || DeclinedSourceCount != other.DeclinedSourceCount)
        {
            return false;
        }

        for (var index = 0; index < Contexts.Count; index++)
        {
            if (Contexts[index] != other.Contexts[index])
            {
                return false;
            }
        }

        for (var index = 0; index < PendingOffers.Count; index++)
        {
            if (PendingOffers[index] != other.PendingOffers[index])
            {
                return false;
            }
        }

        return true;
    }

    public MonitoringSessionManagerSnapshot WithObservation(DateTimeOffset observedAt, long revision) =>
        new(Contexts, PendingOffers, UnclaimedGrowingSourceCount, DeclinedSourceCount, observedAt, revision);

    private int CountState(MonitoringContextState state) =>
        Contexts.Count(context => context.State == state);
}
