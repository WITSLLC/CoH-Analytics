using System.Windows;
using CoHAnalytics.Services;

namespace CoHAnalytics.Shell;

public sealed class HistoricalSegmentDeleteConfirmationService
    : IHistoricalSegmentDeleteConfirmationService
{
    public bool ConfirmDelete(HistoricalSegmentDeleteConfirmationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var dialog = new HistoricalSegmentDeleteConfirmationWindow(request);
        if (Application.Current?.MainWindow is { IsVisible: true } owner)
        {
            dialog.Owner = owner;
        }

        return dialog.ShowDialog() == true;
    }
}
