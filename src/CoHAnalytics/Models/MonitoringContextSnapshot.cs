namespace CoHAnalytics.Models;

/// <summary>
/// An immutable observation of one monitoring context at a point in time.
/// </summary>
/// <remarks>
/// This is the only externally visible shape of a monitoring context. The manager holds its own
/// internal mutable state and publishes a fresh, fully immutable instance of this record whenever
/// a context changes; callers can never observe or mutate a context in place.
/// </remarks>
public sealed record MonitoringContextSnapshot
{
    public required MonitoringContextId ContextId { get; init; }

    public required MonitoringContextState State { get; init; }

    public string? AccountStableId { get; init; }

    public string? AccountDisplayName { get; init; }

    public LogSourceId? CurrentSourceId { get; init; }

    /// <summary>The source this context claimed immediately before its current one, if any.</summary>
    public LogSourceId? PreviousSourceId { get; init; }

    /// <summary>
    /// Monotonic version of the complete authoritative binding state, including the unbound
    /// state. It advances on assignment, release, reclaim, rollover, approved reset or
    /// replacement, and bound-context removal; availability and runtime changes do not advance it.
    /// </summary>
    public long SourceBindingGeneration { get; init; }

    /// <summary>The latest authoritative logical source-binding transition.</summary>
    public MonitoringSourceTransitionKind LastSourceBindingTransitionKind { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset LastStateChangedAt { get; init; }

    public DateTimeOffset? SuspendedAt { get; init; }

    /// <summary>
    /// The state this context occupied immediately before it became <see cref="MonitoringContextState.RuntimeSuspended"/>,
    /// used to decide the safe state to resume into. Only meaningful while suspended.
    /// </summary>
    public MonitoringContextState? PreviousStateBeforeSuspension { get; init; }

    /// <summary>When the current source was claimed. Null when no source is currently claimed.</summary>
    public DateTimeOffset? SourceAssignedAt { get; init; }

    /// <summary>
    /// Earliest byte in the current source that may contain identity evidence for this runtime
    /// generation. Startup recovery must not authorize a live session from content before this
    /// boundary.
    /// </summary>
    public long StartupRecoveryStartOffset { get; init; }

    /// <summary>
    /// The single, explicitly validated previous-day source that startup identity recovery may
    /// consult when the current daily source contains no Welcome. Null for ordinary bindings.
    /// </summary>
    public LogSourceId? StartupRecoveryPredecessorSourceId { get; init; }

    /// <summary>
    /// Earliest byte in <see cref="StartupRecoveryPredecessorSourceId"/> that may contain identity
    /// evidence for the current runtime generation.
    /// </summary>
    public long StartupRecoveryPredecessorStartOffset { get; init; }

    /// <summary>When the source binding most recently transitioned in any way.</summary>
    public DateTimeOffset? LastSourceTransitionAt { get; init; }

    /// <summary>Why the source binding most recently transitioned.</summary>
    public MonitoringContextChangeReason? LastSourceTransitionReason { get; init; }

    /// <summary>
    /// When the currently claimed source was first observed as unavailable. Null while the
    /// source is available or when no source is claimed. The claim is retained while this is
    /// set, so the same source identity can recover without creating another context.
    /// </summary>
    public DateTimeOffset? SourceLostAt { get; init; }

    /// <summary>
    /// The Homecoming client process associated with this context when ownership is unambiguous.
    /// Null while runtime attribution cannot be proven, for example with multiple simultaneous clients.
    /// </summary>
    public HomecomingProcessInstance? ProcessInstance { get; init; }
}
