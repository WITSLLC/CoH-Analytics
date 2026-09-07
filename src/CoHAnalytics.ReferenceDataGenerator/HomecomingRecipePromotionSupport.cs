using CoHAnalytics.ReferenceData;

namespace CoHAnalytics.ReferenceDataGenerator;

internal static class HomecomingRecipePromotionSupport
{
    internal const int MaxReferenceLevel = 50;
    internal const string MemorizedSourceSuffix = "_Memorized";

    internal static bool IsMemorizedSourceId(string homecomingSourceId) =>
        homecomingSourceId.EndsWith(MemorizedSourceSuffix, StringComparison.Ordinal);

    internal static IReadOnlyList<HomecomingLogicalRecipe> Group(
        IReadOnlyList<HomecomingRecipeCandidateRecord> candidates,
        IReadOnlyDictionary<string, ItemReferenceRecordDocument> producedEnhancementsById)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(producedEnhancementsById);

        var groups = candidates
            .GroupBy(candidate => candidate.ProducedEnhancementAppOwnedId, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => CreateLogicalRecipe(group.ToArray(), producedEnhancementsById))
            .ToArray();

        var duplicateNames = groups
            .GroupBy(recipe => recipe.CurrentDisplayName, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicateNames.Length > 0)
        {
            throw new HomecomingRecipePromotionException(
                "Logical Recipe grouping produced duplicate display names: " +
                string.Join(", ", duplicateNames));
        }

        return groups;
    }

    private static HomecomingLogicalRecipe CreateLogicalRecipe(
        IReadOnlyList<HomecomingRecipeCandidateRecord> rows,
        IReadOnlyDictionary<string, ItemReferenceRecordDocument> producedEnhancementsById)
    {
        var producedId = rows[0].ProducedEnhancementAppOwnedId;
        if (!producedEnhancementsById.TryGetValue(producedId, out var produced)
            || !string.Equals(produced.Family, nameof(ReferenceItemFamily.Enhancement), StringComparison.Ordinal))
        {
            throw new HomecomingRecipePromotionException(
                $"Recipe product '{producedId}' is not an existing Enhancement identity.");
        }

        if (rows.Any(row =>
                !string.Equals(row.ProducedEnhancementAppOwnedId, producedId, StringComparison.Ordinal)
                || !string.Equals(row.Rarity, rows[0].Rarity, StringComparison.Ordinal)))
        {
            throw new HomecomingRecipePromotionException(
                $"Recipe product '{producedId}' mixes produced Enhancement or rarity values.");
        }

        var included = rows
            .Where(row => !IsMemorizedSourceId(row.HomecomingSourceId) && row.Level <= MaxReferenceLevel)
            .OrderBy(row => row.Level)
            .ThenBy(row => row.HomecomingSourceId, StringComparer.Ordinal)
            .ToArray();
        if (included.Length == 0)
        {
            throw new HomecomingRecipePromotionException(
                $"Recipe product '{producedId}' has no Reference-selectable levels at or below {MaxReferenceLevel}.");
        }

        var duplicateLevels = included
            .GroupBy(row => row.Level)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicateLevels.Length > 0)
        {
            throw new HomecomingRecipePromotionException(
                $"Recipe product '{producedId}' has duplicate selectable levels {string.Join(", ", duplicateLevels)}.");
        }

        var displayNames = included
            .Select(row => row.DisplayName)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (displayNames.Length != 1)
        {
            throw new HomecomingRecipePromotionException(
                $"Recipe product '{producedId}' has conflicting selectable display names.");
        }

        var rarities = included
            .Select(row => row.Rarity)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (rarities.Length != 1)
        {
            throw new HomecomingRecipePromotionException(
                $"Recipe product '{producedId}' has conflicting rarity values.");
        }

        var icons = included
            .Select(row => string.IsNullOrWhiteSpace(row.Icon) ? null : row.Icon.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (icons.Length != 1)
        {
            throw new HomecomingRecipePromotionException(
                $"Recipe product '{producedId}' has conflicting icon identities.");
        }

        var excluded = rows
            .Where(row => !IsMemorizedSourceId(row.HomecomingSourceId) && row.Level > MaxReferenceLevel)
            .OrderBy(row => row.Level)
            .ThenBy(row => row.HomecomingSourceId, StringComparer.Ordinal)
            .ToArray();
        var highestIncluded = included[^1];
        foreach (var row in excluded)
        {
            if (!string.Equals(row.DisplayName, displayNames[0], StringComparison.Ordinal)
                || !string.Equals(row.Rarity, rarities[0], StringComparison.Ordinal)
                || !RequirementsEqual(row.Requirements, highestIncluded.Requirements))
            {
                throw new HomecomingRecipePromotionException(
                    $"Homecoming Recipe '{row.HomecomingSourceId}' level {row.Level} is not a crafting-cost variant of the level-{highestIncluded.Level} Recipe.");
            }
        }

        var aliasNames = rows
            .Select(row => row.DisplayName)
            .Where(name => !string.Equals(name, displayNames[0], StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        return new HomecomingLogicalRecipe(
            producedId,
            produced.EnhancementSetId,
            ResolveSubtype(produced),
            displayNames[0],
            rarities[0],
            icons[0],
            included,
            excluded,
            aliasNames,
            rows.Count(row => IsMemorizedSourceId(row.HomecomingSourceId)));
    }

    internal static string ResolveSubtype(ItemReferenceRecordDocument produced) =>
        string.IsNullOrWhiteSpace(produced.EnhancementSetId)
            ? produced.Subtype ?? "CraftedInvention"
            : "SetIO";

    private static bool RequirementsEqual(
        IReadOnlyList<HomecomingRecipeRequirementCandidate> left,
        IReadOnlyList<HomecomingRecipeRequirementCandidate> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        var leftOrdered = left
            .OrderBy(value => value.HomecomingSalvageSourceId, StringComparer.Ordinal)
            .ThenBy(value => value.SalvageAppOwnedId, StringComparer.Ordinal)
            .ToArray();
        var rightOrdered = right
            .OrderBy(value => value.HomecomingSalvageSourceId, StringComparer.Ordinal)
            .ThenBy(value => value.SalvageAppOwnedId, StringComparer.Ordinal)
            .ToArray();
        for (var index = 0; index < leftOrdered.Length; index++)
        {
            if (!string.Equals(
                    leftOrdered[index].SalvageAppOwnedId,
                    rightOrdered[index].SalvageAppOwnedId,
                    StringComparison.Ordinal)
                || !string.Equals(
                    leftOrdered[index].HomecomingSalvageSourceId,
                    rightOrdered[index].HomecomingSalvageSourceId,
                    StringComparison.Ordinal)
                || leftOrdered[index].Quantity != rightOrdered[index].Quantity)
            {
                return false;
            }
        }

        return true;
    }
}

internal sealed record HomecomingLogicalRecipe(
    string ProducedEnhancementId,
    string? EnhancementSetId,
    string Subtype,
    string CurrentDisplayName,
    string Rarity,
    string? Icon,
    IReadOnlyList<HomecomingRecipeCandidateRecord> SelectableLevels,
    IReadOnlyList<HomecomingRecipeCandidateRecord> ExcludedSourceLevels,
    IReadOnlyList<string> AdditionalDisplayNames,
    int MemorizedSourceCount);
