using CoHAnalytics.Homecoming;
using CoHAnalytics.ReferenceDataGenerator;

namespace CoHAnalytics.Tests.ReferenceDataGenerator;

public sealed class HomecomingPiggMemberReaderTests
{
    [Fact]
    public void ReadMember_ValidCompressedArchive_ReturnsExactMemberBytes()
    {
        var expected = HomecomingBinaryFixtureBuilder.CreateMessageStore(("P1", "First"));
        var archive = HomecomingBinaryFixtureBuilder.CreatePigg(
            ("bin/other.bin", [1, 2, 3]),
            (HomecomingBinaryFixtureBuilder.MessageMemberName, expected));

        var actual = Read(archive, HomecomingBinaryFixtureBuilder.MessageMemberName);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ReadMember_UsesExactOrdinalMemberName()
    {
        var expected = new byte[] { 4, 5, 6 };
        var archive = HomecomingBinaryFixtureBuilder.CreatePigg(
            ($"{HomecomingBinaryFixtureBuilder.MessageMemberName}.backup", [1, 2, 3]),
            (HomecomingBinaryFixtureBuilder.MessageMemberName, expected));

        var actual = Read(archive, HomecomingBinaryFixtureBuilder.MessageMemberName);

        Assert.Equal(expected, actual);
        Assert.Throws<HomecomingPiggException>(() => Read(archive, "BIN/clientmessages-en.bin"));
    }

    [Fact]
    public void ReadMember_MissingMember_FailsClearly()
    {
        var archive = HomecomingBinaryFixtureBuilder.CreatePigg(("bin/other.bin", [1]));

        var exception = Assert.Throws<HomecomingPiggException>(() =>
            Read(archive, HomecomingBinaryFixtureBuilder.MessageMemberName));

        Assert.Contains("was not found", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(HomecomingBinaryFixtureBuilder.MessageMemberName, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadMember_MalformedHeader_FailsClearly()
    {
        var archive = HomecomingBinaryFixtureBuilder.CreatePigg(("bin/other.bin", [1]));
        archive[0] = 0;

        var exception = Assert.Throws<HomecomingPiggException>(() => Read(archive, "bin/other.bin"));

        Assert.Contains("magic", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReadMember_MalformedDirectory_FailsClearly()
    {
        var archive = HomecomingBinaryFixtureBuilder.CreatePigg(("bin/other.bin", [1]));
        archive[16] = 0;

        var exception = Assert.Throws<HomecomingPiggException>(() => Read(archive, "bin/other.bin"));

        Assert.Contains("Entry 0 magic", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReadMember_TruncatedArchive_FailsClearly()
    {
        var archive = HomecomingBinaryFixtureBuilder.CreatePigg(("bin/other.bin", [1, 2, 3]));
        var truncated = archive[..^2];

        var exception = Assert.Throws<HomecomingPiggException>(() => Read(truncated, "bin/other.bin"));

        Assert.Contains("beyond the archive", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReadMember_InvalidZlibPayload_FailsClearly()
    {
        var archive = HomecomingBinaryFixtureBuilder.CreatePigg(("bin/other.bin", [1, 2, 3, 4, 5]));
        archive[^1] ^= 0xff;

        var exception = Assert.Throws<HomecomingPiggException>(() => Read(archive, "bin/other.bin"));

        Assert.Contains("zlib", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReadMember_IdenticalInput_IsDeterministic()
    {
        var archive = HomecomingBinaryFixtureBuilder.CreatePigg(("bin/other.bin", [1, 2, 3]));

        var first = Read(archive, "bin/other.bin");
        var second = Read(archive, "bin/other.bin");

        Assert.Equal(first, second);
    }

    private static byte[] Read(byte[] archive, string memberName)
    {
        using var stream = new MemoryStream(archive, writable: false);
        return HomecomingPiggMemberReader.ReadMember(stream, memberName);
    }
}

public sealed class HomecomingMessageStoreReaderTests
{
    [Fact]
    public void Read_ValidStore_ResolvesExactMessage()
    {
        var data = HomecomingBinaryFixtureBuilder.CreateMessageStore(("P1671735844", "Absolute Amazement"));

        var store = HomecomingMessageStoreReader.Read(data);

        Assert.Equal(1, store.Count);
        Assert.True(store.TryResolve("P1671735844", out var message));
        Assert.Equal("Absolute Amazement", message);
    }

    [Fact]
    public void Read_MultipleMessages_UsesExactOrdinalKeysAndReportsMissingKeys()
    {
        var data = HomecomingBinaryFixtureBuilder.CreateMessageStore(
            ("P1", "First"),
            ("P2", "Second"));

        var store = HomecomingMessageStoreReader.Read(data);

        Assert.Equal(2, store.Count);
        Assert.True(store.TryResolve("P2", out var second));
        Assert.Equal("Second", second);
        Assert.False(store.TryResolve("p2", out _));
        Assert.False(store.TryResolve("P3", out _));
    }

    [Fact]
    public void Read_MalformedHeader_FailsClearly()
    {
        var data = HomecomingBinaryFixtureBuilder.CreateMessageStore(("P1", "First"));
        data[0] = 0;

        var exception = Assert.Throws<HomecomingMessageStoreException>(() =>
            HomecomingMessageStoreReader.Read(data));

        Assert.Contains("format identifier", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Read_MalformedStringPool_FailsClearly()
    {
        var data = HomecomingBinaryFixtureBuilder.CreateMessageStore(("P1", "First"));
        data[4] = 3;

        var exception = Assert.Throws<HomecomingMessageStoreException>(() =>
            HomecomingMessageStoreReader.Read(data));

        Assert.Contains("count does not match", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Read_TruncatedStore_FailsClearly()
    {
        var data = HomecomingBinaryFixtureBuilder.CreateMessageStore(("P1", "First"));

        var exception = Assert.Throws<HomecomingMessageStoreException>(() =>
            HomecomingMessageStoreReader.Read(data[..^1]));

        Assert.Contains("truncated", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Read_DuplicateKeys_AreRejectedAsInvalidStashData()
    {
        var data = HomecomingBinaryFixtureBuilder.CreateMessageStore(
            ("P1", "First"),
            ("P1", "Second"));

        var exception = Assert.Throws<HomecomingMessageStoreException>(() =>
            HomecomingMessageStoreReader.Read(data));

        Assert.Contains("duplicate key", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Read_NonAsciiUtf8Text_IsPreserved()
    {
        var data = HomecomingBinaryFixtureBuilder.CreateMessageStore(
            ("Currency", "Pandora’s Box costs €5 or £4."));

        var store = HomecomingMessageStoreReader.Read(data);

        Assert.True(store.TryResolve("Currency", out var message));
        Assert.Equal("Pandora’s Box costs €5 or £4.", message);
    }

    [Fact]
    public void Read_IdenticalInput_IsDeterministic()
    {
        var data = HomecomingBinaryFixtureBuilder.CreateMessageStore(("P1", "First"));

        var first = HomecomingMessageStoreReader.Read(data);
        var second = HomecomingMessageStoreReader.Read(data);

        Assert.Equal(first.Count, second.Count);
        Assert.True(first.TryResolve("P1", out var firstMessage));
        Assert.True(second.TryResolve("P1", out var secondMessage));
        Assert.Equal(firstMessage, secondMessage);
    }

    [Fact]
    public void PiggToMessageStore_SyntheticEndToEnd_ResolvesExactMapping()
    {
        var messages = HomecomingBinaryFixtureBuilder.CreateMessageStore(
            ("P1671735844", "Absolute Amazement"),
            ("P828916130", "Flares"));
        var archive = HomecomingBinaryFixtureBuilder.CreatePigg(
            (HomecomingBinaryFixtureBuilder.MessageMemberName, messages));

        using var stream = new MemoryStream(archive, writable: false);
        var member = HomecomingPiggMemberReader.ReadMember(
            stream,
            HomecomingBinaryFixtureBuilder.MessageMemberName);
        var store = HomecomingMessageStoreReader.Read(member);

        Assert.Equal(2, store.Count);
        Assert.True(store.TryResolve("P1671735844", out var message));
        Assert.Equal("Absolute Amazement", message);
    }
}
