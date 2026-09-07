using System.Windows;
using CoHAnalytics.Services;

namespace CoHAnalytics.Shell;

public partial class SupportWindow : Window
{
    public SupportWindow(IExternalUriService externalUriService)
    {
        InitializeComponent();
        DataContext = new SupportWindowViewModel(externalUriService);
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
