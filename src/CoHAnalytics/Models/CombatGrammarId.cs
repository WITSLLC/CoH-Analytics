namespace CoHAnalytics.Models;

/// <summary>Identifies the exact combat grammar that produced one <see cref="CombatEvent"/>.</summary>
public enum CombatGrammarId
{
    Dmg01YouHitWithPower = 0,
    Dmg02YouHitWithoutPower,
    Dmg03SourceHitsYouWithPower,
    Dmg04SourceHitsYouWithoutPower,
    Dmg05SourceCriticallyHitsYouWithPower,
    Heal01YouHealTarget,
    Heal02YouHealYourself,
    Heal03SourceHealsYou,
    Act01YouActivate,
    Act02YouActivatedThePower,
    Def01YouHaveDefeated,
    Def02OtherPlayerDefeated,
    Acc01RolledHit,
    Acc02RolledMiss,
    Acc03ForcedHit,
    Acc04Autohit
}
