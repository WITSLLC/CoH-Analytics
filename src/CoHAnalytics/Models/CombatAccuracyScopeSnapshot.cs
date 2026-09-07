namespace CoHAnalytics.Models;

/// <summary>Immutable attack-resolution accuracy counters for a combat scope.</summary>
public sealed record CombatAccuracyScopeSnapshot
{
    public static CombatAccuracyScopeSnapshot Empty { get; } = new();

    public long Attempts { get; init; }

    public long Hits { get; init; }

    public long Misses { get; init; }

    public long RolledAttempts { get; init; }

    public long DisplayedChanceSumHundredths { get; init; }

    public long RollSumHundredths { get; init; }

    public long ForcedHits { get; init; }

    public long Autohits { get; init; }

    public bool HasAttempts => Attempts > 0;

    public bool HasRolledAttempts => RolledAttempts > 0;
}
