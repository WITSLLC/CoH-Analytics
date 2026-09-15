using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CoHAnalytics.ReferenceData;

namespace CoHAnalytics.ReferenceDataGenerator;

internal sealed class CatalogPromotionWriteException : InvalidOperationException
{
    internal CatalogPromotionWriteException(string message)
        : base(message)
    {
    }
}

internal sealed record CatalogPromotionOwnership(
    string CommandName,
    IReadOnlySet<string> OwnedItemFamilies,
    IReadOnlySet<string> OwnedTopLevelProperties,
    IReadOnlySet<string> OwnedAliasIdPrefixes)
{
    internal static CatalogPromotionOwnership Badges { get; } = new(
        "promote-homecoming-badges",
        new HashSet<string>(StringComparer.Ordinal) { nameof(ReferenceItemFamily.Badge) },
        new HashSet<string>(StringComparer.Ordinal)
        {
            "badges",
            "badgeLocations",
            "zones",
            "badgeAccoladeRequirements"
        },
        new HashSet<string>(StringComparer.Ordinal) { "BAD-" });

    internal static CatalogPromotionOwnership BadgeLogReceiptAliases { get; } = new(
        "sync-badge-log-receipt-aliases",
        new HashSet<string>(StringComparer.Ordinal) { nameof(ReferenceItemFamily.Badge) },
        new HashSet<string>(StringComparer.Ordinal),
        new HashSet<string>(StringComparer.Ordinal) { "BAD-" });

    internal static CatalogPromotionOwnership BadgeRewards { get; } = new(
        "enrich-badge-rewards",
        new HashSet<string>(StringComparer.Ordinal) { nameof(ReferenceItemFamily.Badge) },
        new HashSet<string>(StringComparer.Ordinal) { "badges" },
        new HashSet<string>(StringComparer.Ordinal) { "BAD-" });

    internal static CatalogPromotionOwnership Inspirations { get; } = new(
        "promote-homecoming-inspirations",
        new HashSet<string>(StringComparer.Ordinal) { nameof(ReferenceItemFamily.Inspiration) },
        new HashSet<string>(StringComparer.Ordinal),
        new HashSet<string>(StringComparer.Ordinal) { "INS-" });

    internal static CatalogPromotionOwnership Enhancements { get; } = new(
        "promote-homecoming-enhancements",
        new HashSet<string>(StringComparer.Ordinal) { nameof(ReferenceItemFamily.Enhancement) },
        new HashSet<string>(StringComparer.Ordinal)
        {
            "enhancementSets",
            "enhancementResolverNamedTables"
        },
        new HashSet<string>(StringComparer.Ordinal) { "ENH-" });

    internal static CatalogPromotionOwnership Recipes { get; } = new(
        "promote-homecoming-recipes",
        new HashSet<string>(StringComparer.Ordinal)
        {
            nameof(ReferenceItemFamily.Recipe),
            nameof(ReferenceItemFamily.Salvage)
        },
        new HashSet<string>(StringComparer.Ordinal),
        new HashSet<string>(StringComparer.Ordinal) { "REC-", "SAL-" });

    internal static CatalogPromotionOwnership RouteOrdering { get; } = new(
        "promote-route-ordering",
        new HashSet<string>(StringComparer.Ordinal),
        new HashSet<string>(StringComparer.Ordinal)
        {
            "badgeLocations",
            "routeOrderingProvenance",
            "historyPlaqueRouteCollections",
            "historyPlaqueRouteStops"
        },
        new HashSet<string>(StringComparer.Ordinal));
}

internal static class CatalogPromotionWriteGuard
{
    internal const string AllowProductionWriteOption = "--allow-production-write";
    internal const string MaximumUnderstoodCatalogVersion = "item-ref-3.1.0";

    internal static readonly JsonSerializerOptions WriteOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    internal static bool IsAllowProductionWriteOption(string option) =>
        string.Equals(option, AllowProductionWriteOption, StringComparison.OrdinalIgnoreCase);

    internal static bool IsProductionCatalogPath(string catalogPath)
    {
        var fullPath = Path.GetFullPath(catalogPath);
        var marker = string.Concat(
            "src",
            Path.DirectorySeparatorChar,
            "CoHAnalytics",
            Path.DirectorySeparatorChar,
            "ReferenceData",
            Path.DirectorySeparatorChar,
            "item-catalog.v1.json");
        var altMarker = marker.Replace(Path.DirectorySeparatorChar, '/');
        return fullPath.EndsWith(marker, StringComparison.OrdinalIgnoreCase)
            || fullPath.Replace('\\', '/').EndsWith(altMarker, StringComparison.OrdinalIgnoreCase);
    }

    internal static byte[] Commit(
        string catalogPath,
        byte[] originalBytes,
        ItemReferenceCatalogDocument proposedDocument,
        CatalogPromotionOwnership ownership,
        bool allowProductionWrite = false,
        string? maximumUnderstoodCatalogVersion = null,
        JsonSerializerOptions? writeOptions = null)
    {
        ArgumentNullException.ThrowIfNull(proposedDocument);
        var proposedBytes = Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(proposedDocument, writeOptions ?? WriteOptions) + "\n");
        return CommitSerialized(
            catalogPath,
            originalBytes,
            proposedBytes,
            ownership,
            allowProductionWrite,
            maximumUnderstoodCatalogVersion);
    }

    internal static byte[] CommitSerialized(
        string catalogPath,
        byte[] originalBytes,
        byte[] proposedBytes,
        CatalogPromotionOwnership ownership,
        bool allowProductionWrite = false,
        string? maximumUnderstoodCatalogVersion = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogPath);
        ArgumentNullException.ThrowIfNull(originalBytes);
        ArgumentNullException.ThrowIfNull(proposedBytes);
        ArgumentNullException.ThrowIfNull(ownership);

        var catalogFullPath = Path.GetFullPath(catalogPath);
        if (IsProductionCatalogPath(catalogFullPath) && !allowProductionWrite)
        {
            throw new CatalogPromotionWriteException(
                $"{ownership.CommandName} refused to write the production authored catalog "
                + $"'{catalogFullPath}' without {AllowProductionWriteOption}.");
        }

        using var originalDocument = JsonDocument.Parse(originalBytes);
        using var proposedDocument = JsonDocument.Parse(proposedBytes);
        var originalRoot = originalDocument.RootElement;
        var proposedRoot = proposedDocument.RootElement;
        if (originalRoot.ValueKind != JsonValueKind.Object
            || proposedRoot.ValueKind != JsonValueKind.Object)
        {
            throw new CatalogPromotionWriteException(
                $"{ownership.CommandName} refused to write a non-object catalog document.");
        }

        var originalVersionText = ReadCatalogVersion(originalRoot);
        var proposedVersionText = ReadCatalogVersion(proposedRoot);
        var maximumUnderstood = maximumUnderstoodCatalogVersion ?? MaximumUnderstoodCatalogVersion;
        if (!HomecomingPromotionManifestSupport.TryParseCatalogVersion(maximumUnderstood, out var maximumVersion))
        {
            throw new CatalogPromotionWriteException(
                $"Promotion tool maximum understood catalog version '{maximumUnderstood}' is invalid.");
        }

        if (!HomecomingPromotionManifestSupport.TryParseCatalogVersion(originalVersionText, out var originalVersion)
            || originalVersion > maximumVersion)
        {
            throw new CatalogPromotionWriteException(
                $"{ownership.CommandName} refused to rewrite catalog version "
                + $"'{originalVersionText ?? "<missing>"}' because this executable understands at most "
                + $"'{maximumUnderstood}'.");
        }

        if (!HomecomingPromotionManifestSupport.TryParseCatalogVersion(proposedVersionText, out var proposedVersion)
            || proposedVersion < originalVersion)
        {
            throw new CatalogPromotionWriteException(
                $"{ownership.CommandName} refused to downgrade catalog version "
                + $"'{originalVersionText}' to '{proposedVersionText ?? "<missing>"}'.");
        }

        AssertUnownedStructurePreserved(originalRoot, proposedRoot, ownership);

        using (var validationStream = new MemoryStream(proposedBytes, writable: false))
        {
            var load = ItemReferenceCatalogLoader.Load(validationStream);
            if (!load.Succeeded)
            {
                throw new CatalogPromotionWriteException(
                    $"{ownership.CommandName} produced a catalog that failed validation: {load.FailureReason}");
            }
        }

        var tempPath = catalogFullPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllBytes(tempPath, proposedBytes);
            File.Move(tempPath, catalogFullPath, overwrite: true);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }

        return proposedBytes;
    }

    private static void AssertUnownedStructurePreserved(
        JsonElement originalRoot,
        JsonElement proposedRoot,
        CatalogPromotionOwnership ownership)
    {
        foreach (var property in originalRoot.EnumerateObject())
        {
            if (property.NameEquals("manifest")
                || property.NameEquals("items")
                || property.NameEquals("aliases")
                || ownership.OwnedTopLevelProperties.Contains(property.Name))
            {
                continue;
            }

            if (!proposedRoot.TryGetProperty(property.Name, out var proposedProperty))
            {
                throw new CatalogPromotionWriteException(
                    $"{ownership.CommandName} removed unrelated catalog section '{property.Name}'.");
            }

            RequireOriginalFieldsPreserved(
                property.Value,
                proposedProperty,
                property.Name,
                ownership.CommandName);
        }

        AssertUnownedItemsPreserved(originalRoot, proposedRoot, ownership);
        AssertUnownedAliasesPreserved(originalRoot, proposedRoot, ownership);
    }

    private static void AssertUnownedItemsPreserved(
        JsonElement originalRoot,
        JsonElement proposedRoot,
        CatalogPromotionOwnership ownership)
    {
        if (!originalRoot.TryGetProperty("items", out var originalItems)
            || originalItems.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var proposedItems = IndexObjectArray(proposedRoot, "items", "catalogItemId", ownership.CommandName);
        foreach (var item in originalItems.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var family = ReadString(item, "family") ?? string.Empty;
            if (ownership.OwnedItemFamilies.Contains(family))
            {
                continue;
            }

            var catalogItemId = ReadString(item, "catalogItemId");
            if (string.IsNullOrWhiteSpace(catalogItemId))
            {
                throw new CatalogPromotionWriteException(
                    $"{ownership.CommandName} encountered an original item without catalogItemId.");
            }

            if (!proposedItems.TryGetValue(catalogItemId, out var proposedItem))
            {
                throw new CatalogPromotionWriteException(
                    $"{ownership.CommandName} removed unrelated catalog item '{catalogItemId}'.");
            }

            RequireOriginalFieldsPreserved(
                item,
                proposedItem,
                $"items[{catalogItemId}]",
                ownership.CommandName);
        }
    }

    private static void AssertUnownedAliasesPreserved(
        JsonElement originalRoot,
        JsonElement proposedRoot,
        CatalogPromotionOwnership ownership)
    {
        if (!originalRoot.TryGetProperty("aliases", out var originalAliases)
            || originalAliases.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var proposedAliases = new List<JsonElement>();
        if (proposedRoot.TryGetProperty("aliases", out var proposedArray)
            && proposedArray.ValueKind == JsonValueKind.Array)
        {
            proposedAliases.AddRange(proposedArray.EnumerateArray());
        }

        foreach (var alias in originalAliases.EnumerateArray())
        {
            var catalogItemId = ReadString(alias, "catalogItemId") ?? string.Empty;
            if (IsOwnedAlias(catalogItemId, ownership))
            {
                continue;
            }

            var locale = ReadString(alias, "locale");
            var text = ReadString(alias, "text");
            var nameKind = ReadString(alias, "nameKind");
            var match = proposedAliases.FirstOrDefault(candidate =>
                string.Equals(ReadString(candidate, "catalogItemId"), catalogItemId, StringComparison.Ordinal)
                && string.Equals(ReadString(candidate, "locale"), locale, StringComparison.Ordinal)
                && string.Equals(ReadString(candidate, "text"), text, StringComparison.Ordinal)
                && string.Equals(ReadString(candidate, "nameKind"), nameKind, StringComparison.Ordinal));
            if (match.ValueKind != JsonValueKind.Object)
            {
                throw new CatalogPromotionWriteException(
                    $"{ownership.CommandName} removed unrelated alias '{catalogItemId}' / '{text}'.");
            }

            RequireOriginalFieldsPreserved(
                alias,
                match,
                $"aliases[{catalogItemId}:{text}]",
                ownership.CommandName);
        }
    }

    private static bool IsOwnedAlias(string catalogItemId, CatalogPromotionOwnership ownership)
    {
        foreach (var prefix in ownership.OwnedAliasIdPrefixes)
        {
            if (catalogItemId.StartsWith(prefix, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static void RequireOriginalFieldsPreserved(
        JsonElement original,
        JsonElement proposed,
        string path,
        string commandName)
    {
        if (original.ValueKind == JsonValueKind.Object)
        {
            if (proposed.ValueKind != JsonValueKind.Object)
            {
                throw new CatalogPromotionWriteException(
                    $"{commandName} changed unrelated value kind at '{path}'.");
            }

            foreach (var property in original.EnumerateObject())
            {
                if (!proposed.TryGetProperty(property.Name, out var proposedProperty))
                {
                    throw new CatalogPromotionWriteException(
                        $"{commandName} dropped unrelated field '{path}.{property.Name}'.");
                }

                RequireOriginalFieldsPreserved(
                    property.Value,
                    proposedProperty,
                    $"{path}.{property.Name}",
                    commandName);
            }

            return;
        }

        if (original.ValueKind == JsonValueKind.Array)
        {
            if (proposed.ValueKind != JsonValueKind.Array)
            {
                throw new CatalogPromotionWriteException(
                    $"{commandName} changed unrelated value kind at '{path}'.");
            }

            var originalItems = original.EnumerateArray().ToArray();
            var proposedItems = proposed.EnumerateArray().ToArray();
            if (TryIndexByKey(originalItems, "catalogItemId", out var originalByItemId)
                && TryIndexByKey(proposedItems, "catalogItemId", out var proposedByItemId))
            {
                foreach (var (key, originalItem) in originalByItemId)
                {
                    if (!proposedByItemId.TryGetValue(key, out var proposedItem))
                    {
                        throw new CatalogPromotionWriteException(
                            $"{commandName} removed unrelated '{path}' entry '{key}'.");
                    }

                    RequireOriginalFieldsPreserved(
                        originalItem,
                        proposedItem,
                        $"{path}[{key}]",
                        commandName);
                }

                return;
            }

            if (TryIndexByKey(originalItems, "homecomingSourceId", out var originalBySourceId)
                && TryIndexByKey(proposedItems, "homecomingSourceId", out var proposedBySourceId))
            {
                foreach (var (key, originalItem) in originalBySourceId)
                {
                    if (!proposedBySourceId.TryGetValue(key, out var proposedItem))
                    {
                        throw new CatalogPromotionWriteException(
                            $"{commandName} removed unrelated '{path}' entry '{key}'.");
                    }

                    RequireOriginalFieldsPreserved(
                        originalItem,
                        proposedItem,
                        $"{path}[{key}]",
                        commandName);
                }

                return;
            }

            if (originalItems.Length != proposedItems.Length)
            {
                throw new CatalogPromotionWriteException(
                    $"{commandName} changed unrelated array length at '{path}'.");
            }

            for (var index = 0; index < originalItems.Length; index++)
            {
                RequireOriginalFieldsPreserved(
                    originalItems[index],
                    proposedItems[index],
                    $"{path}[{index}]",
                    commandName);
            }

            return;
        }

        if (!JsonElement.DeepEquals(original, proposed))
        {
            throw new CatalogPromotionWriteException(
                $"{commandName} changed unrelated value at '{path}'.");
        }
    }

    private static Dictionary<string, JsonElement> IndexObjectArray(
        JsonElement root,
        string propertyName,
        string keyName,
        string commandName)
    {
        var indexed = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (!root.TryGetProperty(propertyName, out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return indexed;
        }

        foreach (var element in array.EnumerateArray())
        {
            var key = ReadString(element, keyName);
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            if (!indexed.TryAdd(key, element))
            {
                throw new CatalogPromotionWriteException(
                    $"{commandName} produced duplicate '{keyName}' '{key}' in '{propertyName}'.");
            }
        }

        return indexed;
    }

    private static bool TryIndexByKey(
        IReadOnlyList<JsonElement> elements,
        string keyName,
        out Dictionary<string, JsonElement> indexed)
    {
        indexed = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (elements.Count == 0)
        {
            return false;
        }

        foreach (var element in elements)
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                indexed.Clear();
                return false;
            }

            var key = ReadString(element, keyName);
            if (string.IsNullOrWhiteSpace(key) || !indexed.TryAdd(key, element))
            {
                indexed.Clear();
                return false;
            }
        }

        return true;
    }

    private static string? ReadCatalogVersion(JsonElement root)
    {
        if (!root.TryGetProperty("manifest", out var manifest)
            || manifest.ValueKind != JsonValueKind.Object
            || !manifest.TryGetProperty("catalogVersion", out var version)
            || version.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return version.GetString();
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(propertyName, out var value)
            || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return value.GetString();
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
    }
}
