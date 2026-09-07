namespace CoHAnalytics.Services.Diagnostics;

public enum DiagnosticLogStreamState
{
    Initializing,
    Active,
    Stopping,
    Stopped,
    Degraded
}

/// <summary>Immutable in-memory health and queue-pressure snapshot for the Standard log stream.</summary>
public sealed record DiagnosticLogStatus
{
    public required DiagnosticLogStreamState StreamState { get; init; }

    public required string ActivePath { get; init; }

    public required Guid ApplicationRunId { get; init; }

    public required int QueueDepth { get; init; }

    public required int PeakQueueDepth { get; init; }

    public required long WrittenCount { get; init; }

    public required long DroppedCount { get; init; }

    public DateTimeOffset? LastSuccessfulWriteAtUtc { get; init; }

    public string? LastFailureCode { get; init; }

    public string? LastFailureType { get; init; }

    public int? LastFailureHResult { get; init; }

    public DateTimeOffset? LastFailureAtUtc { get; init; }
}
