namespace CoHAnalytics.Models;

/// <summary>
/// First-class player-versus-pet split for analytical cubes. Per-pet rows roll up by
/// normalized name; instance ordinals stay unresolved.
/// </summary>
public enum CombatAnalyticsScope
{
    Self = 0,
    OwnPetsAggregate = 1,
    PerPet = 2
}

public enum CombatAnalyticsDirection
{
    Outgoing,
    Incoming
}
