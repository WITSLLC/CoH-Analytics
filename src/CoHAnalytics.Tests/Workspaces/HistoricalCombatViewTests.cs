using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CoHAnalytics.Models;
using CoHAnalytics.Services;
using CoHAnalytics.Tests.Services;
using CoHAnalytics.ViewModels.Workspaces;
using CoHAnalytics.Workspaces;

namespace CoHAnalytics.Tests.Workspaces;

[Collection(WpfDispatcherCollection.Name)]
public sealed class HistoricalCombatViewTests(WpfDispatcherFixture dispatcher)
{
    [Fact]
    public Task Workspace_binds_historical_shell_and_selector_changes_without_live_context() =>
        dispatcher.InvokeAsync(() =>
        {
            var theme = new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/CoHAnalytics;component/Themes/Hero/HeroTheme.xaml")
            };
            Application.Current.Resources.MergedDictionaries.Add(theme);
            try
            {
                using var fixture = new HistoricalCombatViewModelTests.Fixture();
                var identity = new FakeIdentityReadService();
                var viewed = new TestGameplaySessionContextSupport.FakeViewedContextService(
                    TestGameplaySessionContextSupport.FollowingLive());
                using var vm = new AnalyticsViewModel(
                    new TestGameplaySessionContextSupport.FakeApplicationOrchestrator(),
                    new FakeGameRuntimeService { CurrentStatus = GameRuntimeStatus.Off },
                    identity, new GameplaySessionContextResolver(identity, viewed), viewed,
                    characterRepository: fixture.Characters,
                    segmentReportService: new SegmentReportService(fixture.Reader, fixture.Browser, fixture.Root),
                    historicalSegmentReader: fixture.Reader, segmentAnnotationWriter: fixture.Store);
                var view = new AnalyticsView { DataContext = vm };
                vm.SelectChipCommand.Execute(AnalyticsChipId.Combat);
                Layout(view);
                var combat = Descendants(view).OfType<HistoricalCombatView>().Single();
                Assert.Equal(Visibility.Visible, combat.Visibility);
                Assert.Same(vm.HistoricalCombat, combat.DataContext);
                var accounts = (ComboBox)combat.FindName("CombatAccountSelector");
                var characters = (ComboBox)combat.FindName("CombatCharacterSelector");
                var segments = (ComboBox)combat.FindName("CombatSegmentSelector");
                var report = (Button)combat.FindName("CombatReportButton");
                Assert.Equal(2, accounts.Items.Count);
                Assert.Same(vm.HistoricalCombat.SelectedSegment, segments.SelectedItem);
                Assert.True(report.IsEnabled);
                accounts.SelectedItem = vm.HistoricalCombat.AccountsChoices.Single(a => a.Id == "Adelbert");
                Layout(view);
                Assert.Equal(2, characters.Items.Count);
                Assert.All(vm.HistoricalCombat.SegmentChoices,
                    s => Assert.Equal(fixture.Adelbert, s.Header.CanonicalCharacterRecordId));
                vm.HistoricalCombat.RunName = "Warrior Earth Farm";
                vm.HistoricalCombat.SaveRunNameCommand.Execute(null);
                Layout(view);
                Assert.StartsWith("Warrior Earth Farm", vm.HistoricalCombat.SelectedSegment!.Label);
                SavePreview(view);
                segments.SelectedItem = vm.HistoricalCombat.SegmentChoices.Single(
                    s => s.Header.CaptureKind == HistoricalCaptureKind.LegacyObservation);
                Layout(view);
                Assert.False(((TextBox)combat.FindName("CombatRunNameEditor")).IsEnabled);
                Assert.True(report.IsEnabled);
                var selected = vm.HistoricalCombat.SelectedSegment;
                vm.SelectChipCommand.Execute(AnalyticsChipId.Compare);
                Layout(view);
                Assert.True(vm.ShowCompareContent);
                Assert.Equal(Visibility.Collapsed, combat.Visibility);
                Assert.Single(vm.Chips, c => c.IsActive && c.ChipId == AnalyticsChipId.Compare);
                vm.SelectChipCommand.Execute(AnalyticsChipId.Combat);
                Layout(view);
                Assert.Equal(selected!.Header.SegmentId, vm.HistoricalCombat.SelectedSegment!.Header.SegmentId);
                Assert.False(vm.ShowContextSummary);
                vm.SelectChipCommand.Execute(AnalyticsChipId.Overview);
                Layout(view);
                Assert.True(vm.ShowOverviewContent);
                Assert.Equal(Visibility.Collapsed, combat.Visibility);
            }
            finally { Application.Current.Resources.MergedDictionaries.Remove(theme); }
        });

    private static void Layout(FrameworkElement view)
    {
        view.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
        view.Measure(new Size(1450, 760));
        view.Arrange(new Rect(0, 0, 1450, 760));
        view.UpdateLayout();
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static void SavePreview(FrameworkElement view)
    {
        var output = Environment.GetEnvironmentVariable("COH_COMBAT_PREVIEW_PATH");
        if (string.IsNullOrWhiteSpace(output)) return;
        var bitmap = new RenderTargetBitmap(1450, 760, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(view);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(output);
        encoder.Save(stream);
    }

    private sealed class FakeIdentityReadService : IGameplaySessionIdentityReadService
    {
        public GameplaySessionIdentityReadModelSnapshot Current => GameplaySessionIdentityReadModelSnapshot.Empty;
        public event EventHandler<GameplaySessionIdentityReadModelChangedEventArgs>? Changed
        {
            add { }
            remove { }
        }
    }
}
