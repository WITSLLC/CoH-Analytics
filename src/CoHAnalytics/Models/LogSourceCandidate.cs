namespace CoHAnalytics.Models;

/// <summary>
/// An immutable observation of one candidate Homecoming chat-log source.
/// </summary>
/// <remarks>
/// A candidate describes only what has been observed about a file's metadata. It deliberately
/// carries no notion of ownership, assignment, selection, character, or gameplay session:
/// deciding which source a monitoring context should consume belongs to the future
/// monitoring session manager, not to log activity observation.
/// </remarks>
public sealed record LogSourceCandidate
{
    public required LogSourceId SourceId { get; init; }

    public required string AccountStableId { get; init; }

    public required string AccountDisplayName { get; init; }

    /// <summary>Normalized absolute path of the candidate file.</summary>
    public required string FilePath { get; init; }

    public required string FileName { get; init; }

    public required DateOnly LogDate { get; init; }

    /// <summary>Whether the file was present at the most recent observation.</summary>
    public required bool Exists { get; init; }

    /// <summary>Length in bytes at the most recent observation.</summary>
    public required long Length { get; init; }

    /// <summary>Length in bytes at the previous observation.</summary>
    public required long PreviousLength { get; init; }

    public DateTimeOffset? LastWriteTime { get; init; }

    /// <summary>Creation timestamp, retained as replacement evidence only.</summary>
    public DateTimeOffset? CreationTime { get; init; }

    public required DateTimeOffset FirstObservedAt { get; init; }

    public required DateTimeOffset LastObservedAt { get; init; }

    /// <summary>When growth was first observed during the current run, if ever.</summary>
    public DateTimeOffset? FirstGrowthAt { get; init; }

    /// <summary>When growth was most recently observed during the current run, if ever.</summary>
    public DateTimeOffset? LastGrowthAt { get; init; }

    public required LogSourceActivityState ActivityState { get; init; }

    public required LogSourceChangeKind LastChangeKind { get; init; }

    /// <summary>Whether growth has been directly observed at any point in the current run.</summary>
    public bool HasObservedGrowth => FirstGrowthAt is not null;

    /// <summary>Whether the file's log date is the current local date.</summary>
    public required bool IsCurrentDailyFile { get; init; }

    /// <summary>
    /// Whether this source looks like a daily rollover of an earlier source in the same
    /// account. Reporting a rollover candidate is not a claim that the character is
    /// unchanged or that a gameplay session should continue.
    /// </summary>
    public bool IsRolloverCandidate { get; init; }

    /// <summary>Source id of the older same-account source this appears to roll over from.</summary>
    public string? RolloverPredecessorSourceId { get; init; }

    /// <summary>Human-readable reasoning for the rollover-candidate assessment.</summary>
    public string? RolloverReason { get; init; }

    /// <summary>Set once a length decrease has been observed during the current run.</summary>
    public bool IsTruncated { get; init; }

    /// <summary>Set once replacement has been reliably detected during the current run.</summary>
    public bool IsReplaced { get; init; }

    /// <summary>The evidence that justified <see cref="IsReplaced"/>, kept separate from truncation.</summary>
    public string? ReplacementEvidence { get; init; }

    /// <summary>Why the source could not be observed, when it is unavailable.</summary>
    public string? UnavailableReason { get; init; }

    /// <summary>
    /// Compares two observations of the same source for semantic equivalence, ignoring
    /// <see cref="LastObservedAt"/> so that repeated no-change scans do not churn revisions.
    /// </summary>
    public bool IsSemanticallyEquivalentTo(LogSourceCandidate other) =>
        SourceId == other.SourceId
        && Exists == other.Exists
        && Length == other.Length
        && PreviousLength == other.PreviousLength
        && LastWriteTime == other.LastWriteTime
        && CreationTime == other.CreationTime
        && FirstObservedAt == other.FirstObservedAt
        && FirstGrowthAt == other.FirstGrowthAt
        && LastGrowthAt == other.LastGrowthAt
        && ActivityState == other.ActivityState
        && LastChangeKind == other.LastChangeKind
        && IsCurrentDailyFile == other.IsCurrentDailyFile
        && IsRolloverCandidate == other.IsRolloverCandidate
        && string.Equals(RolloverPredecessorSourceId, other.RolloverPredecessorSourceId, StringComparison.Ordinal)
        && string.Equals(RolloverReason, other.RolloverReason, StringComparison.Ordinal)
        && IsTruncated == other.IsTruncated
        && IsReplaced == other.IsReplaced
        && string.Equals(ReplacementEvidence, other.ReplacementEvidence, StringComparison.Ordinal)
        && string.Equals(UnavailableReason, other.UnavailableReason, StringComparison.Ordinal);
}
