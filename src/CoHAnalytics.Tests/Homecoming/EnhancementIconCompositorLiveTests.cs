using System.Diagnostics;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CoHAnalytics.Homecoming;
using CoHAnalytics.Models;
using CoHAnalytics.ReferenceData;
using CoHAnalytics.ReferenceDataGenerator;
using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Homecoming;

public sealed class EnhancementIconCompositorLiveTests
{
    private static string LiveInstallRoot => LiveInstallTestEnvironment.InstallRoot;
    private const string PowersArchive = @"assets\live\bin_powers.pigg";
    private const string PowersMember = "bin/powers.bin";

    public static IEnumerable<object[]> RepresentativeCompositions =>
    [
        [
            "Invention: Accuracy",
            "Boosts.Crafted_Accuracy.Crafted_Accuracy",
            ReferenceEnhancementFamily.CraftedInvention,
            "Crafted",
            "Regular",
            null,
            "E_ICON_GEN_ACCURACY_01.tga",
            EnhancementFrameClass.Invention,
            "Accuracy",
            "E_POG_ACCURACY"
        ],
        [
            "Positron's Blast: Accuracy/Damage",
            "Boosts.Crafted_Positrons_Blast_A.Crafted_Positrons_Blast_A",
            null,
            "Crafted",
            "Regular",
            "ECRare",
            "E_ICON_PositronsBlast.tga",
            EnhancementFrameClass.Rare,
            "Damage",
            "E_POG_DAMAGE"
        ],
        [
            "Soulbound Allegiance: Damage",
            "Boosts.Crafted_Soulbound_Allegiance_A.Crafted_Soulbound_Allegiance_A",
            null,
            "Crafted",
            "Superior",
            "ECVeryRare",
            "E_ICON_SoulboundAllegiance.tga",
            EnhancementFrameClass.Superior,
            "Damage",
            "E_POG_DAMAGE"
        ],
        [
            "Armageddon: Damage",
            "Boosts.Crafted_Armageddon_A.Crafted_Armageddon_A",
            null,
            "Crafted",
            "Regular",
            "ECVeryRare",
            "E_ICON_Armageddon.tga",
            EnhancementFrameClass.VeryRare,
            "Damage",
            "E_POG_DAMAGE"
        ],
        [
            "Gladiator's Strike: Damage/Recharge",
            "Boosts.Crafted_Gladiators_Strike_B.Crafted_Gladiators_Strike_B",
            null,
            "Crafted",
            "Regular",
            "ECPVP",
            "E_ICON_GladiatorsStrike.tga",
            EnhancementFrameClass.PvP,
            "Damage",
            "E_POG_DAMAGE"
        ],
        [
            "Blistering Cold: Accuracy/Damage/Recharge",
            "Boosts.Attuned_Blistering_Cold_C.Attuned_Blistering_Cold_C",
            null,
            "Attuned",
            "Regular",
            "ECWinter",
            "E_ICON_Blistering_Cold.tga",
            EnhancementFrameClass.Attuned,
            "Damage",
            "E_POG_DAMAGE"
        ],
        [
            "Superior Blistering Cold: Accuracy/Damage",
            "Boosts.Superior_Attuned_Blistering_Cold_A.Superior_Attuned_Blistering_Cold_A",
            null,
            "Superior_Attuned",
            "Superior",
            "ECSWinter",
            "E_ICON_Blistering_Cold.tga",
            EnhancementFrameClass.SuperiorAttuned,
            "Damage",
            "E_POG_DAMAGE"
        ],
        [
            "Blaster's Wrath: Accuracy/Damage",
            "Boosts.Attuned_Blasters_Wrath_A.Attuned_Blasters_Wrath_A",
            null,
            "Attuned",
            "Regular",
            "ECATO",
            "E_ICON_BlastersWrath.tga",
            EnhancementFrameClass.Attuned,
            "Damage",
            "E_POG_DAMAGE"
        ],
        [
            "Superior Blaster's Wrath: Accuracy/Damage",
            "Boosts.Superior_Attuned_Superior_Blasters_Wrath_A.Superior_Attuned_Superior_Blasters_Wrath_A",
            null,
            "Superior_Attuned",
            "Superior",
            "ECSATO",
            "E_ICON_BlastersWrath.tga",
            EnhancementFrameClass.SuperiorAttuned,
            "Damage",
            "E_POG_DAMAGE"
        ],
        [
            "Positron's Blast: Accuracy/Damage (Attuned)",
            "Boosts.Attuned_Positrons_Blast_A.Attuned_Positrons_Blast_A",
            null,
            "Attuned",
            "Regular",
            "ECRare",
            "E_ICON_PositronsBlast.tga",
            EnhancementFrameClass.Attuned,
            "Damage",
            "E_POG_DAMAGE"
        ]
    ];

    [Theory]
    [MemberData(nameof(RepresentativeCompositions))]
    [Trait("Category", "LiveInstall")]
    public void LiveInstall_RepresentativeEnhancement_ComposesCompleteIcon(
        string label,
        string sourceId,
        ReferenceEnhancementFamily? enhancementFamily,
        string sourceForm,
        string variant,
        string? rarityCode,
        string iconIdentity,
        EnhancementFrameClass expectedFrameClass,
        string expectedPogBoostType,
        string expectedPogIdentity)
    {
        var powers = HomecomingPiggMemberReader.ReadMember(
            Path.Combine(LiveInstallRoot, PowersArchive),
            PowersMember);
        var boost = HomecomingPowersBoostDiscoveryReader.TryGetBoost(powers, sourceId);
        Assert.NotNull(boost);

        var pogBoostType = EnhancementPogBoostTypeResolver.TryResolvePrimaryBoostType(boost!.BoostsAllowed);
        Assert.Equal(expectedPogBoostType, pogBoostType);
        Assert.Equal(expectedPogIdentity, EnhancementPogIdentity.TryResolveIdentity(pogBoostType));

        var frameClass = EnhancementFrameClassResolver.TryResolve(
            enhancementFamily,
            sourceForm,
            variant,
            rarityCode);
        Assert.Equal(expectedFrameClass, frameClass);

        var request = EnhancementIconCompositionRequest.TryCreate(
            iconIdentity,
            pogBoostType,
            enhancementFamily,
            sourceForm,
            variant,
            rarityCode);
        Assert.NotNull(request);

        var compositor = new EnhancementIconCompositor(CreateLiveProvider());
        var firstStopwatch = Stopwatch.StartNew();
        var first = compositor.TryCompose(request!);
        firstStopwatch.Stop();

        var secondStopwatch = Stopwatch.StartNew();
        var second = compositor.TryCompose(request!);
        secondStopwatch.Stop();

        Assert.NotNull(first);
        Assert.Same(first, second);
        Assert.Equal(64, first!.Width);
        Assert.Equal(64, first.Height);
        Assert.True(first.IsFrozen);
        Assert.True(secondStopwatch.Elapsed <= firstStopwatch.Elapsed);

        Console.WriteLine(
            $"{label}: frame={expectedFrameClass}, pog={expectedPogIdentity}, first={firstStopwatch.Elapsed.TotalMilliseconds:F1}ms, cached={secondStopwatch.Elapsed.TotalMilliseconds:F1}ms");
    }

    [Fact]
    [Trait("Category", "LiveInstall")]
    public void LiveInstall_SupportedFrameAndPogAssets_ResolveFromInstalledGame()
    {
        var provider = CreateLiveProvider();
        Assert.NotNull(provider.TryResolve("E_orgin_invention_L.tga"));
        Assert.NotNull(provider.TryResolve("e_orgin_rare_r.tga"));
        Assert.NotNull(provider.TryResolve("E_POG_DAMAGE.tga"));
        Assert.NotNull(provider.TryResolve("E_origin_attune_L.tga"));
        Assert.NotNull(provider.TryResolve("E_origin_superiorattune_R.tga"));
        Assert.NotNull(provider.TryResolve("e_orgin_superior_l.tga"));
    }

    [Theory]
    [MemberData(nameof(RepresentativeCompositions))]
    [Trait("Category", "LiveInstall")]
    public void LiveInstall_RepresentativeEnhancement_FramePixelsSurviveComposition(
        string label,
        string sourceId,
        ReferenceEnhancementFamily? enhancementFamily,
        string sourceForm,
        string variant,
        string? rarityCode,
        string iconIdentity,
        EnhancementFrameClass expectedFrameClass,
        string expectedPogBoostType,
        string expectedPogIdentity)
    {
        var request = EnhancementIconCompositionRequest.TryCreate(
            iconIdentity,
            expectedPogBoostType,
            enhancementFamily,
            sourceForm,
            variant,
            rarityCode);
        Assert.NotNull(request);

        var provider = CreateLiveProvider();
        var compositor = new EnhancementIconCompositor(provider);
        var composed = (BitmapSource?)compositor.TryCompose(request!);
        Assert.NotNull(composed);

        var frameClass = EnhancementFrameClassResolver.TryResolve(
            enhancementFamily,
            sourceForm,
            variant,
            rarityCode);
        Assert.True(EnhancementFrameIdentity.TryResolveHalves(frameClass!.Value, out var leftId, out var rightId));

        var left = (BitmapSource)provider.TryResolve(leftId)!;
        var right = (BitmapSource)provider.TryResolve(rightId)!;
        var survival = EnhancementIconCompositionGeometryAssertions.MeasureFrameColorSurvival(left, right, composed!);

        Assert.True(
            survival >= GetMinimumFrameColorSurvival(expectedFrameClass),
            $"{label} frame color survival {survival:P1} below threshold for {expectedFrameClass}.");
    }

    [Theory]
    [MemberData(nameof(RepresentativeCompositions))]
    [Trait("Category", "LiveInstall")]
    public void LiveInstall_RepresentativeEnhancement_OverlayPlacementMatchesWrapperMetadata(
        string label,
        string sourceId,
        ReferenceEnhancementFamily? enhancementFamily,
        string sourceForm,
        string variant,
        string? rarityCode,
        string iconIdentity,
        EnhancementFrameClass expectedFrameClass,
        string expectedPogBoostType,
        string expectedPogIdentity)
    {
        var provider = CreateLiveProvider();
        Assert.True(provider.TryReadTextureMember($"{expectedPogIdentity}.tga", out var pogBytes));
        Assert.True(provider.TryReadTextureMember(iconIdentity, out var innerBytes));
        Assert.True(HomecomingTextureMemberDecoder.TryReadOverlayPlacement(pogBytes, out var pogPlacement));
        Assert.True(HomecomingTextureMemberDecoder.TryReadOverlayPlacement(innerBytes, out var innerPlacement));
        Assert.Equal(8, pogPlacement.OffsetX);
        Assert.Equal(8, pogPlacement.OffsetY);
        Assert.Equal(9, innerPlacement.OffsetX);
        Assert.Equal(9, innerPlacement.OffsetY);
    }

    private static double GetMinimumFrameColorSurvival(EnhancementFrameClass frameClass) =>
        frameClass switch
        {
            EnhancementFrameClass.VeryRare => 0.85,
            EnhancementFrameClass.Attuned => 0.80,
            EnhancementFrameClass.SuperiorAttuned => 0.80,
            _ => 0.55
        };

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

    public static IEnumerable<object[]> VeryRareSetIoCompositions =>
    [
        ["Absolute Amazement", "SET-00181", "E_ICON_AbsoluteAmazement.tga", "Stun", "E_POG_STUN_DURATION"],
        ["Ragnarok", "SET-00051", "E_ICON_Ragnarok.tga", "Damage", "E_POG_DAMAGE"],
        ["Fortunata Hypnosis", "SET-00176", "E_ICON_FortunataHypnosis.tga", "Confuse", "E_POG_CONFUSION_DURATION"],
        ["Gravitational Anchor", "SET-00166", "E_ICON_GravitationalAnchor.tga", "Immobilize", "E_POG_IMMOBILIZATION_DURATION"],
        ["Hecatomb", "SET-00011", "E_ICON_Hecatomb.tga", "Damage", "E_POG_DAMAGE"],
        ["Coercive Persuasion", "SET-00147", "E_ICON_CoersivePersuasion.tga", "Confuse", "E_POG_CONFUSION_DURATION"],
        ["Unbreakable Constraint", "SET-00158", "E_ICON_UnbreakableConstraint.tga", "Hold", "E_POG_HOLD_DURATION"]
    ];

    [Theory]
    [MemberData(nameof(VeryRareSetIoCompositions))]
    [Trait("Category", "LiveInstall")]
    public void LiveInstall_VeryRareSetIoMembers_ComposeWithMeaningfulInnerArtwork(
        string setName,
        string setId,
        string iconIdentity,
        string pogBoostType,
        string pogIdentity)
    {
        var catalog = ItemReferenceCatalogFactory.LoadEmbeddedProduction();
        Assert.True(catalog.TryGetEnhancementSetById(setId, out var set));

        var request = EnhancementIconCompositionRequest.TryCreate(
            iconIdentity,
            pogBoostType,
            null,
            "Crafted",
            EnhancementIconCompositionSupport.TryResolveSetIoFrameVariant(catalog, set),
            set.RarityCode);
        Assert.NotNull(request);
        Assert.Equal(EnhancementFrameClass.Superior, request.FrameClass);

        var provider = CreateLiveProvider();
        var compositor = new EnhancementIconCompositor(provider);
        var composed = (BitmapSource?)compositor.TryCompose(request);
        Assert.NotNull(composed);
        Assert.NotNull(provider.TryResolve(pogIdentity));

        var colorfulness = EnhancementIconCompositionGeometryAssertions.MeasureCenterColorfulness(composed!);
        Assert.True(
            colorfulness >= 80,
            $"{setName} composed center colorfulness {colorfulness:F1} is below the inner-artwork threshold.");
    }

    [Theory]
    [InlineData("Analyze Weakness", "E_ICON_AnalyzeWeakness.tga", "Accuracy", "E_POG_ACCURACY", EnhancementFrameClass.Rare)]
    [InlineData("Razzle Dazzle", "E_ICON_RazzleDazzle.tga", "Buff_ToHit", "E_POG_BUFF_TO_HIT", EnhancementFrameClass.Rare)]
    [Trait("Category", "LiveInstall")]
    public void LiveInstall_OrdinaryRareSets_KeepRareFrameComposition(
        string label,
        string iconIdentity,
        string pogBoostType,
        string pogIdentity,
        EnhancementFrameClass expectedFrameClass)
    {
        var request = EnhancementIconCompositionRequest.TryCreate(
            iconIdentity,
            pogBoostType,
            null,
            "Crafted",
            null,
            "ECRare");
        Assert.NotNull(request);
        Assert.Equal(expectedFrameClass, request.FrameClass);

        var provider = CreateLiveProvider();
        var compositor = new EnhancementIconCompositor(provider);
        var composed = compositor.TryCompose(request);
        Assert.NotNull(composed);
        Assert.NotNull(provider.TryResolve(pogIdentity));
    }
}
