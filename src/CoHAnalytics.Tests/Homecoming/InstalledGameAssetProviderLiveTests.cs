using System.Diagnostics;
using System.Windows.Media;
using CoHAnalytics.Homecoming;
using CoHAnalytics.Models;
using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Homecoming;

public sealed class InstalledGameAssetProviderLiveTests
{
    private static string LiveInstallRoot => LiveInstallTestEnvironment.InstallRoot;

    public static IEnumerable<object[]> RepresentativeIcons =>
    [
        ["E_ICON_GEN_ACCURACY_01.tga", "texture_library/GUI/Icons/Enhancements/E_ICON_GEN_ACCURACY_01.texture"],
        ["E_ICON_PositronsBlast.tga", "texture_library/GUI/Icons/Enhancements/E_ICON_PositronsBlast.texture"],
        ["E_ICON_SoulboundAllegiance.tga", "texture_library/GUI/Icons/Enhancements/E_ICON_SoulboundAllegiance.texture"]
    ];

    [Theory]
    [MemberData(nameof(RepresentativeIcons))]
    [Trait("Category", "LiveInstall")]
    public void LiveInstall_RepresentativeIcon_ResolvesFrozenBitmap(string iconIdentity, string expectedMemberPath)
    {
        var provider = CreateLiveProvider();
        var archiveSet = HomecomingTextureArtworkArchiveSet.TryOpen(LiveInstallRoot);
        Assert.NotNull(archiveSet);
        Assert.True(archiveSet!.TryReadTextureMember(iconIdentity, out _, out var resolvedMemberPath));
        Assert.Equal(expectedMemberPath, resolvedMemberPath, ignoreCase: true);

        var firstStopwatch = Stopwatch.StartNew();
        var first = provider.TryResolve(iconIdentity);
        firstStopwatch.Stop();

        var secondStopwatch = Stopwatch.StartNew();
        var second = provider.TryResolve(iconIdentity);
        secondStopwatch.Stop();

        Assert.NotNull(first);
        Assert.Same(first, second);
        Assert.Equal(64, first!.Width);
        Assert.Equal(64, first.Height);
        Assert.Equal(System.Windows.Media.PixelFormats.Bgra32, ((System.Windows.Media.Imaging.BitmapSource)first).Format);
        Assert.True(first.IsFrozen);
        Assert.True(secondStopwatch.Elapsed <= firstStopwatch.Elapsed);
    }

    [Fact]
    [Trait("Category", "LiveInstall")]
    public void LiveInstall_MissingIdentity_ReturnsNullWithoutThrow()
    {
        var provider = CreateLiveProvider();

        var image = provider.TryResolve("E_ICON_DOES_NOT_EXIST.tga");

        Assert.Null(image);
    }

    private static InstalledGameAssetProvider CreateLiveProvider()
    {
        var settings = new SettingsService(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        var installationService = new HomecomingInstallationService(settings);
        typeof(HomecomingInstallationService)
            .GetProperty(nameof(HomecomingInstallationService.CurrentInstallation))!
            .SetValue(
                installationService,
                new HomecomingInstallation
                {
                    InstallRoot = LiveInstallRoot,
                    LauncherPath = Path.Combine(LiveInstallRoot, "Homecoming Launcher.exe")
                });
        return new InstalledGameAssetProvider(installationService);
    }
}
