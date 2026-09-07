namespace CoHAnalytics.Models;

/// <summary>One normalized semantic combat event committed to a gameplay session.</summary>
public sealed record CombatEvent
{
    public required MonitoringContextId ContextId { get; init; }

    public GameplaySessionId? SessionId { get; init; }

    public required long ParserSequence { get; init; }

    public required DateTimeOffset ObservedAt { get; init; }

    public DateTime? SourceTimestamp { get; init; }

    public required CombatEventKind Kind { get; init; }

    public required CombatGrammarId GrammarId { get; init; }

    public CombatActorRole ActorRole { get; init; } = CombatActorRole.Self;

    public string? TargetName { get; init; }

    public string? SourceName { get; init; }

    public string? PowerName { get; init; }

    public CombatScaledAmount Amount { get; init; } = CombatScaledAmount.Zero;

    public string? DamageType { get; init; }

    public bool IsOverTime { get; init; }

    /// <summary>Optional trailing clause metadata such as containment markers.</summary>
    public string? EffectSuffix { get; init; }

    public CombatAttackOutcome? AttackOutcome { get; init; }

    /// <summary>Displayed chance-to-hit in hundredths of a percent (95.00% → 9500).</summary>
    public long? DisplayedChanceHundredths { get; init; }

    /// <summary>Rolled value in hundredths (51.51 → 5151).</summary>
    public long? RollHundredths { get; init; }

    public bool WasRolled { get; init; }

    public bool WasForced { get; init; }

    public bool IsAutohit { get; init; }
}
