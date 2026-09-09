using System.Windows;
using CoHAnalytics.Updates;

namespace CoHAnalytics.Shell;

public partial class UpdateAvailableWindow : Window
{
    public UpdateAvailableWindow(UpdateAvailablePresentation presentation)
    {
        DataContext = new UpdateAvailableWindowViewModel(presentation);
        InitializeComponent();
    }

    public UpdateUserAction SelectedAction { get; private set; } = UpdateUserAction.Later;

    private void DownloadButton_OnClick(object sender, RoutedEventArgs e)
    {
        SelectedAction = UpdateUserAction.DownloadPackage;
        Close();
    }

    private void ViewReleaseButton_OnClick(object sender, RoutedEventArgs e)
    {
        SelectedAction = UpdateUserAction.ViewRelease;
        Close();
    }

    private void LaterButton_OnClick(object sender, RoutedEventArgs e)
    {
        SelectedAction = UpdateUserAction.Later;
        Close();
    }
}
