using CoHAnalytics.ReferenceDataGenerator;

namespace CoHAnalytics.Tests.Homecoming;

public sealed class EnhancementIconPogSelectionLiveTests
{
    private static string LiveInstallRoot => LiveInstallTestEnvironment.InstallRoot;
    private const string PowersArchive = @"assets\live\bin_powers.pigg";
    private const string PowersMember = "bin/powers.bin";

    [Theory]
    [InlineData("Boosts.Crafted_Accuracy.Crafted_Accuracy", "Accuracy")]
    [InlineData("Boosts.Crafted_Positrons_Blast_A.Crafted_Positrons_Blast_A", "Damage")]
    [InlineData("Boosts.Crafted_Soulbound_Allegiance_E.Crafted_Soulbound_Allegiance_E", "Damage")]
    [InlineData("Boosts.Attuned_Blistering_Cold_C.Attuned_Blistering_Cold_C", "Damage")]
    [InlineData("Boosts.Crafted_Gladiators_Strike_B.Crafted_Gladiators_Strike_B", "Damage")]
    [Trait("Category", "LiveInstall")]
    public void LiveInstall_RepresentativeBoosts_UseFirstNonOriginBoostTypeForPog(
        string sourceId,
        string expectedPogBoostType)
    {
        var powers = HomecomingPiggMemberReader.ReadMember(
            Path.Combine(LiveInstallRoot, PowersArchive),
            PowersMember);
        var boost = HomecomingPowersBoostDiscoveryReader.TryGetBoost(powers, sourceId);
        Assert.NotNull(boost);
        Assert.Equal([expectedPogBoostType], boost!.NonOriginBoostTypes);
        Assert.Equal(
            expectedPogBoostType,
            EnhancementPogBoostTypeResolver.TryResolvePrimaryBoostType(boost.BoostsAllowed));
    }

    [Fact]
    [Trait("Category", "LiveInstall")]
    public void LiveInstall_ProcBoostRecord_UsesCanonicalBoostsAllowedLeadType()
    {
        var powers = HomecomingPiggMemberReader.ReadMember(
            Path.Combine(LiveInstallRoot, PowersArchive),
            PowersMember);
        var boost = HomecomingPowersBoostDiscoveryReader.TryGetBoost(
            powers,
            "Boosts.Crafted_Soulbound_Allegiance_F.Crafted_Soulbound_Allegiance_F");
        Assert.NotNull(boost);
        Assert.Equal(["Damage"], boost!.NonOriginBoostTypes);
    }
}
