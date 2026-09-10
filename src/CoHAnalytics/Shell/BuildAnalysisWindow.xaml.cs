using System.Windows;

namespace CoHAnalytics.Shell;

public partial class BuildAnalysisWindow : Window
{
    public BuildAnalysisWindow()
    {
        InitializeComponent();
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs e) => Close();
}
