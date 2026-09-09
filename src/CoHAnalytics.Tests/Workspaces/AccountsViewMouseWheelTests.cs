using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CoHAnalytics.Workspaces;

namespace CoHAnalytics.Tests.Workspaces;

[Collection(WpfDispatcherCollection.Name)]
public sealed class AccountsViewMouseWheelTests(WpfDispatcherFixture dispatcher)
{
    [Theory]
    [InlineData(120, 280)]
    [InlineData(-120, 520)]
    public Task Badge_wheel_routes_in_both_directions_to_main_page_scroll_viewer(
        int delta,
        double expectedOffset) =>
        dispatcher.InvokeAsync(() =>
        {
            var badgePageScrollViewer = new ScrollViewer
            {
                Height = 100,
                Content = new Border { Height = 100 }
            };
            var pageContent = new Grid { Height = 1200 };
            pageContent.Children.Add(badgePageScrollViewer);
            var mainPageScrollViewer = new ScrollViewer
            {
                Height = 300,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = pageContent
            };
            var window = new Window
            {
                Width = 600,
                Height = 400,
                ShowInTaskbar = false,
                Content = mainPageScrollViewer
            };

            try
            {
                window.Show();
                window.UpdateLayout();
                mainPageScrollViewer.ScrollToVerticalOffset(400);
                mainPageScrollViewer.UpdateLayout();
                var mouseWheel = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta)
                {
                    RoutedEvent = UIElement.PreviewMouseWheelEvent
                };

                AccountsView.RouteBadgeMouseWheelToMainPage(badgePageScrollViewer, mouseWheel);
                mainPageScrollViewer.UpdateLayout();

                Assert.True(mouseWheel.Handled);
                Assert.Equal(expectedOffset, mainPageScrollViewer.VerticalOffset);
                Assert.Equal(0, mainPageScrollViewer.HorizontalOffset);
            }
            finally
            {
                window.Close();
            }
        });

    [Fact]
    public Task Badge_wheel_is_left_unhandled_without_a_main_page_scroll_viewer() =>
        dispatcher.InvokeAsync(() =>
        {
            var badgePageScrollViewer = new ScrollViewer();
            var mouseWheel = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120)
            {
                RoutedEvent = UIElement.PreviewMouseWheelEvent
            };

            AccountsView.RouteBadgeMouseWheelToMainPage(badgePageScrollViewer, mouseWheel);

            Assert.False(mouseWheel.Handled);
        });
}
