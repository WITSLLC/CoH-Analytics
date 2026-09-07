using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

/// <summary>
/// Owns the collection of monitoring contexts: source claiming and release, automatic enrollment
/// of every unambiguous active source, ambiguity offers, automatic same-account rollover, and
/// runtime-aware suspension/resumption.
/// </summary>
/// <remarks>
/// The manager consumes <see cref="ILogActivityService"/> candidates for source
/// discovery/activity only; it never parses content, identifies characters, or creates
/// gameplay sessions. Those remain later slices.
/// </remarks>
public interface IMonitoringSessionManager
{
    /// <summary>The most recently published immutable snapshot.</summary>
    MonitoringSessionManagerSnapshot Current { get; }

    /// <summary>
    /// Whether Homecoming Runtime was last observed as <see cref="GameRuntimeStatus.Running"/>.
    /// Read immediately at startup and updated immediately on each runtime transition; never
    /// inferred from the context collection, so it stays meaningful even with zero contexts.
    /// </summary>
    bool IsRuntimeAvailable { get; }

    /// <summary>
    /// Raised when the published snapshot became semantically different. Orchestration
    /// consumers must treat this as an invalidation hint and pull <see cref="Current"/>. A future
    /// parser coordinator may instead enqueue the immutable snapshot carried by the event args so
    /// it observes every in-process source-binding generation without doing work in the callback.
    /// </summary>
    event EventHandler<MonitoringSessionManagerChangedEventArgs>? StateChanged;

    Task StartAsync(CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Explicitly claims <paramref name="sourceId"/> for <paramref name="contextId"/>. Returns
    /// an explicit domain result for expected conflicts rather than throwing.
    /// </summary>
    MonitoringSourceClaimResult ClaimSource(MonitoringContextId contextId, LogSourceId sourceId);

    /// <summary>
    /// Releases whatever source <paramref name="contextId"/> currently claims, returning the
    /// context to <see cref="MonitoringContextState.WaitingForSource"/> and making the source
    /// eligible for another claim or offer.
    /// </summary>
    MonitoringSourceClaimResult ReleaseSource(MonitoringContextId contextId);

    /// <summary>
    /// Removes a monitoring context, releasing its source if it holds one and marking it
    /// <see cref="MonitoringContextState.Stopped"/>.
    /// </summary>
    MonitoringSourceClaimResult RemoveContext(MonitoringContextId contextId);

    /// <summary>
    /// Accepts a pending ambiguous-source offer, creating a new monitoring context bound to
    /// the offer's account and source.
    /// </summary>
    MonitoringSourceDecision AcceptOffer(MonitoringSourceOfferId offerId);

    /// <summary>
    /// Declines a pending ambiguous-source offer. The source remains unclaimed and is
    /// suppressed from re-offering until it becomes inactive/unavailable and later returns to
    /// growing (or until explicit reconsideration).
    /// </summary>
    MonitoringSourceDecision DeclineOffer(MonitoringSourceOfferId offerId);

    /// <summary>
    /// Clears any decline suppression for <paramref name="sourceId"/> and re-evaluates it for a
    /// fresh offer if it is currently an eligible unclaimed growing source.
    /// </summary>
    MonitoringSourceDecision ReconsiderSource(LogSourceId sourceId);

    /// <summary>
    /// Clears all live monitoring contexts after a runtime-generation boundary.
    /// When <paramref name="observedZeroClientCountAfterNonZero"/> is true, enrollment cutoff is
    /// advanced so stale logs from the prior process instance cannot resurrect.
    /// </summary>
    void ResetForNewRuntimeGeneration(bool observedZeroClientCountAfterNonZero = true);
}
