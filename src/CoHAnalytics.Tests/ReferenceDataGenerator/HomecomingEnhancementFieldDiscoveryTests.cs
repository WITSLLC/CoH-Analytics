using CoHAnalytics.ReferenceDataGenerator;

namespace CoHAnalytics.Tests.ReferenceDataGenerator;

public sealed class HomecomingEnhancementFieldDiscoveryTests
{
    private static string LiveInstallRoot => LiveInstallTestEnvironment.InstallRoot;
    private const string PowersArchive = @"assets\live\bin_powers.pigg";
    private const string PowersMember = "bin/powers.bin";
    private const string BoostSetsArchive = @"assets\live\bin.pigg";
    private const string BoostSetsMember = "bin/boostsets.bin";
    private const string MessagesMember = "bin/clientmessages-en.bin";

    [Fact]
    public void ReadBoosts_MinimalFixtureRecord_IsSkippedByDiscoveryReader()
    {
        var data = HomecomingBinaryFixtureBuilder.CreatePowers(
            new SyntheticBoostRecord("Boosts.Crafted_Fixture_A.Crafted_Fixture_A", "P_BOOST_FIXTURE"));

        Assert.Empty(HomecomingPowersBoostDiscoveryReader.ReadBoosts(data));
    }

    [Fact]
    [Trait("Category", "LiveInstall")]
    public void LiveInstall_CommonIoDamage_UsesStructuralBoostTypeNotDisplayName()
    {
        var powers = ReadLivePowers();
        var messages = ReadLiveMessages();
        var damage = HomecomingPowersBoostDiscoveryReader.TryGetBoost(
            powers,
            "Boosts.Crafted_Damage.Crafted_Damage");
        Assert.NotNull(damage);
        Assert.Equal("E_ICON_GEN_DAMAGE_01.tga", damage.Icon);
        Assert.Equal(["Damage"], damage.NonOriginBoostTypes);
        Assert.Contains("Damage", damage.BoostsAllowed, StringComparer.Ordinal);
        Assert.True(messages.TryResolve(damage.DisplayNameMessageKey, out var displayName));
        Assert.StartsWith("Invention:", displayName, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Boosts.Crafted_Accuracy.Crafted_Accuracy", "Accuracy", "E_ICON_GEN_ACCURACY_01.tga")]
    [InlineData("Boosts.Crafted_Recharge.Crafted_Recharge", "Recharge", "E_ICON_GEN_RECHARGE_01.tga")]
    [InlineData("Boosts.Crafted_Endurance_Discount.Crafted_Endurance_Discount", "EnduranceDiscount", "E_ICON_GEN_ENDURANCE_DISC_01.tga")]
    [InlineData("Boosts.Crafted_Hold.Crafted_Hold", "Hold", "E_ICON_GEN_HOLD_01.tga")]
    [InlineData("Boosts.Crafted_Immobilize.Crafted_Immobilize", "Immobilize", "E_ICON_GEN_IMMOBILIZE_01.tga")]
    [InlineData("Boosts.Crafted_Knockback.Crafted_Knockback", "Knockback", "E_ICON_GEN_KNOCKBACK_01.tga")]
    [InlineData("Boosts.Crafted_Res_Damage.Crafted_Res_Damage", "Res_Damage", "E_ICON_GEN_RESIST_DAMAGE_01.tga")]
    [InlineData("Boosts.Crafted_Heal.Crafted_Heal", "Heal", "E_ICON_GEN_HEAL_01.tga")]
    [Trait("Category", "LiveInstall")]
    public void LiveInstall_RepresentativeCommonIoRecords_ExposeNonOriginBoostType(
        string sourceId,
        string expectedType,
        string expectedIconSuffix)
    {
        var powers = ReadLivePowers();
        var boost = HomecomingPowersBoostDiscoveryReader.TryGetBoost(powers, sourceId);
        Assert.NotNull(boost);
        Assert.Equal([expectedType], boost.NonOriginBoostTypes);
        Assert.EndsWith(expectedIconSuffix, boost.Icon, StringComparison.OrdinalIgnoreCase);
        Assert.All(
            HomecomingBoostTypeNames.OriginTypes,
            origin => Assert.Contains(origin, boost.BoostsAllowed, StringComparer.Ordinal));
    }

    [Fact]
    [Trait("Category", "LiveInstall")]
    public void LiveInstall_AbsoluteAmazementSet_ExposesBonusesAndAllowedPowers()
    {
        var boostSets = HomecomingBoostSetsDiscoveryReader.Read(ReadLiveBoostSetsBytes());
        var set = Assert.Single(boostSets, value => value.HomecomingSetId == "Absolute_Amazement");
        Assert.True(set.AllowedPowers.Count > 100);
        Assert.Equal(5, set.Bonuses.Count);
        Assert.Contains(
            set.Bonuses,
            bonus => bonus.MinimumBoosts == 2u
                && bonus.MaximumBoosts == 6u
                && bonus.AutoPowerSourceIds.Contains("Set_Bonus.Set_Bonus.Improved_Recovery_7"));
        Assert.Equal(50u, set.MinimumLevel);
        Assert.Equal(50u, set.MaximumLevel);
    }

    [Fact]
    [Trait("Category", "LiveInstall")]
    public void LiveInstall_SetBonusesReferenceCanonicalAutoPowers()
    {
        var boostSets = HomecomingBoostSetsDiscoveryReader.Read(ReadLiveBoostSetsBytes());
        var set = Assert.Single(boostSets, value => value.HomecomingSetId == "Bonesnap");
        Assert.Equal(2, set.Bonuses.Count);
        Assert.All(set.Bonuses, bonus => Assert.NotEmpty(bonus.AutoPowerSourceIds));
        Assert.All(set.Bonuses, bonus => Assert.True(bonus.MinimumBoosts <= bonus.MaximumBoosts));
    }

    [Fact]
    [Trait("Category", "LiveInstall")]
    public void LiveInstall_NullCategoryDisplayTextSets_AreMissingMessageKeysNotImporterOmissions()
    {
        var messages = ReadLiveMessages();
        var boostSets = HomecomingBoostSetsDiscoveryReader.Read(ReadLiveBoostSetsBytes());
        var nullCategorySets = boostSets
            .Where(set => string.Equals(set.CategoryCode, "ECToHitDeBuff", StringComparison.Ordinal))
            .Select(set => set.HomecomingSetId)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            ["Dampened_Spirits", "Dark_Watchers_Despair", "Deflated_Ego", "Discouraging_Words"],
            nullCategorySets);
        Assert.False(messages.TryResolve("ECToHitDeBuff", out _));
    }

    [Fact]
    [Trait("Category", "LiveInstall")]
    public void LiveInstall_SetCategoryCodes_ResolveToPlayerFacingCategoryText()
    {
        var messages = ReadLiveMessages();
        var boostSets = HomecomingBoostSetsDiscoveryReader.Read(ReadLiveBoostSetsBytes());
        var set = Assert.Single(boostSets, value => value.HomecomingSetId == "Bonesnap");
        Assert.Equal("ECMelee", set.CategoryCode);
        Assert.True(messages.TryResolve(set.CategoryCode, out var categoryText));
        Assert.Equal("Category: Melee", categoryText);
    }

    private static byte[] ReadLivePowers() =>
        HomecomingPiggMemberReader.ReadMember(
            Path.Combine(LiveInstallRoot, PowersArchive),
            PowersMember);

    private static byte[] ReadLiveBoostSetsBytes() =>
        HomecomingPiggMemberReader.ReadMember(
            Path.Combine(LiveInstallRoot, BoostSetsArchive),
            BoostSetsMember);

    private static HomecomingMessageStore ReadLiveMessages()
    {
        var messages = HomecomingPiggMemberReader.ReadMember(
            Path.Combine(LiveInstallRoot, BoostSetsArchive),
            MessagesMember);
        return HomecomingMessageStoreReader.Read(messages);
    }
}
