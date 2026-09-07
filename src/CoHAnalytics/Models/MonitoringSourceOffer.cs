namespace CoHAnalytics.Models;

/// <summary>
/// Why a source could not be attributed automatically. Stable domain reasons rather than UI
/// strings, so a UI layer can present its own wording without the manager needing to know it.
/// </summary>
/// <remarks>
/// Every reason represents genuine ambiguity. Unambiguous active sources are enrolled
/// automatically and never produce an offer (§3.6.6).
/// </remarks>
public enum MonitoringSourceOfferReason
{
    /// <summary>
    /// The account is already monitored and another unclaimed source under it started growing.
    /// That may be an in-progress rollover or a second client on one account, and the source
    /// ownership invariant forbids guessing between them (§3.6.8).
    /// </summary>
    AmbiguousAccountSource,

    /// <summary>
    /// The account has no monitoring context, but automatic enrollment is still not possible:
    /// several of its sources are growing simultaneously, or the Homecoming runtime has not been
    /// confirmed available.
    /// </summary>
    AmbiguousInitialSelection
}

/// <summary>
/// An immutable, pending offer to monitor an ambiguous Homecoming chat-log source.
/// </summary>
/// <remarks>
/// An offer is purely a domain fact: "this source is unclaimed and growing, cannot be attributed
/// automatically, and needs an explicit decision." It carries no UI text; a UI layer derives its
/// own presentation from <see cref="State"/> and <see cref="Reason"/>.
/// </remarks>
public sealed record MonitoringSourceOffer
{
    public required MonitoringSourceOfferId OfferId { get; init; }

    public required LogSourceId SourceId { get; init; }

    public required string AccountStableId { get; init; }

    public required string AccountDisplayName { get; init; }

    public required DateTimeOffset DetectedAt { get; init; }

    public required MonitoringSourceOfferState State { get; init; }

    public required MonitoringSourceOfferReason Reason { get; init; }
}
