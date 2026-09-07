using System.IO;
using System.Text;

namespace CoHAnalytics.ReferenceDataGenerator;

internal static class HomecomingSalvageReader
{
    private const int RequiredRecordBytes = 9 * sizeof(uint);
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static IReadOnlyList<HomecomingSalvageRecord> Read(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        try
        {
            using var stream = new MemoryStream(data, writable: false);
            using var reader = new BinaryReader(stream, StrictUtf8);

            var stringPool = HomecomingParse7HeaderReader.Read(reader, "Salvage Parse7");
            var blockLength = ReadUInt32(reader, "definition block length");
            var blockEnd = checked(stream.Position + blockLength);
            Require(blockEnd <= stream.Length, "Salvage definition block extends beyond the input.");

            var recordCount = ReadUInt32(reader, "Salvage record count");
            Require(recordCount <= int.MaxValue, "Salvage record count is too large.");
            Require(recordCount <= (blockEnd - stream.Position) / (sizeof(uint) + RequiredRecordBytes),
                "Salvage record count exceeds the definition block.");

            var records = new List<HomecomingSalvageRecord>(checked((int)recordCount));
            var sourceIds = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < recordCount; index++)
            {
                var recordLength = ReadUInt32(reader, $"Salvage record {index} length");
                Require(recordLength >= RequiredRecordBytes,
                    $"Salvage record {index} is shorter than the required fields.");
                var recordEnd = checked(stream.Position + recordLength);
                Require(recordEnd <= blockEnd,
                    $"Salvage record {index} extends beyond the definition block.");

                var sourceIdOffset = ReadUInt32(reader, $"Salvage record {index} source ID");
                var displayNameOffset = ReadUInt32(reader, $"Salvage record {index} display name");
                _ = ReadUInt32(reader, $"Salvage record {index} display help");
                _ = ReadUInt32(reader, $"Salvage record {index} short help");
                var iconOffset = ReadUInt32(reader, $"Salvage record {index} icon");
                _ = ReadUInt32(reader, $"Salvage record {index} type message");
                _ = ReadUInt32(reader, $"Salvage record {index} flags");
                var rarityValue = ReadUInt32(reader, $"Salvage record {index} rarity");
                var categoryValue = ReadUInt32(reader, $"Salvage record {index} category");

                var sourceId = stringPool.Resolve(sourceIdOffset, $"record {index} source ID");
                var displayNameMessageKey = stringPool.Resolve(
                    displayNameOffset,
                    $"record {index} display name");
                var icon = stringPool.Resolve(iconOffset, $"record {index} icon");
                Require(sourceId.Length > 0, $"Salvage record {index} has an empty source ID.");
                Require(displayNameMessageKey.Length > 0,
                    $"Salvage record '{sourceId}' has an empty display-name message key.");
                Require(icon.Length > 0, $"Salvage record '{sourceId}' has an empty icon identity.");
                Require(sourceIds.Add(sourceId), $"Salvage source ID '{sourceId}' is duplicated.");

                records.Add(new HomecomingSalvageRecord(
                    sourceId,
                    displayNameMessageKey,
                    icon,
                    ReadRarity(rarityValue, sourceId),
                    ReadCategory(categoryValue, sourceId)));

                stream.Position = recordEnd;
            }

            Require(stream.Position == blockEnd,
                "Salvage definition block length does not match its records.");
            Require(blockEnd == stream.Length, "Salvage Parse7 data contains trailing bytes.");
            return records;
        }
        catch (HomecomingSalvageException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is EndOfStreamException or IOException or InvalidDataException
                or DecoderFallbackException or OverflowException)
        {
            throw new HomecomingSalvageException(
                $"Salvage Parse7 data is malformed or truncated: {exception.Message}",
                exception);
        }
    }

    private static HomecomingSalvageRarity ReadRarity(uint value, string sourceId) =>
        value switch
        {
            1 => HomecomingSalvageRarity.Common,
            2 => HomecomingSalvageRarity.Uncommon,
            3 => HomecomingSalvageRarity.Rare,
            4 => HomecomingSalvageRarity.VeryRare,
            _ => throw new HomecomingSalvageException(
                $"Salvage record '{sourceId}' has unknown rarity value {value}.")
        };

    private static HomecomingSalvageCategory ReadCategory(uint value, string sourceId) =>
        value switch
        {
            0 => HomecomingSalvageCategory.Legacy,
            1 => HomecomingSalvageCategory.Invention,
            2 => HomecomingSalvageCategory.Special,
            3 => HomecomingSalvageCategory.Incarnate,
            _ => throw new HomecomingSalvageException(
                $"Salvage record '{sourceId}' has unknown category value {value}.")
        };

    private static uint ReadUInt32(BinaryReader reader, string field)
    {
        EnsureAvailable(reader, sizeof(uint), field);
        return reader.ReadUInt32();
    }

    private static byte[] ReadExactly(BinaryReader reader, int length, string field)
    {
        EnsureAvailable(reader, length, field);
        var bytes = reader.ReadBytes(length);
        if (bytes.Length != length)
        {
            throw new HomecomingSalvageException(
                $"Salvage Parse7 data is truncated while reading {field}.");
        }

        return bytes;
    }

    private static void EnsureAvailable(BinaryReader reader, long length, string field)
    {
        if (length < 0 || reader.BaseStream.Position > reader.BaseStream.Length - length)
        {
            throw new HomecomingSalvageException(
                $"Salvage Parse7 data is truncated while reading {field}.");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new HomecomingSalvageException(message);
        }
    }
}

internal sealed record HomecomingSalvageRecord(
    string HomecomingSourceId,
    string DisplayNameMessageKey,
    string Icon,
    HomecomingSalvageRarity Rarity,
    HomecomingSalvageCategory Category);

internal enum HomecomingSalvageRarity
{
    Common,
    Uncommon,
    Rare,
    VeryRare
}

internal enum HomecomingSalvageCategory
{
    Legacy,
    Invention,
    Special,
    Incarnate
}

internal sealed class HomecomingSalvageException : Exception
{
    internal HomecomingSalvageException(string message)
        : base(message)
    {
    }

    internal HomecomingSalvageException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
