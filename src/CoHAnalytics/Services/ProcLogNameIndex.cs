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

/// <summary>
/// Exact catalogued damage-proc identities, not all enhancement display names. Duplicate
/// display names are retained so that ambiguity survives instead of a winner being chosen.
/// </summary>
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

    /// <summary>
    /// Authored Homecoming schedule table naming a boost effect as proc damage. Structural
    /// evidence from <c>classes.bin</c>, not display prose, so it admits damage procs only.
    /// </summary>
    private const string ProcDamageScheduleTable = "Melee_ProcDamage";

    internal static ProcLogNameIndex FromItems(IEnumerable<ItemReferenceRecord> items) =>
        new(items.Where(IsDamageProc)
            .Select(item => new FrozenProcIdentity(item.CurrentDisplayName, item.CatalogItemId)));

    /// <summary>
    /// A damage proc announces itself in the combat log under its Enhancement display name.
    /// Enhancements without a proc-damage effect (global, set-bonus, and debuff-only pieces)
    /// have no damage telemetry identity and stay unrecognised.
    /// </summary>
    private static bool IsDamageProc(ItemReferenceRecord item) =>
        item.Family == ReferenceItemFamily.Enhancement
        && !string.IsNullOrWhiteSpace(item.CurrentDisplayName)
        && item.SourceVariants.Any(variant => variant.Effects.Any(effect =>
            string.Equals(effect.Table, ProcDamageScheduleTable, StringComparison.Ordinal)));
    public bool IsExactProcIdentity(string name) => _byName.ContainsKey(name);
    public bool IsAmbiguous(string name) => _byName.TryGetValue(name, out var ids) && ids.Length != 1;
    public bool IsKnownGlobalOrIncarnate(string name) => GlobalNames.Contains(name);
}
