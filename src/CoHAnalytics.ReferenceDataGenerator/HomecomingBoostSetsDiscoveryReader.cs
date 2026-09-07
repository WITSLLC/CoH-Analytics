using System.IO;
using System.Text;

namespace CoHAnalytics.ReferenceDataGenerator;

internal static class HomecomingBoostSetsDiscoveryReader
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static IReadOnlyList<HomecomingBoostSetDiscoveryRecord> Read(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        using var stream = new MemoryStream(data, writable: false);
        using var reader = new BinaryReader(stream, StrictUtf8);
        var stringPool = HomecomingParse7HeaderReader.Read(reader, "Boost Sets Parse7");
        _ = ReadUInt32(reader, "Boost Sets definition block length");
        var recordCount = ReadUInt32(reader, "Boost Set record count");

        var records = new List<HomecomingBoostSetDiscoveryRecord>(checked((int)recordCount));
        for (var index = 0; index < recordCount; index++)
        {
            var recordLength = ReadUInt32(reader, $"Boost Set record {index} length");
            var recordBytes = reader.ReadBytes(checked((int)recordLength));
            if (recordBytes.Length != recordLength)
            {
                throw new HomecomingBoostSetsDiscoveryException(
                    $"Boost Set record {index} is truncated.");
            }

            records.Add(ParseRecord(recordBytes, stringPool));
        }

        return records
            .OrderBy(value => value.HomecomingSetId, StringComparer.Ordinal)
            .ToArray();
    }

    internal static HomecomingBoostSetDiscoveryRecord? TryGetSet(byte[] data, string setId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(setId);
        return Read(data).FirstOrDefault(
            value => string.Equals(value.HomecomingSetId, setId, StringComparison.Ordinal));
    }

    private static HomecomingBoostSetDiscoveryRecord ParseRecord(
        byte[] recordBytes,
        HomecomingParse7StringPool stringPool)
    {
        if (recordBytes.Length < 28)
        {
            throw new HomecomingBoostSetsDiscoveryException("Boost Set record is too short.");
        }

        string Resolve(uint offset, string field) =>
            offset == 0 ? string.Empty : stringPool.Resolve(offset, field);

        string? TryResolve(uint offset)
        {
            if (offset == 0)
            {
                return string.Empty;
            }

            try
            {
                return stringPool.Resolve(offset, "category or power count");
            }
            catch (InvalidDataException)
            {
                return null;
            }
        }

        var setId = Resolve(BitConverter.ToUInt32(recordBytes, 0), "source ID");
        var displayNameMessageKey = Resolve(BitConverter.ToUInt32(recordBytes, 4), "display name");
        var descriptionMessageKey = Resolve(BitConverter.ToUInt32(recordBytes, 8), "description");
        var rarity = Resolve(BitConverter.ToUInt32(recordBytes, 16), "rarity");
        var fifthField = BitConverter.ToUInt32(recordBytes, 20);
        var fifthFieldText = TryResolve(fifthField);
        var isPurple = fifthFieldText is null || !fifthFieldText.StartsWith("EC", StringComparison.Ordinal);

        string category;
        uint powerCount;
        var listStart = 24;
        if (isPurple)
        {
            category = string.Empty;
            powerCount = fifthField;
        }
        else
        {
            category = fifthFieldText ?? string.Empty;
            powerCount = BitConverter.ToUInt32(recordBytes, 24);
            listStart = 28;
        }

        var allowedPowers = ReadInlineStringList(recordBytes, listStart, powerCount, out var trailingStart);
        var trailing = ParseTrailingBlock(recordBytes.AsSpan(trailingStart), stringPool);
        return new HomecomingBoostSetDiscoveryRecord(
            setId,
            displayNameMessageKey,
            descriptionMessageKey,
            rarity,
            category,
            allowedPowers,
            trailing.Bonuses,
            trailing.MinimumLevel,
            trailing.MaximumLevel);
    }

    private static IReadOnlyList<string> ReadInlineStringList(
        byte[] recordBytes,
        int start,
        uint expectedCount,
        out int endPosition)
    {
        var values = new List<string>(checked((int)expectedCount));
        var position = start;
        for (var index = 0; index < expectedCount; index++)
        {
            if (position + 2 > recordBytes.Length)
            {
                throw new HomecomingBoostSetsDiscoveryException(
                    "Boost Set allowed-power list is truncated.");
            }

            var byteLength = BitConverter.ToUInt16(recordBytes, position);
            position += 2;
            if (byteLength == 0 || position + byteLength > recordBytes.Length)
            {
                throw new HomecomingBoostSetsDiscoveryException(
                    "Boost Set allowed-power entry is invalid.");
            }

            values.Add(StrictUtf8.GetString(recordBytes, position, byteLength));
            position += byteLength;
            while (position % 4 != 0 && position < recordBytes.Length)
            {
                position++;
            }
        }

        endPosition = position;
        return values;
    }

    private static TrailingBlock ParseTrailingBlock(
        ReadOnlySpan<byte> trailing,
        HomecomingParse7StringPool stringPool)
    {
        var reader = new SpanReader(trailing);
        var bonuses = new List<HomecomingBoostSetBonusRecord>();

        var boostListCount = reader.ReadUInt32();
        if (boostListCount > 256)
        {
            throw new HomecomingBoostSetsDiscoveryException(
                "Boost Set member-group count is implausible.");
        }

        for (var index = 0; index < boostListCount; index++)
        {
            var blockSize = checked((int)reader.ReadUInt32());
            reader.Skip(blockSize);
        }

        var bonusCount = reader.ReadUInt32();
        if (bonusCount > 256)
        {
            throw new HomecomingBoostSetsDiscoveryException(
                "Boost Set bonus count is implausible.");
        }

        for (var index = 0; index < bonusCount; index++)
        {
            var blockSize = checked((int)reader.ReadUInt32());
            var blockStart = reader.Position;
            var blockEnd = blockStart + blockSize;

            var leadingUnknown = reader.ReadUInt32();
            if (leadingUnknown != 0)
            {
                throw new HomecomingBoostSetsDiscoveryException(
                    $"Boost Set bonus {index} leading unknown field must be zero.");
            }

            var minimumBoosts = reader.ReadUInt32();
            var maximumBoosts = reader.ReadUInt32();
            var requiresCount = reader.ReadUInt32();
            if (requiresCount > 256)
            {
                throw new HomecomingBoostSetsDiscoveryException(
                    $"Boost Set bonus {index} requires count is implausible.");
            }

            var requiresTokens = new List<string>(checked((int)requiresCount));
            for (var tokenIndex = 0; tokenIndex < requiresCount; tokenIndex++)
            {
                var offset = reader.ReadUInt32();
                requiresTokens.Add(
                    offset == 0
                        ? string.Empty
                        : stringPool.Resolve(offset, $"bonus {index} requires token {tokenIndex}"));
            }

            var autoPowerCount = reader.ReadUInt32();
            var autoPowers = new List<string>(checked((int)autoPowerCount));
            for (var autoPowerIndex = 0; autoPowerIndex < autoPowerCount; autoPowerIndex++)
            {
                autoPowers.Add(reader.ReadInlineString());
            }

            var trailingUnknown = reader.ReadUInt32();
            if (trailingUnknown != 0)
            {
                throw new HomecomingBoostSetsDiscoveryException(
                    $"Boost Set bonus {index} trailing unknown field must be zero.");
            }

            if (reader.Position != blockEnd)
            {
                throw new HomecomingBoostSetsDiscoveryException(
                    $"Boost Set bonus {index} length does not match parsed fields.");
            }

            bonuses.Add(new HomecomingBoostSetBonusRecord(
                minimumBoosts,
                maximumBoosts,
                requiresTokens,
                autoPowers));
        }

        if (reader.Remaining < 8)
        {
            throw new HomecomingBoostSetsDiscoveryException(
                "Boost Set trailing block is truncated before level fields.");
        }

        var minimumLevel = reader.ReadUInt32();
        var maximumLevel = reader.ReadUInt32();
        return new TrailingBlock(bonuses, minimumLevel, maximumLevel);
    }

    private static uint ReadUInt32(BinaryReader reader, string field)
    {
        if (reader.BaseStream.Position > reader.BaseStream.Length - sizeof(uint))
        {
            throw new HomecomingBoostSetsDiscoveryException(
                $"Boost Sets Parse7 data is truncated while reading {field}.");
        }

        return reader.ReadUInt32();
    }

    private readonly record struct TrailingBlock(
        IReadOnlyList<HomecomingBoostSetBonusRecord> Bonuses,
        uint MinimumLevel,
        uint MaximumLevel);

    private sealed class SpanReader
    {
        private readonly byte[] _data;
        private readonly int _end;
        private int _position;

        internal SpanReader(ReadOnlySpan<byte> data)
        {
            _data = data.ToArray();
            _end = _data.Length;
            _position = 0;
        }

        internal int Position
        {
            get => _position;
            set => _position = value;
        }

        internal int Remaining => _end - _position;

        internal uint ReadUInt32()
        {
            RequireAvailable(sizeof(uint));
            var value = BitConverter.ToUInt32(_data, _position);
            _position += sizeof(uint);
            return value;
        }

        internal string ReadInlineString()
        {
            RequireAvailable(sizeof(ushort));
            var byteLength = BitConverter.ToUInt16(_data, _position);
            _position += sizeof(ushort);
            RequireAvailable(byteLength);
            var value = StrictUtf8.GetString(_data, _position, byteLength);
            _position += byteLength;
            while (_position % 4 != 0 && _position < _end)
            {
                _position++;
            }

            return value;
        }

        internal void Skip(int length)
        {
            RequireAvailable(length);
            _position += length;
        }

        private void RequireAvailable(int length)
        {
            if (length < 0 || _position > _end - length)
            {
                throw new HomecomingBoostSetsDiscoveryException(
                    "Boost Set trailing block is truncated.");
            }
        }
    }
}

internal sealed record HomecomingBoostSetDiscoveryRecord(
    string HomecomingSetId,
    string DisplayNameMessageKey,
    string DescriptionMessageKey,
    string RarityCode,
    string CategoryCode,
    IReadOnlyList<string> AllowedPowers,
    IReadOnlyList<HomecomingBoostSetBonusRecord> Bonuses,
    uint MinimumLevel,
    uint MaximumLevel);

internal sealed record HomecomingBoostSetBonusRecord(
    uint MinimumBoosts,
    uint MaximumBoosts,
    IReadOnlyList<string> RequiresTokens,
    IReadOnlyList<string> AutoPowerSourceIds);

internal sealed class HomecomingBoostSetsDiscoveryException : Exception
{
    internal HomecomingBoostSetsDiscoveryException(string message)
        : base(message)
    {
    }
}
