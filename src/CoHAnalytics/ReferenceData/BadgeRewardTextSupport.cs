using System.Text.RegularExpressions;

namespace CoHAnalytics.ReferenceData;

/// <summary>
/// Resolves and normalizes source-backed reward and requirement text for badge catalog records.
/// </summary>
public static class BadgeRewardTextSupport
{
    private const string FiveRewardMerits = "5 Reward Merits";

    private const string DayJobChargeFallbackSentence =
        "Additional charges are earned while logged out in the appropriate Day Job location.";

    private static readonly Regex InternalResearchDocumentReference = new(
        @";?\s*see\s+[`""']?(?:\d+\s*-\s*)?[^`""']+\.md[`""']?\s+for\s+names\s+and\s+logic\s+warnings\.?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex GenericMarkdownResearchReference = new(
        @";?\s*see\s+[`""'][^`""']+\.md[`""'][^.]*\.?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex DayJobChargeFormulaSuffix = new(
        @";\s*For every (?<hours>[XYZ]|\d+) hours logged out, a character gains 1 charge, up to a maximum of (?<max>[XYZ]|\d+) charges\.?\s*",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex DayJobPowerFromDescription = new(
        @"\byour (.+?) power\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex DayJobPowerFromChargesOf = new(
        @"\bcharges of (.+?)(?:\.|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static IReadOnlySet<string> BuildExplorationZoneCompletionDisplayNames(
        IEnumerable<string> completionBadgeLists)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var completionList in completionBadgeLists)
        {
            foreach (var name in SplitCompletionBadgeNames(completionList))
            {
                if (IsIdentifiedCompletionBadgeName(name))
                {
                    names.Add(name);
                }
            }
        }

        return names;
    }

    public static string? NormalizeRequirementText(string? requirementText)
    {
        if (string.IsNullOrWhiteSpace(requirementText))
        {
            return null;
        }

        var normalized = InternalResearchDocumentReference.Replace(requirementText.Trim(), string.Empty);
        normalized = GenericMarkdownResearchReference.Replace(normalized, string.Empty);
        normalized = normalized.Trim().TrimEnd(';').Trim();

        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    public static string? NormalizeAccoladeRewardPower(
        string? rewardPower,
        string? heroDescription = null,
        string? villainDescription = null)
    {
        if (string.IsNullOrWhiteSpace(rewardPower))
        {
            return null;
        }

        var trimmed = rewardPower.Trim();
        if (trimmed.StartsWith("No separate power or reward confirmed", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("This Accolade has no known power", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (trimmed.StartsWith("Awards ", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed["Awards ".Length..].Trim();
        }

        return NormalizeDayJobChargeRewardPlaceholders(trimmed, heroDescription, villainDescription);
    }

    public static bool ContainsUnresolvedDayJobChargePlaceholder(string? rewardText) =>
        !string.IsNullOrWhiteSpace(rewardText)
        && DayJobChargeFormulaSuffix.IsMatch(rewardText)
        && (rewardText.Contains(" every X ", StringComparison.OrdinalIgnoreCase)
            || rewardText.Contains(" every Y ", StringComparison.OrdinalIgnoreCase)
            || rewardText.Contains(" maximum of Z ", StringComparison.OrdinalIgnoreCase)
            || rewardText.Contains(" maximum of Y ", StringComparison.OrdinalIgnoreCase)
            || rewardText.Contains(" maximum of X ", StringComparison.OrdinalIgnoreCase));

    public static bool ContainsInternalResearchDocumentReference(string? requirementText) =>
        !string.IsNullOrWhiteSpace(requirementText)
        && (InternalResearchDocumentReference.IsMatch(requirementText)
            || GenericMarkdownResearchReference.IsMatch(requirementText));

    public static string? ResolveRewardText(
        string homecomingSourceId,
        string heroName,
        string? villainName,
        string? accoladeRewardPower,
        IReadOnlySet<string> explorationZoneCompletionDisplayNames,
        string? heroDescription = null,
        string? villainDescription = null)
    {
        var fromResearch = NormalizeAccoladeRewardPower(
            accoladeRewardPower,
            heroDescription,
            villainDescription);
        if (!string.IsNullOrWhiteSpace(fromResearch))
        {
            return fromResearch;
        }

        if (!IsExplorationZoneCompletionBadge(heroName, villainName, explorationZoneCompletionDisplayNames))
        {
            return null;
        }

        if (homecomingSourceId.EndsWith("Explorer", StringComparison.Ordinal))
        {
            return FiveRewardMerits;
        }

        return null;
    }

    public static string? ResolveRewardText(BadgeReferenceRecord badge, string? accoladeRewardPower) =>
        ResolveRewardText(
            badge.HomecomingSourceId,
            badge.HeroName,
            badge.VillainName,
            accoladeRewardPower,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            badge.HeroDescription,
            badge.VillainDescription);

    internal static bool IsExplorationZoneCompletionBadge(
        string heroName,
        string? villainName,
        IReadOnlySet<string> explorationZoneCompletionDisplayNames) =>
        explorationZoneCompletionDisplayNames.Contains(heroName)
        || (!string.IsNullOrWhiteSpace(villainName)
            && explorationZoneCompletionDisplayNames.Contains(villainName));

    internal static IEnumerable<string> SplitCompletionBadgeNames(string completionBadges) =>
        completionBadges.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    internal static bool IsIdentifiedCompletionBadgeName(string name) =>
        !string.IsNullOrWhiteSpace(name)
        && !name.Contains("None identified", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeDayJobChargeRewardPlaceholders(
        string rewardText,
        string? heroDescription,
        string? villainDescription)
    {
        var match = DayJobChargeFormulaSuffix.Match(rewardText);
        if (!match.Success)
        {
            return rewardText;
        }

        var effects = rewardText[..match.Index].Trim().TrimEnd(';').Trim();
        var powerName = TryExtractDayJobPowerName(heroDescription, villainDescription, effects);
        var chargeSentence = BuildDayJobChargeSentence(match.Groups["max"].Value);

        if (!string.IsNullOrWhiteSpace(powerName)
            && effects.StartsWith(powerName, StringComparison.OrdinalIgnoreCase))
        {
            effects = effects[powerName.Length..].TrimStart(';', ' ').Trim();
        }

        return string.IsNullOrWhiteSpace(powerName)
            ? $"{effects}. {chargeSentence}"
            : $"{powerName} — {effects}. {chargeSentence}";
    }

    private static string BuildDayJobChargeSentence(string maxToken)
    {
        if (IsUnresolvedFormulaToken(maxToken))
        {
            return DayJobChargeFallbackSentence;
        }

        return $"{DayJobChargeFallbackSentence.TrimEnd('.')}, up to a maximum of {maxToken} charges.";
    }

    private static string? TryExtractDayJobPowerName(
        string? heroDescription,
        string? villainDescription,
        string effects)
    {
        foreach (var description in new[] { heroDescription, villainDescription })
        {
            if (string.IsNullOrWhiteSpace(description))
            {
                continue;
            }

            var powerMatch = DayJobPowerFromDescription.Match(description);
            if (powerMatch.Success)
            {
                return powerMatch.Groups[1].Value.Trim();
            }

            var chargesMatch = DayJobPowerFromChargesOf.Match(description);
            if (chargesMatch.Success)
            {
                return chargesMatch.Groups[1].Value.Trim();
            }
        }

        var semicolonIndex = effects.IndexOf(';', StringComparison.Ordinal);
        if (semicolonIndex <= 0)
        {
            return null;
        }

        var prefix = effects[..semicolonIndex].Trim();
        return prefix.Contains("Ranged", StringComparison.OrdinalIgnoreCase)
            || prefix.Contains("Target", StringComparison.OrdinalIgnoreCase)
            || prefix.Contains("Melee", StringComparison.OrdinalIgnoreCase)
            ? null
            : prefix;
    }

    private static bool IsUnresolvedFormulaToken(string token) =>
        token is "X" or "Y" or "Z";
}
