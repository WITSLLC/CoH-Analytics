using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CoHAnalytics.ViewModels.Workspaces;

namespace CoHAnalytics.Workspaces;

public partial class ReferenceView : UserControl
{
    private bool _suppressBrowseTreeSelectionSync;

    public ReferenceView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is ReferenceViewModel viewModel)
        {
            viewModel.EnsureWorkspaceActivated();
            SyncBrowseTreeToViewModel(viewModel);
        }
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is ReferenceViewModel oldViewModel)
        {
            oldViewModel.PropertyChanged -= ViewModelOnPropertyChanged;
        }

        if (e.NewValue is ReferenceViewModel newViewModel)
        {
            newViewModel.PropertyChanged += ViewModelOnPropertyChanged;
            if (IsLoaded)
            {
                newViewModel.EnsureWorkspaceActivated();
                SyncBrowseTreeToViewModel(newViewModel);
            }
        }
    }

    private void ViewModelOnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is not ReferenceViewModel viewModel
            || e.PropertyName != nameof(ReferenceViewModel.SelectedNode)
            || viewModel.SelectedNode is null)
        {
            return;
        }

        var selectedNode = viewModel.SelectedNode;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (DataContext is not ReferenceViewModel current
                || !ReferenceEquals(current.SelectedNode, selectedNode))
            {
                return;
            }

            SyncBrowseTreeSelection(current, selectedNode);
        });
    }

    private void SyncBrowseTreeToViewModel(ReferenceViewModel viewModel)
    {
        _suppressBrowseTreeSelectionSync = true;
        try
        {
            // A recreated TreeView can fail to render items bound before the view existed.
            BrowseTree.ItemsSource = null;
            BrowseTree.ItemsSource = viewModel.TreeRootNodes;

            if (viewModel.SelectedNode is not null)
            {
                SyncBrowseTreeSelection(viewModel, viewModel.SelectedNode);
            }
        }
        finally
        {
            _suppressBrowseTreeSelectionSync = false;
        }
    }

    private void SyncBrowseTreeSelection(
        ReferenceViewModel viewModel,
        ReferenceEnhancementTreeNodeViewModel selectedNode)
    {
        if (!ReferenceEquals(viewModel.SelectedNode, selectedNode))
        {
            return;
        }

        BrowseTree.UpdateLayout();
        var container = FindTreeViewItem(BrowseTree, selectedNode);
        if (container is not null)
        {
            container.IsSelected = true;
            container.BringIntoView();
        }
    }

    private void BrowseTree_OnSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (_suppressBrowseTreeSelectionSync)
        {
            return;
        }

        if (DataContext is not ReferenceViewModel viewModel)
        {
            return;
        }

        if (e.NewValue is not ReferenceEnhancementTreeNodeViewModel node)
        {
            return;
        }

        viewModel.SelectedNode = node;
    }

    private static TreeViewItem? FindTreeViewItem(ItemsControl parent, ReferenceEnhancementTreeNodeViewModel target)
    {
        if (parent.ItemContainerGenerator.ContainerFromItem(target) is TreeViewItem directItem)
        {
            return directItem;
        }

        foreach (var item in parent.Items)
        {
            if (parent.ItemContainerGenerator.ContainerFromItem(item) is not TreeViewItem container)
            {
                continue;
            }

            if (ReferenceEquals(container.DataContext, target))
            {
                return container;
            }

            var nested = FindTreeViewItem(container, target);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }
}
