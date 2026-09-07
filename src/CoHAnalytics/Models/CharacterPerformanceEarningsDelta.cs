namespace CoHAnalytics.Models;

/// <summary>
/// Earnings and timing portion of a historical character performance observation segment.
/// </summary>
public sealed record CharacterPerformanceEarningsDelta
{
    public required DateTimeOffset StartedAtUtc { get; init; }

    public required DateTimeOffset EndedAtUtc { get; init; }

    public long ExperienceGained { get; init; }

    public long GameplayInfluenceGained { get; init; }

    public TimeSpan ObservedDuration =>
        EndedAtUtc <= StartedAtUtc ? TimeSpan.Zero : EndedAtUtc - StartedAtUtc;
}
