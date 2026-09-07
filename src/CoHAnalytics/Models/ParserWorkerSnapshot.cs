namespace CoHAnalytics.Models;

/// <summary>Immutable observation of one context's parser worker.</summary>
public sealed record ParserWorkerSnapshot
{
    public required ParserWorkerId WorkerId { get; init; }

    public required MonitoringContextId ContextId { get; init; }

    public required ParserWorkerState State { get; init; }

    public LogSourceId? CurrentSourceId { get; init; }

    public required long AppliedSourceBindingGeneration { get; init; }

    public required MonitoringSourceTransitionKind LastAppliedTransitionKind { get; init; }

    public ParserSourceSegmentSnapshot? CurrentSegment { get; init; }

    public required IReadOnlyList<ParserSourceSegmentSnapshot> RecentSegments { get; init; }

    public ParserReadCheckpoint? Checkpoint { get; init; }

    public required long TotalBytesRead { get; init; }

    public required long TotalLinesEmitted { get; init; }

    public required long LastEventSequence { get; init; }

    public DateTimeOffset? LastEventAt { get; init; }

    public string? FaultCode { get; init; }

    public string? FaultMessage { get; init; }
}
