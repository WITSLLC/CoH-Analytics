using CoHAnalytics.ReferenceDataGenerator;

namespace CoHAnalytics.Tests.ReferenceDataGenerator;

public sealed class HomecomingBoostSetBonusRequiresDiscoveryTests
{
    private static string LiveInstallRoot => LiveInstallTestEnvironment.InstallRoot;

    [Fact]
    [Trait("Category", "LiveInstall")]
    public void LiveInstall_BonusRequires_ExactParse_MatchesAuditCensus()
    {
        var source = HomecomingStaticDataSourceDiscovery.Discover(LiveInstallRoot);
        var bytes = HomecomingPiggMemberReader.ReadMember(
            source.BinPiggPath,
            "bin/boostsets.bin");
        var sets = HomecomingBoostSetBonusRequiresDiscoveryReader.Read(bytes);

        Assert.Equal(227, sets.Count);
        var tiers = sets.SelectMany(set => set.Bonuses).ToArray();
        Assert.Equal(1086, tiers.Length);
        Assert.Equal(1138, tiers.Sum(tier => tier.AutoPowerSourceIds.Count));

        var nonempty = tiers.Where(tier => tier.RequiresTokens.Count > 0).ToArray();
        Assert.Equal(82, nonempty.Length);
        Assert.Equal(
            40,
            sets.Count(set => set.Bonuses.Any(tier => tier.RequiresTokens.Count > 0)));
        Assert.All(tiers, tier => Assert.Equal(0u, tier.LeadingUnknown));
        Assert.All(tiers, tier => Assert.Equal(0u, tier.TrailingUnknown));

        var patterns = nonempty
            .GroupBy(tier => Classify(tier.RequiresTokens), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        Assert.Equal(45, patterns["PvPMapGate"]);
        Assert.Equal(37, patterns["PieceSpecificGate"]);
        Assert.Equal(2, patterns.Count);
    }

    [Fact]
    [Trait("Category", "LiveInstall")]
    public void LiveInstall_Aegis_LuckOfTheGambler_And_Panacea_ExposeKnownRequiresPatterns()
    {
        var source = HomecomingStaticDataSourceDiscovery.Discover(LiveInstallRoot);
        var bytes = HomecomingPiggMemberReader.ReadMember(
            source.BinPiggPath,
            "bin/boostsets.bin");
        var sets = HomecomingBoostSetBonusRequiresDiscoveryReader.Read(bytes)
            .ToDictionary(set => set.HomecomingSetId, StringComparer.Ordinal);

        var aegis = Assert.Single(
            sets["Aegis"].Bonuses,
            tier => tier.RequiresTokens.Count > 0);
        Assert.Equal(
            [
                "Crafted_Aegis_F",
                "PowerBoostsSlotted>",
                "1",
                ">=",
                "Attuned_Aegis_F",
                "PowerBoostsSlotted>",
                "1",
                ">=",
                "||"
            ],
            aegis.RequiresTokens);
        Assert.Equal(["Set_Bonus.Global_Bonus.Aegis"], aegis.AutoPowerSourceIds);

        var lotg = Assert.Single(
            sets["Luck_of_the_Gambler"].Bonuses,
            tier => tier.RequiresTokens.Count > 0);
        Assert.Contains("Crafted_Luck_of_the_Gambler_F", lotg.RequiresTokens);
        Assert.Equal(
            ["Set_Bonus.Global_Bonus.Luck_of_the_Gambler"],
            lotg.AutoPowerSourceIds);

        Assert.Contains(
            sets["Panacea"].Bonuses.SelectMany(tier => tier.RequiresTokens),
            token => token == "isPVPMap?");
    }

    [Fact]
    [Trait("Category", "LiveInstall")]
    public void LiveInstall_DiscoveryReader_PreservesSameRequiresSetsAsExactParser()
    {
        var source = HomecomingStaticDataSourceDiscovery.Discover(LiveInstallRoot);
        var bytes = HomecomingPiggMemberReader.ReadMember(
            source.BinPiggPath,
            "bin/boostsets.bin");
        var exact = HomecomingBoostSetBonusRequiresDiscoveryReader.Read(bytes);
        var repaired = HomecomingBoostSetsDiscoveryReader.Read(bytes);

        var exactRequiresSets = exact
            .Where(set => set.Bonuses.Any(tier => tier.RequiresTokens.Count > 0))
            .Select(set => set.HomecomingSetId)
            .ToHashSet(StringComparer.Ordinal);
        var repairedRequiresSets = repaired
            .Where(set => set.Bonuses.Any(tier => tier.RequiresTokens.Count > 0))
            .Select(set => set.HomecomingSetId)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(40, exactRequiresSets.Count);
        Assert.True(exactRequiresSets.SetEquals(repairedRequiresSets));
    }

    private static string Classify(IReadOnlyList<string> tokens)
    {
        if (tokens.Count == 1 && tokens[0] == "isPVPMap?")
        {
            return "PvPMapGate";
        }

        if (tokens.Any(token => token.Contains("PowerBoostsSlotted", StringComparison.Ordinal)))
        {
            return "PieceSpecificGate";
        }

        return "Other";
    }
}
