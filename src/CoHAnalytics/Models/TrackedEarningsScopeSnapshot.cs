namespace CoHAnalytics.Models;

/// <summary>Immutable tracked-scope earnings projection for one gameplay session.</summary>
public sealed record TrackedEarningsScopeSnapshot
{
    public static TrackedEarningsScopeSnapshot Empty { get; } = new();

    public bool IsTracking { get; init; }

    public bool IsPaused { get; init; }

    public DateTimeOffset? StartedAt { get; init; }

    public TimeSpan ActiveElapsed { get; init; }

    public long ExperienceGained { get; init; }

    public long InfluenceGained { get; init; }
}
