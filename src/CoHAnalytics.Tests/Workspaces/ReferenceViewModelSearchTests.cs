using CoHAnalytics.ReferenceData;
using CoHAnalytics.Tests.Services;
using CoHAnalytics.ViewModels.Workspaces;

namespace CoHAnalytics.Tests.Workspaces;

public sealed class ReferenceViewModelSearchTests
{
    private readonly IItemReferenceCatalog _catalog = ItemReferenceCatalogFactory.LoadEmbeddedProduction();

    [Fact]
    public void Matching_search_term_shows_search_results_and_hides_browse_tree()
    {
        using var viewModel = CreateViewModel();

        viewModel.SearchText = "Positron's Blast";

        Assert.NotEmpty(viewModel.SearchResults);
        Assert.Contains(
            viewModel.SearchResults,
            result => result.Label.Contains("Positron's Blast", StringComparison.Ordinal));
        Assert.True(viewModel.ShowSearchResults);
        Assert.False(viewModel.ShowBrowseTree);
    }

    [Fact]
    public void Search_is_case_insensitive()
    {
        using var viewModel = CreateViewModel();

        viewModel.SearchText = "POSITRON'S BLAST";

        Assert.NotEmpty(viewModel.SearchResults);
        Assert.Contains(
            viewModel.SearchResults,
            result => result.Label.Contains("Positron's Blast", StringComparison.OrdinalIgnoreCase));
        Assert.True(viewModel.ShowSearchResults);
    }

    [Fact]
    public void No_match_keeps_browse_tree_visible_and_hides_search_results()
    {
        using var viewModel = CreateViewModel();

        viewModel.SearchText = "zzzz-no-reference-match-zzzz";

        Assert.Empty(viewModel.SearchResults);
        Assert.False(viewModel.ShowSearchResults);
        Assert.True(viewModel.ShowBrowseTree);
    }

    [Fact]
    public void Clearing_search_restores_unfiltered_browse_tree()
    {
        using var viewModel = CreateViewModel();

        viewModel.SearchText = "Positron's Blast";
        Assert.True(viewModel.ShowSearchResults);

        viewModel.SearchText = string.Empty;

        Assert.Empty(viewModel.SearchResults);
        Assert.False(viewModel.ShowSearchResults);
        Assert.True(viewModel.ShowBrowseTree);
        Assert.NotEmpty(viewModel.TreeRootNodes);
    }

    [Fact]
    public void Section_switch_clears_search_state()
    {
        using var viewModel = CreateViewModel();

        viewModel.SearchText = "Positron's Blast";
        Assert.True(viewModel.ShowSearchResults);

        viewModel.SelectSectionChipCommand.Execute(ReferenceSectionId.Recipes);

        Assert.Equal(string.Empty, viewModel.SearchText);
        Assert.Empty(viewModel.SearchResults);
        Assert.False(viewModel.ShowSearchResults);
        Assert.True(viewModel.ShowBrowseTree);
    }

    [Fact]
    public void Search_presentation_notifies_after_results_are_populated()
    {
        using var viewModel = CreateViewModel();
        var showSearchResultsValues = new List<bool>();

        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ReferenceViewModel.ShowSearchResults))
            {
                showSearchResultsValues.Add(viewModel.ShowSearchResults);
            }
        };

        viewModel.SearchText = "Positron's Blast";

        Assert.Contains(true, showSearchResultsValues);
        Assert.True(viewModel.ShowSearchResults);
    }

    [Fact]
    public void Browse_filter_rebuild_notifies_show_search_results_when_results_return()
    {
        using var viewModel = CreateViewModel();
        viewModel.SetWorkspaceActive(true);
        var allFilter = viewModel.RarityFilterOptions.Single(option => option.Label == "All");
        var rareFilter = viewModel.RarityFilterOptions.Single(option => option.Label == "Rare");
        var showSearchResultsValues = new List<bool>();

        viewModel.SearchText = "Positron's Blast";
        Assert.True(viewModel.ShowSearchResults);

        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ReferenceViewModel.ShowSearchResults))
            {
                showSearchResultsValues.Add(viewModel.ShowSearchResults);
            }
        };

        viewModel.EnhancementMinLevel = 10;
        viewModel.EnhancementMaxLevel = 19;
        viewModel.SelectedRarityFilter = rareFilter;
        viewModel.EnhancementMinLevel = viewModel.CatalogMinimumLevel;
        viewModel.EnhancementMaxLevel = viewModel.CatalogMaximumLevel;
        viewModel.SelectedRarityFilter = allFilter;

        Assert.NotEmpty(viewModel.SearchResults);
        Assert.Contains(true, showSearchResultsValues);
        Assert.True(viewModel.ShowSearchResults);
    }

    [Fact]
    public void Recipe_search_shows_results_for_matching_recipe()
    {
        using var viewModel = CreateViewModel();
        viewModel.SelectSectionChipCommand.Execute(ReferenceSectionId.Recipes);

        viewModel.SearchText = "Invention: Accuracy (Recipe)";

        Assert.Contains(
            viewModel.SearchResults,
            result => result.Label == "Invention: Accuracy (Recipe)");
        Assert.True(viewModel.ShowSearchResults);
        Assert.False(viewModel.ShowBrowseTree);
    }

    private ReferenceViewModel CreateViewModel() =>
        new(
            new TestGameplaySessionContextSupport.FakeApplicationOrchestrator(),
            new FakeGameRuntimeService(),
            _catalog);
}
