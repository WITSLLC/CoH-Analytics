using System.Windows.Controls;
using System.Windows.Input;
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
}
