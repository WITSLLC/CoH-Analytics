using CoHAnalytics.Models;
using CoHAnalytics.ReferenceData;

namespace CoHAnalytics.Services;

/// <summary>
/// Maps the embedded production item reference catalog into received-item taxonomy lookups.
/// </summary>
internal sealed class ItemReferenceReceivedItemTaxonomyCatalog : IReceivedItemTaxonomyCatalog
{
    private readonly IItemReferenceCatalog _catalog;

    public ItemReferenceReceivedItemTaxonomyCatalog(IItemReferenceCatalog catalog)
    {
        _catalog = catalog;
    }

    public bool IsLoaded => _catalog.IsLoaded;

    public bool TryClassifySalvage(string normalizedItemText, out ReceivedItemClassificationMetadata metadata)
    {
        if (!_catalog.TryResolve(normalizedItemText, out var resolution)
            || resolution.Item.Family is not ReferenceItemFamily.Salvage)
        {
            metadata = null!;
            return false;
        }

        metadata = ToSalvageMetadata(resolution.Item) with
        {
            CatalogVersion = resolution.CatalogVersion
        };
        return true;
    }

    public bool TryClassifyEnhancement(string normalizedItemText, out ReceivedItemClassificationMetadata metadata)
    {
        if (!_catalog.TryResolve(normalizedItemText, out var resolution)
            || resolution.Item.Family is not ReferenceItemFamily.Enhancement)
        {
            metadata = null!;
            return false;
        }

        metadata = ToEnhancementMetadata(resolution.Item) with
        {
            CatalogVersion = resolution.CatalogVersion
        };
        return true;
    }

    public bool TryClassifyInspiration(string normalizedItemText, out ReceivedItemClassificationMetadata metadata)
    {
        if (!_catalog.TryResolve(normalizedItemText, out var resolution)
            || resolution.Item.Family is not ReferenceItemFamily.Inspiration)
        {
            metadata = null!;
            return false;
        }

        metadata = ToInspirationMetadata(resolution.Item) with
        {
            CatalogVersion = resolution.CatalogVersion
        };
        return true;
    }

    private static ReceivedItemClassificationMetadata ToSalvageMetadata(ItemReferenceRecord item) =>
        new()
        {
            CatalogItemId = item.CatalogItemId,
            SalvageRarity = item.Rarity
        };

    private static ReceivedItemClassificationMetadata ToEnhancementMetadata(ItemReferenceRecord item) =>
        new()
        {
            CatalogItemId = item.CatalogItemId,
            EnhancementTypeLabel = item.Subtype,
            EnhancementRarity = item.Variant,
            EnhancementSetName = item.EnhancementSetId
        };

    private static ReceivedItemClassificationMetadata ToInspirationMetadata(ItemReferenceRecord item) =>
        new()
        {
            CatalogItemId = item.CatalogItemId,
            InspirationForm = item.Subtype
        };
}

/// <summary>Chains taxonomy catalogs; the first match wins.</summary>
internal sealed class ChainedReceivedItemTaxonomyCatalog : IReceivedItemTaxonomyCatalog
{
    private readonly IReadOnlyList<IReceivedItemTaxonomyCatalog> _catalogs;

    public ChainedReceivedItemTaxonomyCatalog(IReadOnlyList<IReceivedItemTaxonomyCatalog> catalogs)
    {
        _catalogs = catalogs;
    }

    public bool IsLoaded => _catalogs.Any(catalog => catalog.IsLoaded);

    public bool TryClassifySalvage(string normalizedItemText, out ReceivedItemClassificationMetadata metadata)
    {
        foreach (var catalog in _catalogs)
        {
            if (catalog.TryClassifySalvage(normalizedItemText, out metadata))
            {
                return true;
            }
        }

        metadata = null!;
        return false;
    }

    public bool TryClassifyEnhancement(string normalizedItemText, out ReceivedItemClassificationMetadata metadata)
    {
        foreach (var catalog in _catalogs)
        {
            if (catalog.TryClassifyEnhancement(normalizedItemText, out metadata))
            {
                return true;
            }
        }

        metadata = null!;
        return false;
    }

    public bool TryClassifyInspiration(string normalizedItemText, out ReceivedItemClassificationMetadata metadata)
    {
        foreach (var catalog in _catalogs)
        {
            if (catalog.TryClassifyInspiration(normalizedItemText, out metadata))
            {
                return true;
            }
        }

        metadata = null!;
        return false;
    }
}
