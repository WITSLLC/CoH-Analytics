using System.Windows.Media;
using CoHAnalytics.Homecoming;
using CoHAnalytics.ReferenceData;

namespace CoHAnalytics.ViewModels.Workspaces;

internal static class LiveSessionLootPresentationSupport
{
    public static LiveSessionItemRowViewModel CreateRow(
        IItemReferenceCatalog? catalog,
        IEnhancementIconCompositor? iconCompositor,
        IHomecomingBoostMetadataProvider? boostMetadata,
        IInstalledGameAssetProvider? assetProvider,
        string observedName,
        string quantityLabel,
        ReferenceItemFamily expectedFamily)
    {
        if (catalog is null || !TryResolveCatalogItem(catalog, observedName, expectedFamily, out var item))
        {
            return new LiveSessionItemRowViewModel
            {
                DisplayName = observedName,
                QuantityLabel = quantityLabel
            };
        }

        var rarityCode = ResolveRarityCode(catalog, item);
        var iconSource = ResolveIconSource(catalog, item, iconCompositor, boostMetadata, assetProvider);
        var presentationBrushKey = ResolvePresentationBrushKey(item, rarityCode);

        return new LiveSessionItemRowViewModel
        {
            DisplayName = item.CurrentDisplayName,
            QuantityLabel = quantityLabel,
            RarityCode = rarityCode,
            PresentationBrushKey = presentationBrushKey,
            IconSource = iconSource
        };
    }

    private static bool TryResolveCatalogItem(
        IItemReferenceCatalog catalog,
        string observedName,
        ReferenceItemFamily expectedFamily,
        out ItemReferenceRecord item)
    {
        if (catalog.TryResolve(observedName, out var resolution)
            && resolution.Item.Family == expectedFamily)
        {
            item = resolution.Item;
            return true;
        }

        if (catalog.TryResolve(
                observedName,
                out resolution,
                ReferenceCatalogQueryScope.AllHomecomingIdentities)
            && resolution.Item.Family == expectedFamily)
        {
            item = resolution.Item;
            return true;
        }

        item = null!;
        return false;
    }

    private static string? ResolveRarityCode(IItemReferenceCatalog catalog, ItemReferenceRecord item) =>
        item.Family switch
        {
            ReferenceItemFamily.Salvage => ReferenceRarityPresentation.SalvageRarityLabelToCode(item.Rarity),
            ReferenceItemFamily.Recipe => ReferenceRarityPresentation.RecipeRarityLabelToCode(item.Rarity),
            ReferenceItemFamily.Enhancement => ResolveEnhancementRarityCode(catalog, item),
            _ => null
        };

    private static string? ResolveEnhancementRarityCode(
        IItemReferenceCatalog catalog,
        ItemReferenceRecord item)
    {
        if (!string.IsNullOrWhiteSpace(item.EnhancementSetId)
            && catalog.TryGetEnhancementSetById(item.EnhancementSetId, out var parentSet))
        {
            return parentSet.RarityCode;
        }

        return null;
    }

    private static string ResolvePresentationBrushKey(ItemReferenceRecord item, string? rarityCode) =>
        item.Family switch
        {
            ReferenceItemFamily.Inspiration => InspirationPresentation.Resolve(item).PresentationKey,
            _ => ReferenceRarityPresentation.ResolveBrushResourceKey(rarityCode)
        };

    private static ImageSource? ResolveIconSource(
        IItemReferenceCatalog catalog,
        ItemReferenceRecord item,
        IEnhancementIconCompositor? iconCompositor,
        IHomecomingBoostMetadataProvider? boostMetadata,
        IInstalledGameAssetProvider? assetProvider) =>
        item.Family switch
        {
            ReferenceItemFamily.Enhancement => ResolveEnhancementIcon(catalog, item, iconCompositor, boostMetadata),
            ReferenceItemFamily.Recipe => ReferenceRecipeBrowseSupport.ResolveProducedEnhancementIcon(
                catalog,
                item.ProducedItemId,
                iconCompositor,
                boostMetadata),
            ReferenceItemFamily.Inspiration => assetProvider?.TryResolve(item.Icon),
            _ => null
        };

    private static ImageSource? ResolveEnhancementIcon(
        IItemReferenceCatalog catalog,
        ItemReferenceRecord item,
        IEnhancementIconCompositor? iconCompositor,
        IHomecomingBoostMetadataProvider? boostMetadata)
    {
        EnhancementSetReferenceRecord? parentSet = null;
        if (!string.IsNullOrWhiteSpace(item.EnhancementSetId)
            && catalog.TryGetEnhancementSetById(item.EnhancementSetId, out var resolvedSet))
        {
            parentSet = resolvedSet;
        }

        return ReferenceEnhancementBrowseSupport.ResolveComposedIconSource(
            iconCompositor,
            catalog,
            item,
            parentSet,
            boostMetadata);
    }
}
