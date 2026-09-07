namespace CoHAnalytics.Models;

/// <summary>Domain-only lifecycle state for one per-context parser worker.</summary>
public enum ParserWorkerState
{
    /// <summary>Constructed but not yet reconciled with a monitoring context snapshot.</summary>
    Created,

    /// <summary>No readable source is currently available; any existing checkpoint is retained.</summary>
    WaitingForSource,

    /// <summary>A source is attached at its checkpoint and currently has no appended bytes.</summary>
    WaitingForData,

    /// <summary>The worker retained newly read bytes during its most recent polling pass.</summary>
    Reading,

    /// <summary>Runtime suspension paused I/O while preserving segment, cursor, and framing state.</summary>
    Suspended,

    /// <summary>A context-local I/O, decoding, transition, or delivery failure stopped this worker.</summary>
    Faulted,

    /// <summary>The worker was stopped and owns no active file handle.</summary>
    Stopped
}
