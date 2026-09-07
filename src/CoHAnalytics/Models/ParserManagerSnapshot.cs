namespace CoHAnalytics.Models;

/// <summary>Immutable aggregate parser state. Raw chat lines are intentionally excluded.</summary>
public sealed record ParserManagerSnapshot
{
    private ParserManagerSnapshot(
        IReadOnlyList<ParserWorkerSnapshot> workers,
        DateTimeOffset? lastEventAt,
        DateTimeOffset observedAt,
        long revision)
    {
        Workers = workers;
        LastEventAt = lastEventAt;
        ObservedAt = observedAt;
        Revision = revision;
    }

    public IReadOnlyList<ParserWorkerSnapshot> Workers { get; }

    public int WorkerCount => Workers.Count;

    public int ReadingCount => Workers.Count(worker => worker.State == ParserWorkerState.Reading);

    public int WaitingCount => Workers.Count(worker =>
        worker.State is ParserWorkerState.WaitingForSource or ParserWorkerState.WaitingForData);

    public int SuspendedCount => Workers.Count(worker => worker.State == ParserWorkerState.Suspended);

    public int FaultedCount => Workers.Count(worker => worker.State == ParserWorkerState.Faulted);

    public long TotalBytesRead => Workers.Sum(worker => worker.TotalBytesRead);

    public long TotalLinesProcessed => Workers.Sum(worker => worker.TotalLinesEmitted);

    public DateTimeOffset? LastEventAt { get; }

    public DateTimeOffset ObservedAt { get; }

    public long Revision { get; }

    public static ParserManagerSnapshot Empty { get; } = new([], null, default, 0);

    public static ParserManagerSnapshot Create(
        IEnumerable<ParserWorkerSnapshot> workers,
        DateTimeOffset? lastEventAt,
        DateTimeOffset observedAt,
        long revision) =>
        new(
            [.. workers.OrderBy(worker => worker.ContextId.ToString(), StringComparer.Ordinal)],
            lastEventAt,
            observedAt,
            revision);

    public bool IsSemanticallyEquivalentTo(ParserManagerSnapshot other) =>
        LastEventAt == other.LastEventAt && Workers.SequenceEqual(other.Workers);
}
