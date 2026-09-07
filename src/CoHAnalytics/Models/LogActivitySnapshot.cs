namespace CoHAnalytics.Models;

/// <summary>
/// An immutable aggregate view of every observed chat-log source candidate.
/// </summary>
/// <remarks>
/// The snapshot is the complete, honest set of candidates. No candidate is suppressed,
/// ranked as globally primary, or marked as belonging to anything: multiple accounts and
/// multiple sources within one account may be growing simultaneously, and all of them appear
/// here so a later monitoring session manager can decide what to do with them.
/// </remarks>
public sealed record LogActivitySnapshot
{
    private LogActivitySnapshot(
        IReadOnlyList<LogSourceCandidate> candidates,
        int observedAccountCount,
        int logsFolderCount,
        DateTimeOffset observedAt,
        long revision)
    {
        Candidates = candidates;
        ObservedAccountCount = observedAccountCount;
        LogsFolderCount = logsFolderCount;
        ObservedAt = observedAt;
        Revision = revision;
    }

    /// <summary>Every candidate, in deterministic order.</summary>
    public IReadOnlyList<LogSourceCandidate> Candidates { get; }

    /// <summary>Number of discovered accounts observed during the scan.</summary>
    public int ObservedAccountCount { get; }

    /// <summary>Number of observed accounts that currently have a Logs folder.</summary>
    public int LogsFolderCount { get; }

    public DateTimeOffset ObservedAt { get; }

    /// <summary>Increments only when the snapshot is semantically different from its predecessor.</summary>
    public long Revision { get; }

    public int CandidateCount => Candidates.Count;

    public int GrowingCount => CountState(LogSourceActivityState.Growing);

    public int InactiveCount => CountState(LogSourceActivityState.Inactive);

    public int HistoricalCount => CountState(LogSourceActivityState.Historical);

    public int WaitingCount => CountState(LogSourceActivityState.Waiting);

    public int UnavailableCount => CountState(LogSourceActivityState.Unavailable);

    public int TruncatedCount => Candidates.Count(candidate => candidate.IsTruncated);

    public int ReplacedCount => Candidates.Count(candidate => candidate.IsReplaced);

    public int RolloverCandidateCount => Candidates.Count(candidate => candidate.IsRolloverCandidate);

    /// <summary>The most recent directly observed growth across all candidates, if any.</summary>
    public DateTimeOffset? LastGrowthAt
    {
        get
        {
            DateTimeOffset? latest = null;
            foreach (var candidate in Candidates)
            {
                if (candidate.LastGrowthAt is { } growth && (latest is null || growth > latest))
                {
                    latest = growth;
                }
            }

            return latest;
        }
    }

    public static LogActivitySnapshot Empty { get; } = new([], 0, 0, default, 0);

    public static LogActivitySnapshot Create(
        IEnumerable<LogSourceCandidate> candidates,
        int observedAccountCount,
        int logsFolderCount,
        DateTimeOffset observedAt,
        long revision) =>
        new(
            [.. candidates
                .OrderBy(candidate => candidate.AccountDisplayName, StringComparer.Ordinal)
                .ThenByDescending(candidate => candidate.LogDate)
                .ThenBy(candidate => candidate.FilePath, StringComparer.Ordinal)],
            observedAccountCount,
            logsFolderCount,
            observedAt,
            revision);

    /// <summary>
    /// Whether another snapshot describes the same observed state. <see cref="ObservedAt"/>
    /// and <see cref="Revision"/> are excluded, so re-observing an unchanged filesystem is
    /// not treated as a change.
    /// </summary>
    public bool IsSemanticallyEquivalentTo(LogActivitySnapshot other)
    {
        if (ObservedAccountCount != other.ObservedAccountCount
            || LogsFolderCount != other.LogsFolderCount
            || Candidates.Count != other.Candidates.Count)
        {
            return false;
        }

        for (var index = 0; index < Candidates.Count; index++)
        {
            if (!Candidates[index].IsSemanticallyEquivalentTo(other.Candidates[index]))
            {
                return false;
            }
        }

        return true;
    }

    public LogActivitySnapshot WithObservation(DateTimeOffset observedAt, long revision) =>
        new(Candidates, ObservedAccountCount, LogsFolderCount, observedAt, revision);

    private int CountState(LogSourceActivityState state) =>
        Candidates.Count(candidate => candidate.ActivityState == state);
}
