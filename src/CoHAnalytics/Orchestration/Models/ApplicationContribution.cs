namespace CoHAnalytics.Orchestration.Models;

public sealed record ApplicationContribution(
    string ProviderId,
    ContributorHealth Health,
    ContributorActivity Activity,
    IReadOnlyCollection<ApplicationFact> Facts,
    IReadOnlyCollection<ApplicationIssue> Issues,
    IReadOnlyCollection<ApplicationAction> Actions,
    DateTimeOffset ObservedAt)
{
    public TimeSpan? ValidFor { get; init; }

    public string? ComponentVersion { get; init; }

    public string? SourceDescription { get; init; }

    public DateTimeOffset? ExpiresAt =>
        ValidFor is { } validFor ? ObservedAt + validFor : null;

    public static ApplicationContribution Empty(string providerId, DateTimeOffset observedAt) =>
        new(
            providerId,
            ContributorHealth.Unknown,
            ContributorActivity.Inactive,
            Array.Empty<ApplicationFact>(),
            Array.Empty<ApplicationIssue>(),
            Array.Empty<ApplicationAction>(),
            observedAt);

    public ApplicationContribution WithImmutableCollections() =>
        this with
        {
            Facts = Facts.ToArray(),
            Issues = Issues.Select(issue => issue with
            {
                ActionIds = issue.ActionIds.ToArray()
            }).ToArray(),
            Actions = Actions.ToArray()
        };
}
