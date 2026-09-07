using System.Globalization;
using System.Text.RegularExpressions;

namespace CoHAnalytics.Services;

/// <summary>
/// Research-backed CUR-01 through CUR-09 reward currency recognition inside received-item text.
/// </summary>
internal static partial class GameplayRewardCurrencyGrammar
{
    private static readonly string[] SingularCurrencyNames =
    [
        "Reward Merit",
        "Astral Merit",
        "Empyrean Merit",
        "Incarnate Thread",
        "Incarnate Shard",
        "Enhancement Converter",
        "Enhancement Catalyst",
        "Enhancement Unslotter",
        "Unstable Aether",
        "Prismatic Aether",
        "Nightmare Obol"
    ];

    public static bool TryParseReceivedItemText(string itemText, out string currencyDisplayName, out long quantity)
    {
        currencyDisplayName = string.Empty;
        quantity = 0;

        if (string.IsNullOrWhiteSpace(itemText))
        {
            return false;
        }

        var trimmed = itemText.Trim();
        if (TryParseStandaloneMeritBody(trimmed, out currencyDisplayName, out quantity))
        {
            return true;
        }

        var unitsMatch = UnitsOfCurrency().Match(trimmed);
        if (unitsMatch.Success)
        {
            if (!TryParseQuantity(unitsMatch.Groups["qty"].Value, out quantity))
            {
                return false;
            }

            return TryMatchCurrencyName(unitsMatch.Groups["name"].Value, out currencyDisplayName);
        }

        if (TryMatchCurrencyName(trimmed, out currencyDisplayName))
        {
            quantity = 1;
            return true;
        }

        return false;
    }

    public static bool TryParseStandaloneMeritBody(string body, out string currencyDisplayName, out long quantity)
    {
        currencyDisplayName = string.Empty;
        quantity = 0;

        var trimmed = body.Trim();
        var rewardMerits = StandaloneRewardMerits().Match(trimmed);
        if (rewardMerits.Success && TryParseQuantity(rewardMerits.Groups["qty"].Value, out quantity))
        {
            currencyDisplayName = "Reward Merit";
            return true;
        }

        var bonusMerits = StandaloneBonusRewardMerits().Match(trimmed);
        if (bonusMerits.Success && TryParseQuantity(bonusMerits.Groups["qty"].Value, out quantity))
        {
            currencyDisplayName = "Reward Merit";
            return true;
        }

        var unitsMerit = StandaloneUnitsOfRewardMerit().Match(trimmed);
        if (unitsMerit.Success && TryParseQuantity(unitsMerit.Groups["qty"].Value, out quantity))
        {
            currencyDisplayName = "Reward Merit";
            return true;
        }

        return false;
    }

    public static bool IsArchitectTicketStatus(string body) =>
        ArchitectTicketStatus().IsMatch(body.Trim());

    private static bool TryMatchCurrencyName(string name, out string currencyDisplayName)
    {
        foreach (var candidate in SingularCurrencyNames)
        {
            if (string.Equals(name.Trim(), candidate, StringComparison.OrdinalIgnoreCase))
            {
                currencyDisplayName = candidate;
                return true;
            }
        }

        currencyDisplayName = string.Empty;
        return false;
    }

    private static bool TryParseQuantity(string text, out long quantity)
    {
        if (!ValidQuantity().IsMatch(text))
        {
            quantity = 0;
            return false;
        }

        return long.TryParse(
            text.Replace(",", string.Empty),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out quantity);
    }

    [GeneratedRegex(@"^(?:\d{1,3}(?:,\d{3})*|\d+)$", RegexOptions.CultureInvariant)]
    private static partial Regex ValidQuantity();

    [GeneratedRegex(@"^(?<qty>(?:\d{1,3}(?:,\d{3})*|\d+)) units of (?<name>.+)$", RegexOptions.CultureInvariant)]
    private static partial Regex UnitsOfCurrency();

    [GeneratedRegex(@"^(?<qty>(?:\d{1,3}(?:,\d{3})*|\d+)) reward merits\.?$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex StandaloneRewardMerits();

    [GeneratedRegex(@"^(?<qty>(?:\d{1,3}(?:,\d{3})*|\d+)) bonus reward merits\.?$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex StandaloneBonusRewardMerits();

    [GeneratedRegex(@"^(?<qty>(?:\d{1,3}(?:,\d{3})*|\d+)) units of Reward Merit\.?$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex StandaloneUnitsOfRewardMerit();

    [GeneratedRegex(@"^You can claim \d+ tickets from Architect Entertainment\.?$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex ArchitectTicketStatus();
}
