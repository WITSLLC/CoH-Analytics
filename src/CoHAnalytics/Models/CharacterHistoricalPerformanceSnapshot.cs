namespace CoHAnalytics.Models;

/// <summary>
/// Immutable lifetime performance summary derived from durable observations for one character.
/// </summary>
public sealed record CharacterHistoricalPerformanceSnapshot
{
    public required CharacterRecordId CharacterRecordId { get; init; }

    /// <summary>Number of valid historical observation segments included in this summary.</summary>
    public long ObservationCount { get; init; }

    public bool HasHistory => ObservationCount > 0;

    public TimeSpan ObservedDuration { get; init; }

    public CombatScaledAmount DamageDealt { get; init; } = CombatScaledAmount.Zero;

    /// <summary>Damage per second in hundredths, matching live session combat representation.</summary>
    public long? DamagePerSecondHundredths { get; init; }

    public double? DamagePerSecond => DamagePerSecondHundredths / (double)CombatScaledAmount.Scale;

    public CombatAccuracyScopeSnapshot Accuracy { get; init; } = CombatAccuracyScopeSnapshot.Empty;

    public long Attempts => Accuracy.Attempts;

    public long Hits => Accuracy.Hits;

    public long Misses => Accuracy.Misses;

    public long RolledAttempts => Accuracy.RolledAttempts;

    public long DisplayedChanceSumHundredths => Accuracy.DisplayedChanceSumHundredths;

    public long RollSumHundredths => Accuracy.RollSumHundredths;

    public long ForcedHits => Accuracy.ForcedHits;

    public long Autohits => Accuracy.Autohits;

    /// <summary>Hit percentage rounded to the same one-decimal precision as combat presentation.</summary>
    public decimal? HitPercent { get; init; }

    /// <summary>Average displayed hit chance, or null when no rolled attempts were observed.</summary>
    public decimal? AverageDisplayedChance { get; init; }

    /// <summary>Average raw hit roll, or null when no rolled attempts were observed.</summary>
    public decimal? AverageRoll { get; init; }

    public long TotalDefeated { get; init; }

    public long MyDefeats { get; init; }

    public long ExperienceGained { get; init; }

    public double? ExperiencePerHour { get; init; }

    public long GameplayInfluenceGained { get; init; }

    public double? GameplayInfluencePerHour { get; init; }

    public static CharacterHistoricalPerformanceSnapshot Empty(CharacterRecordId characterRecordId) =>
        new() { CharacterRecordId = characterRecordId };
}
