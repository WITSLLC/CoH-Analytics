namespace CoHAnalytics.Models;

/// <summary>
/// Combat portion of a historical character performance observation segment.
/// </summary>
public sealed record CharacterPerformanceCombatDelta
{
    public CombatScaledAmount DamageDealt { get; init; } = CombatScaledAmount.Zero;

    public long Attempts { get; init; }

    public long Hits { get; init; }

    public long RolledAttempts { get; init; }

    public long DisplayedChanceSumHundredths { get; init; }

    public long RollSumHundredths { get; init; }

    public long ForcedHits { get; init; }

    public long Autohits { get; init; }

    public long TotalDefeated { get; init; }

    public long MyDefeats { get; init; }
}
