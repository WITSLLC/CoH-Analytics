using CoHAnalytics.ReferenceDataGenerator;

namespace CoHAnalytics.Tests.ReferenceDataGenerator;

public sealed class HomecomingSalvageReaderTests
{
    [Fact]
    public void Read_ValidRecord_RecoversExactRequiredFields()
    {
        var data = HomecomingBinaryFixtureBuilder.CreateSalvage(
            new SyntheticSalvageRecord(
                "S_AbberantTech",
                "P924635081",
                "salvage_AbberantTech.tga",
                Rarity: 1,
                Category: 0));

        var record = Assert.Single(HomecomingSalvageReader.Read(data));

        Assert.Equal("S_AbberantTech", record.HomecomingSourceId);
        Assert.Equal("P924635081", record.DisplayNameMessageKey);
        Assert.Equal("salvage_AbberantTech.tga", record.Icon);
        Assert.Equal(HomecomingSalvageRarity.Common, record.Rarity);
        Assert.Equal(HomecomingSalvageCategory.Legacy, record.Category);
    }

    [Fact]
    public void Read_MultipleRecords_PreservesSourceOrderAndEnums()
    {
        var data = HomecomingBinaryFixtureBuilder.CreateSalvage(
            new SyntheticSalvageRecord("S_First", "P1", "first.tga", 2, 1),
            new SyntheticSalvageRecord("S_Second", "P2", "second.tga", 4, 3));

        var records = HomecomingSalvageReader.Read(data);

        Assert.Equal(2, records.Count);
        Assert.Equal("S_First", records[0].HomecomingSourceId);
        Assert.Equal(HomecomingSalvageRarity.Uncommon, records[0].Rarity);
        Assert.Equal(HomecomingSalvageCategory.Invention, records[0].Category);
        Assert.Equal("S_Second", records[1].HomecomingSourceId);
        Assert.Equal(HomecomingSalvageRarity.VeryRare, records[1].Rarity);
        Assert.Equal(HomecomingSalvageCategory.Incarnate, records[1].Category);
    }

    [Fact]
    public void Read_MalformedHeader_FailsClearly()
    {
        var data = HomecomingBinaryFixtureBuilder.CreateSalvage(
            new SyntheticSalvageRecord("S_First", "P1", "first.tga"));
        data[0] = 0;

        var exception = Assert.Throws<HomecomingSalvageException>(() =>
            HomecomingSalvageReader.Read(data));

        Assert.Contains("signature", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Read_TruncatedRecord_FailsClearly()
    {
        var data = HomecomingBinaryFixtureBuilder.CreateSalvage(
            new SyntheticSalvageRecord("S_First", "P1", "first.tga"));

        var exception = Assert.Throws<HomecomingSalvageException>(() =>
            HomecomingSalvageReader.Read(data[..^1]));

        Assert.Contains("beyond", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Read_DuplicateSourceId_FailsClearly()
    {
        var data = HomecomingBinaryFixtureBuilder.CreateSalvage(
            new SyntheticSalvageRecord("S_Duplicate", "P1", "first.tga"),
            new SyntheticSalvageRecord("S_Duplicate", "P2", "second.tga"));

        var exception = Assert.Throws<HomecomingSalvageException>(() =>
            HomecomingSalvageReader.Read(data));

        Assert.Contains("duplicated", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("S_Duplicate", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(5u, 0u, "rarity")]
    [InlineData(1u, 4u, "category")]
    public void Read_UnknownRequiredEnum_FailsClearly(uint rarity, uint category, string expectedField)
    {
        var data = HomecomingBinaryFixtureBuilder.CreateSalvage(
            new SyntheticSalvageRecord("S_First", "P1", "first.tga", rarity, category));

        var exception = Assert.Throws<HomecomingSalvageException>(() =>
            HomecomingSalvageReader.Read(data));

        Assert.Contains(expectedField, exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class HomecomingSalvageCandidateGeneratorTests
{
    [Fact]
    public void Create_ResolvesMessageAndPreservesHomecomingFields()
    {
        var candidate = CreateCandidate(
            [Record("S_AbberantTech", "P924635081", "salvage_AbberantTech.tga")],
            [("P924635081", "Aberrant Tech")],
            []);

        var record = Assert.Single(candidate.Records);
        Assert.Equal("Aberrant Tech", record.DisplayName);
        Assert.Equal("P924635081", record.DisplayNameMessageKey);
        Assert.Equal("Common", record.Rarity);
        Assert.Equal("Legacy", record.Category);
        Assert.Equal("salvage_AbberantTech.tga", record.Icon);
        Assert.Equal(1, candidate.Summary.ResolvedNames);
    }

    [Fact]
    public void Create_UnresolvedRequiredDisplayMessage_FailsClearly()
    {
        var messages = ReadMessages(("P_OTHER", "Other"));

        var exception = Assert.Throws<HomecomingSalvageCandidateException>(() =>
            HomecomingSalvageCandidateGenerator.Create(
                [Record("S_First", "P_MISSING", "first.tga")],
                messages,
                [],
                "Build",
                "Revision"));

        Assert.Contains("P_MISSING", exception.Message, StringComparison.Ordinal);
        Assert.Contains("unresolved", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Create_OrdersRecordsByHomecomingSourceIdAndAssignsStableNewIds()
    {
        var candidate = CreateCandidate(
            [
                Record("S_Zulu", "P2", "zulu.tga"),
                Record("S_Alpha", "P1", "alpha.tga")
            ],
            [("P1", "Alpha"), ("P2", "Zulu")],
            []);

        Assert.Equal(["S_Alpha", "S_Zulu"],
            candidate.Records.Select(record => record.HomecomingSourceId));
        Assert.Equal(["SAL-00001", "SAL-00002"],
            candidate.Records.Select(record => record.AppOwnedId));
        Assert.All(candidate.Records, record =>
            Assert.Equal("NewFromHomecoming", record.MatchStatus));
    }

    [Fact]
    public void Serialize_IdenticalInputs_AreByteForByteDeterministic()
    {
        var first = CreateCandidate(
            [Record("S_First", "P1", "first.tga")],
            [("P1", "First")],
            []);
        var second = CreateCandidate(
            [Record("S_First", "P1", "first.tga")],
            [("P1", "First")],
            []);

        Assert.Equal(
            HomecomingSalvageCandidateWriter.Serialize(first),
            HomecomingSalvageCandidateWriter.Serialize(second));
    }

    [Fact]
    public void Create_ExactUniqueExistingName_PreservesAppOwnedId()
    {
        var candidate = CreateCandidate(
            [Record("S_First", "P1", "first.tga")],
            [("P1", "First")],
            [new CurrentSalvageIdentity("SAL-00007", "First")]);

        var record = Assert.Single(candidate.Records);
        Assert.Equal("SAL-00007", record.AppOwnedId);
        Assert.Equal("MatchedExisting", record.MatchStatus);
        Assert.Equal(1, candidate.Summary.MatchedExisting);
    }

    [Fact]
    public void Create_NewHomecomingRecord_UsesExistingNextIdConvention()
    {
        var candidate = CreateCandidate(
            [Record("S_New", "P1", "new.tga")],
            [("P1", "New")],
            [new CurrentSalvageIdentity("SAL-00005", "Existing")]);

        var record = Assert.Single(candidate.Records);
        Assert.Equal("SAL-00006", record.AppOwnedId);
        Assert.Equal("NewFromHomecoming", record.MatchStatus);
        Assert.Equal(1, candidate.Summary.NewFromHomecoming);
    }

    [Fact]
    public void Create_CurrentRecordAbsentFromHomecoming_IsReported()
    {
        var candidate = CreateCandidate(
            [Record("S_New", "P1", "new.tga")],
            [("P1", "New")],
            [new CurrentSalvageIdentity("SAL-00005", "Existing")]);

        var unmatched = Assert.Single(candidate.CurrentCatalogNotMatched);
        Assert.Equal("SAL-00005", unmatched.AppOwnedId);
        Assert.Equal("Existing", unmatched.DisplayName);
        Assert.Equal(1, candidate.Summary.CurrentCatalogNotMatched);
    }

    [Fact]
    public void Create_AmbiguousNameMatch_DoesNotGuessIdentity()
    {
        var candidate = CreateCandidate(
            [
                Record("S_First", "P1", "first.tga"),
                Record("S_Second", "P2", "second.tga")
            ],
            [("P1", "Shared"), ("P2", "Shared")],
            [new CurrentSalvageIdentity("SAL-00009", "Shared")]);

        Assert.Equal(2, candidate.Summary.Ambiguous);
        Assert.All(candidate.Records, record =>
        {
            Assert.Null(record.AppOwnedId);
            Assert.Equal("Ambiguous", record.MatchStatus);
            Assert.Equal(["SAL-00009"], record.MatchingExistingAppOwnedIds);
        });
    }

    private static HomecomingSalvageCandidateDocument CreateCandidate(
        IReadOnlyList<HomecomingSalvageRecord> records,
        IReadOnlyList<(string Key, string Value)> messages,
        IReadOnlyList<CurrentSalvageIdentity> current) =>
        HomecomingSalvageCandidateGenerator.Create(
            records,
            ReadMessages(messages.ToArray()),
            current,
            "Issue 28, Page 3 - 28.3.7927",
            "1.20260707.124731.7927");

    private static HomecomingMessageStore ReadMessages(params (string Key, string Value)[] messages) =>
        HomecomingMessageStoreReader.Read(
            HomecomingBinaryFixtureBuilder.CreateMessageStore(messages));

    private static HomecomingSalvageRecord Record(
        string sourceId,
        string messageKey,
        string icon) =>
        new(
            sourceId,
            messageKey,
            icon,
            HomecomingSalvageRarity.Common,
            HomecomingSalvageCategory.Legacy);
}
