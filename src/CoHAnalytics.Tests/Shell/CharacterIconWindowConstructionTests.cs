using CoHAnalytics.Shell;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CoHAnalytics.Services;
using CoHAnalytics.ViewModels.CharacterIcons;

namespace CoHAnalytics.Tests.Shell;

[Collection(WpfDispatcherCollection.Name)]
public sealed class CharacterIconWindowConstructionTests
{
    private readonly WpfDispatcherFixture _dispatcher;

    public CharacterIconWindowConstructionTests(WpfDispatcherFixture dispatcher)
    {
        _dispatcher = dispatcher;
    }

    [Fact]
    public Task Picker_and_editor_windows_load_all_runtime_resources() =>
        _dispatcher.InvokeAsync(() =>
        {
            var theme = new ResourceDictionary
            {
                Source = new Uri(
                    "pack://application:,,,/CoHAnalytics;component/Themes/Hero/HeroTheme.xaml",
                    UriKind.Absolute)
            };
            Application.Current.Resources.MergedDictionaries.Add(theme);
            try
            {
                var picker = new CharacterIconPickerWindow();
                var editor = new CustomCharacterIconEditorWindow();

                Assert.NotNull(picker.Content);
                Assert.NotNull(editor.Content);

                picker.Close();
                editor.Close();
            }
            finally
            {
                Application.Current.Resources.MergedDictionaries.Remove(theme);
            }
        });

    [Fact]
    public Task Picker_gallery_has_two_explicit_shared_scrolling_rows() =>
        _dispatcher.InvokeAsync(() =>
        {
            var theme = new ResourceDictionary
            {
                Source = new Uri(
                    "pack://application:,,,/CoHAnalytics;component/Themes/Hero/HeroTheme.xaml",
                    UriKind.Absolute)
            };
            Application.Current.Resources.MergedDictionaries.Add(theme);
            var picker = new CharacterIconPickerWindow
            {
                DataContext = new CharacterIconPickerViewModel(
                    BuiltInCharacterIconIds.Ordered.Select(id => new BuiltInCharacterIcon
                    {
                        Id = id,
                        ImageSource = new DrawingImage()
                    }).ToArray(),
                    currentIconId: null)
            };
            try
            {
                picker.Show();
                picker.UpdateLayout();

                var gallery = Assert.IsType<ScrollViewer>(picker.FindName("BuiltInGalleryScrollViewer"));
                var list = Assert.IsType<ListBox>(picker.FindName("BuiltInGalleryList"));
                Assert.Equal(20, list.Items.Count);
                Assert.Equal(ScrollBarVisibility.Auto, gallery.HorizontalScrollBarVisibility);
                Assert.Equal(ScrollBarVisibility.Disabled, gallery.VerticalScrollBarVisibility);
                Assert.True(gallery.ScrollableWidth > 0);
                var horizontalScrollBar = Assert.Single(
                    Descendants<ScrollBar>(gallery),
                    scrollBar => scrollBar.Orientation == Orientation.Horizontal && scrollBar.Maximum > 0);
                Assert.True(horizontalScrollBar.ActualWidth > gallery.ViewportWidth * 0.9);
                Assert.True(horizontalScrollBar.ActualHeight >= 10);
                var panel = Assert.Single(Descendants<UniformGrid>(list));
                Assert.Equal(10, panel.Columns);
                Assert.Equal(2, panel.Rows);

                var defaultScrollableWidth = gallery.ScrollableWidth;
                var wheel = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120)
                {
                    RoutedEvent = UIElement.PreviewMouseWheelEvent
                };
                CharacterIconPickerWindow.ScrollGalleryHorizontally(gallery, wheel);
                gallery.UpdateLayout();
                Assert.True(wheel.Handled);
                Assert.True(gallery.HorizontalOffset > 0);
                Assert.Equal(0, gallery.VerticalOffset);

                picker.SizeToContent = SizeToContent.Manual;
                picker.Width = 800;
                picker.UpdateLayout();
                picker.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
                Assert.True(
                    gallery.ScrollableWidth > defaultScrollableWidth,
                    $"Window={picker.ActualWidth}, viewport={gallery.ViewportWidth}, scrollable={gallery.ScrollableWidth}, default={defaultScrollableWidth}");
            }
            finally
            {
                picker.Close();
                Application.Current.Resources.MergedDictionaries.Remove(theme);
            }
        });

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in Descendants<T>(child))
            {
                yield return descendant;
            }
        }
    }
}
