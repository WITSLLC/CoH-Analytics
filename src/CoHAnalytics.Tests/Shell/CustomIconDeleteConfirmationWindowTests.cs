using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CoHAnalytics.Models;
using CoHAnalytics.Services;
using CoHAnalytics.Shell;
using CoHAnalytics.ViewModels.CharacterIcons;

namespace CoHAnalytics.Tests.Shell;

[Collection(WpfDispatcherCollection.Name)]
public sealed class CustomIconDeleteConfirmationWindowTests
{
    private readonly WpfDispatcherFixture _dispatcher;

    public CustomIconDeleteConfirmationWindowTests(WpfDispatcherFixture dispatcher)
    {
        _dispatcher = dispatcher;
    }

    [Fact]
    public Task Confirmation_displays_the_selected_custom_icon_preview() =>
        _dispatcher.InvokeAsync(() => WithTheme(() =>
        {
            var preview = new DrawingImage();
            var dialog = new CustomIconDeleteConfirmationWindow(preview);

            Assert.Same(preview, dialog.PreviewImageSource);
            Assert.Same(preview, dialog.PreviewImage.Source);

            dialog.Close();
        }));

    [Fact]
    public Task Delete_confirmation_invokes_deletion_and_removes_the_final_custom_icon() =>
        _dispatcher.InvokeAsync(() => WithTheme(() =>
        {
            var (viewModel, service) = CreateCustomSelection();
            var selectedIconId = viewModel.SelectedCustomIcon!.IconId;
            var dialog = new CustomIconDeleteConfirmationWindow(
                viewModel.SelectedCustomIcon.ImageSource);
            dialog.Loaded += (_, _) =>
                dialog.DeleteButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            var confirmationResult = dialog.ShowDialog();
            var deleted = CharacterIconPickerWindow.TryDeleteConfirmedCustomIcon(
                viewModel,
                confirmationResult,
                out var errorMessage);

            Assert.True(confirmationResult);
            Assert.True(deleted, errorMessage);
            Assert.Equal([selectedIconId], service.DeletedIds);
            Assert.Empty(viewModel.CustomIcons);
            Assert.False(viewModel.HasCustomIcons);
            Assert.Null(viewModel.SelectedIcon);
        }));

    [Fact]
    public Task Cancel_and_window_close_do_not_delete_or_change_selection() =>
        _dispatcher.InvokeAsync(() => WithTheme(() =>
        {
            var (cancelViewModel, cancelService) = CreateCustomSelection();
            var selected = cancelViewModel.SelectedIcon;
            var cancelDialog = new CustomIconDeleteConfirmationWindow(selected!.ImageSource);
            cancelDialog.Loaded += (_, _) =>
                cancelDialog.CancelButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            var cancelResult = cancelDialog.ShowDialog();
            Assert.False(CharacterIconPickerWindow.TryDeleteConfirmedCustomIcon(
                cancelViewModel,
                cancelResult,
                out var cancelError));
            Assert.Null(cancelError);
            Assert.Empty(cancelService.DeletedIds);
            Assert.Single(cancelViewModel.CustomIcons);
            Assert.Same(selected, cancelViewModel.SelectedIcon);

            var (closeViewModel, closeService) = CreateCustomSelection();
            var closeSelected = closeViewModel.SelectedIcon;
            var closeDialog = new CustomIconDeleteConfirmationWindow(closeSelected!.ImageSource);
            closeDialog.Loaded += (_, _) => closeDialog.Close();

            var closeResult = closeDialog.ShowDialog();
            Assert.False(CharacterIconPickerWindow.TryDeleteConfirmedCustomIcon(
                closeViewModel,
                closeResult,
                out var closeError));
            Assert.Null(closeError);
            Assert.Empty(closeService.DeletedIds);
            Assert.Single(closeViewModel.CustomIcons);
            Assert.Same(closeSelected, closeViewModel.SelectedIcon);
        }));

    [Fact]
    public Task Built_in_and_assigned_custom_icons_do_not_reach_confirmation() =>
        _dispatcher.InvokeAsync(() =>
        {
            var builtIn = CreateBuiltInIcon();
            var builtInViewModel = new CharacterIconPickerViewModel(
                [builtIn],
                [],
                CharacterIconReference.BuiltIn(builtIn.Id),
                new RecordingCustomIconService());
            Assert.False(builtInViewModel.CanDeleteSelectedCustomIcon);
            Assert.False(CharacterIconPickerWindow.CanShowCustomIconDeleteConfirmation(
                builtInViewModel));

            var (assignedViewModel, assignedService) = CreateCustomSelection(
                _ => ["Alpha Hero"]);
            Assert.True(assignedViewModel.CanDeleteSelectedCustomIcon);
            Assert.Equal(["Alpha Hero"], assignedViewModel.GetSelectedCustomIconAssignments());
            Assert.False(CharacterIconPickerWindow.CanShowCustomIconDeleteConfirmation(
                assignedViewModel));
            Assert.Empty(assignedService.DeletedIds);
            Assert.Single(assignedViewModel.CustomIcons);
        });

    private static (CharacterIconPickerViewModel ViewModel, RecordingCustomIconService Service)
        CreateCustomSelection(Func<string, IReadOnlyList<string>>? assignmentLookup = null)
    {
        var custom = new CustomCharacterIcon
        {
            Id = "custom-" + new string('f', 64),
            ImageSource = new DrawingImage()
        };
        var service = new RecordingCustomIconService(custom);
        var viewModel = new CharacterIconPickerViewModel(
            [CreateBuiltInIcon()],
            [custom],
            CharacterIconReference.Custom(custom.Id),
            service,
            assignmentLookup);
        return (viewModel, service);
    }

    private static BuiltInCharacterIcon CreateBuiltInIcon() =>
        new()
        {
            Id = "default-male-01",
            ImageSource = new DrawingImage()
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

    private sealed class RecordingCustomIconService(params CustomCharacterIcon[] icons)
        : ICustomCharacterIconService
    {
        public List<string> DeletedIds { get; } = [];

        public IReadOnlyList<CustomCharacterIcon> GetIcons() => icons;

        public bool TryGetIcon(string iconId, out CustomCharacterIcon icon)
        {
            icon = icons.FirstOrDefault(candidate => candidate.Id == iconId)!;
            return icon is not null;
        }

        public CustomCharacterIconSaveResult SaveNormalizedIcon(byte[] pngData) =>
            throw new NotSupportedException();

        public CustomCharacterIconDeleteResult DeleteIcon(string iconId)
        {
            DeletedIds.Add(iconId);
            return CustomCharacterIconDeleteResult.Success();
        }
    }
}
