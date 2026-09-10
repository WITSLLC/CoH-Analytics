using System.IO.Compression;
using System.Text;

namespace CoHAnalytics.Tests.ReferenceDataGenerator;

internal static class HomecomingBinaryFixtureBuilder
{
    internal const string MessageMemberName = "bin/clientmessages-en.bin";
    internal const string SalvageMemberName = "bin/salvage.bin";
    internal const string PowersMemberName = "bin/powers.bin";
    internal const string PowersetsMemberName = "bin/powersets.bin";
    internal const string BoostSetsMemberName = "bin/boostsets.bin";
    internal const string BaseRecipesMemberName = "bin/baserecipes.bin";
    internal const string BadgesMemberName = "bin/badges.bin";

    internal static byte[] CreateMessageStore(params (string Key, string Value)[] records)
    {
        var strings = new List<string> { string.Empty };
        foreach (var record in records)
        {
            if (!strings.Contains(record.Value, StringComparer.Ordinal))
            {
                strings.Add(record.Value);
            }
        }

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(20090521u);
        WriteStringPool(writer, strings);
        WriteStringPool(writer, []);
        writer.Write(checked((uint)records.Length));

        foreach (var record in records)
        {
            var keyBytes = Encoding.UTF8.GetBytes(record.Key);
            writer.Write(checked((uint)keyBytes.Length));
            writer.Write(keyBytes);
            writer.Write(checked((uint)strings.IndexOf(record.Value)));
            writer.Write(0u);
            writer.Write(0u);
        }

        writer.Flush();
        return stream.ToArray();
    }

    internal static byte[] CreatePigg(params (string Name, byte[] Value)[] members)
    {
        var compressedMembers = members
            .Select(member => Compress(member.Value))
            .ToArray();
        var encodedNames = members
            .Select(member => Encoding.UTF8.GetBytes(member.Name + '\0'))
            .ToArray();
        var stringTableLength = encodedNames.Sum(name => sizeof(uint) + name.Length);
        var payloadOffset = checked(
            16
            + (48 * members.Length)
            + 12
            + stringTableLength);

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(0x00000123u);
        writer.Write((ushort)2);
        writer.Write((ushort)2);
        writer.Write((ushort)16);
        writer.Write((ushort)48);
        writer.Write(checked((uint)members.Length));

        var currentPayloadOffset = payloadOffset;
        for (var index = 0; index < members.Length; index++)
        {
            writer.Write(0x00003456u);
            writer.Write(checked((uint)index));
            writer.Write(checked((uint)members[index].Value.Length));
            writer.Write(0u);
            writer.Write(checked((uint)currentPayloadOffset));
            writer.Write(0u);
            writer.Write(uint.MaxValue);
            writer.Write(new byte[16]);
            writer.Write(checked((uint)compressedMembers[index].Length));
            currentPayloadOffset = checked(currentPayloadOffset + compressedMembers[index].Length);
        }

        writer.Write(0x00006789u);
        writer.Write(checked((uint)members.Length));
        writer.Write(checked((uint)stringTableLength));
        foreach (var name in encodedNames)
        {
            writer.Write(checked((uint)name.Length));
            writer.Write(name);
        }

        foreach (var compressed in compressedMembers)
        {
            writer.Write(compressed);
        }

        writer.Flush();
        return stream.ToArray();
    }

    internal static byte[] CreateSalvage(params SyntheticSalvageRecord[] records)
    {
        var stringPool = new SyntheticParse7StringPool();
        var encodedRecords = records
            .Select(record => new
            {
                record.SourceId,
                record.DisplayNameMessageKey,
                record.Icon,
                record.Rarity,
                record.Category,
                SourceIdOffset = stringPool.Add(record.SourceId),
                DisplayNameOffset = stringPool.Add(record.DisplayNameMessageKey),
                DisplayHelpOffset = stringPool.Add($"{record.DisplayNameMessageKey}_HELP"),
                ShortHelpOffset = stringPool.Add($"{record.DisplayNameMessageKey}_SHORT"),
                IconOffset = stringPool.Add(record.Icon),
                TypeMessageOffset = stringPool.Add("P_TYPE")
            })
            .ToArray();

        using var definitionBlock = new MemoryStream();
        using (var blockWriter = new BinaryWriter(definitionBlock, Encoding.UTF8, leaveOpen: true))
        {
            blockWriter.Write(checked((uint)encodedRecords.Length));
            foreach (var record in encodedRecords)
            {
                blockWriter.Write(36u);
                blockWriter.Write(record.SourceIdOffset);
                blockWriter.Write(record.DisplayNameOffset);
                blockWriter.Write(record.DisplayHelpOffset);
                blockWriter.Write(record.ShortHelpOffset);
                blockWriter.Write(record.IconOffset);
                blockWriter.Write(record.TypeMessageOffset);
                blockWriter.Write(0u);
                blockWriter.Write(record.Rarity);
                blockWriter.Write(record.Category);
            }
        }

        return CreateParse7(stringPool.ToArray(), definitionBlock.ToArray());
    }

    internal static byte[] CreatePowers(params SyntheticBoostRecord[] records)
    {
        var stringPool = new SyntheticParse7StringPool();
        using var definitionBlock = new MemoryStream();
        using var writer = new BinaryWriter(definitionBlock, Encoding.UTF8, leaveOpen: true);
        writer.Write(checked((uint)records.Length));
        foreach (var record in records)
        {
            using var body = new MemoryStream();
            using (var bodyWriter = new BinaryWriter(body, Encoding.UTF8, leaveOpen: true))
            {
                bodyWriter.Write(stringPool.Add(record.SourceId));
                bodyWriter.Write(0u);
                bodyWriter.Write(stringPool.Add(string.Empty));
                bodyWriter.Write(stringPool.Add(record.SourceId.Split('.')[1]));
                bodyWriter.Write(stringPool.Add(record.SourceId));
                bodyWriter.Write(0u);
                bodyWriter.Write(0u);
                bodyWriter.Write(0u);
                bodyWriter.Write(0u);
                bodyWriter.Write(stringPool.Add(record.DisplayNameMessageKey));
            }

            writer.Write(checked((uint)body.Length));
            writer.Write(body.ToArray());
        }

        writer.Flush();
        return CreateParse7(stringPool.ToArray(), definitionBlock.ToArray());
    }

    internal static byte[] CreatePowersDiscovery(params SyntheticInspirationDiscoveryRecord[] records)
    {
        var stringPool = new SyntheticParse7StringPool();
        using var definitionBlock = new MemoryStream();
        using var writer = new BinaryWriter(definitionBlock, Encoding.UTF8, leaveOpen: true);
        writer.Write(checked((uint)records.Length));
        foreach (var record in records)
        {
            using var body = new MemoryStream();
            using (var bodyWriter = new BinaryWriter(body, Encoding.UTF8, leaveOpen: true))
            {
                WriteDiscoveryPowerBody(bodyWriter, stringPool, record);
            }

            writer.Write(checked((uint)body.Length));
            writer.Write(body.ToArray());
        }

        writer.Flush();
        return CreateParse7(stringPool.ToArray(), definitionBlock.ToArray());
    }

    internal static byte[] CreatePowerPresentations(params SyntheticPowerPresentationRecord[] records) =>
        CreatePowersDiscovery(
            records.Select(record => new SyntheticInspirationDiscoveryRecord(
                record.SourceId,
                record.DisplayNameMessageKey,
                record.DisplayHelpMessageKey,
                Icon: record.IconIdentity,
                IsAutoIssued: record.IsAutoIssued,
                IsFree: record.IsFree,
                PowerType: record.PowerType))
            .ToArray());

    internal static byte[] CreatePowersets(params SyntheticPowersetRecord[] records)
    {
        var stringPool = new SyntheticParse7StringPool();
        using var definitionBlock = new MemoryStream();
        using var writer = new BinaryWriter(definitionBlock, Encoding.UTF8, leaveOpen: true);
        writer.Write(checked((uint)records.Length));
        foreach (var record in records)
        {
            using var body = new MemoryStream();
            using (var bodyWriter = new BinaryWriter(body, Encoding.UTF8, leaveOpen: true))
            {
                bodyWriter.Write(stringPool.Add($"DEFS/POWERS/{record.SourceId.Replace('.', '/')}.POWERSETS"));
                bodyWriter.Write(stringPool.Add(record.SourceId));
                bodyWriter.Write(stringPool.Add(record.SourceId.Split('.')[1]));
                bodyWriter.Write(0u);
                bodyWriter.Write(0u);
                bodyWriter.Write(stringPool.Add(record.DisplayNameMessageKey));
            }

            writer.Write(checked((uint)body.Length));
            writer.Write(body.ToArray());
        }

        writer.Flush();
        return CreateParse7(stringPool.ToArray(), definitionBlock.ToArray());
    }

    internal static byte[] CreateBoostSets(params SyntheticBoostSetRecord[] records)
    {
        var stringPool = new SyntheticParse7StringPool();
        using var definitionBlock = new MemoryStream();
        using var writer = new BinaryWriter(definitionBlock, Encoding.UTF8, leaveOpen: true);
        writer.Write(checked((uint)records.Length));
        foreach (var record in records)
        {
            using var body = new MemoryStream();
            using (var bodyWriter = new BinaryWriter(body, Encoding.UTF8, leaveOpen: true))
            {
                bodyWriter.Write(stringPool.Add(record.SourceId));
                bodyWriter.Write(stringPool.Add(record.DisplayNameMessageKey));
                bodyWriter.Write(stringPool.Add("P_DESCRIPTION"));
                var conversionCodes = new[] { record.RarityCode, record.CategoryCode }
                    .Where(value => value.Length > 0)
                    .ToArray();
                bodyWriter.Write(checked((uint)conversionCodes.Length));
                foreach (var conversionCode in conversionCodes)
                {
                    bodyWriter.Write(stringPool.Add(conversionCode));
                }
                WritePackedStringArray(bodyWriter, []);
                bodyWriter.Write(checked((uint)record.MemberGroups.Count));
                foreach (var group in record.MemberGroups)
                {
                    using var nested = new MemoryStream();
                    using (var nestedWriter = new BinaryWriter(nested, Encoding.UTF8, leaveOpen: true))
                    {
                        WritePackedStringArray(nestedWriter, group);
                    }

                    bodyWriter.Write(checked((uint)nested.Length));
                    bodyWriter.Write(nested.ToArray());
                }

                bodyWriter.Write(checked((uint)record.Bonuses.Count));
                foreach (var bonus in record.Bonuses)
                {
                    using var bonusBody = new MemoryStream();
                    using (var bonusWriter = new BinaryWriter(bonusBody, Encoding.UTF8, leaveOpen: true))
                    {
                        bonusWriter.Write(0u);
                        bonusWriter.Write(bonus.MinimumBoosts);
                        bonusWriter.Write(bonus.MaximumBoosts);
                        bonusWriter.Write(checked((uint)bonus.RequiresTokens.Count));
                        foreach (var token in bonus.RequiresTokens)
                        {
                            bonusWriter.Write(stringPool.Add(token));
                        }

                        bonusWriter.Write(checked((uint)bonus.AutoPowerSourceIds.Count));
                        foreach (var autoPower in bonus.AutoPowerSourceIds)
                        {
                            WritePackedString(bonusWriter, autoPower);
                        }

                        bonusWriter.Write(0u);
                    }

                    bodyWriter.Write(checked((uint)bonusBody.Length));
                    bodyWriter.Write(bonusBody.ToArray());
                }

                bodyWriter.Write(record.MinimumLevel);
                bodyWriter.Write(record.MaximumLevel);
                bodyWriter.Write(stringPool.Add(string.Empty));
            }

            writer.Write(checked((uint)body.Length));
            writer.Write(body.ToArray());
        }

        writer.Flush();
        return CreateParse7(stringPool.ToArray(), definitionBlock.ToArray());
    }

    internal static byte[] CreateBaseRecipes(params SyntheticBaseRecipeRecord[] records)
    {
        var stringPool = new SyntheticParse7StringPool();
        using var definitionBlock = new MemoryStream();
        using var writer = new BinaryWriter(definitionBlock, Encoding.UTF8, leaveOpen: true);
        writer.Write(checked((uint)records.Length));
        foreach (var record in records)
        {
            using var body = new MemoryStream();
            using (var bodyWriter = new BinaryWriter(body, Encoding.UTF8, leaveOpen: true))
            {
                bodyWriter.Write(stringPool.Add($"defs/recipes/{record.SourceId}.recipe"));
                bodyWriter.Write(stringPool.Add(record.SourceId));
                bodyWriter.Write(stringPool.Add(record.DisplayNameMessageKey));
                bodyWriter.Write(stringPool.Add($"{record.DisplayNameMessageKey}_HELP"));
                bodyWriter.Write(0u);
                bodyWriter.Write(stringPool.Add("recipe_fixture.tga"));
                bodyWriter.Write(stringPool.Add($"{record.DisplayNameMessageKey}_SHORT"));
                bodyWriter.Write(0u);
                WritePackedStringArray(bodyWriter, record.WorktableIds);

                bodyWriter.Write(checked((uint)record.Requirements.Count));
                foreach (var requirement in record.Requirements)
                {
                    using var nested = new MemoryStream();
                    using (var nestedWriter = new BinaryWriter(nested, Encoding.UTF8, leaveOpen: true))
                    {
                        nestedWriter.Write(requirement.Quantity);
                        WritePackedString(nestedWriter, requirement.SalvageSourceId);
                    }

                    bodyWriter.Write(checked((uint)nested.Length));
                    bodyWriter.Write(nested.ToArray());
                }

                bodyWriter.Write(0u);
                WritePackedStringArray(bodyWriter, []);
                bodyWriter.Write(stringPool.Add(string.Empty));
                bodyWriter.Write(stringPool.Add(record.ProductSourceId));
                bodyWriter.Write(stringPool.Add(string.Empty));
                bodyWriter.Write(stringPool.Add(string.Empty));
                bodyWriter.Write(0u);
                bodyWriter.Write(0u);
                bodyWriter.Write(stringPool.Add(string.Empty));
                WritePooledStringArray(bodyWriter, stringPool, []);
                bodyWriter.Write(record.Rarity);
                bodyWriter.Write(record.Level);
                bodyWriter.Write(0u);
                bodyWriter.Write(0u);
                WritePooledStringArray(bodyWriter, stringPool, record.CraftingCosts);
                for (var index = 0; index < 9; index++)
                {
                    bodyWriter.Write(0u);
                }

                for (var index = 0; index < 3; index++)
                {
                    WritePooledStringArray(bodyWriter, stringPool, []);
                }

                bodyWriter.Write(0u);
                for (var index = 0; index < 5; index++)
                {
                    bodyWriter.Write(stringPool.Add(string.Empty));
                }

                bodyWriter.Write(0u);
                bodyWriter.Write(stringPool.Add(string.Empty));
                bodyWriter.Write(0u);
            }

            writer.Write(checked((uint)body.Length));
            writer.Write(body.ToArray());
        }

        writer.Flush();
        return CreateParse7(stringPool.ToArray(), definitionBlock.ToArray());
    }

    internal static byte[] CreateBadges(params SyntheticBadgeRecord[] records)
    {
        var stringPool = new SyntheticParse7StringPool();
        using var definitionBlock = new MemoryStream();
        using var writer = new BinaryWriter(definitionBlock, Encoding.UTF8, leaveOpen: true);
        writer.Write(checked((uint)records.Length));
        foreach (var record in records)
        {
            using var body = new MemoryStream();
            using (var bodyWriter = new BinaryWriter(body, Encoding.UTF8, leaveOpen: true))
            {
                bodyWriter.Write(stringPool.Add(record.SourcePath));
                bodyWriter.Write(stringPool.Add(record.SourceId));
                bodyWriter.Write(record.NumericIndex);
                bodyWriter.Write(0u);
                bodyWriter.Write(record.BadgeType);
                bodyWriter.Write(stringPool.Add(string.Empty));
                bodyWriter.Write(stringPool.Add(record.HeroDescriptionMessageKey));
                bodyWriter.Write(stringPool.Add(record.HeroNameMessageKey));
                bodyWriter.Write(stringPool.Add(record.HeroIcon));
                bodyWriter.Write(stringPool.Add(string.Empty));
                bodyWriter.Write(stringPool.Add(record.VillainDescriptionMessageKey));
                bodyWriter.Write(stringPool.Add(record.VillainNameMessageKey));
                bodyWriter.Write(stringPool.Add(record.VillainIcon));
                for (var index = 0; index < 7; index++)
                {
                    bodyWriter.Write(0u);
                }

                bodyWriter.Write(stringPool.Add(string.Empty));
                for (var index = 0; index < 10; index++)
                {
                    bodyWriter.Write(0u);
                }
            }

            writer.Write(checked((uint)body.Length));
            writer.Write(body.ToArray());
        }

        writer.Flush();
        return CreateParse7(stringPool.ToArray(), definitionBlock.ToArray());
    }

    private static byte[] CreateParse7(byte[] stringPool, byte[] definitionBlock)
    {
        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true);
        writer.Write("CrypticS"u8);
        writer.Write(0x12345678u);
        writer.Write((ushort)6);
        writer.Write("Parse7"u8);
        writer.Write(checked((uint)stringPool.Length));
        writer.Write(stringPool);
        while (output.Position % 4 != 0)
        {
            writer.Write((byte)0);
        }

        writer.Write(checked((uint)definitionBlock.Length));
        writer.Write(definitionBlock);
        writer.Flush();
        return output.ToArray();
    }

    private static void WritePackedStringArray(
        BinaryWriter writer,
        IReadOnlyCollection<string> values)
    {
        writer.Write(checked((uint)values.Count));
        foreach (var value in values)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            writer.Write(checked((ushort)bytes.Length));
            writer.Write(bytes);
            while (writer.BaseStream.Position % 4 != 0)
            {
                writer.Write((byte)0);
            }
        }
    }

    private static void WritePackedString(BinaryWriter writer, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        writer.Write(checked((ushort)bytes.Length));
        writer.Write(bytes);
        while (writer.BaseStream.Position % 4 != 0)
        {
            writer.Write((byte)0);
        }
    }

    private static void WritePooledStringArray(
        BinaryWriter writer,
        SyntheticParse7StringPool stringPool,
        IReadOnlyCollection<string> values)
    {
        writer.Write(checked((uint)values.Count));
        foreach (var value in values)
        {
            writer.Write(stringPool.Add(value));
        }
    }

    private static void WriteDiscoveryPowerBody(
        BinaryWriter writer,
        SyntheticParse7StringPool stringPool,
        SyntheticInspirationDiscoveryRecord record)
    {
        var parts = record.SourceId.Split('.');
        writer.Write(stringPool.Add(record.SourceId));
        writer.Write(0u);
        WritePooledString(writer, stringPool, string.Empty);
        WritePooledString(writer, stringPool, parts.Length > 1 ? parts[1] : string.Empty);
        WritePooledString(writer, stringPool, record.SourceId);
        writer.Write(0u);
        writer.Write(record.IsAutoIssued ? 1u : 0u);
        writer.Write(0u);
        writer.Write(record.IsFree ? 1u : 0u);
        WritePooledString(writer, stringPool, record.DisplayNameMessageKey);
        WritePooledString(writer, stringPool, record.DisplayHelpMessageKey ?? string.Empty);
        WritePooledString(writer, stringPool, record.ShortHelpMessageKey ?? string.Empty);
        WritePooledString(writer, stringPool, string.Empty);
        WritePooledString(writer, stringPool, string.Empty);
        WritePooledString(writer, stringPool, string.Empty);
        WritePooledString(writer, stringPool, string.Empty);
        WritePooledString(writer, stringPool, string.Empty);
        WritePooledString(writer, stringPool, string.Empty);
        WritePooledString(writer, stringPool, string.Empty);
        WritePooledString(writer, stringPool, string.Empty);
        WritePooledString(writer, stringPool, string.Empty);
        WritePooledString(writer, stringPool, record.Icon ?? string.Empty);
        writer.Write(record.PowerType);
        writer.Write(0u);
        WritePooledStringArray(writer, stringPool, []);
        WritePooledStringArray(writer, stringPool, []);
        WritePooledStringArray(writer, stringPool, []);
        WritePooledStringArray(writer, stringPool, []);
        WritePooledStringArray(writer, stringPool, []);
        WritePooledStringArray(writer, stringPool, []);
        WritePooledStringArray(writer, stringPool, []);
        WritePooledString(writer, stringPool, string.Empty);
        writer.Write(0f);
        writer.Write(new byte[52]);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write(0u);
        WritePooledStringArray(writer, stringPool, []);
        writer.Write(0u);
        writer.Write(0f);
        writer.Write(0u);
        writer.Write(0f);
        writer.Write(0f);
        writer.Write(0f);
        WritePooledStringArray(writer, stringPool, []);
        WritePooledStringArray(writer, stringPool, []);
        WritePooledStringArray(writer, stringPool, []);
        WritePooledUInt32Array(writer, []);
        writer.Write(new byte[12]);
        writer.Write(new byte[12]);
        writer.Write(0u);
        writer.Write(0f);
        writer.Write(0f);
        writer.Write(0f);
        writer.Write(0f);
        writer.Write(0f);
        writer.Write(0f);
        writer.Write(0f);
        writer.Write(0f);
        writer.Write(0f);
        writer.Write(0u);
        writer.Write(0u);
        WritePooledStringArray(writer, stringPool, []);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write(0f);
        writer.Write(0f);
        writer.Write(0f);
        writer.Write(0f);
        writer.Write(0f);
        writer.Write(0f);
        writer.Write(0f);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write(0u);
        WritePooledUInt32Array(writer, []);
        WritePooledUInt32Array(writer, []);
        writer.Write(0u);
        WritePooledUInt32Array(writer, []);
    }

    private static void WritePooledString(
        BinaryWriter writer,
        SyntheticParse7StringPool stringPool,
        string value) =>
        writer.Write(stringPool.Add(value));

    private static void WritePooledUInt32Array(BinaryWriter writer, IReadOnlyList<uint> values)
    {
        writer.Write(checked((uint)values.Count));
        foreach (var value in values)
        {
            writer.Write(value);
        }
    }

    private static void WriteStringPool(BinaryWriter writer, IReadOnlyCollection<string> strings)
    {
        var bytes = strings
            .SelectMany(value => Encoding.UTF8.GetBytes(value + '\0'))
            .ToArray();
        writer.Write(checked((uint)strings.Count));
        writer.Write(checked((uint)bytes.Length));
        writer.Write(bytes);
    }

    private static byte[] Compress(byte[] value)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            zlib.Write(value);
        }

        return output.ToArray();
    }

    private sealed class SyntheticParse7StringPool
    {
        private readonly MemoryStream _stream = new();
        private readonly Dictionary<string, uint> _offsets = new(StringComparer.Ordinal);

        internal SyntheticParse7StringPool()
        {
            _ = Add(string.Empty);
        }

        internal uint Add(string value)
        {
            if (_offsets.TryGetValue(value, out var existing))
            {
                return existing;
            }

            var offset = checked((uint)_stream.Position);
            _stream.Write(Encoding.UTF8.GetBytes(value));
            _stream.WriteByte(0);
            _offsets.Add(value, offset);
            return offset;
        }

        internal byte[] ToArray() => _stream.ToArray();
    }
}

internal sealed record SyntheticSalvageRecord(
    string SourceId,
    string DisplayNameMessageKey,
    string Icon,
    uint Rarity = 1,
    uint Category = 0);

internal sealed record SyntheticBoostRecord(
    string SourceId,
    string DisplayNameMessageKey);

internal sealed record SyntheticInspirationDiscoveryRecord(
    string SourceId,
    string DisplayNameMessageKey,
    string? DisplayHelpMessageKey = null,
    string? ShortHelpMessageKey = null,
    string? Icon = null,
    bool IsAutoIssued = false,
    bool IsFree = false,
    uint PowerType = 0);

internal sealed record SyntheticPowerPresentationRecord(
    string SourceId,
    string DisplayNameMessageKey,
    string IconIdentity,
    bool IsAutoIssued = false,
    bool IsFree = false,
    uint PowerType = 0,
    string? DisplayHelpMessageKey = null);

internal sealed record SyntheticPowersetRecord(
    string SourceId,
    string DisplayNameMessageKey);

internal sealed record SyntheticBoostSetRecord(
    string SourceId,
    string DisplayNameMessageKey,
    string RarityCode,
    string CategoryCode,
    uint MinimumLevel,
    uint MaximumLevel,
    IReadOnlyList<IReadOnlyList<string>> MemberGroups,
    IReadOnlyList<SyntheticBoostSetBonusRecord>? Bonuses = null)
{
    internal IReadOnlyList<SyntheticBoostSetBonusRecord> Bonuses { get; } =
        Bonuses ?? Array.Empty<SyntheticBoostSetBonusRecord>();
}

internal sealed record SyntheticBoostSetBonusRecord(
    uint MinimumBoosts,
    uint MaximumBoosts,
    IReadOnlyList<string> RequiresTokens,
    IReadOnlyList<string> AutoPowerSourceIds);

internal sealed record SyntheticBaseRecipeRecord
{
    internal SyntheticBaseRecipeRecord(
        string sourceId,
        string displayNameMessageKey,
        string productSourceId,
        uint rarity = 1,
        uint level = 10,
        IReadOnlyList<SyntheticBaseRecipeRequirement>? requirements = null,
        IReadOnlyList<string>? worktableIds = null,
        IReadOnlyList<string>? craftingCosts = null)
    {
        SourceId = sourceId;
        DisplayNameMessageKey = displayNameMessageKey;
        ProductSourceId = productSourceId;
        Rarity = rarity;
        Level = level;
        Requirements = requirements ?? [new("S_Fixture", 1)];
        WorktableIds = worktableIds ?? ["Worktable_Invention"];
        CraftingCosts = craftingCosts ?? ["3400"];
    }

    internal string SourceId { get; }

    internal string DisplayNameMessageKey { get; }

    internal string ProductSourceId { get; }

    internal uint Rarity { get; }

    internal uint Level { get; }

    internal IReadOnlyList<SyntheticBaseRecipeRequirement> Requirements { get; }

    internal IReadOnlyList<string> WorktableIds { get; }

    internal IReadOnlyList<string> CraftingCosts { get; }
}

internal sealed record SyntheticBaseRecipeRequirement(
    string SalvageSourceId,
    uint Quantity);

internal sealed record SyntheticBadgeRecord
{
    internal SyntheticBadgeRecord(
        string sourceId,
        string category = "TOURISM",
        uint numericIndex = 1,
        uint badgeType = 1,
        string heroNameMessageKey = "P_HERO_NAME",
        string villainNameMessageKey = "P_VILLAIN_NAME",
        string heroDescriptionMessageKey = "P_HERO_DESCRIPTION",
        string villainDescriptionMessageKey = "P_VILLAIN_DESCRIPTION",
        string heroIcon = "badge_hero",
        string villainIcon = "badge_villain")
    {
        SourceId = sourceId;
        SourcePath = $"DEFS/BADGES/BADGES_{category}.DEF";
        NumericIndex = numericIndex;
        BadgeType = badgeType;
        HeroNameMessageKey = heroNameMessageKey;
        VillainNameMessageKey = villainNameMessageKey;
        HeroDescriptionMessageKey = heroDescriptionMessageKey;
        VillainDescriptionMessageKey = villainDescriptionMessageKey;
        HeroIcon = heroIcon;
        VillainIcon = villainIcon;
    }

    internal string SourceId { get; }

    internal string SourcePath { get; }

    internal uint NumericIndex { get; }

    internal uint BadgeType { get; }

    internal string HeroNameMessageKey { get; }

    internal string VillainNameMessageKey { get; }

    internal string HeroDescriptionMessageKey { get; }

    internal string VillainDescriptionMessageKey { get; }

    internal string HeroIcon { get; }

    internal string VillainIcon { get; }
}
