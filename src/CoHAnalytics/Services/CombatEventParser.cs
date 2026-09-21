using System.Globalization;
using System.Text.RegularExpressions;
using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

/// <summary>
/// Semantic combat telemetry parser. Structural classification remains owned by <see cref="ParserClassifier"/>.
/// </summary>
public sealed partial class CombatEventParser : ICombatEventParser
{
    public bool TryParse(ParserEvent parserEvent, out CombatEvent combatEvent)
    {
        combatEvent = null!;

        if (parserEvent.LineStatus != ParserLineStatus.Complete
            || parserEvent.EventKind is ParserEventKind.Malformed)
        {
            return false;
        }

        if (!ParserLineEnvelope.TryGetBody(parserEvent.RawLine, parserEvent.SourceId.LogDate, out var body))
        {
            return false;
        }

        if (!IsCombatCandidate(parserEvent.EventKind, body))
        {
            return false;
        }

        if (TryParseAttackResolution(body, parserEvent, out combatEvent)
            || TryParseDamageDealt(body, parserEvent, out combatEvent)
            || TryParseDamageReceived(body, parserEvent, out combatEvent)
            || TryParseHealingDealt(body, parserEvent, out combatEvent)
            || TryParseHealingReceived(body, parserEvent, out combatEvent)
            || TryParsePowerActivation(body, parserEvent, out combatEvent)
            || TryParseDefeat(body, parserEvent, out combatEvent))
        {
            return true;
        }

        combatEvent = null!;
        return false;
    }

    /// <summary>
    /// Returns true when the structural classifier would treat the body as combat-shaped
    /// but this parser does not yet emit a semantic <see cref="CombatEvent"/>.
    /// </summary>
    public static bool IsCombatShapedUnparsed(ParserEvent parserEvent)
    {
        if (parserEvent.LineStatus != ParserLineStatus.Complete
            || parserEvent.EventKind is ParserEventKind.Malformed)
        {
            return false;
        }

        if (!ParserLineEnvelope.TryGetBody(parserEvent.RawLine, parserEvent.SourceId.LogDate, out var body))
        {
            return false;
        }

        return IsCombatCandidate(parserEvent.EventKind, body)
            && !new CombatEventParser().TryParse(parserEvent, out _);
    }

    private static bool IsCombatCandidate(ParserEventKind eventKind, string body) =>
        eventKind is ParserEventKind.SystemLine or ParserEventKind.Unknown or ParserEventKind.TimestampedLine
        && (body.StartsWith("You hit ", StringComparison.Ordinal)
            || body.StartsWith("You heal ", StringComparison.Ordinal)
            || body.StartsWith("You activate", StringComparison.Ordinal)
            || body.StartsWith("You activated ", StringComparison.Ordinal)
            || body.StartsWith("You have defeated ", StringComparison.Ordinal)
            || body.Contains(" has defeated ", StringComparison.Ordinal)
            || body.Contains(" hits you", StringComparison.Ordinal)
            || body.Contains(" heals you ", StringComparison.Ordinal)
            || body.StartsWith("HIT ", StringComparison.OrdinalIgnoreCase)
            || body.StartsWith("MISSED ", StringComparison.OrdinalIgnoreCase)
            || body.StartsWith("You take ", StringComparison.Ordinal));

    private static bool TryParseAttackResolution(string body, ParserEvent parserEvent, out CombatEvent combatEvent)
    {
        combatEvent = null!;

        if (CompanionMissSummary().IsMatch(body))
        {
            return false;
        }

        var rolledMiss = RolledMiss().Match(body);
        if (rolledMiss.Success
            && TryCreateRolledAttackResolutionEvent(
                parserEvent,
                CombatGrammarId.Acc02RolledMiss,
                CombatAttackOutcome.Miss,
                rolledMiss.Groups["target"].Value,
                rolledMiss.Groups["power"].Value,
                rolledMiss.Groups["chance"].Value,
                rolledMiss.Groups["roll"].Value,
                out combatEvent))
        {
            return true;
        }

        var rolledHit = RolledHit().Match(body);
        if (rolledHit.Success
            && TryCreateRolledAttackResolutionEvent(
                parserEvent,
                CombatGrammarId.Acc01RolledHit,
                CombatAttackOutcome.Hit,
                rolledHit.Groups["target"].Value,
                rolledHit.Groups["power"].Value,
                rolledHit.Groups["chance"].Value,
                rolledHit.Groups["roll"].Value,
                out combatEvent))
        {
            return true;
        }

        var forcedHit = ForcedHit().Match(body);
        if (forcedHit.Success)
        {
            combatEvent = CreateAttackResolutionEvent(
                parserEvent,
                CombatGrammarId.Acc03ForcedHit,
                CombatAttackOutcome.Hit,
                forcedHit.Groups["target"].Value,
                forcedHit.Groups["power"].Value,
                wasRolled: false,
                wasForced: true,
                isAutohit: false);
            return true;
        }

        var autohit = Autohit().Match(body);
        if (!autohit.Success)
        {
            return false;
        }

        combatEvent = CreateAttackResolutionEvent(
            parserEvent,
            CombatGrammarId.Acc04Autohit,
            CombatAttackOutcome.Hit,
            autohit.Groups["target"].Value,
            autohit.Groups["power"].Value,
            wasRolled: false,
            wasForced: false,
            isAutohit: true);
        return true;
    }

    private static bool TryCreateRolledAttackResolutionEvent(
        ParserEvent parserEvent,
        CombatGrammarId grammarId,
        CombatAttackOutcome outcome,
        string targetName,
        string powerName,
        string chanceText,
        string rollText,
        out CombatEvent combatEvent)
    {
        combatEvent = null!;
        if (!TryParseResolutionHundredths(chanceText, out var displayedChanceHundredths)
            || !TryParseResolutionHundredths(rollText, out var rollHundredths))
        {
            return false;
        }

        combatEvent = CreateAttackResolutionEvent(
            parserEvent,
            grammarId,
            outcome,
            targetName,
            powerName,
            wasRolled: true,
            wasForced: false,
            isAutohit: false,
            displayedChanceHundredths,
            rollHundredths);
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

    private static bool TryParseDamageDealt(string body, ParserEvent parserEvent, out CombatEvent combatEvent)
    {
        combatEvent = null!;

        var withPower = YouHitWithPower().Match(body);
        if (withPower.Success
            && TryCreateDamageEvent(
                parserEvent,
                CombatGrammarId.Dmg01YouHitWithPower,
                withPower.Groups["target"].Value,
                withPower.Groups["power"].Value,
                withPower.Groups["amount"].Value,
                withPower.Groups["type"].Value,
                withPower.Groups["suffix"].Value,
                withPower.Groups["effect"].Value,
                out combatEvent))
        {
            return true;
        }

        var withoutPower = YouHitWithoutPower().Match(body);
        if (withoutPower.Success
            && TryCreateDamageEvent(
                parserEvent,
                CombatGrammarId.Dmg02YouHitWithoutPower,
                withoutPower.Groups["target"].Value,
                powerName: null,
                withoutPower.Groups["amount"].Value,
                withoutPower.Groups["type"].Value,
                withoutPower.Groups["suffix"].Value,
                withoutPower.Groups["effect"].Value,
                out combatEvent))
        {
            return true;
        }

        return false;
    }

    private static bool TryParseDamageReceived(string body, ParserEvent parserEvent, out CombatEvent combatEvent)
    {
        combatEvent = null!;

        var critical = SourceCriticallyHitsYouWithPower().Match(body);
        if (critical.Success
            && TryCreateIncomingDamageEvent(
                parserEvent,
                CombatGrammarId.Dmg05SourceCriticallyHitsYouWithPower,
                critical.Groups["source"].Value,
                critical.Groups["power"].Value,
                critical.Groups["amount"].Value,
                critical.Groups["type"].Value,
                critical.Groups["suffix"].Value,
                out combatEvent))
        {
            return true;
        }

        var withPower = SourceHitsYouWithPower().Match(body);
        if (withPower.Success
            && TryCreateIncomingDamageEvent(
                parserEvent,
                CombatGrammarId.Dmg03SourceHitsYouWithPower,
                withPower.Groups["source"].Value,
                withPower.Groups["power"].Value,
                withPower.Groups["amount"].Value,
                withPower.Groups["type"].Value,
                withPower.Groups["suffix"].Value,
                out combatEvent))
        {
            return true;
        }

        var withoutPower = SourceHitsYouWithoutPower().Match(body);
        if (withoutPower.Success
            && TryCreateIncomingDamageEvent(
                parserEvent,
                CombatGrammarId.Dmg04SourceHitsYouWithoutPower,
                withoutPower.Groups["source"].Value,
                powerName: null,
                withoutPower.Groups["amount"].Value,
                withoutPower.Groups["type"].Value,
                withoutPower.Groups["suffix"].Value,
                out combatEvent))
        {
            return true;
        }

        return false;
    }

    private static bool TryParseHealingDealt(string body, ParserEvent parserEvent, out CombatEvent combatEvent)
    {
        combatEvent = null!;

        var self = YouHealYourself().Match(body);
        if (self.Success
            && CombatScaledAmount.TryParse(self.Groups["amount"].Value, out var amount))
        {
            combatEvent = CreateBaseEvent(
                parserEvent,
                CombatEventKind.HealingDealt,
                CombatGrammarId.Heal02YouHealYourself,
                CombatActorRole.Self,
                targetName: "yourself",
                sourceName: null,
                self.Groups["power"].Value,
                amount);
            return true;
        }

        var target = YouHealTarget().Match(body);
        if (target.Success
            && CombatScaledAmount.TryParse(target.Groups["amount"].Value, out amount))
        {
            combatEvent = CreateBaseEvent(
                parserEvent,
                CombatEventKind.HealingDealt,
                CombatGrammarId.Heal01YouHealTarget,
                CombatActorRole.Self,
                target.Groups["target"].Value,
                sourceName: null,
                target.Groups["power"].Value,
                amount);
            return true;
        }

        return false;
    }

    private static bool TryParseHealingReceived(string body, ParserEvent parserEvent, out CombatEvent combatEvent)
    {
        combatEvent = null!;

        var match = SourceHealsYou().Match(body);
        if (!match.Success
            || !CombatScaledAmount.TryParse(match.Groups["amount"].Value, out var amount))
        {
            return false;
        }

        combatEvent = CreateBaseEvent(
            parserEvent,
            CombatEventKind.HealingReceived,
            CombatGrammarId.Heal03SourceHealsYou,
            CombatActorRole.Other,
            targetName: null,
            match.Groups["source"].Value,
            match.Groups["power"].Value,
            amount);
        return true;
    }

    private static bool TryParsePowerActivation(string body, ParserEvent parserEvent, out CombatEvent combatEvent)
    {
        combatEvent = null!;

        var activatedThe = YouActivatedThePower().Match(body);
        if (activatedThe.Success)
        {
            combatEvent = CreateBaseEvent(
                parserEvent,
                CombatEventKind.PowerActivation,
                CombatGrammarId.Act02YouActivatedThePower,
                CombatActorRole.Self,
                targetName: null,
                sourceName: null,
                activatedThe.Groups["power"].Value,
                CombatScaledAmount.Zero);
            return true;
        }

        var activate = YouActivate().Match(body);
        if (!activate.Success)
        {
            return false;
        }

        combatEvent = CreateBaseEvent(
            parserEvent,
            CombatEventKind.PowerActivation,
            CombatGrammarId.Act01YouActivate,
            CombatActorRole.Self,
            targetName: null,
            sourceName: null,
            activate.Groups["power"].Value,
            CombatScaledAmount.Zero);
        return true;
    }

    private static bool TryParseDefeat(string body, ParserEvent parserEvent, out CombatEvent combatEvent)
    {
        combatEvent = null!;

        var youMatch = YouHaveDefeated().Match(body);
        if (youMatch.Success)
        {
            combatEvent = CreateBaseEvent(
                parserEvent,
                CombatEventKind.Defeat,
                CombatGrammarId.Def01YouHaveDefeated,
                CombatActorRole.Self,
                youMatch.Groups["target"].Value,
                sourceName: null,
                powerName: null,
                CombatScaledAmount.Zero);
            return true;
        }

        var otherMatch = OtherPlayerDefeated().Match(body);
        if (!otherMatch.Success
            || !IsLikelyDefeatedEntityName(otherMatch.Groups["target"].Value))
        {
            return false;
        }

        combatEvent = CreateBaseEvent(
            parserEvent,
            CombatEventKind.Defeat,
            CombatGrammarId.Def02OtherPlayerDefeated,
            CombatActorRole.Other,
            otherMatch.Groups["target"].Value,
            sourceName: otherMatch.Groups["source"].Value,
            powerName: null,
            CombatScaledAmount.Zero);
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

    [GeneratedRegex(
        @"^You hit (?<target>.+?) with your (?<power>.+?) for (?<amount>\d+(?:\.\d+)?) points of (?<type>[A-Za-z]+) damage(?<suffix> over time)?(?:!)?(?: \((?<effect>[^)]+)\))?\.$",
        RegexOptions.CultureInvariant)]
    private static partial Regex YouHitWithPower();

    [GeneratedRegex(
        @"^You hit (?<target>.+?) for (?<amount>\d+(?:\.\d+)?) points of (?<type>[A-Za-z]+) damage(?<suffix> over time)?(?:!)?(?: \((?<effect>[^)]+)\))?\.$",
        RegexOptions.CultureInvariant)]
    private static partial Regex YouHitWithoutPower();

    [GeneratedRegex(
        @"^(?<source>.+?) critically hits you with (?<power>.+?) for (?<amount>\d+(?:\.\d+)?) points of (?<type>[A-Za-z]+) damage(?<suffix> over time)?\.$",
        RegexOptions.CultureInvariant)]
    private static partial Regex SourceCriticallyHitsYouWithPower();

    [GeneratedRegex(
        @"^(?<source>.+?) hits you with (?<power>.+?) for (?<amount>\d+(?:\.\d+)?) points of (?<type>[A-Za-z]+) damage(?<suffix> over time)?\.$",
        RegexOptions.CultureInvariant)]
    private static partial Regex SourceHitsYouWithPower();

    [GeneratedRegex(
        @"^(?<source>.+?) hits you for (?<amount>\d+(?:\.\d+)?) points of (?<type>[A-Za-z]+) damage(?<suffix> over time)?\.$",
        RegexOptions.CultureInvariant)]
    private static partial Regex SourceHitsYouWithoutPower();

    [GeneratedRegex(
        @"^You heal (?<target>.+?) for (?<amount>\d+(?:\.\d+)?) hit points with (?<power>.+)\.$",
        RegexOptions.CultureInvariant)]
    private static partial Regex YouHealTarget();

    [GeneratedRegex(
        @"^You heal yourself for (?<amount>\d+(?:\.\d+)?) hit points with (?<power>.+)\.$",
        RegexOptions.CultureInvariant)]
    private static partial Regex YouHealYourself();

    [GeneratedRegex(
        @"^(?<source>.+?) heals you for (?<amount>\d+(?:\.\d+)?) hit points with (?<power>.+)\.$",
        RegexOptions.CultureInvariant)]
    private static partial Regex SourceHealsYou();

    [GeneratedRegex(@"^You activate (?<power>.+)\.$", RegexOptions.CultureInvariant)]
    private static partial Regex YouActivate();

    [GeneratedRegex(@"^You activated the (?<power>.+) power\.$", RegexOptions.CultureInvariant)]
    private static partial Regex YouActivatedThePower();

    [GeneratedRegex(@"^You have defeated (?<target>.+?)\.?$", RegexOptions.CultureInvariant)]
    private static partial Regex YouHaveDefeated();

    [GeneratedRegex(
        @"^(?<source>.+?) has defeated (?<target>.+?)\.?$",
        RegexOptions.CultureInvariant)]
    private static partial Regex OtherPlayerDefeated();

    [GeneratedRegex(
        @"^HIT (?<target>.+?)! Your (?<power>.+?) power had a (?<chance>\d+(?:\.\d+)?)% chance to hit, you rolled a (?<roll>\d+(?:\.\d+)?)\.$",
        RegexOptions.CultureInvariant)]
    private static partial Regex RolledHit();

    [GeneratedRegex(
        @"^MISSED (?<target>.+?)!! Your (?<power>.+?) power had a (?<chance>\d+(?:\.\d+)?)% chance to hit, you rolled a (?<roll>\d+(?:\.\d+)?)\.$",
        RegexOptions.CultureInvariant)]
    private static partial Regex RolledMiss();

    [GeneratedRegex(
        @"^HIT (?<target>.+?)! Your (?<power>.+?) power was forced to hit by streakbreaker\.$",
        RegexOptions.CultureInvariant)]
    private static partial Regex ForcedHit();

    [GeneratedRegex(
        @"^HIT (?<target>.+?)! Your (?<power>.+?) power is autohit\.$",
        RegexOptions.CultureInvariant)]
    private static partial Regex Autohit();

    [GeneratedRegex(@"^(?<power>.+?) missed!$", RegexOptions.CultureInvariant)]
    private static partial Regex CompanionMissSummary();
}
