namespace CoHAnalytics.Services;

/// <summary>Bounded privacy-safe diagnostics for gameplay-session processing.</summary>
public sealed class GameplaySessionDiagnostics
{
    public required bool IsRunning { get; init; }

    public required long SnapshotRevision { get; init; }

    public required int ActiveSessionCount { get; init; }

    public required int SuspendedSessionCount { get; init; }

    public required int NeedsAttentionSessionCount { get; init; }

    public required int OverflowedSessionCount { get; init; }

    public required long TotalCommittedEvents { get; init; }

    public DateTimeOffset? LastCommittedEventAt { get; init; }

    public required int FailedContextCount { get; init; }

    public required int PendingCommittedEventCount { get; init; }

    public required long LifecycleEpoch { get; init; }

    public required QueuePressureDiagnostics WorkQueue { get; init; }

    public required PendingCommittedEventDiagnostics PendingCommittedEvents { get; init; }

    public required bool WorkQueueOverflowed { get; init; }

    public required bool PreStartBufferOverflowed { get; init; }

    public required long LastAcceptedWorkSequence { get; init; }

    public required long LastCompletedWorkSequence { get; init; }

    public required int ActiveProcessorCallbackCount { get; init; }

    public required IReadOnlyList<string> RecentOperations { get; init; }

    public bool IsQuiescent =>
        LastAcceptedWorkSequence == LastCompletedWorkSequence
        && ActiveProcessorCallbackCount == 0
        && PendingCommittedEventCount == 0;
}
