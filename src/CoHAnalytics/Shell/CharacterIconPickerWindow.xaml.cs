using System.Windows;
using Microsoft.Win32;
using CoHAnalytics.Services;
using CoHAnalytics.ViewModels.CharacterIcons;

namespace CoHAnalytics.Shell;

public partial class CharacterIconPickerWindow : Window
{
    public CharacterIconPickerWindow()
    {
        InitializeComponent();
    }

    private void ApplyButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is CharacterIconPickerViewModel { CanApply: true })
        {
            DialogResult = true;
        }
    }

    private void CancelButton_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void UploadCustomIconButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not CharacterIconPickerViewModel viewModel)
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "Select a Character Image",
            Filter = "PNG and JPEG images (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg|PNG images (*.png)|*.png|JPEG images (*.jpg;*.jpeg)|*.jpg;*.jpeg",
            CheckFileExists = true,
            CheckPathExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var loadResult = CustomCharacterIconImageProcessor.LoadSource(dialog.FileName);
        if (!loadResult.IsSuccess || loadResult.Source is null)
        {
            ShowImportFailure(loadResult.ErrorMessage ?? "The selected image could not be imported.");
            return;
        }

        var editorViewModel = new CustomCharacterIconEditorViewModel(loadResult.Source);
        var editor = new CustomCharacterIconEditorWindow
        {
            Owner = this,
            DataContext = editorViewModel
        };
        if (editor.ShowDialog() != true || editor.NormalizedPng is null)
        {
            return;
        }

        if (!viewModel.TrySaveAndSelectCustomIcon(editor.NormalizedPng, out var errorMessage))
        {
            ShowImportFailure(errorMessage ?? "The custom icon could not be saved.");
            return;
        }
    }

    private void ShowImportFailure(string message)
    {
        MessageBox.Show(
            this,
            message,
            "Custom Icon",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private void DeleteCustomIconButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not CharacterIconPickerViewModel viewModel
            || viewModel.SelectedCustomIcon is null)
        {
            return;
        }

        var assignments = viewModel.GetSelectedCustomIconAssignments();
        if (assignments.Count > 0)
        {
            var displayedCharacters = string.Join(
                Environment.NewLine,
                assignments.Take(5).Select(name => $"• {name}"));
            var remaining = assignments.Count > 5
                ? $"{Environment.NewLine}• and {assignments.Count - 5} more"
                : string.Empty;
            MessageBox.Show(
                this,
                $"This custom icon is still assigned to {assignments.Count} character(s):{Environment.NewLine}{Environment.NewLine}{displayedCharacters}{remaining}{Environment.NewLine}{Environment.NewLine}Assign those characters another icon before deleting it.",
                "Custom Icon In Use",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var confirmation = new CustomIconDeleteConfirmationWindow(
            viewModel.SelectedCustomIcon.ImageSource)
        {
            Owner = this
        };

        if (!TryDeleteConfirmedCustomIcon(
                viewModel,
                confirmation.ShowDialog(),
                out var errorMessage)
            && errorMessage is not null)
        {
            ShowImportFailure(errorMessage);
        }
    }

    internal static bool CanShowCustomIconDeleteConfirmation(
        CharacterIconPickerViewModel viewModel) =>
        viewModel.SelectedCustomIcon is not null
        && viewModel.GetSelectedCustomIconAssignments().Count == 0;

    internal static bool TryDeleteConfirmedCustomIcon(
        CharacterIconPickerViewModel viewModel,
        bool? confirmationResult,
        out string? errorMessage)
    {
        if (confirmationResult != true)
        {
            errorMessage = null;
            return false;
        }

        return viewModel.TryDeleteSelectedCustomIcon(out errorMessage);
    }
}
