using System.Text.RegularExpressions;
using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

/// <summary>
/// Combat grammar table. Matches text shapes only; it does not assign actors, amounts, or
/// event kinds. Match order is the legacy <see cref="CombatEventParser"/> precedence.
/// </summary>
internal static partial class GrammarMatcher
{
    /// <summary>
    /// Returns the first matching combat grammar for <paramref name="body"/>. Companion miss
    /// summaries are recognized and suppressed so they do not emit a grammar hit.
    /// </summary>
    public static bool TryMatch(string body, out GrammarMatch match)
    {
        match = null!;

        if (IsCompanionMissSummary(body))
        {
            return false;
        }

        return TryMatchRolledMiss(body, out match)
            || TryMatchRolledHit(body, out match)
            || TryMatchForcedHit(body, out match)
            || TryMatchAutohit(body, out match)
            || TryMatchYouHitWithPower(body, out match)
            || TryMatchYouHitWithoutPower(body, out match)
            || TryMatchSourceCriticallyHitsYouWithPower(body, out match)
            || TryMatchSourceHitsYouWithPower(body, out match)
            || TryMatchSourceHitsYouWithoutPower(body, out match)
            || TryMatchYouHealYourself(body, out match)
            || TryMatchYouHealTarget(body, out match)
            || TryMatchSourceHealsYou(body, out match)
            || TryMatchYouActivatedThePower(body, out match)
            || TryMatchYouActivate(body, out match)
            || TryMatchYouHaveDefeated(body, out match)
            || TryMatchOtherPlayerDefeated(body, out match);
    }

    public static bool IsCompanionMissSummary(string body) => CompanionMissSummary().IsMatch(body);

    private static bool TryMatchRolledMiss(string body, out GrammarMatch match) =>
        TryCreate(RolledMiss().Match(body), CombatGrammarId.Acc02RolledMiss, out match);

    private static bool TryMatchRolledHit(string body, out GrammarMatch match) =>
        TryCreate(RolledHit().Match(body), CombatGrammarId.Acc01RolledHit, out match);

    private static bool TryMatchForcedHit(string body, out GrammarMatch match) =>
        TryCreate(ForcedHit().Match(body), CombatGrammarId.Acc03ForcedHit, out match);

    private static bool TryMatchAutohit(string body, out GrammarMatch match) =>
        TryCreate(Autohit().Match(body), CombatGrammarId.Acc04Autohit, out match);

    private static bool TryMatchYouHitWithPower(string body, out GrammarMatch match) =>
        TryCreate(YouHitWithPower().Match(body), CombatGrammarId.Dmg01YouHitWithPower, out match);

    private static bool TryMatchYouHitWithoutPower(string body, out GrammarMatch match) =>
        TryCreate(YouHitWithoutPower().Match(body), CombatGrammarId.Dmg02YouHitWithoutPower, out match);

    private static bool TryMatchSourceCriticallyHitsYouWithPower(string body, out GrammarMatch match) =>
        TryCreate(
            SourceCriticallyHitsYouWithPower().Match(body),
            CombatGrammarId.Dmg05SourceCriticallyHitsYouWithPower,
            out match);

    private static bool TryMatchSourceHitsYouWithPower(string body, out GrammarMatch match) =>
        TryCreate(
            SourceHitsYouWithPower().Match(body),
            CombatGrammarId.Dmg03SourceHitsYouWithPower,
            out match);

    private static bool TryMatchSourceHitsYouWithoutPower(string body, out GrammarMatch match) =>
        TryCreate(
            SourceHitsYouWithoutPower().Match(body),
            CombatGrammarId.Dmg04SourceHitsYouWithoutPower,
            out match);

    private static bool TryMatchYouHealYourself(string body, out GrammarMatch match) =>
        TryCreate(YouHealYourself().Match(body), CombatGrammarId.Heal02YouHealYourself, out match);

    private static bool TryMatchYouHealTarget(string body, out GrammarMatch match) =>
        TryCreate(YouHealTarget().Match(body), CombatGrammarId.Heal01YouHealTarget, out match);

    private static bool TryMatchSourceHealsYou(string body, out GrammarMatch match) =>
        TryCreate(SourceHealsYou().Match(body), CombatGrammarId.Heal03SourceHealsYou, out match);

    private static bool TryMatchYouActivatedThePower(string body, out GrammarMatch match) =>
        TryCreate(YouActivatedThePower().Match(body), CombatGrammarId.Act02YouActivatedThePower, out match);

    private static bool TryMatchYouActivate(string body, out GrammarMatch match) =>
        TryCreate(YouActivate().Match(body), CombatGrammarId.Act01YouActivate, out match);

    private static bool TryMatchYouHaveDefeated(string body, out GrammarMatch match) =>
        TryCreate(YouHaveDefeated().Match(body), CombatGrammarId.Def01YouHaveDefeated, out match);

    private static bool TryMatchOtherPlayerDefeated(string body, out GrammarMatch match) =>
        TryCreate(OtherPlayerDefeated().Match(body), CombatGrammarId.Def02OtherPlayerDefeated, out match);

    private static bool TryCreate(Match regexMatch, CombatGrammarId grammarId, out GrammarMatch match)
    {
        if (!regexMatch.Success)
        {
            match = null!;
            return false;
        }

        match = new GrammarMatch
        {
            GrammarId = grammarId,
            Captures = ToCaptures(regexMatch)
        };
        return true;
    }

    private static IReadOnlyDictionary<string, string> ToCaptures(Match match)
    {
        var captures = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Group group in match.Groups)
        {
            if (string.IsNullOrEmpty(group.Name) || int.TryParse(group.Name, out _))
            {
                continue;
            }

            captures[group.Name] = group.Value;
        }

        return captures;
    }

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
