namespace CoHAnalytics.Orchestration.Models;

public sealed record ApplicationIssue(
    string Code,
    ApplicationIssueSeverity Severity,
    string Summary,
    string ProviderId,
    DateTimeOffset CreatedAt)
{
    public string? Detail { get; init; }

    public bool RequiresUserAction { get; init; }

    public string? RelatedEntityId { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }

    public DateTimeOffset? ExpiresAt { get; init; }

    public IReadOnlyCollection<string> ActionIds { get; init; } = Array.Empty<string>();

    public string DedupeKey => RelatedEntityId is null
        ? $"{ProviderId}/{Code}"
        : $"{ProviderId}/{Code}/{RelatedEntityId}";
}
