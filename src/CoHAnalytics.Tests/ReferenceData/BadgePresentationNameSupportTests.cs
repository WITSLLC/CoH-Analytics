using CoHAnalytics.ReferenceData;

namespace CoHAnalytics.Tests.ReferenceData;

public sealed class BadgePresentationNameSupportTests
{
    [Fact]
    public void Neutral_presentation_uses_combined_catalog_name_for_alignment_variants()
    {
        var catalog = ItemReferenceCatalogFactory.LoadEmbeddedProduction();
        Assert.True(catalog.TryGetBadgeByHomecomingSourceId("SkywayCityTour1", out var badge));

        Assert.Equal(
            "Purifier / Defiler",
            BadgePresentationNameSupport.GetNeutralDisplayName(catalog, badge));
    }

    [Theory]
    [InlineData("Adamant", "Adamant / Ironman / Ironwoman")]
    [InlineData("BrickstownExplorer", "Zig Warden / King of the Zig / Queen of the Zig")]
    [InlineData("Level30", "Defender of Truth / Wiseguy / Wisegal")]
    [InlineData("SafeguardSecurityExpert", "Security Expert / Inside Man / Inside Woman")]
    [InlineData("Sensation", "Sensation / Mr. Big / Ms. Big")]
    [InlineData("StMartialExplorer", "Johnny's Ex-Best Friend / Johnny's Go To Guy / Johnny's Go To Gal")]
    public void Neutral_presentation_expands_gender_expressions_without_selecting_a_variant(
        string sourceId,
        string expected)
    {
        var catalog = ItemReferenceCatalogFactory.LoadEmbeddedProduction();
        Assert.True(catalog.TryGetBadgeByHomecomingSourceId(sourceId, out var badge));

        Assert.Equal(expected, BadgePresentationNameSupport.GetNeutralDisplayName(catalog, badge));
    }

    [Fact]
    public void All_production_gender_expressions_have_clean_neutral_presentation()
    {
        var catalog = ItemReferenceCatalogFactory.LoadEmbeddedProduction();
        var badges = catalog.GetBadges()
            .Where(badge => badge.HeroName.Contains("{Hero.gender=", StringComparison.Ordinal)
                || badge.VillainName.Contains("{Hero.gender=", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(46, badges.Length);
        Assert.All(
            badges,
            badge => Assert.DoesNotContain(
                "{Hero.gender=",
                BadgePresentationNameSupport.GetNeutralDisplayName(catalog, badge),
                StringComparison.Ordinal));
    }
}
