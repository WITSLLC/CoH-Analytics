namespace CoHAnalytics.Models;

public enum CharacterPerformanceCombatProjectionOutcome
{
    Success,
    InvalidBaseline,
    InvalidCurrent,
    CounterRegression,
    InvalidDelta
}

/// <summary>Outcome of projecting session combat totals from baseline to current.</summary>
public sealed record CharacterPerformanceCombatProjectionResult
{
    public required CharacterPerformanceCombatProjectionOutcome Outcome { get; init; }

    public CharacterPerformanceCombatDelta? Delta { get; init; }

    public string? Detail { get; init; }

    public bool IsSuccess => Outcome == CharacterPerformanceCombatProjectionOutcome.Success;
}
