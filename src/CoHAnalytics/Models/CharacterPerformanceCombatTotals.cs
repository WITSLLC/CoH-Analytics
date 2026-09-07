namespace CoHAnalytics.Models;

/// <summary>
/// Session-scope cumulative combat ingredients used for historical performance projection.
/// </summary>
public sealed record CharacterPerformanceCombatTotals
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

    public static CharacterPerformanceCombatTotals FromCombatSnapshot(CombatSnapshot snapshot) =>
        new()
        {
            DamageDealt = snapshot.DamageDealt,
            Attempts = snapshot.Accuracy.Attempts,
            Hits = snapshot.Accuracy.Hits,
            RolledAttempts = snapshot.Accuracy.RolledAttempts,
            DisplayedChanceSumHundredths = snapshot.Accuracy.DisplayedChanceSumHundredths,
            RollSumHundredths = snapshot.Accuracy.RollSumHundredths,
            ForcedHits = snapshot.Accuracy.ForcedHits,
            Autohits = snapshot.Accuracy.Autohits,
            TotalDefeated = snapshot.TotalDefeated,
            MyDefeats = snapshot.MyDefeats
        };
}
