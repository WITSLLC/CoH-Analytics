using CoHAnalytics.Homecoming;

namespace CoHAnalytics.Tests.Homecoming;

public sealed class IncarnateIconIdentityTests
{
    [Theory]
    [InlineData("Alpha", "Agility", "Common", "Incarnate_Alpha_Agility_Common.tga")]
    [InlineData("Judgement", "Ion", "Uncommon", "Incarnate_Judgement_Ion_Uncommon.tga")]
    [InlineData("Interface", "Reactive", "Rare", "Incarnate_Interface_Reactive_Rare.tga")]
    [InlineData("Lore", "Rikti", "VeryRare", "Incarnate_Lore_Rikti_VeryRare.tga")]
    [InlineData("Destiny", "Barrier", "Common", "Incarnate_Destiny_Barrier_Common.tga")]
    [InlineData("Hybrid", "Assault", "Rare", "Incarnate_Hybrid_Assault_Rare.tga")]
    public void TryCreateBranchTier_UsesCanonicalClientNamingPattern(
        string slotToken,
        string branchToken,
        string tierToken,
        string expected)
    {
        var identity = IncarnateIconIdentity.TryCreateBranchTier(slotToken, branchToken, tierToken);

        Assert.Equal(expected, identity);
    }

    [Theory]
    [InlineData(null, "Agility", "Common")]
    [InlineData("Alpha", "", "Common")]
    [InlineData("Alpha", "Agility", "two words")]
    [InlineData("Alpha", "../Agility", "Common")]
    public void TryCreateBranchTier_InvalidToken_ReturnsNull(
        string? slotToken,
        string? branchToken,
        string? tierToken)
    {
        Assert.Null(IncarnateIconIdentity.TryCreateBranchTier(slotToken, branchToken, tierToken));
    }
}
