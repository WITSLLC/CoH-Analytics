using System.Windows;
using System.Windows.Input;
using CoHAnalytics.ViewModels.CharacterIcons;

namespace CoHAnalytics.Shell;

public partial class CustomCharacterIconEditorWindow : Window
{
    private Point? _lastDragPoint;

    public CustomCharacterIconEditorWindow()
    {
        InitializeComponent();
    }

    public byte[]? NormalizedPng { get; private set; }

    private void EditorViewport_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _lastDragPoint = e.GetPosition(EditorViewport);
        EditorViewport.CaptureMouse();
        e.Handled = true;
    }

    private void EditorViewport_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_lastDragPoint is not Point previous
            || e.LeftButton != MouseButtonState.Pressed
            || DataContext is not CustomCharacterIconEditorViewModel viewModel)
        {
            return;
        }

        var current = e.GetPosition(EditorViewport);
        viewModel.MoveBy(current.X - previous.X, current.Y - previous.Y);
        _lastDragPoint = current;
        e.Handled = true;
    }

    private void EditorViewport_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        EndDrag();
        e.Handled = true;
    }

    private void EditorViewport_OnLostMouseCapture(object sender, MouseEventArgs e) =>
        _lastDragPoint = null;

    private void ApplyButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not CustomCharacterIconEditorViewModel viewModel)
        {
            return;
        }

        try
        {
            NormalizedPng = viewModel.RenderNormalizedPng();
            DialogResult = true;
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                $"The custom icon could not be rendered.\n\n{exception.Message}",
                "Custom Icon",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void CancelButton_OnClick(object sender, RoutedEventArgs e)
    {
        EndDrag();
        DialogResult = false;
    }

    private void EndDrag()
    {
        _lastDragPoint = null;
        if (EditorViewport.IsMouseCaptured)
        {
            EditorViewport.ReleaseMouseCapture();
        }
    }
}
