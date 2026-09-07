using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Services;

[Collection(WpfDispatcherCollection.Name)]
public sealed class CustomCharacterIconServiceTests
{
    private readonly WpfDispatcherFixture _dispatcher;

    public CustomCharacterIconServiceTests(WpfDispatcherFixture dispatcher)
    {
        _dispatcher = dispatcher;
    }

    [Fact]
    public Task Saved_normalized_icon_is_content_addressed_deduplicated_and_restart_safe() =>
        _dispatcher.InvokeAsync(() =>
        {
            var root = CreateDirectory();
            var settings = new SettingsService(root);
            var iconsDirectory = Path.Combine(root, "User Icons");
            var service = new CustomCharacterIconService(settings, iconsDirectory);
            var source = CustomCharacterIconImageProcessorTests.CreateSolidBitmap(
                20, 20, 12, 34, 56, 255);
            var png = CustomCharacterIconImageProcessor.RenderNormalizedPng(
                source, 1, 0, 0, 420);

            var first = service.SaveNormalizedIcon(png);
            var second = service.SaveNormalizedIcon(png);

            Assert.True(first.IsSuccess, first.ErrorMessage);
            Assert.True(second.IsSuccess, second.ErrorMessage);
            Assert.Equal(first.Icon!.Id, second.Icon!.Id);
            Assert.Matches("^custom-[0-9a-f]{64}$", first.Icon.Id);
            Assert.Single(Directory.GetFiles(iconsDirectory, "custom-*.png"));

            var restarted = new CustomCharacterIconService(settings, iconsDirectory);
            Assert.True(restarted.TryGetIcon(first.Icon.Id, out var resolved));
            Assert.Equal(first.Icon.Id, resolved.Id);
            Assert.Single(restarted.GetIcons());

            var deleted = restarted.DeleteIcon(first.Icon.Id);
            Assert.True(deleted.IsSuccess, deleted.ErrorMessage);
            Assert.Empty(restarted.GetIcons());
            Assert.False(File.Exists(Path.Combine(iconsDirectory, first.Icon.Id + ".png")));
            Assert.True(restarted.DeleteIcon(first.Icon.Id).IsSuccess);
        });

    [Fact]
    public Task Invalid_normalized_data_and_corrupt_managed_files_do_not_resolve() =>
        _dispatcher.InvokeAsync(() =>
        {
            var root = CreateDirectory();
            var settings = new SettingsService(root);
            var iconsDirectory = Path.Combine(root, "User Icons");
            var service = new CustomCharacterIconService(settings, iconsDirectory);

            var failed = service.SaveNormalizedIcon([1, 2, 3, 4]);
            Assert.False(failed.IsSuccess);

            Directory.CreateDirectory(iconsDirectory);
            var corruptId = "custom-" + new string('a', 64);
            File.WriteAllText(Path.Combine(iconsDirectory, corruptId + ".png"), "corrupt");
            Assert.False(service.TryGetIcon(corruptId, out _));
            Assert.Empty(service.GetIcons());
            Assert.False(service.DeleteIcon(corruptId).IsSuccess);
            Assert.True(File.Exists(Path.Combine(iconsDirectory, corruptId + ".png")));
        });

    private static string CreateDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "coh-analytics-custom-icon-store",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
