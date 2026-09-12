namespace CoHAnalytics.Models;

/// <summary>
/// Analytically valid perspectives on one logical event. Slice 2 assigns a single facet
/// matching the parsed family. Dedup facet unions belong to a later slice.
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
    Defeat = 64
}
