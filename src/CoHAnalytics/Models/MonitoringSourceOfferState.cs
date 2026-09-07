namespace CoHAnalytics.Models;

/// <summary>
/// Domain-only state for one additional-session offer. Never added to <c>ContributorHealth</c>,
/// <c>ContributorActivity</c>, or any other orchestrator-normalized enum.
/// </summary>
/// <remarks>
/// There is deliberately no <c>Expired</c> value in this slice: offers are never time-limited,
/// only invalidated by a concrete observed event (source lost, claimed elsewhere) or an explicit
/// user decision. If a future slice adds time-based offer expiry, that value can be introduced
/// then without disturbing the others.
/// </remarks>
public enum MonitoringSourceOfferState
{
    /// <summary>Awaiting a user decision. The only state included in the manager snapshot.</summary>
    Pending,

    /// <summary>Accepted; a new monitoring context now claims the source.</summary>
    Accepted,

    /// <summary>Declined; the source remains unclaimed and is suppressed from re-offering.</summary>
    Declined,

    /// <summary>Invalidated because the source disappeared or became unreadable while pending.</summary>
    SourceUnavailable,

    /// <summary>Invalidated because something else claimed the source while pending.</summary>
    ClaimedElsewhere,

    /// <summary>Withdrawn without a user decision (reserved for future programmatic cancellation).</summary>
    Cancelled
}
