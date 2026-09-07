using CoHAnalytics.Models;
using CoHAnalytics.ReferenceData;

namespace CoHAnalytics.Services;

/// <summary>Enriches generic received-item text into research-backed reward categories.</summary>
public interface IGameplayReceivedItemClassifier
{
    ReceivedItemClassificationResult Classify(string rawItemText);

    bool TryClassify(string rawItemText, out GameplaySessionRewardCategory category, out ReceivedItemClassificationMetadata? metadata);
}

internal static class GameplayReceivedItemNormalization
{
    public static string NormalizeLookupKey(string text) =>
        text.Trim();
}

internal sealed class GameplayReceivedItemClassifier : IGameplayReceivedItemClassifier
{
    private readonly IReceivedItemTaxonomyCatalog _catalog;
    private readonly IItemReferenceCatalog? _itemReferenceCatalog;

    public GameplayReceivedItemClassifier(
        IReceivedItemTaxonomyCatalog catalog,
        IItemReferenceCatalog? itemReferenceCatalog = null)
    {
        _catalog = catalog;
        _itemReferenceCatalog = itemReferenceCatalog;
    }

    public ReceivedItemClassificationResult Classify(string rawItemText)
    {
        if (string.IsNullOrWhiteSpace(rawItemText))
        {
            return Unresolved();
        }

        var trimmed = rawItemText.Trim();
        if (GameplayReceivedItemRecipeGrammar.IsRecipeReceivedItem(trimmed))
        {
            if (_itemReferenceCatalog is not null
                && _itemReferenceCatalog.TryResolve(trimmed, out var recipeResolution)
                && recipeResolution.Item.Family is ReferenceItemFamily.Recipe)
            {
                return Resolved(
                    GameplaySessionRewardCategory.Recipe,
                    ReferenceItemFamily.Recipe,
                    recipeResolution.Item.CatalogItemId,
                    recipeResolution.CatalogVersion,
                    metadata: null);
            }

            return PresentedUnresolved(
                GameplaySessionRewardCategory.Recipe,
                ReferenceItemFamily.Recipe);
        }

        foreach (var lookupKey in BuildEnhancementLookupKeys(trimmed))
        {
            if (_catalog.TryClassifyEnhancement(lookupKey, out var enhancementMetadata))
            {
                return FromTaxonomy(
                    GameplaySessionRewardCategory.Enhancement,
                    ReferenceItemFamily.Enhancement,
                    enhancementMetadata);
            }
        }

        var genericLookupKey = GameplayReceivedItemNormalization.NormalizeLookupKey(trimmed);
        if (_catalog.TryClassifyInspiration(genericLookupKey, out var inspirationMetadata))
        {
            return FromTaxonomy(
                GameplaySessionRewardCategory.Inspiration,
                ReferenceItemFamily.Inspiration,
                inspirationMetadata);
        }

        if (_catalog.TryClassifySalvage(genericLookupKey, out var salvageMetadata))
        {
            return FromTaxonomy(
                GameplaySessionRewardCategory.Salvage,
                ReferenceItemFamily.Salvage,
                salvageMetadata);
        }

        return Unresolved();
    }

    public bool TryClassify(
        string rawItemText,
        out GameplaySessionRewardCategory category,
        out ReceivedItemClassificationMetadata? metadata)
    {
        var result = Classify(rawItemText);
        category = result.PresentationCategory;
        metadata = result.Metadata;
        return result.HasKnownPresentation;
    }

    private static ReceivedItemClassificationResult FromTaxonomy(
        GameplaySessionRewardCategory category,
        ReferenceItemFamily family,
        ReceivedItemClassificationMetadata metadata) =>
        string.IsNullOrWhiteSpace(metadata.CatalogItemId)
            ? PresentedUnresolved(category, family, metadata)
            : Resolved(
                category,
                family,
                metadata.CatalogItemId,
                metadata.CatalogVersion,
                metadata);

    private static ReceivedItemClassificationResult Resolved(
        GameplaySessionRewardCategory category,
        ReferenceItemFamily family,
        string catalogItemId,
        string? catalogVersion,
        ReceivedItemClassificationMetadata? metadata) =>
        new()
        {
            PresentationCategory = category,
            ResolutionState = AcquisitionIdentityResolutionState.Resolved,
            FamilyHint = family,
            CatalogItemId = catalogItemId,
            CatalogVersion = catalogVersion,
            Metadata = metadata
        };

    private static ReceivedItemClassificationResult PresentedUnresolved(
        GameplaySessionRewardCategory category,
        ReferenceItemFamily family,
        ReceivedItemClassificationMetadata? metadata = null) =>
        new()
        {
            PresentationCategory = category,
            ResolutionState = AcquisitionIdentityResolutionState.PresentedUnresolved,
            FamilyHint = family,
            Metadata = metadata
        };

    private static ReceivedItemClassificationResult Unresolved() =>
        new()
        {
            PresentationCategory = GameplaySessionRewardCategory.ReceivedItem,
            ResolutionState = AcquisitionIdentityResolutionState.Unresolved
        };

    private static IEnumerable<string> BuildEnhancementLookupKeys(string trimmed)
    {
        var lookupKey = GameplayReceivedItemNormalization.NormalizeLookupKey(trimmed);
        yield return lookupKey;

        var colonLookupKey = TryBuildSetPieceParenthesesToColonLookupKey(trimmed);
        if (colonLookupKey is not null
            && !string.Equals(colonLookupKey, lookupKey, StringComparison.OrdinalIgnoreCase))
        {
            yield return colonLookupKey;
        }
    }

    /// <summary>
    /// Homecoming log receipts use parentheses for set pieces, e.g. "Bands of Hermes (Immobilize)",
    /// while catalog and Mids long names use a colon separator.
    /// </summary>
    private static string? TryBuildSetPieceParenthesesToColonLookupKey(string trimmed)
    {
        if (trimmed.EndsWith("(Recipe)", StringComparison.Ordinal))
        {
            return null;
        }

        var openIndex = trimmed.LastIndexOf('(');
        if (openIndex <= 0 || trimmed[^1] is not ')')
        {
            return null;
        }

        var setName = trimmed[..openIndex].TrimEnd();
        var pieceName = trimmed[(openIndex + 1)..^1].Trim();
        if (string.IsNullOrWhiteSpace(setName) || string.IsNullOrWhiteSpace(pieceName))
        {
            return null;
        }

        return $"{setName}: {pieceName}";
    }
}

internal static class GameplayReceivedItemRecipeGrammar
{
    public static bool IsRecipeReceivedItem(string itemText) =>
        itemText.EndsWith("(Recipe)", StringComparison.Ordinal);
}
