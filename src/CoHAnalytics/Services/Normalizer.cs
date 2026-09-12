using System.Globalization;
using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

/// <summary>
/// Converts a <see cref="GrammarMatch"/> plus parser provenance into today's
/// <see cref="CombatEvent"/>. Actor, amount, and kind assignment live here; text shapes do not.
/// </summary>
internal static class Normalizer
{
    public static bool TryNormalize(
        GrammarMatch match,
        ParserEvent parserEvent,
        out CombatEvent combatEvent)
    {
        combatEvent = null!;

        return match.GrammarId switch
        {
            CombatGrammarId.Acc02RolledMiss => TryCreateRolledAttackResolution(
                parserEvent,
                CombatGrammarId.Acc02RolledMiss,
                CombatAttackOutcome.Miss,
                match,
                out combatEvent),
            CombatGrammarId.Acc01RolledHit => TryCreateRolledAttackResolution(
                parserEvent,
                CombatGrammarId.Acc01RolledHit,
                CombatAttackOutcome.Hit,
                match,
                out combatEvent),
            CombatGrammarId.Acc03ForcedHit => TryCreateForcedOrAutohit(
                parserEvent,
                CombatGrammarId.Acc03ForcedHit,
                wasForced: true,
                isAutohit: false,
                match,
                out combatEvent),
            CombatGrammarId.Acc04Autohit => TryCreateForcedOrAutohit(
                parserEvent,
                CombatGrammarId.Acc04Autohit,
                wasForced: false,
                isAutohit: true,
                match,
                out combatEvent),
            CombatGrammarId.Dmg01YouHitWithPower => TryCreateDamageEvent(
                parserEvent,
                CombatGrammarId.Dmg01YouHitWithPower,
                match.Capture("target"),
                match.Capture("power"),
                match.Capture("amount"),
                match.Capture("type"),
                match.Capture("suffix"),
                match.Capture("effect"),
                out combatEvent),
            CombatGrammarId.Dmg02YouHitWithoutPower => TryCreateDamageEvent(
                parserEvent,
                CombatGrammarId.Dmg02YouHitWithoutPower,
                match.Capture("target"),
                powerName: null,
                match.Capture("amount"),
                match.Capture("type"),
                match.Capture("suffix"),
                match.Capture("effect"),
                out combatEvent),
            CombatGrammarId.Dmg05SourceCriticallyHitsYouWithPower => TryCreateIncomingDamageEvent(
                parserEvent,
                CombatGrammarId.Dmg05SourceCriticallyHitsYouWithPower,
                match.Capture("source"),
                match.Capture("power"),
                match.Capture("amount"),
                match.Capture("type"),
                match.Capture("suffix"),
                out combatEvent),
            CombatGrammarId.Dmg03SourceHitsYouWithPower => TryCreateIncomingDamageEvent(
                parserEvent,
                CombatGrammarId.Dmg03SourceHitsYouWithPower,
                match.Capture("source"),
                match.Capture("power"),
                match.Capture("amount"),
                match.Capture("type"),
                match.Capture("suffix"),
                out combatEvent),
            CombatGrammarId.Dmg04SourceHitsYouWithoutPower => TryCreateIncomingDamageEvent(
                parserEvent,
                CombatGrammarId.Dmg04SourceHitsYouWithoutPower,
                match.Capture("source"),
                powerName: null,
                match.Capture("amount"),
                match.Capture("type"),
                match.Capture("suffix"),
                out combatEvent),
            CombatGrammarId.Heal02YouHealYourself => TryCreateHeal(
                parserEvent,
                CombatEventKind.HealingDealt,
                CombatGrammarId.Heal02YouHealYourself,
                CombatActorRole.Self,
                targetName: "yourself",
                sourceName: null,
                match.Capture("power"),
                match.Capture("amount"),
                out combatEvent),
            CombatGrammarId.Heal01YouHealTarget => TryCreateHeal(
                parserEvent,
                CombatEventKind.HealingDealt,
                CombatGrammarId.Heal01YouHealTarget,
                CombatActorRole.Self,
                match.Capture("target"),
                sourceName: null,
                match.Capture("power"),
                match.Capture("amount"),
                out combatEvent),
            CombatGrammarId.Heal03SourceHealsYou => TryCreateHeal(
                parserEvent,
                CombatEventKind.HealingReceived,
                CombatGrammarId.Heal03SourceHealsYou,
                CombatActorRole.Other,
                targetName: null,
                match.Capture("source"),
                match.Capture("power"),
                match.Capture("amount"),
                out combatEvent),
            CombatGrammarId.Act02YouActivatedThePower => TryCreateActivation(
                parserEvent,
                CombatGrammarId.Act02YouActivatedThePower,
                match.Capture("power"),
                out combatEvent),
            CombatGrammarId.Act01YouActivate => TryCreateActivation(
                parserEvent,
                CombatGrammarId.Act01YouActivate,
                match.Capture("power"),
                out combatEvent),
            CombatGrammarId.Def01YouHaveDefeated => TryCreateYouDefeated(
                parserEvent,
                match.Capture("target"),
                out combatEvent),
            CombatGrammarId.Def02OtherPlayerDefeated => TryCreateOtherDefeated(
                parserEvent,
                match.Capture("source"),
                match.Capture("target"),
                out combatEvent),
            _ => false
        };
    }

    private static bool TryCreateRolledAttackResolution(
        ParserEvent parserEvent,
        CombatGrammarId grammarId,
        CombatAttackOutcome outcome,
        GrammarMatch match,
        out CombatEvent combatEvent)
    {
        combatEvent = null!;
        if (!TryParseResolutionHundredths(match.Capture("chance"), out var displayedChanceHundredths)
            || !TryParseResolutionHundredths(match.Capture("roll"), out var rollHundredths))
        {
            return false;
        }

        combatEvent = CreateAttackResolutionEvent(
            parserEvent,
            grammarId,
            outcome,
            match.Capture("target"),
            match.Capture("power"),
            wasRolled: true,
            wasForced: false,
            isAutohit: false,
            displayedChanceHundredths,
            rollHundredths);
        return true;
    }

    private static bool TryCreateForcedOrAutohit(
        ParserEvent parserEvent,
        CombatGrammarId grammarId,
        bool wasForced,
        bool isAutohit,
        GrammarMatch match,
        out CombatEvent combatEvent)
    {
        combatEvent = CreateAttackResolutionEvent(
            parserEvent,
            grammarId,
            CombatAttackOutcome.Hit,
            match.Capture("target"),
            match.Capture("power"),
            wasRolled: false,
            wasForced,
            isAutohit);
        return true;
    }

    private static CombatEvent CreateAttackResolutionEvent(
        ParserEvent parserEvent,
        CombatGrammarId grammarId,
        CombatAttackOutcome outcome,
        string targetName,
        string powerName,
        bool wasRolled,
        bool wasForced,
        bool isAutohit,
        long? displayedChanceHundredths = null,
        long? rollHundredths = null) =>
        new()
        {
            ContextId = parserEvent.ContextId,
            ParserSequence = parserEvent.Sequence,
            ObservedAt = parserEvent.ObservedAt,
            SourceTimestamp = parserEvent.SourceTimestamp,
            Kind = CombatEventKind.AttackResolution,
            GrammarId = grammarId,
            ActorRole = CombatActorRole.Self,
            TargetName = targetName,
            PowerName = powerName,
            AttackOutcome = outcome,
            DisplayedChanceHundredths = displayedChanceHundredths,
            RollHundredths = rollHundredths,
            WasRolled = wasRolled,
            WasForced = wasForced,
            IsAutohit = isAutohit
        };

    private static bool TryParseResolutionHundredths(string text, out long hundredths)
    {
        hundredths = 0;
        if (!CombatScaledAmount.TryParse(text, out var amount))
        {
            return false;
        }

        hundredths = amount.Hundredths;
        return true;
    }

    private static bool TryCreateDamageEvent(
        ParserEvent parserEvent,
        CombatGrammarId grammarId,
        string targetName,
        string? powerName,
        string amountText,
        string damageTypeText,
        string suffixText,
        string effectText,
        out CombatEvent combatEvent)
    {
        combatEvent = null!;
        if (!CombatScaledAmount.TryParse(amountText, out var amount))
        {
            return false;
        }

        combatEvent = CreateBaseEvent(
            parserEvent,
            CombatEventKind.DamageDealt,
            grammarId,
            CombatActorRole.Self,
            targetName,
            sourceName: null,
            powerName,
            amount,
            NormalizeDamageType(damageTypeText),
            IsOverTimeSuffix(suffixText),
            NormalizeEffectSuffix(effectText));
        return true;
    }

    private static bool TryCreateIncomingDamageEvent(
        ParserEvent parserEvent,
        CombatGrammarId grammarId,
        string sourceName,
        string? powerName,
        string amountText,
        string damageTypeText,
        string suffixText,
        out CombatEvent combatEvent)
    {
        combatEvent = null!;
        if (!CombatScaledAmount.TryParse(amountText, out var amount))
        {
            return false;
        }

        combatEvent = CreateBaseEvent(
            parserEvent,
            CombatEventKind.DamageReceived,
            grammarId,
            CombatActorRole.Other,
            targetName: null,
            sourceName,
            powerName,
            amount,
            NormalizeDamageType(damageTypeText),
            IsOverTimeSuffix(suffixText));
        return true;
    }

    private static bool TryCreateHeal(
        ParserEvent parserEvent,
        CombatEventKind kind,
        CombatGrammarId grammarId,
        CombatActorRole actorRole,
        string? targetName,
        string? sourceName,
        string powerName,
        string amountText,
        out CombatEvent combatEvent)
    {
        combatEvent = null!;
        if (!CombatScaledAmount.TryParse(amountText, out var amount))
        {
            return false;
        }

        combatEvent = CreateBaseEvent(
            parserEvent,
            kind,
            grammarId,
            actorRole,
            targetName,
            sourceName,
            powerName,
            amount);
        return true;
    }

    private static bool TryCreateActivation(
        ParserEvent parserEvent,
        CombatGrammarId grammarId,
        string powerName,
        out CombatEvent combatEvent)
    {
        combatEvent = CreateBaseEvent(
            parserEvent,
            CombatEventKind.PowerActivation,
            grammarId,
            CombatActorRole.Self,
            targetName: null,
            sourceName: null,
            powerName,
            CombatScaledAmount.Zero);
        return true;
    }

    private static bool TryCreateYouDefeated(
        ParserEvent parserEvent,
        string targetName,
        out CombatEvent combatEvent)
    {
        combatEvent = CreateBaseEvent(
            parserEvent,
            CombatEventKind.Defeat,
            CombatGrammarId.Def01YouHaveDefeated,
            CombatActorRole.Self,
            targetName,
            sourceName: null,
            powerName: null,
            CombatScaledAmount.Zero);
        return true;
    }

    private static bool TryCreateOtherDefeated(
        ParserEvent parserEvent,
        string sourceName,
        string targetName,
        out CombatEvent combatEvent)
    {
        combatEvent = null!;
        if (!IsLikelyDefeatedEntityName(targetName))
        {
            return false;
        }

        combatEvent = CreateBaseEvent(
            parserEvent,
            CombatEventKind.Defeat,
            CombatGrammarId.Def02OtherPlayerDefeated,
            CombatActorRole.Other,
            targetName,
            sourceName,
            powerName: null,
            CombatScaledAmount.Zero);
        return true;
    }

    private static CombatEvent CreateBaseEvent(
        ParserEvent parserEvent,
        CombatEventKind kind,
        CombatGrammarId grammarId,
        CombatActorRole actorRole,
        string? targetName,
        string? sourceName,
        string? powerName,
        CombatScaledAmount amount,
        string? damageType = null,
        bool isOverTime = false,
        string? effectSuffix = null) =>
        new()
        {
            ContextId = parserEvent.ContextId,
            ParserSequence = parserEvent.Sequence,
            ObservedAt = parserEvent.ObservedAt,
            SourceTimestamp = parserEvent.SourceTimestamp,
            Kind = kind,
            GrammarId = grammarId,
            ActorRole = actorRole,
            TargetName = targetName,
            SourceName = sourceName,
            PowerName = powerName,
            Amount = amount,
            DamageType = damageType,
            IsOverTime = isOverTime,
            EffectSuffix = effectSuffix
        };

    private static bool IsLikelyDefeatedEntityName(string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0)
        {
            return false;
        }

        if (!char.IsUpper(trimmed[0]) && !char.IsDigit(trimmed[0]))
        {
            return false;
        }

        return !trimmed.Contains(" among ", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsOverTimeSuffix(string suffixText) =>
        suffixText.Contains("over time", StringComparison.OrdinalIgnoreCase);

    private static string? NormalizeEffectSuffix(string effectText)
    {
        var trimmed = effectText.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    private static string NormalizeDamageType(string damageTypeText) =>
        CultureInfo.InvariantCulture.TextInfo.ToTitleCase(damageTypeText.ToLowerInvariant());
}
