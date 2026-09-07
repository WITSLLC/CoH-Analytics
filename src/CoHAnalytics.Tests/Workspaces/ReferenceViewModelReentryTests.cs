using CoHAnalytics.Orchestration.Contracts;
using CoHAnalytics.ReferenceData;
using CoHAnalytics.Tests.Services;
using CoHAnalytics.ViewModels.Workspaces;

namespace CoHAnalytics.Tests.Workspaces;

public sealed class ReferenceViewModelReentryTests
{
    private static readonly IItemReferenceCatalog ProductionCatalog =
        ItemReferenceCatalogFactory.LoadEmbeddedProduction();

    [Fact]
    public void Reentry_hydrates_badges_tree_after_workspace_deactivation()
    {
        using var viewModel = CreateViewModel();
        viewModel.SetWorkspaceActive(true);
        viewModel.SelectSectionChipCommand.Execute(ReferenceSectionId.Badges);
        var initialRootCount = viewModel.TreeRootNodes.Count;
        Assert.NotEmpty(viewModel.TreeRootNodes);

        viewModel.SetWorkspaceActive(false);
        viewModel.TreeRootNodes.Clear();
        viewModel.SetWorkspaceActive(true);

        Assert.Equal(ReferenceSectionId.Badges, viewModel.ActiveSection);
        Assert.Equal(initialRootCount, viewModel.TreeRootNodes.Count);
        Assert.Contains(
            viewModel.TreeRootNodes,
            node => node.NodeKey == ReferenceBadgeBrowseSupport.ExplorationRootNodeKey);
    }

    [Fact]
    public void Reentry_hydrates_recipes_tree_after_workspace_deactivation()
    {
        using var viewModel = CreateViewModel();
        viewModel.SetWorkspaceActive(true);
        viewModel.SelectSectionChipCommand.Execute(ReferenceSectionId.Recipes);
        var initialRootCount = viewModel.TreeRootNodes.Count;
        Assert.NotEmpty(viewModel.TreeRootNodes);

        viewModel.SetWorkspaceActive(false);
        viewModel.TreeRootNodes.Clear();
        viewModel.SetWorkspaceActive(true);

        Assert.Equal(ReferenceSectionId.Recipes, viewModel.ActiveSection);
        Assert.Equal(initialRootCount, viewModel.TreeRootNodes.Count);
        Assert.Contains(
            viewModel.TreeRootNodes,
            node => node.NodeKey == ReferenceRecipeBrowseSupport.SetsRootNodeKey);
    }

    [Fact]
    public void Reentry_hydrates_enhancements_tree_after_workspace_deactivation()
    {
        using var viewModel = CreateViewModel();
        viewModel.SetWorkspaceActive(true);
        viewModel.SelectSectionChipCommand.Execute(ReferenceSectionId.Enhancements);
        var initialRootCount = viewModel.TreeRootNodes.Count;
        Assert.NotEmpty(viewModel.TreeRootNodes);

        viewModel.SetWorkspaceActive(false);
        viewModel.TreeRootNodes.Clear();
        viewModel.SetWorkspaceActive(true);

        Assert.Equal(ReferenceSectionId.Enhancements, viewModel.ActiveSection);
        Assert.Equal(initialRootCount, viewModel.TreeRootNodes.Count);
        Assert.Contains(
            viewModel.TreeRootNodes,
            node => node.NodeKey == ReferenceEnhancementBrowseSupport.SetsRootNodeKey);
    }

    [Fact]
    public void Filter_changes_while_workspace_inactive_do_not_rebuild_tree()
    {
        using var viewModel = CreateViewModel();
        viewModel.SetWorkspaceActive(true);
        viewModel.SelectSectionChipCommand.Execute(ReferenceSectionId.Enhancements);
        var rootReference = viewModel.TreeRootNodes[0];
        var initialCount = viewModel.TreeRootNodes.Count;

        viewModel.SetWorkspaceActive(false);
        viewModel.EnhancementMinLevel = viewModel.CatalogMaximumLevel;
        viewModel.EnhancementMaxLevel = viewModel.CatalogMinimumLevel;
        viewModel.SelectedRarityFilter = viewModel.RarityFilterOptions.LastOrDefault();

        Assert.Same(rootReference, viewModel.TreeRootNodes[0]);
        Assert.Equal(initialCount, viewModel.TreeRootNodes.Count);
    }

    [Fact]
    public void Reentry_restores_valid_selection_and_detail()
    {
        using var viewModel = CreateViewModel();
        viewModel.SetWorkspaceActive(true);
        viewModel.SelectSectionChipCommand.Execute(ReferenceSectionId.Badges);
        var explorationRoot = viewModel.TreeRootNodes
            .Single(node => node.NodeKey == ReferenceBadgeBrowseSupport.ExplorationRootNodeKey);
        var zoneNode = explorationRoot.Children.First();
        var badgeNode = zoneNode.Children.First(node => node.Kind == ReferenceEnhancementBrowseNodeKind.Badge);
        viewModel.SelectedNode = badgeNode;
        Assert.Equal(ReferenceEnhancementDetailKind.Badge, viewModel.Detail.Kind);

        viewModel.SetWorkspaceActive(false);
        viewModel.SetWorkspaceActive(true);

        Assert.NotNull(viewModel.SelectedNode);
        Assert.Equal(badgeNode.NodeKey, viewModel.SelectedNode.NodeKey);
        Assert.Equal(ReferenceEnhancementDetailKind.Badge, viewModel.Detail.Kind);
        Assert.NotEmpty(viewModel.TreeRootNodes);
    }

    [Fact]
    public void Reentry_after_empty_tree_restores_selection_when_still_valid()
    {
        using var viewModel = CreateViewModel();
        viewModel.SetWorkspaceActive(true);
        viewModel.SelectSectionChipCommand.Execute(ReferenceSectionId.Recipes);
        var recipeSet = viewModel.TreeRootNodes
            .Single(node => node.NodeKey == ReferenceRecipeBrowseSupport.SetsRootNodeKey)
            .Children
            .First(child => child.Kind == ReferenceEnhancementBrowseNodeKind.Category)
            .Children
            .First(child => child.Kind == ReferenceEnhancementBrowseNodeKind.Set);
        viewModel.SelectedNode = recipeSet;
        Assert.Equal(ReferenceEnhancementDetailKind.Set, viewModel.Detail.Kind);

        viewModel.SetWorkspaceActive(false);
        viewModel.TreeRootNodes.Clear();
        viewModel.SetWorkspaceActive(true);

        Assert.NotEmpty(viewModel.TreeRootNodes);
        Assert.NotNull(viewModel.SelectedNode);
        Assert.Equal(recipeSet.NodeKey, viewModel.SelectedNode.NodeKey);
        Assert.Equal(ReferenceEnhancementDetailKind.Set, viewModel.Detail.Kind);
    }

    [Fact]
    public void Reentry_clears_cross_section_detail_when_tree_was_rebuilt()
    {
        using var viewModel = CreateViewModel();
        viewModel.SetWorkspaceActive(true);
        viewModel.SelectSectionChipCommand.Execute(ReferenceSectionId.Badges);
        var explorationRoot = viewModel.TreeRootNodes
            .Single(node => node.NodeKey == ReferenceBadgeBrowseSupport.ExplorationRootNodeKey);
        var zoneNode = explorationRoot.Children.First();
        var badgeNode = zoneNode.Children.First(node => node.Kind == ReferenceEnhancementBrowseNodeKind.Badge);
        viewModel.SelectedNode = badgeNode;
        Assert.Equal(ReferenceEnhancementDetailKind.Badge, viewModel.Detail.Kind);

        viewModel.SetWorkspaceActive(false);
        viewModel.TreeRootNodes.Clear();
        viewModel.SelectSectionChipCommand.Execute(ReferenceSectionId.Recipes);
        viewModel.SetWorkspaceActive(true);

        Assert.NotEmpty(viewModel.TreeRootNodes);
        Assert.Null(viewModel.SelectedNode);
        Assert.Equal(ReferenceEnhancementDetailKind.Empty, viewModel.Detail.Kind);
        Assert.Equal("Recipes", viewModel.Detail.Title);
    }

    [Fact]
    public void Repeated_reentry_cycles_keep_tree_populated_for_all_sections()
    {
        using var viewModel = CreateViewModel();
        viewModel.SetWorkspaceActive(true);

        foreach (var section in new[]
                 {
                     ReferenceSectionId.Badges,
                     ReferenceSectionId.Recipes,
                     ReferenceSectionId.Enhancements
                 })
        {
            viewModel.SelectSectionChipCommand.Execute(section);
            Assert.NotEmpty(viewModel.TreeRootNodes);

            for (var cycle = 0; cycle < 2; cycle++)
            {
                viewModel.SetWorkspaceActive(false);
                viewModel.SetWorkspaceActive(true);
                Assert.Equal(section, viewModel.ActiveSection);
                Assert.NotEmpty(viewModel.TreeRootNodes);
            }
        }
    }

    private static ReferenceViewModel CreateViewModel() =>
        new(
            new TestGameplaySessionContextSupport.FakeApplicationOrchestrator(),
            new FakeGameRuntimeService(),
            ProductionCatalog);
}
