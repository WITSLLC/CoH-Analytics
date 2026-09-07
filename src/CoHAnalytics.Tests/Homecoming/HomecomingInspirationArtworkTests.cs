using System.Windows.Media;
using CoHAnalytics.Homecoming;
using CoHAnalytics.Models;
using CoHAnalytics.ReferenceData;
using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Homecoming;

public sealed class HomecomingInspirationArtworkTests
{
    private static string LiveInstallRoot => LiveInstallTestEnvironment.InstallRoot;

    public static IEnumerable<object[]> RepresentativeInspirationIcons =>
    [
        [
            "Inspiration_Dual_Def_Res_Lvl_3.tga",
            "texture_library/GUI/Icons/Inspirations/Inspiration_Dual_Def_Res_Lvl_3.texture"
        ],
        [
            "Inspiration_Dual_Health_End_Lvl_1.tga",
            "texture_library/GUI/Icons/Inspirations/Inspiration_Dual_Health_End_Lvl_1.texture"
        ],
        [
            "Inspiration_Team_Acc_Lvl_2.tga",
            "texture_library/GUI/Icons/Inspirations/Inspiration_Team_Acc_Lvl_2.texture"
        ],
        [
            "Inspiration_Team_Dual_Dmg_Acc_Lvl_1.tga",
            "texture_library/GUI/Icons/Inspirations/Inspiration_Team_Dual_Dmg_Acc_Lvl_1.texture"
        ],
        [
            "Inspiration_Present.tga",
            "texture_library/GUI/Icons/Inspirations/Inspiration_Present.texture"
        ],
        [
            "Inspiration_Anniversary.tga",
            "texture_library/GUI/Icons/Inspirations/Inspiration_Anniversary.texture"
        ],
        [
            "Inspiration_LevelShift_Lvl_4.tga",
            "texture_library/GUI/Icons/Inspirations/Inspiration_LevelShift_Lvl_4.texture"
        ]
    ];

    [Fact]
    public void CreateMemberPathCandidates_IncludesInspirationTexturePaths()
    {
        var candidates = HomecomingIconMemberPathNormalizer.CreateMemberPathCandidates(
            "Inspiration_Dual_Def_Res_Lvl_3.tga");

        Assert.Contains(
            "texture_library/GUI/Icons/Inspirations/Inspiration_Dual_Def_Res_Lvl_3.texture",
            candidates);
        Assert.Contains(
            "texture_library/gui/icons/inspirations/inspiration_dual_def_res_lvl_3.texture",
            candidates);
    }

    [Fact]
    public void CreateMemberPathCandidates_EnhancementPathsRemainFirst()
    {
        var candidates = HomecomingIconMemberPathNormalizer.CreateMemberPathCandidates("E_ICON_GEN_ACCURACY_01.tga");

        Assert.Equal(
            "texture_library/GUI/Icons/Enhancements/E_ICON_GEN_ACCURACY_01.texture",
            candidates[0]);
        Assert.Equal(
            "texture_library/gui/icons/enhancements/e_icon_gen_accuracy_01.texture",
            candidates[1]);
    }

    [Fact]
    public void CreateMemberPathCandidates_BadgePathsRemainBeforeInspirations()
    {
        var candidates = HomecomingIconMemberPathNormalizer.CreateMemberPathCandidates("badge_tourist_01.tga");

        Assert.Equal(
            "texture_library/GUI/Icons/Badges/badge_tourist_01.texture",
            candidates[2]);
        Assert.Equal(
            "texture_library/gui/icons/badges/badge_tourist_01.texture",
            candidates[3]);
        Assert.Equal(
            "texture_library/GUI/Icons/Inspirations/badge_tourist_01.texture",
            candidates[4]);
    }

    [Fact]
    public void FixtureInstall_InspirationIcon_DecodesAndCaches()
    {
        using var fixture = InstalledGameArtworkFixture.Create();
        var provider = CreateProvider(fixture.InstallRoot);

        var first = provider.TryResolve("Inspiration_FIXTURE.tga");
        var second = provider.TryResolve("Inspiration_FIXTURE.tga");

        Assert.NotNull(first);
        Assert.Same(first, second);
        Assert.Equal(64, first!.Width);
        Assert.Equal(64, first.Height);
        Assert.True(first.IsFrozen);
    }

    [Fact]
    public void FixtureInstall_UnknownInspirationIcon_ReturnsNullWithoutThrow()
    {
        using var fixture = InstalledGameArtworkFixture.Create();
        var provider = CreateProvider(fixture.InstallRoot);

        var image = provider.TryResolve("Inspiration_DOES_NOT_EXIST.tga");

        Assert.Null(image);
    }

    [Fact]
    public void FixtureInstall_SharedInspirationIconIdentity_ReturnsSameCachedImage()
    {
        using var fixture = InstalledGameArtworkFixture.Create();
        var provider = CreateProvider(fixture.InstallRoot);

        var first = provider.TryResolve("Inspiration_FIXTURE.tga");
        var second = provider.TryResolve("Inspiration_FIXTURE.tga");

        Assert.NotNull(first);
        Assert.Same(first, second);
    }

    [Theory]
    [MemberData(nameof(RepresentativeInspirationIcons))]
    [Trait("Category", "LiveInstall")]
    public void LiveInstall_RepresentativeInspirationIcon_ResolvesFrozenBitmap(
        string iconIdentity,
        string expectedMemberPath)
    {
        var archiveSet = HomecomingTextureArtworkArchiveSet.TryOpen(LiveInstallRoot);
        Assert.NotNull(archiveSet);
        Assert.True(archiveSet!.TryReadTextureMember(iconIdentity, out _, out var resolvedMemberPath));
        Assert.Equal(expectedMemberPath, resolvedMemberPath, ignoreCase: true);

        var provider = CreateLiveProvider();
        var image = provider.TryResolve(iconIdentity);

        Assert.NotNull(image);
        Assert.Equal(32, image!.Width);
        Assert.Equal(32, image.Height);
        Assert.True(image.IsFrozen);
    }

    [Fact]
    [Trait("Category", "LiveInstall")]
    public void LiveInstall_InspirationTextureMemberLocatedInKnownArtworkArchives()
    {
        const string memberPath =
            "texture_library/GUI/Icons/Inspirations/Inspiration_Dual_Def_Res_Lvl_3.texture";
        var locatedArchives = new List<string>();
        foreach (var relativeArchivePath in HomecomingTextureArtworkArchives.RelativeArchivePaths)
        {
            var archivePath = HomecomingTextureArtworkArchives.ResolveArchivePath(
                LiveInstallRoot,
                relativeArchivePath);
            var archive = HomecomingPiggArchiveIndex.TryOpen(archivePath);
            if (archive is not null && archive.TryResolveMemberPath(memberPath, out _))
            {
                locatedArchives.Add(relativeArchivePath);
            }
        }

        Assert.Equal(
            [Path.Combine("assets", "issue24", "stage2.pigg")],
            locatedArchives);
    }

    [Fact]
    [Trait("Category", "LiveInstall")]
    public void LiveInstall_AllProductionInspirationIcons_Resolve()
    {
        var catalog = (ItemReferenceCatalog)ItemReferenceCatalogFactory.LoadEmbeddedProduction();
        var inspirations = catalog.DebugItems.Values
            .Where(item => item.Family == ReferenceItemFamily.Inspiration)
            .OrderBy(item => item.CatalogItemId, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(96, inspirations.Count);

        var provider = CreateLiveProvider();
        var archiveSet = HomecomingTextureArtworkArchiveSet.TryOpen(LiveInstallRoot);
        Assert.NotNull(archiveSet);

        var unresolved = new List<string>();
        foreach (var inspiration in inspirations)
        {
            Assert.False(string.IsNullOrWhiteSpace(inspiration.Icon));
            if (!archiveSet!.TryReadTextureMember(inspiration.Icon!, out _, out _))
            {
                unresolved.Add($"{inspiration.CatalogItemId} {inspiration.CurrentDisplayName} {inspiration.Icon}");
                continue;
            }

            var image = provider.TryResolve(inspiration.Icon);
            Assert.NotNull(image);
        }

        Assert.Empty(unresolved);
    }

    [Fact]
    [Trait("Category", "LiveInstall")]
    public void LiveInstall_ProductionInspirationIconIdentityCounts()
    {
        var catalog = (ItemReferenceCatalog)ItemReferenceCatalogFactory.LoadEmbeddedProduction();
        var inspirations = catalog.DebugItems.Values
            .Where(item => item.Family == ReferenceItemFamily.Inspiration)
            .ToList();

        Assert.Equal(96, inspirations.Count);
        Assert.Equal(86, inspirations.Select(item => item.Icon).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    private static InstalledGameAssetProvider CreateProvider(string installRoot)
    {
        var settings = new SettingsService(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        var installationService = new HomecomingInstallationService(settings);
        SetCurrentInstallation(
            installationService,
            new HomecomingInstallation
            {
                InstallRoot = installRoot,
                LauncherPath = Path.Combine(installRoot, "Homecoming Launcher.exe")
            });
        return new InstalledGameAssetProvider(installationService);
    }

    private static InstalledGameAssetProvider CreateLiveProvider() =>
        CreateProvider(LiveInstallRoot);

    private static void SetCurrentInstallation(
        HomecomingInstallationService service,
        HomecomingInstallation installation) =>
        typeof(HomecomingInstallationService)
            .GetProperty(nameof(HomecomingInstallationService.CurrentInstallation))!
            .SetValue(service, installation);
}
