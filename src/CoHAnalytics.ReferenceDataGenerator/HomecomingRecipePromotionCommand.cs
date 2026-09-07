using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CoHAnalytics.ReferenceData;

namespace CoHAnalytics.ReferenceDataGenerator;

internal static class HomecomingRecipePromotionCommand
{
    internal const string CatalogVersion = "item-ref-2.9.0";
    internal const string SourceRevision = "homecoming-recipe-promotion-2026-08-14";
    private const string SourceNotes =
        "Promote Homecoming invention Recipes into the canonical catalog with ValidLevels capped at 50.";
    private const string MessageMemberName = "bin/clientmessages-en.bin";
    private const string SalvageMemberName = "bin/salvage.bin";
    private const string BaseRecipesMemberName = "bin/baserecipes.bin";

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    internal static int Run(string[] args, TextWriter output, TextWriter error)
    {
        if (!TryParseArgs(args, out var installRoot, out var catalogPath, out var failureReason))
        {
            error.WriteLine(failureReason);
            error.WriteLine(
                "Usage: promote-homecoming-recipes --install <HomecomingRoot> --catalog <item-catalog.v1.json>");
            return 1;
        }

        try
        {
            var beforeHashes = SnapshotHomecomingHashes(installRoot);
            var result = Promote(installRoot, catalogPath);
            var afterHashes = SnapshotHomecomingHashes(installRoot);
            if (!beforeHashes.SequenceEqual(afterHashes, StringComparer.Ordinal))
            {
                throw new HomecomingRecipePromotionException(
                    "Homecoming installation changed during promotion.");
            }

            WriteSummary(output, result);
            return 0;
        }
        catch (Exception exception) when (
            exception is HomecomingRecipePromotionException
                or HomecomingStaticDataSourceException
                or HomecomingPiggException
                or HomecomingRecipeCandidateException
                or HomecomingSalvageCandidateException
                or HomecomingEnhancementCandidateException
                or HomecomingBaseRecipesException
                or HomecomingMessageStoreException
                or HomecomingSalvageException
                or InvalidOperationException
                or IOException
                or JsonException
                or InvalidDataException)
        {
            error.WriteLine($"Recipe promotion: FAIL — {exception.Message}");
            return 1;
        }
    }

    internal static HomecomingRecipePromotionResult Promote(string installRoot, string catalogPath)
    {
        var first = PromoteOnce(installRoot, catalogPath);
        var second = PromoteOnce(installRoot, catalogPath);
        if (!string.Equals(first.CatalogSha256, second.CatalogSha256, StringComparison.Ordinal))
        {
            throw new HomecomingRecipePromotionException(
                "Recipe promotion is not deterministic across repeated runs.");
        }

        return first with { DeterminismSha256 = second.CatalogSha256 };
    }

    private static HomecomingRecipePromotionResult PromoteOnce(string installRoot, string catalogPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogPath);

        var source = HomecomingStaticDataSourceDiscovery.Discover(installRoot);
        var catalogFullPath = Path.GetFullPath(catalogPath);
        if (!File.Exists(catalogFullPath))
        {
            throw new HomecomingRecipePromotionException(
                $"Catalog path '{catalogFullPath}' was not found.");
        }

        var document = JsonSerializer.Deserialize<ItemReferenceCatalogDocument>(
            File.ReadAllBytes(catalogFullPath),
            ReadOptions)
            ?? throw new HomecomingRecipePromotionException("Catalog document is empty.");

        var messages = HomecomingMessageStoreReader.Read(
            HomecomingPiggMemberReader.ReadMember(source.BinPiggPath, MessageMemberName));
        var salvageRecords = HomecomingSalvageReader.Read(
            HomecomingPiggMemberReader.ReadMember(source.BinPiggPath, SalvageMemberName));
        var currentSalvage = document.Items
            .Where(item =>
                string.Equals(item.Family, nameof(ReferenceItemFamily.Salvage), StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(item.CatalogItemId)
                && !string.IsNullOrWhiteSpace(item.CurrentDisplayName))
            .Select(item => new CurrentSalvageIdentity(item.CatalogItemId!, item.CurrentDisplayName!))
            .ToArray();
        var salvageCandidate = HomecomingSalvageCandidateGenerator.Create(
            salvageRecords,
            messages,
            currentSalvage,
            source.BuildVersion,
            source.PackageRevision);

        var powersBytes = HomecomingPiggMemberReader.ReadMember(
            source.BinPowersPiggPath,
            HomecomingEnhancementCandidateGenerator.PowersMember);
        var boostSetBytes = HomecomingPiggMemberReader.ReadMember(
            source.BinPiggPath,
            HomecomingEnhancementCandidateGenerator.BoostSetsMember);
        var concreteBoosts = HomecomingPowersReader.ReadBoosts(powersBytes);
        var boostSets = HomecomingBoostSetsReader.Read(boostSetBytes);
        var discoveryBoosts = HomecomingPowersBoostDiscoveryReader.ReadBoosts(powersBytes)
            .Where(value => value.SourceId.StartsWith("Boosts.", StringComparison.Ordinal))
            .ToDictionary(value => value.SourceId, StringComparer.Ordinal);
        var currentEnhancements = document.Items
            .Where(item =>
                string.Equals(item.Family, nameof(ReferenceItemFamily.Enhancement), StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(item.CatalogItemId)
                && !string.IsNullOrWhiteSpace(item.CurrentDisplayName))
            .Select(item => new CurrentEnhancementIdentity(
                item.CatalogItemId!,
                item.CurrentDisplayName!,
                item.EnhancementSetId,
                item.SourceVariants?
                    .Select(variant => variant.HomecomingSourceId)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => value!)
                    .OrderBy(value => value, StringComparer.Ordinal)
                    .ToArray()
                ?? []))
            .ToArray();
        var currentSets = document.EnhancementSets
            .Where(set =>
                !string.IsNullOrWhiteSpace(set.CatalogItemId)
                && !string.IsNullOrWhiteSpace(set.CurrentDisplayName))
            .Select(set => new CurrentEnhancementSetIdentity(
                set.CatalogItemId!,
                set.CurrentDisplayName!))
            .ToArray();
        var enhancementCandidate = HomecomingEnhancementCandidateGenerator.Create(
            concreteBoosts,
            boostSets,
            messages,
            discoveryBoosts,
            currentEnhancements,
            currentSets,
            source.BuildVersion,
            source.PackageRevision);

        var baseRecipes = HomecomingBaseRecipesReader.Read(
            HomecomingPiggMemberReader.ReadMember(source.BinPiggPath, BaseRecipesMemberName));
        var currentRecipes = document.Items
            .Where(item =>
                string.Equals(item.Family, nameof(ReferenceItemFamily.Recipe), StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(item.CatalogItemId)
                && !string.IsNullOrWhiteSpace(item.CurrentDisplayName))
            .Select(item => new CurrentRecipeIdentity(item.CatalogItemId!, item.CurrentDisplayName!))
            .ToArray();
        var recipeCandidate = HomecomingRecipeCandidateGenerator.Create(
            baseRecipes,
            messages,
            salvageCandidate,
            enhancementCandidate,
            currentRecipes,
            source.BuildVersion,
            source.PackageRevision);

        var itemDocumentsById = document.Items
            .Where(value => !string.IsNullOrWhiteSpace(value.CatalogItemId))
            .ToDictionary(value => value.CatalogItemId!, StringComparer.Ordinal);
        RequireRecipeSalvageMatchesCatalog(
            recipeCandidate.Records,
            salvageCandidate.Records,
            itemDocumentsById);
        var remappedCandidates = RemapProducedEnhancements(
            recipeCandidate.Records,
            itemDocumentsById);
        var producedEnhancements = itemDocumentsById
            .Where(pair =>
                string.Equals(pair.Value.Family, nameof(ReferenceItemFamily.Enhancement), StringComparison.Ordinal))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        var logicalRecipes = HomecomingRecipePromotionSupport.Group(
            remappedCandidates,
            producedEnhancements);

        var stats = ApplyLogicalRecipes(document, itemDocumentsById, logicalRecipes);
        document.Items = itemDocumentsById.Values
            .OrderBy(value => value.CatalogItemId, StringComparer.Ordinal)
            .ToList();
        document.Aliases = document.Aliases
            .OrderBy(value => value.CatalogItemId, StringComparer.Ordinal)
            .ThenBy(value => value.Text, StringComparer.Ordinal)
            .ToList();
        HomecomingPromotionManifestSupport.ApplyPromotionRelease(
            document,
            CatalogVersion,
            SourceRevision,
            SourceNotes,
            source.BuildVersion);

        var serialized = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(document, WriteOptions) + "\n");
        using (var validationStream = new MemoryStream(serialized))
        {
            var load = ItemReferenceCatalogLoader.Load(validationStream);
            if (!load.Succeeded)
            {
                throw new HomecomingRecipePromotionException(
                    $"Promoted catalog failed validation: {load.FailureReason}");
            }
        }

        File.WriteAllBytes(catalogFullPath, serialized);
        return new HomecomingRecipePromotionResult(
            catalogFullPath,
            source.BuildVersion,
            Convert.ToHexString(SHA256.HashData(serialized)),
            Convert.ToHexString(SHA256.HashData(serialized)),
            recipeCandidate.Summary.RecipeCandidates,
            stats);
    }

    internal static void RequireRecipeSalvageMatchesCatalog(
        IReadOnlyList<HomecomingRecipeCandidateRecord> candidates,
        IReadOnlyList<HomecomingSalvageCandidateRecord> salvageCandidates,
        IReadOnlyDictionary<string, ItemReferenceRecordDocument> itemDocumentsById)
    {
        var salvageBySourceId = salvageCandidates.ToDictionary(
            record => record.HomecomingSourceId,
            StringComparer.Ordinal);
        foreach (var candidate in candidates)
        {
            foreach (var requirement in candidate.Requirements)
            {
                if (!salvageBySourceId.TryGetValue(requirement.HomecomingSalvageSourceId, out var salvage)
                    || salvage.MatchStatus != nameof(HomecomingSalvageMatchStatus.MatchedExisting)
                    || string.IsNullOrWhiteSpace(salvage.AppOwnedId)
                    || !string.Equals(salvage.AppOwnedId, requirement.SalvageAppOwnedId, StringComparison.Ordinal)
                    || !itemDocumentsById.TryGetValue(salvage.AppOwnedId, out var salvageItem)
                    || !string.Equals(
                        salvageItem.Family,
                        nameof(ReferenceItemFamily.Salvage),
                        StringComparison.Ordinal))
                {
                    throw new HomecomingRecipePromotionException(
                        $"Recipe '{candidate.HomecomingSourceId}' salvage '{requirement.HomecomingSalvageSourceId}' does not match an existing catalog Salvage identity.");
                }
            }
        }
    }

    internal static IReadOnlyList<HomecomingRecipeCandidateRecord> RemapProducedEnhancements(
        IReadOnlyList<HomecomingRecipeCandidateRecord> candidates,
        IReadOnlyDictionary<string, ItemReferenceRecordDocument> itemDocumentsById)
    {
        var exact = new Dictionary<string, string>(StringComparer.Ordinal);
        var insensitive = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in itemDocumentsById.Values)
        {
            if (!string.Equals(item.Family, nameof(ReferenceItemFamily.Enhancement), StringComparison.Ordinal)
                || item.SourceVariants is null)
            {
                continue;
            }

            foreach (var variant in item.SourceVariants)
            {
                if (string.IsNullOrWhiteSpace(variant.HomecomingSourceId)
                    || string.IsNullOrWhiteSpace(item.CatalogItemId))
                {
                    continue;
                }

                exact[variant.HomecomingSourceId] = item.CatalogItemId;
                insensitive.TryAdd(variant.HomecomingSourceId, item.CatalogItemId);
            }
        }

        return candidates
            .Select(candidate =>
            {
                if (!exact.TryGetValue(candidate.MatchedHomecomingBoostSourceId, out var producedId)
                    && !exact.TryGetValue(candidate.HomecomingProducedBoostSourceId, out producedId)
                    && !insensitive.TryGetValue(candidate.MatchedHomecomingBoostSourceId, out producedId)
                    && !insensitive.TryGetValue(candidate.HomecomingProducedBoostSourceId, out producedId))
                {
                    throw new HomecomingRecipePromotionException(
                        $"Recipe '{candidate.HomecomingSourceId}' product '{candidate.HomecomingProducedBoostSourceId}' does not resolve to a catalog Enhancement.");
                }

                return candidate with { ProducedEnhancementAppOwnedId = producedId };
            })
            .ToArray();
    }

    private static RecipePromotionStats ApplyLogicalRecipes(
        ItemReferenceCatalogDocument document,
        Dictionary<string, ItemReferenceRecordDocument> itemDocumentsById,
        IReadOnlyList<HomecomingLogicalRecipe> logicalRecipes)
    {
        var existingByProduct = itemDocumentsById.Values
            .Where(item =>
                string.Equals(item.Family, nameof(ReferenceItemFamily.Recipe), StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(item.ProducedItemId)
                && !string.IsNullOrWhiteSpace(item.CatalogItemId))
            .ToDictionary(item => item.ProducedItemId!, item => item, StringComparer.Ordinal);
        var usedIds = new HashSet<string>(
            itemDocumentsById.Keys,
            StringComparer.Ordinal);
        var maximumRecipeId = itemDocumentsById.Keys
            .Where(id => id.StartsWith("REC-", StringComparison.Ordinal))
            .Select(ParseRecipeId)
            .DefaultIfEmpty(0)
            .Max();
        var aliases = document.Aliases ?? [];
        var aliasKeys = aliases
            .Select(alias => ItemReferenceLookup.NormalizeLookupKey(alias.Text ?? string.Empty))
            .Where(key => !string.IsNullOrEmpty(key))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var stats = new RecipePromotionStats();

        foreach (var logical in logicalRecipes)
        {
            if (!existingByProduct.TryGetValue(logical.ProducedEnhancementId, out var recipeDocument))
            {
                maximumRecipeId = checked(maximumRecipeId + 1);
                var catalogItemId = FormatRecipeId(maximumRecipeId);
                if (!usedIds.Add(catalogItemId))
                {
                    throw new HomecomingRecipePromotionException(
                        $"Assigned Recipe ID '{catalogItemId}' is already in use.");
                }

                recipeDocument = new ItemReferenceRecordDocument
                {
                    CatalogItemId = catalogItemId,
                    Family = nameof(ReferenceItemFamily.Recipe),
                    ActiveStatus = nameof(ReferenceActiveStatus.Active),
                    VerificationStatus = nameof(ReferenceVerificationStatus.VerifiedMultiSource)
                };
                itemDocumentsById[catalogItemId] = recipeDocument;
                stats.RecipesAdded++;
            }
            else
            {
                stats.RecipesUpdated++;
            }

            recipeDocument.Subtype = logical.Subtype;
            recipeDocument.CurrentDisplayName = logical.CurrentDisplayName;
            recipeDocument.Rarity = logical.Rarity;
            recipeDocument.EnhancementSetId = logical.EnhancementSetId;
            recipeDocument.ProducedItemId = logical.ProducedEnhancementId;
            recipeDocument.Icon = logical.Icon;
            recipeDocument.RecipeLevels = logical.SelectableLevels
                .Select(level => new RecipeLevelReferenceRecordDocument
                {
                    Level = checked((int)level.Level),
                    HomecomingSourceId = level.HomecomingSourceId,
                    CraftingCost = level.CraftingCost,
                    Requirements = level.Requirements
                        .OrderBy(requirement => requirement.SalvageAppOwnedId, StringComparer.Ordinal)
                        .Select(requirement => new RecipeRequirementReferenceRecordDocument
                        {
                            SalvageItemId = requirement.SalvageAppOwnedId,
                            Quantity = requirement.Quantity
                        })
                        .ToList()
                })
                .ToList();
            recipeDocument.ExcludedHomecomingSourceLevels = logical.ExcludedSourceLevels.Count == 0
                ? null
                : logical.ExcludedSourceLevels
                    .Select(level => new RecipeExcludedSourceLevelReferenceRecordDocument
                    {
                        Level = checked((int)level.Level),
                        HomecomingSourceId = level.HomecomingSourceId,
                        CraftingCost = level.CraftingCost
                    })
                    .ToList();

            AddRecipeAlias(
                aliases,
                aliasKeys,
                recipeDocument.CatalogItemId!,
                logical.CurrentDisplayName,
                preferred: true,
                logical.ProducedEnhancementId);
            foreach (var additionalName in logical.AdditionalDisplayNames)
            {
                AddRecipeAlias(
                    aliases,
                    aliasKeys,
                    recipeDocument.CatalogItemId!,
                    additionalName,
                    preferred: false,
                    logical.ProducedEnhancementId);
            }

            stats.LogicalRecipes++;
            stats.SelectableLevelRows += logical.SelectableLevels.Count;
            stats.ExcludedSourceRows += logical.ExcludedSourceLevels.Count;
            stats.MemorizedSourceRows += logical.MemorizedSourceCount;
            if (logical.ExcludedSourceLevels.Count > 0)
            {
                stats.RecipesWithExcludedSourceLevels++;
            }

            stats.RarityCounts[logical.Rarity] = stats.RarityCounts.GetValueOrDefault(logical.Rarity) + 1;
            foreach (var level in logical.SelectableLevels)
            {
                var min = stats.MinimumLevel ?? checked((int)level.Level);
                var max = stats.MaximumLevel ?? checked((int)level.Level);
                stats.MinimumLevel = Math.Min(min, checked((int)level.Level));
                stats.MaximumLevel = Math.Max(max, checked((int)level.Level));
            }
        }

        document.Aliases = aliases;
        return stats;
    }

    private static void AddRecipeAlias(
        List<ItemReferenceAliasRecordDocument> aliases,
        HashSet<string> aliasKeys,
        string catalogItemId,
        string text,
        bool preferred,
        string producedEnhancementId)
    {
        var lookupKey = ItemReferenceLookup.NormalizeLookupKey(text);
        if (string.IsNullOrEmpty(lookupKey))
        {
            return;
        }

        var existing = aliases.FirstOrDefault(alias =>
            string.Equals(
                ItemReferenceLookup.NormalizeLookupKey(alias.Text ?? string.Empty),
                lookupKey,
                StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            if (string.Equals(existing.CatalogItemId, catalogItemId, StringComparison.Ordinal))
            {
                return;
            }

            if (string.Equals(existing.CatalogItemId, producedEnhancementId, StringComparison.Ordinal))
            {
                return;
            }

            throw new HomecomingRecipePromotionException(
                $"Recipe alias '{text}' already belongs to '{existing.CatalogItemId}'.");
        }

        aliasKeys.Add(lookupKey);
        aliases.Add(new ItemReferenceAliasRecordDocument
        {
            CatalogItemId = catalogItemId,
            Locale = "en",
            Text = text,
            NameKind = nameof(ReferenceAliasNameKind.LogReceipt),
            IsPreferred = preferred
        });
    }

    private static int ParseRecipeId(string value)
    {
        if (value.Length != 9
            || !value.StartsWith("REC-", StringComparison.Ordinal)
            || !int.TryParse(value.AsSpan(4), out var parsed)
            || parsed is < 1 or > 99999)
        {
            throw new HomecomingRecipePromotionException($"Recipe identifier '{value}' is invalid.");
        }

        return parsed;
    }

    private static string FormatRecipeId(int value)
    {
        if (value is < 1 or > 99999)
        {
            throw new HomecomingRecipePromotionException("No REC- identifier remains available for Recipe promotion.");
        }

        return $"REC-{value:D5}";
    }

    private static bool TryParseArgs(
        string[] args,
        out string installRoot,
        out string catalogPath,
        out string failureReason)
    {
        installRoot = string.Empty;
        catalogPath = string.Empty;
        failureReason = string.Empty;
        for (var index = 0; index < args.Length; index++)
        {
            var option = args[index];
            if (option is "--install" && index + 1 < args.Length)
            {
                installRoot = args[++index];
                continue;
            }

            if (option is "--catalog" && index + 1 < args.Length)
            {
                catalogPath = args[++index];
                continue;
            }

            failureReason = $"Unknown promote-homecoming-recipes option '{option}'.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(installRoot) || string.IsNullOrWhiteSpace(catalogPath))
        {
            failureReason = "Both --install and --catalog are required.";
            return false;
        }

        return true;
    }

    private static IReadOnlyList<string> SnapshotHomecomingHashes(string installRoot)
    {
        var source = HomecomingStaticDataSourceDiscovery.Discover(installRoot);
        return
        [
            $"{source.BinPiggPath}|{Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source.BinPiggPath)))}",
            $"{source.BinPowersPiggPath}|{Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source.BinPowersPiggPath)))}"
        ];
    }

    private static void WriteSummary(TextWriter output, HomecomingRecipePromotionResult result)
    {
        output.WriteLine($"Recipe promotion: PASS");
        output.WriteLine($"Catalog: {result.CatalogPath}");
        output.WriteLine($"Homecoming build: {result.BuildVersion}");
        output.WriteLine($"Source Recipe rows: {result.SourceRecipeRows}");
        output.WriteLine($"Logical Recipes: {result.Stats.LogicalRecipes}");
        output.WriteLine($"Recipes added: {result.Stats.RecipesAdded}");
        output.WriteLine($"Recipes updated: {result.Stats.RecipesUpdated}");
        output.WriteLine($"Selectable level rows: {result.Stats.SelectableLevelRows}");
        output.WriteLine($"Excluded source rows above 50: {result.Stats.ExcludedSourceRows}");
        output.WriteLine($"Memorized source rows folded: {result.Stats.MemorizedSourceRows}");
        output.WriteLine($"Recipes with excluded 51–53 rows: {result.Stats.RecipesWithExcludedSourceLevels}");
        output.WriteLine($"ValidLevels: {result.Stats.MinimumLevel}–{result.Stats.MaximumLevel}");
        output.WriteLine(
            "Rarity counts: " +
            string.Join(
                ", ",
                result.Stats.RarityCounts
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => $"{pair.Key}={pair.Value}")));
        output.WriteLine($"Catalog SHA-256: {result.CatalogSha256}");
        output.WriteLine($"Determinism SHA-256: {result.DeterminismSha256}");
    }
}

internal sealed record HomecomingRecipePromotionResult(
    string CatalogPath,
    string BuildVersion,
    string CatalogSha256,
    string DeterminismSha256,
    int SourceRecipeRows,
    RecipePromotionStats Stats);

internal sealed class RecipePromotionStats
{
    public int LogicalRecipes { get; set; }

    public int RecipesAdded { get; set; }

    public int RecipesUpdated { get; set; }

    public int SelectableLevelRows { get; set; }

    public int ExcludedSourceRows { get; set; }

    public int MemorizedSourceRows { get; set; }

    public int RecipesWithExcludedSourceLevels { get; set; }

    public int? MinimumLevel { get; set; }

    public int? MaximumLevel { get; set; }

    public Dictionary<string, int> RarityCounts { get; } = new(StringComparer.Ordinal);
}

internal sealed class HomecomingRecipePromotionException : Exception
{
    internal HomecomingRecipePromotionException(string message)
        : base(message)
    {
    }
}
