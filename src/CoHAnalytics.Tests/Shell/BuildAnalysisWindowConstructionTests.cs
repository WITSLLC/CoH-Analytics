using System.Windows;
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
    public Task Window_loads_all_runtime_resources() =>
        _dispatcher.InvokeAsync(() =>
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
                var window = new BuildAnalysisWindow
                {
                    DataContext = new CharacterBuildSetAnalysis
                    {
                        SummaryBonuses =
                        [
                            new CharacterBuildSummaryBonus
                            {
                                CanonicalIdentity = "Set_Bonus.Set_Bonus.Test",
                                Title = "Localized bonus title",
                                DetailText = "Canonical bonus detail.",
                                Count = 1,
                                CountLabel = "1×"
                            }
                        ],
                        GlobalBonuses = [],
                        Sets = []
                    }
                };

                window.Show();
                window.UpdateLayout();
                Assert.True(window.IsVisible);
                window.Close();
            }
            finally
            {
                Application.Current.Resources.MergedDictionaries.Remove(theme);
            }
        });
}
