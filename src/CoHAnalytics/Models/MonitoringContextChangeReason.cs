namespace CoHAnalytics.Models;

/// <summary>
/// Why a monitoring context's current source claim most recently changed. Serves both as the
/// reason a current claim was made and as the context's <c>LastSourceTransitionReason</c>,
/// since those are the same fact at any point in time: whatever last changed the claim is the
/// reason the claim (or its absence) currently holds.
/// </summary>
public enum MonitoringContextChangeReason
{
    /// <summary>No source transition has occurred yet.</summary>
    None,

    /// <summary>Supplied directly through the internal test/dev context-creation seam.</summary>
    ManualSeed,

    /// <summary>Assigned through the explicit <c>ClaimSource</c> domain API.</summary>
    ManualClaim,

    /// <summary>
    /// Automatically claimed because the account had exactly one unclaimed growing source and no
    /// existing monitoring context.
    /// </summary>
    AutomaticUnambiguousSelection,

    /// <summary>Claimed because a pending ambiguous-source offer was accepted.</summary>
    AcceptedAdditionalSessionOffer,

    /// <summary>Silently switched to a new same-account daily log file.</summary>
    AutomaticRollover,

    /// <summary>Approved a parser reset after the currently assigned file was truncated.</summary>
    TruncationReset,

    /// <summary>Switched to the verified successor identity for a replaced file at the same path.</summary>
    SourceReplaced,

    /// <summary>Released through the explicit <c>ReleaseSource</c> domain API.</summary>
    SourceReleased,

    /// <summary>The claimed source became unavailable while the claim was retained.</summary>
    SourceLost,

    /// <summary>A previously lost source became observable again under the same identity.</summary>
    SourceRecovered,

    /// <summary>The owning context was removed through the explicit <c>RemoveContext</c> API.</summary>
    ContextRemoved,

    /// <summary>
    /// Retired because a different account became the sole active source on the same
    /// Homecoming process instance.
    /// </summary>
    SameProcessAccountHandoffRetired
}
