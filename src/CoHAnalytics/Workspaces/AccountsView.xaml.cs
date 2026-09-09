using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CoHAnalytics.Shell;
using CoHAnalytics.ViewModels.Workspaces;

namespace CoHAnalytics.Workspaces;

public partial class AccountsView : UserControl
{
    public AccountsView()
    {
        InitializeComponent();
    }

    private void OnAccountCardClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border border || border.DataContext is not AccountListItemViewModel account)
        {
            return;
        }

        if (DataContext is AccountsViewModel viewModel)
        {
            viewModel.SelectAccountCommand.Execute(account);
        }
    }

    private void OnCharacterIconClick(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is not AccountsViewModel viewModel
            || viewModel.CreateCharacterIconPicker() is not { } pickerViewModel)
        {
            return;
        }

        var picker = new CharacterIconPickerWindow
        {
            Owner = System.Windows.Window.GetWindow(this),
            DataContext = pickerViewModel
        };

        if (picker.ShowDialog() == true)
        {
            _ = viewModel.ApplyCharacterIconSelection(pickerViewModel.SelectedIcon?.Reference);
        }
    }

    private void BadgePageScrollViewer_OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is ScrollViewer badgePageScrollViewer)
        {
            RouteBadgeMouseWheelToMainPage(badgePageScrollViewer, e);
        }
    }

    internal static void RouteBadgeMouseWheelToMainPage(
        ScrollViewer badgePageScrollViewer,
        MouseWheelEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(badgePageScrollViewer);
        ArgumentNullException.ThrowIfNull(e);

        var ancestor = VisualTreeHelper.GetParent(badgePageScrollViewer);
        while (ancestor is not null and not ScrollViewer)
        {
            ancestor = VisualTreeHelper.GetParent(ancestor);
        }

        if (ancestor is not ScrollViewer mainPageScrollViewer)
        {
            return;
        }

        mainPageScrollViewer.ScrollToVerticalOffset(mainPageScrollViewer.VerticalOffset - e.Delta);
        e.Handled = true;
    }
}
