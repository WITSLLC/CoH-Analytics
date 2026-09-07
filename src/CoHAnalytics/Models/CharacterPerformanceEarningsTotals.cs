namespace CoHAnalytics.Models;

/// <summary>
/// Session-scope cumulative earnings ingredients used for historical performance projection.
/// </summary>
public sealed record CharacterPerformanceEarningsTotals
{
    public DateTimeOffset? StartedAtUtc { get; init; }

    public long ExperienceGained { get; init; }

    public long GameplayInfluenceGained { get; init; }

    public static CharacterPerformanceEarningsTotals FromGameplaySessionSnapshot(
        GameplaySessionSnapshot snapshot) =>
        new()
        {
            StartedAtUtc = snapshot.StartedAt.ToUniversalTime(),
            ExperienceGained = snapshot.SessionExperienceGained,
            GameplayInfluenceGained = snapshot.SessionGameplayInfluenceGained
        };
}
