using CoHAnalytics.Homecoming;

namespace CoHAnalytics.Tests.Homecoming;

public sealed class HomecomingIconMemberPathNormalizerTests
{
    [Theory]
    [InlineData("E_ICON_GEN_ACCURACY_01.tga", "E_ICON_GEN_ACCURACY_01")]
    [InlineData("E_ICON_PositronsBlast.tga", "E_ICON_PositronsBlast")]
    [InlineData("  E_ICON_SoulboundAllegiance.tga  ", "E_ICON_SoulboundAllegiance")]
    public void NormalizeBaseName_StripsTgaExtension(string iconIdentity, string expectedBaseName)
    {
        var actual = HomecomingIconMemberPathNormalizer.NormalizeBaseName(iconIdentity);

        Assert.Equal(expectedBaseName, actual);
    }

    [Fact]
    public void CreateMemberPathCandidates_UsesProvenGuiAndLowercasePaths()
    {
        var candidates = HomecomingIconMemberPathNormalizer.CreateMemberPathCandidates("E_ICON_GEN_ACCURACY_01.tga");

        Assert.Equal(
            [
                "texture_library/GUI/Icons/Enhancements/E_ICON_GEN_ACCURACY_01.texture",
                "texture_library/gui/icons/enhancements/e_icon_gen_accuracy_01.texture",
                "texture_library/GUI/Icons/Badges/E_ICON_GEN_ACCURACY_01.texture",
                "texture_library/gui/icons/badges/e_icon_gen_accuracy_01.texture",
                "texture_library/GUI/Icons/Inspirations/E_ICON_GEN_ACCURACY_01.texture",
                "texture_library/gui/icons/inspirations/e_icon_gen_accuracy_01.texture"
            ],
            candidates);
    }

    [Fact]
    public void CreateMemberPathCandidates_EmptyEnhancementSlot_UsesCanonicalCreationPath()
    {
        var candidates = HomecomingIconMemberPathNormalizer.CreateMemberPathCandidates(
            EnhancementIconIdentity.EmptySlot);

        Assert.Equal(
            [
                "texture_library/GUI/CREATION/Enhancements/EnhncTray_RingHole.texture",
                "texture_library/gui/creation/enhancements/enhnctray_ringhole.texture"
            ],
            candidates);
    }

    [Theory]
    [InlineData("badge_tourist_01.tga", "badge_tourist_01")]
    public void CreateMemberPathCandidates_BadgePaths_UseBadgesDirectory(string iconIdentity, string expectedBaseName)
    {
        var candidates = HomecomingIconMemberPathNormalizer.CreateMemberPathCandidates(iconIdentity);

        Assert.Equal(expectedBaseName, HomecomingIconMemberPathNormalizer.NormalizeBaseName(iconIdentity));
        Assert.Contains($"texture_library/GUI/Icons/Badges/{expectedBaseName}.texture", candidates);
        Assert.Contains($"texture_library/gui/icons/badges/{expectedBaseName.ToLowerInvariant()}.texture", candidates);
    }

    [Theory]
    [InlineData("Inspiration_Dual_Def_Res_Lvl_3.tga", "Inspiration_Dual_Def_Res_Lvl_3")]
    public void CreateMemberPathCandidates_InspirationPaths_UseInspirationsDirectory(
        string iconIdentity,
        string expectedBaseName)
    {
        var candidates = HomecomingIconMemberPathNormalizer.CreateMemberPathCandidates(iconIdentity);

        Assert.Equal(expectedBaseName, HomecomingIconMemberPathNormalizer.NormalizeBaseName(iconIdentity));
        Assert.Contains($"texture_library/GUI/Icons/Inspirations/{expectedBaseName}.texture", candidates);
        Assert.Contains(
            $"texture_library/gui/icons/inspirations/{expectedBaseName.ToLowerInvariant()}.texture",
            candidates);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateMemberPathCandidates_EmptyIdentity_ReturnsNoCandidates(string? iconIdentity)
    {
        var candidates = HomecomingIconMemberPathNormalizer.CreateMemberPathCandidates(iconIdentity!);

        Assert.Empty(candidates);
    }
}
