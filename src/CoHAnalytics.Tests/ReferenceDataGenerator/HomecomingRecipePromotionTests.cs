using System.Security.Cryptography;
using System.Text.Json;
using CoHAnalytics.ReferenceData;
using CoHAnalytics.ReferenceDataGenerator;

namespace CoHAnalytics.Tests.ReferenceDataGenerator;

public sealed class HomecomingRecipePromotionSupportTests
{
    [Fact]
    public void Group_Folds_level_variants_and_excludes_51_through_53()
    {
        var produced = Enhancement("ENH-00883", "Adjusted Targeting: To Hit Buff", "SET-00010");
        var rows = Enumerable.Range(21, 33)
            .Select(level => Candidate(
                $"Adjusted_Targeting_A_{level}",
                "Adjusted Targeting: To Hit Buff (Recipe)",
                produced.CatalogItemId!,
                (uint)level,
                cost: 1000u + (uint)level))
            .ToArray();

        var logical = Assert.Single(
            HomecomingRecipePromotionSupport.Group(rows, Catalog(produced)));

        Assert.Equal("ENH-00883", logical.ProducedEnhancementId);
        Assert.Equal("SET-00010", logical.EnhancementSetId);
        Assert.Equal("SetIO", logical.Subtype);
        Assert.Equal("Uncommon", logical.Rarity);
        Assert.Equal("E_ICON_GEN.tga", logical.Icon);
        Assert.Equal(Enumerable.Range(21, 30), logical.SelectableLevels.Select(row => (int)row.Level));
        Assert.Equal([51, 52, 53], logical.ExcludedSourceLevels.Select(row => (int)row.Level));
        Assert.DoesNotContain(
            logical.SelectableLevels,
            row => row.Level > HomecomingRecipePromotionSupport.MaxReferenceLevel);
        Assert.All(logical.ExcludedSourceLevels, row => Assert.True(row.Level > 50));
        Assert.Equal(0, logical.MemorizedSourceCount);
        Assert.DoesNotContain("+1", logical.CurrentDisplayName, StringComparison.Ordinal);
        Assert.DoesNotContain("booster", logical.CurrentDisplayName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Group_Does_not_create_memorized_levels()
    {
        var produced = Enhancement("ENH-00001", "Invention: Accuracy");
        var rows = new[]
        {
            Candidate("Invention_Accuracy_10", "Invention: Accuracy (Recipe)", produced.CatalogItemId!, 10, 3300),
            Candidate(
                "Invention_Accuracy_10_Memorized",
                "Invention: Accuracy (Recipe)",
                produced.CatalogItemId!,
                10,
                1800),
            Candidate("Invention_Accuracy_50", "Invention: Accuracy (Recipe)", produced.CatalogItemId!, 50, 454600),
            Candidate(
                "Invention_Accuracy_50_Memorized",
                "Invention: Accuracy Reduction (Recipe)",
                produced.CatalogItemId!,
                50,
                245200)
        };

        var logical = Assert.Single(
            HomecomingRecipePromotionSupport.Group(rows, Catalog(produced)));

        Assert.Equal([10, 50], logical.SelectableLevels.Select(row => (int)row.Level));
        Assert.Equal(3300u, logical.SelectableLevels[0].CraftingCost);
        Assert.Equal(454600u, logical.SelectableLevels[1].CraftingCost);
        Assert.Equal(2, logical.MemorizedSourceCount);
        Assert.Equal(["Invention: Accuracy Reduction (Recipe)"], logical.AdditionalDisplayNames);
        Assert.Empty(logical.ExcludedSourceLevels);
    }

    [Fact]
    public void Group_Stops_when_51_53_are_not_crafting_cost_variants()
    {
        var produced = Enhancement("ENH-00883", "Adjusted Targeting: To Hit Buff", "SET-00010");
        var rows = new[]
        {
            Candidate(
                "Adjusted_Targeting_A_50",
                "Adjusted Targeting: To Hit Buff (Recipe)",
                produced.CatalogItemId!,
                50,
                requirements: [new("SAL-00001", "S_A", 1)]),
            Candidate(
                "Adjusted_Targeting_A_51",
                "Adjusted Targeting: To Hit Buff (Recipe)",
                produced.CatalogItemId!,
                51,
                requirements: [new("SAL-00002", "S_B", 1)])
        };

        var exception = Assert.Throws<HomecomingRecipePromotionException>(() =>
            HomecomingRecipePromotionSupport.Group(rows, Catalog(produced)));

        Assert.Contains("not a crafting-cost variant", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Remap_Uses_catalog_source_variant_not_candidate_enhancement_id()
    {
        var catalogEnhancement = Enhancement(
            "ENH-00001",
            "Invention: Accuracy",
            sourceId: "Boosts.Crafted_Accuracy.Crafted_Accuracy");
        var candidate = Candidate(
            "Invention_Accuracy_10",
            "Invention: Accuracy (Recipe)",
            "ENH-01927",
            10);
        candidate = candidate with
        {
            HomecomingProducedBoostSourceId = "Boosts.Crafted_Accuracy.Crafted_Accuracy",
            MatchedHomecomingBoostSourceId = "Boosts.Crafted_Accuracy.Crafted_Accuracy"
        };

        var remapped = Assert.Single(
            HomecomingRecipePromotionCommand.RemapProducedEnhancements(
                [candidate],
                Catalog(catalogEnhancement)));

        Assert.Equal("ENH-00001", remapped.ProducedEnhancementAppOwnedId);
    }

    private static Dictionary<string, ItemReferenceRecordDocument> Catalog(
        params ItemReferenceRecordDocument[] items) =>
        items.ToDictionary(item => item.CatalogItemId!, StringComparer.Ordinal);

    private static ItemReferenceRecordDocument Enhancement(
        string catalogItemId,
        string displayName,
        string? setId = null,
        string sourceId = "Boosts.Fixture.Fixture") =>
        new()
        {
            CatalogItemId = catalogItemId,
            Family = nameof(ReferenceItemFamily.Enhancement),
            Subtype = setId is null ? "CraftedInvention" : "SetIO",
            CurrentDisplayName = displayName,
            EnhancementSetId = setId,
            SourceVariants =
            [
                new EnhancementSourceVariantReferenceRecordDocument
                {
                    HomecomingSourceId = sourceId,
                    SourceForm = "Crafted"
                }
            ]
        };

    private static HomecomingRecipeCandidateRecord Candidate(
        string sourceId,
        string displayName,
        string producedId,
        uint level,
        uint cost = 1000,
        IReadOnlyList<HomecomingRecipeRequirementCandidate>? requirements = null) =>
        new(
            "REC-00001",
            sourceId,
            "P_NAME",
            displayName,
            "E_ICON_GEN.tga",
            producedId,
            "Boosts.Fixture.Fixture",
            "Boosts.Fixture.Fixture",
            false,
            "Uncommon",
            2,
            level,
            cost,
            ["Worktable_Invention"],
            requirements ?? [new("SAL-00001", "S_Fixture", 1)],
            "NewFromHomecoming");
}

[Collection(LiveInstallPromotionCollection.Name)]
public sealed class HomecomingRecipePromotionCommandTests
{
    private static string LiveInstallRoot => LiveInstallTestEnvironment.InstallRoot;

    [Fact]
    [Trait("Category", "LiveInstall")]
    public void LiveInstall_Promotion_IsDeterministic_AndCapsLevelsAt50()
    {
        var catalogPath = Path.Combine(
            Path.GetTempPath(),
            $"coh-recipe-promote-{Guid.NewGuid():N}.json");
        try
        {
            using (var embedded = typeof(ItemReferenceCatalogFactory).Assembly.GetManifestResourceStream(
                       ItemReferenceCatalogFactory.ProductionCatalogResourceName)
                   ?? throw new InvalidOperationException("Embedded production catalog missing."))
            using (var output = File.Create(catalogPath))
            {
                embedded.CopyTo(output);
            }

            PromotionManifestOwnershipTestSupport.WriteFutureManifest(catalogPath);
            var beforeStableIds = PromotionManifestOwnershipTestSupport.ReadStableIds(catalogPath, "Recipe");
            var beforeHashes = SnapshotPiggs(LiveInstallRoot);
            var first = HomecomingRecipePromotionCommand.Promote(LiveInstallRoot, catalogPath);
            var midBytes = File.ReadAllBytes(catalogPath);
            var second = HomecomingRecipePromotionCommand.Promote(LiveInstallRoot, catalogPath);
            var afterBytes = File.ReadAllBytes(catalogPath);
            var afterHashes = SnapshotPiggs(LiveInstallRoot);

            Assert.Equal(first.CatalogSha256, second.CatalogSha256);
            Assert.Equal(midBytes, afterBytes);
            Assert.Equal(beforeHashes, afterHashes);
            PromotionManifestOwnershipTestSupport.AssertFutureManifestPreserved(catalogPath);
            Assert.Equal(
                beforeStableIds,
                PromotionManifestOwnershipTestSupport.ReadStableIds(catalogPath, "Recipe"));
            Assert.True(first.Stats.LogicalRecipes > 0);
            Assert.Equal(22122, first.SourceRecipeRows);
            Assert.True(first.Stats.MaximumLevel <= HomecomingRecipePromotionSupport.MaxReferenceLevel);
            Assert.True(first.Stats.ExcludedSourceRows > 0);

            using var document = JsonDocument.Parse(afterBytes);
            Assert.DoesNotContain("\"iconBytes\"", System.Text.Encoding.UTF8.GetString(afterBytes), StringComparison.Ordinal);

            var recipes = document.RootElement.GetProperty("items").EnumerateArray()
                .Where(item => item.GetProperty("family").GetString() == "Recipe")
                .ToArray();
            Assert.Equal(first.Stats.LogicalRecipes, recipes.Length);
            Assert.All(recipes, recipe =>
            {
                Assert.False(string.IsNullOrWhiteSpace(recipe.GetProperty("producedItemId").GetString()));
                Assert.False(string.IsNullOrWhiteSpace(recipe.GetProperty("rarity").GetString()));
                Assert.False(string.IsNullOrWhiteSpace(recipe.GetProperty("icon").GetString()));
                var icon = recipe.GetProperty("icon").GetString()!;
                Assert.DoesNotContain('/', icon);
                Assert.DoesNotContain('\\', icon);
                Assert.StartsWith("E_ICON_", icon, StringComparison.OrdinalIgnoreCase);
                Assert.True(recipe.TryGetProperty("recipeLevels", out var levels));
                Assert.All(
                    levels.EnumerateArray(),
                    level => Assert.InRange(level.GetProperty("level").GetInt32(), 1, 50));
                if (recipe.TryGetProperty("excludedHomecomingSourceLevels", out var excluded))
                {
                    Assert.All(
                        excluded.EnumerateArray(),
                        level => Assert.True(level.GetProperty("level").GetInt32() > 50));
                }
            });

            var adjusted = recipes.Single(recipe =>
                recipe.GetProperty("currentDisplayName").GetString()
                == "Adjusted Targeting: To Hit Buff (Recipe)");
            var adjustedLevels = adjusted.GetProperty("recipeLevels").EnumerateArray()
                .Select(level => level.GetProperty("level").GetInt32())
                .ToArray();
            Assert.DoesNotContain(51, adjustedLevels);
            Assert.DoesNotContain(52, adjustedLevels);
            Assert.DoesNotContain(53, adjustedLevels);
            Assert.Contains(50, adjustedLevels);
            Assert.Equal(
                [51, 52, 53],
                adjusted.GetProperty("excludedHomecomingSourceLevels").EnumerateArray()
                    .Select(level => level.GetProperty("level").GetInt32())
                    .ToArray());
        }
        finally
        {
            if (File.Exists(catalogPath))
            {
                File.Delete(catalogPath);
            }
        }
    }

    private static IReadOnlyList<string> SnapshotPiggs(string installRoot)
    {
        var source = HomecomingStaticDataSourceDiscovery.Discover(installRoot);
        return
        [
            $"{source.BinPiggPath}|{Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source.BinPiggPath)))}",
            $"{source.BinPowersPiggPath}|{Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source.BinPowersPiggPath)))}"
        ];
    }
}
