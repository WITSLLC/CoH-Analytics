using System.Windows.Controls;

namespace CoHAnalytics.Shell;

public partial class ShellView : UserControl
{
    public ShellView()
    {
        InitializeComponent();
    }

    private void WorkspaceScrollViewer_OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, WorkspaceScrollViewer))
        {
            return;
        }

        WorkspaceLogoScrollTransform.Y = -e.VerticalOffset;
    }
}
