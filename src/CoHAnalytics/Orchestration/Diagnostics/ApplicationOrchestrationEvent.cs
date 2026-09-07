namespace CoHAnalytics.Orchestration.Diagnostics;

public enum ApplicationOrchestrationEventKind
{
    ContributorRegistered,
    ContributorLifecycleChanged,
    ContributorFaulted,
    ContributionInvalidated,
    ContributionPulled,
    ContributionFailed,
    ContributionTimedOut,
    ContributionMarkedStale,
    DependencyUnresolved,
    RefreshStarted,
    RefreshCompleted,
    SnapshotPublished,
    SnapshotSuppressed,
    PrimaryIssueSelected,
    OverallStateSelected,
    ActionInvocationFailed
}

public sealed record ApplicationOrchestrationEvent(
    long Sequence,
    DateTimeOffset Timestamp,
    ApplicationOrchestrationEventKind Kind,
    string Summary)
{
    public string? ProviderId { get; init; }

    public string? RelatedCapabilityId { get; init; }

    public long? SnapshotRevision { get; init; }

    public string? Detail { get; init; }
}
