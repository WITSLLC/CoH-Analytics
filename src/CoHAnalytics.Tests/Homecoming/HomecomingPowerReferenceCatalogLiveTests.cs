using CoHAnalytics.Homecoming;
using CoHAnalytics.Models;
using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Homecoming;

public sealed class HomecomingPowerReferenceCatalogLiveTests
{
    private static readonly Lazy<HomecomingInstallationService> InstallationService =
        new(CreateInstallationService);

    private static readonly Lazy<HomecomingPowerReferenceCatalog> Catalog =
        new(() => new HomecomingPowerReferenceCatalog(InstallationService.Value));

    private static readonly Lazy<InstalledGameAssetProvider> AssetProvider =
        new(() => new InstalledGameAssetProvider(InstallationService.Value));

    [Theory]
    [MemberData(
        nameof(HomecomingPowerReferenceCatalogTests.BlueDevilPowers),
        MemberType = typeof(HomecomingPowerReferenceCatalogTests))]
    [Trait("Category", "LiveInstall")]
    public void LiveInstall_BlueDevilPower_ResolvesCanonicalPresentationAndIcon(
        string categoryId,
        string powersetId,
        string powerId,
        string expectedPowersetDisplayName,
        string expectedPowerDisplayName,
        string expectedIconIdentity,
        bool expectedAutoIssued,
        bool expectedFree,
        HomecomingPowerType expectedPowerType)
    {
        Assert.True(Catalog.Value.TryResolve(categoryId, powersetId, powerId, out var power));
        Assert.Equal(expectedPowersetDisplayName, power.PowersetDisplayName);
        Assert.Equal(expectedPowerDisplayName, power.PowerDisplayName);
        Assert.Equal(expectedIconIdentity, power.IconIdentity);
        Assert.Equal(expectedAutoIssued, power.IsAutoIssued);
        Assert.Equal(expectedFree, power.IsFree);
        Assert.Equal(expectedPowerType, power.PowerType);

        var image = AssetProvider.Value.TryResolve(power.IconIdentity);
        Assert.NotNull(image);
        Assert.Equal(32, image!.Width);
        Assert.Equal(32, image.Height);
        Assert.True(image.IsFrozen);
    }

    [Fact]
    [Trait("Category", "LiveInstall")]
    public void LiveInstall_PowerIconPresentInLiveAndBaseArchives_UsesLiveMember()
    {
        var archiveSet = HomecomingTextureArtworkArchiveSet.TryOpen(LiveInstallTestEnvironment.InstallRoot);

        Assert.NotNull(archiveSet);
        Assert.True(archiveSet!.TryReadTextureMember("Sword_Hack.tga", out _, out var memberPath));
        Assert.Equal(
            "texture_library/gui/icons/powers/sword_hack.texture",
            memberPath,
            ignoreCase: false);
    }

    private static HomecomingInstallationService CreateInstallationService()
    {
        var settings = new SettingsService(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        var installationService = new HomecomingInstallationService(settings);
        typeof(HomecomingInstallationService)
            .GetProperty(nameof(HomecomingInstallationService.CurrentInstallation))!
            .SetValue(
                installationService,
                new HomecomingInstallation
                {
                    InstallRoot = LiveInstallTestEnvironment.InstallRoot,
                    LauncherPath = Path.Combine(
                        LiveInstallTestEnvironment.InstallRoot,
                        "Homecoming Launcher.exe")
                });
        return installationService;
    }
}
