using CoHAnalytics.ReferenceData;
using CoHAnalytics.ReferenceDataGenerator;

namespace CoHAnalytics.Tests.ReferenceData;

/// <summary>
/// Phase-1 verification that canonical Homecoming booster math reproduces observed controls
/// before production resolver changes rely on it.
/// </summary>
public sealed class EnhancementHelpBoosterVerificationTests
{
    private static string LiveInstallRoot => LiveInstallTestEnvironment.InstallRoot;
    private const string BinPiggRelative = @"assets\live\bin.pigg";

    [Fact]
    [Trait("Category", "LiveInstall")]
    public void Canonical_booster_multipliers_match_boost_effect_boosters_bin()
    {
        Assert.Equal(
            EnhancementHelpResolverBoosterMultipliers.Plus0ThroughPlus5,
            new[] { 1.0f, 1.05f, 1.10f, 1.15f, 1.20f, 1.25f });

        var boosters = HomecomingBoostEffectCurveDiscoveryReader.Read(
            HomecomingPiggMemberReader.ReadMember(
                Path.Combine(LiveInstallRoot, BinPiggRelative),
                "bin/boost_effect_boosters.bin"));

        Assert.Equal(EnhancementHelpResolverBoosterMultipliers.Plus0ThroughPlus5.Length, boosters.Count);
        for (var index = 0; index < boosters.Count; index++)
        {
            Assert.Equal(
                EnhancementHelpResolverBoosterMultipliers.Plus0ThroughPlus5[index],
                boosters[index],
                5);
        }
    }

    [Fact]
    public void Control_A_Crafted_Accuracy_L50_through_L55_match_observed_Homecoming_values()
    {
        var inputs = ItemReferenceCatalogFactory.LoadEmbeddedProductionResolverInputs();
        Assert.True(inputs.Catalog.IsLoaded);
        Assert.NotNull(inputs.NamedTables);

        Assert.True(inputs.Catalog.TryResolve("Invention: Accuracy", out var resolution));
        var variant = resolution.Item.SourceVariants.Single(source =>
            string.Equals(
                source.HomecomingSourceId,
                "Boosts.Crafted_Accuracy.Crafted_Accuracy",
                StringComparison.Ordinal));
        var table = inputs.NamedTables!["Melee_Boosts_33"];
        var nativeCapIndex = EnhancementHelpResolverBoosterMultipliers.NativeCapPresentationLevel - 1;
        var baseRawPercentage = table[nativeCapIndex] * variant.Effects[0].Scale * 100f;

        string[] observed = ["42.4", "44.5", "46.6", "48.7", "50.9", "53"];
        for (var boosterCount = 0; boosterCount < observed.Length; boosterCount++)
        {
            var calculated = EnhancementHelpScaleFormatter.FormatDisplayPercentage(
                baseRawPercentage * EnhancementHelpResolverBoosterMultipliers.Plus0ThroughPlus5[boosterCount]);
            Assert.Equal(observed[boosterCount], calculated);
        }
    }

    [Fact]
    public void Control_B_Soulbound_Damage_Endurance_L55_matches_observed_41_4_for_both_aspects()
    {
        var inputs = ItemReferenceCatalogFactory.LoadEmbeddedProductionResolverInputs();
        Assert.True(inputs.Catalog.IsLoaded);
        Assert.NotNull(inputs.NamedTables);

        Assert.True(
            inputs.Catalog.TryResolve(
                "Soulbound Allegiance: Damage/Endurance Reduction",
                out var resolution));
        var variant = resolution.Item.SourceVariants.Single(source =>
            string.Equals(
                source.HomecomingSourceId,
                "Boosts.Crafted_Soulbound_Allegiance_E.Crafted_Soulbound_Allegiance_E",
                StringComparison.Ordinal));
        var table = inputs.NamedTables!["Melee_Boosts_33"];
        var nativeCapIndex = EnhancementHelpResolverBoosterMultipliers.NativeCapPresentationLevel - 1;

        foreach (var effect in variant.Effects)
        {
            var baseRawPercentage = table[nativeCapIndex] * effect.Scale * 100f;
            var boostedRawPercentage = baseRawPercentage
                * EnhancementHelpResolverBoosterMultipliers.Plus0ThroughPlus5[5];
            Assert.Equal("41.4", EnhancementHelpScaleFormatter.FormatDisplayPercentage(boostedRawPercentage));
        }

        var l50DamageRaw = table[nativeCapIndex] * variant.Effects[0].Scale * 100f;
        Assert.Equal("33.1", EnhancementHelpScaleFormatter.FormatDisplayPercentage(l50DamageRaw));
    }
}
