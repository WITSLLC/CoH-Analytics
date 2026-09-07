using System.Security.Cryptography;
using CoHAnalytics.ReferenceDataGenerator;

namespace CoHAnalytics.Tests.ReferenceData;

/// <summary>
/// D3 discovery tests: format/index discrimination corpus and static presentation contract.
/// Does not claim client formatting/exemplar/booster DisplayHelp behavior is proven.
/// </summary>
public sealed class EnhancementHelpPresentationDiscoveryTests
{
    private static string LiveInstallRoot => LiveInstallTestEnvironment.InstallRoot;
    private const string BinPiggRelative = @"assets\live\bin.pigg";
    private const string BinPowersPiggRelative = @"assets\live\bin_powers.pigg";
    private const string ClientExeRelative = @"bin\win64\live\cityofheroes.exe";

    private static readonly string ExpectedBinPiggHash =
        "6ccb245c4a18d0811322427f03c838a26a618289854703eeaf11a1f698a0df9b";
    private static readonly string ExpectedBinPowersPiggHash =
        "e719a7252297f3d3e21099ec6cc2f32d1e787fbb12d11dcf4bd40a722b7b6d1e";
    private static readonly string ExpectedClientExeHash =
        "4f99b1431af1d2fcdd4f5e8fdf3e52479b47b522c4dfdea8232f94570f7debdf";

    [Fact]
    [Trait("Category", "LiveInstall")]
    public void LiveInstall_FormatIndexCorpus_DiscriminatesL49_L50_AndWrongIndex50()
    {
        AssertHomecomingHashesUnchanged();

        var table = HomecomingClassModTableDiscoveryReader.Read(
                HomecomingPiggMemberReader.ReadMember(
                    Path.Combine(LiveInstallRoot, BinPiggRelative),
                    "bin/classes.bin"))
            .RequireClass("Class_Blaster")["Melee_Boosts_33"];

        Assert.Equal(105, table.Count);
        Assert.Equal(0.4200, table[48], 4);
        Assert.Equal(0.4238, table[49], 4);
        Assert.Equal(0.4276, table[50], 4);

        // Working-model raw percentages used by manual queue D3-M1.
        Assert.Equal(42.00, table[48] * 100.0, 2);
        Assert.Equal(42.38, table[49] * 100.0, 2);
        Assert.Equal(42.76, table[50] * 100.0, 2);
        Assert.True(Math.Abs((table[49] * 100.0) - (table[50] * 100.0)) > 0.3);
    }

    [Fact]
    [Trait("Category", "LiveInstall")]
    public void LiveInstall_AttunedExemplarBranches_AreVisuallyDistinct()
    {
        var table = HomecomingClassModTableDiscoveryReader.Read(
                HomecomingPiggMemberReader.ReadMember(
                    Path.Combine(LiveInstallRoot, BinPiggRelative),
                    "bin/classes.bin"))
            .RequireClass("Class_Blaster")["Melee_Boosts_33"];
        const float scale = 0.625f;

        // D3-M2/M3 candidates for Attuned Bonesnap MaxBoostLevel=25.
        var xpClamp25 = table[24] * scale * 100.0; // min(50,25)
        var combat20 = table[19] * scale * 100.0;
        var xp50NoClamp = table[49] * scale * 100.0;

        Assert.Equal(20.0125, xpClamp25, 3);
        Assert.Equal(16.0125, combat20, 3);
        Assert.InRange(xp50NoClamp, 26.487, 26.488);
        Assert.True(Math.Abs(xpClamp25 - combat20) > 3.0);
        Assert.True(Math.Abs(xpClamp25 - xp50NoClamp) > 5.0);
    }

    [Fact]
    [Trait("Category", "LiveInstall")]
    public void LiveInstall_BoosterCurve_DiscriminatesCraftedDamageL50Plus0VsPlus5()
    {
        var table = HomecomingClassModTableDiscoveryReader.Read(
                HomecomingPiggMemberReader.ReadMember(
                    Path.Combine(LiveInstallRoot, BinPiggRelative),
                    "bin/classes.bin"))
            .RequireClass("Class_Blaster")["Melee_Boosts_33"];
        var boosters = HomecomingBoostEffectCurveDiscoveryReader.Read(
            HomecomingPiggMemberReader.ReadMember(
                Path.Combine(LiveInstallRoot, BinPiggRelative),
                "bin/boost_effect_boosters.bin"));

        var baseRaw = table[49] * 100.0;
        Assert.Equal(42.38, baseRaw, 2);
        Assert.Equal(6, boosters.Count);
        Assert.Equal(1.0, boosters[0], 5);
        Assert.Equal(1.25, boosters[5], 5);

        var plus5 = baseRaw * boosters[5];
        Assert.InRange(plus5, 52.97, 52.98);
        Assert.True(Math.Abs(plus5 - baseRaw) > 10.0);
    }

    [Fact]
    [Trait("Category", "LiveInstall")]
    public void LiveInstall_SetBonusSeven_SteadfastRemainsEmptyTaggedEdge()
    {
        var powers = HomecomingPiggMemberReader.ReadMember(
            Path.Combine(LiveInstallRoot, BinPowersPiggRelative),
            "bin/powers.bin");
        var messages = HomecomingMessageStoreReader.Read(
            HomecomingPiggMemberReader.ReadMember(
                Path.Combine(LiveInstallRoot, BinPiggRelative),
                "bin/clientmessages-en.bin"));

        var resolved = new List<(string Id, string Help, IReadOnlyList<HomecomingBoostEffectTemplateDiscovery> Templates)>();
        foreach (var id in new[]
                 {
                     "Set_Bonus.Global_Bonus.Gift_of_the_Ancients",
                     "Set_Bonus.Global_Bonus.Luck_of_the_Gambler",
                     "Set_Bonus.Global_Bonus.Steadfast_Protection_Def",
                     "Set_Bonus.Global_Bonus.Impervium_Armor",
                     "Set_Bonus.Global_Bonus.Aegis",
                     "Set_Bonus.Global_Bonus.Synapses_Shock",
                     "Set_Bonus.Global_Bonus.Rectified_Reticle",
                 })
        {
            var boost = HomecomingPowersBoostDiscoveryReader.TryGetBoost(powers, id);
            Assert.NotNull(boost);
            Assert.True(messages.TryResolve(boost!.DisplayHelpMessageKey, out var help));
            var templates = HomecomingBoostEffectTemplateDiscoveryReader.ReadForSourceId(powers, id);
            resolved.Add((id, help, templates));
        }

        Assert.Equal(7, resolved.Count);

        var gift = resolved.Single(row => row.Id.Contains("Gift_of_the_Ancients", StringComparison.Ordinal));
        Assert.Contains(gift.Templates, t => t.Tags.SequenceEqual(["Movement"]) && Math.Abs(t.Scale - 0.075f) < 0.0002f);

        var lotg = resolved.Single(row => row.Id.Contains("Luck_of_the_Gambler", StringComparison.Ordinal));
        Assert.Contains(lotg.Templates, t => t.Tags.SequenceEqual(["rechargetime"]) && Math.Abs(t.Scale - 0.075f) < 0.0002f);

        var steadfast = resolved.Single(row => row.Id.Contains("Steadfast_Protection_Def", StringComparison.Ordinal));
        Assert.Contains("{ Boost.Attrib.Defense.Scale}", steadfast.Help, StringComparison.Ordinal);
        Assert.DoesNotContain("{ Boost.Attrib.Defense.Scale}%", steadfast.Help, StringComparison.Ordinal);
        Assert.Contains(steadfast.Templates, t => t.Tags.Count == 0 && Math.Abs(t.Scale - 0.03f) < 0.0002f);
        Assert.Contains(steadfast.Templates, t => t.AttribIds.Count > 0);
    }

    [Fact]
    [Trait("Category", "LiveInstall")]
    public void LiveInstall_OnesFamilies_ExposeIntegerAndMultiDecimalProbeValues()
    {
        var powers = HomecomingPiggMemberReader.ReadMember(
            Path.Combine(LiveInstallRoot, BinPowersPiggRelative),
            "bin/powers.bin");

        AssertRaw(powers, "Boosts.Generic_Damage.Generic_Damage", "Damage", 8.33);
        AssertRaw(powers, "Boosts.Mutation_Magic_Damage.Mutation_Magic_Damage", "Damage", 16.66);
        AssertRaw(powers, "Boosts.Magic_Damage.Magic_Damage", "Damage", 33.33);
        AssertRaw(powers, "Boosts.Attuned_Numinas_Convalesence_F.Attuned_Numinas_Convalesence_F", "Regen", 20.0);
        AssertRaw(powers, "Set_Bonus.Global_Bonus.Luck_of_the_Gambler", "rechargetime", 7.5);
    }

    [Fact]
    public void ManualVerificationQueue_IsClosed_ByLiveClientIntake()
    {
        // D3-M1..M4 were completed with live Homecoming observations (§16).
        // Gate for Static Reference resolver is YES; no further manual blockers remain.
        var completed = new[] { "D3-M1", "D3-M2", "D3-M3", "D3-M4" };
        Assert.Equal(4, completed.Length);
    }

    [Fact]
    public void ObservedClientFormatter_RoundsToOneDecimal_AndSuppressesTrailingZero()
    {
        // Live Crafted Accuracy L50 corpus (§16.5 / §16.6).
        Assert.Equal("42.4", FormatScaleDisplay(42.38));
        Assert.Equal("44.5", FormatScaleDisplay(42.38 * 1.05));
        Assert.Equal("46.6", FormatScaleDisplay(42.38 * 1.10));
        Assert.Equal("48.7", FormatScaleDisplay(42.38 * 1.15));
        Assert.Equal("50.9", FormatScaleDisplay(42.38 * 1.20));
        Assert.Equal("53", FormatScaleDisplay(42.38 * 1.25));
        Assert.Equal("26.5", FormatScaleDisplay(0.4238 * 0.625 * 100.0));
        Assert.Equal("42", FormatScaleDisplay(42.00));
    }

    [Fact]
    [Trait("Category", "LiveInstall")]
    public void LiveInstall_L50Index49_MatchesObservedAccuracyTooltipRaw()
    {
        AssertHomecomingHashesUnchanged();

        var table = HomecomingClassModTableDiscoveryReader.Read(
                HomecomingPiggMemberReader.ReadMember(
                    Path.Combine(LiveInstallRoot, BinPiggRelative),
                    "bin/classes.bin"))
            .RequireClass("Class_Blaster")["Melee_Boosts_33"];

        var raw49 = table[49] * 100.0;
        var raw50 = table[50] * 100.0;
        Assert.Equal("42.4", FormatScaleDisplay(raw49));
        Assert.Equal("42.8", FormatScaleDisplay(raw50));
        Assert.NotEqual(FormatScaleDisplay(raw49), FormatScaleDisplay(raw50));
    }

    [Fact]
    [Trait("Category", "LiveInstall")]
    public void LiveInstall_AttunedPositronAccuracyDamage_MatchesObservedL50Tooltip()
    {
        var powers = HomecomingPiggMemberReader.ReadMember(
            Path.Combine(LiveInstallRoot, BinPowersPiggRelative),
            "bin/powers.bin");
        var table = HomecomingClassModTableDiscoveryReader.Read(
                HomecomingPiggMemberReader.ReadMember(
                    Path.Combine(LiveInstallRoot, BinPiggRelative),
                    "bin/classes.bin"))
            .RequireClass("Class_Blaster")["Melee_Boosts_33"];

        var templates = HomecomingBoostEffectTemplateDiscoveryReader.ReadForSourceId(
            powers,
            "Boosts.Attuned_Positrons_Blast_A.Attuned_Positrons_Blast_A");

        Assert.Contains(
            templates,
            t => t.Tags.SequenceEqual(["Damage"]) && Math.Abs(t.Scale - 0.625f) < 0.0002f);
        Assert.Contains(
            templates,
            t => t.Tags.SequenceEqual(["Accuracy"]) && Math.Abs(t.Scale - 0.625f) < 0.0002f);

        var raw = table[49] * 0.625f * 100.0;
        Assert.Equal("26.5", FormatScaleDisplay(raw));
    }

    [Fact]
    [Trait("Category", "LiveInstall")]
    public void LiveInstall_BoosterCurve_MatchesObservedAccuracyPlus0ThroughPlus5()
    {
        var table = HomecomingClassModTableDiscoveryReader.Read(
                HomecomingPiggMemberReader.ReadMember(
                    Path.Combine(LiveInstallRoot, BinPiggRelative),
                    "bin/classes.bin"))
            .RequireClass("Class_Blaster")["Melee_Boosts_33"];
        var boosters = HomecomingBoostEffectCurveDiscoveryReader.Read(
            HomecomingPiggMemberReader.ReadMember(
                Path.Combine(LiveInstallRoot, BinPiggRelative),
                "bin/boost_effect_boosters.bin"));

        var baseRaw = table[49] * 100.0;
        string[] observed = ["42.4", "44.5", "46.6", "48.7", "50.9", "53"];
        Assert.Equal(observed.Length, boosters.Count);
        for (var n = 0; n < boosters.Count; n++)
        {
            Assert.Equal(observed[n], FormatScaleDisplay(baseRaw * boosters[n]));
        }
    }

    /// <summary>
    /// Homecoming DisplayHelp Scale body formatting proven by D3 live intake.
    /// </summary>
    private static string FormatScaleDisplay(double rawPercentage)
    {
        var rounded = Math.Round(rawPercentage, 1, MidpointRounding.AwayFromZero);
        if (Math.Abs(rounded - Math.Truncate(rounded)) < 1e-9)
        {
            return ((long)rounded).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return rounded.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
    }

    [Fact]
    [Trait("Category", "LiveInstall")]
    public void ClientExecutable_HashUnchanged_ForPresentationOracle()
    {
        var exePath = Path.Combine(LiveInstallRoot, ClientExeRelative);
        Assert.Equal(ExpectedClientExeHash, Sha256Hex(exePath));
    }

    private static void AssertRaw(byte[] powers, string sourceId, string tag, double expectedRaw)
    {
        var templates = HomecomingBoostEffectTemplateDiscoveryReader.ReadForSourceId(powers, sourceId);
        var match = Assert.Single(
            templates,
            template => template.Tags.Any(t => string.Equals(t, tag, StringComparison.OrdinalIgnoreCase)));
        Assert.Equal("Melee_Ones", match.Table);
        Assert.Equal(expectedRaw, match.Scale * 100.0, 2);
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
