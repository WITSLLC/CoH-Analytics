using CoHAnalytics.ReferenceData;

namespace CoHAnalytics.Tests.ReferenceData;

public sealed class ItemReferenceCatalogRecipeProductionTests
{
    [Fact]
    public void Production_catalog_promotes_homecoming_recipes()
    {
        var catalog = ItemReferenceCatalogFactory.LoadEmbeddedProduction();
        Assert.True(catalog.IsLoaded, catalog.LoadFailureReason);
        Assert.Equal("item-ref-3.1.0", catalog.Manifest!.CatalogVersion);
        Assert.Equal("homecoming-inspiration-promotion-2026-08-16", catalog.Manifest.SourceRevision);
        Assert.Equal(
            "Promote Homecoming Inspirations into the canonical catalog with client-derived metadata.",
            catalog.Manifest.SourceNotes);

        var recipes = catalog.GetRecipes();
        Assert.Equal(873, recipes.Count);
        Assert.Equal(recipes.Count, recipes.Select(recipe => recipe.CatalogItemId).Distinct().Count());
        Assert.Equal(recipes.Count, recipes.Select(recipe => recipe.ProducedItemId).Distinct().Count());
        Assert.All(recipes, recipe =>
        {
            Assert.Equal(ReferenceItemFamily.Recipe, recipe.Family);
            Assert.False(string.IsNullOrWhiteSpace(recipe.ProducedItemId));
            Assert.True(catalog.TryGetById(recipe.ProducedItemId!, out var produced));
            Assert.Equal(ReferenceItemFamily.Enhancement, produced.Family);
            Assert.Equal(produced.EnhancementSetId, recipe.EnhancementSetId);
            Assert.False(string.IsNullOrWhiteSpace(recipe.Rarity));
            Assert.False(string.IsNullOrWhiteSpace(recipe.Icon));
            Assert.StartsWith("E_ICON_", recipe.Icon, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain('/', recipe.Icon);
            Assert.DoesNotContain('\\', recipe.Icon);
            Assert.NotEmpty(recipe.RecipeLevels);
            Assert.Equal(recipe.RecipeLevels.Select(level => level.Level), recipe.ValidLevels);
            Assert.All(recipe.ValidLevels, level => Assert.InRange(level, 1, 50));
            Assert.All(recipe.ExcludedHomecomingSourceLevels, excluded => Assert.InRange(excluded.Level, 51, 53));
            Assert.DoesNotContain("+1", recipe.CurrentDisplayName, StringComparison.Ordinal);
            Assert.DoesNotContain("booster", recipe.CurrentDisplayName, StringComparison.OrdinalIgnoreCase);
            Assert.All(recipe.RecipeLevels, level =>
            {
                Assert.True(level.CraftingCost > 0);
                Assert.NotEmpty(level.Requirements);
                Assert.All(level.Requirements, requirement =>
                {
                    Assert.True(catalog.TryGetById(requirement.SalvageItemId, out var salvage));
                    Assert.Equal(ReferenceItemFamily.Salvage, salvage.Family);
                    Assert.True(requirement.Quantity > 0);
                });
            });
        });
    }

    [Theory]
    [InlineData("Invention: Accuracy (Recipe)", "Invention: Accuracy", null, "Common")]
    [InlineData("Adjusted Targeting: To Hit Buff (Recipe)", "Adjusted Targeting: To Hit Buff", "Adjusted Targeting", "Uncommon")]
    [InlineData(
        "Absolute Amazement: Stun Duration (Superior) (Recipe)",
        "Absolute Amazement: Stun Duration",
        "Absolute Amazement",
        "Very Rare")]
    [InlineData("Positron's Blast: Acc/Dam (Recipe)", "Positron's Blast: Accuracy/Damage", "Positron's Blast", "Rare")]
    [InlineData(
        "Soulbound Allegiance: Damage (Superior) (Recipe)",
        "Soulbound Allegiance: Damage",
        "Soulbound Allegiance",
        "Very Rare")]
    [InlineData(
        "Armageddon: Damage (Superior) (Recipe)",
        "Armageddon: Damage",
        "Armageddon",
        "Very Rare")]
    public void Representative_recipes_resolve_with_expected_identity(
        string recipeName,
        string producedName,
        string? setName,
        string rarity)
    {
        var catalog = ItemReferenceCatalogFactory.LoadEmbeddedProduction();
        Assert.True(catalog.TryResolve(recipeName, out var recipeResolution));
        Assert.Equal(ReferenceItemFamily.Recipe, recipeResolution.Item.Family);
        Assert.Equal(rarity, recipeResolution.Item.Rarity);
        Assert.False(string.IsNullOrWhiteSpace(recipeResolution.Item.Icon));

        Assert.True(catalog.TryGetById(recipeResolution.Item.ProducedItemId!, out var produced));
        Assert.Equal(producedName, produced.CurrentDisplayName);
        Assert.Equal(produced.EnhancementSetId, recipeResolution.Item.EnhancementSetId);
        if (setName is null)
        {
            Assert.Null(recipeResolution.Item.EnhancementSetId);
            Assert.Equal("CraftedInvention", recipeResolution.Item.Subtype);
        }
        else
        {
            Assert.True(catalog.TryGetEnhancementSetById(recipeResolution.Item.EnhancementSetId!, out var set));
            Assert.Equal(setName, set.CurrentDisplayName);
            Assert.Equal("SetIO", recipeResolution.Item.Subtype);
        }
    }

    [Fact]
    public void Adjusted_Targeting_caps_valid_levels_at_50_and_keeps_51_53_as_excluded_source()
    {
        var catalog = ItemReferenceCatalogFactory.LoadEmbeddedProduction();
        Assert.True(catalog.TryResolve("Adjusted Targeting: To Hit Buff (Recipe)", out var resolution));
        var recipe = resolution.Item;

        Assert.Equal(50, recipe.ValidLevels.Max());
        Assert.DoesNotContain(51, recipe.ValidLevels);
        Assert.DoesNotContain(52, recipe.ValidLevels);
        Assert.DoesNotContain(53, recipe.ValidLevels);
        Assert.Equal([51, 52, 53], recipe.ExcludedHomecomingSourceLevels.Select(level => level.Level));
        Assert.Contains("Adjusted_Targeting_A_50", recipe.RecipeLevels.Select(level => level.HomecomingSourceId));
        Assert.Equal(
            ["Adjusted_Targeting_A_51", "Adjusted_Targeting_A_52", "Adjusted_Targeting_A_53"],
            recipe.ExcludedHomecomingSourceLevels.Select(level => level.HomecomingSourceId));

        var costs = recipe.RecipeLevels.Select(level => level.CraftingCost).Distinct().Count();
        Assert.True(costs > 1);
        var level50 = recipe.RecipeLevels.Single(level => level.Level == 50);
        Assert.True(level50.CraftingCost > 0);
        Assert.NotEmpty(level50.Requirements);
        Assert.All(
            recipe.ExcludedHomecomingSourceLevels,
            excluded => Assert.NotEqual(level50.CraftingCost, excluded.CraftingCost));
    }

    [Fact]
    public void Common_IO_recipe_preserves_per_level_crafting_through_50()
    {
        var catalog = ItemReferenceCatalogFactory.LoadEmbeddedProduction();
        Assert.True(catalog.TryResolve("Invention: Accuracy (Recipe)", out var resolution));
        var recipe = resolution.Item;

        Assert.Equal(10, recipe.ValidLevels.Min());
        Assert.Equal(50, recipe.ValidLevels.Max());
        Assert.Empty(recipe.ExcludedHomecomingSourceLevels);
        Assert.Equal("CraftedInvention", recipe.Subtype);
        Assert.Equal("Common", recipe.Rarity);

        var byLevel = recipe.RecipeLevels.ToDictionary(level => level.Level);
        Assert.NotEqual(byLevel[10].CraftingCost, byLevel[50].CraftingCost);
        Assert.NotEmpty(byLevel[10].Requirements);
        Assert.NotEmpty(byLevel[50].Requirements);
        Assert.All(
            recipe.RecipeLevels,
            level => Assert.False(
                level.HomecomingSourceId.EndsWith("_Memorized", StringComparison.Ordinal)));
    }

    [Fact]
    public void Recipe_search_and_enhancement_alias_ownership_are_preserved()
    {
        var catalog = ItemReferenceCatalogFactory.LoadEmbeddedProduction();
        var results = catalog.Search("Adjusted Targeting", ReferenceItemFamily.Recipe);
        Assert.Contains(
            results,
            result => result.CurrentDisplayName == "Adjusted Targeting: To Hit Buff (Recipe)");

        Assert.True(catalog.TryResolve("Expedient Reinforcement: Resist Bonus Aura for Pets", out var enhancement));
        Assert.Equal(ReferenceItemFamily.Enhancement, enhancement.Item.Family);
        Assert.False(catalog.TryResolve("Armageddon: Damage (Recipe)", out _));
    }

    [Fact]
    public void Production_recipe_rarity_counts_match_logical_grouping()
    {
        var catalog = ItemReferenceCatalogFactory.LoadEmbeddedProduction();
        var rarityCounts = catalog.GetRecipes()
            .GroupBy(recipe => recipe.Rarity, StringComparer.Ordinal)
            .ToDictionary(group => group.Key!, group => group.Count(), StringComparer.Ordinal);

        Assert.Equal(
            new HashSet<string>(StringComparer.Ordinal) { "Common", "Uncommon", "Rare", "Very Rare" },
            rarityCounts.Keys.ToHashSet(StringComparer.Ordinal));
        Assert.Equal(873, rarityCounts.Values.Sum());
        Assert.Equal(26, rarityCounts["Common"]);
        Assert.Equal(295, rarityCounts["Uncommon"]);
        Assert.Equal(492, rarityCounts["Rare"]);
        Assert.Equal(60, rarityCounts["Very Rare"]);
    }
}
