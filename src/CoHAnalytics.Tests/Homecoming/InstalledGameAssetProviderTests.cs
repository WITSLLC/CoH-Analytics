using System.Windows.Media;
using CoHAnalytics.Homecoming;
using CoHAnalytics.Models;
using CoHAnalytics.Services;
using CoHAnalytics.Tests.ReferenceDataGenerator;

namespace CoHAnalytics.Tests.Homecoming;

public sealed class InstalledGameAssetProviderTests
{
    public static IEnumerable<object[]> RepresentativeIncarnateIcons =>
    [
        [IncarnateIconIdentity.TryCreateBranchTier("Alpha", "Agility", "Common")!],
        [IncarnateIconIdentity.TryCreateBranchTier("Judgement", "Ion", "Common")!],
        [IncarnateIconIdentity.TryCreateBranchTier("Interface", "Reactive", "Common")!],
        [IncarnateIconIdentity.TryCreateBranchTier("Lore", "Rikti", "Common")!],
        [IncarnateIconIdentity.TryCreateBranchTier("Destiny", "Barrier", "Common")!],
        [IncarnateIconIdentity.TryCreateBranchTier("Hybrid", "Assault", "Common")!]
    ];

    [Fact]
    public void TryResolve_KnownFixtureIcon_DecodesAndCaches()
    {
        using var fixture = InstalledGameArtworkFixture.Create();
        var provider = CreateProvider(fixture.InstallRoot);

        var first = provider.TryResolve("E_ICON_FIXTURE.tga");
        var second = provider.TryResolve("E_ICON_FIXTURE.tga");

        Assert.NotNull(first);
        Assert.Same(first, second);
        Assert.Equal(64, first!.Width);
        Assert.Equal(64, first.Height);
        Assert.True(first.IsFrozen);
    }

    [Fact]
    public void TryResolve_EmptyEnhancementSlot_DecodesCanonicalCreationAsset()
    {
        using var fixture = InstalledGameArtworkFixture.Create();
        var provider = CreateProvider(fixture.InstallRoot);

        var first = provider.TryResolve(EnhancementIconIdentity.EmptySlot);
        var second = provider.TryResolve(EnhancementIconIdentity.EmptySlot);

        Assert.NotNull(first);
        Assert.Same(first, second);
        Assert.Equal(64, first!.Width);
        Assert.Equal(64, first.Height);
        Assert.True(first.IsFrozen);
    }

    [Theory]
    [MemberData(nameof(RepresentativeIncarnateIcons))]
    public void TryResolve_RepresentativeIncarnateIcon_DecodesFromExistingProvider(string iconIdentity)
    {
        using var fixture = InstalledGameArtworkFixture.Create();
        var provider = CreateProvider(fixture.InstallRoot);

        var image = provider.TryResolve(iconIdentity);

        Assert.NotNull(image);
        Assert.Equal(32, image!.Width);
        Assert.Equal(32, image.Height);
        Assert.True(image.IsFrozen);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryResolve_EmptyIdentity_ReturnsNullWithoutThrow(string? iconIdentity)
    {
        using var fixture = InstalledGameArtworkFixture.Create();
        var provider = CreateProvider(fixture.InstallRoot);

        var image = provider.TryResolve(iconIdentity);

        Assert.Null(image);
    }

    [Fact]
    public void TryResolve_MissingMember_ReturnsNullWithoutThrow()
    {
        using var fixture = InstalledGameArtworkFixture.Create();
        var provider = CreateProvider(fixture.InstallRoot);

        var image = provider.TryResolve("E_ICON_DOES_NOT_EXIST.tga");

        Assert.Null(image);
    }

    [Fact]
    public void TryResolve_MissingArchive_ReturnsNullWithoutThrow()
    {
        using var fixture = InstalledGameArtworkFixture.Create(includeArchives: false);
        var provider = CreateProvider(fixture.InstallRoot);

        var image = provider.TryResolve("E_ICON_FIXTURE.tga");

        Assert.Null(image);
    }

    [Fact]
    public void TryResolve_NoInstallation_ReturnsNullWithoutThrow()
    {
        var provider = CreateProvider(installRoot: null);

        var image = provider.TryResolve("E_ICON_FIXTURE.tga");

        Assert.Null(image);
    }

    private static InstalledGameAssetProvider CreateProvider(string? installRoot)
    {
        var settings = new SettingsService(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        var installationService = new HomecomingInstallationService(settings);
        if (installRoot is not null)
        {
            SetCurrentInstallation(
                installationService,
                new HomecomingInstallation
                {
                    InstallRoot = installRoot,
                    LauncherPath = Path.Combine(installRoot, "Homecoming Launcher.exe")
                });
        }

        return new InstalledGameAssetProvider(installationService);
    }

    private static void SetCurrentInstallation(
        HomecomingInstallationService service,
        HomecomingInstallation installation) =>
        typeof(HomecomingInstallationService)
            .GetProperty(nameof(HomecomingInstallationService.CurrentInstallation))!
            .SetValue(service, installation);
}

internal sealed class InstalledGameArtworkFixture : IDisposable
{
    private readonly string _root;

    private InstalledGameArtworkFixture(string root)
    {
        _root = root;
    }

    internal string InstallRoot => _root;

    internal static InstalledGameArtworkFixture Create(bool includeArchives = true)
    {
        var root = Path.Combine(Path.GetTempPath(), "coh-analytics-artwork-fixture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "assets", "live"));
        Directory.CreateDirectory(Path.Combine(root, "assets", "issue24"));

        if (includeArchives)
        {
            var textureMember = HomecomingTextureFixtureBuilder.CreateWrappedBgraTexture(width: 64, height: 64, includeAlpha: true);
            var archive = HomecomingBinaryFixtureBuilder.CreatePigg(
                [
                    ("texture_library/GUI/Icons/Enhancements/E_ICON_FIXTURE.texture", textureMember),
                    ("texture_library/GUI/Icons/Inspirations/Inspiration_FIXTURE.texture", textureMember)
                ]);

            File.WriteAllBytes(Path.Combine(root, "assets", "live", "texture_gui.pigg"), archive);

            var stage2Archive = HomecomingBinaryFixtureBuilder.CreatePigg(
                [
                    (
                        "texture_library/GUI/CREATION/Enhancements/EnhncTray_RingHole.texture",
                        textureMember)
                ]);
            File.WriteAllBytes(Path.Combine(root, "assets", "issue24", "stage2.pigg"), stage2Archive);

            var incarnateTextureMember = HomecomingTextureFixtureBuilder.CreateWrappedBgraTexture(
                width: 32,
                height: 32,
                includeAlpha: true);
            var incarnateMembers = RepresentativeIncarnateMemberNames()
                .Select(memberName =>
                    ($"texture_library/GUI/Icons/Powers/{memberName}.texture", incarnateTextureMember))
                .ToArray();
            var stage1bArchive = HomecomingBinaryFixtureBuilder.CreatePigg(incarnateMembers);
            File.WriteAllBytes(Path.Combine(root, "assets", "issue24", "stage1b.pigg"), stage1bArchive);
        }

        return new InstalledGameArtworkFixture(root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
        }
    }

    private static IEnumerable<string> RepresentativeIncarnateMemberNames() =>
    [
        "Incarnate_Alpha_Agility_Common",
        "Incarnate_Judgement_Ion_Common",
        "Incarnate_Interface_Reactive_Common",
        "Incarnate_Lore_Rikti_Common",
        "Incarnate_Destiny_Barrier_Common",
        "Incarnate_Hybrid_Assault_Common"
    ];
}
