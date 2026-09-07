using System.Windows.Media;
using CoHAnalytics.ReferenceData;

namespace CoHAnalytics.Homecoming;

public static class EnhancementIconCompositionSupport
{
    public static string? TryResolveSetIoFrameVariant(
        IItemReferenceCatalog catalog,
        EnhancementSetReferenceRecord set)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(set);

        if (!string.Equals(set.RarityCode, "ECVeryRare", StringComparison.Ordinal))
        {
            return null;
        }

        var hasSuperiorPresentationMember = catalog
            .GetEnhancements(ReferenceCatalogQueryScope.CurrentHomecoming)
            .Any(member =>
                string.Equals(member.EnhancementSetId, set.CatalogItemId, StringComparison.Ordinal)
                && HasSuperiorPresentation(member));

        return hasSuperiorPresentationMember ? "Superior" : null;
    }

    private static bool HasSuperiorPresentation(ItemReferenceRecord member) =>
        string.Equals(member.Variant, "Superior", StringComparison.Ordinal)
        || member.SourceVariants.Any(variant =>
            string.Equals(variant.SourceForm, "Superior_Attuned", StringComparison.Ordinal));

    public static EnhancementIconCompositionRequest? TryBuildRequest(
        ItemReferenceRecord item,
        EnhancementSetReferenceRecord? parentSet,
        EnhancementSourceVariantReferenceRecord iconVariant,
        IHomecomingBoostMetadataProvider? boostMetadata = null,
        IItemReferenceCatalog? catalog = null)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(iconVariant);

        var iconIdentity = iconVariant.Icon ?? item.Icon;
        var pogBoostType = TryResolvePogBoostType(item, iconVariant, boostMetadata);
        var frameVariant = parentSet is null
            ? item.Variant
            : catalog is not null
                ? TryResolveSetIoFrameVariant(catalog, parentSet)
                : null;
        return EnhancementIconCompositionRequest.TryCreate(
            iconIdentity,
            pogBoostType,
            item.EnhancementFamily,
            iconVariant.SourceForm,
            frameVariant,
            parentSet?.RarityCode);
    }

    public static ItemReferenceRecord? TryResolveSetArtworkRepresentativeMember(
        IItemReferenceCatalog catalog,
        EnhancementSetReferenceRecord set)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(set);

        return catalog
            .GetEnhancements(ReferenceCatalogQueryScope.CurrentHomecoming)
            .Where(item => string.Equals(item.EnhancementSetId, set.CatalogItemId, StringComparison.Ordinal))
            .Where(item => !string.IsNullOrWhiteSpace(item.Icon)
                           || item.SourceVariants.Any(variant => !string.IsNullOrWhiteSpace(variant.Icon)))
            .OrderBy(item => item.CatalogItemId, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    public static ImageSource? TryComposeSetArtworkIcon(
        IEnhancementIconCompositor? compositor,
        IItemReferenceCatalog catalog,
        EnhancementSetReferenceRecord set,
        Func<ItemReferenceRecord, EnhancementSourceVariantReferenceRecord?> selectIconVariant,
        IHomecomingBoostMetadataProvider? boostMetadata = null)
    {
        if (compositor is null)
        {
            return null;
        }

        var representative = TryResolveSetArtworkRepresentativeMember(catalog, set);
        if (representative is null)
        {
            return null;
        }

        var iconVariant = selectIconVariant(representative);
        return TryComposeIcon(compositor, catalog, representative, set, iconVariant, boostMetadata);
    }

    public static ImageSource? TryComposeIcon(
        IEnhancementIconCompositor? compositor,
        IItemReferenceCatalog? catalog,
        ItemReferenceRecord item,
        EnhancementSetReferenceRecord? parentSet,
        EnhancementSourceVariantReferenceRecord? iconVariant,
        IHomecomingBoostMetadataProvider? boostMetadata = null)
    {
        if (compositor is null || iconVariant is null)
        {
            return null;
        }

        var request = TryBuildRequest(item, parentSet, iconVariant, boostMetadata, catalog);
        return request is null ? null : compositor.TryCompose(request);
    }

    private static string? TryResolvePogBoostType(
        ItemReferenceRecord item,
        EnhancementSourceVariantReferenceRecord iconVariant,
        IHomecomingBoostMetadataProvider? boostMetadata)
    {
        if (!string.IsNullOrWhiteSpace(item.CommonIoBoostType)
            && !item.CommonIoBoostType.Contains('+', StringComparison.Ordinal))
        {
            return item.CommonIoBoostType;
        }

        var boostsAllowed = boostMetadata?.TryGetBoostsAllowed(iconVariant.HomecomingSourceId);
        return EnhancementPogBoostTypeResolver.TryResolvePrimaryBoostType(boostsAllowed);
    }
}
