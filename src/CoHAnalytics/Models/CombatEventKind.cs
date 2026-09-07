namespace CoHAnalytics.Models;

/// <summary>Normalized combat event categories produced by <see cref="Services.CombatEventParser"/>.</summary>
public enum CombatEventKind
{
  DamageDealt = 0,
    DamageReceived,
    HealingDealt,
    HealingReceived,
    PowerActivation,
    Defeat,

    /// <summary>Reserved for a future accuracy slice.</summary>
    AttackResolution,

    /// <summary>Reserved for future observation capture of combat-shaped unparsed lines.</summary>
    Unparsed
}
