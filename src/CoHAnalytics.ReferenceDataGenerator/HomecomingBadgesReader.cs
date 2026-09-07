using System.IO;
using System.Text;

namespace CoHAnalytics.ReferenceDataGenerator;

internal static class HomecomingBadgesReader
{
    private const int RecordByteLength = 31 * sizeof(uint);
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static IReadOnlyList<HomecomingBadgeRecord> Read(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        try
        {
            using var stream = new MemoryStream(data, writable: false);
            using var reader = new BinaryReader(stream, StrictUtf8);
            var stringPool = HomecomingParse7HeaderReader.Read(reader, "Badges Parse7");
            var blockLength = ReadUInt32(reader, "Badges definition block length");
            var blockEnd = checked(stream.Position + blockLength);
            Require(blockEnd <= stream.Length, "Badges definition block extends beyond the input.");

            var recordCount = ReadUInt32(reader, "Badge record count");
            Require(recordCount <= int.MaxValue, "Badge record count is too large.");
            Require(recordCount <= (blockEnd - stream.Position) / (sizeof(uint) + RecordByteLength),
                "Badge record count exceeds the definition block.");

            var records = new List<HomecomingBadgeRecord>(checked((int)recordCount));
            var sourceIds = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < recordCount; index++)
            {
                var recordLength = ReadUInt32(reader, $"Badge record {index} length");
                Require(recordLength == RecordByteLength,
                    $"Badge record {index} has unsupported length {recordLength}.");
                var recordEnd = checked(stream.Position + recordLength);
                Require(recordEnd <= blockEnd,
                    $"Badge record {index} extends beyond the definition block.");

                var sourcePath = ResolveString(reader, stringPool, $"record {index} source path");
                var sourceId = ResolveString(reader, stringPool, $"record {index} source ID");
                var numericIndex = ReadUInt32(reader, $"Badge '{sourceId}' numeric index");
                _ = ReadUInt32(reader, $"Badge '{sourceId}' flags 0");
                var badgeType = ReadUInt32(reader, $"Badge '{sourceId}' type");
                _ = ResolveString(reader, stringPool, $"record '{sourceId}' hero progress text");
                var heroDescriptionMessageKey = ResolveString(
                    reader,
                    stringPool,
                    $"record '{sourceId}' hero description");
                var heroNameMessageKey = ResolveString(
                    reader,
                    stringPool,
                    $"record '{sourceId}' hero name");
                var heroIcon = ResolveString(reader, stringPool, $"record '{sourceId}' hero icon");
                _ = ResolveString(reader, stringPool, $"record '{sourceId}' villain progress text");
                var villainDescriptionMessageKey = ResolveString(
                    reader,
                    stringPool,
                    $"record '{sourceId}' villain description");
                var villainNameMessageKey = ResolveString(
                    reader,
                    stringPool,
                    $"record '{sourceId}' villain name");
                var villainIcon = ResolveString(
                    reader,
                    stringPool,
                    $"record '{sourceId}' villain icon");

                for (var field = 0; field < 7; field++)
                {
                    _ = ReadUInt32(reader, $"Badge '{sourceId}' numeric field {field}");
                }

                _ = ResolveString(reader, stringPool, $"record '{sourceId}' trailing string");
                for (var field = 0; field < 10; field++)
                {
                    _ = ReadUInt32(reader, $"Badge '{sourceId}' trailing numeric field {field}");
                }

                Require(stream.Position == recordEnd,
                    $"Badge '{sourceId}' record length does not match its fields.");
                Require(sourcePath.Length > 0, $"Badge record {index} has an empty source path.");
                Require(sourceId.Length > 0, $"Badge record {index} has an empty source ID.");
                Require(heroNameMessageKey.Length > 0,
                    $"Badge '{sourceId}' has an empty hero name field.");
                Require(villainNameMessageKey.Length > 0,
                    $"Badge '{sourceId}' has an empty villain name field.");
                Require(sourceIds.Add(sourceId), $"Badge source ID '{sourceId}' is duplicated.");

                records.Add(new HomecomingBadgeRecord(
                    sourcePath,
                    sourceId,
                    numericIndex,
                    badgeType,
                    heroNameMessageKey,
                    villainNameMessageKey,
                    heroDescriptionMessageKey,
                    villainDescriptionMessageKey,
                    heroIcon,
                    villainIcon));
            }

            Require(stream.Position == blockEnd,
                "Badges definition block length does not match its records.");
            Require(blockEnd == stream.Length, "Badges Parse7 data contains trailing bytes.");
            return records;
        }
        catch (HomecomingBadgesException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is EndOfStreamException or IOException or InvalidDataException
                or DecoderFallbackException or OverflowException)
        {
            throw new HomecomingBadgesException(
                $"Badges Parse7 data is malformed or truncated: {exception.Message}",
                exception);
        }
    }

    private static string ResolveString(
        BinaryReader reader,
        HomecomingParse7StringPool stringPool,
        string field) =>
        stringPool.Resolve(ReadUInt32(reader, field), field);

    private static uint ReadUInt32(BinaryReader reader, string field)
    {
        EnsureAvailable(reader, sizeof(uint), field);
        return reader.ReadUInt32();
    }

    private static void EnsureAvailable(BinaryReader reader, long length, string field)
    {
        if (length < 0 || reader.BaseStream.Position > reader.BaseStream.Length - length)
        {
            throw new HomecomingBadgesException(
                $"Badges Parse7 data is truncated while reading {field}.");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new HomecomingBadgesException(message);
        }
    }
}

internal sealed record HomecomingBadgeRecord(
    string SourcePath,
    string HomecomingSourceId,
    uint NumericIndex,
    uint BadgeType,
    string HeroNameMessageKey,
    string VillainNameMessageKey,
    string HeroDescriptionMessageKey,
    string VillainDescriptionMessageKey,
    string HeroIcon,
    string VillainIcon);

internal sealed class HomecomingBadgesException : Exception
{
    internal HomecomingBadgesException(string message)
        : base(message)
    {
    }

    internal HomecomingBadgesException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
