namespace CoHAnalytics.Models;

/// <summary>Lightweight per-session drop counts by classified reward category.</summary>
public sealed record GameplaySessionRewardCategoryCounts
{
    public long SalvageDropCount { get; init; }

    public long RecipeDropCount { get; init; }

    public long EnhancementDropCount { get; init; }

    public long InspirationDropCount { get; init; }
}
