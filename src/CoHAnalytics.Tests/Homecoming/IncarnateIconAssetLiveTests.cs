using CoHAnalytics.Homecoming;
using CoHAnalytics.Models;
using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Homecoming;

public sealed class IncarnateIconAssetLiveTests
{
    private static string LiveInstallRoot => LiveInstallTestEnvironment.InstallRoot;

    public static IEnumerable<object[]> RepresentativeIcons =>
    [
        [IncarnateIconIdentity.AlphaBlank, "texture_library/GUI/Icons/Powers/Incarnate_Alpha_Blank.texture"],
        [
            IncarnateIconIdentity.TryCreateBranchTier("Alpha", "Agility", "Common")!,
            "texture_library/GUI/Icons/Powers/Incarnate_Alpha_Agility_Common.texture"
        ],
        [
            IncarnateIconIdentity.TryCreateBranchTier("Judgement", "Ion", "Uncommon")!,
            "texture_library/GUI/Icons/Powers/Incarnate_Judgement_Ion_Uncommon.texture"
        ],
        [
            IncarnateIconIdentity.TryCreateBranchTier("Interface", "Reactive", "Rare")!,
            "texture_library/GUI/Icons/Powers/Incarnate_Interface_Reactive_Rare.texture"
        ],
        [
            IncarnateIconIdentity.TryCreateBranchTier("Lore", "Rikti", "VeryRare")!,
            "texture_library/GUI/Icons/Powers/Incarnate_Lore_Rikti_VeryRare.texture"
        ],
        [
            IncarnateIconIdentity.TryCreateBranchTier("Destiny", "Barrier", "Common")!,
            "texture_library/GUI/Icons/Powers/Incarnate_Destiny_Barrier_Common.texture"
        ],
        [
            IncarnateIconIdentity.TryCreateBranchTier("Hybrid", "Assault", "Rare")!,
            "texture_library/GUI/Icons/Powers/Incarnate_Hybrid_Assault_Rare.texture"
        ],
        [
            IncarnateIconIdentity.TryCreateBranchTier("Judgement", "Mighty", "VeryRare")!,
            "texture_library/gui/icons/powers/incarnate_judgement_mighty_veryrare.texture"
        ],
        [
            IncarnateIconIdentity.TryCreateBranchTier("Lore", "Demons", "Rare")!,
            "texture_library/gui/icons/powers/incarnate_lore_demons_rare.texture"
        ]
    ];

    [Theory]
    [MemberData(nameof(RepresentativeIcons))]
    [Trait("Category", "LiveInstall")]
    public void LiveInstall_IncarnateIcon_ResolvesCanonicalMember(
        string iconIdentity,
        string expectedMemberPath)
    {
        var archiveSet = HomecomingTextureArtworkArchiveSet.TryOpen(LiveInstallRoot);
        Assert.NotNull(archiveSet);
        Assert.True(archiveSet!.TryReadTextureMember(iconIdentity, out _, out var resolvedMemberPath));
        Assert.Equal(expectedMemberPath, resolvedMemberPath, ignoreCase: true);

        var image = CreateLiveProvider().TryResolve(iconIdentity);

        Assert.NotNull(image);
        Assert.Equal(32, image!.Width);
        Assert.Equal(32, image.Height);
        Assert.True(image.IsFrozen);
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
