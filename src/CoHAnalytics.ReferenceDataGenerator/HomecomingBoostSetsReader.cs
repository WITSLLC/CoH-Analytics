using System.IO;
using System.Text;

namespace CoHAnalytics.ReferenceDataGenerator;

internal static class HomecomingBoostSetsReader
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static IReadOnlyList<HomecomingBoostSetRecord> Read(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        try
        {
            using var stream = new MemoryStream(data, writable: false);
            using var reader = new BinaryReader(stream, StrictUtf8);
            var stringPool = HomecomingParse7HeaderReader.Read(reader, "Boost Sets Parse7");
            var blockLength = ReadUInt32(reader, "Boost Sets definition block length");
            var blockEnd = checked(stream.Position + blockLength);
            Require(blockEnd <= stream.Length, "Boost Sets definition block extends beyond the input.");

            var recordCount = ReadUInt32(reader, "Boost Set record count");
            Require(recordCount <= int.MaxValue, "Boost Set record count is too large.");
            Require(recordCount <= (blockEnd - stream.Position) / sizeof(uint),
                "Boost Set record count exceeds the definition block.");

            var records = new List<HomecomingBoostSetRecord>(checked((int)recordCount));
            var setIds = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < recordCount; index++)
            {
                var recordLength = ReadUInt32(reader, $"Boost Set record {index} length");
                var recordStart = stream.Position;
                var recordEnd = checked(recordStart + recordLength);
                Require(recordEnd <= blockEnd,
                    $"Boost Set record {index} extends beyond the definition block.");

                var setId = ResolveString(reader, stringPool, $"record {index} source ID");
                var displayNameMessageKey = ResolveString(
                    reader,
                    stringPool,
                    $"record '{setId}' display name");
                var descriptionMessageKey = ResolveString(
                    reader,
                    stringPool,
                    $"record '{setId}' description");

                var conversionCodeCount = ReadUInt32(
                    reader,
                    $"Boost Set '{setId}' conversion-code count");
                Require(conversionCodeCount is 1 or 2,
                    $"Boost Set '{setId}' must contain one or two conversion codes.");
                var conversionCodes = new List<string>(checked((int)conversionCodeCount));
                for (var conversionIndex = 0; conversionIndex < conversionCodeCount; conversionIndex++)
                {
                    conversionCodes.Add(ResolveString(
                        reader,
                        stringPool,
                        $"record '{setId}' conversion code {conversionIndex}"));
                }

                SkipPackedStringArray(reader, recordStart, recordEnd, $"Boost Set '{setId}' allowed powers");
                var memberGroups = ReadMemberGroups(reader, recordStart, recordEnd, setId);
                SkipBonuses(reader, recordEnd, setId);
                var minimumLevel = ReadUInt32(reader, $"Boost Set '{setId}' minimum level");
                var maximumLevel = ReadUInt32(reader, $"Boost Set '{setId}' maximum level");
                _ = ResolveString(reader, stringPool, $"record '{setId}' store product");

                Require(stream.Position == recordEnd,
                    $"Boost Set '{setId}' record length does not match its fields.");
                Require(setId.Length > 0, $"Boost Set record {index} has an empty source ID.");
                Require(displayNameMessageKey.Length > 0,
                    $"Boost Set '{setId}' has an empty display-name message key.");
                Require(descriptionMessageKey.Length > 0,
                    $"Boost Set '{setId}' has an empty description message key.");
                Require(conversionCodes.All(value => value.Length > 0),
                    $"Boost Set '{setId}' has an empty conversion code.");
                Require(conversionCodes.Distinct(StringComparer.Ordinal).Count() == conversionCodes.Count,
                    $"Boost Set '{setId}' has a duplicate conversion code.");
                Require(minimumLevel <= maximumLevel,
                    $"Boost Set '{setId}' minimum level exceeds its maximum level.");
                Require(memberGroups.Count > 0,
                    $"Boost Set '{setId}' has no logical member groups.");
                Require(setIds.Add(setId), $"Boost Set source ID '{setId}' is duplicated.");

                records.Add(new HomecomingBoostSetRecord(
                    setId,
                    displayNameMessageKey,
                    descriptionMessageKey,
                    conversionCodes,
                    minimumLevel,
                    maximumLevel,
                    memberGroups));
            }

            Require(stream.Position == blockEnd,
                "Boost Sets definition block length does not match its records.");
            Require(blockEnd == stream.Length, "Boost Sets Parse7 data contains trailing bytes.");
            return records;
        }
        catch (HomecomingBoostSetsException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is EndOfStreamException or IOException or InvalidDataException
                or DecoderFallbackException or OverflowException)
        {
            throw new HomecomingBoostSetsException(
                $"Boost Sets Parse7 data is malformed or truncated: {exception.Message}",
                exception);
        }
    }

    private static IReadOnlyList<IReadOnlyList<string>> ReadMemberGroups(
        BinaryReader reader,
        long recordStart,
        long recordEnd,
        string setId)
    {
        var groupCount = ReadUInt32(reader, $"Boost Set '{setId}' member-group count");
        Require(groupCount <= int.MaxValue, $"Boost Set '{setId}' member-group count is too large.");
        Require(groupCount <= (recordEnd - reader.BaseStream.Position) / (2 * sizeof(uint)),
            $"Boost Set '{setId}' member-group count exceeds the record.");
        var groups = new List<IReadOnlyList<string>>(checked((int)groupCount));
        for (var groupIndex = 0; groupIndex < groupCount; groupIndex++)
        {
            var groupLength = ReadUInt32(
                reader,
                $"Boost Set '{setId}' member group {groupIndex} length");
            var groupEnd = checked(reader.BaseStream.Position + groupLength);
            Require(groupEnd <= recordEnd,
                $"Boost Set '{setId}' member group {groupIndex} extends beyond the record.");
            var members = ReadPackedStringArray(
                reader,
                recordStart,
                groupEnd,
                $"Boost Set '{setId}' member group {groupIndex}");
            Require(reader.BaseStream.Position == groupEnd,
                $"Boost Set '{setId}' member group {groupIndex} length does not match its members.");
            Require(members.Count > 0,
                $"Boost Set '{setId}' member group {groupIndex} is empty.");
            Require(members.All(member => member.Length > 0),
                $"Boost Set '{setId}' member group {groupIndex} contains an empty source ID.");
            Require(members.Distinct(StringComparer.Ordinal).Count() == members.Count,
                $"Boost Set '{setId}' member group {groupIndex} contains a duplicate source ID.");
            groups.Add(members);
        }

        return groups;
    }

    private static void SkipBonuses(BinaryReader reader, long recordEnd, string setId)
    {
        var bonusCount = ReadUInt32(reader, $"Boost Set '{setId}' bonus count");
        Require(bonusCount <= (recordEnd - reader.BaseStream.Position) / sizeof(uint),
            $"Boost Set '{setId}' bonus count exceeds the record.");
        for (var index = 0; index < bonusCount; index++)
        {
            var bonusLength = ReadUInt32(reader, $"Boost Set '{setId}' bonus {index} length");
            var bonusEnd = checked(reader.BaseStream.Position + bonusLength);
            Require(bonusEnd <= recordEnd,
                $"Boost Set '{setId}' bonus {index} extends beyond the record.");
            reader.BaseStream.Position = bonusEnd;
        }
    }

    private static void SkipPackedStringArray(
        BinaryReader reader,
        long recordStart,
        long recordEnd,
        string field) =>
        _ = ReadPackedStringArray(reader, recordStart, recordEnd, field);

    private static IReadOnlyList<string> ReadPackedStringArray(
        BinaryReader reader,
        long recordStart,
        long containingEnd,
        string field)
    {
        var count = ReadUInt32(reader, $"{field} count");
        Require(count <= int.MaxValue, $"{field} count is too large.");
        Require(count <= (containingEnd - reader.BaseStream.Position) / sizeof(ushort),
            $"{field} count exceeds its containing record.");
        var values = new List<string>(checked((int)count));
        for (var index = 0; index < count; index++)
        {
            var byteLength = ReadUInt16(reader, $"{field} {index} length");
            EnsureWithin(reader, byteLength, containingEnd, $"{field} {index}");
            var bytes = reader.ReadBytes(byteLength);
            Require(bytes.Length == byteLength, $"{field} {index} is truncated.");
            values.Add(StrictUtf8.GetString(bytes));

            var relativePosition = reader.BaseStream.Position - recordStart;
            var paddingLength = (4 - (relativePosition % 4)) % 4;
            EnsureWithin(reader, paddingLength, containingEnd, $"{field} {index} padding");
            var padding = reader.ReadBytes(checked((int)paddingLength));
            Require(padding.All(value => value == 0), $"{field} {index} padding is invalid.");
        }

        return values;
    }

    private static string ResolveString(
        BinaryReader reader,
        HomecomingParse7StringPool stringPool,
        string field) =>
        stringPool.Resolve(ReadUInt32(reader, field), field);

    private static ushort ReadUInt16(BinaryReader reader, string field)
    {
        EnsureAvailable(reader, sizeof(ushort), field);
        return reader.ReadUInt16();
    }

    private static uint ReadUInt32(BinaryReader reader, string field)
    {
        EnsureAvailable(reader, sizeof(uint), field);
        return reader.ReadUInt32();
    }

    private static void EnsureAvailable(BinaryReader reader, long length, string field)
    {
        if (length < 0 || reader.BaseStream.Position > reader.BaseStream.Length - length)
        {
            throw new HomecomingBoostSetsException(
                $"Boost Sets Parse7 data is truncated while reading {field}.");
        }
    }

    private static void EnsureWithin(
        BinaryReader reader,
        long length,
        long containingEnd,
        string field)
    {
        if (length < 0 || reader.BaseStream.Position > containingEnd - length)
        {
            throw new HomecomingBoostSetsException($"{field} extends beyond its containing record.");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new HomecomingBoostSetsException(message);
        }
    }
}

internal sealed record HomecomingBoostSetRecord(
    string HomecomingSetId,
    string DisplayNameMessageKey,
    string DescriptionMessageKey,
    IReadOnlyList<string> ConversionCodes,
    uint MinimumLevel,
    uint MaximumLevel,
    IReadOnlyList<IReadOnlyList<string>> MemberGroups);

internal sealed class HomecomingBoostSetsException : Exception
{
    internal HomecomingBoostSetsException(string message)
        : base(message)
    {
    }

    internal HomecomingBoostSetsException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
