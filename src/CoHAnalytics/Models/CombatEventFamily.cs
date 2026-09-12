namespace CoHAnalytics.Models;

/// <summary>Canonical combat family. Slice 2 maps only currently parsed grammars onto known values.</summary>
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
