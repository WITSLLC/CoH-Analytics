using System.Text;
using CoHAnalytics.ReferenceDataGenerator;

namespace CoHAnalytics.Tests.ReferenceDataGenerator;

public sealed class HomecomingBadgesReaderTests
{
    [Fact]
    public void Read_ValidBadge_RecoversExactProvenFields()
    {
        var data = HomecomingBinaryFixtureBuilder.CreateBadges(
            new SyntheticBadgeRecord(
                "AtlasParkTour1",
                category: "TOURISM",
                numericIndex: 86,
                badgeType: 1,
                heroNameMessageKey: "P_HERO_NAME",
                villainNameMessageKey: "P_VILLAIN_NAME",
                heroDescriptionMessageKey: "P_HERO_DESCRIPTION",
                villainDescriptionMessageKey: "P_VILLAIN_DESCRIPTION",
                heroIcon: "badge_tourist_01",
                villainIcon: "v_badge_tourist_01"));

        var record = Assert.Single(HomecomingBadgesReader.Read(data));

        Assert.Equal("AtlasParkTour1", record.HomecomingSourceId);
        Assert.Equal("DEFS/BADGES/BADGES_TOURISM.DEF", record.SourcePath);
        Assert.Equal(86u, record.NumericIndex);
        Assert.Equal(1u, record.BadgeType);
        Assert.Equal("P_HERO_NAME", record.HeroNameMessageKey);
        Assert.Equal("P_VILLAIN_NAME", record.VillainNameMessageKey);
        Assert.Equal("P_HERO_DESCRIPTION", record.HeroDescriptionMessageKey);
        Assert.Equal("P_VILLAIN_DESCRIPTION", record.VillainDescriptionMessageKey);
        Assert.Equal("badge_tourist_01", record.HeroIcon);
        Assert.Equal("v_badge_tourist_01", record.VillainIcon);
    }

    [Fact]
    public void Read_TruncatedRecord_FailsClearly()
    {
        var data = HomecomingBinaryFixtureBuilder.CreateBadges(
            new SyntheticBadgeRecord("Badge_Truncated"));

        var exception = Assert.Throws<HomecomingBadgesException>(() =>
            HomecomingBadgesReader.Read(data[..^1]));

        Assert.Contains("beyond", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Read_InvalidStringReference_FailsClearly()
    {
        var data = HomecomingBinaryFixtureBuilder.CreateBadges(
            new SyntheticBadgeRecord("Badge_Invalid_String"));
        var poolLength = BitConverter.ToUInt32(data, 20);
        var afterPool = checked(24 + (int)poolLength + ((4 - ((int)poolLength % 4)) % 4));
        var recordBody = afterPool + 12;
        BitConverter.GetBytes(uint.MaxValue).CopyTo(data, recordBody + (7 * sizeof(uint)));

        var exception = Assert.Throws<HomecomingBadgesException>(() =>
            HomecomingBadgesReader.Read(data));

        Assert.Contains("invalid string-pool offset", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Read_DuplicateSourceId_FailsClearly()
    {
        var data = HomecomingBinaryFixtureBuilder.CreateBadges(
            new SyntheticBadgeRecord("Badge_Duplicate"),
            new SyntheticBadgeRecord("Badge_Duplicate", category: "HISTORY"));

        var exception = Assert.Throws<HomecomingBadgesException>(() =>
            HomecomingBadgesReader.Read(data));

        Assert.Contains("duplicated", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class HomecomingBadgeCandidateGeneratorTests
{
    [Fact]
    public void Create_ValidBadge_ResolvesNamesDescriptionsAndPreservesSourceCategory()
    {
        var result = Create([new SyntheticBadgeRecord("AtlasParkTour1")]);

        var record = Assert.Single(result.Records);
        Assert.Equal("BAD-00001", record.AppOwnedId);
        Assert.Equal("AtlasParkTour1", record.HomecomingSourceId);
        Assert.Equal("TOURISM", record.CanonicalCategory);
        Assert.Equal("DEFS/BADGES/BADGES_TOURISM.DEF", record.SourcePath);
        Assert.Equal("Hero Name", record.HeroName);
        Assert.Equal("Villain Name", record.VillainName);
        Assert.Equal("Hero Description", record.HeroDescription);
        Assert.Equal("Villain Description", record.VillainDescription);
        Assert.Equal("badge_hero", record.HeroIcon);
        Assert.Equal("badge_villain", record.VillainIcon);
        Assert.False(result.Source.CanonicalZoneAssociationAvailable);
    }

    [Fact]
    public void Create_UnknownHomecomingCategory_IsPreservedRatherThanGuessed()
    {
        var result = Create([new SyntheticBadgeRecord("Badge_Future", category: "FUTURE_CATEGORY")]);

        var record = Assert.Single(result.Records);
        Assert.Equal("FUTURE_CATEGORY", record.CanonicalCategory);
        Assert.Equal("DEFS/BADGES/BADGES_FUTURE_CATEGORY.DEF", record.SourcePath);
    }

    [Fact]
    public void Create_UnresolvedRequiredName_FailsWithoutFallback()
    {
        var messages = Messages(("P_OTHER", "Other"));
        var records = HomecomingBadgesReader.Read(
            HomecomingBinaryFixtureBuilder.CreateBadges(
                new SyntheticBadgeRecord("Badge_Missing_Name")));

        var exception = Assert.Throws<HomecomingBadgeCandidateException>(() =>
            HomecomingBadgeCandidateGenerator.Create(
                records, messages, [], "Issue 28", "1.2.3"));

        Assert.Contains("P_HERO_NAME", exception.Message, StringComparison.Ordinal);
        Assert.Contains("unresolved", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Create_OptionalMissingDescriptions_AreNull()
    {
        var result = Create(
            [
                new SyntheticBadgeRecord(
                    "Badge_No_Description",
                    heroDescriptionMessageKey: string.Empty,
                    villainDescriptionMessageKey: string.Empty)
            ]);

        var record = Assert.Single(result.Records);
        Assert.Null(record.HeroDescriptionMessageKey);
        Assert.Null(record.HeroDescription);
        Assert.Null(record.VillainDescriptionMessageKey);
        Assert.Null(record.VillainDescription);
        Assert.Equal(0, result.Summary.RecordsWithResolvedDescriptions);
    }

    [Fact]
    public void Create_NonPlayerFacingSentinel_IsReportedAndNotCandidate()
    {
        var result = Create(
            [
                new SyntheticBadgeRecord(
                    "Internal_Counter",
                    category: "INTERNAL",
                    heroNameMessageKey: ".",
                    villainNameMessageKey: ".",
                    heroDescriptionMessageKey: string.Empty,
                    villainDescriptionMessageKey: string.Empty,
                    heroIcon: string.Empty,
                    villainIcon: string.Empty)
            ]);

        Assert.Empty(result.Records);
        Assert.Equal(1, result.Summary.TotalLiveBadgeRecords);
        Assert.Equal(1, result.Summary.NonPlayerFacingDefinitions);
        Assert.Equal(0, result.Summary.UnresolvedOrInvalid);
    }

    [Fact]
    public void Create_ExactSourceBinding_PreservesExistingBadgeId()
    {
        var result = Create(
            [new SyntheticBadgeRecord("Badge_Source_Bound")],
            [new("BAD-00042", "Old Name", "HISTORY", "Badge_Source_Bound")]);

        var record = Assert.Single(result.Records);
        Assert.Equal("BAD-00042", record.AppOwnedId);
        Assert.Equal("MatchedExisting", record.MatchStatus);
    }

    [Fact]
    public void Create_SameDisplayName_UsesCompatibleHomecomingCategoryOnly()
    {
        var records = new[]
        {
            new SyntheticBadgeRecord("Badge_Tourism", category: "TOURISM"),
            new SyntheticBadgeRecord("Badge_History", category: "HISTORY", badgeType: 2)
        };
        var current = new[]
        {
            new CurrentBadgeIdentity("BAD-00010", "Hero Name", "HISTORY", null)
        };

        var result = Create(records, current);

        Assert.Equal("BAD-00010", result.Records.Single(value =>
            value.HomecomingSourceId == "Badge_History").AppOwnedId);
        Assert.Equal("BAD-00011", result.Records.Single(value =>
            value.HomecomingSourceId == "Badge_Tourism").AppOwnedId);
    }

    [Fact]
    public void Create_AmbiguousOldNameMatch_DoesNotGuess()
    {
        var result = Create(
            [
                new SyntheticBadgeRecord("Badge_One"),
                new SyntheticBadgeRecord("Badge_Two")
            ],
            [new("BAD-00005", "Hero Name", "TOURISM", null)]);

        Assert.All(result.Records, record =>
        {
            Assert.Null(record.AppOwnedId);
            Assert.Equal("Ambiguous", record.MatchStatus);
            Assert.Equal(["BAD-00005"], record.MatchingExistingAppOwnedIds);
        });
        Assert.Empty(result.CurrentCatalogNotMatched);
    }

    [Fact]
    public void Create_NewIdsAndOrdering_AreStableAndNeverReuseExistingIds()
    {
        var source = new[]
        {
            new SyntheticBadgeRecord("Badge_Z"),
            new SyntheticBadgeRecord("Badge_A")
        };
        var current = new[]
        {
            new CurrentBadgeIdentity("BAD-00007", "Current Only", null, null)
        };

        var first = Create(source, current);
        var second = Create(source.Reverse().ToArray(), current);

        Assert.Equal(
            [("Badge_A", "BAD-00008"), ("Badge_Z", "BAD-00009")],
            first.Records.Select(record => (record.HomecomingSourceId, record.AppOwnedId)));
        Assert.Equal(
            HomecomingBadgeCandidateWriter.Serialize(first),
            HomecomingBadgeCandidateWriter.Serialize(second));
        Assert.Equal("BAD-00007", Assert.Single(first.CurrentCatalogNotMatched).AppOwnedId);
    }

    [Fact]
    public void Serialize_IsDeterministicAndContainsNoTimestamp()
    {
        var result = Create([new SyntheticBadgeRecord("Badge_Deterministic")]);

        var first = HomecomingBadgeCandidateWriter.Serialize(result);
        var second = HomecomingBadgeCandidateWriter.Serialize(result);

        Assert.Equal(first, second);
        Assert.DoesNotContain(
            "generatedAt",
            Encoding.UTF8.GetString(first),
            StringComparison.OrdinalIgnoreCase);
    }

    private static HomecomingBadgeCandidateDocument Create(
        IReadOnlyList<SyntheticBadgeRecord> source,
        IReadOnlyList<CurrentBadgeIdentity>? current = null)
    {
        var records = HomecomingBadgesReader.Read(
            HomecomingBinaryFixtureBuilder.CreateBadges(source.ToArray()));
        return HomecomingBadgeCandidateGenerator.Create(
            records,
            Messages(
                ("P_HERO_NAME", "Hero Name"),
                ("P_VILLAIN_NAME", "Villain Name"),
                ("P_HERO_DESCRIPTION", "Hero Description"),
                ("P_VILLAIN_DESCRIPTION", "Villain Description")),
            current ?? [],
            "Issue 28",
            "1.2.3");
    }

    private static HomecomingMessageStore Messages(
        params (string Key, string Value)[] messages) =>
        HomecomingMessageStoreReader.Read(
            HomecomingBinaryFixtureBuilder.CreateMessageStore(messages));
}
