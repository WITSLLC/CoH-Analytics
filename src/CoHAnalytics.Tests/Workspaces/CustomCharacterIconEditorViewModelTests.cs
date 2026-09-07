using System.Windows.Media.Imaging;
using CoHAnalytics.Services;
using CoHAnalytics.ViewModels.CharacterIcons;

namespace CoHAnalytics.Tests.Workspaces;

[Collection(WpfDispatcherCollection.Name)]
public sealed class CustomCharacterIconEditorViewModelTests
{
    private readonly WpfDispatcherFixture _dispatcher;

    public CustomCharacterIconEditorViewModelTests(WpfDispatcherFixture dispatcher)
    {
        _dispatcher = dispatcher;
    }

    [Fact]
    public Task Editor_uses_cover_fit_and_supports_move_scale_and_reset() =>
        _dispatcher.InvokeAsync(() =>
        {
            var bitmap = CoHAnalytics.Tests.Services.CustomCharacterIconImageProcessorTests.CreateSolidBitmap(
                200, 100, 10, 20, 30, 255);
            var viewModel = new CustomCharacterIconEditorViewModel(new CustomCharacterIconSource
            {
                Image = bitmap,
                OriginalPixelWidth = 200,
                OriginalPixelHeight = 100
            });

            Assert.Equal(840, viewModel.SourceDisplayWidth);
            Assert.Equal(420, viewModel.SourceDisplayHeight);
            Assert.Equal(0, viewModel.ScaleSliderValue);
            Assert.Equal(1, viewModel.Scale);
            Assert.Equal("100%", viewModel.ScalePercentage);

            viewModel.ScaleSliderValue = -2;
            Assert.Equal(0.25, viewModel.Scale);
            Assert.Equal("25%", viewModel.ScalePercentage);

            viewModel.ScaleSliderValue = 2;
            Assert.Equal(4, viewModel.Scale);
            Assert.Equal("400%", viewModel.ScalePercentage);
            viewModel.MoveBy(35, -20);
            Assert.Equal(35, viewModel.OffsetX);
            Assert.Equal(-20, viewModel.OffsetY);

            viewModel.ResetCommand.Execute(null);
            Assert.Equal(0, viewModel.ScaleSliderValue);
            Assert.Equal(1, viewModel.Scale);
            Assert.Equal("100%", viewModel.ScalePercentage);
            Assert.Equal(0, viewModel.OffsetX);
            Assert.Equal(0, viewModel.OffsetY);
        });

    [Fact]
    public Task Approved_silhouette_is_packaged_as_a_runtime_resource() =>
        _dispatcher.InvokeAsync(() =>
        {
            var image = new BitmapImage(new Uri(
                "pack://application:,,,/CoHAnalytics;component/Assets/Images/CharacterIcons/gallery_silhouette.png",
                UriKind.Absolute));
            Assert.Equal(1024, image.PixelWidth);
            Assert.Equal(1024, image.PixelHeight);
        });

    [Fact]
    public Task Discarding_editor_state_does_not_write_or_assign_any_icon() =>
        _dispatcher.InvokeAsync(() =>
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                "coh-analytics-editor-cancel",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var settings = new SettingsService(root);
            var store = new CustomCharacterIconService(settings, Path.Combine(root, "User Icons"));
            var bitmap = CoHAnalytics.Tests.Services.CustomCharacterIconImageProcessorTests.CreateSolidBitmap(
                32, 32, 1, 2, 3, 255);

            _ = new CustomCharacterIconEditorViewModel(new CustomCharacterIconSource
            {
                Image = bitmap,
                OriginalPixelWidth = 32,
                OriginalPixelHeight = 32
            });

            Assert.Empty(store.GetIcons());
            Assert.False(Directory.Exists(Path.Combine(root, "User Icons")));
        });
}
