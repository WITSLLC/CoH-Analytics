namespace CoHAnalytics.Models;

public enum GameplaySessionOutcome
{
    Success,
    ServiceStopped,
    InvalidContext,
    NoActiveSession,
    CharacterNotInAccount,
    RecordNotFound,
    ReentrantCommandRejected,
    Overloaded,
    ProcessingFailed
}

/// <summary>Explicit outcome of a gameplay-session identity command.</summary>
public sealed record GameplaySessionOperationResult
{
    public required GameplaySessionOutcome Outcome { get; init; }

    public string? Detail { get; init; }

    public bool IsSuccess => Outcome == GameplaySessionOutcome.Success;

    /// <summary>
    /// True when authoritative process-exit finalization acquired a parser fence and then
    /// closed it. The worker cannot apply later Ready snapshots, so monitoring must suspend
    /// or retire the context instead of leaving it Ready.
    /// </summary>
    public bool ClosedParserFence { get; init; }

    public static GameplaySessionOperationResult Success() =>
        new() { Outcome = GameplaySessionOutcome.Success };

    public static GameplaySessionOperationResult Failure(
        GameplaySessionOutcome outcome,
        string? detail = null) =>
        new() { Outcome = outcome, Detail = detail };
}
