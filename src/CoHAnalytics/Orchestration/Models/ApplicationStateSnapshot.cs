namespace CoHAnalytics.Orchestration.Models;

public sealed record ProviderSummary(
    string ProviderId,
    string DisplayName,
    ContributorHealth Health,
    ContributorActivity Activity,
    ApplicationContributorLifecycleState LifecycleState,
    DateTimeOffset ObservedAt)
{
    public bool IsStale { get; init; }

    public ApplicationContributorImportance Importance { get; init; }

    public int IssueCount { get; init; }

    public ApplicationIssueSeverity? HighestSeverity { get; init; }

    public IReadOnlyCollection<string> UnmetRequiredCapabilities { get; init; } = Array.Empty<string>();
}

public sealed record ApplicationStateSnapshot(
    OverallApplicationState State,
    IReadOnlyCollection<ProviderSummary> Providers,
    IReadOnlyCollection<ApplicationIssue> ActiveIssues,
    ApplicationIssue? PrimaryIssue,
    IReadOnlyCollection<ApplicationAction> SuggestedActions,
    DateTimeOffset UpdatedAt)
{
    public IReadOnlyCollection<ApplicationFact> Facts { get; init; } = Array.Empty<ApplicationFact>();

    public string? StateSummary { get; init; }

    public SnapshotGenerationReason Reason { get; init; }

    public long Revision { get; init; }

    public static ApplicationStateSnapshot Empty(DateTimeOffset updatedAt) =>
        new(
            OverallApplicationState.Unknown,
            Array.Empty<ProviderSummary>(),
            Array.Empty<ApplicationIssue>(),
            PrimaryIssue: null,
            Array.Empty<ApplicationAction>(),
            updatedAt)
        {
            Reason = SnapshotGenerationReason.Initial,
            Revision = 0
        };
}

public sealed class ApplicationStateChangedEventArgs(ApplicationStateSnapshot snapshot) : EventArgs
{
    public ApplicationStateSnapshot Snapshot { get; } = snapshot;
}
