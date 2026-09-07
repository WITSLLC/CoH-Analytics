namespace CoHAnalytics.Models;

/// <summary>Accumulated quantity for one recognized reward currency during a session.</summary>
public sealed record GameplaySessionRewardCurrencyTotal
{
    public required string CurrencyDisplayName { get; init; }

    public long Quantity { get; init; }
}
