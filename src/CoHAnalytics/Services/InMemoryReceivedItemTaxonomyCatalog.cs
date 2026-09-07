using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

/// <summary>In-memory taxonomy catalog for tests and narrow fixtures.</summary>
internal sealed class InMemoryReceivedItemTaxonomyCatalog : IReceivedItemTaxonomyCatalog
{
    private readonly Dictionary<string, ReceivedItemClassificationMetadata> _salvage =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, ReceivedItemClassificationMetadata> _enhancements =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, ReceivedItemClassificationMetadata> _inspirations =
        new(StringComparer.OrdinalIgnoreCase);

    public bool IsLoaded => _salvage.Count > 0 || _enhancements.Count > 0 || _inspirations.Count > 0;

    public void AddSalvage(string displayName, ReceivedItemClassificationMetadata metadata) =>
        _salvage[GameplayReceivedItemNormalization.NormalizeLookupKey(displayName)] = metadata;

    public void AddEnhancement(string displayName, ReceivedItemClassificationMetadata metadata) =>
        _enhancements[GameplayReceivedItemNormalization.NormalizeLookupKey(displayName)] = metadata;

    public void AddInspiration(string displayName, ReceivedItemClassificationMetadata metadata) =>
        _inspirations[GameplayReceivedItemNormalization.NormalizeLookupKey(displayName)] = metadata;

    public bool TryClassifySalvage(string normalizedItemText, out ReceivedItemClassificationMetadata metadata) =>
        _salvage.TryGetValue(normalizedItemText, out metadata!);

    public bool TryClassifyEnhancement(string normalizedItemText, out ReceivedItemClassificationMetadata metadata) =>
        _enhancements.TryGetValue(normalizedItemText, out metadata!);

    public bool TryClassifyInspiration(string normalizedItemText, out ReceivedItemClassificationMetadata metadata) =>
        _inspirations.TryGetValue(normalizedItemText, out metadata!);
}
