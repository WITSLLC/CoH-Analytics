using System.Text;
using CoHAnalytics.ReferenceDataGenerator;

namespace CoHAnalytics.Tests.ReferenceDataGenerator;

public sealed class HomecomingInspirationReaderTests
{
    [Fact]
    public void ReadInspirations_ValidRecord_RecoversExactRequiredFields()
    {
        var data = HomecomingBinaryFixtureBuilder.CreatePowersDiscovery(
            new SyntheticInspirationDiscoveryRecord(
                "Inspirations.Anniversary.Anniversary_Defense",
                "P2871104909",
                "P2871104909_HELP",
                "P2871104909_SHORT",
                "Inspiration_Anniversary.tga"));

        var record = Assert.Single(HomecomingPowersReader.ReadInspirations(data));

        Assert.Equal("Inspirations.Anniversary.Anniversary_Defense", record.HomecomingSourceId);
        Assert.Equal("P2871104909", record.DisplayNameMessageKey);
        Assert.Equal("Anniversary", record.HomecomingCategory);
        Assert.Null(record.StandardTier);
        Assert.Equal("Special/Event", record.InspirationForm);
        Assert.Equal("Inspiration_Anniversary.tga", record.IconIdentity);
    }

    [Fact]
    public void ReadIdentities_MultipleRecords_SeparatesBoostsAndInspirations()
    {
        var boostData = HomecomingBinaryFixtureBuilder.CreatePowers(
            new SyntheticBoostRecord("Boosts.Crafted_First.Crafted_First", "P_BOOST"));
        var boostIdentities = HomecomingPowersReader.ReadIdentities(boostData);
        Assert.Single(boostIdentities.Boosts);
        Assert.Empty(boostIdentities.Inspirations);

        var inspirationData = HomecomingBinaryFixtureBuilder.CreatePowersDiscovery(
            new SyntheticInspirationDiscoveryRecord("Inspirations.Insp_First.Insp_First", "P_INSP_1"),
            new SyntheticInspirationDiscoveryRecord("Inspirations.Insp_Second.Insp_Second", "P_INSP_2"));
        var inspirationIdentities = HomecomingPowersReader.ReadIdentities(inspirationData);
        Assert.Empty(inspirationIdentities.Boosts);
        Assert.Equal(2, inspirationIdentities.Inspirations.Count);
        Assert.Equal(
            ["Inspirations.Insp_First.Insp_First", "Inspirations.Insp_Second.Insp_Second"],
            inspirationIdentities.Inspirations.Select(record => record.HomecomingSourceId));
    }

    [Fact]
    public void ReadInspirations_TruncatedInput_FailsClearly()
    {
        var data = HomecomingBinaryFixtureBuilder.CreatePowersDiscovery(
            new SyntheticInspirationDiscoveryRecord("Inspirations.Insp_First.Insp_First", "P_INSP"));

        var exception = Assert.Throws<HomecomingPowersException>(() =>
            HomecomingPowersReader.ReadInspirations(data[..^1]));

        Assert.Contains("beyond", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReadInspirations_MalformedHeader_FailsClearly()
    {
        var data = HomecomingBinaryFixtureBuilder.CreatePowersDiscovery(
            new SyntheticInspirationDiscoveryRecord("Inspirations.Insp_First.Insp_First", "P_INSP"));
        data[0] = 0;

        var exception = Assert.Throws<HomecomingPowersException>(() =>
            HomecomingPowersReader.ReadInspirations(data));

        Assert.Contains("signature", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReadInspirations_DuplicateSourceId_FailsClearly()
    {
        const string sourceId = "Inspirations.Insp_Duplicate.Insp_Duplicate";
        var data = HomecomingBinaryFixtureBuilder.CreatePowersDiscovery(
            new SyntheticInspirationDiscoveryRecord(sourceId, "P_INSP_1"),
            new SyntheticInspirationDiscoveryRecord(sourceId, "P_INSP_2"));

        var exception = Assert.Throws<HomecomingPowersException>(() =>
            HomecomingPowersReader.ReadInspirations(data));

        Assert.Contains("duplicated", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(sourceId, exception.Message, StringComparison.Ordinal);
    }
}

public sealed class HomecomingInspirationCandidateGeneratorTests
{
    [Fact]
    public void Create_ResolvesExactMessageAndPreservesHomecomingIdentity()
    {
        var candidate = CreateCandidate(
            [Record("Inspirations.Anniversary.Anniversary_Defense", "P2871104909")],
            [("P2871104909", "Happy Anniversary!")],
            []);

        var record = Assert.Single(candidate.Records);
        Assert.Equal("Inspirations.Anniversary.Anniversary_Defense", record.HomecomingSourceId);
        Assert.Equal("P2871104909", record.DisplayNameMessageKey);
        Assert.Equal("Happy Anniversary!", record.DisplayName);
        Assert.Equal("Anniversary", record.HomecomingCategory);
        Assert.Null(record.StandardTier);
        Assert.Equal("Special/Event", record.InspirationForm);
        Assert.Equal(1, candidate.Summary.ResolvedNames);
    }

    [Fact]
    public void Create_UnresolvedRequiredDisplayMessage_FailsClearly()
    {
        var messages = ReadMessages(("P_OTHER", "Other"));

        var exception = Assert.Throws<HomecomingInspirationCandidateException>(() =>
            HomecomingInspirationCandidateGenerator.Create(
                [Record("Inspirations.Insp_First.Insp_First", "P_MISSING")],
                messages,
                [],
                "Build",
                "Revision"));

        Assert.Contains("P_MISSING", exception.Message, StringComparison.Ordinal);
        Assert.Contains("unresolved", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Create_OrdersRecordsBySourceIdAndAssignsStableNewIds()
    {
        var candidate = CreateCandidate(
            [
                Record("Inspirations.Zulu.Zulu", "P_ZULU"),
                Record("Inspirations.Alpha.Alpha", "P_ALPHA")
            ],
            [("P_ALPHA", "Alpha"), ("P_ZULU", "Zulu")],
            [new CurrentInspirationIdentity("INS-00075", "Existing")]);

        Assert.Equal(
            ["Inspirations.Alpha.Alpha", "Inspirations.Zulu.Zulu"],
            candidate.Records.Select(record => record.HomecomingSourceId));
        Assert.Equal(
            ["INS-00076", "INS-00077"],
            candidate.Records.Select(record => record.AppOwnedId));
        Assert.All(candidate.Records, record =>
            Assert.Equal("NewFromHomecoming", record.MatchStatus));
    }

    [Fact]
    public void Create_ExactUniqueExistingName_PreservesAppOwnedId()
    {
        var candidate = CreateCandidate(
            [Record("Inspirations.Insight.Insight", "P_INSIGHT")],
            [("P_INSIGHT", "Insight")],
            [new CurrentInspirationIdentity("INS-00001", "Insight")]);

        var record = Assert.Single(candidate.Records);
        Assert.Equal("INS-00001", record.AppOwnedId);
        Assert.Equal("MatchedExisting", record.MatchStatus);
        Assert.Equal(["INS-00001"], record.MatchingExistingAppOwnedIds);
    }

    [Fact]
    public void Create_CurrentRecordAbsentFromHomecoming_IsReported()
    {
        var candidate = CreateCandidate(
            [Record("Inspirations.New.New", "P_NEW")],
            [("P_NEW", "New")],
            [new CurrentInspirationIdentity("INS-00001", "Insight")]);

        var unmatched = Assert.Single(candidate.CurrentCatalogNotMatched);
        Assert.Equal("INS-00001", unmatched.AppOwnedId);
        Assert.Equal("Insight", unmatched.DisplayName);
        Assert.Equal(1, candidate.Summary.CurrentCatalogNotMatched);
    }

    [Fact]
    public void Create_AmbiguousExactNameMatch_DoesNotGuessIdentity()
    {
        var candidate = CreateCandidate(
            [Record("Inspirations.Shared.Shared", "P_SHARED")],
            [("P_SHARED", "Shared")],
            [
                new CurrentInspirationIdentity("INS-00001", "Shared"),
                new CurrentInspirationIdentity("INS-00002", "Shared")
            ]);

        var record = Assert.Single(candidate.Records);
        Assert.Null(record.AppOwnedId);
        Assert.Equal("Ambiguous", record.MatchStatus);
        Assert.Equal(["INS-00001", "INS-00002"], record.MatchingExistingAppOwnedIds);
        Assert.Equal(1, candidate.Summary.Ambiguous);
    }

    [Fact]
    public void Serialize_IdenticalInputs_AreByteForByteDeterministic()
    {
        var first = CreateCandidate(
            [Record("Inspirations.First.First", "P_FIRST", "Inspiration_First.tga")],
            [("P_FIRST", "First")],
            []);
        var second = CreateCandidate(
            [Record("Inspirations.First.First", "P_FIRST", "Inspiration_First.tga")],
            [("P_FIRST", "First")],
            []);

        var firstBytes = HomecomingInspirationCandidateWriter.Serialize(first);
        Assert.Equal(firstBytes, HomecomingInspirationCandidateWriter.Serialize(second));
        var json = Encoding.UTF8.GetString(firstBytes);
        Assert.Contains("\"homecomingCategory\"", json, StringComparison.Ordinal);
        Assert.Contains("\"iconIdentity\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("generatedAt", json, StringComparison.OrdinalIgnoreCase);
    }

    private static HomecomingInspirationCandidateDocument CreateCandidate(
        IReadOnlyList<HomecomingConcreteInspirationRecord> records,
        IReadOnlyList<(string Key, string Value)> messages,
        IReadOnlyList<CurrentInspirationIdentity> current) =>
        HomecomingInspirationCandidateGenerator.Create(
            records,
            ReadMessages(messages.ToArray()),
            current,
            "Issue 28, Page 3 - 28.3.7927",
            "1.20260707.124731.7927");

    private static HomecomingMessageStore ReadMessages(params (string Key, string Value)[] messages) =>
        HomecomingMessageStoreReader.Read(
            HomecomingBinaryFixtureBuilder.CreateMessageStore(messages));

    private static HomecomingConcreteInspirationRecord Record(
        string sourceId,
        string messageKey,
        string? icon = null)
    {
        var category = HomecomingInspirationSourceMetadata.ParseHomecomingCategory(sourceId);
        return new HomecomingConcreteInspirationRecord(
            sourceId,
            messageKey,
            null,
            null,
            icon,
            category,
            HomecomingInspirationSourceMetadata.ParseStandardTier(category),
            HomecomingInspirationSourceMetadata.ParseInspirationForm(category));
    }
}
