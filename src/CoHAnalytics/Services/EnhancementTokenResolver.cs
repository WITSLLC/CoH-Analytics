using System.Security.Cryptography;
using System.Text.Json;
using CoHAnalytics.Models;
using CoHAnalytics.ReferenceData;

namespace CoHAnalytics.Services;

/// <summary>Collision-aware raw enhancement token to catalog item mapping.</summary>
public sealed class EnhancementTokenResolver
{
    private readonly Dictionary<string, ItemReferenceRecord> _unique =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> _collisions = new(StringComparer.Ordinal);

    public string? CatalogFingerprint { get; private set; }
    public ProcLogNameIndex ProcLogNames { get; private set; } = ProcLogNameIndex.Empty;

    private EnhancementTokenResolver()
    {
    }

    public static EnhancementTokenResolver FromCatalog(IItemReferenceCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var resolver = new EnhancementTokenResolver();
        if (!catalog.IsLoaded)
        {
            return resolver;
        }

        return FromItems(catalog.GetEnhancements(), catalog.Manifest?.CatalogVersion);
    }

    internal static EnhancementTokenResolver FromItems(IEnumerable<ItemReferenceRecord> source, string? version)
    {
        var resolver = new EnhancementTokenResolver();
        var items = source.ToArray();
        resolver.ProcLogNames = ProcLogNameIndex.FromItems(items);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            Version = version,
            Items = items.OrderBy(i => i.CatalogItemId, StringComparer.Ordinal).Select(i => new
            {
                i.CatalogItemId, i.CurrentDisplayName, i.EnhancementSetId,
                Variants = i.SourceVariants.Select(v => v.HomecomingSourceId).OrderBy(v => v, StringComparer.Ordinal)
            })
        });
        resolver.CatalogFingerprint = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        foreach (var item in items)
        {
            foreach (var variant in item.SourceVariants)
            {
                resolver.Register(variant.HomecomingSourceId, item);
                var token = TokenFromSourceId(variant.HomecomingSourceId);
                if (token is not null)
                {
                    resolver.Register(token, item);
                }
            }
        }

        return resolver;
    }

    public ProcMappingStatus TryResolve(string? rawEnhancementToken, out ItemReferenceRecord? item)
    {
        item = null;
        if (string.IsNullOrWhiteSpace(rawEnhancementToken))
        {
            return ProcMappingStatus.Unknown;
        }

        if (IsCollision(rawEnhancementToken))
        {
            return ProcMappingStatus.Collision;
        }

        if (_unique.TryGetValue(rawEnhancementToken, out item))
        {
            return ProcMappingStatus.Resolved;
        }

        var dotted = $"Boosts.{rawEnhancementToken}.{rawEnhancementToken}";
        if (IsCollision(dotted))
        {
            return ProcMappingStatus.Collision;
        }

        if (_unique.TryGetValue(dotted, out item))
        {
            return ProcMappingStatus.Resolved;
        }

        return ProcMappingStatus.Incomplete;
    }

    private bool IsCollision(string key) =>
        _collisions.Contains(key);

    private void Register(string key, ItemReferenceRecord item)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        if (_collisions.Contains(key))
        {
            return;
        }

        if (_unique.TryGetValue(key, out var existing))
        {
            if (existing.CatalogItemId == item.CatalogItemId)
            {
                return;
            }

            _unique.Remove(key);
            _collisions.Add(key);
            return;
        }

        _unique[key] = item;
    }

    internal static string? TokenFromSourceId(string? homecomingSourceId)
    {
        if (string.IsNullOrWhiteSpace(homecomingSourceId))
        {
            return null;
        }

        var separator = homecomingSourceId.LastIndexOf('.');
        return separator < 0 ? homecomingSourceId : homecomingSourceId[(separator + 1)..];
    }
}
