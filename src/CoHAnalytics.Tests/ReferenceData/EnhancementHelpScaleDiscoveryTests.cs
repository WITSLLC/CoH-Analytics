using System.Security.Cryptography;
using CoHAnalytics.ReferenceDataGenerator;

namespace CoHAnalytics.Tests.ReferenceData;

/// <summary>
/// Discovery/proof tests for Enhancement help Scale token arithmetic inputs.
/// Does not implement a production presentation resolver.
/// </summary>
public sealed class EnhancementHelpScaleDiscoveryTests
{
    private static string LiveInstallRoot => LiveInstallTestEnvironment.InstallRoot;
    private const string BinPiggRelative = @"assets\live\bin.pigg";
    private const string BinPowersPiggRelative = @"assets\live\bin_powers.pigg";

    private static readonly string ExpectedBinPiggHash =
        "6ccb245c4a18d0811322427f03c838a26a618289854703eeaf11a1f698a0df9b";
    private static readonly string ExpectedBinPowersPiggHash =
        "e719a7252297f3d3e21099ec6cc2f32d1e787fbb12d11dcf4bd40a722b7b6d1e";

    [Fact]
    [Trait("Category", "LiveInstall")]
    public void LiveInstall_BoostEffectCurves_MatchParseBoostEffectivenessTableShape()
    {
        AssertHomecomingHashesUnchanged();

        var binPigg = Path.Combine(LiveInstallRoot, BinPiggRelative);
        var above = HomecomingBoostEffectCurveDiscoveryReader.Read(
            HomecomingPiggMemberReader.ReadMember(binPigg, "bin/boost_effect_above.bin"));
        var below = HomecomingBoostEffectCurveDiscoveryReader.Read(
            HomecomingPiggMemberReader.ReadMember(binPigg, "bin/boost_effect_below.bin"));
        var boosters = HomecomingBoostEffectCurveDiscoveryReader.Read(
            HomecomingPiggMemberReader.ReadMember(binPigg, "bin/boost_effect_boosters.bin"));

        Assert.Equal(4, above.Count);
        Assert.Equal(1.0, above[0], 5);
        Assert.Equal(1.05, above[1], 5);
        Assert.Equal(1.10, above[2], 5);
        Assert.Equal(1.15, above[3], 5);

        Assert.Equal(4, below.Count);
        Assert.Equal(1.0, below[0], 5);
        Assert.Equal(0.9, below[1], 5);
        Assert.Equal(0.8, below[2], 5);
        Assert.Equal(0.7, below[3], 5);

        Assert.Equal(6, boosters.Count);
        Assert.Equal(1.0, boosters[0], 5);
        Assert.Equal(1.05, boosters[1], 5);
        Assert.Equal(1.10, boosters[2], 5);
        Assert.Equal(1.15, boosters[3], 5);
        Assert.Equal(1.20, boosters[4], 5);
        Assert.Equal(1.25, boosters[5], 5);
    }

    [Fact]
    [Trait("Category", "LiveInstall")]
    public void LiveInstall_ClassModTables_ExposeIdenticalBoostSchedulesAcrossPlayerClasses()
    {
        AssertHomecomingHashesUnchanged();

        var classes = HomecomingClassModTableDiscoveryReader.Read(
            HomecomingPiggMemberReader.ReadMember(
                Path.Combine(LiveInstallRoot, BinPiggRelative),
                "bin/classes.bin"));
        var blaster = classes.RequireClass("Class_Blaster");
        Assert.Equal(105, blaster["Melee_Boosts_33"].Count);
        Assert.Equal(1.0f, blaster["Melee_Ones"][50]);

        Assert.Equal(0.4238, blaster["Melee_Boosts_33"][49], 4);
        Assert.Equal(0.3330, blaster["Melee_Boosts_33"][25], 4);

        foreach (var className in classes.TablesByClassName.Keys)
        {
            var tables = classes.TablesByClassName[className];
            Assert.Equal(blaster["Melee_Boosts_33"].Count, tables["Melee_Boosts_33"].Count);
            Assert.Equal(blaster["Melee_Ones"].Count, tables["Melee_Ones"].Count);
            for (var index = 0; index < blaster["Melee_Boosts_33"].Count; index++)
            {
                Assert.Equal(blaster["Melee_Boosts_33"][index], tables["Melee_Boosts_33"][index]);
                Assert.Equal(blaster["Melee_Ones"][index], tables["Melee_Ones"][index]);
            }
        }
    }

    [Fact]
    [Trait("Category", "LiveInstall")]
    public void LiveInstall_RepresentativeBoostEffects_ExposeTableAndScaleChain()
    {
        AssertHomecomingHashesUnchanged();

        var powers = HomecomingPiggMemberReader.ReadMember(
            Path.Combine(LiveInstallRoot, BinPowersPiggRelative),
            "bin/powers.bin");
        var classes = HomecomingClassModTableDiscoveryReader.Read(
            HomecomingPiggMemberReader.ReadMember(
                Path.Combine(LiveInstallRoot, BinPiggRelative),
                "bin/classes.bin"));
        var table = classes.RequireClass("Class_Blaster")["Melee_Boosts_33"];
        var ones = classes.RequireClass("Class_Blaster")["Melee_Ones"];

        AssertBoost(
            powers,
            "Boosts.Crafted_Damage.Crafted_Damage",
            expectedTag: "Damage",
            expectedTable: "Melee_Boosts_33",
            expectedScale: 1.0f);
        AssertBoost(
            powers,
            "Boosts.Crafted_Accuracy.Crafted_Accuracy",
            expectedTag: "Accuracy",
            expectedTable: "Melee_Boosts_33",
            expectedScale: 1.0f);
        AssertBoost(
            powers,
            "Boosts.Crafted_Recharge.Crafted_Recharge",
            expectedTag: "rechargetime",
            expectedTable: "Melee_Boosts_33",
            expectedScale: 1.0f);
        AssertBoost(
            powers,
            "Boosts.Crafted_Endurance_Discount.Crafted_Endurance_Discount",
            expectedTag: "Endurance",
            expectedTable: "Melee_Boosts_33",
            expectedScale: 1.0f);
        AssertBoost(
            powers,
            "Boosts.Crafted_Bonesnap_A.Crafted_Bonesnap_A",
            expectedTag: "Damage",
            expectedTable: "Melee_Boosts_33",
            expectedScale: 0.625f);
        AssertBoost(
            powers,
            "Boosts.Attuned_Bonesnap_A.Attuned_Bonesnap_A",
            expectedTag: "Damage",
            expectedTable: "Melee_Boosts_33",
            expectedScale: 0.625f);
        AssertBoost(
            powers,
            "Boosts.Crafted_Absolute_Amazement_A.Crafted_Absolute_Amazement_A",
            expectedTag: "Mez",
            expectedTable: "Melee_Boosts_33",
            expectedScale: 1.25f);
        AssertBoost(
            powers,
            "Boosts.Superior_Attuned_Absolute_Amazement_A.Superior_Attuned_Absolute_Amazement_A",
            expectedTag: "Mez",
            expectedTable: "Melee_Boosts_33",
            expectedScale: 1.25f);
        AssertBoost(
            powers,
            "Boosts.Generic_Damage.Generic_Damage",
            expectedTag: "Damage",
            expectedTable: "Melee_Ones",
            expectedScale: 0.0833f);
        AssertBoost(
            powers,
            "Boosts.Magic_Damage.Magic_Damage",
            expectedTag: "Damage",
            expectedTable: "Melee_Ones",
            expectedScale: 0.3333f);

        // Formula proof rows (index = 1-based level - 1).
        Assert.Equal(42.38, table[49] * 1.0f * 100.0, 2);
        Assert.Equal(26.49, table[49] * 0.625f * 100.0, 2);
        Assert.Equal(52.97, table[49] * 1.25f * 100.0, 2);
        Assert.Equal(8.33, ones[49] * 0.0833f * 100.0, 2);
        Assert.Equal(33.33, ones[49] * 0.3333f * 100.0, 2);
        Assert.Equal(20.01, table[24] * 0.625f * 100.0, 2); // Attuned Bonesnap capped at set max 25
    }

    [Fact]
    [Trait("Category", "LiveInstall")]
    public void LiveInstall_TokenBinding_UsesEffectTagsWithCaseFlexibility()
    {
        var powers = HomecomingPiggMemberReader.ReadMember(
            Path.Combine(LiveInstallRoot, BinPowersPiggRelative),
            "bin/powers.bin");
        var templates = HomecomingBoostEffectTemplateDiscoveryReader.ReadForSourceId(
            powers,
            "Boosts.Crafted_Recharge.Crafted_Recharge");
        var template = Assert.Single(templates);
        Assert.Equal(["rechargetime"], template.Tags);

        // Help token is RechargeTime; effect tag is rechargetime — binding cannot be
        // ordinal case-sensitive against tags alone without normalization.
        Assert.Equal(
            "rechargetime",
            template.Tags[0],
            ignoreCase: true);
        Assert.NotEqual("RechargeTime", template.Tags[0]);
    }

    private static void AssertBoost(
        byte[] powers,
        string sourceId,
        string expectedTag,
        string expectedTable,
        float expectedScale)
    {
        var templates = HomecomingBoostEffectTemplateDiscoveryReader.ReadForSourceId(powers, sourceId);
        Assert.Contains(
            templates,
            template => template.Tags.Any(tag =>
                    string.Equals(tag, expectedTag, StringComparison.OrdinalIgnoreCase))
                && string.Equals(template.Table, expectedTable, StringComparison.Ordinal)
                && Math.Abs(template.Scale - expectedScale) < 0.0002f);
    }

    private static void AssertHomecomingHashesUnchanged()
    {
        Assert.Equal(ExpectedBinPiggHash, Sha256Hex(Path.Combine(LiveInstallRoot, BinPiggRelative)));
        Assert.Equal(
            ExpectedBinPowersPiggHash,
            Sha256Hex(Path.Combine(LiveInstallRoot, BinPowersPiggRelative)));
    }

    private static string Sha256Hex(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
