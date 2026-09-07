namespace CoHAnalytics.Orchestration.Models;

public sealed record ApplicationContributorDescriptor(
    string ProviderId,
    string DisplayName,
    IReadOnlyCollection<string> Produces,
    IReadOnlyCollection<string> Requires,
    IReadOnlyCollection<string> Optional,
    ApplicationContributorImportance Importance,
    int Priority,
    TimeSpan? MaxAge = null,
    TimeSpan? PullTimeout = null)
{
    public const int ExpectedSchemaVersion = 1;

    public int SchemaVersion { get; init; } = ExpectedSchemaVersion;

    public string? Description { get; init; }

    public string? Version { get; init; }

    public string? IconKey { get; init; }

    public IReadOnlyCollection<string> Consumes =>
        [.. Requires, .. Optional];
}
