using System.Text;
using CoHAnalytics.ReferenceDataGenerator;

namespace CoHAnalytics.Tests.ReferenceDataGenerator;

public sealed class HomecomingPowersReaderTests
{
    [Fact]
    public void ReadBoosts_ValidRecord_RecoversExactRequiredFields()
    {
        var data = HomecomingBinaryFixtureBuilder.CreatePowers(
            new SyntheticBoostRecord(
                "Boosts.Crafted_Bonesnap_A.Crafted_Bonesnap_A",
                "P1257397495"));

        var record = Assert.Single(HomecomingPowersReader.ReadBoosts(data));

        Assert.Equal("Boosts.Crafted_Bonesnap_A.Crafted_Bonesnap_A", record.HomecomingSourceId);
        Assert.Equal("P1257397495", record.DisplayNameMessageKey);
        Assert.Equal("Crafted", record.SourceForm);
    }

    [Fact]
    public void ReadBoosts_MultipleRecords_PreservesSourceRecordsAndForms()
    {
        var data = HomecomingBinaryFixtureBuilder.CreatePowers(
            new SyntheticBoostRecord("Boosts.Attuned_Bonesnap_A.Attuned_Bonesnap_A", "P1"),
            new SyntheticBoostRecord(
                "Boosts.Superior_Attuned_Absolute_Amazement_A.Superior_Attuned_Absolute_Amazement_A",
                "P2"));

        var records = HomecomingPowersReader.ReadBoosts(data);

        Assert.Equal(2, records.Count);
        Assert.Equal("Attuned", records[0].SourceForm);
        Assert.Equal("Superior_Attuned", records[1].SourceForm);
    }

    [Fact]
    public void ReadBoosts_TruncatedInput_FailsClearly()
    {
        var data = HomecomingBinaryFixtureBuilder.CreatePowers(
            new SyntheticBoostRecord("Boosts.Crafted_First.Crafted_First", "P1"));

        var exception = Assert.Throws<HomecomingPowersException>(() =>
            HomecomingPowersReader.ReadBoosts(data[..^1]));

        Assert.Contains("beyond", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReadBoosts_DuplicateSourceId_FailsClearly()
    {
        var data = HomecomingBinaryFixtureBuilder.CreatePowers(
            new SyntheticBoostRecord("Boosts.Crafted_First.Crafted_First", "P1"),
            new SyntheticBoostRecord("Boosts.Crafted_First.Crafted_First", "P2"));

        var exception = Assert.Throws<HomecomingPowersException>(() =>
            HomecomingPowersReader.ReadBoosts(data));

        Assert.Contains("duplicated", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class HomecomingBoostSetsReaderTests
{
    [Fact]
    public void Read_ValidSet_RecoversExactFieldsAndMemberGroups()
    {
        var data = HomecomingBinaryFixtureBuilder.CreateBoostSets(
            Set(
                "Bonesnap",
                "P83626102",
                "ECUncommon",
                "ECMelee",
                10,
                25,
                [
                    [
                        "Boosts.Crafted_Bonesnap_A.Crafted_Bonesnap_A",
                        "Boosts.Attuned_Bonesnap_A.Attuned_Bonesnap_A"
                    ]
                ]));

        var record = Assert.Single(HomecomingBoostSetsReader.Read(data));

        Assert.Equal("Bonesnap", record.HomecomingSetId);
        Assert.Equal("P83626102", record.DisplayNameMessageKey);
        Assert.Equal("P_DESCRIPTION", record.DescriptionMessageKey);
        Assert.Equal(["ECUncommon", "ECMelee"], record.ConversionCodes);
        Assert.Equal(10u, record.MinimumLevel);
        Assert.Equal(25u, record.MaximumLevel);
        Assert.Equal(
            [
                "Boosts.Crafted_Bonesnap_A.Crafted_Bonesnap_A",
                "Boosts.Attuned_Bonesnap_A.Attuned_Bonesnap_A"
            ],
            Assert.Single(record.MemberGroups));
    }

    [Fact]
    public void Read_SetWithOneConversionCode_PreservesExactCodeWithoutAssumingItsMeaning()
    {
        var data = HomecomingBinaryFixtureBuilder.CreateBoostSets(
            Set(
                "Hecatomb",
                "P2166787892",
                "ECVeryRare",
                string.Empty,
                50,
                50,
                [["Boosts.Crafted_Hecatomb_A.Crafted_Hecatomb_A"]]));

        var record = Assert.Single(HomecomingBoostSetsReader.Read(data));

        Assert.Equal(["ECVeryRare"], record.ConversionCodes);
    }

    [Fact]
    public void Read_TruncatedInput_FailsClearly()
    {
        var data = HomecomingBinaryFixtureBuilder.CreateBoostSets(
            Set("First", "P1", memberGroups: [["Boosts.Crafted_First.Crafted_First"]]));

        var exception = Assert.Throws<HomecomingBoostSetsException>(() =>
            HomecomingBoostSetsReader.Read(data[..^1]));

        Assert.Contains("beyond", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Read_DuplicateSetId_FailsClearly()
    {
        var data = HomecomingBinaryFixtureBuilder.CreateBoostSets(
            Set("Duplicate", "P1", memberGroups: [["Boosts.Crafted_First.Crafted_First"]]),
            Set("Duplicate", "P2", memberGroups: [["Boosts.Crafted_Second.Crafted_Second"]]));

        var exception = Assert.Throws<HomecomingBoostSetsException>(() =>
            HomecomingBoostSetsReader.Read(data));

        Assert.Contains("duplicated", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static SyntheticBoostSetRecord Set(
        string sourceId,
        string displayKey,
        string rarity = "ECUncommon",
        string category = "ECMelee",
        uint minimumLevel = 10,
        uint maximumLevel = 50,
        IReadOnlyList<IReadOnlyList<string>>? memberGroups = null) =>
        new(
            sourceId,
            displayKey,
            rarity,
            category,
            minimumLevel,
            maximumLevel,
            memberGroups ?? [["Boosts.Crafted_First.Crafted_First"]]);
}

public sealed class HomecomingEnhancementCandidateGeneratorTests
{
    [Fact]
    public void Create_SetMembershipGroupsCraftedAndAttunedAsOneLogicalEnhancement()
    {
        var crafted = Boost("Boosts.Crafted_Bonesnap_A.Crafted_Bonesnap_A", "P_PIECE", "Crafted");
        var attuned = Boost("Boosts.Attuned_Bonesnap_A.Attuned_Bonesnap_A", "P_PIECE", "Attuned");

        var candidate = CreateCandidate(
            [crafted, attuned],
            [Set("Bonesnap", "P_SET", [[crafted.HomecomingSourceId, attuned.HomecomingSourceId]])],
            [("P_PIECE", "Bonesnap: Accuracy/Damage"), ("P_SET", "Bonesnap")]);

        var enhancement = Assert.Single(candidate.Enhancements);
        Assert.Equal("Bonesnap", enhancement.HomecomingEnhancementSetId);
        Assert.Equal(2, enhancement.SourceVariants.Count);
        Assert.Equal(
            [attuned.HomecomingSourceId, crafted.HomecomingSourceId],
            enhancement.SourceVariants.Select(value => value.HomecomingSourceId));
        Assert.Equal(1, candidate.EnhancementSummary.MultiVariantLogicalEnhancements);
    }

    [Fact]
    public void Create_MissingCategoryCodeUsesExactHomecomingDescriptionMessageJoin()
    {
        var special = Boost("Boosts.Crafted_Special_A.Crafted_Special_A", "P_SPECIAL", "Crafted");
        var ordinary = Boost("Boosts.Crafted_Ordinary_A.Crafted_Ordinary_A", "P_ORDINARY", "Crafted");
        var sets = new HomecomingBoostSetRecord[]
        {
            new("Special", "P_SPECIAL_SET", "P_STUNS", ["ECVeryRare"], 50, 50, [[special.HomecomingSourceId]]),
            new("Ordinary", "P_ORDINARY_SET", "P_STUNS", ["ECUncommon", "ECStun"], 10, 50, [[ordinary.HomecomingSourceId]])
        };

        var candidate = CreateCandidate(
            [special, ordinary],
            sets,
            [
                ("P_SPECIAL", "Special: Stun"),
                ("P_ORDINARY", "Ordinary: Stun"),
                ("P_SPECIAL_SET", "Special"),
                ("P_ORDINARY_SET", "Ordinary"),
                ("ECVeryRare", "Rarity: Very Rare"),
                ("ECUncommon", "Rarity: Uncommon"),
                ("ECStun", "Category: Stun")
            ]);

        var set = Assert.Single(candidate.EnhancementSets, value => value.HomecomingSetId == "Special");
        Assert.Equal("ECStun", set.CategoryCode);
        Assert.Equal("ExactDescriptionMessageKeyJoin", set.CategoryCodeSource);
    }

    [Fact]
    public void Create_CategoryOnlyConversionPreservesCategoryWithoutInventingRarity()
    {
        var boost = Boost("Boosts.Attuned_Cupids_Crush_A.Attuned_Cupids_Crush_A", "P_PIECE", "Attuned");
        var set = new HomecomingBoostSetRecord(
            "Cupids_Crush",
            "P_SET",
            "P_GROUP",
            ["ECUniversalDamage"],
            10,
            50,
            [[boost.HomecomingSourceId]]);

        var candidate = CreateCandidate(
            [boost],
            [set],
            [
                ("P_PIECE", "Cupid's Crush: Accuracy/Damage"),
                ("P_SET", "Cupid's Crush"),
                ("ECUniversalDamage", "Category: Universal Damage")
            ]);

        var result = Assert.Single(candidate.EnhancementSets);
        Assert.Null(result.RarityCode);
        Assert.Null(result.RarityDisplayText);
        Assert.Equal("ECUniversalDamage", result.CategoryCode);
        Assert.Equal("ConversionCode", result.CategoryCodeSource);
    }

    [Fact]
    public void Create_BoostSetsPvpRarityCode_NormalizesToCanonicalHomecomingIdentity()
    {
        var boost = Boost("Boosts.Crafted_Gladiators_Strike_A.Crafted_Gladiators_Strike_A", "P_PIECE", "Crafted");
        var set = new HomecomingBoostSetRecord(
            "Gladiators_Strike",
            "P_SET",
            "P_GROUP",
            ["ECPvP", "ECMelee"],
            10,
            50,
            [[boost.HomecomingSourceId]]);

        var candidate = CreateCandidate(
            [boost],
            [set],
            [
                ("P_PIECE", "Gladiator's Strike: Accuracy/Damage"),
                ("P_SET", "Gladiator's Strike"),
                ("ECPVP", "Rarity: PvP")
            ]);

        var result = Assert.Single(candidate.EnhancementSets);
        Assert.Equal("ECPVP", result.RarityCode);
        Assert.Equal("Rarity: PvP", result.RarityDisplayText);
        Assert.Equal(
            "PvP",
            HomecomingPresentationLabels.ResolveRarityDisplayText(result.RarityDisplayText));
    }

    [Fact]
    public void Create_UnknownConversionCode_FailsClearly()
    {
        var boost = Boost("Boosts.Crafted_Unknown_A.Crafted_Unknown_A", "P_PIECE", "Crafted");
        var set = new HomecomingBoostSetRecord(
            "Unknown_Set",
            "P_SET",
            "P_GROUP",
            ["ECUnknown"],
            10,
            50,
            [[boost.HomecomingSourceId]]);

        var exception = Assert.Throws<HomecomingEnhancementCandidateException>(() =>
            CreateCandidate(
                [boost],
                [set],
                [("P_PIECE", "Unknown: Accuracy"), ("P_SET", "Unknown")]));

        Assert.Contains("unknown conversion code", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Create_SuperiorAttunedVariantIsPreservedWithoutCanonicalEnumCoercion()
    {
        var crafted = Boost("Boosts.Crafted_Absolute_Amazement_A.Crafted_Absolute_Amazement_A", "P1", "Crafted");
        var superior = Boost(
            "Boosts.Superior_Attuned_Absolute_Amazement_A.Superior_Attuned_Absolute_Amazement_A",
            "P1",
            "Superior_Attuned");

        var candidate = CreateCandidate(
            [crafted, superior],
            [Set("Absolute_Amazement", "P_SET", [[crafted.HomecomingSourceId, superior.HomecomingSourceId]])],
            [("P1", "Absolute Amazement: Stun Duration"), ("P_SET", "Absolute Amazement")]);

        Assert.Equal(
            ["Crafted", "Superior_Attuned"],
            Assert.Single(candidate.Enhancements).SourceVariants.Select(value => value.SourceForm));
    }

    [Fact]
    public void Create_UnrelatedSimilarBoostsWithoutMembership_DoNotMerge()
    {
        var first = Boost("Boosts.Crafted_Similar_A.Crafted_Similar_A", "P1", "Crafted");
        var second = Boost("Boosts.Crafted_Similar_B.Crafted_Similar_B", "P2", "Crafted");

        var candidate = CreateCandidate(
            [first, second],
            [],
            [("P1", "Similar: Accuracy"), ("P2", "Similar: Accuracy/Damage")]);

        Assert.Equal(2, candidate.Enhancements.Count);
        Assert.All(candidate.Enhancements, value => Assert.Single(value.SourceVariants));
    }

    [Fact]
    public void Create_ConcreteSourceInConflictingGroups_FailsClearly()
    {
        var boost = Boost("Boosts.Crafted_Shared.Crafted_Shared", "P1", "Crafted");

        var exception = Assert.Throws<HomecomingEnhancementCandidateException>(() =>
            CreateCandidate(
                [boost],
                [
                    Set("First", "P_SET1", [[boost.HomecomingSourceId]]),
                    Set("Second", "P_SET2", [[boost.HomecomingSourceId]])
                ],
                [("P1", "Shared"), ("P_SET1", "First"), ("P_SET2", "Second")]));

        Assert.Contains("conflicting logical identities", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Create_SetMemberMissingFromPowers_FailsClearly()
    {
        var exception = Assert.Throws<HomecomingEnhancementCandidateException>(() =>
            CreateCandidate(
                [],
                [Set("First", "P_SET", [["Boosts.Crafted_Missing.Crafted_Missing"]])],
                [("P_SET", "First")]));

        Assert.Contains("not found in powers.bin", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Create_UnresolvedRequiredDisplayMessage_FailsClearly(bool missingBoostMessage)
    {
        var boost = Boost("Boosts.Crafted_First.Crafted_First", "P_BOOST", "Crafted");
        var messages = missingBoostMessage
            ? new[] { ("P_SET", "First Set") }
            : new[] { ("P_BOOST", "First") };

        var exception = Assert.Throws<HomecomingEnhancementCandidateException>(() =>
            CreateCandidate(
                [boost],
                [Set("First_Set", "P_SET", [[boost.HomecomingSourceId]])],
                messages));

        Assert.Contains("unresolved", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Create_ExactCurrentSetAndStructuralEnhancementMatchesPreserveIds()
    {
        var boost = Boost("Boosts.Crafted_Bonesnap_A.Crafted_Bonesnap_A", "P_BOOST", "Crafted");

        var candidate = CreateCandidate(
            [boost],
            [Set("Bonesnap", "P_SET", [[boost.HomecomingSourceId]])],
            [("P_BOOST", "Bonesnap: Accuracy/Damage"), ("P_SET", "Bonesnap")],
            [new CurrentEnhancementIdentity("ENH-00017", "Bonesnap: Accuracy/Damage", "SET-00007", [])],
            [new CurrentEnhancementSetIdentity("SET-00007", "Bonesnap")]);

        Assert.Equal("SET-00007", Assert.Single(candidate.EnhancementSets).AppOwnedId);
        Assert.Equal("ENH-00017", Assert.Single(candidate.Enhancements).AppOwnedId);
        Assert.Equal("MatchedExisting", candidate.Enhancements[0].MatchStatus);
    }

    [Fact]
    public void Create_NewIdentitiesUseDeterministicNextIds()
    {
        var boost = Boost("Boosts.Crafted_New_A.Crafted_New_A", "P_BOOST", "Crafted");

        var candidate = CreateCandidate(
            [boost],
            [Set("New_Set", "P_SET", [[boost.HomecomingSourceId]])],
            [("P_BOOST", "New Set: Accuracy"), ("P_SET", "New Set")],
            [new CurrentEnhancementIdentity("ENH-00009", "Existing", null, [])],
            [new CurrentEnhancementSetIdentity("SET-00004", "Existing Set")]);

        Assert.Equal("SET-00005", Assert.Single(candidate.EnhancementSets).AppOwnedId);
        Assert.Equal("ENH-00010", Assert.Single(candidate.Enhancements).AppOwnedId);
    }

    [Fact]
    public void Create_AmbiguousExistingMatchGetsNoGuessedId()
    {
        var boost = Boost("Boosts.Crafted_First.Crafted_First", "P_BOOST", "Crafted");

        var candidate = CreateCandidate(
            [boost],
            [Set("First_Set", "P_SET", [[boost.HomecomingSourceId]])],
            [("P_BOOST", "Shared"), ("P_SET", "First Set")],
            [
                new CurrentEnhancementIdentity("ENH-00001", "Shared", "SET-00001", []),
                new CurrentEnhancementIdentity("ENH-00002", "Shared", "SET-00001", [])
            ],
            [new CurrentEnhancementSetIdentity("SET-00001", "First Set")]);

        var enhancement = Assert.Single(candidate.Enhancements);
        Assert.Null(enhancement.AppOwnedId);
        Assert.Equal("Ambiguous", enhancement.MatchStatus);
        Assert.Equal(["ENH-00001", "ENH-00002"], enhancement.MatchingExistingAppOwnedIds);
    }

    [Fact]
    public void Create_UnsetBoostDoesNotMatchCurrentSetMemberByNameAlone()
    {
        var boost = Boost("Boosts.Hamidon_First.Hamidon_First", "P_BOOST", "Hamidon");

        var candidate = CreateCandidate(
            [boost],
            [],
            [("P_BOOST", "Shared")],
            [new CurrentEnhancementIdentity("ENH-00001", "Shared", "SET-00001", [])],
            [new CurrentEnhancementSetIdentity("SET-00001", "Existing Set")]);

        var enhancement = Assert.Single(candidate.Enhancements);
        Assert.Equal("NewFromHomecoming", enhancement.MatchStatus);
        Assert.Equal("ENH-00002", enhancement.AppOwnedId);
    }

    [Fact]
    public void Create_CurrentCatalogOnlyRecordsAreReported()
    {
        var boost = Boost("Boosts.Crafted_New.Crafted_New", "P_BOOST", "Crafted");

        var candidate = CreateCandidate(
            [boost],
            [],
            [("P_BOOST", "New")],
            [new CurrentEnhancementIdentity("ENH-00001", "Existing", null, [])],
            [new CurrentEnhancementSetIdentity("SET-00001", "Existing Set")]);

        Assert.Equal("ENH-00001", Assert.Single(candidate.CurrentCatalogEnhancementsNotMatched).AppOwnedId);
        Assert.Equal("SET-00001", Assert.Single(candidate.CurrentCatalogEnhancementSetsNotMatched).AppOwnedId);
    }

    [Fact]
    public void Create_OrdersLogicalEnhancementsAndVariantsOrdinally()
    {
        var zulu = Boost("Boosts.Crafted_Zulu.Crafted_Zulu", "P_Z", "Crafted");
        var alpha = Boost("Boosts.Attuned_Alpha.Attuned_Alpha", "P_A", "Attuned");
        var craftedAlpha = Boost("Boosts.Crafted_Alpha.Crafted_Alpha", "P_A", "Crafted");

        var candidate = CreateCandidate(
            [zulu, craftedAlpha, alpha],
            [Set("Alpha_Set", "P_SET", [[craftedAlpha.HomecomingSourceId, alpha.HomecomingSourceId]])],
            [("P_Z", "Zulu"), ("P_A", "Alpha"), ("P_SET", "Alpha Set")]);

        Assert.Equal(
            [alpha.HomecomingSourceId, zulu.HomecomingSourceId],
            candidate.Enhancements.Select(value => value.SourceVariants[0].HomecomingSourceId));
        Assert.Equal(
            [alpha.HomecomingSourceId, craftedAlpha.HomecomingSourceId],
            candidate.Enhancements[0].SourceVariants.Select(value => value.HomecomingSourceId));
    }

    [Fact]
    public void Serialize_IdenticalInputIsDeterministicAndContainsNoTimestamp()
    {
        var boost = Boost("Boosts.Crafted_First.Crafted_First", "P1", "Crafted");
        var first = CreateCandidate([boost], [], [("P1", "First")]);
        var second = CreateCandidate([boost], [], [("P1", "First")]);

        var firstBytes = HomecomingEnhancementCandidateWriter.Serialize(first);
        var secondBytes = HomecomingEnhancementCandidateWriter.Serialize(second);

        Assert.Equal(firstBytes, secondBytes);
        Assert.DoesNotContain("timestamp", Encoding.UTF8.GetString(firstBytes), StringComparison.OrdinalIgnoreCase);
    }

    private static HomecomingEnhancementCandidateDocument CreateCandidate(
        IReadOnlyList<HomecomingConcreteBoostRecord> boosts,
        IReadOnlyList<HomecomingBoostSetRecord> sets,
        IReadOnlyList<(string Key, string Value)> messages,
        IReadOnlyList<CurrentEnhancementIdentity>? currentEnhancements = null,
        IReadOnlyList<CurrentEnhancementSetIdentity>? currentSets = null,
        IReadOnlyDictionary<string, HomecomingBoostDiscoveryRecord>? discoveryBoosts = null)
    {
        var requiredClassificationMessages = new[]
        {
            (Key: "ECUncommon", Value: "Rarity: Uncommon"),
            (Key: "ECMelee", Value: "Category: Melee")
        };
        var completeMessages = messages
            .Concat(requiredClassificationMessages.Where(required =>
                messages.All(value => value.Key != required.Key)))
            .ToArray();
        return HomecomingEnhancementCandidateGenerator.Create(
            boosts,
            sets,
            HomecomingMessageStoreReader.Read(
                HomecomingBinaryFixtureBuilder.CreateMessageStore(completeMessages)),
            discoveryBoosts ?? BuildDefaultDiscovery(boosts),
            currentEnhancements ?? [],
            currentSets ?? [],
            "Issue 28, Page 3 - 28.3.7927",
            "1.20260707.124731.7927");
    }

    private static IReadOnlyDictionary<string, HomecomingBoostDiscoveryRecord> BuildDefaultDiscovery(
        IReadOnlyList<HomecomingConcreteBoostRecord> boosts) =>
        boosts.ToDictionary(
            boost => boost.HomecomingSourceId,
            boost => new HomecomingBoostDiscoveryRecord(
                boost.HomecomingSourceId,
                boost.DisplayNameMessageKey,
                "P_HELP",
                string.Empty,
                "icon.tga",
                ["Science", "Mutation", "Magic", "Technology", "Natural"],
                [],
                0f,
                0f,
                0f,
                0f),
            StringComparer.Ordinal);

    private static HomecomingConcreteBoostRecord Boost(
        string sourceId,
        string displayKey,
        string sourceForm) =>
        new(sourceId, displayKey, sourceForm);

    private static HomecomingBoostSetRecord Set(
        string sourceId,
        string displayKey,
        IReadOnlyList<IReadOnlyList<string>> memberGroups) =>
        new(sourceId, displayKey, "P_DESCRIPTION", ["ECUncommon", "ECMelee"], 10, 50, memberGroups);
}
