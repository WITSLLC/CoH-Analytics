using System.Globalization;
using System.Text.RegularExpressions;
using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

/// <summary>
/// Semantic gameplay telemetry parser for research-backed XP, Influence, and reward grammars.
/// Structural classification remains owned by <see cref="ParserClassifier"/>.
/// </summary>
public sealed partial class GameplayTelemetryParser : IGameplayTelemetryParser
{
    public bool TryParse(ParserEvent parserEvent, out GameplayTelemetryObservation observation)
    {
        observation = null!;

        if (parserEvent.LineStatus != ParserLineStatus.Complete
            || parserEvent.EventKind is ParserEventKind.Malformed)
        {
            return false;
        }

        if (!ParserLineEnvelope.TryGetBody(parserEvent.RawLine, parserEvent.SourceId.LogDate, out var body))
        {
            return false;
        }

        if (TryParseCharacterLevel(body, out observation))
        {
            return true;
        }

        if (parserEvent.EventKind is not ParserEventKind.Unknown
            && TryParseXpAndInfluence(body, out observation))
        {
            return true;
        }

        if (TryParseReward(body, out observation))
        {
            return true;
        }

        observation = null!;
        return false;
    }

    private static bool TryParseCharacterLevel(string body, out GameplayTelemetryObservation observation)
    {
        observation = null!;

        if (CharacterLevelImprovement().Match(body) is not { Success: true } match)
        {
            return false;
        }

        if (!int.TryParse(match.Groups["level"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var level)
            || level <= 0)
        {
            return false;
        }

        observation = new GameplayTelemetryObservation
        {
            GrammarId = GameplayTelemetryGrammarId.Chr01CharacterLevelImprovement,
            CharacterObservedLevel = level
        };
        return true;
    }

    private static bool TryParseXpAndInfluence(string body, out GameplayTelemetryObservation observation)
    {
        observation = null!;

        if (Xp02ExperienceDebtAndInfluence().Match(body) is { Success: true } xp02)
        {
            if (!TryParseAmount(xp02.Groups["xp"].Value, out var xp)
                || !TryParseAmount(xp02.Groups["debt"].Value, out var debt)
                || !TryParseAmount(xp02.Groups["influence"].Value, out var influence))
            {
                return false;
            }

            observation = new GameplayTelemetryObservation
            {
                GrammarId = GameplayTelemetryGrammarId.Xp02ExperienceDebtAndInfluence,
                ExperienceGained = xp,
                GameplayInfluenceGained = influence,
                DebtWorkedOff = debt
            };
            return true;
        }

        if (Xp01ExperienceAndInfluence().Match(body) is { Success: true } xp01)
        {
            if (!TryParseAmount(xp01.Groups["xp"].Value, out var xp)
                || !TryParseAmount(xp01.Groups["influence"].Value, out var influence))
            {
                return false;
            }

            observation = new GameplayTelemetryObservation
            {
                GrammarId = GameplayTelemetryGrammarId.Xp01ExperienceAndInfluence,
                ExperienceGained = xp,
                GameplayInfluenceGained = influence
            };
            return true;
        }

        if (Xp04ExperienceAndDebt().Match(body) is { Success: true } xp04)
        {
            if (!TryParseAmount(xp04.Groups["xp"].Value, out var xp)
                || !TryParseAmount(xp04.Groups["debt"].Value, out var debt))
            {
                return false;
            }

            observation = new GameplayTelemetryObservation
            {
                GrammarId = GameplayTelemetryGrammarId.Xp04ExperienceAndDebt,
                ExperienceGained = xp,
                DebtWorkedOff = debt
            };
            return true;
        }

        if (Xp03ExperienceOnly().Match(body) is { Success: true } xp03)
        {
            if (!TryParseAmount(xp03.Groups["xp"].Value, out var xp))
            {
                return false;
            }

            observation = new GameplayTelemetryObservation
            {
                GrammarId = GameplayTelemetryGrammarId.Xp03ExperienceOnly,
                ExperienceGained = xp
            };
            return true;
        }

        if (Inf01GameplayInfluenceOnly().Match(body) is { Success: true } inf01)
        {
            if (!TryParseAmount(inf01.Groups["influence"].Value, out var influence))
            {
                return false;
            }

            observation = new GameplayTelemetryObservation
            {
                GrammarId = GameplayTelemetryGrammarId.Inf01GameplayInfluenceOnly,
                GameplayInfluenceGained = influence
            };
            return true;
        }

        return false;
    }

    private static bool TryParseReward(string body, out GameplayTelemetryObservation observation)
    {
        observation = null!;

        if (TryParseBadgeEarned(body, out observation))
        {
            return true;
        }

        if (GameplayRewardCurrencyGrammar.IsArchitectTicketStatus(body))
        {
            return false;
        }

        if (GameplayRewardCurrencyGrammar.TryParseStandaloneMeritBody(body, out var meritName, out var meritQty))
        {
            observation = new GameplayTelemetryObservation
            {
                GrammarId = GameplayTelemetryGrammarId.Cur01RewardMeritStandalone,
                RewardCategory = GameplaySessionRewardCategory.RewardCurrency,
                RewardDisplayName = meritName,
                RewardQuantity = meritQty
            };
            return true;
        }

        if (GenericReceivedItem().Match(body) is not { Success: true } received)
        {
            return false;
        }

        var itemText = received.Groups["item"].Value;
        if (GameplayRewardCurrencyGrammar.TryParseReceivedItemText(itemText, out var currencyName, out var quantity))
        {
            observation = new GameplayTelemetryObservation
            {
                GrammarId = GameplayTelemetryGrammarId.Recv01GenericReceivedItem,
                RewardCategory = GameplaySessionRewardCategory.RewardCurrency,
                RewardDisplayName = currencyName,
                RewardQuantity = quantity,
                ReceivedItemText = itemText
            };
            return true;
        }

        observation = new GameplayTelemetryObservation
        {
            GrammarId = GameplayTelemetryGrammarId.Recv01GenericReceivedItem,
            RewardCategory = GameplaySessionRewardCategory.ReceivedItem,
            RewardDisplayName = itemText,
            RewardQuantity = 1,
            ReceivedItemText = itemText
        };
        return true;
    }

    private static bool TryParseBadgeEarned(string body, out GameplayTelemetryObservation observation)
    {
        observation = null!;

        if (BadgeEarned().Match(body) is not { Success: true } match)
        {
            return false;
        }

        var badgeTitle = match.Groups["badge"].Value.Trim();
        if (string.IsNullOrEmpty(badgeTitle))
        {
            return false;
        }

        observation = new GameplayTelemetryObservation
        {
            GrammarId = GameplayTelemetryGrammarId.Oth02BadgeEarned,
            RewardCategory = GameplaySessionRewardCategory.Badge,
            RewardDisplayName = badgeTitle,
            RewardQuantity = 1
        };
        return true;
    }

    private static bool TryParseAmount(string text, out long amount)
    {
        if (!ValidCommaFormattedUnsignedInteger().IsMatch(text))
        {
            amount = 0;
            return false;
        }

        return long.TryParse(
            text.Replace(",", string.Empty),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out amount);
    }

    [GeneratedRegex(@"^(?:\d{1,3}(?:,\d{3})*|\d+)$", RegexOptions.CultureInvariant)]
    private static partial Regex ValidCommaFormattedUnsignedInteger();

    [GeneratedRegex(
        @"^You gain (?<xp>[\d,]+) experience, work off (?<debt>[\d,]+) debt, and gain (?<influence>[\d,]+) influence\.$",
        RegexOptions.CultureInvariant)]
    private static partial Regex Xp02ExperienceDebtAndInfluence();

    [GeneratedRegex(
        @"^You gain (?<xp>[\d,]+) experience and (?<influence>[\d,]+) influence\.$",
        RegexOptions.CultureInvariant)]
    private static partial Regex Xp01ExperienceAndInfluence();

    [GeneratedRegex(
        @"^You gain (?<xp>[\d,]+) experience and work off (?<debt>[\d,]+) debt\.$",
        RegexOptions.CultureInvariant)]
    private static partial Regex Xp04ExperienceAndDebt();

    [GeneratedRegex(@"^You gain (?<xp>[\d,]+) experience\.$", RegexOptions.CultureInvariant)]
    private static partial Regex Xp03ExperienceOnly();

    [GeneratedRegex(@"^You gain (?<influence>[\d,]+) influence\.$", RegexOptions.CultureInvariant)]
    private static partial Regex Inf01GameplayInfluenceOnly();

    [GeneratedRegex(@"^You received (?<item>.+)\.$", RegexOptions.CultureInvariant)]
    private static partial Regex GenericReceivedItem();

    [GeneratedRegex(
        @"^Congratulations! You earned the (?<badge>.+) badge\.$",
        RegexOptions.CultureInvariant)]
    private static partial Regex BadgeEarned();

    [GeneratedRegex(
        @"^Your combat improves to level (?<level>\d+)! Seek a trainer to further your abilities\.$",
        RegexOptions.CultureInvariant)]
    private static partial Regex CharacterLevelImprovement();
}
