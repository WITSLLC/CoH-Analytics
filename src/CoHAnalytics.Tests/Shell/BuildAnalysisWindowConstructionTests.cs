using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CoHAnalytics.Shell;
using CoHAnalytics.ViewModels.Workspaces;

namespace CoHAnalytics.Tests.Shell;

[Collection(WpfDispatcherCollection.Name)]
public sealed class BuildAnalysisWindowConstructionTests
{
    private readonly WpfDispatcherFixture _dispatcher;

    public BuildAnalysisWindowConstructionTests(WpfDispatcherFixture dispatcher)
    {
        _dispatcher = dispatcher;
    }

    [Fact]
    public Task Populated_window_renders_compact_tabs_toggle_and_expandable_set_rows() =>
        _dispatcher.InvokeAsync(() => WithTheme(() =>
        {
            var window = new BuildAnalysisWindow { DataContext = CreatePopulatedAnalysis() };
            try
            {
                window.Show();
                window.UpdateLayout();

                var tabs = Assert.IsType<TabControl>(window.FindName("AnalysisTabs"));
                Assert.Equal(0, tabs.SelectedIndex);
                Assert.Single(Assert.IsType<ItemsControl>(window.FindName("SummaryBonusItems")).Items);
                Assert.Single(Assert.IsType<ItemsControl>(window.FindName("GlobalBonusItems")).Items);

                var detail = Assert.Single(Descendants<TextBlock>(window), block =>
                    block.Text == "Canonical bonus detail.");
                Assert.Equal(TextWrapping.NoWrap, detail.TextWrapping);
                var toggle = Assert.IsType<CheckBox>(window.FindName("DetailedDescriptionsCheckBox"));
                Assert.False(toggle.IsChecked);
                toggle.IsChecked = true;
                window.UpdateLayout();
                Assert.True(window.ShowDetailedDescriptions);
                Assert.Equal(TextWrapping.Wrap, detail.TextWrapping);

                tabs.SelectedIndex = 1;
                window.UpdateLayout();
                var expander = Assert.Single(Descendants<Expander>(window));
                Assert.True(expander.IsExpanded);
                expander.IsExpanded = false;
                window.UpdateLayout();
                Assert.False(expander.IsExpanded);
                expander.IsExpanded = true;
                window.UpdateLayout();
                Assert.Contains(Descendants<TextBlock>(window), block => block.Text == "Localized set bonus");
            }
            finally
            {
                window.Close();
            }
        }));

    [Fact]
    public Task Empty_window_renders_summary_and_by_set_empty_states() =>
        _dispatcher.InvokeAsync(() => WithTheme(() =>
        {
            var window = new BuildAnalysisWindow
            {
                DataContext = new CharacterBuildSetAnalysis
                {
                    SummaryBonuses = [],
                    GlobalBonuses = [],
                    Sets = []
                }
            };
            try
            {
                window.Show();
                window.UpdateLayout();
                Assert.Contains(Descendants<TextBlock>(window), block =>
                    block.Visibility == Visibility.Visible
                    && block.Text.StartsWith("No earned enhancement-set bonuses", StringComparison.Ordinal));

                Assert.IsType<TabControl>(window.FindName("AnalysisTabs")).SelectedIndex = 1;
                window.UpdateLayout();
                Assert.Contains(Descendants<TextBlock>(window), block =>
                    block.Visibility == Visibility.Visible
                    && block.Text.StartsWith("No enhancement sets", StringComparison.Ordinal));
            }
            finally
            {
                window.Close();
            }
        }));

    private static CharacterBuildSetAnalysis CreatePopulatedAnalysis() =>
        new()
        {
            TotalEnhancementCount = 5,
            IncompleteSetCount = 1,
            SummaryBonuses =
            [
                new CharacterBuildSummaryBonus
                {
                    CanonicalIdentity = "Set_Bonus.Set_Bonus.Test",
                    Title = "Localized bonus title",
                    DetailText = "Canonical bonus detail.",
                    Count = 2,
                    CountLabel = "2×",
                    SourceLabel = "2 sets",
                    SourceTooltip = "Fixture Set\nSecond Fixture Set"
                }
            ],
            GlobalBonuses =
            [
                new CharacterBuildSummaryBonus
                {
                    CanonicalIdentity = "Set_Bonus.Global_Bonus.Test",
                    Title = "Localized global title",
                    DetailText = "Canonical global detail.",
                    Count = 1,
                    CountLabel = "1×",
                    SourceLabel = "Fixture Set",
                    SourceTooltip = "Fixture Set"
                }
            ],
            Sets =
            [
                new CharacterBuildSetAnalysisEntry
                {
                    EnhancementSetId = "SET-TEST",
                    DisplayName = "Fixture Set",
                    PieceCount = 3,
                    PieceCountLabel = "3 pieces",
                    TotalPieceCount = 6,
                    PieceProgressLabel = "3 / 6 pieces",
                    CategoryLabel = "Damage Set",
                    EarnedBonusCountLabel = "1 bonus",
                    EarnedBonuses =
                    [
                        new CharacterBuildEarnedSetBonus
                        {
                            ThresholdLabel = "2-piece",
                            HelpLines = ["Canonical set detail."]
                        }
                    ],
                    BonusRows =
                    [
                        new CharacterBuildSetBonusRow
                        {
                            ThresholdLabel = "2×",
                            Title = "Localized set bonus",
                            DetailText = "Canonical set detail."
                        }
                    ]
                }
            ]
        };

    private static void WithTheme(Action action)
    {
        var theme = new ResourceDictionary
        {
            Source = new Uri(
                "pack://application:,,,/CoHAnalytics;component/Themes/Hero/HeroTheme.xaml",
                UriKind.Absolute)
        };
        Application.Current.Resources.MergedDictionaries.Add(theme);
        try
        {
            action();
        }
        finally
        {
            Application.Current.Resources.MergedDictionaries.Remove(theme);
        }
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in Descendants<T>(child))
            {
                yield return descendant;
            }
        }
    }
}
