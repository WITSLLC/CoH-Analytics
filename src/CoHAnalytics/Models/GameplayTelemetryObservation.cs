namespace CoHAnalytics.Models;

/// <summary>
/// Research-backed gameplay telemetry grammar identifiers derived from Homecoming reward chat grammar.
/// </summary>
public enum GameplayTelemetryGrammarId
{
    Xp01ExperienceAndInfluence,
    Xp02ExperienceDebtAndInfluence,
    Xp03ExperienceOnly,
    Xp04ExperienceAndDebt,
    Inf01GameplayInfluenceOnly,
    Recv01GenericReceivedItem,
    Cur01RewardMeritStandalone,
    Chr01CharacterLevelImprovement,
    Oth02BadgeEarned
}

/// <summary>
/// Parsed gameplay telemetry amounts from one committed log line.
/// </summary>
public sealed record GameplayTelemetryObservation
{
    public required GameplayTelemetryGrammarId GrammarId { get; init; }

    public long ExperienceGained { get; init; }

    public long GameplayInfluenceGained { get; init; }

    public long DebtWorkedOff { get; init; }

    public GameplaySessionRewardCategory RewardCategory { get; init; } = GameplaySessionRewardCategory.None;

    public string? RewardDisplayName { get; init; }

    public long RewardQuantity { get; init; }

    public string? ReceivedItemText { get; init; }

    public int? CharacterObservedLevel { get; init; }

    public BadgeAcquiredEvent? BadgeAcquisition { get; init; }
}
