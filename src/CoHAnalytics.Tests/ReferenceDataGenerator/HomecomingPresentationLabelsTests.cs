using CoHAnalytics.ReferenceDataGenerator;

namespace CoHAnalytics.Tests.ReferenceDataGenerator;

public sealed class HomecomingPresentationLabelsTests
{
    [Theory]
    [InlineData("Recharge", "Recharge Reduction")]
    [InlineData("EnduranceDiscount", "Endurance Reduction")]
    [InlineData("Res_Damage", "Damage Resistance")]
    [InlineData("Accuracy", "Accuracy")]
    [InlineData("Damage", "Damage")]
    [InlineData("Hold", "Hold")]
    public void Common_IO_presentation_labels_map_structural_keys(string key, string expected)
    {
        Assert.Equal(expected, HomecomingPresentationLabels.TryGetCommonIoBoostTypeDisplayText(key));
    }

    [Fact]
    public void Unknown_common_IO_structural_key_has_no_guessed_label()
    {
        Assert.Null(HomecomingPresentationLabels.TryGetCommonIoBoostTypeDisplayText("NotARealBoostType"));
        Assert.Null(
            HomecomingPresentationLabels.TryGetCommonIoBoostTypeDisplayText(
                "Accuracy+Hamidon"));
    }

    [Fact]
    public void ECToHitDeBuff_uses_approved_presentation_fallback()
    {
        Assert.Equal(
            "To-Hit Debuff",
            HomecomingPresentationLabels.ResolveCategoryDisplayText("ECToHitDeBuff", null));
        Assert.Equal(
            "To-Hit Debuff",
            HomecomingPresentationLabels.ResolveCategoryDisplayText(
                "ECToHitDeBuff",
                "Category: ShouldNotWin"));
    }

    [Fact]
    public void Category_message_prefix_is_stripped_for_presentation_only()
    {
        Assert.Equal(
            "Melee",
            HomecomingPresentationLabels.ResolveCategoryDisplayText("ECMelee", "Category: Melee"));
        Assert.Equal(
            "ECMelee",
            HomecomingPresentationLabels.ResolveCategoryDisplayText("ECMelee", null));
    }
}
