namespace CoHAnalytics.Models;

/// <summary>
/// Immutable aggregate performance evidence observed for one character during one contiguous
/// segment of an authoritative gameplay session.
/// </summary>
public sealed record CharacterPerformanceObservation
{
    public const int CurrentSchemaVersion = 2;

    public const int MinimumSupportedSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary>
    /// Whether this retained observation contributes to historical Overview aggregates.
    /// </summary>
    public bool IncludeInOverview { get; init; } = true;

    public required GameplaySessionId GameplaySessionId { get; init; }

    public required int SegmentOrdinal { get; init; }

    public required CharacterRecordId CharacterRecordId { get; init; }

    public required DateTimeOffset StartedAtUtc { get; init; }

    public required DateTimeOffset EndedAtUtc { get; init; }

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

    public long ExperienceGained { get; init; }

    public long GameplayInfluenceGained { get; init; }

    /// <summary>Derived observation duration. Duration is never persisted separately.</summary>
    public TimeSpan ObservedDuration =>
        EndedAtUtc <= StartedAtUtc ? TimeSpan.Zero : EndedAtUtc - StartedAtUtc;

    /// <summary>Derived miss count under the current two-outcome accuracy contract.</summary>
    public long Misses => Attempts - Hits;
}
