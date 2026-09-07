using System.Windows;
using CoHAnalytics.Services;

namespace CoHAnalytics.Shell;

public partial class AboutWindow : Window
{
    public AboutWindow(
        ILocalDocumentService localDocumentService,
        IExternalUriService externalUriService)
    {
        InitializeComponent();
        DataContext = new AboutWindowViewModel(localDocumentService, externalUriService);
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
