using CoHAnalytics.Homecoming;
using CoHAnalytics.ReferenceData;
using CoHAnalytics.ViewModels.Workspaces;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CoHAnalytics.Tests.Workspaces;

public sealed class ReferenceEnhancementBrowseSupportTests
{
    private readonly IItemReferenceCatalog _catalog = ItemReferenceCatalogFactory.LoadEmbeddedProduction();
    private readonly IEnhancementHelpResolver _resolver =
        ItemReferenceCatalogFactory.CreateEmbeddedProductionResolver();

    private ReferenceEnhancementDetailModel BuildDetail(
        ReferenceEnhancementBrowseNode? node,
        ReferenceEnhancementBrowseTree tree,
        int? presentationLevel = null,
        IEnhancementIconCompositor? iconCompositor = null,
        IHomecomingBoostMetadataProvider? boostMetadata = null) =>
        ReferenceEnhancementBrowseSupport.BuildDetail(
            _catalog,
            node,
            tree,
            _resolver,
            presentationLevel,
            iconCompositor,
            boostMetadata);

    private ReferenceEnhancementBrowseNode? RequireLevelNode(
        ReferenceEnhancementBrowseTree tree,
        string enhancementDisplayName,
        int presentationLevel)
    {
        Assert.True(_catalog.TryResolve(enhancementDisplayName, out var resolution));
        var enhancementNode = ReferenceEnhancementBrowseSupport.FindNodeForEnhancementId(
            tree,
            resolution.Item.CatalogItemId);
        Assert.NotNull(enhancementNode);
        var levelNode = ReferenceEnhancementBrowseSupport.FindNodeForEnhancementLevel(
            tree,
            resolution.Item.CatalogItemId,
            presentationLevel);
        Assert.NotNull(levelNode);
        return levelNode;
    }

    private static int[] GetVisibleLevelPresentationLevels(
        ReferenceEnhancementBrowseTree tree,
        string enhancementId) =>
        ReferenceEnhancementBrowseSupport.FindNodeForEnhancementId(tree, enhancementId)!
            .Children
            .Where(child => child.Kind == ReferenceEnhancementBrowseNodeKind.Level)
            .Select(child => child.PresentationLevel!.Value)
            .ToArray();

    [Fact]
    public void Browse_tree_excludes_historical_enhancements_and_sets()
    {
        Assert.True(_catalog.IsLoaded, _catalog.LoadFailureReason);

        var allEnhancements = _catalog.GetEnhancements(ReferenceCatalogQueryScope.AllHomecomingIdentities);
        var currentEnhancements = _catalog.GetEnhancements(ReferenceCatalogQueryScope.CurrentHomecoming);
        var tree = ReferenceEnhancementBrowseSupport.BuildBrowseTree(_catalog);

        Assert.True(allEnhancements.Count > currentEnhancements.Count);
        Assert.Equal(1628, currentEnhancements.Count);
        Assert.Equal(1628, tree.Census.CurrentEnhancementCount);

        var historicalOnly = allEnhancements
            .Select(item => item.CatalogItemId)
            .Except(currentEnhancements.Select(item => item.CatalogItemId), StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(historicalOnly);
        Assert.All(historicalOnly, id =>
            Assert.False(tree.NodesByKey.ContainsKey($"enh:{id}")));
    }

    [Fact]
    public void Set_hierarchy_builds_category_set_and_logical_member_levels()
    {
        Assert.True(_catalog.IsLoaded, _catalog.LoadFailureReason);

        var tree = ReferenceEnhancementBrowseSupport.BuildBrowseTree(_catalog);
        var setsRoot = tree.RootNodes.Single(node => node.NodeKey == ReferenceEnhancementBrowseSupport.SetsRootNodeKey);

        Assert.NotEmpty(setsRoot.Children);
        var melee = setsRoot.Children.FirstOrDefault(node =>
            node.DisplayName.Equals("Melee", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(melee);

        var bonesnap = melee!.Children.FirstOrDefault(node =>
            node.DisplayName.Equals("Bonesnap", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(bonesnap);
        Assert.Equal(ReferenceEnhancementBrowseNodeKind.Set, bonesnap!.Kind);
        Assert.NotEmpty(bonesnap.Children);
        Assert.All(bonesnap.Children, member =>
            Assert.Equal(ReferenceEnhancementBrowseNodeKind.Enhancement, member.Kind));
    }

    [Fact]
    public void Common_invention_branch_contains_only_crafted_invention_current_records()
    {
        Assert.True(_catalog.IsLoaded, _catalog.LoadFailureReason);

        var tree = ReferenceEnhancementBrowseSupport.BuildBrowseTree(_catalog);
        var commonRoot = tree.RootNodes.Single(node => node.NodeKey == ReferenceEnhancementBrowseSupport.CommonRootNodeKey);

        Assert.Equal(27, tree.Census.CommonInventionEnhancementCount);
        var commonEnhancementIds = commonRoot.Children
            .SelectMany(group => group.Children)
            .Select(node => node.EnhancementId!)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(27, commonEnhancementIds.Count);

        foreach (var enhancementId in commonEnhancementIds)
        {
            Assert.True(_catalog.TryGetById(enhancementId, out var item));
            Assert.Equal(ReferenceEnhancementFamily.CraftedInvention, item.EnhancementFamily);
            Assert.Null(item.EnhancementSetId);
            Assert.NotEqual(ReferenceEnhancementFamily.OriginOrTraining, item.EnhancementFamily);
        }
    }

    [Fact]
    public void Selection_builds_category_set_and_enhancement_detail_models()
    {
        Assert.True(_catalog.IsLoaded, _catalog.LoadFailureReason);

        var tree = ReferenceEnhancementBrowseSupport.BuildBrowseTree(_catalog);
        var melee = tree.NodesByKey.Values.First(node =>
            node.Kind == ReferenceEnhancementBrowseNodeKind.Category
            && node.DisplayName.Equals("Melee", StringComparison.OrdinalIgnoreCase));
        var bonesnap = melee.Children.First(node =>
            node.DisplayName.Equals("Bonesnap", StringComparison.OrdinalIgnoreCase));
        var member = bonesnap.Children.First();

        var categoryDetail = BuildDetail(melee, tree);
        var setDetail = BuildDetail(bonesnap, tree);
        var enhancementDetail = BuildDetail(member, tree);

        Assert.Equal(ReferenceEnhancementDetailKind.Category, categoryDetail.Kind);
        Assert.Equal("Melee", categoryDetail.Title);
        Assert.NotEmpty(categoryDetail.SetItems);

        Assert.Equal(ReferenceEnhancementDetailKind.Set, setDetail.Kind);
        Assert.Equal("Bonesnap", setDetail.Title);
        Assert.NotEmpty(setDetail.MemberItems);
        Assert.NotEmpty(setDetail.SetBonusTiers);

        Assert.Equal(ReferenceEnhancementDetailKind.Enhancement, enhancementDetail.Kind);
        Assert.NotEmpty(enhancementDetail.Title);
    }

    [Fact]
    public void Set_bonus_tiers_preserve_requires_patterns_and_multiple_auto_powers()
    {
        Assert.True(_catalog.IsLoaded, _catalog.LoadFailureReason);

        var tree = ReferenceEnhancementBrowseSupport.BuildBrowseTree(_catalog);
        var aegis = FindSetNode(tree, "Aegis");
        Assert.NotNull(aegis);

        var detail = BuildDetail(aegis!, tree);
        Assert.Contains(detail.SetBonusTiers, tier =>
            tier.RequiresPattern == ReferenceEnhancementSetBonusRequiresPattern.PieceGate
            && !string.IsNullOrWhiteSpace(tier.ConditionLabel));

        var pvpSet = tree.NodesByKey.Values
            .FirstOrDefault(node =>
                node.Kind == ReferenceEnhancementBrowseNodeKind.Set
                && BuildDetail(node, tree).SetBonusTiers
                    .Any(tier => tier.RequiresPattern == ReferenceEnhancementSetBonusRequiresPattern.PvPMap));

        Assert.NotNull(pvpSet);

        var multiPowerSet = tree.NodesByKey.Values
            .First(node =>
                node.Kind == ReferenceEnhancementBrowseNodeKind.Set
                && BuildDetail(node, tree).SetBonusTiers
                    .Any(tier => tier.AutoPowerHelps.Count > 1));

        Assert.NotEmpty(
            BuildDetail(multiPowerSet, tree).SetBonusTiers);
    }

    [Fact]
    public void Variant_help_uses_logical_help_when_agreed_and_variant_sections_when_not()
    {
        Assert.True(_catalog.IsLoaded, _catalog.LoadFailureReason);

        Assert.True(_catalog.TryResolve("Invention: Accuracy", out var agreed));
        var agreedNode = ReferenceEnhancementBrowseSupport.FindNodeForEnhancementId(
            ReferenceEnhancementBrowseSupport.BuildBrowseTree(_catalog),
            agreed.Item.CatalogItemId);
        Assert.NotNull(agreedNode);

        var agreedDetail = BuildDetail(
            agreedNode,
            ReferenceEnhancementBrowseSupport.BuildBrowseTree(_catalog),
            presentationLevel: 50);
        Assert.False(string.IsNullOrWhiteSpace(agreedDetail.ResolvedDisplayHelp));
        Assert.Equal("Increases accuracy by 42.4%.", agreedDetail.ResolvedDisplayHelp);
        Assert.Contains("{Boost.Attrib.Accuracy.Scale}", agreedDetail.CanonicalDisplayHelp!, StringComparison.Ordinal);
        Assert.False(agreedDetail.ShowVariantHelpSections);

        var tree = ReferenceEnhancementBrowseSupport.BuildBrowseTree(_catalog);
        var disagreementNode = tree.NodesByKey.Values
            .FirstOrDefault(node =>
                node.Kind == ReferenceEnhancementBrowseNodeKind.Enhancement
                && _catalog.TryGetById(node.EnhancementId!, out var item)
                && string.IsNullOrWhiteSpace(item.DisplayHelp)
                && item.SourceVariants.Select(variant => variant.DisplayHelp).Distinct().Count() > 1);

        Assert.NotNull(disagreementNode);
        var disagreementDetail = BuildDetail(disagreementNode, tree);
        Assert.Null(disagreementDetail.ResolvedDisplayHelp);
        Assert.True(disagreementDetail.ShowVariantHelpSections);
        Assert.True(disagreementDetail.VariantHelpSections.Count >= 2);
        Assert.All(
            disagreementDetail.VariantHelpSections,
            section => Assert.DoesNotContain("{Boost.Attrib", section.HelpText, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Cupid's Crush")]
    [InlineData("Overwhelming Force")]
    public void Null_rarity_sets_do_not_display_invented_rarity(string setName)
    {
        Assert.True(_catalog.IsLoaded, _catalog.LoadFailureReason);

        var tree = ReferenceEnhancementBrowseSupport.BuildBrowseTree(_catalog);
        var setNode = FindSetNode(tree, setName);
        Assert.NotNull(setNode);

        var detail = BuildDetail(setNode!, tree);
        Assert.Null(detail.RarityLabel);
    }

    [Fact]
    public void Common_io_group_uses_presentation_label_not_display_name_parsing()
    {
        Assert.True(_catalog.IsLoaded, _catalog.LoadFailureReason);

        var tree = ReferenceEnhancementBrowseSupport.BuildBrowseTree(_catalog);
        var rechargeGroup = tree.NodesByKey.Values.FirstOrDefault(node =>
            node.Kind == ReferenceEnhancementBrowseNodeKind.CommonGroup
            && node.DisplayName.Contains("Recharge", StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(rechargeGroup);
        Assert.Equal("Recharge Reduction", rechargeGroup!.DisplayName);
    }

    [Fact]
    public void Crafted_accuracy_detail_resolves_supported_scale_tokens_at_level_50()
    {
        Assert.True(_catalog.TryResolve("Invention: Accuracy", out var resolution));
        var tree = ReferenceEnhancementBrowseSupport.BuildBrowseTree(_catalog);
        var levelNode = RequireLevelNode(tree, "Invention: Accuracy", 50);
        Assert.NotNull(levelNode);

        var detail = BuildDetail(levelNode, tree, presentationLevel: 50);

        Assert.Equal(50, detail.PresentationLevel);
        Assert.Equal("Values shown for Level 50", detail.PresentationLevelLabel);
        Assert.Equal("Increases accuracy by 42.4%.", detail.ResolvedDisplayHelp);
        Assert.Equal(EnhancementHelpResolutionStatus.FullyResolved, detail.HelpResolutionStatus);
        Assert.Contains("{Boost.Attrib.Accuracy.Scale}", detail.CanonicalDisplayHelp!, StringComparison.Ordinal);
        Assert.DoesNotContain("{Boost.Attrib", detail.ResolvedDisplayHelp!, StringComparison.Ordinal);
    }

    [Fact]
    public void Crafted_accuracy_enhancement_node_without_level_does_not_assume_level_50()
    {
        Assert.True(_catalog.TryResolve("Invention: Accuracy", out var resolution));
        var tree = ReferenceEnhancementBrowseSupport.BuildBrowseTree(_catalog);
        var node = ReferenceEnhancementBrowseSupport.FindNodeForEnhancementId(tree, resolution.Item.CatalogItemId);
        Assert.NotNull(node);

        var detail = BuildDetail(node, tree);

        Assert.Null(detail.PresentationLevelLabel);
        Assert.Null(detail.ResolvedDisplayHelp);
    }

    [Fact]
    public void Crafted_damage_detail_resolves_to_observed_level_50_magnitude()
    {
        Assert.True(_catalog.TryResolve("Invention: Damage", out var resolution));
        var tree = ReferenceEnhancementBrowseSupport.BuildBrowseTree(_catalog);
        var node = ReferenceEnhancementBrowseSupport.FindNodeForEnhancementId(tree, resolution.Item.CatalogItemId);
        Assert.NotNull(node);

        var detail = BuildDetail(RequireLevelNode(tree, "Invention: Damage", 50)!, tree, presentationLevel: 50);

        Assert.Equal("Increases damage by 42.4%.", detail.ResolvedDisplayHelp);
    }

    [Fact]
    public void Positrons_blast_resolves_dual_aspect_logical_help_at_level_50()
    {
        var tree = ReferenceEnhancementBrowseSupport.BuildBrowseTree(_catalog);
        var levelNode = RequireLevelNode(tree, "Positron's Blast: Accuracy/Damage", 50);
        Assert.NotNull(levelNode);

        var detail = BuildDetail(levelNode, tree, presentationLevel: 50);

        Assert.False(detail.ShowVariantHelpSections);
        Assert.Equal(
            "Enhances the damage of a power by 26.5% and accuracy by 26.5%.",
            detail.ResolvedDisplayHelp);
        Assert.Contains("{Boost.Attrib.Damage.Scale}", detail.CanonicalDisplayHelp!, StringComparison.Ordinal);
        Assert.Contains("{Boost.Attrib.Accuracy.Scale}", detail.CanonicalDisplayHelp!, StringComparison.Ordinal);
    }

    [Fact]
    public void Attuned_variant_help_uses_max_boost_level_at_presentation_level_50()
    {
        Assert.True(_catalog.TryGetById("ENH-00804", out var item));
        var attuned = item.SourceVariants.First(variant =>
            string.Equals(variant.SourceForm, "Attuned", StringComparison.Ordinal));
        var expected = _resolver.Resolve(
            attuned.DisplayHelp!,
            attuned,
            new EnhancementHelpPresentationContext
            {
                PresentationLevel = ReferenceEnhancementBrowseSupport.DefaultPresentationLevel
            }).ResolvedText;

        var tree = ReferenceEnhancementBrowseSupport.BuildBrowseTree(_catalog);
        var levelNode = ReferenceEnhancementBrowseSupport.FindNodeForEnhancementLevel(
            tree,
            item.CatalogItemId,
            ReferenceEnhancementBrowseSupport.DefaultPresentationLevel);
        Assert.NotNull(levelNode);

        var section = Assert.Single(
            BuildDetail(levelNode, tree, presentationLevel: 50).VariantHelpSections,
            value => value.SourceFormLabel == "Attuned");

        Assert.Equal(expected, section.HelpText);
    }

    [Fact]
    public void Plain_english_proc_help_remains_unchanged_without_resolution_note()
    {
        Assert.True(_catalog.TryResolve("Eradication: Chance for Energy Damage", out var resolution));
        var tree = ReferenceEnhancementBrowseSupport.BuildBrowseTree(_catalog);
        var node = ReferenceEnhancementBrowseSupport.FindNodeForEnhancementId(tree, resolution.Item.CatalogItemId);
        Assert.NotNull(node);

        var detail = BuildDetail(node, tree);

        Assert.Null(detail.ResolvedDisplayHelp);
        Assert.True(detail.ShowVariantHelpSections);
        Assert.Equal(2, detail.VariantHelpSections.Count);
        Assert.All(
            detail.VariantHelpSections,
            section =>
            {
                Assert.DoesNotContain("{Boost.Attrib", section.HelpText, StringComparison.Ordinal);
                Assert.Equal(section.CanonicalHelpText, section.HelpText);
                Assert.Equal(EnhancementHelpResolutionStatus.Unchanged, section.HelpResolutionStatus);
            });
        Assert.False(detail.ShowHelpResolutionNote);
    }

    [Fact]
    public void Hecatomb_damage_detail_displays_resolved_player_facing_text()
    {
        Assert.True(_catalog.TryResolve("Hecatomb: Damage", out var resolution));
        var tree = ReferenceEnhancementBrowseSupport.BuildBrowseTree(_catalog);
        var levelNode = RequireLevelNode(tree, "Hecatomb: Damage", 50);
        Assert.NotNull(levelNode);

        var detail = BuildDetail(levelNode, tree, presentationLevel: 50);

        Assert.False(string.IsNullOrWhiteSpace(detail.ResolvedDisplayHelp));
        Assert.DoesNotContain("{Boost.Attrib", detail.ResolvedDisplayHelp!, StringComparison.Ordinal);
    }

    [Fact]
    public void Steadfast_set_bonus_preserves_unresolved_token_and_shows_note()
    {
        var tree = ReferenceEnhancementBrowseSupport.BuildBrowseTree(_catalog);
        var steadfast = FindSetNode(tree, "Steadfast Protection");
        Assert.NotNull(steadfast);

        var detail = BuildDetail(steadfast!, tree);
        var unresolvedHelp = detail.SetBonusTiers
            .SelectMany(tier => tier.AutoPowerHelps)
            .First(help => help.Contains("{ Boost.Attrib.Defense.Scale}", StringComparison.Ordinal));

        Assert.Contains("{ Boost.Attrib.Defense.Scale}", unresolvedHelp, StringComparison.Ordinal);
    }

    [Fact]
    public void Set_bonus_token_help_is_resolved_when_supported()
    {
        var tree = ReferenceEnhancementBrowseSupport.BuildBrowseTree(_catalog);
        var resolvedSet = tree.NodesByKey.Values
            .First(node =>
                node.Kind == ReferenceEnhancementBrowseNodeKind.Set
                && BuildDetail(node, tree).SetBonusTiers
                    .SelectMany(tier => tier.AutoPowerHelps)
                    .Any(help => !help.Contains('{', StringComparison.Ordinal)
                        && help.Contains('%', StringComparison.Ordinal)));

        Assert.NotNull(resolvedSet);
    }

    [Fact]
    public void Level_filter_20_to_35_keeps_positron_blast_within_range()
    {
        var filter = new ReferenceEnhancementBrowseFilter
        {
            MinLevel = 20,
            MaxLevel = 35
        };
        var tree = ReferenceEnhancementBrowseSupport.BuildBrowseTree(_catalog, filter);
        var levelNode = RequireLevelNode(tree, "Positron's Blast: Accuracy/Damage", 25);
        Assert.NotNull(levelNode);
        Assert.Null(ReferenceEnhancementBrowseSupport.FindNodeForEnhancementLevel(
            tree,
            levelNode.EnhancementId!,
            19));
    }

    [Fact]
    public void Level_filter_excludes_enhancements_with_no_visible_levels()
    {
        var filter = new ReferenceEnhancementBrowseFilter
        {
            MinLevel = 45,
            MaxLevel = 50
        };
        var tree = ReferenceEnhancementBrowseSupport.BuildBrowseTree(_catalog, filter);
        Assert.Null(FindSetNode(tree, "Bonesnap"));
    }

    [Fact]
    public void Explicit_level_nodes_are_restricted_to_selected_range_and_sorted()
    {
        var filter = new ReferenceEnhancementBrowseFilter
        {
            MinLevel = 20,
            MaxLevel = 35
        };
        var tree = ReferenceEnhancementBrowseSupport.BuildBrowseTree(_catalog, filter);
        Assert.True(_catalog.TryResolve("Positron's Blast: Accuracy/Damage", out var resolution));
        var enhancementNode = ReferenceEnhancementBrowseSupport.FindNodeForEnhancementId(
            tree,
            resolution.Item.CatalogItemId);
        Assert.NotNull(enhancementNode);

        var levels = enhancementNode!.Children
            .Where(child => child.Kind == ReferenceEnhancementBrowseNodeKind.Level)
            .Select(child => child.PresentationLevel!.Value)
            .ToArray();

        Assert.Equal([20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35], levels);
    }

    [Fact]
    public void Selecting_level_20_and_25_resolves_positron_acc_dam_end_to_observed_values()
    {
        var tree = ReferenceEnhancementBrowseSupport.BuildBrowseTree(_catalog);
        var level20 = RequireLevelNode(tree, "Positron's Blast: Accuracy/Damage/Endurance", 20);
        var level25 = RequireLevelNode(tree, "Positron's Blast: Accuracy/Damage/Endurance", 25);

        var detail20 = BuildDetail(level20, tree, presentationLevel: 20);
        var detail25 = BuildDetail(level25, tree, presentationLevel: 25);

        Assert.Equal(
            "Enhances the accuracy of a power by 12.8% and damage by 12.8% and reduces endurance cost by 12.8%.",
            detail20.ResolvedDisplayHelp);
        Assert.Equal(
            "Enhances the accuracy of a power by 16% and damage by 16% and reduces endurance cost by 16%.",
            detail25.ResolvedDisplayHelp);
    }

    [Fact]
    public void Selecting_level_26_resolves_positron_acc_dam_end_to_observed_16_6_percent()
    {
        var tree = ReferenceEnhancementBrowseSupport.BuildBrowseTree(_catalog);
        var level26 = RequireLevelNode(tree, "Positron's Blast: Accuracy/Damage/Endurance", 26);
        var detail = BuildDetail(level26, tree, presentationLevel: 26);

        Assert.Equal(
            "Enhances the accuracy of a power by 16.6% and damage by 16.6% and reduces endurance cost by 16.6%.",
            detail.ResolvedDisplayHelp);
    }

    [Fact]
    public void Normalize_filter_clamps_minimum_when_it_exceeds_maximum()
    {
        var bounds = ReferenceEnhancementBrowseSupport.GetBrowseBounds(_catalog);
        var normalized = ReferenceEnhancementBrowseSupport.NormalizeFilter(
            new ReferenceEnhancementBrowseFilter
            {
                MinLevel = 40,
                MaxLevel = 20
            },
            bounds);

        Assert.Equal(40, normalized.MinLevel);
        Assert.Equal(40, normalized.MaxLevel);
    }

    [Fact]
    public void Rarity_filter_limits_sets_to_selected_rarity()
    {
        var filter = new ReferenceEnhancementBrowseFilter
        {
            MinLevel = 10,
            MaxLevel = 50,
            Rarity = "Rare"
        };
        var tree = ReferenceEnhancementBrowseSupport.BuildBrowseTree(_catalog, filter);
        var positronSet = FindSetNode(tree, "Positron's Blast");
        Assert.NotNull(positronSet);

        var uncommonSet = FindSetNode(tree, "Bonesnap");
        Assert.Null(uncommonSet);
    }

    [Fact]
    public void Origin_filter_is_not_supported_by_current_catalog_data()
    {
        Assert.False(ReferenceEnhancementBrowseSupport.OriginFilterSupportedByCatalogData());
    }

    [Fact]
    public void Combined_level_and_rarity_filters_compose()
    {
        var filter = new ReferenceEnhancementBrowseFilter
        {
            MinLevel = 20,
            MaxLevel = 35,
            Rarity = "Rare"
        };
        var tree = ReferenceEnhancementBrowseSupport.BuildBrowseTree(_catalog, filter);
        Assert.NotNull(FindSetNode(tree, "Positron's Blast"));
        Assert.NotNull(RequireLevelNode(tree, "Positron's Blast: Accuracy/Damage", 30));
        Assert.Null(FindSetNode(tree, "Bonesnap"));
    }

    [Fact]
    public void Search_results_only_include_nodes_present_in_filtered_tree()
    {
        var filter = new ReferenceEnhancementBrowseFilter
        {
            MinLevel = 10,
            MaxLevel = 19,
            Rarity = "Rare"
        };
        var tree = ReferenceEnhancementBrowseSupport.BuildBrowseTree(_catalog, filter);
        Assert.True(_catalog.TryResolve("Positron's Blast: Accuracy/Damage", out var resolution));
        Assert.Null(ReferenceEnhancementBrowseSupport.FindNodeForEnhancementId(tree, resolution.Item.CatalogItemId));
    }

    [Fact]
    public void Available_as_deduplicates_repeated_crafted_variants()
    {
        Assert.True(_catalog.TryResolve("Invention: Accuracy", out var resolution));
        var label = ReferenceEnhancementBrowseSupport.FormatAvailableSourceForms(resolution.Item.SourceVariants);
        Assert.Equal("Crafted", label);
    }

    [Fact]
    public void Available_as_preserves_distinct_source_forms()
    {
        Assert.True(_catalog.TryGetById("ENH-00804", out var item));
        var label = ReferenceEnhancementBrowseSupport.FormatAvailableSourceForms(item.SourceVariants);
        Assert.Equal("Crafted · Attuned", label);
    }

    [Fact]
    public void Set_hierarchy_remains_category_set_enhancement_with_level_layer()
    {
        var tree = ReferenceEnhancementBrowseSupport.BuildBrowseTree(_catalog);
        var positron = FindSetNode(tree, "Positron's Blast");
        Assert.NotNull(positron);
        var member = positron!.Children.First(child =>
            child.Kind == ReferenceEnhancementBrowseNodeKind.Enhancement
            && child.Children.Any(level => level.Kind == ReferenceEnhancementBrowseNodeKind.Level));

        Assert.Equal(ReferenceEnhancementBrowseNodeKind.Set, positron.Kind);
        Assert.Equal(ReferenceEnhancementBrowseNodeKind.Enhancement, member.Kind);
        Assert.Contains(
            member.Children,
            child => child.Kind == ReferenceEnhancementBrowseNodeKind.Level && child.PresentationLevel == 20);
    }

    [Fact]
    public void Common_io_selectable_levels_include_boosted_presentation_levels_for_boostable_crafted_variant()
    {
        Assert.True(_catalog.TryResolve("Invention: Accuracy", out var resolution));
        var levels = ReferenceEnhancementBrowseSupport.GetSelectableLevels(resolution.Item, parentSet: null);
        Assert.Equal(
            new[] { 10, 15, 20, 25, 30, 35, 40, 45, 50, 51, 52, 53, 54, 55 },
            levels);
    }

    [Fact]
    public void Boostable_crafted_set_piece_selectable_levels_include_51_through_55()
    {
        Assert.True(
            _catalog.TryResolve("Soulbound Allegiance: Damage/Endurance Reduction", out var resolution));
        Assert.True(_catalog.TryGetEnhancementSetById(resolution.Item.EnhancementSetId!, out var set));
        var levels = ReferenceEnhancementBrowseSupport.GetSelectableLevels(resolution.Item, set);
        Assert.Equal(new[] { 50, 51, 52, 53, 54, 55 }, levels);
    }

    [Fact]
    public void Attuned_only_resolution_variant_does_not_expose_boosted_presentation_levels()
    {
        Assert.True(_catalog.TryResolve("Positron's Blast: Accuracy/Damage", out var resolution));
        var attuned = resolution.Item.SourceVariants.Single(variant =>
            string.Equals(variant.SourceForm, "Attuned", StringComparison.Ordinal));
        Assert.False(EnhancementHelpResolverBoosterMultipliers.SupportsBoostedPresentationLevels(attuned));
    }

    [Fact]
    public void Set_piece_selectable_levels_use_continuous_set_range()
    {
        Assert.True(_catalog.TryResolve("Positron's Blast: Accuracy/Damage", out var resolution));
        Assert.True(_catalog.TryGetEnhancementSetById(resolution.Item.EnhancementSetId!, out var set));
        var levels = ReferenceEnhancementBrowseSupport.GetSelectableLevels(resolution.Item, set);
        Assert.Equal(36, levels.Count);
        Assert.Equal(20, levels[0]);
        Assert.Equal(50, levels[30]);
        Assert.Equal(55, levels[^1]);
        Assert.Contains(26, levels);
        Assert.Equal(new[] { 51, 52, 53, 54, 55 }, levels.Skip(31));
    }

    [Fact]
    public void Browse_bounds_include_boosted_effective_level_cap()
    {
        var bounds = ReferenceEnhancementBrowseSupport.GetBrowseBounds(_catalog);
        Assert.Equal(EnhancementHelpResolverBoosterMultipliers.MaxBoostedPresentationLevel, bounds.MaximumLevel);
    }

    [Fact]
    public void Boostable_common_io_browse_tree_exposes_level_51_through_55_nodes()
    {
        var tree = ReferenceEnhancementBrowseSupport.BuildBrowseTree(_catalog);
        Assert.True(_catalog.TryResolve("Invention: Accuracy", out var resolution));

        var levels = GetVisibleLevelPresentationLevels(tree, resolution.Item.CatalogItemId);
        Assert.Equal(
            new[] { 10, 15, 20, 25, 30, 35, 40, 45, 50, 51, 52, 53, 54, 55 },
            levels);

        foreach (var level in new[] { 51, 52, 53, 54, 55 })
        {
            Assert.NotNull(RequireLevelNode(tree, "Invention: Accuracy", level));
        }
    }

    [Fact]
    public void Boostable_crafted_set_piece_browse_tree_exposes_level_51_through_55_nodes()
    {
        var tree = ReferenceEnhancementBrowseSupport.BuildBrowseTree(_catalog);
        Assert.True(
            _catalog.TryResolve("Soulbound Allegiance: Damage/Endurance Reduction", out var resolution));

        var levels = GetVisibleLevelPresentationLevels(tree, resolution.Item.CatalogItemId);
        Assert.Equal(new[] { 50, 51, 52, 53, 54, 55 }, levels);
        Assert.NotNull(RequireLevelNode(tree, "Soulbound Allegiance: Damage/Endurance Reduction", 55));
    }

    [Fact]
    public void Attuned_only_enhancement_browse_tree_does_not_expose_boosted_level_nodes()
    {
        var tree = ReferenceEnhancementBrowseSupport.BuildBrowseTree(_catalog);
        Assert.True(_catalog.TryGetById("ENH-00075", out var item));
        Assert.DoesNotContain(
            item.SourceVariants,
            variant => string.Equals(variant.SourceForm, "Crafted", StringComparison.Ordinal));

        var levels = GetVisibleLevelPresentationLevels(tree, item.CatalogItemId);
        Assert.DoesNotContain(51, levels);
        Assert.DoesNotContain(55, levels);
        Assert.Null(ReferenceEnhancementBrowseSupport.FindNodeForEnhancementLevel(tree, item.CatalogItemId, 51));
    }

    [Fact]
    public void Level_filter_50_to_55_shows_native_and_boosted_levels_for_boostable_common_io()
    {
        var filter = new ReferenceEnhancementBrowseFilter
        {
            MinLevel = 50,
            MaxLevel = 55
        };
        var tree = ReferenceEnhancementBrowseSupport.BuildBrowseTree(_catalog, filter);
        Assert.True(_catalog.TryResolve("Invention: Accuracy", out var resolution));

        Assert.Equal(
            new[] { 50, 51, 52, 53, 54, 55 },
            GetVisibleLevelPresentationLevels(tree, resolution.Item.CatalogItemId));
    }

    [Fact]
    public void Level_filter_51_to_55_keeps_boostable_common_io_visible_with_only_boosted_levels()
    {
        var filter = new ReferenceEnhancementBrowseFilter
        {
            MinLevel = 51,
            MaxLevel = 55
        };
        var tree = ReferenceEnhancementBrowseSupport.BuildBrowseTree(_catalog, filter);
        Assert.True(_catalog.TryResolve("Invention: Accuracy", out var resolution));

        Assert.NotNull(ReferenceEnhancementBrowseSupport.FindNodeForEnhancementId(
            tree,
            resolution.Item.CatalogItemId));
        Assert.Equal(
            new[] { 51, 52, 53, 54, 55 },
            GetVisibleLevelPresentationLevels(tree, resolution.Item.CatalogItemId));
        Assert.Null(ReferenceEnhancementBrowseSupport.FindNodeForEnhancementLevel(
            tree,
            resolution.Item.CatalogItemId,
            50));
    }

    [Fact]
    public void Boosted_level_nodes_remain_numerically_sorted_in_browse_tree()
    {
        var tree = ReferenceEnhancementBrowseSupport.BuildBrowseTree(_catalog);
        Assert.True(_catalog.TryResolve("Invention: Accuracy", out var resolution));

        var levels = GetVisibleLevelPresentationLevels(tree, resolution.Item.CatalogItemId);
        Assert.Equal(levels.OrderBy(level => level).ToArray(), levels);
        Assert.Equal(new[] { 50, 51, 52, 53, 54, 55 }, levels[^6..]);
    }

    [Theory]
    [InlineData(51, "44.5")]
    [InlineData(55, "53")]
    public void Selecting_boosted_accuracy_levels_resolves_through_existing_resolver(
        int presentationLevel,
        string expectedAccuracy)
    {
        var tree = ReferenceEnhancementBrowseSupport.BuildBrowseTree(_catalog);
        var levelNode = RequireLevelNode(tree, "Invention: Accuracy", presentationLevel);
        var detail = BuildDetail(levelNode, tree, presentationLevel: presentationLevel);

        Assert.Equal(presentationLevel, detail.PresentationLevel);
        Assert.Equal($"Values shown for Level {presentationLevel}", detail.PresentationLevelLabel);
        Assert.Equal($"Increases accuracy by {expectedAccuracy}%.", detail.ResolvedDisplayHelp);
    }

    [Fact]
    public void Selecting_soulbound_level_55_resolves_both_aspects_through_existing_resolver()
    {
        var tree = ReferenceEnhancementBrowseSupport.BuildBrowseTree(_catalog);
        var levelNode = RequireLevelNode(tree, "Soulbound Allegiance: Damage/Endurance Reduction", 55);
        var detail = BuildDetail(levelNode, tree, presentationLevel: 55);

        Assert.Equal(
            "UNIQUE -- No more than 1 enhancement of this type may be slotted by a character. Enhances the damage of a power by 41.4% and reduces endurance cost by 41.4%.",
            detail.ResolvedDisplayHelp);
    }

    [Fact]
    public void Enhancement_browse_filters_are_hidden_outside_enhancements_section()
    {
        Assert.True(ReferenceEnhancementBrowseSupport.ShouldShowEnhancementBrowseFilters(
            ReferenceSectionId.Enhancements,
            isCatalogLoaded: true));
        Assert.True(ReferenceEnhancementBrowseSupport.ShouldShowEnhancementBrowseFilters(
            ReferenceSectionId.Recipes,
            isCatalogLoaded: true));
        Assert.False(ReferenceEnhancementBrowseSupport.ShouldShowEnhancementBrowseFilters(
            ReferenceSectionId.Salvage,
            isCatalogLoaded: true));
        Assert.False(ReferenceEnhancementBrowseSupport.ShouldShowEnhancementBrowseFilters(
            ReferenceSectionId.Inspirations,
            isCatalogLoaded: true));
        Assert.False(ReferenceEnhancementBrowseSupport.ShouldShowEnhancementBrowseFilters(
            ReferenceSectionId.Badges,
            isCatalogLoaded: true));
        Assert.False(ReferenceEnhancementBrowseSupport.ShouldShowEnhancementBrowseFilters(
            ReferenceSectionId.Powers,
            isCatalogLoaded: true));
    }

    [Fact]
    public void Reference_rarity_presentation_maps_proven_codes_to_bright_families()
    {
        Assert.Equal(
            ReferenceRarityPresentationFamily.Uncommon,
            ReferenceRarityPresentation.ResolveFamily("ECUncommon"));
        Assert.Equal(
            ReferenceRarityPresentationFamily.Rare,
            ReferenceRarityPresentation.ResolveFamily("ECRare"));
        Assert.Equal(
            ReferenceRarityPresentationFamily.VeryRare,
            ReferenceRarityPresentation.ResolveFamily("ECVeryRare"));

        Assert.Equal(
            ReferenceRarityPresentation.UncommonBrushKey,
            ReferenceRarityPresentation.ResolveBrushResourceKey("ECUncommon"));
        Assert.Equal(
            ReferenceRarityPresentation.RareBrushKey,
            ReferenceRarityPresentation.ResolveBrushResourceKey("ECRare"));
        Assert.Equal(
            ReferenceRarityPresentation.VeryRareBrushKey,
            ReferenceRarityPresentation.ResolveBrushResourceKey("ECVeryRare"));
    }

    [Theory]
    [InlineData("ECATO")]
    [InlineData("ECWinter")]
    [InlineData("ECPVP")]
    public void Reference_rarity_presentation_maps_auction_house_orange_identities_to_rare_family(string rarityCode)
    {
        Assert.Equal(
            ReferenceRarityPresentationFamily.Rare,
            ReferenceRarityPresentation.ResolveFamily(rarityCode));
        Assert.Equal(
            ReferenceRarityPresentation.RareBrushKey,
            ReferenceRarityPresentation.ResolveBrushResourceKey(rarityCode));
    }

    [Theory]
    [InlineData("ECSATO")]
    [InlineData("ECSWinter")]
    public void Reference_rarity_presentation_maps_auction_house_purple_identities_to_very_rare_family(
        string rarityCode)
    {
        Assert.Equal(
            ReferenceRarityPresentationFamily.VeryRare,
            ReferenceRarityPresentation.ResolveFamily(rarityCode));
        Assert.Equal(
            ReferenceRarityPresentation.VeryRareBrushKey,
            ReferenceRarityPresentation.ResolveBrushResourceKey(rarityCode));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("ECUnknown")]
    [InlineData("ECPvP")]
    public void Reference_rarity_presentation_uses_default_for_unknown_identities(string? rarityCode)
    {
        Assert.Equal(
            ReferenceRarityPresentationFamily.Default,
            ReferenceRarityPresentation.ResolveFamily(rarityCode));
        Assert.Equal(
            ReferenceRarityPresentation.DefaultBrushKey,
            ReferenceRarityPresentation.ResolveBrushResourceKey(rarityCode));
    }

    [Fact]
    public void Production_sets_map_auction_house_observed_rarity_codes_to_presentation_families()
    {
        Assert.True(_catalog.IsLoaded, _catalog.LoadFailureReason);

        var tree = ReferenceEnhancementBrowseSupport.BuildBrowseTree(_catalog);

        Assert.Equal(
            ReferenceRarityPresentationFamily.Rare,
            ReferenceRarityPresentation.ResolveFamily(FindSetNode(tree, "Blaster's Wrath")!.RarityCode));
        Assert.Equal(
            ReferenceRarityPresentationFamily.VeryRare,
            ReferenceRarityPresentation.ResolveFamily(
                FindSetNode(tree, "Superior Blaster's Wrath")!.RarityCode));
        Assert.Equal(
            ReferenceRarityPresentationFamily.Rare,
            ReferenceRarityPresentation.ResolveFamily(FindSetNode(tree, "Blistering Cold")!.RarityCode));
        Assert.Equal(
            ReferenceRarityPresentationFamily.VeryRare,
            ReferenceRarityPresentation.ResolveFamily(
                FindSetNode(tree, "Superior Blistering Cold")!.RarityCode));
        Assert.Equal(
            ReferenceRarityPresentationFamily.Rare,
            ReferenceRarityPresentation.ResolveFamily(FindSetNode(tree, "Gladiator's Strike")!.RarityCode));
        Assert.Equal(
            ReferenceRarityPresentationFamily.VeryRare,
            ReferenceRarityPresentation.ResolveFamily(FindSetNode(tree, "Apocalypse")!.RarityCode));
        Assert.Equal(
            ReferenceRarityPresentationFamily.VeryRare,
            ReferenceRarityPresentation.ResolveFamily(FindSetNode(tree, "Armageddon")!.RarityCode));
    }

    [Fact]
    public void Set_tree_nodes_carry_canonical_rarity_codes_for_presentation()
    {
        Assert.True(_catalog.IsLoaded, _catalog.LoadFailureReason);

        var tree = ReferenceEnhancementBrowseSupport.BuildBrowseTree(_catalog);

        var curare = FindSetNode(tree, "Essence of Curare");
        var positron = FindSetNode(tree, "Positron's Blast");
        var apocalypse = FindSetNode(tree, "Apocalypse");

        Assert.NotNull(curare);
        Assert.NotNull(positron);
        Assert.NotNull(apocalypse);
        Assert.Equal("ECUncommon", curare!.RarityCode);
        Assert.Equal("ECRare", positron!.RarityCode);
        Assert.Equal("ECVeryRare", apocalypse!.RarityCode);
    }

    [Fact]
    public void Rarity_filter_options_include_canonical_codes_for_proven_labels()
    {
        Assert.True(_catalog.IsLoaded, _catalog.LoadFailureReason);

        var options = ReferenceEnhancementBrowseSupport.GetRarityFilterOptions(_catalog);
        var uncommon = options.Single(option => option.Label == "Uncommon");
        var rare = options.Single(option => option.Label == "Rare");
        var veryRare = options.Single(option => option.Label == "Very Rare");
        var all = options.Single(option => option.Label == "All");

        Assert.Null(all.RarityCode);
        Assert.Equal("ECUncommon", uncommon.RarityCode);
        Assert.Equal("ECRare", rare.RarityCode);
        Assert.Equal("ECVeryRare", veryRare.RarityCode);
    }

    [Fact]
    public void Set_detail_includes_canonical_rarity_code_for_presentation()
    {
        Assert.True(_catalog.IsLoaded, _catalog.LoadFailureReason);

        var tree = ReferenceEnhancementBrowseSupport.BuildBrowseTree(_catalog);
        var setNode = FindSetNode(tree, "Armageddon");
        Assert.NotNull(setNode);

        var detail = BuildDetail(setNode, tree);
        Assert.Equal("Very Rare", detail.RarityLabel);
        Assert.Equal("ECVeryRare", detail.RarityCode);
    }

    [Fact]
    public void Enhancement_detail_resolves_composed_icon_source()
    {
        Assert.True(_catalog.IsLoaded, _catalog.LoadFailureReason);

        var compositor = new FakeEnhancementIconCompositor();
        var tree = ReferenceEnhancementBrowseSupport.BuildBrowseTree(_catalog);
        Assert.True(_catalog.TryResolve("Invention: Accuracy", out var resolution));
        var enhancementNode = ReferenceEnhancementBrowseSupport.FindNodeForEnhancementId(
            tree,
            resolution.Item.CatalogItemId);
        Assert.NotNull(enhancementNode);

        var detail = BuildDetail(enhancementNode, tree, iconCompositor: compositor);

        Assert.Equal("E_ICON_GEN_ACCURACY_01.tga", detail.IconIdentity);
        Assert.NotNull(detail.IconSource);
        Assert.Same(compositor.ResolveMarker("E_ICON_GEN_ACCURACY_01.tga", "Accuracy", EnhancementFrameClass.Invention), detail.IconSource);
        var request = Assert.Single(compositor.Requests);
        Assert.Equal("E_ICON_GEN_ACCURACY_01.tga", request.IconIdentity);
        Assert.Equal("Accuracy", request.PogBoostType);
        Assert.Equal(EnhancementFrameClass.Invention, request.FrameClass);
    }

    [Fact]
    public void Set_member_summary_and_detail_share_the_same_composed_icon_source()
    {
        Assert.True(_catalog.IsLoaded, _catalog.LoadFailureReason);

        var compositor = new FakeEnhancementIconCompositor();
        var boostMetadata = new FakeBoostMetadataProvider()
            .With("Boosts.Crafted_Positrons_Blast_A.Crafted_Positrons_Blast_A", "Damage", "Natural", "Technology", "Magic", "Mutation", "Science")
            .With("Boosts.Attuned_Positrons_Blast_A.Attuned_Positrons_Blast_A", "Damage", "Natural", "Technology", "Magic", "Mutation", "Science");
        var tree = ReferenceEnhancementBrowseSupport.BuildBrowseTree(_catalog);
        var setNode = FindSetNode(tree, "Positron's Blast");
        Assert.NotNull(setNode);

        var memberNode = setNode!.Children.First(child =>
            child.DisplayName.Contains("Accuracy/Damage", StringComparison.OrdinalIgnoreCase));
        var memberDetail = BuildDetail(memberNode, tree, iconCompositor: compositor, boostMetadata: boostMetadata);
        var setDetail = BuildDetail(setNode, tree, iconCompositor: compositor, boostMetadata: boostMetadata);
        var memberSummary = setDetail.MemberItems.Single(item =>
            item.EnhancementId == memberNode.EnhancementId);

        Assert.Equal("E_ICON_PositronsBlast.tga", memberDetail.IconIdentity);
        Assert.NotNull(setDetail.IconSource);
        Assert.NotNull(memberSummary.IconSource);
        Assert.Same(setDetail.IconSource, memberSummary.IconSource);
        Assert.Same(memberDetail.IconSource, memberSummary.IconSource);
        Assert.Contains(compositor.Requests, request =>
            request.IconIdentity == "E_ICON_PositronsBlast.tga"
            && request.PogBoostType == "Damage"
            && request.FrameClass == EnhancementFrameClass.Rare);
    }

    [Fact]
    public void Missing_composition_result_preserves_null_icon_source()
    {
        Assert.True(_catalog.IsLoaded, _catalog.LoadFailureReason);

        var compositor = new FakeEnhancementIconCompositor { ReturnNull = true };
        var tree = ReferenceEnhancementBrowseSupport.BuildBrowseTree(_catalog);
        Assert.True(_catalog.TryResolve("Invention: Accuracy", out var resolution));
        var enhancementNode = ReferenceEnhancementBrowseSupport.FindNodeForEnhancementId(
            tree,
            resolution.Item.CatalogItemId);

        var detail = BuildDetail(enhancementNode, tree, iconCompositor: compositor);

        Assert.Equal("E_ICON_GEN_ACCURACY_01.tga", detail.IconIdentity);
        Assert.Null(detail.IconSource);
    }

    [Fact]
    public void Category_and_level_nodes_do_not_receive_artwork_icon_source()
    {
        Assert.True(_catalog.IsLoaded, _catalog.LoadFailureReason);

        var compositor = new FakeEnhancementIconCompositor();
        var boostMetadata = CreatePositronsBlastBoostMetadata();
        var tree = ReferenceEnhancementBrowseSupport.BuildBrowseTree(_catalog);
        var categoryNode = tree.NodesByKey.Values.First(node =>
            node.Kind == ReferenceEnhancementBrowseNodeKind.Category);
        var setNode = FindSetNode(tree, "Positron's Blast");
        var levelNode = RequireLevelNode(tree, "Positron's Blast: Accuracy/Damage", 50);

        var categoryDetail = BuildDetail(categoryNode, tree, iconCompositor: compositor, boostMetadata: boostMetadata);
        var setDetail = BuildDetail(setNode, tree, iconCompositor: compositor, boostMetadata: boostMetadata);
        var levelDetail = BuildDetail(levelNode, tree, iconCompositor: compositor, boostMetadata: boostMetadata);

        Assert.Null(categoryDetail.IconSource);
        Assert.NotNull(setDetail.IconSource);
        Assert.All(categoryDetail.SetItems, item => Assert.Null(item.IconSource));
        Assert.Null(levelNode!.IconIdentity);
        Assert.NotNull(levelDetail.IconSource);
    }

    [Fact]
    public void Set_tree_node_receives_same_composed_icon_as_member_rows()
    {
        Assert.True(_catalog.IsLoaded, _catalog.LoadFailureReason);

        var compositor = new FakeEnhancementIconCompositor();
        var boostMetadata = CreatePositronsBlastBoostMetadata();
        var tree = ReferenceEnhancementBrowseSupport.BuildBrowseTree(_catalog);
        var setNode = FindSetNode(tree, "Positron's Blast");
        Assert.NotNull(setNode);

        var memberNode = setNode!.Children.First(child =>
            child.DisplayName.Contains("Accuracy/Damage", StringComparison.OrdinalIgnoreCase));
        var setViewModel = new ReferenceEnhancementTreeNodeViewModel(setNode, _catalog, compositor, boostMetadata);
        var memberViewModel = new ReferenceEnhancementTreeNodeViewModel(memberNode, _catalog, compositor, boostMetadata);

        Assert.NotNull(setViewModel.IconSource);
        Assert.NotNull(memberViewModel.IconSource);
        Assert.Same(setViewModel.IconSource, memberViewModel.IconSource);
    }

    [Fact]
    public void Absolute_Amazement_members_share_set_artwork_composition_request()
    {
        Assert.True(_catalog.IsLoaded, _catalog.LoadFailureReason);
        Assert.True(_catalog.TryGetEnhancementSetById("SET-00181", out var set));

        var boostMetadata = CreateAbsoluteAmazementBoostMetadata();
        var memberNames =
            new[]
            {
                "Absolute Amazement: Chance for To Hit Debuff",
                "Absolute Amazement: Recharge/Accuracy",
                "Absolute Amazement: Stun Duration",
                "Absolute Amazement: Stun Duration/Endurance Reduction",
                "Absolute Amazement: Stun Duration/Recharge",
                "Absolute Amazement: Stun Duration/Recharge/Accuracy"
            };

        var requests = memberNames
            .Select(name =>
            {
                Assert.True(_catalog.TryResolve(name, out var resolution));
                return ReferenceEnhancementBrowseSupport.TryBuildCompositionRequest(
                    _catalog,
                    resolution.Item,
                    set,
                    boostMetadata);
            })
            .ToArray();

        Assert.All(requests, request => Assert.NotNull(request));
        var canonical = requests[0]!;
        Assert.All(requests, request =>
        {
            Assert.Equal("E_ICON_AbsoluteAmazement.tga", request!.IconIdentity);
            Assert.Equal("Stun", request.PogBoostType);
            Assert.Equal(EnhancementFrameClass.Superior, request.FrameClass);
            Assert.Equal(canonical.IconIdentity, request.IconIdentity);
            Assert.Equal(canonical.PogBoostType, request.PogBoostType);
            Assert.Equal(canonical.FrameClass, request.FrameClass);
        });
    }

    [Theory]
    [InlineData("SET-00181", "Absolute Amazement")]
    [InlineData("SET-00051", "Ragnarok")]
    [InlineData("SET-00176", "Fortunata Hypnosis")]
    [InlineData("SET-00166", "Gravitational Anchor")]
    [InlineData("SET-00011", "Hecatomb")]
    [InlineData("SET-00147", "Coercive Persuasion")]
    [InlineData("SET-00158", "Unbreakable Constraint")]
    public void Very_rare_sets_with_superior_presentation_share_superior_frame_artwork(
        string setId,
        string setName)
    {
        Assert.True(_catalog.IsLoaded, _catalog.LoadFailureReason);
        Assert.True(_catalog.TryGetEnhancementSetById(setId, out var set));

        var boostMetadata = CreateSetMemberBoostMetadata(
            _catalog,
            set,
            "Stun",
            "Damage",
            "Hold",
            "Immobilize",
            "Confuse",
            "Natural",
            "Technology",
            "Magic",
            "Mutation",
            "Science");
        var tree = ReferenceEnhancementBrowseSupport.BuildBrowseTree(_catalog);
        var setNode = FindSetNode(tree, setName);
        Assert.NotNull(setNode);

        var memberRequests = setNode!.Children
            .Select(child =>
            {
                Assert.True(_catalog.TryGetById(child.EnhancementId!, out var item));
                return ReferenceEnhancementBrowseSupport.TryBuildCompositionRequest(_catalog, item, set, boostMetadata);
            })
            .ToArray();

        Assert.All(memberRequests, request => Assert.NotNull(request));
        var canonical = memberRequests[0]!;
        Assert.Equal(EnhancementFrameClass.Superior, canonical.FrameClass);
        Assert.All(memberRequests, request =>
        {
            Assert.Equal(canonical.IconIdentity, request!.IconIdentity);
            Assert.Equal(canonical.PogBoostType, request.PogBoostType);
            Assert.Equal(canonical.FrameClass, request.FrameClass);
        });

        var representative = EnhancementIconCompositionSupport.TryResolveSetArtworkRepresentativeMember(_catalog, set);
        Assert.NotNull(representative);
        var setRowRequest = ReferenceEnhancementBrowseSupport.TryBuildCompositionRequest(
            _catalog,
            representative,
            set,
            boostMetadata);
        Assert.NotNull(setRowRequest);
        Assert.Equal(canonical.FrameClass, setRowRequest.FrameClass);
        Assert.Equal(canonical.IconIdentity, setRowRequest.IconIdentity);
    }

    [Theory]
    [InlineData("Analyze Weakness")]
    [InlineData("Razzle Dazzle")]
    public void Ordinary_rare_sets_keep_rare_frame_class_for_shared_artwork(string setName)
    {
        Assert.True(_catalog.IsLoaded, _catalog.LoadFailureReason);

        var setNode = FindSetNode(ReferenceEnhancementBrowseSupport.BuildBrowseTree(_catalog), setName);
        Assert.NotNull(setNode);
        Assert.True(_catalog.TryGetEnhancementSetById(setNode!.SetId!, out var set));
        Assert.Equal("ECRare", set.RarityCode);

        var boostMetadata = CreateSetMemberBoostMetadata(
            _catalog,
            set,
            "Accuracy",
            "Buff_ToHit",
            "Natural",
            "Technology",
            "Magic",
            "Mutation",
            "Science");
        var requests = setNode.Children
            .Select(child =>
            {
                Assert.True(_catalog.TryGetById(child.EnhancementId!, out var item));
                return ReferenceEnhancementBrowseSupport.TryBuildCompositionRequest(_catalog, item, set, boostMetadata);
            })
            .Where(request => request is not null)
            .Cast<EnhancementIconCompositionRequest>()
            .ToArray();

        Assert.NotEmpty(requests);
        Assert.All(requests, request => Assert.Equal(EnhancementFrameClass.Rare, request.FrameClass));
    }

    [Fact]
    public void Absolute_Amazement_control_member_uses_superior_frame_like_pre_910eeb2()
    {
        Assert.True(_catalog.IsLoaded, _catalog.LoadFailureReason);
        Assert.True(_catalog.TryGetEnhancementSetById("SET-00181", out var set));
        Assert.True(_catalog.TryGetById("ENH-00705", out var controlMember));
        Assert.True(_catalog.TryGetById("ENH-01279", out var siblingMember));

        var boostMetadata = CreateAbsoluteAmazementBoostMetadata();
        var controlRequest = ReferenceEnhancementBrowseSupport.TryBuildCompositionRequest(
            _catalog,
            controlMember,
            set,
            boostMetadata);
        var siblingRequest = ReferenceEnhancementBrowseSupport.TryBuildCompositionRequest(
            _catalog,
            siblingMember,
            set,
            boostMetadata);

        Assert.NotNull(controlRequest);
        Assert.NotNull(siblingRequest);
        Assert.Equal(EnhancementFrameClass.Superior, controlRequest.FrameClass);
        Assert.Equal(controlRequest.FrameClass, siblingRequest.FrameClass);
        Assert.True(
            EnhancementFrameIdentity.TryResolveHalves(EnhancementFrameClass.Superior, out var leftId, out _));
        Assert.Equal("e_orgin_superior_l.tga", leftId);
    }

    [Fact]
    public void Representative_control_sets_keep_shared_set_artwork_composition()
    {
        Assert.True(_catalog.IsLoaded, _catalog.LoadFailureReason);

        foreach (var (setName, boostsAllowed) in new (string, string[])[]
                 {
                     ("Analyze Weakness", ["Accuracy", "Natural", "Technology", "Magic", "Mutation", "Science"]),
                     ("Razzle Dazzle", ["Buff_ToHit", "Natural", "Technology", "Magic", "Mutation", "Science"]),
                     ("Rope A Dope", ["Hold", "Natural", "Technology", "Magic", "Mutation", "Science"]),
                     ("Positron's Blast", ["Damage", "Natural", "Technology", "Magic", "Mutation", "Science"])
                 })
        {
            var setNode = FindSetNode(ReferenceEnhancementBrowseSupport.BuildBrowseTree(_catalog), setName);
            Assert.NotNull(setNode);
            Assert.True(_catalog.TryGetEnhancementSetById(setNode!.SetId!, out var set));

            var boostMetadata = CreateSetMemberBoostMetadata(_catalog, set, boostsAllowed);
            var requests = setNode.Children
                .Select(child =>
                {
                    Assert.True(_catalog.TryGetById(child.EnhancementId!, out var item));
                    return ReferenceEnhancementBrowseSupport.TryBuildCompositionRequest(_catalog, item, set, boostMetadata);
                })
                .Where(request => request is not null)
                .Cast<EnhancementIconCompositionRequest>()
                .ToArray();

            Assert.NotEmpty(requests);
            var canonical = requests[0];
            Assert.All(requests, request =>
            {
                Assert.Equal(canonical.IconIdentity, request.IconIdentity);
                Assert.Equal(canonical.PogBoostType, request.PogBoostType);
                Assert.Equal(canonical.FrameClass, request.FrameClass);
            });
        }
    }

    [Fact]
    public void Tree_enhancement_node_receives_composed_icon_source()
    {
        Assert.True(_catalog.IsLoaded, _catalog.LoadFailureReason);

        var compositor = new FakeEnhancementIconCompositor();
        var tree = ReferenceEnhancementBrowseSupport.BuildBrowseTree(_catalog);
        Assert.True(_catalog.TryResolve("Invention: Accuracy", out var resolution));
        var browseNode = ReferenceEnhancementBrowseSupport.FindNodeForEnhancementId(
            tree,
            resolution.Item.CatalogItemId);
        Assert.NotNull(browseNode);

        var viewModel = new ReferenceEnhancementTreeNodeViewModel(
            browseNode!,
            _catalog,
            compositor);

        Assert.Equal("E_ICON_GEN_ACCURACY_01.tga", viewModel.IconIdentity);
        Assert.NotNull(viewModel.IconSource);
        Assert.All(viewModel.Children, child => Assert.Null(child.IconSource));
    }

    [Fact]
    public void Boosted_level_detail_uses_same_enhancement_icon_source_at_level_55()
    {
        Assert.True(_catalog.IsLoaded, _catalog.LoadFailureReason);

        var compositor = new FakeEnhancementIconCompositor();
        var boostMetadata = new FakeBoostMetadataProvider()
            .With("Boosts.Crafted_Soulbound_Allegiance_E.Crafted_Soulbound_Allegiance_E", "Damage", "Natural", "Technology", "Magic", "Mutation", "Science")
            .With("Boosts.Superior_Attuned_Soulbound_Allegiance_E.Superior_Attuned_Soulbound_Allegiance_E", "Damage", "Natural", "Technology", "Magic", "Mutation", "Science");
        var tree = ReferenceEnhancementBrowseSupport.BuildBrowseTree(_catalog);
        var levelNode = RequireLevelNode(tree, "Soulbound Allegiance: Damage/Endurance Reduction", 55);
        var enhancementNode = ReferenceEnhancementBrowseSupport.FindNodeForEnhancementId(
            tree,
            levelNode!.EnhancementId!);

        var levelDetail = BuildDetail(levelNode, tree, iconCompositor: compositor, boostMetadata: boostMetadata);
        var enhancementDetail = BuildDetail(enhancementNode, tree, presentationLevel: 55, iconCompositor: compositor, boostMetadata: boostMetadata);

        Assert.Equal("E_ICON_SoulboundAllegiance.tga", levelDetail.IconIdentity);
        Assert.NotNull(levelDetail.IconSource);
        Assert.Same(enhancementDetail.IconSource, levelDetail.IconSource);
    }

    [Fact]
    public void Soulbound_edge_uses_concrete_variant_metadata_for_frame_class()
    {
        Assert.True(_catalog.IsLoaded, _catalog.LoadFailureReason);
        Assert.True(_catalog.TryResolve("Soulbound Allegiance: Damage/Endurance Reduction", out var resolution));

        var compositor = new FakeEnhancementIconCompositor();
        var boostMetadata = new FakeBoostMetadataProvider()
            .With("Boosts.Crafted_Soulbound_Allegiance_E.Crafted_Soulbound_Allegiance_E", "Damage", "Natural", "Technology", "Magic", "Mutation", "Science");
        var tree = ReferenceEnhancementBrowseSupport.BuildBrowseTree(_catalog);
        var enhancementNode = ReferenceEnhancementBrowseSupport.FindNodeForEnhancementId(
            tree,
            resolution.Item.CatalogItemId);
        Assert.NotNull(enhancementNode);

        var detail = BuildDetail(enhancementNode, tree, iconCompositor: compositor, boostMetadata: boostMetadata);
        var request = Assert.Single(compositor.Requests);

        Assert.Equal("Regular", resolution.Item.Variant);
        Assert.Equal(EnhancementFrameClass.Superior, request.FrameClass);
        Assert.Equal("Damage", request.PogBoostType);
        Assert.NotNull(detail.IconSource);
    }

    [Fact]
    public void Attuned_and_superior_attuned_mappings_reach_expected_frame_classes()
    {
        Assert.True(_catalog.IsLoaded, _catalog.LoadFailureReason);

        var compositor = new FakeEnhancementIconCompositor();
        var boostMetadata = new FakeBoostMetadataProvider()
            .With("Boosts.Attuned_Blistering_Cold_C.Attuned_Blistering_Cold_C", "Damage", "Natural", "Technology", "Magic", "Mutation", "Science")
            .With("Boosts.Superior_Attuned_Blistering_Cold_A.Superior_Attuned_Blistering_Cold_A", "Damage", "Natural", "Technology", "Magic", "Mutation", "Science");
        var tree = ReferenceEnhancementBrowseSupport.BuildBrowseTree(_catalog);

        var winterNode = ReferenceEnhancementBrowseSupport.FindNodeForEnhancementId(
            tree,
            tree.NodesByKey.Values.First(node =>
                node.DisplayName.Equals("Blistering Cold: Accuracy/Damage/Recharge", StringComparison.OrdinalIgnoreCase)).EnhancementId!);
        var superiorWinterNode = ReferenceEnhancementBrowseSupport.FindNodeForEnhancementId(
            tree,
            tree.NodesByKey.Values.First(node =>
                node.DisplayName.Equals("Superior Blistering Cold: Accuracy/Damage", StringComparison.OrdinalIgnoreCase)).EnhancementId!);

        BuildDetail(winterNode, tree, iconCompositor: compositor, boostMetadata: boostMetadata);
        BuildDetail(superiorWinterNode, tree, iconCompositor: compositor, boostMetadata: boostMetadata);

        Assert.Contains(compositor.Requests, request => request.FrameClass == EnhancementFrameClass.Attuned);
        Assert.Contains(compositor.Requests, request => request.FrameClass == EnhancementFrameClass.SuperiorAttuned);
    }

    private sealed class FakeEnhancementIconCompositor : IEnhancementIconCompositor
    {
        public List<EnhancementIconCompositionRequest> Requests { get; } = [];

        public bool ReturnNull { get; init; }

        public ImageSource? TryCompose(EnhancementIconCompositionRequest request)
        {
            Requests.Add(request);
            if (ReturnNull)
            {
                return null;
            }

            return ResolveMarker(request.IconIdentity, request.PogBoostType, request.FrameClass);
        }

        public ImageSource ResolveMarker(string iconIdentity, string pogBoostType, EnhancementFrameClass frameClass)
        {
            var key = $"{iconIdentity}|{pogBoostType}|{frameClass}";
            if (_markers.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var pixels = new byte[] { 40, 80, 120, 200 };
            var source = BitmapSource.Create(
                1,
                1,
                96,
                96,
                PixelFormats.Bgra32,
                null,
                pixels,
                4);
            source.Freeze();
            _markers[key] = source;
            return source;
        }

        private readonly Dictionary<string, ImageSource> _markers = new(StringComparer.OrdinalIgnoreCase);
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

    private static FakeBoostMetadataProvider CreatePositronsBlastBoostMetadata() =>
        new FakeBoostMetadataProvider()
            .With("Boosts.Crafted_Positrons_Blast_A.Crafted_Positrons_Blast_A", "Damage", "Natural", "Technology", "Magic", "Mutation", "Science")
            .With("Boosts.Attuned_Positrons_Blast_A.Attuned_Positrons_Blast_A", "Damage", "Natural", "Technology", "Magic", "Mutation", "Science");

    private FakeBoostMetadataProvider CreateAbsoluteAmazementBoostMetadata()
    {
        Assert.True(_catalog.TryGetEnhancementSetById("SET-00181", out var set));
        return CreateSetMemberBoostMetadata(
            _catalog,
            set,
            "Stun",
            "Natural",
            "Technology",
            "Magic",
            "Mutation",
            "Science");
    }

    private static FakeBoostMetadataProvider CreateSetMemberBoostMetadata(
        IItemReferenceCatalog catalog,
        EnhancementSetReferenceRecord set,
        params string[] boostsAllowed)
    {
        var provider = new FakeBoostMetadataProvider();
        foreach (var item in catalog.GetEnhancements(ReferenceCatalogQueryScope.CurrentHomecoming)
                     .Where(member => string.Equals(member.EnhancementSetId, set.CatalogItemId, StringComparison.Ordinal)))
        {
            foreach (var variant in item.SourceVariants)
            {
                provider.With(variant.HomecomingSourceId, boostsAllowed);
            }
        }

        return provider;
    }

    private static ReferenceEnhancementBrowseNode? FindSetNode(
        ReferenceEnhancementBrowseTree tree,
        string setName) =>
        tree.NodesByKey.Values.FirstOrDefault(node =>
            node.Kind == ReferenceEnhancementBrowseNodeKind.Set
            && node.DisplayName.Equals(setName, StringComparison.OrdinalIgnoreCase));
}
