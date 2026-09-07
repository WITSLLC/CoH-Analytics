using System.IO;
using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

/// <summary>
/// Loads salvage and enhancement lookup indexes from the user's local Mids Homecoming database files.
/// Binary layout follows the Mids Reborn Homecoming database format (read-only, not redistributed).
/// </summary>
internal sealed class MidsHomecomingReceivedItemTaxonomyCatalog : IReceivedItemTaxonomyCatalog
{
    private readonly Dictionary<string, ReceivedItemClassificationMetadata> _salvage =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, ReceivedItemClassificationMetadata> _enhancements =
        new(StringComparer.OrdinalIgnoreCase);

    public bool IsLoaded { get; private set; }

    public static MidsHomecomingReceivedItemTaxonomyCatalog TryLoadFromHomecomingDatabasePath(string? homecomingDatabasePath)
    {
        var catalog = new MidsHomecomingReceivedItemTaxonomyCatalog();
        if (string.IsNullOrWhiteSpace(homecomingDatabasePath))
        {
            return catalog;
        }

        try
        {
            var salvagePath = Path.Combine(homecomingDatabasePath, MidsPathRules.SalvageDatabaseFileName);
            if (File.Exists(salvagePath))
            {
                catalog.LoadSalvage(salvagePath);
            }

            var enhancementPath = Path.Combine(homecomingDatabasePath, MidsPathRules.EnhancementDatabaseFileName);
            if (File.Exists(enhancementPath))
            {
                catalog.LoadEnhancements(enhancementPath);
            }

            catalog.IsLoaded = catalog._salvage.Count > 0 || catalog._enhancements.Count > 0;
        }
        catch
        {
            catalog._salvage.Clear();
            catalog._enhancements.Clear();
            catalog.IsLoaded = false;
        }

        return catalog;
    }

    public bool TryClassifySalvage(string normalizedItemText, out ReceivedItemClassificationMetadata metadata) =>
        _salvage.TryGetValue(normalizedItemText, out metadata!);

    public bool TryClassifyEnhancement(string normalizedItemText, out ReceivedItemClassificationMetadata metadata) =>
        _enhancements.TryGetValue(normalizedItemText, out metadata!);

    public bool TryClassifyInspiration(string normalizedItemText, out ReceivedItemClassificationMetadata metadata)
    {
        metadata = null!;
        return false;
    }

    private void LoadSalvage(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new BinaryReader(stream);
        if (!string.Equals(reader.ReadString(), MidsHomecomingDatabaseHeaders.Salvage, StringComparison.Ordinal))
        {
            return;
        }

        var count = reader.ReadInt32() + 1;
        for (var index = 0; index < count; index++)
        {
            reader.ReadString();
            var externalName = reader.ReadString();
            var rarity = (MidsRecipeRarity)reader.ReadInt32();
            var levelMin = reader.ReadInt32();
            var levelMax = reader.ReadInt32();
            reader.ReadInt32();

            if (string.IsNullOrWhiteSpace(externalName))
            {
                continue;
            }

            var metadata = new ReceivedItemClassificationMetadata
            {
                SalvageRarity = rarity.ToString(),
                SalvageLevelMin = levelMin,
                SalvageLevelMax = levelMax
            };
            _salvage[GameplayReceivedItemNormalization.NormalizeLookupKey(externalName)] = metadata;
        }
    }

    private void LoadEnhancements(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new BinaryReader(stream);
        if (!string.Equals(reader.ReadString(), MidsHomecomingDatabaseHeaders.Enhancement, StringComparison.Ordinal))
        {
            return;
        }

        reader.ReadSingle();
        var enhancementCount = reader.ReadInt32() + 1;
        var enhancementRecords = new List<MidsEnhancementRecord>(enhancementCount);
        for (var index = 0; index < enhancementCount; index++)
        {
            enhancementRecords.Add(MidsBinaryEnhancementReader.ReadRecord(reader));
        }

        var setCount = reader.ReadInt32() + 1;
        var setDisplayNames = new List<string>(setCount);
        for (var index = 0; index < setCount; index++)
        {
            setDisplayNames.Add(MidsBinaryEnhancementSetReader.ReadDisplayName(reader));
        }

        foreach (var record in enhancementRecords)
        {
            if (string.IsNullOrWhiteSpace(record.Name))
            {
                continue;
            }

            var setDisplayName = record.SetIndex >= 0 && record.SetIndex < setDisplayNames.Count
                ? setDisplayNames[record.SetIndex]
                : null;
            var longName = BuildEnhancementLongName(record, setDisplayNames);
            var metadata = new ReceivedItemClassificationMetadata
            {
                EnhancementSetName = setDisplayName,
                EnhancementLevelMin = record.LevelMin,
                EnhancementLevelMax = record.LevelMax,
                EnhancementRarity = record.Superior ? "Superior" : "Regular",
                EnhancementTypeLabel = record.TypeLabel
            };

            IndexEnhancementName(longName, metadata);
            if (!string.Equals(longName, record.Name, StringComparison.Ordinal))
            {
                IndexEnhancementName(record.Name, metadata);
            }
        }
    }

    private void IndexEnhancementName(string name, ReceivedItemClassificationMetadata metadata) =>
        _enhancements[GameplayReceivedItemNormalization.NormalizeLookupKey(name)] = metadata;

    private static string BuildEnhancementLongName(MidsEnhancementRecord record, IReadOnlyList<string> setDisplayNames)
    {
        switch (record.TypeId)
        {
            case MidsEnhancementTypeId.Invention:
                return $"Invention: {record.Name}";
            case MidsEnhancementTypeId.Set when record.SetIndex >= 0 && record.SetIndex < setDisplayNames.Count:
                return $"{setDisplayNames[record.SetIndex]}: {record.Name}";
            default:
                return record.Name;
        }
    }
}

internal static class MidsHomecomingDatabaseHeaders
{
    public const string Salvage = "Mids Reborn Salvage Database";
    public const string Recipe = "Mids Reborn Recipe Database";
    public const string Enhancement = "Mids Reborn Enhancement Database";
}

internal enum MidsRecipeRarity
{
    Common,
    Uncommon,
    Rare,
    UltraRare
}

internal enum MidsEnhancementTypeId
{
    None = 0,
    Normal = 1,
    Invention = 2,
    SpecialOrigin = 3,
    Set = 4
}

internal sealed record MidsEnhancementRecord
{
    public required string Name { get; init; }

    public required MidsEnhancementTypeId TypeId { get; init; }

    public int SetIndex { get; init; }

    public int LevelMin { get; init; }

    public int LevelMax { get; init; }

    public bool Superior { get; init; }

    public string TypeLabel { get; init; } = string.Empty;
}

internal static class MidsBinaryEnhancementReader
{
    public static MidsEnhancementRecord ReadRecord(BinaryReader reader)
    {
        reader.ReadInt32();
        var name = reader.ReadString();
        reader.ReadString();
        reader.ReadString();
        var typeId = (MidsEnhancementTypeId)reader.ReadInt32();
        reader.ReadInt32();
        var classCount = reader.ReadInt32() + 1;
        for (var index = 0; index < classCount; index++)
        {
            reader.ReadInt32();
        }

        reader.ReadString();
        var setIndex = reader.ReadInt32();
        reader.ReadString();
        reader.ReadSingle();
        var levelMin = reader.ReadInt32();
        var levelMax = reader.ReadInt32();
        reader.ReadBoolean();
        reader.ReadInt32();
        reader.ReadInt32();
        var effectCount = reader.ReadInt32() + 1;
        for (var index = 0; index < effectCount; index++)
        {
            reader.ReadInt32();
            reader.ReadInt32();
            reader.ReadInt32();
            reader.ReadInt32();
            reader.ReadInt32();
            reader.ReadSingle();
            if (reader.ReadBoolean())
            {
                MidsBinaryEffectReader.Skip(reader);
            }
        }

        reader.ReadString();
        reader.ReadString();
        var superior = reader.ReadBoolean();
        reader.ReadBoolean();
        reader.ReadBoolean();

        return new MidsEnhancementRecord
        {
            Name = name,
            TypeId = typeId,
            SetIndex = setIndex,
            LevelMin = levelMin,
            LevelMax = levelMax,
            Superior = superior,
            TypeLabel = MapTypeLabel(typeId)
        };
    }

    private static string MapTypeLabel(MidsEnhancementTypeId typeId) =>
        typeId switch
        {
            MidsEnhancementTypeId.Invention => "Invention",
            MidsEnhancementTypeId.Set => "Set",
            MidsEnhancementTypeId.SpecialOrigin => "Special",
            MidsEnhancementTypeId.Normal => "Normal",
            _ => string.Empty
        };
}

internal static class MidsBinaryEnhancementSetReader
{
    public static string ReadDisplayName(BinaryReader reader)
    {
        var displayName = reader.ReadString();
        reader.ReadString();
        reader.ReadString();
        reader.ReadString();
        reader.ReadInt32();
        reader.ReadString();
        reader.ReadInt32();
        reader.ReadInt32();
        var enhancementCount = reader.ReadInt32() + 1;
        for (var index = 0; index < enhancementCount; index++)
        {
            reader.ReadInt32();
        }

        var bonusCount = reader.ReadInt32() + 1;
        for (var index = 0; index < bonusCount; index++)
        {
            SkipRegularBonusItem(reader);
        }

        var specialBonusCount = reader.ReadInt32() + 1;
        for (var index = 0; index < specialBonusCount; index++)
        {
            SkipSpecialBonusItem(reader);
        }

        return displayName;
    }

    private static void SkipRegularBonusItem(BinaryReader reader)
    {
        reader.ReadInt32();
        reader.ReadString();
        reader.ReadInt32();
        reader.ReadInt32();
        var nameCount = reader.ReadInt32() + 1;
        for (var index = 0; index < nameCount; index++)
        {
            reader.ReadString();
            reader.ReadInt32();
        }
    }

    private static void SkipSpecialBonusItem(BinaryReader reader)
    {
        reader.ReadInt32();
        reader.ReadString();
        var nameCount = reader.ReadInt32() + 1;
        for (var index = 0; index < nameCount; index++)
        {
            reader.ReadString();
            reader.ReadInt32();
        }
    }
}

internal static class MidsBinaryEffectReader
{
    public static void Skip(BinaryReader reader)
    {
        reader.ReadString();
        reader.ReadInt32();
        reader.ReadInt32();
        reader.ReadInt32();
        reader.ReadInt32();
        reader.ReadInt32();
        reader.ReadInt32();
        reader.ReadString();
        reader.ReadSingle();
        reader.ReadInt32();
        reader.ReadInt32();
        reader.ReadSingle();
        reader.ReadInt32();
        reader.ReadBoolean();
        reader.ReadBoolean();
        reader.ReadInt32();
        reader.ReadBoolean();
        reader.ReadBoolean();
        reader.ReadInt32();
        reader.ReadInt32();
        reader.ReadInt32();
        reader.ReadSingle();
        reader.ReadSingle();
        reader.ReadSingle();
        reader.ReadInt32();
        reader.ReadInt32();
        reader.ReadString();
        reader.ReadInt32();
        reader.ReadBoolean();
        reader.ReadBoolean();
        reader.ReadBoolean();
        reader.ReadString();
        reader.ReadInt32();
        reader.ReadString();
        reader.ReadString();
        reader.ReadString();
        reader.ReadBoolean();
        reader.ReadString();
        reader.ReadSingle();
        reader.ReadInt32();
        reader.ReadSingle();
        reader.ReadSingle();
        reader.ReadInt32();
        reader.ReadSingle();
        reader.ReadSingle();
        reader.ReadInt32();
        reader.ReadSingle();
        reader.ReadSingle();
        reader.ReadSingle();
        reader.ReadSingle();
        reader.ReadSingle();
        reader.ReadInt32();
        reader.ReadSingle();
        reader.ReadSingle();
        reader.ReadInt32();
        reader.ReadSingle();
        reader.ReadSingle();
        reader.ReadSingle();
        reader.ReadSingle();
        var conditionalCount = reader.ReadInt32();
        for (var index = 0; index < conditionalCount; index++)
        {
            reader.ReadString();
            reader.ReadString();
        }
    }
}
