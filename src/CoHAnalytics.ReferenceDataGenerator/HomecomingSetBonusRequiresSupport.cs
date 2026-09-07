using CoHAnalytics.ReferenceData;

namespace CoHAnalytics.ReferenceDataGenerator;

internal static class HomecomingSetBonusRequiresSupport
{
    private static readonly HashSet<string> OperatorTokens = new(StringComparer.Ordinal)
    {
        ">=",
        "<=",
        "==",
        "!=",
        ">",
        "<",
        "||",
        "&&",
        "and",
        "or"
    };

    internal static ReferenceEnhancementSetBonusRequiresPattern ClassifyPattern(
        IReadOnlyList<string> requiresTokens)
    {
        if (requiresTokens.Count == 0)
        {
            return ReferenceEnhancementSetBonusRequiresPattern.None;
        }

        if (requiresTokens.Count == 1
            && string.Equals(requiresTokens[0], "isPVPMap?", StringComparison.Ordinal))
        {
            return ReferenceEnhancementSetBonusRequiresPattern.PvPMap;
        }

        if (requiresTokens.Any(token =>
                token.Contains("PowerBoostsSlotted", StringComparison.Ordinal)))
        {
            return ReferenceEnhancementSetBonusRequiresPattern.PieceGate;
        }

        return ReferenceEnhancementSetBonusRequiresPattern.Other;
    }

    internal static IReadOnlyList<string> ResolveRequiredEnhancementIds(
        string homecomingSetId,
        string setAppOwnedId,
        IReadOnlyList<string> requiresTokens,
        IReadOnlyList<IReadOnlyList<string>> memberGroups,
        IReadOnlyList<HomecomingEnhancementCandidateRecord> setEnhancements)
    {
        if (ClassifyPattern(requiresTokens) != ReferenceEnhancementSetBonusRequiresPattern.PieceGate)
        {
            return Array.Empty<string>();
        }

        var resolved = new HashSet<string>(StringComparer.Ordinal);
        foreach (var token in ExtractPieceReferenceTokens(requiresTokens))
        {
            var memberGroup = FindMemberGroupForToken(memberGroups, token);
            if (memberGroup is null)
            {
                throw new HomecomingEnhancementPromotionException(
                    $"Boost Set '{homecomingSetId}' Requires token '{token}' did not match any member group.");
            }

            var memberSources = memberGroup.ToHashSet(StringComparer.Ordinal);
            var matches = setEnhancements
                .Where(enhancement =>
                    enhancement.SourceVariants.Any(variant =>
                        memberSources.Contains(variant.HomecomingSourceId)))
                .Select(enhancement => enhancement.AppOwnedId)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            if (matches.Length != 1)
            {
                throw new HomecomingEnhancementPromotionException(
                    $"Boost Set '{homecomingSetId}' Requires token '{token}' resolved to " +
                    $"{matches.Length} logical Enhancements in set '{setAppOwnedId}'.");
            }

            resolved.Add(matches[0]!);
        }

        return resolved
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<string>? FindMemberGroupForToken(
        IReadOnlyList<IReadOnlyList<string>> memberGroups,
        string token)
    {
        IReadOnlyList<string>? match = null;
        foreach (var group in memberGroups)
        {
            if (!group.Any(sourceId => SourceMatchesPieceToken(sourceId, token)))
            {
                continue;
            }

            if (match is not null)
            {
                throw new HomecomingEnhancementPromotionException(
                    $"Requires token '{token}' matched multiple member groups.");
            }

            match = group;
        }

        return match;
    }

    private static bool SourceMatchesPieceToken(string sourceId, string token) =>
        sourceId.StartsWith("Boosts.", StringComparison.Ordinal)
        && (sourceId.Contains($".{token}.", StringComparison.Ordinal)
            || sourceId.EndsWith($".{token}", StringComparison.Ordinal));

    private static IEnumerable<string> ExtractPieceReferenceTokens(IReadOnlyList<string> tokens)
    {
        foreach (var token in tokens)
        {
            if (OperatorTokens.Contains(token)
                || string.Equals(token, "PowerBoostsSlotted>", StringComparison.Ordinal)
                || uint.TryParse(token, out _))
            {
                continue;
            }

            if (token.StartsWith("Crafted_", StringComparison.Ordinal)
                || token.StartsWith("Attuned_", StringComparison.Ordinal)
                || token.StartsWith("Superior_", StringComparison.Ordinal))
            {
                yield return token;
            }
        }
    }
}
