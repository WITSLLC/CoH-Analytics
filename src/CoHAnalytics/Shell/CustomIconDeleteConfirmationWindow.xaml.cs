using System.Windows;
using System.Windows.Media;

namespace CoHAnalytics.Shell;

public partial class CustomIconDeleteConfirmationWindow : Window
{
    public CustomIconDeleteConfirmationWindow(ImageSource previewImageSource)
    {
        PreviewImageSource = previewImageSource
            ?? throw new ArgumentNullException(nameof(previewImageSource));
        DataContext = this;
        InitializeComponent();
    }

    public ImageSource PreviewImageSource { get; }

    private void DeleteButton_OnClick(object sender, RoutedEventArgs e) =>
        DialogResult = true;

    private void CancelButton_OnClick(object sender, RoutedEventArgs e) =>
        DialogResult = false;
}
