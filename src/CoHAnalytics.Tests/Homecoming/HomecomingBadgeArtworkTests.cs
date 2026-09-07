using CoHAnalytics.Homecoming;

namespace CoHAnalytics.Tests.Homecoming;

public sealed class HomecomingBadgeArtworkTests
{
    private static string LiveInstallRoot => LiveInstallTestEnvironment.InstallRoot;

    [Fact]
    public void CreateMemberPathCandidates_IncludesBadgeTexturePaths()
    {
        var candidates = HomecomingIconMemberPathNormalizer.CreateMemberPathCandidates("badge_tourist_01.tga");

        Assert.Contains("texture_library/GUI/Icons/Badges/badge_tourist_01.texture", candidates);
        Assert.Contains("texture_library/gui/icons/badges/badge_tourist_01.texture", candidates);
    }

    [Fact]
    [Trait("Category", "LiveInstall")]
    public void LiveInstall_BadgeIconMemberExistsWhenInstallPresent()
    {
        var archiveSet = HomecomingTextureArtworkArchiveSet.TryOpen(LiveInstallRoot);
        Assert.NotNull(archiveSet);
        Assert.True(archiveSet!.TryReadTextureMember("badge_tourist_01.tga", out _, out var memberPath));
        Assert.Contains("/Badges/", memberPath, StringComparison.OrdinalIgnoreCase);
    }
}
