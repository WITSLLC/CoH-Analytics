using CoHAnalytics.ReferenceDataGenerator;

namespace CoHAnalytics.Tests.ReferenceDataGenerator;

public sealed class HomecomingInspirationSourceMetadataTests
{
    [Theory]
    [InlineData("Inspirations.Large_Dual.Protected", "Large_Dual", "Large", "Dual")]
    [InlineData("Inspirations.Small_Dual.Revitalize", "Small_Dual", "Small", "Dual")]
    [InlineData("Inspirations.Medium_Team.Insight", "Medium_Team", "Medium", "Team")]
    [InlineData("Inspirations.Super_Team_Dual.Mighty", "Super_Team_Dual", "Super", "TeamDual")]
    [InlineData("Inspirations.Special.Special", "Special", null, "Special/Event")]
    [InlineData("Inspirations.Holiday.Holiday", "Holiday", null, "Special/Event")]
    [InlineData("Inspirations.Anniversary.Anniversary_Defense", "Anniversary", null, "Special/Event")]
    [InlineData("Inspirations.Small.Small", "Small", "Small", "Single")]
    public void ParseMetadata_UsesAuthoritativeSourceIdentity(
        string sourceId,
        string expectedCategory,
        string? expectedTier,
        string expectedForm)
    {
        var category = HomecomingInspirationSourceMetadata.ParseHomecomingCategory(sourceId);
        Assert.Equal(expectedCategory, category);
        Assert.Equal(expectedTier, HomecomingInspirationSourceMetadata.ParseStandardTier(category));
        Assert.Equal(expectedForm, HomecomingInspirationSourceMetadata.ParseInspirationForm(category));
    }

    [Fact]
    public void ParseHomecomingCategory_RejectsNonInspirationSourceId()
    {
        var exception = Assert.Throws<HomecomingPowersException>(() =>
            HomecomingInspirationSourceMetadata.ParseHomecomingCategory("Boosts.Crafted_Damage.Crafted_Damage"));

        Assert.Contains("Inspirations", exception.Message, StringComparison.Ordinal);
    }
}
