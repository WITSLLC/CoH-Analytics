using System.Text.RegularExpressions;
using CoHAnalytics.ReferenceData;

namespace CoHAnalytics.ReferenceDataGenerator;

internal static class HomecomingEnhancementFamilySupport
{
    private static readonly string[] OriginBoostTypes =
    [
        "Science", "Mutation", "Magic", "Technology", "Natural"
    ];

    internal static ReferenceEnhancementFamily ClassifyLogicalEnhancement(
        string displayName,
        IReadOnlyList<(string HomecomingSourceId, HomecomingBoostDiscoveryRecord Discovery)> sourceVariants)
    {
        if (sourceVariants.Count == 0)
        {
            throw new HomecomingEnhancementPromotionException(
                $"Logical Enhancement '{displayName}' has no source variants.");
        }

        var families = sourceVariants
            .Select(variant => ClassifySource(variant.HomecomingSourceId, variant.Discovery))
            .Distinct()
            .ToArray();

        if (families.Length != 1)
        {
            throw new HomecomingEnhancementPromotionException(
                $"Logical Enhancement '{displayName}' has mixed structural families: " +
                string.Join(", ", families.OrderBy(value => value.ToString(), StringComparer.Ordinal)));
        }

        return families[0];
    }

    internal static ReferenceEnhancementFamily ClassifySource(
        string sourceId,
        HomecomingBoostDiscoveryRecord discovery)
    {
        if (IsCraftedLevelVariantId(sourceId)
            || (sourceId.StartsWith("Boosts.Crafted_", StringComparison.Ordinal)
                && discovery.NonOriginBoostTypes.Count == 1
                && discovery.BoostsAllowed.Count(value => OriginBoostTypes.Contains(value, StringComparer.Ordinal)) == 5))
        {
            if (sourceId.StartsWith("Boosts.Crafted_", StringComparison.Ordinal)
                && !sourceId.Contains("Hamidon", StringComparison.OrdinalIgnoreCase)
                && discovery.NonOriginBoostTypes.All(value =>
                    !string.Equals(value, "Hamidon", StringComparison.Ordinal)))
            {
                return ReferenceEnhancementFamily.CraftedInvention;
            }
        }

        if (discovery.NonOriginBoostTypes.Contains("Hamidon", StringComparer.Ordinal)
            || sourceId.Contains("Hamidon", StringComparison.OrdinalIgnoreCase)
            || sourceId.Contains("Hydra", StringComparison.OrdinalIgnoreCase)
            || sourceId.Contains("Titan", StringComparison.OrdinalIgnoreCase)
            || sourceId.Contains("Yin", StringComparison.OrdinalIgnoreCase)
            || sourceId.Contains("Synthetic", StringComparison.OrdinalIgnoreCase)
            || sourceId.Contains("DSync", StringComparison.OrdinalIgnoreCase)
            || sourceId.Contains("D_Sync", StringComparison.OrdinalIgnoreCase))
        {
            if (sourceId.Contains("Hydra", StringComparison.OrdinalIgnoreCase))
            {
                return ReferenceEnhancementFamily.Hydra;
            }

            if (sourceId.Contains("Titan", StringComparison.OrdinalIgnoreCase))
            {
                return ReferenceEnhancementFamily.Titan;
            }

            if (sourceId.Contains("Yin", StringComparison.OrdinalIgnoreCase))
            {
                return ReferenceEnhancementFamily.Yin;
            }

            if (sourceId.Contains("Synthetic", StringComparison.OrdinalIgnoreCase))
            {
                return ReferenceEnhancementFamily.Synthetic;
            }

            if (sourceId.Contains("DSync", StringComparison.OrdinalIgnoreCase)
                || sourceId.Contains("D_Sync", StringComparison.OrdinalIgnoreCase))
            {
                return ReferenceEnhancementFamily.DSync;
            }

            return ReferenceEnhancementFamily.Hamidon;
        }

        if (sourceId.StartsWith("Boosts.Crafted_", StringComparison.Ordinal)
            && !sourceId.Contains("Hamidon", StringComparison.OrdinalIgnoreCase))
        {
            return ReferenceEnhancementFamily.CraftedInvention;
        }

        if (discovery.NonOriginBoostTypes.Count == 0)
        {
            return ReferenceEnhancementFamily.OriginOrTraining;
        }

        if (discovery.NonOriginBoostTypes.Count == 1
            && discovery.BoostsAllowed.Count(value => OriginBoostTypes.Contains(value, StringComparer.Ordinal)) >= 1
            && (sourceId.StartsWith("Boosts.Generic_", StringComparison.Ordinal)
                || sourceId.StartsWith("Boosts.Magic_", StringComparison.Ordinal)
                || sourceId.StartsWith("Boosts.Mutation_", StringComparison.Ordinal)
                || sourceId.StartsWith("Boosts.Natural_", StringComparison.Ordinal)
                || sourceId.StartsWith("Boosts.Science_", StringComparison.Ordinal)
                || sourceId.StartsWith("Boosts.Technology_", StringComparison.Ordinal)))
        {
            return ReferenceEnhancementFamily.OriginOrTraining;
        }

        if (discovery.NonOriginBoostTypes.Count > 1)
        {
            throw new HomecomingEnhancementPromotionException(
                $"Boost '{sourceId}' has multiple non-origin types without a proven special-family identity.");
        }

        throw new HomecomingEnhancementPromotionException(
            $"Boost '{sourceId}' could not be structurally classified into a proven Enhancement family.");
    }

    internal static string? ResolveApplicabilityKey(IReadOnlyList<string> nonOriginBoostTypes)
    {
        if (nonOriginBoostTypes.Count == 0)
        {
            return null;
        }

        return string.Join("+", nonOriginBoostTypes.Order(StringComparer.Ordinal));
    }

    internal static string? ResolveApplicabilityDisplayText(
        ReferenceEnhancementFamily family,
        IReadOnlyList<string> nonOriginBoostTypes)
    {
        if (family != ReferenceEnhancementFamily.CraftedInvention)
        {
            return null;
        }

        if (nonOriginBoostTypes.Count != 1)
        {
            return null;
        }

        return HomecomingPresentationLabels.TryGetCommonIoBoostTypeDisplayText(nonOriginBoostTypes[0]);
    }

    private static bool IsCraftedLevelVariantId(string sourceId) =>
        Regex.IsMatch(
            sourceId,
            @"^Boosts\.Crafted_.+_\d+\.Crafted_.+_\d+$",
            RegexOptions.CultureInvariant);
}
