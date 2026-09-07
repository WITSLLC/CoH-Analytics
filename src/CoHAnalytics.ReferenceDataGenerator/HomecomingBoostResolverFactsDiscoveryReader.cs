using System.IO;
using System.Collections.ObjectModel;
using System.Text;

namespace CoHAnalytics.ReferenceDataGenerator;

/// <summary>
/// Discovery reader for Enhancement help Scale resolver static facts:
/// effect tags/table/scale and boost level flags from live <c>powers.bin</c>.
/// </summary>
internal static class HomecomingBoostResolverFactsDiscoveryReader
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static HomecomingBoostResolverFactsDiscovery ReadForSourceId(byte[] powersData, string sourceId)
    {
        ArgumentNullException.ThrowIfNull(powersData);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);

        return ReadForSourceIds(powersData, [sourceId])[sourceId];
    }

    internal static IReadOnlyDictionary<string, HomecomingBoostResolverFactsDiscovery> ReadForSourceIds(
        byte[] powersData,
        IEnumerable<string> sourceIds)
    {
        ArgumentNullException.ThrowIfNull(powersData);
        ArgumentNullException.ThrowIfNull(sourceIds);

        var requested = sourceIds
            .Select(sourceId =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
                return sourceId;
            })
            .ToHashSet(StringComparer.Ordinal);
        if (requested.Count == 0)
        {
            return new ReadOnlyDictionary<string, HomecomingBoostResolverFactsDiscovery>(
                new Dictionary<string, HomecomingBoostResolverFactsDiscovery>(StringComparer.Ordinal));
        }

        // Validate the powers layout once for the entire batch. The former single-record call path
        // repeated this full parse for every enhancement source ID.
        _ = HomecomingPowersBoostDiscoveryReader.ReadBoosts(powersData);

        using var stream = new MemoryStream(powersData, writable: false);
        using var reader = new BinaryReader(stream, StrictUtf8);
        var stringPool = HomecomingParse7HeaderReader.Read(reader, "Powers Parse7");
        var blockLength = ReadUInt32(reader, "Powers definition block length");
        _ = checked(stream.Position + blockLength);
        var recordCount = ReadUInt32(reader, "Power record count");
        var results = new Dictionary<string, HomecomingBoostResolverFactsDiscovery>(StringComparer.Ordinal);

        for (var index = 0; index < recordCount; index++)
        {
            var recordLength = ReadUInt32(reader, $"Power record {index} length");
            var recordStart = checked((int)stream.Position);
            var recordEnd = checked(recordStart + (int)recordLength);
            var subReader = new HomecomingParse7SubReader(
                powersData,
                stringPool,
                recordStart,
                checked((int)recordLength));
            var id = subReader.ReadString("source ID");
            if (!requested.Contains(id))
            {
                stream.Position = recordEnd;
                continue;
            }

            stream.Position = recordEnd;
            if (!results.TryAdd(
                    id,
                    ParseBoostRecord(powersData, stringPool, recordStart, checked((int)recordLength))))
            {
                throw new HomecomingPowersBoostDiscoveryException(
                    $"Boost '{id}' appears more than once in Powers Parse7 data.");
            }
        }

        var missing = requested
            .Where(sourceId => !results.ContainsKey(sourceId))
            .OrderBy(sourceId => sourceId, StringComparer.Ordinal)
            .FirstOrDefault();
        if (missing is not null)
        {
            throw new HomecomingPowersBoostDiscoveryException($"Boost '{missing}' record was not located.");
        }

        return new ReadOnlyDictionary<string, HomecomingBoostResolverFactsDiscovery>(results);
    }

    internal static IReadOnlyList<HomecomingBoostResolverEffectDiscovery> BuildScaleEffects(
        IReadOnlyList<HomecomingBoostEffectTemplateDiscovery> templates)
    {
        var effects = new List<HomecomingBoostResolverEffectDiscovery>();
        var ambiguousTags = new HashSet<string>(StringComparer.Ordinal);
        var tagBindings = new Dictionary<string, (string Table, float Scale, IReadOnlyList<uint> AttribIds)>(
            StringComparer.Ordinal);

        foreach (var template in templates)
        {
            Require(!string.IsNullOrWhiteSpace(template.Table), "Boost effect template has an empty table name.");
            Require(float.IsFinite(template.Scale), "Boost effect template scale is not finite.");

            foreach (var tag in template.Tags)
            {
                if (ambiguousTags.Contains(tag))
                {
                    continue;
                }

                if (tagBindings.TryGetValue(tag, out var existing)
                    && (!string.Equals(existing.Table, template.Table, StringComparison.Ordinal)
                        || Math.Abs(existing.Scale - template.Scale) > 1e-6f))
                {
                    if (TryPreferScheduleBinding(existing, template, out var preferred))
                    {
                        tagBindings[tag] = preferred;
                        continue;
                    }

                    ambiguousTags.Add(tag);
                    tagBindings.Remove(tag);
                    continue;
                }

                if (!tagBindings.ContainsKey(tag))
                {
                    tagBindings[tag] = (template.Table, template.Scale, template.AttribIds);
                }
            }
        }

        foreach (var (tag, binding) in tagBindings.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            effects.Add(new HomecomingBoostResolverEffectDiscovery(
                tag,
                binding.Table,
                binding.Scale,
                binding.AttribIds));
        }

        return effects;
    }

    private static bool TryPreferScheduleBinding(
        (string Table, float Scale, IReadOnlyList<uint> AttribIds) existing,
        HomecomingBoostEffectTemplateDiscovery candidate,
        out (string Table, float Scale, IReadOnlyList<uint> AttribIds) preferred)
    {
        var existingIsSchedule = IsEnhancementScaleScheduleTable(existing.Table);
        var candidateIsSchedule = IsEnhancementScaleScheduleTable(candidate.Table);
        if (candidateIsSchedule && !existingIsSchedule)
        {
            preferred = (candidate.Table, candidate.Scale, candidate.AttribIds);
            return true;
        }

        if (existingIsSchedule && !candidateIsSchedule)
        {
            preferred = existing;
            return true;
        }

        if (existingIsSchedule && candidateIsSchedule)
        {
            var existingRank = ScheduleTablePreferenceRank(existing.Table);
            var candidateRank = ScheduleTablePreferenceRank(candidate.Table);
            if (candidateRank > existingRank)
            {
                preferred = (candidate.Table, candidate.Scale, candidate.AttribIds);
                return true;
            }

            if (existingRank > candidateRank)
            {
                preferred = existing;
                return true;
            }
        }

        preferred = default;
        return false;
    }

    private static int ScheduleTablePreferenceRank(string table)
    {
        if (table.Contains("Boosts", StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        if (table.Contains("Ones", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        return 0;
    }

    private static bool IsEnhancementScaleScheduleTable(string table)
    {
        if (HomecomingClassModTableDiscoveryReader.IdenticalAcrossClassesTableNames.Contains(table))
        {
            return true;
        }

        if (table.StartsWith("Melee_", StringComparison.OrdinalIgnoreCase)
            || table.StartsWith("Ranged_", StringComparison.OrdinalIgnoreCase))
        {
            return table.Contains("Boosts", StringComparison.OrdinalIgnoreCase)
                || table.Contains("Ones", StringComparison.OrdinalIgnoreCase)
                || table.Contains("EndDrain", StringComparison.OrdinalIgnoreCase)
                || table.Contains("EndDiscount", StringComparison.OrdinalIgnoreCase)
                || table.Contains("TempDamage", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static HomecomingBoostResolverFactsDiscovery ParseBoostRecord(
        byte[] data,
        HomecomingParse7StringPool stringPool,
        int recordStart,
        int recordLength)
    {
        foreach (var hasField41B in new[] { false, true })
        {
            foreach (var hasField45B in new[] { true, false })
            {
                try
                {
                    var reader = new HomecomingParse7SubReader(data, stringPool, recordStart, recordLength);
                    var templates = ParseEffectsAndTail(reader, hasField45B, hasField41B);
                    var effects = BuildScaleEffects(templates);
                    var flags = ReadBoostLevelFlags(reader);
                    reader.SkipToEnd();
                    return new HomecomingBoostResolverFactsDiscovery(effects, flags);
                }
                catch (HomecomingPowersBoostDiscoveryException)
                {
                    // try next layout
                }
            }
        }

        throw new HomecomingPowersBoostDiscoveryException(
            "Unable to parse boost resolver facts with known power layouts.");
    }

    private static IReadOnlyList<HomecomingBoostEffectTemplateDiscovery> ParseEffectsAndTail(
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
        return ReadEffectTemplates(reader);
    }

    private static HomecomingBoostLevelFlagsDiscovery ReadBoostLevelFlags(HomecomingParse7SubReader reader)
    {
        reader.ReadBool("IgnoreStrength");
        reader.ReadBool("ShowBuffIcon");
        reader.ReadBool("ShowInInventory");
        reader.ReadBool("ShowInManage");
        reader.ReadBool("ShowInInfo");
        reader.ReadBool("Deletable");
        reader.ReadBool("Tradeable");
        reader.ReadUInt32("MaxBoosts");
        reader.ReadBool("DoNotSave");
        reader.ReadBool("BoostIgnoreEffectiveness");
        reader.ReadBool("BoostAlwaysCountForSet");
        reader.ReadUInt32("HC fork word 1");
        reader.ReadUInt32("HC fork word 2");
        reader.ReadUInt32("HC fork word 3");
        reader.ReadUInt32("HC fork word 4");
        reader.ReadBool("BoostTradeable");
        reader.ReadBool("BoostCombinable");
        reader.ReadBool("BoostAccountBound");
        var boostUsePlayerLevel = reader.ReadBool("BoostUsePlayerLevel");
        var boostBoostable = reader.ReadBool("BoostBoostable");
        reader.ReadString("BoostCatalystConversion");
        reader.ReadString("StoreProduct");
        reader.ReadUInt32("BoostLicenseLevel");
        reader.ReadInt32("boost tail reserved");
        var minSlotLevel = reader.ReadInt32("MinSlotLevel");
        var maxSlotLevel = reader.ReadInt32("MaxSlotLevel");
        var maxBoostLevel = reader.ReadInt32("MaxBoostLevel");
        Require(maxBoostLevel is >= 0 and <= 100, $"MaxBoostLevel {maxBoostLevel} is implausible.");
        _ = minSlotLevel;
        _ = maxSlotLevel;

        return new HomecomingBoostLevelFlagsDiscovery(
            boostUsePlayerLevel,
            maxBoostLevel,
            boostBoostable);
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

internal sealed record HomecomingBoostResolverFactsDiscovery(
    IReadOnlyList<HomecomingBoostResolverEffectDiscovery> Effects,
    HomecomingBoostLevelFlagsDiscovery LevelFlags);

internal sealed record HomecomingBoostResolverEffectDiscovery(
    string Tag,
    string Table,
    float Scale,
    IReadOnlyList<uint> AttribIds);

internal sealed record HomecomingBoostLevelFlagsDiscovery(
    bool BoostUsePlayerLevel,
    int MaxBoostLevel,
    bool BoostBoostable);
