namespace CoHAnalytics.Models;

/// <summary>
/// Explicit outcome of a source-claim domain operation. Expected user-driven conflicts are
/// represented here rather than as exceptions; only programming-contract violations throw.
/// </summary>
public enum MonitoringSourceClaimOutcome
{
    Success,
    ContextNotFound,
    SourceNotFound,
    SourceAlreadyClaimed,
    ContextAlreadyHasSource,
    SourceUnavailable,
    InvalidAccountBinding,
    ManagerNotRunning
}

/// <summary>
/// Result of <c>ClaimSource</c>, <c>ReleaseSource</c>, or <c>RemoveContext</c>.
/// </summary>
public sealed record MonitoringSourceClaimResult
{
    public required MonitoringSourceClaimOutcome Outcome { get; init; }

    public string? Detail { get; init; }

    public bool IsSuccess => Outcome == MonitoringSourceClaimOutcome.Success;

    public static MonitoringSourceClaimResult Success() =>
        new() { Outcome = MonitoringSourceClaimOutcome.Success };

    public static MonitoringSourceClaimResult Failure(MonitoringSourceClaimOutcome outcome, string? detail = null) =>
        new() { Outcome = outcome, Detail = detail };
}
