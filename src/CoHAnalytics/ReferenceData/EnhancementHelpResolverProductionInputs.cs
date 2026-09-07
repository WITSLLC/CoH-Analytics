namespace CoHAnalytics.ReferenceData;

/// <summary>Embedded production catalog plus I1 NamedTables for static help resolution.</summary>
public sealed record EnhancementHelpResolverProductionInputs(
    IItemReferenceCatalog Catalog,
    IReadOnlyDictionary<string, IReadOnlyList<float>>? NamedTables);
