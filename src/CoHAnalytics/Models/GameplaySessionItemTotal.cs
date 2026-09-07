namespace CoHAnalytics.Models;

/// <summary>Accumulated quantity for one recognized item name during a gameplay session.</summary>
public sealed record GameplaySessionItemTotal
{
    public required string DisplayName { get; init; }

    public long Quantity { get; init; }
}
