using System.IO;
using System.Text;

namespace CoHAnalytics.HomecomingBinary;

internal static class HomecomingPowerDefinitionReader
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static IReadOnlyList<HomecomingPowerPresentationRecord> ReadPresentations(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        try
        {
            return ReadPresentationsCore(data);
        }
        catch (HomecomingPowersBoostDiscoveryException exception)
        {
            throw new InvalidDataException(
                $"Power presentation data is malformed or truncated: {exception.Message}",
                exception);
        }
    }

    private static IReadOnlyList<HomecomingPowerPresentationRecord> ReadPresentationsCore(byte[] data)
    {

        using var stream = new MemoryStream(data, writable: false);
        using var reader = new BinaryReader(stream, StrictUtf8);
        var stringPool = HomecomingParse7HeaderReader.Read(reader, "Powers Parse7");
        var blockLength = reader.ReadUInt32();
        var blockEnd = checked(stream.Position + blockLength);
        if (blockEnd > stream.Length)
        {
            throw new InvalidDataException("Powers definition block extends beyond the input.");
        }

        var recordCount = reader.ReadUInt32();
        var records = new List<HomecomingPowerPresentationRecord>(checked((int)recordCount));
        for (var index = 0; index < recordCount; index++)
        {
            var recordLength = reader.ReadUInt32();
            var recordStart = checked((int)stream.Position);
            var recordEnd = checked(recordStart + recordLength);
            if (recordEnd > blockEnd)
            {
                throw new InvalidDataException($"Power record {index} extends beyond the definition block.");
            }

            var subReader = new HomecomingParse7SubReader(
                data,
                stringPool,
                recordStart,
                checked((int)recordLength));
            var fields = ReadCommonFields(subReader);
            records.Add(new HomecomingPowerPresentationRecord(
                fields.SourceId,
                fields.DisplayNameMessageKey,
                fields.IconIdentity,
                fields.IsAutoIssued,
                fields.IsFree,
                fields.PowerType));
            stream.Position = recordEnd;
        }

        if (stream.Position != blockEnd)
        {
            throw new InvalidDataException("Powers definition block length does not match its records.");
        }

        return records;
    }

    internal static HomecomingPowerCommonFields ReadCommonFields(HomecomingParse7SubReader reader)
    {
        var sourceId = reader.ReadString("source ID");
        reader.ReadUInt32("crc");
        reader.ReadString("source");
        reader.ReadString("name");
        reader.ReadString("source name");
        reader.ReadUInt32("system");
        var isAutoIssued = reader.ReadBool("auto issue");
        reader.ReadBool("auto issue save level");
        var isFree = reader.ReadBool("free");
        var displayNameMessageKey = reader.ReadString("display name");
        var displayHelpMessageKey = reader.ReadString("display help");
        var shortHelpMessageKey = reader.ReadString("short help");
        reader.ReadString("target help");
        reader.ReadString("target short help");
        reader.ReadString("attacker attack");
        reader.ReadString("attacker attack floater");
        reader.ReadString("attacker hit");
        reader.ReadString("victim hit");
        reader.ReadString("confirm");
        reader.ReadString("float rewarded");
        reader.ReadString("power defense float");
        var iconIdentity = reader.ReadString("icon");
        var powerType = reader.ReadUInt32("power type");
        reader.ReadUInt32("num allowed");

        return new HomecomingPowerCommonFields(
            sourceId,
            displayNameMessageKey,
            displayHelpMessageKey,
            shortHelpMessageKey,
            iconIdentity,
            isAutoIssued,
            isFree,
            powerType);
    }
}

internal readonly record struct HomecomingPowerCommonFields(
    string SourceId,
    string DisplayNameMessageKey,
    string DisplayHelpMessageKey,
    string ShortHelpMessageKey,
    string IconIdentity,
    bool IsAutoIssued,
    bool IsFree,
    uint PowerType);

internal readonly record struct HomecomingPowerPresentationRecord(
    string SourceId,
    string DisplayNameMessageKey,
    string IconIdentity,
    bool IsAutoIssued,
    bool IsFree,
    uint PowerType);
