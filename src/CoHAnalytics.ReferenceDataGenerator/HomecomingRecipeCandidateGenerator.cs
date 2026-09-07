using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using CoHAnalytics.ReferenceData;

namespace CoHAnalytics.ReferenceDataGenerator;

internal static class HomecomingRecipeCandidateGenerator
{
    internal const string SchemaVersion = "homecoming-recipe-candidate-v1";
    internal const string SourceArchive = "assets/live/bin.pigg";
    internal const string SourceMember = "bin/baserecipes.bin";
    internal const string InventionWorktable = "Worktable_Invention";

    internal static HomecomingRecipeCandidateDocument Create(
        IReadOnlyList<HomecomingBaseRecipeRecord> baseRecipes,
        HomecomingMessageStore messageStore,
        HomecomingSalvageCandidateDocument salvageCandidate,
        HomecomingEnhancementCandidateDocument enhancementCandidate,
        IReadOnlyList<CurrentRecipeIdentity> currentRecipes,
        string buildVersion,
        string packageRevision)
    {
        ArgumentNullException.ThrowIfNull(baseRecipes);
        ArgumentNullException.ThrowIfNull(messageStore);
        ArgumentNullException.ThrowIfNull(salvageCandidate);
        ArgumentNullException.ThrowIfNull(enhancementCandidate);
        ArgumentNullException.ThrowIfNull(currentRecipes);
        ArgumentException.ThrowIfNullOrWhiteSpace(buildVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageRevision);

        var sourceRecipes = baseRecipes
            .Where(IsInventionEnhancementRecipe)
            .OrderBy(recipe => recipe.HomecomingSourceId, StringComparer.Ordinal)
            .ToArray();
        var productIndex = CreateProductIndex(enhancementCandidate.Enhancements);
        var resolvedProducts = ResolveProducts(sourceRecipes, productIndex);
        var assignedEnhancementIds = AssignMissingEnhancementIds(
            resolvedProducts,
            enhancementCandidate.Enhancements);
        var salvageIndex = CreateSalvageIndex(salvageCandidate.Records);

        var maximumRecipeId = ValidateCurrentRecipes(currentRecipes);
        var records = new List<HomecomingRecipeCandidateRecord>(sourceRecipes.Length);
        for (var index = 0; index < sourceRecipes.Length; index++)
        {
            var recipe = sourceRecipes[index];
            var product = resolvedProducts[index];
            var displayName = ResolveDisplayName(recipe, messageStore);
            var rarity = RarityName(recipe.Rarity, recipe.HomecomingSourceId);
            var craftingCost = ReadCraftingCost(recipe);
            var requirements = ResolveRequirements(recipe, salvageIndex);
            var appOwnedId = FormatRecipeId(checked(maximumRecipeId + index + 1));
            var logicalKey = CreateLogicalEnhancementKey(product.Candidate);
            var producedEnhancementId = product.Candidate.AppOwnedId
                ?? assignedEnhancementIds.ByLogicalKey[logicalKey];

            records.Add(new HomecomingRecipeCandidateRecord(
                appOwnedId,
                recipe.HomecomingSourceId,
                recipe.DisplayNameMessageKey,
                displayName,
                recipe.Icon,
                producedEnhancementId,
                recipe.ProductSourceId,
                product.Variant.HomecomingSourceId,
                product.UsedCaseInsensitiveJoin,
                rarity,
                recipe.Rarity,
                recipe.Level,
                craftingCost,
                recipe.WorktableIds.Order(StringComparer.Ordinal).ToArray(),
                requirements,
                "NewFromHomecoming"));
        }

        var rarityCounts = Enumerable.Range(1, 4)
            .Select(value => new HomecomingRecipeValueCount(
                RarityName(checked((uint)value), "summary"),
                records.Count(record => record.RarityRawCode == value)))
            .ToArray();
        var levelCounts = records
            .GroupBy(record => record.Level)
            .OrderBy(group => group.Key)
            .Select(group => new HomecomingRecipeLevelCount(group.Key, group.Count()))
            .ToArray();
        var assignedIds = assignedEnhancementIds.Records;

        return new HomecomingRecipeCandidateDocument(
            SchemaVersion,
            new HomecomingRecipeCandidateSource(
                buildVersion,
                packageRevision,
                SourceArchive,
                SourceMember),
            new HomecomingRecipeCandidateSummary(
                baseRecipes.Count,
                records.Count,
                records.Count,
                resolvedProducts.Select(value => CreateLogicalEnhancementKey(value.Candidate))
                    .Distinct(StringComparer.Ordinal)
                    .Count(),
                records.Count,
                records.Count(record => record.UsedCaseInsensitiveProductJoin),
                0,
                records.Count(record => assignedIds.Any(
                    assigned => assigned.AppOwnedId == record.ProducedEnhancementAppOwnedId)),
                assignedIds.Count,
                records.Sum(record => record.Requirements.Count),
                records.SelectMany(record => record.Requirements)
                    .Select(requirement => requirement.HomecomingSalvageSourceId)
                    .Distinct(StringComparer.Ordinal)
                    .Count(),
                records.Count,
                rarityCounts,
                records.Count == 0 ? null : records.Min(record => record.Level),
                records.Count == 0 ? null : records.Max(record => record.Level),
                levelCounts,
                records.Count,
                records.Count,
                0),
            assignedIds,
            records,
            currentRecipes
                .OrderBy(recipe => recipe.AppOwnedId, StringComparer.Ordinal)
                .Select(recipe => new HomecomingCurrentRecipeRecord(
                    recipe.AppOwnedId,
                    recipe.DisplayName))
                .ToArray());
    }

    internal static IReadOnlyList<CurrentRecipeIdentity> LoadEmbeddedCurrentCatalog()
    {
        var assembly = typeof(ItemReferenceCatalogFactory).Assembly;
        using var stream = assembly.GetManifestResourceStream(
            ItemReferenceCatalogFactory.ProductionCatalogResourceName)
            ?? throw new HomecomingRecipeCandidateException(
                "Embedded production item catalog was not found.");
        using var document = JsonDocument.Parse(stream);

        var recipes = new List<CurrentRecipeIdentity>();
        foreach (var item in document.RootElement.GetProperty("items").EnumerateArray())
        {
            if (!string.Equals(
                    item.GetProperty("family").GetString(),
                    "Recipe",
                    StringComparison.Ordinal))
            {
                continue;
            }

            recipes.Add(new CurrentRecipeIdentity(
                RequireString(item, "catalogItemId"),
                RequireString(item, "currentDisplayName")));
        }

        return recipes;
    }

    private static bool IsInventionEnhancementRecipe(HomecomingBaseRecipeRecord recipe) =>
        recipe.WorktableIds.Contains(InventionWorktable, StringComparer.Ordinal)
        && recipe.ProductSourceId.StartsWith("Boosts.", StringComparison.Ordinal);

    private static ProductIndex CreateProductIndex(
        IReadOnlyList<HomecomingEnhancementCandidateRecord> enhancements)
    {
        var exact = new Dictionary<string, ProductTarget>(StringComparer.Ordinal);
        var insensitive = new Dictionary<string, ProductTarget>(StringComparer.OrdinalIgnoreCase);
        foreach (var enhancement in enhancements)
        {
            foreach (var variant in enhancement.SourceVariants)
            {
                var target = new ProductTarget(enhancement, variant);
                if (!exact.TryAdd(variant.HomecomingSourceId, target))
                {
                    throw new HomecomingRecipeCandidateException(
                        $"Enhancement source ID '{variant.HomecomingSourceId}' is duplicated.");
                }

                if (insensitive.TryGetValue(variant.HomecomingSourceId, out var collision))
                {
                    throw new HomecomingRecipeCandidateException(
                        "Case-insensitive Enhancement source-ID collision between " +
                        $"'{collision.Variant.HomecomingSourceId}' and " +
                        $"'{variant.HomecomingSourceId}'.");
                }

                insensitive.Add(variant.HomecomingSourceId, target);
            }
        }

        return new ProductIndex(exact, insensitive);
    }

    private static IReadOnlyList<ResolvedProduct> ResolveProducts(
        IReadOnlyList<HomecomingBaseRecipeRecord> recipes,
        ProductIndex index)
    {
        var resolved = new List<ResolvedProduct>(recipes.Count);
        foreach (var recipe in recipes)
        {
            if (index.Exact.TryGetValue(recipe.ProductSourceId, out var exact))
            {
                resolved.Add(new ResolvedProduct(exact.Candidate, exact.Variant, false));
                continue;
            }

            if (!index.OrdinalIgnoreCase.TryGetValue(recipe.ProductSourceId, out var insensitive))
            {
                throw new HomecomingRecipeCandidateException(
                    $"Base Recipe '{recipe.HomecomingSourceId}' product source ID " +
                    $"'{recipe.ProductSourceId}' does not resolve to a Homecoming Enhancement candidate.");
            }

            resolved.Add(new ResolvedProduct(insensitive.Candidate, insensitive.Variant, true));
        }

        return resolved;
    }

    private static AssignedEnhancementIdResult AssignMissingEnhancementIds(
        IReadOnlyList<ResolvedProduct> products,
        IReadOnlyList<HomecomingEnhancementCandidateRecord> allEnhancements)
    {
        var usedIds = new HashSet<string>(StringComparer.Ordinal);
        var maximumId = 0;
        foreach (var enhancement in allEnhancements.Where(value => value.AppOwnedId is not null))
        {
            if (!usedIds.Add(enhancement.AppOwnedId!))
            {
                throw new HomecomingRecipeCandidateException(
                    $"Enhancement candidate ID '{enhancement.AppOwnedId}' is duplicated.");
            }

            maximumId = Math.Max(maximumId, ParseIdentifier(enhancement.AppOwnedId!, "ENH"));
        }

        var needed = products
            .Select(product => product.Candidate)
            .Where(candidate => candidate.AppOwnedId is null)
            .GroupBy(CreateLogicalEnhancementKey, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(CreateLogicalEnhancementKey, StringComparer.Ordinal)
            .ToArray();
        var byLogicalKey = new Dictionary<string, string>(StringComparer.Ordinal);
        var assigned = new List<HomecomingRecipeAssignedEnhancementId>(needed.Length);
        foreach (var enhancement in needed)
        {
            maximumId = checked(maximumId + 1);
            if (maximumId > 99999)
            {
                throw new HomecomingRecipeCandidateException(
                    "No ENH- identifier remains available for Recipe product candidates.");
            }

            var appOwnedId = $"ENH-{maximumId:D5}";
            Require(usedIds.Add(appOwnedId),
                $"Assigned Enhancement candidate ID '{appOwnedId}' is already in use.");
            var logicalKey = CreateLogicalEnhancementKey(enhancement);
            byLogicalKey.Add(logicalKey, appOwnedId);
            assigned.Add(new HomecomingRecipeAssignedEnhancementId(
                appOwnedId,
                enhancement.DisplayName,
                logicalKey,
                enhancement.SourceVariants
                    .Select(variant => variant.HomecomingSourceId)
                    .Order(StringComparer.Ordinal)
                    .ToArray()));
        }

        return new AssignedEnhancementIdResult(byLogicalKey, assigned);
    }

    private static Dictionary<string, HomecomingSalvageCandidateRecord> CreateSalvageIndex(
        IReadOnlyList<HomecomingSalvageCandidateRecord> salvage)
    {
        var index = new Dictionary<string, HomecomingSalvageCandidateRecord>(StringComparer.Ordinal);
        foreach (var item in salvage)
        {
            if (!index.TryAdd(item.HomecomingSourceId, item))
            {
                throw new HomecomingRecipeCandidateException(
                    $"Salvage source ID '{item.HomecomingSourceId}' is duplicated.");
            }
        }

        return index;
    }

    private static IReadOnlyList<HomecomingRecipeRequirementCandidate> ResolveRequirements(
        HomecomingBaseRecipeRecord recipe,
        IReadOnlyDictionary<string, HomecomingSalvageCandidateRecord> salvageIndex)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var requirements = new List<HomecomingRecipeRequirementCandidate>(recipe.Requirements.Count);
        foreach (var requirement in recipe.Requirements
                     .OrderBy(value => value.HomecomingSalvageSourceId, StringComparer.Ordinal))
        {
            Require(requirement.Quantity > 0,
                $"Base Recipe '{recipe.HomecomingSourceId}' has a non-positive salvage quantity.");
            Require(seen.Add(requirement.HomecomingSalvageSourceId),
                $"Base Recipe '{recipe.HomecomingSourceId}' repeats salvage requirement " +
                $"'{requirement.HomecomingSalvageSourceId}'.");
            if (!salvageIndex.TryGetValue(requirement.HomecomingSalvageSourceId, out var salvage))
            {
                throw new HomecomingRecipeCandidateException(
                    $"Base Recipe '{recipe.HomecomingSourceId}' salvage source ID " +
                    $"'{requirement.HomecomingSalvageSourceId}' is unresolved.");
            }

            Require(salvage.AppOwnedId is not null,
                $"Base Recipe '{recipe.HomecomingSourceId}' salvage source ID " +
                $"'{requirement.HomecomingSalvageSourceId}' has no app-owned candidate ID.");
            requirements.Add(new HomecomingRecipeRequirementCandidate(
                salvage.AppOwnedId!,
                requirement.HomecomingSalvageSourceId,
                requirement.Quantity));
        }

        Require(requirements.Count > 0,
            $"Base Recipe '{recipe.HomecomingSourceId}' has no salvage requirements.");
        return requirements;
    }

    private static string ResolveDisplayName(
        HomecomingBaseRecipeRecord recipe,
        HomecomingMessageStore messageStore)
    {
        if (!messageStore.TryResolve(recipe.DisplayNameMessageKey, out var displayName))
        {
            throw new HomecomingRecipeCandidateException(
                $"Base Recipe '{recipe.HomecomingSourceId}' display-name message key " +
                $"'{recipe.DisplayNameMessageKey}' is unresolved.");
        }

        Require(!string.IsNullOrWhiteSpace(displayName),
            $"Base Recipe '{recipe.HomecomingSourceId}' display-name message key " +
            $"'{recipe.DisplayNameMessageKey}' resolved to an empty value.");
        return displayName;
    }

    private static uint ReadCraftingCost(HomecomingBaseRecipeRecord recipe)
    {
        Require(recipe.CraftingCosts.Count == 1,
            $"Base Recipe '{recipe.HomecomingSourceId}' must contain exactly one crafting cost.");
        if (!uint.TryParse(
                recipe.CraftingCosts[0],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var value)
            || value == 0)
        {
            throw new HomecomingRecipeCandidateException(
                $"Base Recipe '{recipe.HomecomingSourceId}' crafting cost " +
                $"'{recipe.CraftingCosts[0]}' is not a positive integer.");
        }

        return value;
    }

    private static int ValidateCurrentRecipes(IReadOnlyList<CurrentRecipeIdentity> currentRecipes)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var maximum = 0;
        foreach (var recipe in currentRecipes)
        {
            Require(ids.Add(recipe.AppOwnedId),
                $"Current catalog Recipe ID '{recipe.AppOwnedId}' is duplicated.");
            maximum = Math.Max(maximum, ParseIdentifier(recipe.AppOwnedId, "REC"));
        }

        return maximum;
    }

    private static int ParseIdentifier(string value, string prefix)
    {
        if (value.Length != 9
            || !value.StartsWith(prefix + "-", StringComparison.Ordinal)
            || !int.TryParse(
                value.AsSpan(4),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var parsed)
            || parsed is < 1 or > 99999)
        {
            throw new HomecomingRecipeCandidateException(
                $"App-owned {prefix} identifier '{value}' is invalid.");
        }

        return parsed;
    }

    private static string FormatRecipeId(int value)
    {
        if (value is < 1 or > 99999)
        {
            throw new HomecomingRecipeCandidateException(
                "No REC- identifier remains available for Homecoming Recipe candidates.");
        }

        return $"REC-{value:D5}";
    }

    private static string CreateLogicalEnhancementKey(HomecomingEnhancementCandidateRecord candidate) =>
        string.Join(
            "|",
            candidate.SourceVariants
                .Select(variant => variant.HomecomingSourceId)
                .Order(StringComparer.Ordinal));

    private static string RarityName(uint value, string sourceId) =>
        value switch
        {
            1 => "Common",
            2 => "Uncommon",
            3 => "Rare",
            4 => "Very Rare",
            _ => throw new HomecomingRecipeCandidateException(
                $"Base Recipe '{sourceId}' has unknown rarity value {value}.")
        };

    private static string RequireString(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(property.GetString()))
        {
            throw new HomecomingRecipeCandidateException(
                $"Current catalog Recipe field '{propertyName}' is missing.");
        }

        return property.GetString()!;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new HomecomingRecipeCandidateException(message);
        }
    }

    private sealed record ProductTarget(
        HomecomingEnhancementCandidateRecord Candidate,
        HomecomingEnhancementSourceVariant Variant);

    private sealed record ProductIndex(
        IReadOnlyDictionary<string, ProductTarget> Exact,
        IReadOnlyDictionary<string, ProductTarget> OrdinalIgnoreCase);

    private sealed record ResolvedProduct(
        HomecomingEnhancementCandidateRecord Candidate,
        HomecomingEnhancementSourceVariant Variant,
        bool UsedCaseInsensitiveJoin);

    private sealed record AssignedEnhancementIdResult(
        IReadOnlyDictionary<string, string> ByLogicalKey,
        IReadOnlyList<HomecomingRecipeAssignedEnhancementId> Records);
}

internal static class HomecomingRecipeCandidateWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    internal static string GetOutputPath(string salvageCandidatePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(salvageCandidatePath);
        var fullPath = Path.GetFullPath(salvageCandidatePath);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new HomecomingRecipeCandidateException(
                $"Candidate output path '{fullPath}' has no parent directory.");
        var extension = Path.GetExtension(fullPath);
        if (extension.Length == 0)
        {
            extension = ".json";
        }

        return Path.Combine(
            directory,
            Path.GetFileNameWithoutExtension(fullPath) + ".recipes" + extension);
    }

    internal static byte[] Serialize(HomecomingRecipeCandidateDocument candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        return Encoding.UTF8.GetBytes(JsonSerializer.Serialize(candidate, JsonOptions) + "\n");
    }

    internal static void Write(string outputPath, HomecomingRecipeCandidateDocument candidate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        var fullPath = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new HomecomingRecipeCandidateException(
                $"Candidate output path '{fullPath}' has no parent directory.");
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(fullPath, Serialize(candidate));
    }
}

internal sealed record CurrentRecipeIdentity(string AppOwnedId, string DisplayName);

internal sealed record HomecomingRecipeCandidateDocument(
    string SchemaVersion,
    HomecomingRecipeCandidateSource Source,
    HomecomingRecipeCandidateSummary Summary,
    IReadOnlyList<HomecomingRecipeAssignedEnhancementId> AssignedEnhancementIds,
    IReadOnlyList<HomecomingRecipeCandidateRecord> Records,
    IReadOnlyList<HomecomingCurrentRecipeRecord> CurrentCatalogRecipesNotMatched);

internal sealed record HomecomingRecipeCandidateSource(
    string BuildVersion,
    string PackageRevision,
    string Archive,
    string Member);

internal sealed record HomecomingRecipeCandidateSummary(
    int TotalBaseRecipeRecords,
    int CraftingRecipes,
    int ResolvedNames,
    int UniqueProducts,
    int CompletedProductJoins,
    int CaseInsensitiveProductJoinsUsed,
    int CaseInsensitiveProductCollisions,
    int RecipesUsingAssignedEnhancementIds,
    int NewlyAssignedEnhancementIds,
    int SalvageRequirementRows,
    int UniqueSalvageIds,
    int CompleteSalvageMatrices,
    IReadOnlyList<HomecomingRecipeValueCount> RarityCounts,
    uint? MinimumLevel,
    uint? MaximumLevel,
    IReadOnlyList<HomecomingRecipeLevelCount> LevelCounts,
    int CraftingCostsValidated,
    int RecipeCandidates,
    int InvalidOrUnresolved);

internal sealed record HomecomingRecipeValueCount(string Value, int Count);

internal sealed record HomecomingRecipeLevelCount(uint Level, int Count);

internal sealed record HomecomingRecipeAssignedEnhancementId(
    string AppOwnedId,
    string DisplayName,
    string LogicalIdentityKey,
    IReadOnlyList<string> HomecomingSourceIds);

internal sealed record HomecomingRecipeCandidateRecord(
    string AppOwnedId,
    string HomecomingSourceId,
    string DisplayNameMessageKey,
    string DisplayName,
    string Icon,
    string ProducedEnhancementAppOwnedId,
    string HomecomingProducedBoostSourceId,
    string MatchedHomecomingBoostSourceId,
    bool UsedCaseInsensitiveProductJoin,
    string Rarity,
    uint RarityRawCode,
    uint Level,
    uint CraftingCost,
    IReadOnlyList<string> WorktableIds,
    IReadOnlyList<HomecomingRecipeRequirementCandidate> Requirements,
    string MatchStatus);

internal sealed record HomecomingRecipeRequirementCandidate(
    string SalvageAppOwnedId,
    string HomecomingSalvageSourceId,
    uint Quantity);

internal sealed record HomecomingCurrentRecipeRecord(
    string AppOwnedId,
    string DisplayName);

internal sealed class HomecomingRecipeCandidateException : Exception
{
    internal HomecomingRecipeCandidateException(string message)
        : base(message)
    {
    }
}
