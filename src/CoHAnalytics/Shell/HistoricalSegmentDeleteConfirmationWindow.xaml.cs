using System.Windows;
using CoHAnalytics.Services;

namespace CoHAnalytics.Shell;

public partial class HistoricalSegmentDeleteConfirmationWindow : Window
{
    public HistoricalSegmentDeleteConfirmationWindow(
        HistoricalSegmentDeleteConfirmationRequest request)
    {
        Request = request ?? throw new ArgumentNullException(nameof(request));
        DataContext = Request;
        InitializeComponent();
    }

    public HistoricalSegmentDeleteConfirmationRequest Request { get; }

    private void DeleteButton_OnClick(object sender, RoutedEventArgs e) =>
        DialogResult = true;

    private void CancelButton_OnClick(object sender, RoutedEventArgs e) =>
        DialogResult = false;
}
