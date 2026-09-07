using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using CoHAnalytics.Models;
using CoHAnalytics.Orchestration.Contracts;
using CoHAnalytics.Orchestration.Models;
using CoHAnalytics.ReferenceData;
using CoHAnalytics.Services;
using CoHAnalytics.ViewModels.Workspaces;

namespace CoHAnalytics.Tests.Workspaces;

[Collection(WpfDispatcherCollection.Name)]
public sealed class ReferenceDetailNavigationCrashTests
{
    private readonly WpfDispatcherFixture _dispatcher;

    public ReferenceDetailNavigationCrashTests(WpfDispatcherFixture dispatcher)
    {
        _dispatcher = dispatcher;
    }

    [Fact]
    public async Task Navigation_row_buttons_survive_repeated_member_drill_down()
    {
        await _dispatcher.InvokeAsync(async () =>
        {
            Window? window = null;
            try
            {
                var theme = new ResourceDictionary
                {
                    Source = new Uri("pack://application:,,,/CoHAnalytics;component/Themes/Hero/HeroTheme.xaml", UriKind.Absolute)
                };

                var catalog = ItemReferenceCatalogFactory.LoadEmbeddedProduction();
                using var viewModel = new ReferenceViewModel(
                    new FakeApplicationOrchestrator(ApplicationStateSnapshot.Empty(DateTimeOffset.UtcNow)),
                    new FakeGameRuntimeService(),
                    catalog);

                var host = new UserControl
                {
                    DataContext = viewModel,
                    Width = 720,
                    Height = 640,
                    Content = CreateMemberList(viewModel, (Style)theme["Button.NavigationRow"])
                };

                window = new Window
                {
                    Width = 760,
                    Height = 680,
                    Content = new ScrollViewer { Content = host }
                };

                window.Show();
                window.UpdateLayout();
                await _dispatcher.DrainAsync();

                var tree = ReferenceEnhancementBrowseSupport.BuildBrowseTree(catalog);
                var bonesnap = tree.NodesByKey.Values.Single(node =>
                    node.Kind == ReferenceEnhancementBrowseNodeKind.Set
                    && node.DisplayName == "Bonesnap");

                for (var iteration = 0; iteration < 12; iteration++)
                {
                    viewModel.SelectSetFromDetailCommand.Execute(bonesnap.SetId);
                    await _dispatcher.DrainAsync();
                    Assert.Equal(ReferenceEnhancementDetailKind.Set, viewModel.Detail.Kind);
                    Assert.NotEmpty(viewModel.Detail.MemberItems);
                    window.UpdateLayout();
                    await _dispatcher.DrainAsync();

                    var memberButtons = FindMemberButtons(host).ToList();
                    Assert.NotEmpty(memberButtons);
                    var button = memberButtons[iteration % memberButtons.Count];
                    var expectedEnhancementId = Assert.IsType<string>(button.Tag);
                    var command = Assert.IsAssignableFrom<System.Windows.Input.ICommand>(button.Command);
                    Assert.True(command.CanExecute(button.CommandParameter));
                    command.Execute(button.CommandParameter);
                    await _dispatcher.DrainAsync();

                    Assert.Equal(expectedEnhancementId, viewModel.SelectedNode?.EnhancementId);
                    Assert.Equal(ReferenceEnhancementDetailKind.Enhancement, viewModel.Detail.Kind);
                    Assert.Equal(bonesnap.SetId, viewModel.Detail.ParentSetId);
                    window.UpdateLayout();
                    await _dispatcher.DrainAsync();
                }
            }
            finally
            {
                window?.Close();
            }
        }).WaitAsync(TimeSpan.FromSeconds(90));
    }

    private static ItemsControl CreateMemberList(ReferenceViewModel viewModel, Style navigationRowStyle)
    {
        var itemsControl = new ItemsControl();
        itemsControl.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(nameof(ReferenceEnhancementDetailViewModel.MemberItems))
        {
            Source = viewModel.Detail
        });

        var buttonFactory = new FrameworkElementFactory(typeof(Button));
        buttonFactory.SetValue(Button.StyleProperty, navigationRowStyle);
        buttonFactory.SetValue(Button.MarginProperty, new Thickness(0, 0, 0, 6));
        buttonFactory.SetValue(Button.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch);
        buttonFactory.SetBinding(
            Button.CommandProperty,
            new Binding($"DataContext.{nameof(ReferenceViewModel.NavigateToNodeCommand)}")
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(UserControl), 1)
            });
        buttonFactory.SetBinding(
            Button.CommandParameterProperty,
            new Binding(nameof(ReferenceEnhancementMemberSummaryItem.TargetNodeKey)));
        buttonFactory.SetBinding(
            FrameworkElement.TagProperty,
            new Binding(nameof(ReferenceEnhancementMemberSummaryItem.EnhancementId)));

        var labelFactory = new FrameworkElementFactory(typeof(TextBlock));
        labelFactory.SetBinding(TextBlock.TextProperty, new Binding(nameof(ReferenceEnhancementMemberSummaryItem.DisplayName)));
        buttonFactory.AppendChild(labelFactory);

        itemsControl.ItemTemplate = new DataTemplate
        {
            VisualTree = buttonFactory
        };

        return itemsControl;
    }

    private static IEnumerable<Button> FindMemberButtons(DependencyObject parent) =>
        FindVisualChildren<Button>(parent)
            .Where(button => button.Tag is string enhancementId
                && enhancementId.StartsWith("ENH-", StringComparison.Ordinal));

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var nested in FindVisualChildren<T>(child))
            {
                yield return nested;
            }
        }
    }

    private sealed class FakeApplicationOrchestrator(ApplicationStateSnapshot current) : IApplicationOrchestrator
    {
        public ApplicationStateSnapshot Current { get; private set; } = current;

#pragma warning disable CS0067
        public event EventHandler<ApplicationStateChangedEventArgs>? SnapshotChanged;
#pragma warning restore CS0067

        public Task RefreshAsync(string? providerId = null, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FakeGameRuntimeService : IGameRuntimeService
    {
        public GameRuntimeStatus CurrentStatus { get; set; } = GameRuntimeStatus.Unconfigured;

        public int RunningClientCount { get; set; }

        public IReadOnlyList<HomecomingProcessInstance> RunningClients { get; set; } =
            Array.Empty<HomecomingProcessInstance>();

        public string? LastErrorMessage { get; set; }

#pragma warning disable CS0067
        public event EventHandler<GameRuntimeStatusChangedEventArgs>? StatusChanged;
#pragma warning restore CS0067

        public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task LaunchAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void Start()
        {
        }

        public void Stop()
        {
        }

        public void Dispose()
        {
        }
    }
}
