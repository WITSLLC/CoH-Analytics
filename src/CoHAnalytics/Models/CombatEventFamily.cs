namespace CoHAnalytics.Models;

/// <summary>Canonical combat family. Adapter maps only families with a truthful legacy equivalent.</summary>
public enum CombatEventFamily
{
    DamageDealt = 0,
    DamageReceived,
    HealDealt,
    HealReceived,
    EnduranceGrantDealt,
    EnduranceGrantReceived,
    AttackResolution,
    Activation,
    Defeat,
    Mez,
    Knock,
    CompanionMissSummary,
    EnvironmentDamage,
    Unparsed
}
