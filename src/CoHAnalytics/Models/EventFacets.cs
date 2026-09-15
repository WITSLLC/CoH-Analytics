namespace CoHAnalytics.Models;

/// <summary>
/// Analytically valid perspectives on one logical event. Normalization assigns a single
/// family facet. Proven mirror collapse unions directional facets on the survivor.
/// </summary>
[Flags]
public enum EventFacets
{
    None = 0,
    DamageDealt = 1,
    DamageReceived = 2,
    HealDelivered = 4,
    HealReceived = 8,
    AttackResolution = 16,
    Activation = 32,
    Defeat = 64,
    EnduranceGrantDealt = 128,
    EnduranceGrantReceived = 256,
    Mez = 512,
    Knock = 1024,
    CompanionMissSummary = 2048
}
