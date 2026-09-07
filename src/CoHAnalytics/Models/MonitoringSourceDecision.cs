namespace CoHAnalytics.Models;

/// <summary>
/// Explicit outcome of an offer-decision domain operation. Expected user-driven conflicts are
/// represented here rather than as exceptions; only programming-contract violations throw.
/// </summary>
public enum MonitoringSourceDecisionOutcome
{
    Success,
    OfferNotFound,
    OfferNotPending,
    SourceUnavailable,
    SourceAlreadyClaimed,
    ManagerNotRunning
}

/// <summary>
/// Result of <c>AcceptOffer</c>, <c>DeclineOffer</c>, or <c>ReconsiderSource</c>.
/// </summary>
public sealed record MonitoringSourceDecision
{
    public required MonitoringSourceDecisionOutcome Outcome { get; init; }

    /// <summary>Populated with the newly created context's id when acceptance succeeds.</summary>
    public MonitoringContextId? ContextId { get; init; }

    public string? Detail { get; init; }

    public bool IsSuccess => Outcome == MonitoringSourceDecisionOutcome.Success;

    public static MonitoringSourceDecision Success(MonitoringContextId? contextId = null) =>
        new() { Outcome = MonitoringSourceDecisionOutcome.Success, ContextId = contextId };

    public static MonitoringSourceDecision Failure(MonitoringSourceDecisionOutcome outcome, string? detail = null) =>
        new() { Outcome = outcome, Detail = detail };
}
