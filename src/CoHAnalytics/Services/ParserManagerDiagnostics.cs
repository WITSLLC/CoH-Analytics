using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

/// <summary>Bounded parser diagnostics. Raw lines and absolute paths are never included.</summary>
public sealed record ParserManagerDiagnostics
{
    public required bool IsRunning { get; init; }

    public required long LastMonitoringSnapshotRevision { get; init; }

    public required long LastParserSnapshotRevision { get; init; }

    public required int QueuedEventCount { get; init; }

    public required long LifecycleEpoch { get; init; }

    public required QueuePressureDiagnostics MonitoringSnapshotQueue { get; init; }

    public required QueuePressureDiagnostics EventQueue { get; init; }

    public QueuePressureInvariantClassification EventQueueInvariant => EventQueue.ClassifyInvariant();

    public QueuePressureInvariantClassification MonitoringSnapshotQueueInvariant =>
        MonitoringSnapshotQueue.ClassifyInvariant();

    public required bool MonitoringSnapshotQueueOverflowed { get; init; }

    public required bool EventQueueOverflowed { get; init; }

    public required ParserClassificationDiagnostics Classification { get; init; }

    public required IReadOnlyList<ParserWorkerDiagnostics> Workers { get; init; }

    public required IReadOnlyList<string> RecentDecisions { get; init; }
}

public sealed record ParserWorkerDiagnostics
{
    public required string WorkerId { get; init; }

    public required string ContextId { get; init; }

    public string? SourceOpaqueId { get; init; }

    public string? SourceFileName { get; init; }

    public required ParserWorkerState State { get; init; }

    public required long AppliedBindingGeneration { get; init; }

    public required MonitoringSourceTransitionKind TransitionKind { get; init; }

    public string? SegmentId { get; init; }

    public long? StartingOffset { get; init; }

    public long? CurrentOffset { get; init; }

    public required long BytesRead { get; init; }

    public required long LinesEmitted { get; init; }

    public required int PartialBufferByteCount { get; init; }

    public required string DecoderState { get; init; }

    public string? FaultCode { get; init; }
}
