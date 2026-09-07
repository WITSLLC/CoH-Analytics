using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

/// <summary>Authoritative lookup for salvage, enhancement, and inspiration received-item names.</summary>
public interface IReceivedItemTaxonomyCatalog
{
    bool IsLoaded { get; }

    bool TryClassifySalvage(string normalizedItemText, out ReceivedItemClassificationMetadata metadata);

    bool TryClassifyEnhancement(string normalizedItemText, out ReceivedItemClassificationMetadata metadata);

    bool TryClassifyInspiration(string normalizedItemText, out ReceivedItemClassificationMetadata metadata);
}
