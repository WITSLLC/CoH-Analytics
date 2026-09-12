using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

/// <summary>
/// Converts a <see cref="GrammarMatch"/> plus parser provenance into a
/// <see cref="CanonicalCombatEvent"/>. Actor, amount, and family assignment live here;
/// text shapes do not.
/// </summary>
internal static class Normalizer
{
    public static bool TryNormalize(
        GrammarMatch match,
        ParserEvent parserEvent,
        out CanonicalCombatEvent canonicalEvent)
    {
        canonicalEvent = null!;

        return match.GrammarId switch
        {
            CombatGrammarId.Acc02RolledMiss => TryCreateRolledAttackResolution(
                parserEvent,
                CombatGrammarId.Acc02RolledMiss,
                CombatAttackOutcome.Miss,
                match,
                out canonicalEvent),
            CombatGrammarId.Acc01RolledHit => TryCreateRolledAttackResolution(
                parserEvent,
                CombatGrammarId.Acc01RolledHit,
                CombatAttackOutcome.Hit,
                match,
                out canonicalEvent),
            CombatGrammarId.Acc03ForcedHit => TryCreateForcedOrAutohit(
                parserEvent,
                CombatGrammarId.Acc03ForcedHit,
                wasForced: true,
                isAutohit: false,
                match,
                out canonicalEvent),
            CombatGrammarId.Acc04Autohit => TryCreateForcedOrAutohit(
                parserEvent,
                CombatGrammarId.Acc04Autohit,
                wasForced: false,
                isAutohit: true,
                match,
                out canonicalEvent),
            CombatGrammarId.Dmg01YouHitWithPower => TryCreateDamageDealt(
                parserEvent,
                CombatGrammarId.Dmg01YouHitWithPower,
                match.Capture("target"),
                match.Capture("power"),
                match.Capture("amount"),
                match.Capture("type"),
                match.Capture("suffix"),
                match.Capture("effect"),
                out canonicalEvent),
            CombatGrammarId.Dmg02YouHitWithoutPower => TryCreateDamageDealt(
                parserEvent,
                CombatGrammarId.Dmg02YouHitWithoutPower,
                match.Capture("target"),
                powerName: null,
                match.Capture("amount"),
                match.Capture("type"),
                match.Capture("suffix"),
                match.Capture("effect"),
                out canonicalEvent),
            CombatGrammarId.Dmg05SourceCriticallyHitsYouWithPower => TryCreateDamageReceived(
                parserEvent,
                CombatGrammarId.Dmg05SourceCriticallyHitsYouWithPower,
                match.Capture("source"),
                match.Capture("power"),
                match.Capture("amount"),
                match.Capture("type"),
                match.Capture("suffix"),
                isCritical: true,
                out canonicalEvent),
            CombatGrammarId.Dmg03SourceHitsYouWithPower => TryCreateDamageReceived(
                parserEvent,
                CombatGrammarId.Dmg03SourceHitsYouWithPower,
                match.Capture("source"),
                match.Capture("power"),
                match.Capture("amount"),
                match.Capture("type"),
                match.Capture("suffix"),
                isCritical: false,
                out canonicalEvent),
            CombatGrammarId.Dmg04SourceHitsYouWithoutPower => TryCreateDamageReceived(
                parserEvent,
                CombatGrammarId.Dmg04SourceHitsYouWithoutPower,
                match.Capture("source"),
                powerName: null,
                match.Capture("amount"),
                match.Capture("type"),
                match.Capture("suffix"),
                isCritical: false,
                out canonicalEvent),
            CombatGrammarId.Heal02YouHealYourself => TryCreateHeal(
                parserEvent,
                CombatEventFamily.HealDealt,
                CombatGrammarId.Heal02YouHealYourself,
                ActorRef.Self,
                ActorRef.SelfNamed("yourself"),
                match.Capture("power"),
                match.Capture("amount"),
                out canonicalEvent),
            CombatGrammarId.Heal01YouHealTarget => TryCreateHeal(
                parserEvent,
                CombatEventFamily.HealDealt,
                CombatGrammarId.Heal01YouHealTarget,
                ActorRef.Self,
                ActorRef.UnknownNamed(match.Capture("target")),
                match.Capture("power"),
                match.Capture("amount"),
                out canonicalEvent),
            CombatGrammarId.Heal03SourceHealsYou => TryCreateHeal(
                parserEvent,
                CombatEventFamily.HealReceived,
                CombatGrammarId.Heal03SourceHealsYou,
                ActorRef.UnknownNamed(match.Capture("source")),
                ActorRef.Self,
                match.Capture("power"),
                match.Capture("amount"),
                out canonicalEvent),
            CombatGrammarId.Act02YouActivatedThePower => TryCreateActivation(
                parserEvent,
                CombatGrammarId.Act02YouActivatedThePower,
                match.Capture("power"),
                out canonicalEvent),
            CombatGrammarId.Act01YouActivate => TryCreateActivation(
                parserEvent,
                CombatGrammarId.Act01YouActivate,
                match.Capture("power"),
                out canonicalEvent),
            CombatGrammarId.Def01YouHaveDefeated => TryCreateYouDefeated(
                parserEvent,
                match.Capture("target"),
                out canonicalEvent),
            CombatGrammarId.Def02OtherPlayerDefeated => TryCreateOtherDefeated(
                parserEvent,
                match.Capture("source"),
                match.Capture("target"),
                out canonicalEvent),
            _ => false
        };
    }

    private static bool TryCreateRolledAttackResolution(
        ParserEvent parserEvent,
        CombatGrammarId grammarId,
        CombatAttackOutcome outcome,
        GrammarMatch match,
        out CanonicalCombatEvent canonicalEvent)
    {
        canonicalEvent = null!;
        if (!TryParseResolutionHundredths(match.Capture("chance"), out var displayedChanceHundredths)
            || !TryParseResolutionHundredths(match.Capture("roll"), out var rollHundredths))
        {
            return false;
        }

        canonicalEvent = CreateCanonical(
            parserEvent,
            CombatEventFamily.AttackResolution,
            grammarId,
            ActorRef.Self,
            ActorRef.UnknownNamed(match.Capture("target")),
            match.Capture("power"),
            CombatScaledAmount.Zero,
            MagnitudeKind.None,
            outcome: outcome,
            displayedChanceHundredths: displayedChanceHundredths,
            rollHundredths: rollHundredths);
        return true;
    }

    private static bool TryCreateForcedOrAutohit(
        ParserEvent parserEvent,
        CombatGrammarId grammarId,
        bool wasForced,
        bool isAutohit,
        GrammarMatch match,
        out CanonicalCombatEvent canonicalEvent)
    {
        var delivery = DeliveryFlags.None;
        if (wasForced)
        {
            delivery |= DeliveryFlags.Forced;
        }

        if (isAutohit)
        {
            delivery |= DeliveryFlags.Autohit;
        }

        canonicalEvent = CreateCanonical(
            parserEvent,
            CombatEventFamily.AttackResolution,
            grammarId,
            ActorRef.Self,
            ActorRef.UnknownNamed(match.Capture("target")),
            match.Capture("power"),
            CombatScaledAmount.Zero,
            MagnitudeKind.None,
            delivery: delivery,
            outcome: CombatAttackOutcome.Hit);
        return true;
    }

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

    private static bool TryCreateDamageDealt(
        ParserEvent parserEvent,
        CombatGrammarId grammarId,
        string targetName,
        string? powerName,
        string amountText,
        string damageTypeText,
        string suffixText,
        string effectText,
        out CanonicalCombatEvent canonicalEvent)
    {
        canonicalEvent = null!;
        if (!CombatScaledAmount.TryParse(amountText, out var amount))
        {
            return false;
        }

        var effectSuffix = NormalizeEffectSuffix(effectText);
        canonicalEvent = CreateCanonical(
            parserEvent,
            CombatEventFamily.DamageDealt,
            grammarId,
            ActorRef.Self,
            ActorRef.UnknownNamed(targetName),
            powerName,
            amount,
            MagnitudeKind.HitPoints,
            DamageType.FromParsedToken(damageTypeText),
            ToDamageDelivery(suffixText, effectSuffix),
            effectSuffix);
        return true;
    }

    private static bool TryCreateDamageReceived(
        ParserEvent parserEvent,
        CombatGrammarId grammarId,
        string sourceName,
        string? powerName,
        string amountText,
        string damageTypeText,
        string suffixText,
        bool isCritical,
        out CanonicalCombatEvent canonicalEvent)
    {
        canonicalEvent = null!;
        if (!CombatScaledAmount.TryParse(amountText, out var amount))
        {
            return false;
        }

        var delivery = ToDamageDelivery(suffixText, effectSuffix: null);
        if (isCritical)
        {
            delivery |= DeliveryFlags.Critical;
        }

        canonicalEvent = CreateCanonical(
            parserEvent,
            CombatEventFamily.DamageReceived,
            grammarId,
            ActorRef.UnknownNamed(sourceName),
            ActorRef.Self,
            powerName,
            amount,
            MagnitudeKind.HitPoints,
            DamageType.FromParsedToken(damageTypeText),
            delivery);
        return true;
    }

    private static bool TryCreateHeal(
        ParserEvent parserEvent,
        CombatEventFamily family,
        CombatGrammarId grammarId,
        ActorRef actor,
        ActorRef target,
        string powerName,
        string amountText,
        out CanonicalCombatEvent canonicalEvent)
    {
        canonicalEvent = null!;
        if (!CombatScaledAmount.TryParse(amountText, out var amount))
        {
            return false;
        }

        canonicalEvent = CreateCanonical(
            parserEvent,
            family,
            grammarId,
            actor,
            target,
            powerName,
            amount,
            MagnitudeKind.HitPoints);
        return true;
    }

    private static bool TryCreateActivation(
        ParserEvent parserEvent,
        CombatGrammarId grammarId,
        string powerName,
        out CanonicalCombatEvent canonicalEvent)
    {
        canonicalEvent = CreateCanonical(
            parserEvent,
            CombatEventFamily.Activation,
            grammarId,
            ActorRef.Self,
            target: null,
            powerName,
            CombatScaledAmount.Zero,
            MagnitudeKind.None);
        return true;
    }

    private static bool TryCreateYouDefeated(
        ParserEvent parserEvent,
        string targetName,
        out CanonicalCombatEvent canonicalEvent)
    {
        canonicalEvent = CreateCanonical(
            parserEvent,
            CombatEventFamily.Defeat,
            CombatGrammarId.Def01YouHaveDefeated,
            ActorRef.Self,
            ActorRef.UnknownNamed(targetName),
            powerName: null,
            CombatScaledAmount.Zero,
            MagnitudeKind.None);
        return true;
    }

    private static bool TryCreateOtherDefeated(
        ParserEvent parserEvent,
        string sourceName,
        string targetName,
        out CanonicalCombatEvent canonicalEvent)
    {
        canonicalEvent = null!;
        if (!IsLikelyDefeatedEntityName(targetName))
        {
            return false;
        }

        canonicalEvent = CreateCanonical(
            parserEvent,
            CombatEventFamily.Defeat,
            CombatGrammarId.Def02OtherPlayerDefeated,
            ActorRef.UnknownNamed(sourceName),
            ActorRef.UnknownNamed(targetName),
            powerName: null,
            CombatScaledAmount.Zero,
            MagnitudeKind.None);
        return true;
    }

    private static CanonicalCombatEvent CreateCanonical(
        ParserEvent parserEvent,
        CombatEventFamily family,
        CombatGrammarId grammarId,
        ActorRef actor,
        ActorRef? target,
        string? powerName,
        CombatScaledAmount amount,
        MagnitudeKind magnitude,
        DamageType? damageType = null,
        DeliveryFlags delivery = DeliveryFlags.None,
        string? effectSuffix = null,
        CombatAttackOutcome? outcome = null,
        long? displayedChanceHundredths = null,
        long? rollHundredths = null)
    {
        var provenance = EventProvenance.FromParserEvent(parserEvent);
        return new CanonicalCombatEvent
        {
            Provenance = provenance,
            Sequence = provenance.ParserSequence,
            ObservedAt = provenance.ObservedAt,
            SourceTimestamp = provenance.SourceTimestamp,
            Family = family,
            GrammarId = grammarId,
            Actor = actor,
            Target = target,
            PowerName = powerName,
            Amount = amount,
            Magnitude = magnitude,
            DamageType = damageType,
            Delivery = delivery,
            EffectSuffix = effectSuffix,
            Outcome = outcome,
            DisplayedChanceHundredths = displayedChanceHundredths,
            RollHundredths = rollHundredths,
            SourceChannel = provenance.SourceChannel,
            MirrorClass = new MirrorClassification
            {
                Family = family,
                GrammarId = grammarId,
                SourceChannel = provenance.SourceChannel
            },
            Facets = ToFacet(family),
            DuplicateOf = null
        };
    }

    private static EventFacets ToFacet(CombatEventFamily family) =>
        family switch
        {
            CombatEventFamily.DamageDealt => EventFacets.DamageDealt,
            CombatEventFamily.DamageReceived => EventFacets.DamageReceived,
            CombatEventFamily.HealDealt => EventFacets.HealDelivered,
            CombatEventFamily.HealReceived => EventFacets.HealReceived,
            CombatEventFamily.AttackResolution => EventFacets.AttackResolution,
            CombatEventFamily.Activation => EventFacets.Activation,
            CombatEventFamily.Defeat => EventFacets.Defeat,
            _ => EventFacets.None
        };

    private static DeliveryFlags ToDamageDelivery(string suffixText, string? effectSuffix)
    {
        var delivery = DeliveryFlags.None;
        if (IsOverTimeSuffix(suffixText))
        {
            delivery |= DeliveryFlags.DoT;
        }

        if (string.Equals(effectSuffix, "CONTAINMENT", StringComparison.Ordinal))
        {
            delivery |= DeliveryFlags.Containment;
        }

        return delivery;
    }

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
}
