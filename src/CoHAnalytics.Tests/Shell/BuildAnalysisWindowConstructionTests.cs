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

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public Task Tab_chips_render_full_right_edge_without_clipping_in_every_selected_state(int selectedIndex) =>
        _dispatcher.InvokeAsync(() => WithTheme(() =>
        {
            var window = new BuildAnalysisWindow { DataContext = CreatePopulatedAnalysis() };
            try
            {
                window.Show();
                var tabs = Assert.IsType<TabControl>(window.FindName("AnalysisTabs"));
                tabs.SelectedIndex = selectedIndex;
                window.UpdateLayout();

                var panel = Assert.Single(Descendants<System.Windows.Controls.Primitives.TabPanel>(window));
                var chips = Descendants<TabItem>(window)
                    .Select(tab =>
                    {
                        var border = Assert.Single(
                            Descendants<System.Windows.Controls.Border>(tab),
                            candidate => candidate.Name == "TabBorder");
                        var borderLeft = border.TransformToAncestor(panel).Transform(new Point(0, 0)).X;
                        var borderRight = borderLeft + border.ActualWidth;
                        var tabRight = tab.TransformToAncestor(panel).Transform(new Point(tab.ActualWidth, 0)).X;
                        var content = Assert.Single(Descendants<ContentPresenter>(border));
                        return (tab, border, borderLeft, borderRight, tabRight, content);
                    })
                    .ToArray();

                Assert.Equal(3, chips.Length);
                foreach (var chip in chips)
                {
                    // The chip border must not be flush against its item's right boundary: the
                    // template reserves clearance so the right edge/rounded corner is never clipped.
                    Assert.True(
                        chip.tabRight - chip.borderRight >= 4,
                        $"Chip '{chip.tab.Header}' right edge has no clearance (border {chip.borderRight:F2}, tab {chip.tabRight:F2}).");
                    Assert.False(chip.border.ClipToBounds);
                    Assert.False(chip.tab.ClipToBounds);
                    Assert.True(chip.content.ActualWidth > 0);
                }

                // Neighboring chips keep a real gap and never overlap.
                for (var index = 1; index < chips.Length; index++)
                {
                    Assert.True(
                        chips[index].borderLeft - chips[index - 1].borderRight >= 4,
                        $"Chips '{chips[index - 1].tab.Header}' and '{chips[index].tab.Header}' are not separated.");
                }
            }
            finally
            {
                window.Close();
            }
        }));

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
                Assert.False(expander.IsExpanded);
                expander.IsExpanded = true;
                window.UpdateLayout();
                Assert.True(expander.IsExpanded);
                Assert.Contains(Descendants<TextBlock>(window), block => block.Text == "Localized set bonus");
                expander.IsExpanded = false;
                window.UpdateLayout();
                Assert.False(expander.IsExpanded);
            }
            finally
            {
                window.Close();
            }
        }));

    [Fact]
    public Task By_set_expanders_start_collapsed_and_remain_independently_controllable() =>
        _dispatcher.InvokeAsync(() => WithTheme(() =>
        {
            var window = new BuildAnalysisWindow { DataContext = CreateMultiSetAnalysis() };
            try
            {
                window.Show();
                var tabs = Assert.IsType<TabControl>(window.FindName("AnalysisTabs"));
                tabs.SelectedIndex = 1;
                window.UpdateLayout();

                var expanders = Descendants<Expander>(window).ToArray();
                Assert.Equal(2, expanders.Length);
                Assert.All(expanders, expander => Assert.False(expander.IsExpanded));

                expanders[0].IsExpanded = true;
                window.UpdateLayout();
                Assert.True(expanders[0].IsExpanded);
                Assert.False(expanders[1].IsExpanded);

                expanders[1].IsExpanded = true;
                window.UpdateLayout();
                Assert.True(expanders[0].IsExpanded);
                Assert.True(expanders[1].IsExpanded);

                expanders[0].IsExpanded = false;
                window.UpdateLayout();
                Assert.False(expanders[0].IsExpanded);
                Assert.True(expanders[1].IsExpanded);

                tabs.SelectedIndex = 2;
                window.UpdateLayout();
                Assert.IsType<ItemsControl>(window.FindName("PvpBonusItems"));

                tabs.SelectedIndex = 0;
                window.UpdateLayout();
                Assert.IsType<ItemsControl>(window.FindName("SummaryBonusItems"));
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

    private static CharacterBuildSetAnalysis CreateMultiSetAnalysis()
    {
        var first = CreatePopulatedAnalysis().Sets[0];
        return new CharacterBuildSetAnalysis
        {
            TotalEnhancementCount = 8,
            IncompleteSetCount = 2,
            SummaryBonuses = CreatePopulatedAnalysis().SummaryBonuses,
            GlobalBonuses = CreatePopulatedAnalysis().GlobalBonuses,
            Sets =
            [
                first,
                new CharacterBuildSetAnalysisEntry
                {
                    EnhancementSetId = "SET-SECOND",
                    DisplayName = "Second Fixture Set",
                    PowerName = "Power Two",
                    PieceCount = 4,
                    PieceCountLabel = "4 pieces",
                    TotalPieceCount = 6,
                    PieceProgressLabel = "4 / 6 pieces",
                    CategoryLabel = "Defense Set",
                    EarnedBonusCountLabel = "2 bonuses",
                    EarnedBonuses =
                    [
                        new CharacterBuildEarnedSetBonus
                        {
                            ThresholdLabel = "2-piece",
                            HelpLines = ["Second set detail."]
                        }
                    ],
                    BonusRows =
                    [
                        new CharacterBuildSetBonusRow
                        {
                            ThresholdLabel = "2×",
                            Title = "Second localized bonus",
                            DetailText = "Second set detail."
                        }
                    ]
                }
            ]
        };
    }

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
