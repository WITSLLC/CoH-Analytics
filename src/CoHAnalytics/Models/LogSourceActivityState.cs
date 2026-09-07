namespace CoHAnalytics.Models;

/// <summary>
/// Observed activity of a candidate chat-log source. These values describe what the
/// application has directly observed about a file during the current run. None of them
/// states whether in-game chat logging is enabled or disabled.
/// </summary>
public enum LogSourceActivityState
{
    /// <summary>Default before the source has been observed. Never published in a snapshot.</summary>
    Unknown,

    /// <summary>
    /// The file exists and is dated earlier than today, and no growth has been observed
    /// during the current application run.
    /// </summary>
    Historical,

    /// <summary>
    /// The file exists and is dated today, and no growth has been observed during the
    /// current application run. It may become active.
    /// </summary>
    Waiting,

    /// <summary>The file length increased between two observations in the current run.</summary>
    Growing,

    /// <summary>
    /// The file grew earlier in the current run, and no growth has been observed since
    /// beyond the configured inactivity threshold. This does not prove logging stopped.
    /// </summary>
    Inactive,

    /// <summary>The file still exists and its length decreased.</summary>
    Truncated,

    /// <summary>The path reliably refers to a different underlying file than before.</summary>
    Replaced,

    /// <summary>The file disappeared or could not be accessed during the latest scan.</summary>
    Unavailable
}
