using System.IO;
using System.Text;

namespace CoHAnalytics.HomecomingBinary;

internal static class HomecomingPowersetDefinitionReader
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static IReadOnlyList<HomecomingPowersetPresentationRecord> ReadPresentations(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        try
        {
            return ReadPresentationsCore(data);
        }
        catch (HomecomingPowersBoostDiscoveryException exception)
        {
            throw new InvalidDataException(
                $"Powerset presentation data is malformed or truncated: {exception.Message}",
                exception);
        }
    }

    private static IReadOnlyList<HomecomingPowersetPresentationRecord> ReadPresentationsCore(byte[] data)
    {

        using var stream = new MemoryStream(data, writable: false);
        using var reader = new BinaryReader(stream, StrictUtf8);
        var stringPool = HomecomingParse7HeaderReader.Read(reader, "Powersets Parse7");
        var blockLength = reader.ReadUInt32();
        var blockEnd = checked(stream.Position + blockLength);
        if (blockEnd > stream.Length)
        {
            throw new InvalidDataException("Powersets definition block extends beyond the input.");
        }

        var recordCount = reader.ReadUInt32();
        var records = new List<HomecomingPowersetPresentationRecord>(checked((int)recordCount));
        for (var index = 0; index < recordCount; index++)
        {
            var recordLength = reader.ReadUInt32();
            var recordStart = checked((int)stream.Position);
            var recordEnd = checked(recordStart + recordLength);
            if (recordEnd > blockEnd)
            {
                throw new InvalidDataException($"Powerset record {index} extends beyond the definition block.");
            }

            var subReader = new HomecomingParse7SubReader(
                data,
                stringPool,
                recordStart,
                checked((int)recordLength));
            subReader.ReadString("source path");
            var sourceId = subReader.ReadString("source ID");
            subReader.ReadString("name");
            subReader.ReadString("system name");
            subReader.ReadString("source name");
            var displayNameMessageKey = subReader.ReadString("display name");
            records.Add(new HomecomingPowersetPresentationRecord(sourceId, displayNameMessageKey));
            stream.Position = recordEnd;
        }

        if (stream.Position != blockEnd)
        {
            throw new InvalidDataException("Powersets definition block length does not match its records.");
        }

        return records;
    }
}

internal readonly record struct HomecomingPowersetPresentationRecord(
    string SourceId,
    string DisplayNameMessageKey);
