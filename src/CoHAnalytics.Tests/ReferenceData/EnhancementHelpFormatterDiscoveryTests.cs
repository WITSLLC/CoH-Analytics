using System.Security.Cryptography;
using System.Text.RegularExpressions;
using CoHAnalytics.ReferenceData;
using CoHAnalytics.ReferenceDataGenerator;

namespace CoHAnalytics.Tests.ReferenceData;

/// <summary>
/// D2 discovery/proof tests for help presentation boundaries (formatter, binding edges,
/// Attuned/exemplar messaging). Does not implement a production resolver.
/// </summary>
public sealed class EnhancementHelpFormatterDiscoveryTests
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

    private static readonly Regex BraceTokenRegex = new(@"\{[^{}]+\}", RegexOptions.CultureInvariant);

    [Fact]
    [Trait("Category", "LiveInstall")]
    public void LiveInstall_ScaleHelpMessages_DeclareNoMessageStoreVariables()
    {
        AssertHomecomingHashesUnchanged();

        var store = HomecomingMessageStoreReader.Read(
            HomecomingPiggMemberReader.ReadMember(
                Path.Combine(LiveInstallRoot, BinPiggRelative),
                "bin/clientmessages-en.bin"));

        Assert.True(store.TryResolve("EnhancementIgnoreEffectiveness", out var ignoreEffectiveness));
        Assert.Equal("Effectiveness does not vary as Security Level changes", ignoreEffectiveness);

        Assert.True(store.TryResolve("StatusExemplarTip", out var exemplarTip));
        Assert.Contains("{combatlevel}", exemplarTip, StringComparison.Ordinal);
        Assert.Contains("{level}", exemplarTip, StringComparison.Ordinal);
        Assert.Contains("Exemplared Combat Level", exemplarTip, StringComparison.Ordinal);

        Assert.True(store.TryResolve("EnhancementCombineLevel", out var combineLevel));
        Assert.Contains("Security Level {Level}", combineLevel, StringComparison.Ordinal);

        // Scale tokens are inline in power help values and are not bound through the
        // message-store variable/format pool (%.1f / %.2f / …).
        Assert.True(store.TryResolve("P236680247", out var numinaHelp));
        Assert.Contains("{Boost.Attrib.Regen.Scale}", numinaHelp, StringComparison.Ordinal);
        Assert.Contains("%", numinaHelp, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "LiveInstall")]
    public void LiveInstall_RegenAndRegeneration_AreDistinctEffectTags()
    {
        AssertHomecomingHashesUnchanged();

        var powers = HomecomingPiggMemberReader.ReadMember(
            Path.Combine(LiveInstallRoot, BinPowersPiggRelative),
            "bin/powers.bin");

        var numina = HomecomingBoostEffectTemplateDiscoveryReader.ReadForSourceId(
            powers,
            "Boosts.Attuned_Numinas_Convalesence_F.Attuned_Numinas_Convalesence_F");
        Assert.Contains(
            numina,
            template => template.Tags.SequenceEqual(["Regen"])
                && template.Table == "Melee_Ones"
                && Math.Abs(template.Scale - 0.2f) < 0.0002f);

        var decreased = HomecomingBoostEffectTemplateDiscoveryReader.ReadForSourceId(
            powers,
            "Boosts.Crafted_Decreased_Regeneration.Crafted_Decreased_Regeneration");
        var regeneration = Assert.Single(decreased);
        Assert.Equal(["Regeneration"], regeneration.Tags);
        Assert.Equal("Melee_Boosts_33", regeneration.Table);
        Assert.Equal(1.0f, regeneration.Scale, 4);

        Assert.DoesNotContain(numina, template => template.Tags.Contains("Regeneration"));
        Assert.DoesNotContain(decreased, template => template.Tags.Contains("Regen"));
    }

    [Fact]
    [Trait("Category", "LiveInstall")]
    public void LiveInstall_LowercaseAccuracyToken_BindsToAccuracyTag()
    {
        var powers = HomecomingPiggMemberReader.ReadMember(
            Path.Combine(LiveInstallRoot, BinPowersPiggRelative),
            "bin/powers.bin");
        var nightmare = HomecomingBoostEffectTemplateDiscoveryReader.ReadForSourceId(
            powers,
            "Boosts.Attuned_Nightmare_F.Attuned_Nightmare_F");

        Assert.Contains(
            nightmare,
            template => template.Tags.Any(tag =>
                string.Equals(tag, "Accuracy", StringComparison.OrdinalIgnoreCase)));

        var catalog = ItemReferenceCatalogFactory.LoadEmbeddedProduction();
        Assert.True(catalog.TryResolve("Nightmare: Accuracy/Fear", out var item));
        Assert.Contains(
            "{Boost.Attrib.accuracy.Scale}",
            item.Item.DisplayHelp,
            StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "LiveInstall")]
    public void LiveInstall_SetBonusScaleTokens_UseSameGrammar_AndRecoverEffects()
    {
        AssertHomecomingHashesUnchanged();

        var catalog = ItemReferenceCatalogFactory.LoadEmbeddedProduction();
        var tokenized = catalog.GetEnhancementSets(ReferenceCatalogQueryScope.CurrentHomecoming)
            .Values
            .SelectMany(set => set.Bonuses)
            .SelectMany(bonus => bonus.AutoPowers)
            .Where(power => !string.IsNullOrWhiteSpace(power.DisplayHelp)
                && BraceTokenRegex.IsMatch(power.DisplayHelp!))
            .Select(power => (SourceId: power.HomecomingSourceId, Help: power.DisplayHelp!))
            .ToArray();

        Assert.Equal(7, tokenized.Length);
        Assert.Contains(
            tokenized,
            row => row.Help.Contains("{ Boost.Attrib.Defense.Scale}", StringComparison.Ordinal));

        var powers = HomecomingPiggMemberReader.ReadMember(
            Path.Combine(LiveInstallRoot, BinPowersPiggRelative),
            "bin/powers.bin");

        var lotg = HomecomingBoostEffectTemplateDiscoveryReader.ReadForSourceId(
            powers,
            "Set_Bonus.Global_Bonus.Luck_of_the_Gambler");
        var lotgTemplate = Assert.Single(lotg);
        Assert.Equal(["rechargetime"], lotgTemplate.Tags);
        Assert.Equal("Melee_Ones", lotgTemplate.Table);
        Assert.Equal(0.075f, lotgTemplate.Scale, 4);
        Assert.Equal(7.5, lotgTemplate.Scale * 100.0, 4);

        var gift = HomecomingBoostEffectTemplateDiscoveryReader.ReadForSourceId(
            powers,
            "Set_Bonus.Global_Bonus.Gift_of_the_Ancients");
        var giftTemplate = Assert.Single(gift);
        Assert.Equal(["Movement"], giftTemplate.Tags);
        Assert.Equal("Melee_Ones", giftTemplate.Table);
        Assert.Equal(0.075f, giftTemplate.Scale, 4);

        var steadfast = HomecomingBoostEffectTemplateDiscoveryReader.ReadForSourceId(
            powers,
            "Set_Bonus.Global_Bonus.Steadfast_Protection_Def");
        Assert.Contains(steadfast, template => template.Tags.Count == 0);
        Assert.Contains(
            steadfast,
            template => template.Table == "Melee_Ones" && Math.Abs(template.Scale - 0.03f) < 0.0002f);
    }

    [Fact]
    [Trait("Category", "LiveInstall")]
    public void LiveInstall_SuperiorAttuned_SharesCraftedScaleTable_DiffersByAttuneFlagsInD1Corpus()
    {
        var powers = HomecomingPiggMemberReader.ReadMember(
            Path.Combine(LiveInstallRoot, BinPowersPiggRelative),
            "bin/powers.bin");

        var crafted = Assert.Single(
            HomecomingBoostEffectTemplateDiscoveryReader.ReadForSourceId(
                powers,
                "Boosts.Crafted_Absolute_Amazement_A.Crafted_Absolute_Amazement_A"));
        var superior = Assert.Single(
            HomecomingBoostEffectTemplateDiscoveryReader.ReadForSourceId(
                powers,
                "Boosts.Superior_Attuned_Absolute_Amazement_A.Superior_Attuned_Absolute_Amazement_A"));

        Assert.Equal(crafted.Tags, superior.Tags);
        Assert.Equal(crafted.Table, superior.Table);
        Assert.Equal(crafted.Scale, superior.Scale);
    }

    [Fact]
    [Trait("Category", "LiveInstall")]
    public void LiveInstall_SpecialFamily_DSyncUsesOnesScheduleLikeHamidon()
    {
        var powers = HomecomingPiggMemberReader.ReadMember(
            Path.Combine(LiveInstallRoot, BinPowersPiggRelative),
            "bin/powers.bin");
        var dsync = HomecomingBoostEffectTemplateDiscoveryReader.ReadForSourceId(
            powers,
            "Boosts.DSync_Damage_Accuracy.DSync_Damage_Accuracy");

        Assert.Contains(
            dsync,
            template => template.Tags.SequenceEqual(["Damage"])
                && template.Table == "Melee_Ones"
                && Math.Abs(template.Scale - 0.3333f) < 0.0002f);
        Assert.Contains(
            dsync,
            template => template.Tags.SequenceEqual(["Accuracy"])
                && template.Table == "Melee_Ones"
                && Math.Abs(template.Scale - 0.3333f) < 0.0002f);
    }

    [Fact]
    [Trait("Category", "LiveInstall")]
    public void ClientExecutable_ExposesEffectivenessAndExemplarPresentationKeys()
    {
        var exePath = Path.Combine(LiveInstallRoot, ClientExeRelative);
        Assert.Equal(ExpectedClientExeHash, Sha256Hex(exePath));
        var bytes = File.ReadAllBytes(exePath);
        Assert.True(ContainsAscii(bytes, "EnhancementIgnoreEffectiveness"));
        Assert.True(ContainsAscii(bytes, "EnhancementCombineLevel"));
        Assert.True(ContainsAscii(bytes, "StatusExemplarTip"));
        Assert.True(ContainsAscii(bytes, "BoostUsePlayerLevel"));
        Assert.True(ContainsAscii(bytes, "MaxBoostLevel"));
        Assert.True(ContainsAscii(bytes, "defs/boost_effect_boosters.def"));
        Assert.False(ContainsAscii(bytes, "Boost.Attrib"));
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

    private static bool ContainsAscii(byte[] haystack, string needle)
    {
        var pattern = System.Text.Encoding.ASCII.GetBytes(needle);
        return haystack.AsSpan().IndexOf(pattern) >= 0;
    }
}
