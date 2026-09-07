using System.Windows;
using System.Windows.Controls;

namespace CoHAnalytics.Shell;

public partial class ApplicationTitleBarView : UserControl
{
    public ApplicationTitleBarView()
    {
        InitializeComponent();
    }

    public bool IsPointOverApplicationMenu(Point screenPoint)
    {
        if (!ApplicationMenu.IsLoaded || ApplicationMenu.ActualWidth <= 0)
        {
            return false;
        }

        var topLeft = ApplicationMenu.PointToScreen(new Point(0, 0));
        var bottomRight = ApplicationMenu.PointToScreen(
            new Point(ApplicationMenu.ActualWidth, ApplicationMenu.ActualHeight));
        var bounds = new Rect(topLeft, bottomRight);
        return bounds.Contains(screenPoint);
    }
}
