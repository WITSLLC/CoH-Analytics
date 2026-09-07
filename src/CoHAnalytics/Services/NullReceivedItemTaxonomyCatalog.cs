using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

/// <summary>Fallback catalog when no authoritative Homecoming reference data is available.</summary>
internal sealed class NullReceivedItemTaxonomyCatalog : IReceivedItemTaxonomyCatalog
{
    public static NullReceivedItemTaxonomyCatalog Instance { get; } = new();

    public bool IsLoaded => false;

    public bool TryClassifySalvage(string normalizedItemText, out ReceivedItemClassificationMetadata metadata)
    {
        metadata = null!;
        return false;
    }

    public bool TryClassifyEnhancement(string normalizedItemText, out ReceivedItemClassificationMetadata metadata)
    {
        metadata = null!;
        return false;
    }

    public bool TryClassifyInspiration(string normalizedItemText, out ReceivedItemClassificationMetadata metadata)
    {
        metadata = null!;
        return false;
    }
}
