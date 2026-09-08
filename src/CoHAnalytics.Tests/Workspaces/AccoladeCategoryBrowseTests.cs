using CoHAnalytics.ReferenceData;
using CoHAnalytics.ViewModels.Workspaces;

namespace CoHAnalytics.Tests.Workspaces;

public sealed class AccoladeCategoryBrowseTests
{
    private static readonly IItemReferenceCatalog ProductionCatalog =
        ItemReferenceCatalogFactory.LoadEmbeddedProduction();

    private static readonly string[] ExpectedCategoryNames =
    [
        "Task Forces / Strike Forces / Trials",
        "Exploration",
        "Day Jobs",
        "Defeat / Hunting",
        "Missions / Story",
        "PvP",
        "Events",
        "General"
    ];

    private static readonly int[] ExpectedCategoryCounts = [9, 60, 19, 3, 28, 1, 2, 23];

    [Fact]
    public void Accolades_root_contains_expected_non_empty_categories()
    {
        var tree = ReferenceBadgeBrowseSupport.BuildBrowseTree(ProductionCatalog);
        var root = FindAccoladesRoot(tree);

        Assert.Equal(ExpectedCategoryNames.Length, root.Children.Count);
        Assert.Equal(ExpectedCategoryNames, root.Children.Select(child => child.DisplayName).ToArray());
        Assert.All(
            root.Children,
            child => Assert.Equal(ReferenceEnhancementBrowseNodeKind.BadgeAccoladeCategory, child.Kind));
    }

    [Fact]
    public void Accolade_category_counts_total_145()
    {
        var tree = ReferenceBadgeBrowseSupport.BuildBrowseTree(ProductionCatalog);
        var root = FindAccoladesRoot(tree);

        Assert.Equal(ExpectedCategoryCounts, root.Children.Select(child => child.Children.Count).ToArray());
        Assert.Equal(145, root.Children.Sum(child => child.Children.Count));
    }

    [Fact]
    public void Each_accolade_appears_exactly_once_in_accolades_tree()
    {
        var tree = ReferenceBadgeBrowseSupport.BuildBrowseTree(ProductionCatalog);
        var accoladeNodes = tree.NodesByKey.Values
            .Where(node => node.Kind == ReferenceEnhancementBrowseNodeKind.BadgeAccolade)
            .ToArray();

        Assert.Equal(145, accoladeNodes.Length);
        Assert.Equal(145, accoladeNodes.Select(node => node.BadgeId).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void History_category_is_not_shown_when_empty()
    {
        var tree = ReferenceBadgeBrowseSupport.BuildBrowseTree(ProductionCatalog);

        Assert.DoesNotContain(
            tree.NodesByKey.Values,
            node => node.Kind == ReferenceEnhancementBrowseNodeKind.BadgeAccoladeCategory
                && string.Equals(node.DisplayName, "History", StringComparison.Ordinal));
    }

    [Fact]
    public void Accolades_are_alphabetical_within_each_category()
    {
        var tree = ReferenceBadgeBrowseSupport.BuildBrowseTree(ProductionCatalog);
        var root = FindAccoladesRoot(tree);

        foreach (var category in root.Children)
        {
            var names = category.Children.Select(child => child.DisplayName).ToArray();
            var sorted = names.OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(name => name, StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(sorted, names);
        }
    }

    [Fact]
    public void Atlas_tour_guide_appears_under_accolades_exploration()
    {
        var tree = ReferenceBadgeBrowseSupport.BuildBrowseTree(ProductionCatalog);
        var explorationCategory = FindAccoladesRoot(tree).Children.Single(category =>
            string.Equals(category.DisplayName, "Exploration", StringComparison.Ordinal));

        var atlasTourGuide = explorationCategory.Children.Single(child => child.BadgeId == "BAD-01917");
        Assert.Equal("Atlas Tour Guide", atlasTourGuide.DisplayName);
        Assert.Equal(
            ReferenceBadgeBrowseSupport.CreateAccoladeCategoryNodeKey("exploration"),
            atlasTourGuide.ParentNodeKey);
    }

    [Fact]
    public void Atlas_medallion_appears_under_general()
    {
        var tree = ReferenceBadgeBrowseSupport.BuildBrowseTree(ProductionCatalog);
        var generalCategory = FindAccoladesRoot(tree).Children.Single(category =>
            string.Equals(category.DisplayName, "General", StringComparison.Ordinal));

        Assert.Contains(
            generalCategory.Children,
            child => child.BadgeId == "BAD-01927"
                && child.DisplayName == "Received the Atlas Medallion / Atlas Shrugged");
    }

    [Fact]
    public void Alchemist_appears_under_day_jobs_with_requirements_unchanged()
    {
        var tree = ReferenceBadgeBrowseSupport.BuildBrowseTree(ProductionCatalog);
        var dayJobsCategory = FindAccoladesRoot(tree).Children.Single(category =>
            string.Equals(category.DisplayName, "Day Jobs", StringComparison.Ordinal));

        var alchemistNode = dayJobsCategory.Children.Single(child => child.BadgeId == "BAD-02094");
        var detail = ReferenceBadgeBrowseSupport.BuildDetail(
            ProductionCatalog,
            alchemistNode,
            acquiredBadgeIds: null,
            installedGameAssetProvider: null);

        Assert.Equal("Alchemist", alchemistNode.DisplayName);
        Assert.Equal(ReferenceEnhancementDetailKind.Accolade, detail.Kind);
        Assert.Equal(2, detail.AccoladeRequirements.Count);
        Assert.Single(detail.AccoladeRequirements, item => item.BadgeId == "BAD-02104");
    }

    private static ReferenceEnhancementBrowseNode FindAccoladesRoot(ReferenceEnhancementBrowseTree tree) =>
        tree.RootNodes.Single(node => node.Kind == ReferenceEnhancementBrowseNodeKind.BadgeAccoladesRoot);
}
