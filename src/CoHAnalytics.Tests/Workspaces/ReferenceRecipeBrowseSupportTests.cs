using System.Globalization;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CoHAnalytics.Homecoming;
using CoHAnalytics.Orchestration.Contracts;
using CoHAnalytics.Orchestration.Models;
using CoHAnalytics.ReferenceData;
using CoHAnalytics.Services;
using CoHAnalytics.Tests.Services;
using CoHAnalytics.ViewModels.Workspaces;

namespace CoHAnalytics.Tests.Workspaces;

public sealed class ReferenceRecipeBrowseSupportTests
{
    private readonly IItemReferenceCatalog _catalog = ItemReferenceCatalogFactory.LoadEmbeddedProduction();
    private readonly IEnhancementHelpResolver _resolver =
        ItemReferenceCatalogFactory.CreateEmbeddedProductionResolver();

    [Fact]
    public void Recipes_and_badges_chips_are_enabled()
    {
        using var viewModel = CreateViewModel();
        var recipes = viewModel.SectionChips.Single(chip => chip.SectionId == ReferenceSectionId.Recipes);
        var badges = viewModel.SectionChips.Single(chip => chip.SectionId == ReferenceSectionId.Badges);

        Assert.True(recipes.IsEnabled);
        Assert.True(badges.IsEnabled);
        Assert.Equal(ReferenceSectionId.Enhancements, viewModel.ActiveSection);
    }

    [Fact]
    public void Recipe_tree_builds_from_promoted_catalog_recipes()
    {
        Assert.True(_catalog.IsLoaded, _catalog.LoadFailureReason);
        var recipes = _catalog.GetRecipes();
        Assert.Equal(873, recipes.Count);

        var tree = ReferenceRecipeBrowseSupport.BuildBrowseTree(_catalog);
        var recipeNodes = tree.NodesByKey.Values
            .Where(node => node.Kind == ReferenceEnhancementBrowseNodeKind.Recipe)
            .ToArray();

        Assert.Equal(recipes.Count, recipeNodes.Length);
        Assert.All(recipes, recipe =>
            Assert.NotNull(ReferenceRecipeBrowseSupport.FindNodeForRecipeId(tree, recipe.CatalogItemId)));
        Assert.Contains(tree.RootNodes, node => node.NodeKey == ReferenceRecipeBrowseSupport.SetsRootNodeKey);
        Assert.Contains(tree.RootNodes, node => node.NodeKey == ReferenceRecipeBrowseSupport.CommonRootNodeKey);
    }

    [Fact]
    public void Common_io_recipe_appears_under_common_invention_branch()
    {
        var tree = ReferenceRecipeBrowseSupport.BuildBrowseTree(_catalog);
        var recipeNode = RequireRecipeNode(tree, "Invention: Accuracy (Recipe)");
        var group = RequireAncestor(tree, recipeNode, ReferenceEnhancementBrowseNodeKind.CommonGroup);
        var branch = RequireAncestor(tree, recipeNode, ReferenceEnhancementBrowseNodeKind.RootCommonBranch);

        Assert.Equal("Common Invention Recipes", branch.DisplayName);
        Assert.False(string.IsNullOrWhiteSpace(group.DisplayName));
        Assert.Null(recipeNode.SetId);
    }

    [Fact]
    public void Set_recipe_appears_under_canonical_set_and_category()
    {
        Assert.True(_catalog.TryResolve("Adjusted Targeting: To Hit Buff (Recipe)", out var resolution));
        Assert.True(_catalog.TryGetEnhancementSetById(resolution.Item.EnhancementSetId!, out var set));

        var tree = ReferenceRecipeBrowseSupport.BuildBrowseTree(_catalog);
        var recipeNode = RequireRecipeNode(tree, "Adjusted Targeting: To Hit Buff (Recipe)");
        var setNode = RequireAncestor(tree, recipeNode, ReferenceEnhancementBrowseNodeKind.Set);
        var category = RequireAncestor(tree, recipeNode, ReferenceEnhancementBrowseNodeKind.Category);
        var branch = RequireAncestor(tree, recipeNode, ReferenceEnhancementBrowseNodeKind.RootSetsBranch);

        Assert.Equal("Adjusted Targeting", setNode.DisplayName);
        Assert.Equal(set.CatalogItemId, setNode.SetId);
        Assert.Equal(set.CategoryDisplayText ?? set.CategoryCode, category.DisplayName);
        Assert.Equal("Recipe Sets", branch.DisplayName);
    }

    [Theory]
    [InlineData("Invention: Accuracy (Recipe)")]
    [InlineData("Adjusted Targeting: To Hit Buff (Recipe)")]
    [InlineData("Absolute Amazement: Stun Duration (Superior) (Recipe)")]
    [InlineData("Positron's Blast: Acc/Dam (Recipe)")]
    [InlineData("Soulbound Allegiance: Damage (Superior) (Recipe)")]
    [InlineData("Armageddon: Damage (Superior) (Recipe)")]
    public void Representative_recipe_levels_render_through_50_only(string recipeName)
    {
        Assert.True(_catalog.TryResolve(recipeName, out var resolution));
        var recipe = resolution.Item;
        var tree = ReferenceRecipeBrowseSupport.BuildBrowseTree(_catalog);
        var recipeNode = RequireRecipeNode(tree, recipeName);
        var levels = GetVisibleLevels(recipeNode);

        Assert.Equal(recipe.ValidLevels.Where(level => level <= 50).OrderBy(level => level), levels);
        Assert.Equal(50, levels.Max());
        Assert.DoesNotContain(51, levels);
        Assert.DoesNotContain(52, levels);
        Assert.DoesNotContain(53, levels);
        Assert.Null(ReferenceRecipeBrowseSupport.FindNodeForRecipeLevel(tree, recipe.CatalogItemId, 51));
        Assert.Null(ReferenceRecipeBrowseSupport.FindNodeForRecipeLevel(tree, recipe.CatalogItemId, 55));
    }

    [Fact]
    public void Recipe_tree_has_no_level_nodes_above_50()
    {
        var tree = ReferenceRecipeBrowseSupport.BuildBrowseTree(_catalog);
        var overFifty = tree.NodesByKey.Values
            .Where(node => node.Kind == ReferenceEnhancementBrowseNodeKind.Level)
            .Select(node => node.PresentationLevel)
            .Where(level => level is > 50)
            .ToArray();

        Assert.Empty(overFifty);
        Assert.DoesNotContain(tree.NodesByKey.Keys, key =>
            key.Contains(":lvl:51", StringComparison.Ordinal)
            || key.Contains(":lvl:52", StringComparison.Ordinal)
            || key.Contains(":lvl:53", StringComparison.Ordinal)
            || key.Contains("+1", StringComparison.Ordinal)
            || key.Contains("+2", StringComparison.Ordinal));
    }

    [Fact]
    public void Recipe_detail_resolves_selected_level_and_defaults_to_max_valid_level()
    {
        Assert.True(_catalog.TryResolve("Invention: Accuracy (Recipe)", out var resolution));
        var tree = ReferenceRecipeBrowseSupport.BuildBrowseTree(_catalog);
        var recipeNode = RequireRecipeNode(tree, "Invention: Accuracy (Recipe)");
        var level10 = ReferenceRecipeBrowseSupport.FindNodeForRecipeLevel(tree, resolution.Item.CatalogItemId, 10);
        Assert.NotNull(level10);

        var defaultDetail = BuildDetail(tree, recipeNode);
        var level10Detail = BuildDetail(tree, level10, presentationLevel: 10);

        Assert.Equal(ReferenceEnhancementDetailKind.Recipe, defaultDetail.Kind);
        Assert.Equal(50, defaultDetail.PresentationLevel);
        Assert.Equal("Crafting values for Level 50", defaultDetail.PresentationLevelLabel);
        Assert.Equal(10, level10Detail.PresentationLevel);
        Assert.Equal("Crafting values for Level 10", level10Detail.PresentationLevelLabel);
        Assert.NotEqual(defaultDetail.CraftingCostLabel, level10Detail.CraftingCostLabel);
    }

    [Fact]
    public void Accuracy_recipe_description_matches_produced_enhancement_at_the_same_level()
    {
        Assert.True(_catalog.TryResolve("Invention: Accuracy (Recipe)", out var recipeResolution));
        Assert.True(_catalog.TryGetById(recipeResolution.Item.ProducedItemId!, out var produced));

        var recipeTree = ReferenceRecipeBrowseSupport.BuildBrowseTree(_catalog);
        var enhancementTree = ReferenceEnhancementBrowseSupport.BuildBrowseTree(_catalog);
        var recipeNode = RequireRecipeNode(recipeTree, "Invention: Accuracy (Recipe)");
        var level10 = ReferenceRecipeBrowseSupport.FindNodeForRecipeLevel(
            recipeTree,
            recipeResolution.Item.CatalogItemId,
            10);
        Assert.NotNull(level10);

        var recipeDefault = BuildDetail(recipeTree, recipeNode);
        var recipeLevel10 = BuildDetail(recipeTree, level10, presentationLevel: 10);
        var enhancement50 = BuildEnhancementDetail(enhancementTree, produced.CatalogItemId, 50);
        var enhancement10 = BuildEnhancementDetail(enhancementTree, produced.CatalogItemId, 10);

        Assert.Equal("Increases accuracy by 42.4%.", recipeDefault.ResolvedDisplayHelp);
        Assert.Equal(enhancement50.ResolvedDisplayHelp, recipeDefault.ResolvedDisplayHelp);
        Assert.Equal(enhancement10.ResolvedDisplayHelp, recipeLevel10.ResolvedDisplayHelp);
        Assert.NotEqual(recipeDefault.ResolvedDisplayHelp, recipeLevel10.ResolvedDisplayHelp);
        Assert.Equal(EnhancementHelpResolutionStatus.FullyResolved, recipeDefault.HelpResolutionStatus);
        Assert.Contains("{Boost.Attrib.Accuracy.Scale}", recipeDefault.CanonicalDisplayHelp!, StringComparison.Ordinal);
        Assert.DoesNotContain("{Boost.Attrib", recipeDefault.ResolvedDisplayHelp!, StringComparison.Ordinal);
        Assert.False(recipeDefault.ShowVariantHelpSections);
    }

    [Theory]
    [InlineData("Invention: Accuracy (Recipe)")]
    [InlineData("Adjusted Targeting: To Hit Buff (Recipe)")]
    [InlineData("Positron's Blast: Acc/Dam (Recipe)")]
    [InlineData("Absolute Amazement: Stun Duration (Superior) (Recipe)")]
    public void Recipe_description_matches_produced_enhancement_help(string recipeName)
    {
        Assert.True(_catalog.TryResolve(recipeName, out var recipeResolution));
        Assert.True(_catalog.TryGetById(recipeResolution.Item.ProducedItemId!, out var produced));

        var recipeTree = ReferenceRecipeBrowseSupport.BuildBrowseTree(_catalog);
        var enhancementTree = ReferenceEnhancementBrowseSupport.BuildBrowseTree(_catalog);
        var recipeDetail = BuildDetail(recipeTree, RequireRecipeNode(recipeTree, recipeName));
        var enhancementDetail = BuildEnhancementDetail(
            enhancementTree,
            produced.CatalogItemId,
            recipeDetail.PresentationLevel);

        Assert.False(string.IsNullOrWhiteSpace(recipeDetail.ResolvedDisplayHelp));
        Assert.DoesNotContain("{Boost.Attrib", recipeDetail.ResolvedDisplayHelp!, StringComparison.Ordinal);
        if (!string.IsNullOrWhiteSpace(enhancementDetail.ResolvedDisplayHelp))
        {
            Assert.Equal(enhancementDetail.ResolvedDisplayHelp, recipeDetail.ResolvedDisplayHelp);
        }

        Assert.False(recipeDetail.ShowVariantHelpSections);
        Assert.Empty(recipeDetail.SetBonusTiers);
    }

    [Fact]
    public void Recipe_detail_crafting_cost_matches_promoted_per_level_data()
    {
        Assert.True(_catalog.TryResolve("Invention: Accuracy (Recipe)", out var resolution));
        var recipe = resolution.Item;
        var tree = ReferenceRecipeBrowseSupport.BuildBrowseTree(_catalog);
        var level50 = recipe.RecipeLevels.Single(level => level.Level == 50);
        var detail = BuildDetail(
            tree,
            ReferenceRecipeBrowseSupport.FindNodeForRecipeLevel(tree, recipe.CatalogItemId, 50),
            presentationLevel: 50);

        Assert.Equal(FormatCost(level50.CraftingCost), detail.CraftingCostLabel);
    }

    [Fact]
    public void Recipe_detail_salvage_matches_promoted_per_level_data()
    {
        Assert.True(_catalog.TryResolve("Adjusted Targeting: To Hit Buff (Recipe)", out var resolution));
        var recipe = resolution.Item;
        var tree = ReferenceRecipeBrowseSupport.BuildBrowseTree(_catalog);
        var level50 = recipe.RecipeLevels.Single(level => level.Level == 50);
        var detail = BuildDetail(
            tree,
            ReferenceRecipeBrowseSupport.FindNodeForRecipeLevel(tree, recipe.CatalogItemId, 50),
            presentationLevel: 50);

        Assert.Equal(level50.Requirements.Count, detail.SalvageRequirements.Count);
        foreach (var requirement in level50.Requirements)
        {
            Assert.True(_catalog.TryGetById(requirement.SalvageItemId, out var salvage));
            Assert.Contains(
                detail.SalvageRequirements,
                item => item.SalvageItemId == requirement.SalvageItemId
                    && item.Quantity == requirement.Quantity
                    && item.DisplayName == salvage.CurrentDisplayName);
        }
    }

    [Theory]
    [InlineData("Invention: Accuracy (Recipe)")]
    [InlineData("Adjusted Targeting: To Hit Buff (Recipe)")]
    [InlineData("Absolute Amazement: Stun Duration (Superior) (Recipe)")]
    [InlineData("Positron's Blast: Acc/Dam (Recipe)")]
    [InlineData("Soulbound Allegiance: Damage (Superior) (Recipe)")]
    [InlineData("Armageddon: Damage (Superior) (Recipe)")]
    public void Representative_recipe_detail_uses_catalog_identity(string recipeName)
    {
        Assert.True(_catalog.TryResolve(recipeName, out var resolution));
        var recipe = resolution.Item;
        Assert.True(_catalog.TryGetById(recipe.ProducedItemId!, out var produced));
        var tree = ReferenceRecipeBrowseSupport.BuildBrowseTree(_catalog);
        var detail = BuildDetail(tree, RequireRecipeNode(tree, recipeName));

        Assert.Equal(recipe.CurrentDisplayName, detail.Title);
        Assert.Equal(recipe.Rarity, detail.RarityLabel);
        Assert.Equal(produced.CurrentDisplayName, detail.ProducedEnhancementName);
        Assert.Equal(produced.CatalogItemId, detail.ProducedEnhancementId);
        Assert.Equal(recipe.EnhancementSetId, detail.ParentSetId);
        Assert.Equal(produced.Icon ?? produced.SourceVariants.FirstOrDefault()?.Icon, detail.IconIdentity);
        Assert.Equal(recipe.ValidLevels.Max(), detail.PresentationLevel);
        Assert.False(string.IsNullOrWhiteSpace(detail.CraftingCostLabel));
        Assert.NotEmpty(detail.SalvageRequirements);
        Assert.All(detail.SalvageRequirements, item => Assert.True(item.Quantity > 0));
    }

    [Fact]
    public void Recipe_icon_resolution_starts_from_produced_item_id()
    {
        Assert.True(_catalog.TryResolve("Armageddon: Damage (Superior) (Recipe)", out var resolution));
        var recipe = resolution.Item;
        Assert.False(string.IsNullOrWhiteSpace(recipe.ProducedItemId));
        var tree = ReferenceRecipeBrowseSupport.BuildBrowseTree(_catalog);
        var recipeNode = RequireRecipeNode(tree, "Armageddon: Damage (Superior) (Recipe)");
        Assert.Equal(recipe.ProducedItemId, recipeNode.EnhancementId);

        Assert.True(_catalog.TryGetById(recipe.ProducedItemId!, out var produced));
        var boostMetadata = CreateBoostMetadata(produced);
        EnhancementSetReferenceRecord? parentSet = null;
        if (!string.IsNullOrWhiteSpace(produced.EnhancementSetId)
            && _catalog.TryGetEnhancementSetById(produced.EnhancementSetId, out var set))
        {
            parentSet = set;
        }

        var recipeRequest = ReferenceRecipeBrowseSupport.TryBuildProducedEnhancementCompositionRequest(
            _catalog,
            recipe.ProducedItemId,
            boostMetadata);
        var enhancementRequest = ReferenceEnhancementBrowseSupport.TryBuildCompositionRequest(
            _catalog,
            produced,
            parentSet,
            boostMetadata);

        Assert.NotNull(recipeRequest);
        Assert.Equal(enhancementRequest, recipeRequest);
    }

    [Theory]
    [InlineData("Invention: Accuracy (Recipe)", "Invention: Accuracy")]
    [InlineData("Positron's Blast: Acc/Dam (Recipe)", "Positron's Blast: Accuracy/Damage")]
    [InlineData("Absolute Amazement: Stun Duration (Superior) (Recipe)", "Absolute Amazement: Stun Duration")]
    [InlineData("Soulbound Allegiance: Damage (Superior) (Recipe)", "Soulbound Allegiance: Damage")]
    [InlineData("Armageddon: Damage (Superior) (Recipe)", "Armageddon: Damage")]
    public void Recipe_icon_matches_produced_enhancement_composition(
        string recipeName,
        string enhancementName)
    {
        Assert.True(_catalog.TryResolve(recipeName, out var recipeResolution));
        Assert.True(_catalog.TryResolve(enhancementName, out var enhancementResolution));
        Assert.Equal(enhancementResolution.Item.CatalogItemId, recipeResolution.Item.ProducedItemId);

        var boostMetadata = CreateBoostMetadata(enhancementResolution.Item);
        var recipeRequest = ReferenceRecipeBrowseSupport.TryBuildProducedEnhancementCompositionRequest(
            _catalog,
            recipeResolution.Item.ProducedItemId,
            boostMetadata);
        EnhancementSetReferenceRecord? parentSet = null;
        if (!string.IsNullOrWhiteSpace(enhancementResolution.Item.EnhancementSetId)
            && _catalog.TryGetEnhancementSetById(enhancementResolution.Item.EnhancementSetId, out var set))
        {
            parentSet = set;
        }

        var enhancementRequest = ReferenceEnhancementBrowseSupport.TryBuildCompositionRequest(
            _catalog,
            enhancementResolution.Item,
            parentSet,
            boostMetadata);
        Assert.Equal(enhancementRequest, recipeRequest);
        Assert.NotNull(recipeRequest);

        var compositor = new RecordingEnhancementIconCompositor();
        var tree = ReferenceRecipeBrowseSupport.BuildBrowseTree(_catalog);
        var recipeNode = RequireRecipeNode(tree, recipeName);
        var recipeDetail = BuildDetail(tree, recipeNode, compositor: compositor, boostMetadata: boostMetadata);
        var enhancementIcon = ReferenceEnhancementBrowseSupport.ResolveComposedIconSource(
            compositor,
            _catalog,
            enhancementResolution.Item,
            parentSet,
            boostMetadata);
        var treeVm = new ReferenceEnhancementTreeNodeViewModel(
            recipeNode,
            _catalog,
            compositor,
            boostMetadata);

        Assert.NotNull(recipeDetail.IconSource);
        Assert.Same(enhancementIcon, recipeDetail.IconSource);
        Assert.Same(enhancementIcon, treeVm.IconSource);
    }

    [Fact]
    public void Raw_recipe_e_icon_is_not_used_as_visible_fallback()
    {
        Assert.True(_catalog.TryResolve("Positron's Blast: Acc/Dam (Recipe)", out var resolution));
        Assert.True(_catalog.TryGetById(resolution.Item.ProducedItemId!, out var produced));
        var assets = new FakeInstalledGameAssetProvider();
        var compositor = new RecordingEnhancementIconCompositor { ReturnNull = true };
        var boostMetadata = CreateBoostMetadata(produced);
        var tree = ReferenceRecipeBrowseSupport.BuildBrowseTree(_catalog);
        var recipeNode = RequireRecipeNode(tree, "Positron's Blast: Acc/Dam (Recipe)");
        var detail = BuildDetail(
            tree,
            recipeNode,
            compositor: compositor,
            boostMetadata: boostMetadata,
            assets: assets);
        var treeVm = new ReferenceEnhancementTreeNodeViewModel(
            recipeNode,
            _catalog,
            compositor,
            boostMetadata,
            assets);

        Assert.Null(detail.IconSource);
        Assert.Null(treeVm.IconSource);
        Assert.DoesNotContain(resolution.Item.Icon, assets.Requests);
        Assert.NotEmpty(compositor.Requests);
        Assert.Equal("P", detail.IconPlaceholderLetter);
        Assert.Equal("P", treeVm.IconPlaceholderLetter);
    }

    [Fact]
    public void Failed_recipe_composition_uses_letter_placeholder()
    {
        var compositor = new RecordingEnhancementIconCompositor { ReturnNull = true };
        var tree = ReferenceRecipeBrowseSupport.BuildBrowseTree(_catalog);
        var recipeNode = RequireRecipeNode(tree, "Invention: Accuracy (Recipe)");
        var detail = BuildDetail(tree, recipeNode, compositor: compositor);
        var treeVm = new ReferenceEnhancementTreeNodeViewModel(recipeNode, _catalog, compositor);

        Assert.Null(detail.IconSource);
        Assert.Null(treeVm.IconSource);
        Assert.Equal("I", detail.IconPlaceholderLetter);
        Assert.Equal("I", treeVm.IconPlaceholderLetter);
        Assert.NotEmpty(compositor.Requests);
    }

    [Fact]
    public void Recipe_salvage_is_ordered_common_uncommon_rare_with_rarity_codes()
    {
        Assert.True(_catalog.TryResolve("Invention: Accuracy (Recipe)", out var resolution));
        var recipe = resolution.Item;
        var level50 = recipe.RecipeLevels.Single(level => level.Level == 50);
        var tree = ReferenceRecipeBrowseSupport.BuildBrowseTree(_catalog);
        var detail = BuildDetail(
            tree,
            ReferenceRecipeBrowseSupport.FindNodeForRecipeLevel(tree, recipe.CatalogItemId, 50),
            presentationLevel: 50);

        Assert.Equal(level50.Requirements.Count, detail.SalvageRequirements.Count);
        Assert.Equal(
            level50.Requirements.Select(requirement => (requirement.SalvageItemId, requirement.Quantity)).ToHashSet(),
            detail.SalvageRequirements.Select(item => (item.SalvageItemId, item.Quantity)).ToHashSet());

        var rarities = detail.SalvageRequirements
            .Select(item =>
            {
                Assert.True(_catalog.TryGetById(item.SalvageItemId, out var salvage));
                return salvage.Rarity;
            })
            .ToArray();
        Assert.Equal(rarities.OrderBy(SalvageRaritySortKey).ToArray(), rarities);

        foreach (var item in detail.SalvageRequirements)
        {
            Assert.True(_catalog.TryGetById(item.SalvageItemId, out var salvage));
            Assert.Equal(ExpectedSalvageRarityCode(salvage.Rarity), item.RarityCode);
            Assert.Equal(
                ReferenceRarityPresentation.ResolveFamily(ExpectedSalvageRarityCode(salvage.Rarity)),
                ReferenceRarityPresentation.ResolveFamily(item.RarityCode));
            Assert.Equal($"{item.Quantity} ×", item.QuantityLabel);
        }
    }

    [Fact]
    public void Enhancement_reference_tree_and_level_55_behavior_remain_unchanged()
    {
        var enhancementTree = ReferenceEnhancementBrowseSupport.BuildBrowseTree(_catalog);
        var recipeTree = ReferenceRecipeBrowseSupport.BuildBrowseTree(_catalog);

        Assert.Equal(1628, enhancementTree.Census.CurrentEnhancementCount);
        Assert.True(_catalog.TryResolve("Soulbound Allegiance: Damage", out var enhancement));
        Assert.NotNull(ReferenceEnhancementBrowseSupport.FindNodeForEnhancementLevel(
            enhancementTree,
            enhancement.Item.CatalogItemId,
            55));
        Assert.True(_catalog.TryResolve("Soulbound Allegiance: Damage (Superior) (Recipe)", out var recipe));
        Assert.Null(ReferenceRecipeBrowseSupport.FindNodeForRecipeLevel(
            recipeTree,
            recipe.Item.CatalogItemId,
            55));
        Assert.DoesNotContain(
            recipeTree.NodesByKey.Values,
            node => node.Kind == ReferenceEnhancementBrowseNodeKind.Enhancement);
    }

    [Fact]
    public void Recipe_search_and_filters_do_not_expose_levels_above_50()
    {
        var bounds = ReferenceRecipeBrowseSupport.GetBrowseBounds(_catalog);
        Assert.Equal(50, bounds.MaximumLevel);
        Assert.DoesNotContain(
            ReferenceRecipeBrowseSupport.GetRarityFilterOptions(_catalog).Select(option => option.Label),
            label => label.Contains("+", StringComparison.Ordinal));

        var leakedFilterTree = ReferenceRecipeBrowseSupport.BuildBrowseTree(
            _catalog,
            new ReferenceEnhancementBrowseFilter { MinLevel = 1, MaxLevel = 55 });
        Assert.DoesNotContain(
            leakedFilterTree.NodesByKey.Values,
            node => node.Kind == ReferenceEnhancementBrowseNodeKind.Level && node.PresentationLevel is > 50);

        using var viewModel = CreateViewModel();
        Assert.True(viewModel.EnhancementMaxLevel > 50);

        viewModel.SelectSectionChipCommand.Execute(ReferenceSectionId.Recipes);

        Assert.Equal(ReferenceSectionId.Recipes, viewModel.ActiveSection);
        Assert.Equal(50, viewModel.CatalogMaximumLevel);
        Assert.Equal(50, viewModel.EnhancementMaxLevel);
        Assert.Equal(50, viewModel.LevelFilterOptions.Max());
        Assert.DoesNotContain(viewModel.LevelFilterOptions, level => level > 50);

        viewModel.SearchText = "51";
        Assert.DoesNotContain(
            viewModel.SearchResults,
            result => result.TargetNodeKey.Contains(":lvl:51", StringComparison.Ordinal)
                || result.TargetNodeKey.Contains(":lvl:52", StringComparison.Ordinal)
                || result.TargetNodeKey.Contains(":lvl:53", StringComparison.Ordinal)
                || result.Label.Contains("+1", StringComparison.Ordinal)
                || result.Label.Contains("+2", StringComparison.Ordinal)
                || result.Label.Contains("+3", StringComparison.Ordinal));

        viewModel.SearchText = "Invention: Accuracy (Recipe)";
        Assert.Contains(
            viewModel.SearchResults,
            result => result.Label == "Invention: Accuracy (Recipe)"
                && result.TargetNodeKey.StartsWith("recipe:", StringComparison.Ordinal)
                && !result.TargetNodeKey.Contains(":lvl:", StringComparison.Ordinal));
    }

    private ReferenceEnhancementDetailModel BuildDetail(
        ReferenceEnhancementBrowseTree tree,
        ReferenceEnhancementBrowseNode? node,
        int? presentationLevel = null,
        IInstalledGameAssetProvider? assets = null,
        IEnhancementIconCompositor? compositor = null,
        IHomecomingBoostMetadataProvider? boostMetadata = null) =>
        ReferenceRecipeBrowseSupport.BuildDetail(
            _catalog,
            node,
            tree,
            compositor,
            boostMetadata,
            assets,
            presentationLevel,
            _resolver);

    private ReferenceEnhancementDetailModel BuildEnhancementDetail(
        ReferenceEnhancementBrowseTree tree,
        string enhancementId,
        int presentationLevel)
    {
        var levelNode = ReferenceEnhancementBrowseSupport.FindNodeForEnhancementLevel(
            tree,
            enhancementId,
            presentationLevel);
        Assert.NotNull(levelNode);
        return ReferenceEnhancementBrowseSupport.BuildDetail(
            _catalog,
            levelNode,
            tree,
            _resolver,
            presentationLevel);
    }

    private static FakeBoostMetadataProvider CreateBoostMetadata(ItemReferenceRecord item)
    {
        var provider = new FakeBoostMetadataProvider();
        foreach (var variant in item.SourceVariants)
        {
            provider.With(
                variant.HomecomingSourceId,
                "Damage",
                "Natural",
                "Technology",
                "Magic",
                "Mutation",
                "Science");
        }

        return provider;
    }

    private static int SalvageRaritySortKey(string? rarity) =>
        rarity switch
        {
            "Common" => 0,
            "Uncommon" => 1,
            "Rare" => 2,
            "Very Rare" => 3,
            _ => 4
        };

    private static string? ExpectedSalvageRarityCode(string? rarity) =>
        rarity switch
        {
            "Uncommon" => "ECUncommon",
            "Rare" => "ECRare",
            "Very Rare" => "ECVeryRare",
            _ => null
        };

    private ReferenceEnhancementBrowseNode RequireRecipeNode(
        ReferenceEnhancementBrowseTree tree,
        string recipeName)
    {
        Assert.True(_catalog.TryResolve(recipeName, out var resolution));
        var node = ReferenceRecipeBrowseSupport.FindNodeForRecipeId(tree, resolution.Item.CatalogItemId);
        Assert.NotNull(node);
        return node;
    }

    private static ReferenceEnhancementBrowseNode RequireAncestor(
        ReferenceEnhancementBrowseTree tree,
        ReferenceEnhancementBrowseNode node,
        ReferenceEnhancementBrowseNodeKind kind)
    {
        var current = node;
        while (!string.IsNullOrWhiteSpace(current.ParentNodeKey)
            && tree.NodesByKey.TryGetValue(current.ParentNodeKey, out var parent))
        {
            if (parent.Kind == kind)
            {
                return parent;
            }

            current = parent;
        }

        Assert.Fail($"Expected ancestor kind {kind} for {node.DisplayName}.");
        return node;
    }

    private static int[] GetVisibleLevels(ReferenceEnhancementBrowseNode recipeNode) =>
        recipeNode.Children
            .Where(child => child.Kind == ReferenceEnhancementBrowseNodeKind.Level)
            .Select(child => child.PresentationLevel!.Value)
            .ToArray();

    private static string FormatCost(uint cost) =>
        cost.ToString("N0", CultureInfo.InvariantCulture);

    private ReferenceViewModel CreateViewModel(
        IInstalledGameAssetProvider? assets = null,
        IEnhancementIconCompositor? compositor = null) =>
        new(
            new TestGameplaySessionContextSupport.FakeApplicationOrchestrator(),
            new FakeGameRuntimeService(),
            _catalog,
            compositor,
            installedGameAssetProvider: assets);

    private sealed class FakeInstalledGameAssetProvider : IInstalledGameAssetProvider
    {
        public List<string?> Requests { get; } = [];

        public HashSet<string> MissingIdentities { get; } = new(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, ImageSource> _resolved = new(StringComparer.OrdinalIgnoreCase);

        public ImageSource? TryResolve(string? iconIdentity)
        {
            Requests.Add(iconIdentity);
            if (string.IsNullOrWhiteSpace(iconIdentity)
                || MissingIdentities.Contains(iconIdentity))
            {
                return null;
            }

            if (_resolved.TryGetValue(iconIdentity, out var cached))
            {
                return cached;
            }

            var pixels = new byte[] { 40, 80, 120, 200 };
            var source = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, pixels, 4);
            source.Freeze();
            _resolved[iconIdentity] = source;
            return source;
        }
    }

    private sealed class FakeBoostMetadataProvider : IHomecomingBoostMetadataProvider
    {
        private readonly Dictionary<string, IReadOnlyList<string>> _boostsAllowed = new(StringComparer.Ordinal);

        public FakeBoostMetadataProvider With(string sourceId, params string[] boostsAllowed)
        {
            _boostsAllowed[sourceId] = boostsAllowed;
            return this;
        }

        public IReadOnlyList<string>? TryGetBoostsAllowed(string? homecomingSourceId)
        {
            if (string.IsNullOrWhiteSpace(homecomingSourceId))
            {
                return null;
            }

            return _boostsAllowed.TryGetValue(homecomingSourceId.Trim(), out var boostsAllowed)
                ? boostsAllowed
                : null;
        }
    }

    private sealed class RecordingEnhancementIconCompositor : IEnhancementIconCompositor
    {
        public List<EnhancementIconCompositionRequest> Requests { get; } = [];

        public bool ReturnNull { get; init; }

        private readonly Dictionary<EnhancementIconCompositionRequest, ImageSource> _markers = [];

        public ImageSource? TryCompose(EnhancementIconCompositionRequest request)
        {
            Requests.Add(request);
            if (ReturnNull)
            {
                return null;
            }

            if (_markers.TryGetValue(request, out var cached))
            {
                return cached;
            }

            var pixels = new byte[] { 40, 80, 120, 200 };
            var source = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, pixels, 4);
            source.Freeze();
            _markers[request] = source;
            return source;
        }
    }
}
