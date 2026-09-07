namespace CoHAnalytics.Models;

/// <summary>
/// The most recent observed transition for a candidate chat-log source.
/// </summary>
public enum LogSourceChangeKind
{
    /// <summary>No observation has been recorded yet.</summary>
    None,

    /// <summary>The file was present when the source was first observed in this run.</summary>
    Discovered,

    /// <summary>The file appeared after observation of its Logs folder had begun.</summary>
    Created,

    /// <summary>The observed length increased.</summary>
    Grew,

    /// <summary>Metadata was identical to the previous observation.</summary>
    Unchanged,

    /// <summary>The observed length decreased while the file still exists.</summary>
    Truncated,

    /// <summary>The path now refers to a different underlying file.</summary>
    Replaced,

    /// <summary>The file no longer exists at its observed path.</summary>
    Disappeared,

    /// <summary>File metadata could not be read.</summary>
    Inaccessible
}
