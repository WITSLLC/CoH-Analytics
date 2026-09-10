using CoHAnalytics.Homecoming;
using CoHAnalytics.Orchestration.Contracts;
using CoHAnalytics.ReferenceData;
using CoHAnalytics.Tests.Services;
using CoHAnalytics.ViewModels.Workspaces;

namespace CoHAnalytics.Tests.Workspaces;

public sealed class ReferenceViewModelLevelFilterTests
{
    private static readonly IItemReferenceCatalog ProductionCatalog =
        ItemReferenceCatalogFactory.LoadEmbeddedProduction();

    [Fact]
    public void Initial_enhancement_view_uses_general_defaults()
    {
        using var viewModel = CreateViewModel();

        AssertGeneralDefaults(viewModel);
        AssertLevelFilterState(viewModel);
    }

    [Fact]
    public void Initial_recipe_view_uses_general_defaults()
    {
        using var viewModel = CreateViewModel();
        viewModel.SelectSectionChipCommand.Execute(ReferenceSectionId.Recipes);

        AssertGeneralDefaults(viewModel);
        AssertLevelFilterState(viewModel);
    }

    [Fact]
    public void Common_invention_enhancement_branch_uses_invention_defaults()
    {
        using var viewModel = CreateViewModel();
        viewModel.SetWorkspaceActive(true);
        var commonRoot = viewModel.TreeRootNodes
            .Single(node => node.NodeKey == ReferenceEnhancementBrowseSupport.CommonRootNodeKey);
        viewModel.SelectedNode = commonRoot;

        AssertInventionDefaults(viewModel);
        AssertLevelFilterState(viewModel);
    }

    [Fact]
    public void Common_invention_recipe_branch_uses_invention_defaults()
    {
        using var viewModel = CreateViewModel();
        viewModel.SetWorkspaceActive(true);
        viewModel.SelectSectionChipCommand.Execute(ReferenceSectionId.Recipes);
        var commonRoot = viewModel.TreeRootNodes
            .Single(node => node.NodeKey == ReferenceRecipeBrowseSupport.CommonRootNodeKey);
        viewModel.SelectedNode = commonRoot;

        AssertInventionDefaults(viewModel);
        AssertLevelFilterState(viewModel);
    }

    [Fact]
    public void Badges_to_recipes_restores_valid_level_filter_state()
    {
        using var viewModel = CreateViewModel();
        viewModel.SetWorkspaceActive(true);

        viewModel.SelectSectionChipCommand.Execute(ReferenceSectionId.Badges);
        viewModel.SelectSectionChipCommand.Execute(ReferenceSectionId.Recipes);

        AssertGeneralDefaults(viewModel);
        AssertLevelFilterState(viewModel);
    }

    [Fact]
    public void Badges_to_enhancements_restores_valid_level_filter_state()
    {
        using var viewModel = CreateViewModel();
        viewModel.SetWorkspaceActive(true);

        viewModel.SelectSectionChipCommand.Execute(ReferenceSectionId.Badges);
        viewModel.SelectSectionChipCommand.Execute(ReferenceSectionId.Enhancements);

        AssertGeneralDefaults(viewModel);
        AssertLevelFilterState(viewModel);
    }

    [Fact]
    public void Repeated_section_switching_never_leaves_blank_level_filters()
    {
        using var viewModel = CreateViewModel();
        viewModel.SetWorkspaceActive(true);

        foreach (var section in new[]
                 {
                     ReferenceSectionId.Enhancements,
                     ReferenceSectionId.Recipes,
                     ReferenceSectionId.Badges,
                     ReferenceSectionId.Recipes,
                     ReferenceSectionId.Enhancements,
                     ReferenceSectionId.Badges
                 })
        {
            viewModel.SelectSectionChipCommand.Execute(section);
            if (section is ReferenceSectionId.Enhancements or ReferenceSectionId.Recipes)
            {
                AssertLevelFilterState(viewModel);
            }
        }
    }

    [Fact]
    public void User_selected_range_is_preserved_when_returning_to_same_section_and_scope()
    {
        using var viewModel = CreateViewModel();
        viewModel.SetWorkspaceActive(true);

        viewModel.EnhancementMinLevel = 20;
        viewModel.EnhancementMaxLevel = 35;

        viewModel.SelectSectionChipCommand.Execute(ReferenceSectionId.Badges);
        viewModel.SelectSectionChipCommand.Execute(ReferenceSectionId.Enhancements);

        Assert.Equal(20, viewModel.EnhancementMinLevel);
        Assert.Equal(35, viewModel.EnhancementMaxLevel);
        AssertLevelFilterState(viewModel);
    }

    [Fact]
    public void Level_filter_options_remain_populated_after_round_trip_with_unchanged_selected_values()
    {
        using var viewModel = CreateViewModel();
        viewModel.SetWorkspaceActive(true);

        Assert.Equal(50, viewModel.EnhancementMaxLevel);

        viewModel.SelectSectionChipCommand.Execute(ReferenceSectionId.Badges);
        viewModel.SelectSectionChipCommand.Execute(ReferenceSectionId.Enhancements);

        Assert.Equal(50, viewModel.EnhancementMaxLevel);
        AssertLevelFilterState(viewModel);
        Assert.Equal(
            EnhancementHelpResolverBoosterMultipliers.MaxBoostedPresentationLevel,
            viewModel.LevelFilterOptions.Max());
    }

    private static void AssertGeneralDefaults(ReferenceViewModel viewModel)
    {
        Assert.Equal(ReferenceEnhancementBrowseSupport.GeneralDefaultMinimumLevel, viewModel.EnhancementMinLevel);
        Assert.Equal(ReferenceEnhancementBrowseSupport.DefaultPresentationLevel, viewModel.EnhancementMaxLevel);
    }

    private static void AssertInventionDefaults(ReferenceViewModel viewModel)
    {
        Assert.Equal(ReferenceEnhancementBrowseSupport.InventionDefaultMinimumLevel, viewModel.EnhancementMinLevel);
        Assert.Equal(ReferenceEnhancementBrowseSupport.DefaultPresentationLevel, viewModel.EnhancementMaxLevel);
    }

    private static void AssertLevelFilterState(ReferenceViewModel viewModel)
    {
        Assert.NotEmpty(viewModel.LevelFilterOptions);
        Assert.Contains(viewModel.EnhancementMinLevel, viewModel.LevelFilterOptions);
        Assert.Contains(viewModel.EnhancementMaxLevel, viewModel.LevelFilterOptions);
        Assert.True(viewModel.EnhancementMinLevel <= viewModel.EnhancementMaxLevel);
    }

    private static ReferenceViewModel CreateViewModel() =>
        new(
            new TestGameplaySessionContextSupport.FakeApplicationOrchestrator(),
            new FakeGameRuntimeService(),
            ProductionCatalog);
}
