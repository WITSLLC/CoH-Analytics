namespace CoHAnalytics.Models;

public enum CharacterPerformanceEarningsProjectionOutcome
{
    Success,
    InvalidBaseline,
    InvalidCurrent,
    InvalidBoundary,
    CounterRegression
}

/// <summary>Outcome of projecting session earnings totals from baseline to a boundary timestamp.</summary>
public sealed record CharacterPerformanceEarningsProjectionResult
{
    public required CharacterPerformanceEarningsProjectionOutcome Outcome { get; init; }

    public CharacterPerformanceEarningsDelta? Delta { get; init; }

    public string? Detail { get; init; }

    public bool IsSuccess => Outcome == CharacterPerformanceEarningsProjectionOutcome.Success;
}
