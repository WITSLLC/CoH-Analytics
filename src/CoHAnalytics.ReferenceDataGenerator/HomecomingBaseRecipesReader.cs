using System.IO;
using System.Text;

namespace CoHAnalytics.ReferenceDataGenerator;

internal static class HomecomingBaseRecipesReader
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static IReadOnlyList<HomecomingBaseRecipeRecord> Read(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        try
        {
            using var stream = new MemoryStream(data, writable: false);
            using var reader = new BinaryReader(stream, StrictUtf8);
            var stringPool = HomecomingParse7HeaderReader.Read(reader, "Base Recipes Parse7");
            var blockLength = ReadUInt32(reader, "Base Recipes definition block length");
            var blockEnd = checked(stream.Position + blockLength);
            Require(blockEnd <= stream.Length,
                "Base Recipes definition block extends beyond the input.");

            var recordCount = ReadUInt32(reader, "Base Recipe record count");
            Require(recordCount <= int.MaxValue, "Base Recipe record count is too large.");
            Require(recordCount <= (blockEnd - stream.Position) / sizeof(uint),
                "Base Recipe record count exceeds the definition block.");

            var records = new List<HomecomingBaseRecipeRecord>(checked((int)recordCount));
            var sourceIds = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < recordCount; index++)
            {
                var recordLength = ReadUInt32(reader, $"Base Recipe record {index} length");
                var recordStart = stream.Position;
                var recordEnd = checked(recordStart + recordLength);
                Require(recordEnd <= blockEnd,
                    $"Base Recipe record {index} extends beyond the definition block.");

                _ = ResolveString(reader, stringPool, $"record {index} definition path");
                var sourceId = ResolveString(reader, stringPool, $"record {index} source ID");
                var displayNameMessageKey = ResolveString(
                    reader,
                    stringPool,
                    $"record '{sourceId}' display name");
                _ = ResolveString(reader, stringPool, $"record '{sourceId}' display help");
                _ = ReadUInt32(reader, $"Base Recipe '{sourceId}' display flags");
                var icon = ResolveString(reader, stringPool, $"record '{sourceId}' icon");
                _ = ResolveString(reader, stringPool, $"record '{sourceId}' short help");
                _ = ReadUInt32(reader, $"Base Recipe '{sourceId}' short-help flags");

                var worktableIds = ReadPackedStringArray(
                    reader,
                    recordStart,
                    recordEnd,
                    $"Base Recipe '{sourceId}' worktables");
                var requirements = ReadRequirements(reader, recordStart, recordEnd, sourceId);
                SkipNestedBlockList(reader, recordEnd, $"Base Recipe '{sourceId}' alternate requirements");
                _ = ReadPackedStringArray(
                    reader,
                    recordStart,
                    recordEnd,
                    $"Base Recipe '{sourceId}' reward tokens");
                _ = ResolveString(reader, stringPool, $"record '{sourceId}' reward table");

                var productSourceId = ResolveString(
                    reader,
                    stringPool,
                    $"record '{sourceId}' product");
                _ = ResolveString(reader, stringPool, $"record '{sourceId}' product display");
                _ = ResolveString(reader, stringPool, $"record '{sourceId}' product help");
                _ = ReadUInt32(reader, $"Base Recipe '{sourceId}' product count");
                _ = ReadUInt32(reader, $"Base Recipe '{sourceId}' product flags");
                _ = ResolveString(reader, stringPool, $"record '{sourceId}' product icon");
                _ = ReadPooledStringArray(
                    reader,
                    stringPool,
                    recordEnd,
                    $"Base Recipe '{sourceId}' product expressions");

                var rarity = ReadUInt32(reader, $"Base Recipe '{sourceId}' rarity");
                var level = ReadUInt32(reader, $"Base Recipe '{sourceId}' level");
                _ = ReadUInt32(reader, $"Base Recipe '{sourceId}' level flags");
                _ = ReadUInt32(reader, $"Base Recipe '{sourceId}' visibility flags");
                var craftingCosts = ReadPooledStringArray(
                    reader,
                    stringPool,
                    recordEnd,
                    $"Base Recipe '{sourceId}' crafting costs");

                for (var fieldIndex = 0; fieldIndex < 9; fieldIndex++)
                {
                    _ = ReadUInt32(reader, $"Base Recipe '{sourceId}' numeric field {fieldIndex}");
                }

                for (var fieldIndex = 0; fieldIndex < 3; fieldIndex++)
                {
                    _ = ReadPooledStringArray(
                        reader,
                        stringPool,
                        recordEnd,
                        $"Base Recipe '{sourceId}' expression list {fieldIndex}");
                }

                _ = ReadUInt32(reader, $"Base Recipe '{sourceId}' trailing flags 0");
                for (var fieldIndex = 0; fieldIndex < 5; fieldIndex++)
                {
                    _ = ResolveString(
                        reader,
                        stringPool,
                        $"record '{sourceId}' trailing string {fieldIndex}");
                }

                _ = ReadUInt32(reader, $"Base Recipe '{sourceId}' trailing flags 1");
                _ = ResolveString(reader, stringPool, $"record '{sourceId}' buy string");
                _ = ReadUInt32(reader, $"Base Recipe '{sourceId}' trailing flags 2");

                Require(stream.Position == recordEnd,
                    $"Base Recipe '{sourceId}' record length does not match its fields.");
                Require(sourceId.Length > 0, $"Base Recipe record {index} has an empty source ID.");
                Require(sourceIds.Add(sourceId), $"Base Recipe source ID '{sourceId}' is duplicated.");

                records.Add(new HomecomingBaseRecipeRecord(
                    sourceId,
                    displayNameMessageKey,
                    icon,
                    worktableIds,
                    requirements,
                    productSourceId,
                    rarity,
                    level,
                    craftingCosts));
            }

            Require(stream.Position == blockEnd,
                "Base Recipes definition block length does not match its records.");
            Require(blockEnd == stream.Length, "Base Recipes Parse7 data contains trailing bytes.");
            return records;
        }
        catch (HomecomingBaseRecipesException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is EndOfStreamException or IOException or InvalidDataException
                or DecoderFallbackException or OverflowException)
        {
            throw new HomecomingBaseRecipesException(
                $"Base Recipes Parse7 data is malformed or truncated: {exception.Message}",
                exception);
        }
    }

    private static IReadOnlyList<HomecomingBaseRecipeRequirement> ReadRequirements(
        BinaryReader reader,
        long recordStart,
        long recordEnd,
        string sourceId)
    {
        var count = ReadUInt32(reader, $"Base Recipe '{sourceId}' requirement count");
        Require(count <= int.MaxValue,
            $"Base Recipe '{sourceId}' requirement count is too large.");
        Require(count <= (recordEnd - reader.BaseStream.Position) / sizeof(uint),
            $"Base Recipe '{sourceId}' requirement count exceeds the record.");

        var requirements = new List<HomecomingBaseRecipeRequirement>(checked((int)count));
        for (var index = 0; index < count; index++)
        {
            var nestedLength = ReadUInt32(
                reader,
                $"Base Recipe '{sourceId}' requirement {index} length");
            var nestedEnd = checked(reader.BaseStream.Position + nestedLength);
            Require(nestedEnd <= recordEnd,
                $"Base Recipe '{sourceId}' requirement {index} extends beyond the record.");
            var quantity = ReadUInt32(reader, $"Base Recipe '{sourceId}' requirement {index} quantity");
            var salvageSourceId = ReadPackedString(
                reader,
                recordStart,
                nestedEnd,
                $"Base Recipe '{sourceId}' requirement {index} salvage ID");
            Require(reader.BaseStream.Position == nestedEnd,
                $"Base Recipe '{sourceId}' requirement {index} length does not match its fields.");
            requirements.Add(new HomecomingBaseRecipeRequirement(salvageSourceId, quantity));
        }

        return requirements;
    }

    private static void SkipNestedBlockList(BinaryReader reader, long recordEnd, string field)
    {
        var count = ReadUInt32(reader, $"{field} count");
        Require(count <= (recordEnd - reader.BaseStream.Position) / sizeof(uint),
            $"{field} count exceeds the record.");
        for (var index = 0; index < count; index++)
        {
            var nestedLength = ReadUInt32(reader, $"{field} {index} length");
            EnsureWithin(reader, nestedLength, recordEnd, $"{field} {index}");
            reader.BaseStream.Position += nestedLength;
        }
    }

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
            values.Add(ReadPackedString(reader, recordStart, containingEnd, $"{field} {index}"));
        }

        return values;
    }

    private static string ReadPackedString(
        BinaryReader reader,
        long recordStart,
        long containingEnd,
        string field)
    {
        var byteLength = ReadUInt16(reader, $"{field} length");
        EnsureWithin(reader, byteLength, containingEnd, field);
        var bytes = reader.ReadBytes(byteLength);
        Require(bytes.Length == byteLength, $"{field} is truncated.");
        var value = StrictUtf8.GetString(bytes);

        var relativePosition = reader.BaseStream.Position - recordStart;
        var paddingLength = (4 - (relativePosition % 4)) % 4;
        EnsureWithin(reader, paddingLength, containingEnd, $"{field} padding");
        var padding = reader.ReadBytes(checked((int)paddingLength));
        Require(padding.All(value => value == 0), $"{field} padding is invalid.");
        return value;
    }

    private static IReadOnlyList<string> ReadPooledStringArray(
        BinaryReader reader,
        HomecomingParse7StringPool stringPool,
        long recordEnd,
        string field)
    {
        var count = ReadUInt32(reader, $"{field} count");
        Require(count <= int.MaxValue, $"{field} count is too large.");
        Require(count <= (recordEnd - reader.BaseStream.Position) / sizeof(uint),
            $"{field} count exceeds the record.");
        var values = new List<string>(checked((int)count));
        for (var index = 0; index < count; index++)
        {
            values.Add(ResolveString(reader, stringPool, $"{field} {index}"));
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
            throw new HomecomingBaseRecipesException(
                $"Base Recipes Parse7 data is truncated while reading {field}.");
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
            throw new HomecomingBaseRecipesException(
                $"{field} extends beyond its containing record.");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new HomecomingBaseRecipesException(message);
        }
    }
}

internal sealed record HomecomingBaseRecipeRecord(
    string HomecomingSourceId,
    string DisplayNameMessageKey,
    string Icon,
    IReadOnlyList<string> WorktableIds,
    IReadOnlyList<HomecomingBaseRecipeRequirement> Requirements,
    string ProductSourceId,
    uint Rarity,
    uint Level,
    IReadOnlyList<string> CraftingCosts);

internal sealed record HomecomingBaseRecipeRequirement(
    string HomecomingSalvageSourceId,
    uint Quantity);

internal sealed class HomecomingBaseRecipesException : Exception
{
    internal HomecomingBaseRecipesException(string message)
        : base(message)
    {
    }

    internal HomecomingBaseRecipesException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
