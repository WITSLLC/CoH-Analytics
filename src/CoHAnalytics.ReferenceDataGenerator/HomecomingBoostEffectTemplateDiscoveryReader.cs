using System.IO;
using System.Text;

namespace CoHAnalytics.ReferenceDataGenerator;

/// <summary>
/// Discovery-only reader that recovers boost effect <c>tags</c>/<c>table</c>/<c>scale</c>
/// without changing the production <see cref="HomecomingPowersBoostDiscoveryReader"/> path.
/// </summary>
internal static class HomecomingBoostEffectTemplateDiscoveryReader
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static IReadOnlyList<HomecomingBoostEffectTemplateDiscovery> ReadForSourceId(
        byte[] powersData,
        string sourceId)
    {
        ArgumentNullException.ThrowIfNull(powersData);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);

        var boost = HomecomingPowersBoostDiscoveryReader.TryGetBoost(powersData, sourceId);
        Require(boost is not null, $"Boost '{sourceId}' was not found.");

        // Re-walk powers.bin looking for the matching record and parse its effect tail.
        using var stream = new MemoryStream(powersData, writable: false);
        using var reader = new BinaryReader(stream, StrictUtf8);
        var stringPool = HomecomingParse7HeaderReader.Read(reader, "Powers Parse7");
        var blockLength = ReadUInt32(reader, "Powers definition block length");
        var blockEnd = checked(stream.Position + blockLength);
        var recordCount = ReadUInt32(reader, "Power record count");

        // Layout detection matches the existing discovery reader.
        var recordsStart = stream.Position;
        var probe = HomecomingPowersBoostDiscoveryReader.TryGetBoost(powersData, sourceId);
        _ = probe;
        stream.Position = recordsStart;

        // Use the same layout detector indirectly by parsing with both layouts via score —
        // call ReadBoosts once to ensure layout is valid, then scan raw records.
        _ = HomecomingPowersBoostDiscoveryReader.ReadBoosts(powersData);

        stream.Position = recordsStart;
        for (var index = 0; index < recordCount; index++)
        {
            var recordLength = ReadUInt32(reader, $"Power record {index} length");
            var recordStart = checked((int)stream.Position);
            var recordEnd = checked(recordStart + (int)recordLength);
            var sub = new HomecomingParse7SubReader(
                powersData,
                stringPool,
                recordStart,
                checked((int)recordLength));
            var id = sub.ReadString("source ID");
            if (!string.Equals(id, sourceId, StringComparison.Ordinal))
            {
                stream.Position = recordEnd;
                continue;
            }

            // Fast-forward using the production discovery parse (no effects), then
            // re-parse this record with effect extraction via a dedicated walk copy.
            stream.Position = recordEnd;
            return ParseEffectsFromBoostRecord(powersData, stringPool, recordStart, checked((int)recordLength));
        }

        throw new HomecomingPowersBoostDiscoveryException($"Boost '{sourceId}' record was not located.");
    }

    private static IReadOnlyList<HomecomingBoostEffectTemplateDiscovery> ParseEffectsFromBoostRecord(
        byte[] data,
        HomecomingParse7StringPool stringPool,
        int recordStart,
        int recordLength)
    {
        // Delegate to a one-off parse that mirrors production field order through
        // boosts_allowed, then continues into effects. Layout flags are detected by
        // attempting both variants.
        foreach (var hasField41B in new[] { false, true })
        {
            foreach (var hasField45B in new[] { true, false })
            {
                try
                {
                    var reader = new HomecomingParse7SubReader(data, stringPool, recordStart, recordLength);
                    return ParseOne(reader, hasField45B, hasField41B);
                }
                catch (HomecomingPowersBoostDiscoveryException)
                {
                    // try next layout
                }
            }
        }

        throw new HomecomingPowersBoostDiscoveryException(
            "Unable to parse boost effect templates with known power layouts.");
    }

    private static IReadOnlyList<HomecomingBoostEffectTemplateDiscovery> ParseOne(
        HomecomingParse7SubReader reader,
        bool hasField45B,
        bool hasField41B)
    {
        reader.ReadString("source ID");
        reader.ReadUInt32("crc");
        reader.ReadString("source");
        reader.ReadString("name");
        reader.ReadString("source name");
        reader.ReadUInt32("system");
        reader.ReadBool("auto issue");
        reader.ReadBool("auto issue save level");
        reader.ReadBool("free");
        reader.ReadString("display name");
        reader.ReadString("display help");
        reader.ReadString("short help");
        reader.ReadString("target help");
        reader.ReadString("target short help");
        reader.ReadString("attacker attack");
        reader.ReadString("attacker attack floater");
        reader.ReadString("attacker hit");
        reader.ReadString("victim hit");
        reader.ReadString("confirm");
        reader.ReadString("float rewarded");
        reader.ReadString("power defense float");
        reader.ReadString("icon");
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
        reader.ReadSingle("radius");
        reader.ReadSingle("arc");
        reader.ReadSingle("chain delay");
        if (hasField41B)
        {
            reader.Skip(8, "field 41b");
        }

        reader.ReadStringArray("chain eff expression");
        reader.ReadStringArray("field43 str");
        reader.ReadStringArray("chain target expression");
        reader.ReadUInt32Array("field43c");
        reader.Skip(12, "box offset");
        reader.Skip(12, "box size");
        if (hasField45B)
        {
            reader.ReadUInt32("field 45b");
        }

        reader.ReadSingle("range");
        reader.ReadSingle("range secondary");
        reader.ReadSingle("time to activate");
        reader.ReadSingle("time to root");
        reader.ReadSingle("recharge time");
        reader.ReadSingle("activate period");
        reader.ReadSingle("endurance cost");
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
        reader.ReadUInt32Array("boosts allowed");
        ReadBoundedUInt32Array(reader, "mode group refs");
        ReadBoundedUInt32Array(reader, "exclusion groups");
        ReadBoundedUInt32Array(reader, "modes required");
        ReadBoundedUInt32Array(reader, "modes disallowed");
        ReadBoundedUInt32Array(reader, "modes suspended");
        ReadBoundedUInt32Array(reader, "post-mode array");
        SkipRedirects(reader);
        var templates = ReadEffectTemplates(reader);
        Require(templates.Count > 0, "Boost effect template list was empty.");
        return templates;
    }

    private static IReadOnlyList<uint> ReadBoundedUInt32Array(HomecomingParse7SubReader reader, string field)
    {
        var count = reader.ReadUInt32($"{field} count");
        Require(count <= 4096, $"{field} count {count} is implausible.");
        var values = new List<uint>(checked((int)count));
        for (var index = 0; index < count; index++)
        {
            values.Add(reader.ReadUInt32($"{field} {index}"));
        }

        return values;
    }

    private static void SkipRedirects(HomecomingParse7SubReader reader)
    {
        var count = reader.ReadUInt32("redirect count");
        Require(count <= 1000, $"Redirect count {count} is implausible.");
        for (var index = 0; index < count; index++)
        {
            var length = reader.ReadUInt32($"redirect {index} length");
            reader.Skip(checked((int)length), $"redirect {index}");
        }
    }

    private static IReadOnlyList<HomecomingBoostEffectTemplateDiscovery> ReadEffectTemplates(
        HomecomingParse7SubReader reader)
    {
        var effectCount = reader.ReadUInt32("effects count");
        Require(effectCount is > 0 and <= 256, $"Effects count {effectCount} is implausible.");
        var templates = new List<HomecomingBoostEffectTemplateDiscovery>();
        for (var effectIndex = 0; effectIndex < effectCount; effectIndex++)
        {
            var effectLength = reader.ReadUInt32($"effect {effectIndex} length");
            var effectReader = reader.SubReader(effectLength, $"effect {effectIndex}");
            templates.AddRange(ParseEffectGroup(effectReader));
        }

        return templates;
    }

    private static IReadOnlyList<HomecomingBoostEffectTemplateDiscovery> ParseEffectGroup(
        HomecomingParse7SubReader reader)
    {
        var tags = reader.ReadStringArray("effect tags");
        reader.ReadString("effect display info");
        reader.ReadSingle("effect chance");
        reader.ReadSingle("effect ppm");
        reader.ReadSingle("effect delay");
        reader.ReadSingle("effect radius inner");
        reader.ReadSingle("effect radius outer");
        reader.ReadStringArray("effect requires");
        reader.ReadUInt32("effect flags");
        reader.ReadUInt32("effect eval flags");

        var templateCount = reader.ReadUInt32("effect template count");
        Require(templateCount <= 256, $"Effect template count {templateCount} is implausible.");
        var templates = new List<HomecomingBoostEffectTemplateDiscovery>(checked((int)templateCount));
        for (var templateIndex = 0; templateIndex < templateCount; templateIndex++)
        {
            var templateLength = reader.ReadUInt32($"effect template {templateIndex} length");
            var templateReader = reader.SubReader(templateLength, $"effect template {templateIndex}");
            var attribIds = templateReader.ReadUInt32Array("template attribs");
            templateReader.ReadUInt32("template aspect");
            templateReader.ReadUInt32("template application type");
            templateReader.ReadUInt32("template type");
            templateReader.ReadUInt32("template target");
            SkipTargetInfo(templateReader);
            var table = templateReader.ReadString("template table");
            var scale = templateReader.ReadSingle("template scale");
            templates.Add(new HomecomingBoostEffectTemplateDiscovery(tags, attribIds, table, scale));
        }

        reader.SkipToEnd();
        return templates;
    }

    private static void SkipTargetInfo(HomecomingParse7SubReader reader)
    {
        var count = reader.ReadUInt32("target info count");
        Require(count <= 8, $"TargetInfo count {count} is implausible.");
        for (var index = 0; index < count; index++)
        {
            var length = reader.ReadUInt32($"target info {index} length");
            reader.Skip(checked((int)length), $"target info {index}");
        }
    }

    private static uint ReadUInt32(BinaryReader reader, string field)
    {
        if (reader.BaseStream.Position > reader.BaseStream.Length - sizeof(uint))
        {
            throw new HomecomingPowersBoostDiscoveryException(
                $"Powers Parse7 data is truncated while reading {field}.");
        }

        return reader.ReadUInt32();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new HomecomingPowersBoostDiscoveryException(message);
        }
    }
}

internal sealed record HomecomingBoostEffectTemplateDiscovery(
    IReadOnlyList<string> Tags,
    IReadOnlyList<uint> AttribIds,
    string Table,
    float Scale);
