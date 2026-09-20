using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
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
    public Task Offense_view_binds_power_selection_and_preserves_other_section_placeholders() =>
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
                var vm = fixture.Create(); vm.Refresh();
                vm.Offense.SetProjection(CombatOffenseViewModelTests.Sample(), CombatOffenseViewModelTests.Manifest());
                var host = new HistoricalCombatView { DataContext = vm };
                Layout(host);
                var offense = Descendants(host).OfType<CombatOffenseView>().Single();
                Assert.Equal(Visibility.Visible, offense.Visibility);
                Assert.Same(vm.Offense, offense.DataContext);
                var list = (ListBox)offense.FindName("PowerList");
                Assert.Equal(230, list.MaxHeight);
                Assert.Equal(2, list.Items.Count);
                Assert.Empty(Descendants(list).OfType<Image>());
                Assert.Contains(Descendants(list).OfType<TextBlock>().Select(t => t.Text), t => t == "Player");
                Assert.Contains(Descendants(list).OfType<TextBlock>().Select(t => t.Text), t => t == "Owned pets");
                Assert.DoesNotContain(Descendants(offense).OfType<TextBlock>().Select(t => t.Text), t => t == "Power / source");
                var headers = Descendants(offense).OfType<Button>().Select(b => b.Content as string).ToArray();
                Assert.Contains("Source", headers);
                Assert.Contains("Damage ▾", headers);
                Assert.Equal(vm.Offense.SortPowersCommand, Descendants(offense).OfType<Button>().First(b => b.Content as string == "Source").Command);
                var strip = (ItemsControl)host.FindName("OffenseSummaryStrip");
                Assert.Equal(Visibility.Visible, strip.Visibility);
                Assert.Equal(vm.Offense.Summary, strip.ItemsSource);
                list.SelectedItem = vm.Offense.Powers[1];
                Layout(host);
                Assert.Same(vm.Offense.Powers[1], vm.Offense.SelectedPower);
                Assert.Equal(CombatAnalyticsScope.Self, vm.Offense.SelectedPower!.Source.Scope);
                Assert.Equal("Player", vm.Offense.SelectedPower.Scope);
                Assert.Equal("50.01", vm.Offense.SelectedPower.Damage);
                Assert.Equal("3", vm.Offense.SelectedPower.Activations);
                var numeric = Descendants(list).OfType<TextBlock>()
                    .Where(t => t.Text is "50.01" or "73.44" or "3").ToArray();
                Assert.Equal(4, numeric.Length);
                Assert.All(numeric, t =>
                {
                    Assert.Equal(HorizontalAlignment.Right, t.HorizontalAlignment);
                    Assert.Equal(FontNumeralAlignment.Tabular, Typography.GetNumeralAlignment(t));
                });
                Assert.Contains(Descendants(list).OfType<TextBlock>(), t => t.Text == "Player" && t.HorizontalAlignment != HorizontalAlignment.Right);
                var style = (Style)offense.FindResource("Text.NumericValue");
                Assert.Contains(style.Setters.OfType<Setter>(), s => s.Property == Typography.NumeralAlignmentProperty && Equals(s.Value, FontNumeralAlignment.Tabular));
                Assert.Contains(style.Setters.OfType<Setter>(), s => s.Property == FrameworkElement.HorizontalAlignmentProperty && Equals(s.Value, HorizontalAlignment.Right));
                var summaryValues = Descendants(strip).OfType<TextBlock>()
                    .Where(t => t.Text is "123.45" or "50.01" or "4.56" or "98.76").ToArray();
                Assert.Equal(4, summaryValues.Length);
                Assert.All(summaryValues, t =>
                {
                    Assert.Equal(HorizontalAlignment.Right, t.HorizontalAlignment);
                    Assert.Equal(FontNumeralAlignment.Tabular, Typography.GetNumeralAlignment(t));
                });
                Assert.Contains(Descendants(strip).OfType<TextBlock>(),
                    t => t.Text == "Total outgoing damage" && t.HorizontalAlignment != HorizontalAlignment.Right);
                Assert.NotEmpty(Descendants(offense).OfType<Image>());
                Assert.Empty(Descendants(list).OfType<Image>());
                var detailImage = Descendants(offense).OfType<Image>().Single();
                Assert.Same(vm.Offense.SelectedPower!.Icon, detailImage.Source);
                Assert.Same(CombatOffenseViewModel.GenericDamageIcon, detailImage.Source);
                list.SelectedItem = vm.Offense.Powers[0];
                Layout(host);
                Assert.Same(vm.Offense.Powers[0], vm.Offense.SelectedPower);
                Assert.Equal(CombatAnalyticsScope.OwnPetsAggregate, vm.Offense.SelectedPower!.Source.Scope);
                Assert.Equal("Owned pets", vm.Offense.SelectedPower.Scope);
                Assert.Equal("73.44", vm.Offense.SelectedPower.Damage);
                Assert.Equal(new[] { "Offense", "Incoming", "Healing" }, vm.Sections.Select(s => s.Label));
                Assert.DoesNotContain(vm.Sections, s => s.Label == "Pets");
                Assert.DoesNotContain(Descendants(host).OfType<TextBlock>().Select(t => t.Text),
                    t => t == "Pets" || t == "Select a segment and open Report to view its captured combat analytics.");
                vm.SelectSectionCommand.Execute(CombatSectionId.Incoming);
                Layout(host);
                var incoming = Descendants(host).OfType<CombatIncomingView>().Single();
                var incomingStrip = (ItemsControl)host.FindName("IncomingSummaryStrip");
                Assert.Equal(Visibility.Collapsed, offense.Visibility);
                Assert.Equal(Visibility.Collapsed, strip.Visibility);
                Assert.Equal(Visibility.Visible, incoming.Visibility);
                Assert.Equal(Visibility.Visible, incomingStrip.Visibility);
                vm.Incoming.SetProjection(CombatAnalyticsProjection.Empty with
                {
                    Powers = [CombatOffenseViewModelTests.Power("Bone Shard") with { Direction = CombatAnalyticsDirection.Incoming }]
                });
                Layout(host);
                var incomingList = (ListBox)incoming.FindName("IncomingPowerList");
                Assert.Equal(230, incomingList.MaxHeight);
                Assert.Empty(Descendants(incomingList).OfType<Image>());
                var incomingHeaders = Descendants(incoming).OfType<Button>().Select(b => b.Content as string).ToArray();
                Assert.Contains("Target", incomingHeaders);
                Assert.DoesNotContain("Source", incomingHeaders);
                Assert.Equal(vm.Incoming.SortPowersCommand, Descendants(incoming).OfType<Button>().First(b => b.Content as string == "Target").Command);
                Assert.Contains(Descendants(incomingList).OfType<TextBlock>().Select(t => t.Text), t => t == "Player");
                Assert.DoesNotContain(Descendants(incoming).OfType<TextBlock>().Select(t => t.Text), t => t is "Distinct targets");
                Assert.Contains(Descendants(incoming).OfType<Button>().Select(b => b.Content as string), h => h == "Events ▾" || h == "Events");
                Assert.DoesNotContain(Descendants(incoming).OfType<TextBlock>().Select(t => t.Text), t => t is not null && t.Contains("Resistance", StringComparison.OrdinalIgnoreCase));
                Assert.NotEmpty(Descendants(incoming).OfType<Image>());
                Assert.Same(CombatOffenseViewModel.GenericDamageIcon, Descendants(incoming).OfType<Image>().Single().Source);
                vm.SelectSectionCommand.Execute(CombatSectionId.Healing);
                Layout(host);
                var healing = Descendants(host).OfType<CombatHealingView>().Single();
                var healingStrip = (ItemsControl)host.FindName("HealingSummaryStrip");
                Assert.Equal(Visibility.Collapsed, offense.Visibility);
                Assert.Equal(Visibility.Collapsed, strip.Visibility);
                Assert.Equal(Visibility.Collapsed, incoming.Visibility);
                Assert.Equal(Visibility.Collapsed, incomingStrip.Visibility);
                Assert.Equal(Visibility.Visible, healing.Visibility);
                Assert.Equal(Visibility.Collapsed, healingStrip.Visibility);
                vm.Healing.SetProjection(CombatAnalyticsProjection.Empty with
                {
                    Session = CombatSessionSummary.Empty with
                    {
                        Metrics = CombatSessionMetricSet.Empty with
                        {
                            HealingDealt = Metric<CombatScaledAmount>.Available(new(2215)),
                            HealingReceived = Metric<CombatScaledAmount>.Available(new(800))
                        }
                    },
                    Powers =
                    [
                        CombatOffenseViewModelTests.Power("Transfusion") with
                        {
                            HealingMagnitudeMetric = Metric<CombatScaledAmount>.Available(new(5001)),
                            DamageMagnitudeMetric = Metric<CombatScaledAmount>.NotCaptured()
                        }
                    ]
                });
                Layout(host);
                var healingList = (ListBox)healing.FindName("HealingPowerList");
                Assert.Equal(230, healingList.MaxHeight);
                Assert.Empty(Descendants(healingList).OfType<Image>());
                var healingHeaders = Descendants(healing).OfType<Button>().Select(b => b.Content as string).ToArray();
                Assert.Contains("Direction", healingHeaders);
                Assert.Contains("Amount ▾", healingHeaders);
                Assert.Equal(vm.Healing.SortPowersCommand, Descendants(healing).OfType<Button>().First(b => b.Content as string == "Direction").Command);
                Assert.Contains(Descendants(healingList).OfType<TextBlock>().Select(t => t.Text), t => t == "Healing Dealt");
                Assert.DoesNotContain(Descendants(healing).OfType<TextBlock>().Select(t => t.Text), t => t is not null && t.Contains("HPS", StringComparison.OrdinalIgnoreCase));
                Assert.DoesNotContain(Descendants(healing).OfType<TextBlock>().Select(t => t.Text), t => t is not null && t.Contains("overheal", StringComparison.OrdinalIgnoreCase));
                Assert.DoesNotContain(Descendants(healing).OfType<TextBlock>().Select(t => t.Text), t => t is "Distinct targets");
                var healingNumeric = Descendants(healingList).OfType<TextBlock>()
                    .Where(t => t.Text is "50.01" or "8").ToArray();
                Assert.Equal(2, healingNumeric.Length);
                Assert.All(healingNumeric, t =>
                {
                    Assert.Equal(HorizontalAlignment.Right, t.HorizontalAlignment);
                    Assert.Equal(FontNumeralAlignment.Tabular, Typography.GetNumeralAlignment(t));
                });
                var healingSummaryValues = Descendants(healingStrip).OfType<TextBlock>()
                    .Where(t => t.Text is "22.15" or "8.00").ToArray();
                Assert.Empty(healingSummaryValues);
                Assert.Empty(healingStrip.Items);
                Assert.Equal(Visibility.Collapsed, healingStrip.Visibility);
                Assert.NotEmpty(Descendants(healing).OfType<Image>());
                Assert.Same(CombatOffenseViewModel.GenericDamageIcon, Descendants(healing).OfType<Image>().Single().Source);
                vm.SelectSectionCommand.Execute(CombatSectionId.Offense);
                Layout(host);
                Assert.Equal(Visibility.Visible, offense.Visibility);
                Assert.Equal(Visibility.Visible, strip.Visibility);
                Assert.Empty(Descendants(list).OfType<Image>());
                var preview = new CombatOffenseView { DataContext = vm.Offense };
                Layout(preview);
                SavePreview(preview, "COH_OFFENSE_PREVIEW_PATH");
            }
            finally { Application.Current.Resources.MergedDictionaries.Remove(theme); }
        });

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
                var compare = Descendants(view).OfType<HistoricalCompareView>().Single();
                Assert.Equal(Visibility.Visible, compare.Visibility);
                Assert.Same(vm.HistoricalCompare, compare.DataContext);
                Assert.Equal("VS", ((TextBlock)compare.FindName("CompareVsLabel")).Text);
                Assert.NotNull(compare.FindName("CompareAccountSelectorA"));
                Assert.NotNull(compare.FindName("CompareAccountSelectorB"));
                Assert.DoesNotContain(Descendants(compare).OfType<Button>(), b =>
                    (b.Content as string)?.Contains("Swap", StringComparison.OrdinalIgnoreCase) == true);
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

    private static void SavePreview(FrameworkElement view, string variable = "COH_COMBAT_PREVIEW_PATH")
    {
        var output = Environment.GetEnvironmentVariable(variable);
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
