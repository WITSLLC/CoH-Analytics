using System.IO;
using System.Text;

namespace CoHAnalytics.ReferenceDataGenerator;

/// <summary>
/// Discovery-only exact bonus-tier parser for boostsets.bin.
/// Consumes Requires token arrays (string-pool offsets) that the production
/// discovery reader currently skips incorrectly.
/// Does not modify production promotion behavior.
/// </summary>
internal static class HomecomingBoostSetBonusRequiresDiscoveryReader
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static IReadOnlyList<HomecomingBoostSetBonusRequiresDiscoveryRecord> Read(byte[] data)
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
            var records = new List<HomecomingBoostSetBonusRequiresDiscoveryRecord>(checked((int)recordCount));
            for (var index = 0; index < recordCount; index++)
            {
                var recordLength = ReadUInt32(reader, $"Boost Set record {index} length");
                var recordStart = stream.Position;
                var recordEnd = checked(recordStart + recordLength);
                Require(recordEnd <= blockEnd,
                    $"Boost Set record {index} extends beyond the definition block.");

                var setId = Resolve(reader, stringPool, $"record {index} source ID");
                _ = Resolve(reader, stringPool, $"record '{setId}' display name");
                _ = Resolve(reader, stringPool, $"record '{setId}' description");

                var conversionCodeCount = ReadUInt32(reader, $"Boost Set '{setId}' conversion-code count");
                for (var conversionIndex = 0; conversionIndex < conversionCodeCount; conversionIndex++)
                {
                    _ = Resolve(reader, stringPool, $"record '{setId}' conversion code {conversionIndex}");
                }

                SkipPackedStringArray(reader, recordStart, recordEnd, $"Boost Set '{setId}' allowed powers");

                var groupCount = ReadUInt32(reader, $"Boost Set '{setId}' member-group count");
                for (var groupIndex = 0; groupIndex < groupCount; groupIndex++)
                {
                    var groupLength = ReadUInt32(
                        reader,
                        $"Boost Set '{setId}' member group {groupIndex} length");
                    var groupEnd = checked(reader.BaseStream.Position + groupLength);
                    Require(groupEnd <= recordEnd,
                        $"Boost Set '{setId}' member group {groupIndex} extends beyond the record.");
                    SkipPackedStringArray(
                        reader,
                        recordStart,
                        groupEnd,
                        $"Boost Set '{setId}' member group {groupIndex}");
                    Require(reader.BaseStream.Position == groupEnd,
                        $"Boost Set '{setId}' member group {groupIndex} length does not match.");
                }

                var bonusCount = ReadUInt32(reader, $"Boost Set '{setId}' bonus count");
                var bonuses = new List<HomecomingBoostSetBonusTierRequiresDiscoveryRecord>(
                    checked((int)bonusCount));
                for (var bonusIndex = 0; bonusIndex < bonusCount; bonusIndex++)
                {
                    var bonusLength = ReadUInt32(reader, $"Boost Set '{setId}' bonus {bonusIndex} length");
                    var bonusEnd = checked(reader.BaseStream.Position + bonusLength);
                    Require(bonusEnd <= recordEnd,
                        $"Boost Set '{setId}' bonus {bonusIndex} extends beyond the record.");

                    var leadingUnknown = ReadUInt32(reader, $"bonus {bonusIndex} leading unknown");
                    var minimumBoosts = ReadUInt32(reader, $"bonus {bonusIndex} min_boosts");
                    var maximumBoosts = ReadUInt32(reader, $"bonus {bonusIndex} max_boosts");
                    var requiresCount = ReadUInt32(reader, $"bonus {bonusIndex} requires count");
                    Require(requiresCount <= 256,
                        $"Boost Set '{setId}' bonus {bonusIndex} requires count is implausible.");

                    var requiresTokens = new List<string>(checked((int)requiresCount));
                    var requiresOffsets = new List<uint>(checked((int)requiresCount));
                    for (var tokenIndex = 0; tokenIndex < requiresCount; tokenIndex++)
                    {
                        var offset = ReadUInt32(
                            reader,
                            $"bonus {bonusIndex} requires token {tokenIndex}");
                        requiresOffsets.Add(offset);
                        requiresTokens.Add(
                            offset == 0
                                ? string.Empty
                                : stringPool.Resolve(offset, $"bonus {bonusIndex} requires token {tokenIndex}"));
                    }

                    var autoPowerCount = ReadUInt32(reader, $"bonus {bonusIndex} auto_power count");
                    var autoPowers = new List<string>(checked((int)autoPowerCount));
                    for (var autoPowerIndex = 0; autoPowerIndex < autoPowerCount; autoPowerIndex++)
                    {
                        autoPowers.Add(ReadInlineStringAbsolutePad(
                            reader,
                            recordEnd,
                            $"bonus {bonusIndex} auto_power {autoPowerIndex}"));
                    }

                    var trailingUnknown = ReadUInt32(reader, $"bonus {bonusIndex} trailing unknown");
                    Require(reader.BaseStream.Position == bonusEnd,
                        $"Boost Set '{setId}' bonus {bonusIndex} length does not match parsed fields " +
                        $"(leadingUnknown={leadingUnknown}, trailingUnknown={trailingUnknown}).");

                    bonuses.Add(new HomecomingBoostSetBonusTierRequiresDiscoveryRecord(
                        bonusIndex,
                        leadingUnknown,
                        minimumBoosts,
                        maximumBoosts,
                        requiresOffsets,
                        requiresTokens,
                        autoPowers,
                        trailingUnknown));
                }

                var minimumLevel = ReadUInt32(reader, $"Boost Set '{setId}' minimum level");
                var maximumLevel = ReadUInt32(reader, $"Boost Set '{setId}' maximum level");
                _ = Resolve(reader, stringPool, $"record '{setId}' store product");
                Require(reader.BaseStream.Position == recordEnd,
                    $"Boost Set '{setId}' record length does not match its fields.");

                records.Add(new HomecomingBoostSetBonusRequiresDiscoveryRecord(
                    setId,
                    minimumLevel,
                    maximumLevel,
                    bonuses));
            }

            Require(stream.Position == blockEnd,
                "Boost Sets definition block length does not match its records.");
            return records
                .OrderBy(value => value.HomecomingSetId, StringComparer.Ordinal)
                .ToArray();
        }
        catch (HomecomingBoostSetBonusRequiresDiscoveryException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is EndOfStreamException or IOException or InvalidDataException
                or DecoderFallbackException or OverflowException)
        {
            throw new HomecomingBoostSetBonusRequiresDiscoveryException(
                $"Boost Sets bonus Requires Parse7 data is malformed or truncated: {exception.Message}",
                exception);
        }
    }

    private static void SkipPackedStringArray(
        BinaryReader reader,
        long recordStart,
        long containingEnd,
        string field)
    {
        var count = ReadUInt32(reader, $"{field} count");
        for (var index = 0; index < count; index++)
        {
            var byteLength = ReadUInt16(reader, $"{field} {index} length");
            EnsureWithin(reader, byteLength, containingEnd, $"{field} {index}");
            _ = reader.ReadBytes(byteLength);
            var relativePosition = reader.BaseStream.Position - recordStart;
            var paddingLength = (4 - (relativePosition % 4)) % 4;
            EnsureWithin(reader, paddingLength, containingEnd, $"{field} {index} padding");
            var padding = reader.ReadBytes(checked((int)paddingLength));
            Require(padding.All(value => value == 0), $"{field} {index} padding is invalid.");
        }
    }

    private static string ReadInlineStringAbsolutePad(
        BinaryReader reader,
        long containingEnd,
        string field)
    {
        var byteLength = ReadUInt16(reader, $"{field} length");
        EnsureWithin(reader, byteLength, containingEnd, field);
        var bytes = reader.ReadBytes(byteLength);
        Require(bytes.Length == byteLength, $"{field} is truncated.");
        var value = StrictUtf8.GetString(bytes);
        while (reader.BaseStream.Position % 4 != 0)
        {
            EnsureWithin(reader, 1, containingEnd, $"{field} padding");
            var pad = reader.ReadByte();
            Require(pad == 0, $"{field} padding is invalid.");
        }

        return value;
    }

    private static string Resolve(
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
            throw new HomecomingBoostSetBonusRequiresDiscoveryException(
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
            throw new HomecomingBoostSetBonusRequiresDiscoveryException(
                $"{field} extends beyond its containing record.");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new HomecomingBoostSetBonusRequiresDiscoveryException(message);
        }
    }
}

internal sealed record HomecomingBoostSetBonusRequiresDiscoveryRecord(
    string HomecomingSetId,
    uint MinimumLevel,
    uint MaximumLevel,
    IReadOnlyList<HomecomingBoostSetBonusTierRequiresDiscoveryRecord> Bonuses);

internal sealed record HomecomingBoostSetBonusTierRequiresDiscoveryRecord(
    int BonusIndex,
    uint LeadingUnknown,
    uint MinimumBoosts,
    uint MaximumBoosts,
    IReadOnlyList<uint> RequiresOffsets,
    IReadOnlyList<string> RequiresTokens,
    IReadOnlyList<string> AutoPowerSourceIds,
    uint TrailingUnknown);

internal sealed class HomecomingBoostSetBonusRequiresDiscoveryException : Exception
{
    internal HomecomingBoostSetBonusRequiresDiscoveryException(string message)
        : base(message)
    {
    }

    internal HomecomingBoostSetBonusRequiresDiscoveryException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
