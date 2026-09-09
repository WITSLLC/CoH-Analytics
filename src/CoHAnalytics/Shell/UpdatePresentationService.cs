using System.Windows;
using CoHAnalytics.Updates;

namespace CoHAnalytics.Shell;

public sealed class UpdatePresentationService(Window owner, Func<bool> canPresent) : IUpdatePresentation
{
    public bool CanPresent => canPresent();

    public UpdateUserAction ShowUpdateAvailable(UpdateAvailablePresentation presentation)
    {
        var window = new UpdateAvailableWindow(presentation)
        {
            Owner = owner
        };
        _ = window.ShowDialog();
        return window.SelectedAction;
    }

    public void ShowCurrent(string message) =>
        MessageBox.Show(owner, message, "Check for Updates", MessageBoxButton.OK, MessageBoxImage.Information);

    public void ShowUnableToCheck(string message) =>
        MessageBox.Show(owner, message, "Check for Updates", MessageBoxButton.OK, MessageBoxImage.Warning);

    public void ShowUnableToOpenLink() =>
        MessageBox.Show(
            owner,
            "CoH Analytics couldn’t open the selected GitHub link.",
            "Open Update Link",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
}
