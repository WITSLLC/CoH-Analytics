namespace CoHAnalytics.Models;

/// <summary>
/// The latest authoritative change to a monitoring context's logical source binding.
/// </summary>
/// <remarks>
/// This vocabulary is intentionally narrower than <see cref="MonitoringContextChangeReason"/>:
/// source loss, recovery, runtime suspension, and runtime resumption do not replace or reset a
/// binding and therefore never appear here. Future parser workers use this value together with
/// <c>SourceBindingGeneration</c> to decide when a logical source segment changed.
/// </remarks>
public enum MonitoringSourceTransitionKind
{
    /// <summary>No source-binding transition has occurred.</summary>
    None,

    /// <summary>A source was assigned to a previously unbound context.</summary>
    SourceAssigned,

    /// <summary>The current source was explicitly released.</summary>
    SourceReleased,

    /// <summary>A previously released source was assigned to the same context again.</summary>
    SourceReclaimed,

    /// <summary>The context switched to a newer same-account daily source.</summary>
    AutomaticRollover,

    /// <summary>The manager approved resetting the parser cursor for a truncated source.</summary>
    TruncationReset,

    /// <summary>The manager approved a verified replacement identity at the same source path.</summary>
    SourceReplaced,

    /// <summary>The context was removed while it still owned a source.</summary>
    ContextRemoved
}
