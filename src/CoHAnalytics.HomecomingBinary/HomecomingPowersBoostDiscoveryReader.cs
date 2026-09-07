using System.IO;
using System.Text;

namespace CoHAnalytics.HomecomingBinary;

internal static class HomecomingPowersBoostDiscoveryReader
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static IReadOnlyList<HomecomingBoostDiscoveryRecord> ReadBoosts(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        try
        {
            using var stream = new MemoryStream(data, writable: false);
            using var reader = new BinaryReader(stream, StrictUtf8);
            var stringPool = HomecomingParse7HeaderReader.Read(reader, "Powers Parse7");
            var blockLength = ReadUInt32(reader, "Powers definition block length");
            var blockEnd = checked(stream.Position + blockLength);
            Require(blockEnd <= stream.Length, "Powers definition block extends beyond the input.");

            var recordCount = ReadUInt32(reader, "Power record count");
            Require(recordCount <= int.MaxValue, "Power record count is too large.");

            var recordsStart = stream.Position;
            var layout = DetectLayout(data, stringPool, recordsStart, recordCount);
            stream.Position = recordsStart;

            var boosts = new List<HomecomingBoostDiscoveryRecord>();
            var sourceIds = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < recordCount; index++)
            {
                var recordLength = ReadUInt32(reader, $"Power record {index} length");
                var recordStart = checked((int)stream.Position);
                var recordEnd = checked(recordStart + recordLength);
                Require(recordEnd <= blockEnd, $"Power record {index} extends beyond the definition block.");

                var subReader = new HomecomingParse7SubReader(
                    data,
                    stringPool,
                    recordStart,
                    checked((int)recordLength));
                HomecomingBoostDiscoveryRecord parsed;
                try
                {
                    parsed = ParseRecord(subReader, layout);
                }
                catch (HomecomingPowersBoostDiscoveryException)
                {
                    stream.Position = recordEnd;
                    continue;
                }

                stream.Position = recordEnd;

                if (!parsed.SourceId.StartsWith("Boosts.", StringComparison.Ordinal)
                    && !parsed.SourceId.StartsWith("Set_Bonus.", StringComparison.Ordinal))
                {
                    continue;
                }

                if (parsed.SourceId.StartsWith("Boosts.", StringComparison.Ordinal))
                {
                    Require(sourceIds.Add(parsed.SourceId),
                        $"Boost source ID '{parsed.SourceId}' is duplicated.");
                }

                boosts.Add(parsed);
            }

            Require(stream.Position == blockEnd,
                "Powers definition block length does not match its records.");
            return boosts
                .OrderBy(value => value.SourceId, StringComparer.Ordinal)
                .ToArray();
        }
        catch (HomecomingPowersBoostDiscoveryException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is EndOfStreamException or IOException or InvalidDataException
                or DecoderFallbackException or OverflowException)
        {
            throw new HomecomingPowersBoostDiscoveryException(
                $"Powers Parse7 data is malformed or truncated: {exception.Message}",
                exception);
        }
    }

    internal static HomecomingBoostDiscoveryRecord? TryGetBoost(byte[] data, string sourceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        return ReadBoosts(data).FirstOrDefault(
            value => string.Equals(value.SourceId, sourceId, StringComparison.Ordinal));
    }

    private static HomecomingPowerLayout DetectLayout(
        byte[] data,
        HomecomingParse7StringPool stringPool,
        long recordsStart,
        uint recordCount)
    {
        var sampleCount = Math.Min(recordCount, 32u);
        var bestScore = -1;
        var bestLayout = new HomecomingPowerLayout(HasField45B: true, HasField41B: false);
        foreach (var hasField41B in new[] { false, true })
        {
            foreach (var hasField45B in new[] { false, true })
            {
                var layout = new HomecomingPowerLayout(hasField45B, hasField41B);
                var score = ScoreLayout(data, stringPool, recordsStart, sampleCount, layout);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestLayout = layout;
                }
            }
        }

        return bestLayout;
    }

    private static int ScoreLayout(
        byte[] data,
        HomecomingParse7StringPool stringPool,
        long recordsStart,
        uint sampleCount,
        HomecomingPowerLayout layout)
    {
        var score = 0;
        var position = recordsStart;
        for (var index = 0; index < sampleCount; index++)
        {
            if (position + sizeof(uint) > data.Length)
            {
                return -1;
            }

            var recordLength = BitConverter.ToUInt32(data, (int)position);
            position += sizeof(uint);
            var recordStart = checked((int)position);
            var recordEnd = checked(recordStart + recordLength);
            if (recordEnd > data.Length)
            {
                return -1;
            }

            try
            {
                var subReader = new HomecomingParse7SubReader(
                    data,
                    stringPool,
                    recordStart,
                    checked((int)recordLength));
                var parsed = ParseRecord(subReader, layout);
                if (parsed.Range is >= 0 and <= 500
                    && parsed.RechargeTime is >= 0 and <= 3600
                    && parsed.EnduranceCost is >= 0 and <= 500
                    && parsed.TimeToActivate is >= 0 and <= 30)
                {
                    score++;
                }
            }
            catch (HomecomingPowersBoostDiscoveryException)
            {
                return -1;
            }

            position = recordEnd;
        }

        return score;
    }

    private static HomecomingBoostDiscoveryRecord ParseRecord(
        HomecomingParse7SubReader reader,
        HomecomingPowerLayout layout)
    {
        var sourceId = reader.ReadString("source ID");
        reader.ReadUInt32("crc");
        reader.ReadString("source");
        reader.ReadString("name");
        reader.ReadString("source name");
        reader.ReadUInt32("system");
        reader.ReadBool("auto issue");
        reader.ReadBool("auto issue save level");
        reader.ReadBool("free");
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
        var icon = reader.ReadString("icon");
        reader.ReadUInt32("power type");
        reader.ReadUInt32("num allowed");
        reader.ReadUInt32Array("attack types");
        reader.ReadStringArray("buy requires");
        reader.ReadStringArray("activate requires");
        reader.ReadStringArray("slot requires");
        reader.ReadStringArray("target requires");
        reader.ReadStringArray("reward requires");
        reader.ReadStringArray("auction requires");
        reader.ReadString("reward fallback");
        reader.ReadSingle("accuracy");
        reader.Skip(52, "cast flags");
        reader.ReadUInt32("ai report");
        reader.ReadUInt32("extra field 35b");
        reader.ReadUInt32("effect area");
        reader.ReadUInt32("max targets hit");
        reader.ReadStringArray("max targets expression");
        reader.ReadUInt32("over cap trigger");
        reader.ReadSingle("over cap multiplier");
        reader.ReadUInt32("over cap exponential");
        var radius = reader.ReadSingle("radius");
        var arc = reader.ReadSingle("arc");
        reader.ReadSingle("chain delay");
        if (layout.HasField41B)
        {
            reader.Skip(8, "field 41b");
        }

        reader.ReadStringArray("chain eff expression");
        reader.ReadStringArray("field43 str");
        reader.ReadStringArray("chain target expression");
        reader.ReadUInt32Array("field43c");
        reader.Skip(12, "box offset");
        reader.Skip(12, "box size");
        if (layout.HasField45B)
        {
            reader.ReadUInt32("field 45b");
        }

        var range = reader.ReadSingle("range");
        reader.ReadSingle("range secondary");
        var timeToActivate = reader.ReadSingle("time to activate");
        reader.ReadSingle("time to root");
        var rechargeTime = reader.ReadSingle("recharge time");
        reader.ReadSingle("activate period");
        var enduranceCost = reader.ReadSingle("endurance cost");
        reader.ReadSingle("idea cost");
        reader.ReadSingle("max toggle time");
        reader.ReadUInt32("time to confirm");
        reader.ReadUInt32("self confirm");
        reader.ReadStringArray("confirm requires");
        reader.ReadBool("destroy on limit");
        reader.ReadBool("stacking usage");
        reader.ReadUInt32("num charges");
        reader.ReadUInt32("max num charges");
        reader.ReadSingle("usage time");
        reader.ReadSingle("max usage time");
        reader.ReadSingle("lifetime");
        reader.ReadSingle("max lifetime");
        reader.ReadSingle("lifetime in game");
        reader.ReadSingle("max lifetime in game");
        reader.ReadSingle("interrupt time");
        reader.ReadUInt32("target visibility");
        reader.ReadUInt32("target type");
        reader.ReadUInt32("target secondary");
        reader.ReadUInt32Array("targets autohit");
        reader.ReadUInt32Array("targets affected");
        reader.ReadBool("targets through vision phase");
        var boostsRaw = reader.ReadUInt32Array("boosts allowed");
        var boostsAllowed = HomecomingBoostTypeNames.ResolveMany(boostsRaw);
        reader.SkipToEnd();

        return new HomecomingBoostDiscoveryRecord(
            sourceId,
            displayNameMessageKey,
            displayHelpMessageKey,
            shortHelpMessageKey,
            icon,
            boostsAllowed,
            HomecomingBoostTypeNames.NonOriginTypes(boostsAllowed),
            range,
            rechargeTime,
            enduranceCost,
            timeToActivate);
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
            throw new HomecomingPowersBoostDiscoveryException(
                $"Powers Parse7 data is truncated while reading {field}.");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new HomecomingPowersBoostDiscoveryException(message);
        }
    }

    private readonly record struct HomecomingPowerLayout(bool HasField45B, bool HasField41B);
}

internal sealed record HomecomingBoostDiscoveryRecord(
    string SourceId,
    string DisplayNameMessageKey,
    string DisplayHelpMessageKey,
    string ShortHelpMessageKey,
    string Icon,
    IReadOnlyList<string> BoostsAllowed,
    IReadOnlyList<string> NonOriginBoostTypes,
    float Range,
    float RechargeTime,
    float EnduranceCost,
    float TimeToActivate);

internal sealed class HomecomingPowersBoostDiscoveryException : Exception
{
    internal HomecomingPowersBoostDiscoveryException(string message)
        : base(message)
    {
    }

    internal HomecomingPowersBoostDiscoveryException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
