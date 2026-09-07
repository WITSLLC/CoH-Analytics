using System.IO.Compression;
using System.Text;
using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Services;

public sealed class BuiltInCharacterIconServiceTests
{
    [Fact]
    public void Embedded_bundle_exposes_exact_ordered_icon_set()
    {
        var service = new BuiltInCharacterIconService();

        Assert.Equal(14, service.Icons.Count);
        Assert.Equal(BuiltInCharacterIconIds.Ordered, service.Icons.Select(icon => icon.Id));
        Assert.Equal(
            Enumerable.Range(1, 7).Select(index => $"default-male-{index:00}"),
            service.Icons.Take(7).Select(icon => icon.Id));
        Assert.Equal(
            Enumerable.Range(1, 7).Select(index => $"default-female-{index:00}"),
            service.Icons.Skip(7).Select(icon => icon.Id));
        Assert.All(service.Icons, icon => Assert.True(icon.ImageSource.IsFrozen));
    }

    [Fact]
    public void Lookup_returns_cached_image_by_stable_id()
    {
        var service = new BuiltInCharacterIconService();
        var expected = service.Icons[8];

        Assert.True(service.TryGetIcon(expected.Id, out var actual));
        Assert.Same(expected, actual);
        Assert.Same(expected.ImageSource, actual.ImageSource);
        Assert.False(service.TryGetIcon("unknown-icon", out _));
    }

    [Fact]
    public void Missing_or_invalid_bundle_fails_closed()
    {
        var missing = new BuiltInCharacterIconService(() => null);
        var corrupt = new BuiltInCharacterIconService(
            () => new MemoryStream(Encoding.UTF8.GetBytes("not an icon bundle")));

        Assert.Empty(missing.Icons);
        Assert.Empty(corrupt.Icons);
    }

    [Fact]
    public void Embedded_bundle_contains_manifest_and_no_loose_icon_resources()
    {
        var assembly = typeof(BuiltInCharacterIconService).Assembly;
        var resources = assembly.GetManifestResourceNames();

        Assert.Contains(BuiltInCharacterIconService.BundleResourceName, resources);
        Assert.DoesNotContain(resources, name =>
            name.EndsWith("male1.png", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("female1.png", StringComparison.OrdinalIgnoreCase));

        using var stream = assembly.GetManifestResourceStream(BuiltInCharacterIconService.BundleResourceName);
        Assert.NotNull(stream);
        using var archive = new ZipArchive(stream!, ZipArchiveMode.Read);
        Assert.NotNull(archive.GetEntry("manifest.json"));
        Assert.Equal(15, archive.Entries.Count);
    }
}
