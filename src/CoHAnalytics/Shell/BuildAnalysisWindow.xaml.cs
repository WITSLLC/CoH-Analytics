using System.Windows;

namespace CoHAnalytics.Shell;

public partial class BuildAnalysisWindow : Window
{
    public static readonly DependencyProperty ShowDetailedDescriptionsProperty =
        DependencyProperty.Register(
            nameof(ShowDetailedDescriptions),
            typeof(bool),
            typeof(BuildAnalysisWindow),
            new PropertyMetadata(false));

    public BuildAnalysisWindow()
    {
        InitializeComponent();
    }

    public bool ShowDetailedDescriptions
    {
        get => (bool)GetValue(ShowDetailedDescriptionsProperty);
        set => SetValue(ShowDetailedDescriptionsProperty, value);
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs e) => Close();
}
