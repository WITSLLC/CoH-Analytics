using System.Globalization;
using System.Text.RegularExpressions;
using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

/// <summary>
/// Parses the raw, source-ordered build layout from an uncompressed Homecoming
/// <c>/buildsavefile</c> export.
/// </summary>
public static class HomecomingBuildLayoutParser
{
    private static readonly Regex CharacterHeaderRegex =
        new(@"^(.+)\: Level ([0-9]+) (\S+) (\S+)$", RegexOptions.CultureInvariant);

    private static readonly Regex PowerLineRegex =
        new(@"^Level ([0-9]+)\: (\S+) (\S+) (\S+)$", RegexOptions.CultureInvariant);

    private static readonly Regex EnhancementLineRegex =
        new(@"^(\S+?)(?: \(([0-9]+)(?:\+([0-9]+))?\))?$", RegexOptions.CultureInvariant);

    public static bool TryParse(string content, out HomecomingBuildLayoutSnapshot snapshot)
    {
        snapshot = null!;
        if (string.IsNullOrWhiteSpace(content)
            || content.Contains("|MBD;", StringComparison.Ordinal)
            || content.Contains("|MxDz;", StringComparison.Ordinal))
        {
            return false;
        }

        var normalized = content.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalized.Split('\n');
        string? characterName = null;
        int? characterLevel = null;
        string? rawClassToken = null;
        var powers = new List<PowerBuilder>();
        PowerBuilder? currentPower = null;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (string.Equals(line, "Badges Earned:", StringComparison.Ordinal))
            {
                break;
            }

            var headerMatch = CharacterHeaderRegex.Match(line);
            if (headerMatch.Success)
            {
                if (!TryParseInteger(headerMatch.Groups[2].Value, out var parsedCharacterLevel))
                {
                    return false;
                }

                characterName ??= headerMatch.Groups[1].Value.Trim();
                characterLevel ??= parsedCharacterLevel;
                rawClassToken ??= headerMatch.Groups[4].Value;
                currentPower = null;
                continue;
            }

            var powerMatch = PowerLineRegex.Match(line);
            if (powerMatch.Success)
            {
                if (!TryParseInteger(powerMatch.Groups[1].Value, out var acquisitionLevel))
                {
                    return false;
                }

                currentPower = new PowerBuilder(
                    acquisitionLevel,
                    powerMatch.Groups[2].Value,
                    powerMatch.Groups[3].Value,
                    powerMatch.Groups[4].Value,
                    powers.Count);
                powers.Add(currentPower);
                continue;
            }

            if (line.StartsWith("Level ", StringComparison.Ordinal)
                && line.Contains(':', StringComparison.Ordinal))
            {
                return false;
            }

            if (!char.IsWhiteSpace(rawLine[0]))
            {
                currentPower = null;
                continue;
            }

            if (currentPower is null)
            {
                continue;
            }

            if (!TryParseSlot(line, currentPower.Slots.Count, out var slot))
            {
                return false;
            }

            currentPower.Slots.Add(slot);
        }

        if (powers.Count == 0)
        {
            return false;
        }

        snapshot = new HomecomingBuildLayoutSnapshot(
            characterName,
            characterLevel,
            rawClassToken,
            powers.Select(power => power.ToSnapshot()));
        return true;
    }

    private static bool TryParseSlot(
        string line,
        int slotOrder,
        out HomecomingBuildSlotSnapshot slot)
    {
        if (string.Equals(line, "EMPTY", StringComparison.Ordinal))
        {
            slot = new HomecomingBuildSlotSnapshot(
                isEmpty: true,
                rawEnhancementToken: null,
                isAttuned: false,
                baseEnhancementLevel: null,
                boostValue: null,
                slotOrder);
            return true;
        }

        var match = EnhancementLineRegex.Match(line);
        if (!match.Success
            || string.Equals(match.Groups[1].Value, "EMPTY", StringComparison.Ordinal))
        {
            slot = null!;
            return false;
        }

        int? baseEnhancementLevel = null;
        if (match.Groups[2].Success)
        {
            if (!TryParseInteger(match.Groups[2].Value, out var parsedBaseLevel))
            {
                slot = null!;
                return false;
            }

            baseEnhancementLevel = parsedBaseLevel;
        }

        int? boostValue = null;
        if (match.Groups[3].Success)
        {
            if (!TryParseInteger(match.Groups[3].Value, out var parsedBoostValue))
            {
                slot = null!;
                return false;
            }

            boostValue = parsedBoostValue;
        }

        var rawToken = match.Groups[1].Value;
        slot = new HomecomingBuildSlotSnapshot(
            isEmpty: false,
            rawToken,
            IsAttunedEnhancementToken(rawToken),
            baseEnhancementLevel,
            boostValue,
            slotOrder);
        return true;
    }

    private static bool IsAttunedEnhancementToken(string rawToken) =>
        rawToken.StartsWith("Attuned_", StringComparison.OrdinalIgnoreCase)
        || rawToken.Contains("_Attuned_", StringComparison.OrdinalIgnoreCase);

    private static bool TryParseInteger(string value, out int result) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out result);

    private sealed class PowerBuilder(
        int acquisitionLevel,
        string rawCategoryToken,
        string rawPowerSetToken,
        string rawPowerToken,
        int sourceOrder)
    {
        public List<HomecomingBuildSlotSnapshot> Slots { get; } = [];

        public HomecomingBuildPowerSnapshot ToSnapshot() =>
            new(
                acquisitionLevel,
                rawCategoryToken,
                rawPowerSetToken,
                rawPowerToken,
                sourceOrder,
                Slots);
    }
}
