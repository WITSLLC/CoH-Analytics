using System.Windows;
using System.Windows.Controls;
using CoHAnalytics.Services;
using CoHAnalytics.Shell;

namespace CoHAnalytics.Tests.Shell;

[Collection(WpfDispatcherCollection.Name)]
public sealed class HistoricalSegmentDeleteConfirmationWindowTests
{
    private readonly WpfDispatcherFixture _dispatcher;

    public HistoricalSegmentDeleteConfirmationWindowTests(WpfDispatcherFixture dispatcher)
    {
        _dispatcher = dispatcher;
    }

    [Fact]
    public Task Confirmation_displays_human_readable_segment_context_without_side_effects() =>
        _dispatcher.InvokeAsync(() => WithTheme(() =>
        {
            var request = CreateRequest();
            var dialog = new HistoricalSegmentDeleteConfirmationWindow(request);

            dialog.UpdateLayout();

            Assert.Same(request, dialog.Request);
            Assert.Equal("Dawn's Vanguard", dialog.CharacterText.Text);
            Assert.Equal("TestAccount", dialog.AccountText.Text);
            Assert.Equal("August 24, 2026 12:14 AM", dialog.DateTimeText.Text);
            dialog.Close();
        }));

    [Fact]
    public Task Delete_returns_true_only_after_explicit_delete_action() =>
        _dispatcher.InvokeAsync(() => WithTheme(() =>
        {
            var dialog = new HistoricalSegmentDeleteConfirmationWindow(CreateRequest());
            dialog.Loaded += (_, _) =>
                dialog.DeleteButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.True(dialog.ShowDialog());
        }));

    [Fact]
    public Task Cancel_and_window_close_return_nonconfirming_results() =>
        _dispatcher.InvokeAsync(() => WithTheme(() =>
        {
            var cancelDialog = new HistoricalSegmentDeleteConfirmationWindow(CreateRequest());
            cancelDialog.Loaded += (_, _) =>
                cancelDialog.CancelButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(cancelDialog.ShowDialog());

            var closeDialog = new HistoricalSegmentDeleteConfirmationWindow(CreateRequest());
            closeDialog.Loaded += (_, _) => closeDialog.Close();
            Assert.NotEqual(true, closeDialog.ShowDialog());
        }));

    private static HistoricalSegmentDeleteConfirmationRequest CreateRequest() =>
        new()
        {
            CharacterLabel = "Dawn's Vanguard",
            AccountLabel = "TestAccount",
            DateTimeLabel = "August 24, 2026 12:14 AM"
        };

    private static void WithTheme(Action action)
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
            action();
        }
        finally
        {
            Application.Current.Resources.MergedDictionaries.Remove(theme);
        }
    }
}
