using CoHAnalytics.Models;
using CoHAnalytics.ReferenceData;

namespace CoHAnalytics.Services;

public interface IProcLogNameIndex
{
    bool CanIdentifyProcs { get; }
    IReadOnlyList<FrozenProcIdentity> Identities { get; }
    bool IsExactProcIdentity(string powerName);
    bool IsAmbiguous(string powerName);
    bool IsKnownGlobalOrIncarnate(string powerName);
}

/// <summary>Fixture-validated exact mappings, not all enhancement display names.</summary>
public sealed class ProcLogNameIndex : IProcLogNameIndex
{
    // Architecture section 11 examples, not an exhaustive classifier. Absence proves nothing.
    private static readonly HashSet<string> GlobalNames = new(StringComparer.Ordinal)
        { "Reactive Interface", "Doublehit", "Particle Burst" };
    private readonly Dictionary<string, FrozenProcIdentity[]> _byName;
    private ProcLogNameIndex(IEnumerable<FrozenProcIdentity> identities)
    {
        Identities = Array.AsReadOnly(identities.Distinct().OrderBy(i => i.LogName, StringComparer.Ordinal)
            .ThenBy(i => i.CatalogItemId, StringComparer.Ordinal).ToArray());
        _byName = Identities.GroupBy(i => i.LogName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
    }
    public static ProcLogNameIndex Empty { get; } = new([]);
    public IReadOnlyList<FrozenProcIdentity> Identities { get; }
    public bool CanIdentifyProcs => Identities.Count > 0;
    public static ProcLogNameIndex FromFrozen(IEnumerable<FrozenProcIdentity> identities) => new(identities);
    public static ProcLogNameIndex FromCatalog(IItemReferenceCatalog catalog) =>
        catalog.IsLoaded ? FromItems(catalog.GetEnhancements()) : Empty;

    internal static ProcLogNameIndex FromItems(IEnumerable<ItemReferenceRecord> items)
    {
        var rows = items.ToArray();
        // The real build and combat fixtures prove this exact stable-item/log-name mapping only.
        // The catalog has no generic proc/log-name field. Do not infer one from display prose.
        var validated = rows.Where(i => i.CatalogItemId == "ENH-01287"
            && i.CurrentDisplayName == "Armageddon: Chance for Fire Damage"
            && i.SourceVariants.Any(v => EnhancementTokenResolver.TokenFromSourceId(v.HomecomingSourceId) == "Crafted_Armageddon_F"))
            .Select(i => i.CurrentDisplayName).ToHashSet(StringComparer.Ordinal);
        return new(rows.Where(i => validated.Contains(i.CurrentDisplayName))
            .Select(i => new FrozenProcIdentity(i.CurrentDisplayName, i.CatalogItemId)));
    }
    public bool IsExactProcIdentity(string name) => _byName.ContainsKey(name);
    public bool IsAmbiguous(string name) => _byName.TryGetValue(name, out var ids) && ids.Length != 1;
    public bool IsKnownGlobalOrIncarnate(string name) => GlobalNames.Contains(name);
}
