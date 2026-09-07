using System.IO;
using System.Text;

namespace CoHAnalytics.ReferenceDataGenerator;

/// <summary>
/// Discovery-only reader for named combat-mod tables hosted in <c>classes.bin</c>
/// (for example <c>Melee_Boosts_33</c>, <c>Melee_Ones</c>).
/// </summary>
/// <remarks>
/// Each CharacterClass record embeds a ModTable struct-array of NamedTable entries:
/// <c>u32 elemSize; string name; u32 count; f32[count]</c> with
/// <c>elemSize == 8 + 4*count</c>. This reader scans each class record for those
/// frames and retains tables that participate in Enhancement Scale resolution.
/// Surrounding class fields remain undecoded.
/// </remarks>
internal static class HomecomingClassModTableDiscoveryReader
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static readonly HashSet<string> IdenticalAcrossClassesTableNames = new(StringComparer.Ordinal)
    {
        "Melee_Ones",
        "Ranged_Ones",
        "Melee_Boosts_20",
        "Melee_Boosts_33",
        "Melee_Boosts_40",
        "Melee_Boosts_60",
        "Ranged_Boosts_20",
        "Ranged_Boosts_33",
        "Ranged_Boosts_40",
        "Ranged_Boosts_60",
    };

    internal static HomecomingClassModTableDiscoveryResult Read(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        try
        {
            using var stream = new MemoryStream(data, writable: false);
            using var reader = new BinaryReader(stream, StrictUtf8);
            var stringPool = HomecomingParse7HeaderReader.Read(reader, "Classes Parse7");
            var blockLength = ReadUInt32(reader, "Classes definition block length");
            var blockEnd = checked(stream.Position + blockLength);
            Require(blockEnd <= stream.Length, "Classes definition block extends beyond the input.");

            var recordCount = ReadUInt32(reader, "Class record count");
            Require(recordCount is > 0 and <= 4096, $"Class record count {recordCount} is implausible.");

            var byClass = new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<float>>>(
                StringComparer.Ordinal);
            for (var index = 0; index < recordCount; index++)
            {
                var recordLength = ReadUInt32(reader, $"Class record {index} length");
                var recordStart = checked((int)stream.Position);
                var recordEnd = checked(recordStart + (int)recordLength);
                Require(recordEnd <= blockEnd, $"Class record {index} extends beyond the definition block.");

                var subReader = new HomecomingParse7SubReader(
                    data,
                    stringPool,
                    recordStart,
                    checked((int)recordLength));
                var className = subReader.ReadString("class name");
                var tables = ExtractNamedScaleTables(data, stringPool, recordStart, checked((int)recordLength));
                stream.Position = recordEnd;

                if (className.StartsWith("Class_", StringComparison.Ordinal) && tables.Count > 0)
                {
                    byClass[className] = tables;
                }
            }

            Require(stream.Position == blockEnd, "Classes definition block length does not match its records.");
            Require(byClass.Count > 0, "No player class ModTables were recovered.");
            return new HomecomingClassModTableDiscoveryResult(byClass);
        }
        catch (HomecomingClassModTableDiscoveryException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is EndOfStreamException or IOException or InvalidDataException
                or DecoderFallbackException or OverflowException or HomecomingPowersBoostDiscoveryException)
        {
            throw new HomecomingClassModTableDiscoveryException(
                $"Classes Parse7 data is malformed or truncated: {exception.Message}",
                exception);
        }
    }

    internal static IReadOnlyList<float> RequireNamedTable(byte[] data, string className, string tableName)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentException.ThrowIfNullOrWhiteSpace(className);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);

        using var stream = new MemoryStream(data, writable: false);
        using var reader = new BinaryReader(stream, StrictUtf8);
        var stringPool = HomecomingParse7HeaderReader.Read(reader, "Classes Parse7");
        var blockLength = ReadUInt32(reader, "Classes definition block length");
        var blockEnd = checked(stream.Position + blockLength);
        var recordCount = ReadUInt32(reader, "Class record count");

        for (var index = 0; index < recordCount; index++)
        {
            var recordLength = ReadUInt32(reader, $"Class record {index} length");
            var recordStart = checked((int)stream.Position);
            var recordEnd = checked(recordStart + (int)recordLength);
            Require(recordEnd <= blockEnd, $"Class record {index} extends beyond the definition block.");

            var subReader = new HomecomingParse7SubReader(
                data,
                stringPool,
                recordStart,
                checked((int)recordLength));
            var currentClassName = subReader.ReadString("class name");
            stream.Position = recordEnd;

            if (!string.Equals(currentClassName, className, StringComparison.Ordinal))
            {
                continue;
            }

            if (TryExtractNamedTable(data, stringPool, recordStart, checked((int)recordLength), tableName, out var values))
            {
                return values;
            }

            throw new HomecomingClassModTableDiscoveryException(
                $"NamedTable '{tableName}' was not found in class '{className}'.");
        }

        throw new HomecomingClassModTableDiscoveryException($"Class '{className}' was not found.");
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<float>> ExtractNamedScaleTables(
        byte[] data,
        HomecomingParse7StringPool stringPool,
        int recordStart,
        int recordLength)
    {
        var tables = new Dictionary<string, IReadOnlyList<float>>(StringComparer.Ordinal);
        var recordEnd = checked(recordStart + recordLength);

        for (var offset = recordStart; offset + 12 <= recordEnd; offset += 4)
        {
            var elemSize = BitConverter.ToUInt32(data, offset);
            if (elemSize < 12 || offset + elemSize > recordEnd)
            {
                continue;
            }

            var nameOffset = BitConverter.ToUInt32(data, offset + 4);
            string name;
            try
            {
                name = nameOffset == 0 ? string.Empty : stringPool.Resolve(nameOffset, "named table");
            }
            catch (InvalidDataException)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(name) || !IdenticalAcrossClassesTableNames.Contains(name))
            {
                continue;
            }

            if (!TryReadNamedTableFrame(data, offset, elemSize, out var values))
            {
                continue;
            }

            tables.TryAdd(name, values);
        }

        return tables;
    }

    private static bool TryExtractNamedTable(
        byte[] data,
        HomecomingParse7StringPool stringPool,
        int recordStart,
        int recordLength,
        string tableName,
        out IReadOnlyList<float> values)
    {
        values = Array.Empty<float>();
        var recordEnd = checked(recordStart + recordLength);

        for (var offset = recordStart; offset + 12 <= recordEnd; offset += 4)
        {
            var elemSize = BitConverter.ToUInt32(data, offset);
            if (elemSize < 12 || offset + elemSize > recordEnd)
            {
                continue;
            }

            var nameOffset = BitConverter.ToUInt32(data, offset + 4);
            string name;
            try
            {
                name = nameOffset == 0 ? string.Empty : stringPool.Resolve(nameOffset, "named table");
            }
            catch (InvalidDataException)
            {
                continue;
            }

            if (!string.Equals(name, tableName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!TryReadNamedTableFrame(data, offset, elemSize, out values))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private static bool TryReadNamedTableFrame(
        byte[] data,
        int offset,
        uint elemSize,
        out IReadOnlyList<float> values)
    {
        values = Array.Empty<float>();

        var count = BitConverter.ToUInt32(data, offset + 8);
        if (count is < 50 or > 200)
        {
            return false;
        }

        var expectedSize = checked(8u + (4u * count));
        if (elemSize != expectedSize)
        {
            return false;
        }

        var parsed = new float[count];
        for (var index = 0; index < parsed.Length; index++)
        {
            parsed[index] = BitConverter.ToSingle(data, offset + 12 + (index * 4));
        }

        values = parsed;
        return true;
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
            throw new HomecomingClassModTableDiscoveryException(
                $"Classes Parse7 data is truncated while reading {field}.");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new HomecomingClassModTableDiscoveryException(message);
        }
    }
}

internal sealed record HomecomingClassModTableDiscoveryResult(
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<float>>> TablesByClassName)
{
    internal IReadOnlyDictionary<string, IReadOnlyList<float>> RequireClass(string className)
    {
        if (!TablesByClassName.TryGetValue(className, out var tables))
        {
            throw new HomecomingClassModTableDiscoveryException(
                $"Class '{className}' was not present in the ModTable discovery result.");
        }

        return tables;
    }
}

internal sealed class HomecomingClassModTableDiscoveryException : Exception
{
    internal HomecomingClassModTableDiscoveryException(string message)
        : base(message)
    {
    }

    internal HomecomingClassModTableDiscoveryException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
