namespace CoHAnalytics.ReferenceData;

/// <summary>
/// Canonical lookup for observed item text against the app-owned reference catalog.
/// Consumers resolve names here; file format and loader details remain internal.
/// </summary>
public interface IItemReferenceCatalog
{
    /// <summary>Whether the catalog loaded and passed validation.</summary>
    bool IsLoaded { get; }

    /// <summary>When <see cref="IsLoaded"/> is false, a short failure reason for diagnostics.</summary>
    string? LoadFailureReason { get; }

    /// <summary>Manifest metadata when the catalog loaded successfully.</summary>
    ItemReferenceManifest? Manifest { get; }

    /// <summary>
    /// Resolves exact observed text to a canonical item. Unknown spellings return false without throwing.
    /// Defaults to current Homecoming content only.
    /// </summary>
    bool TryResolve(
        string rawObservedText,
        out ItemReferenceResolution resolution,
        ReferenceCatalogQueryScope queryScope = ReferenceCatalogQueryScope.CurrentHomecoming);

    /// <summary>
    /// Resolves a stable catalog item id directly. Historical identities remain reachable.
    /// </summary>
    bool TryGetById(string catalogItemId, out ItemReferenceRecord item);

    /// <summary>
    /// Resolves a stable Enhancement Set id directly. Historical identities remain reachable.
    /// </summary>
    bool TryGetEnhancementSetById(string catalogItemId, out EnhancementSetReferenceRecord set);

    /// <summary>
    /// Returns Enhancement Sets for the requested query scope. Defaults to current Homecoming content only.
    /// </summary>
    IReadOnlyDictionary<string, EnhancementSetReferenceRecord> GetEnhancementSets(
        ReferenceCatalogQueryScope queryScope = ReferenceCatalogQueryScope.CurrentHomecoming);

    /// <summary>
    /// Returns Enhancement items for the requested query scope. Defaults to current Homecoming content only.
    /// </summary>
    IReadOnlyList<ItemReferenceRecord> GetEnhancements(
        ReferenceCatalogQueryScope queryScope = ReferenceCatalogQueryScope.CurrentHomecoming);

    /// <summary>
    /// Returns Recipe items. Recipes are not server-availability scoped.
    /// </summary>
    IReadOnlyList<ItemReferenceRecord> GetRecipes();

    /// <summary>
    /// Returns Badge items promoted into the catalog.
    /// </summary>
    IReadOnlyList<BadgeReferenceRecord> GetBadges();

    /// <summary>
    /// Resolves a stable Badge id directly.
    /// </summary>
    bool TryGetBadgeById(string catalogItemId, out BadgeReferenceRecord badge);

    /// <summary>
    /// Resolves a Badge by Homecoming source id.
    /// </summary>
    bool TryGetBadgeByHomecomingSourceId(string homecomingSourceId, out BadgeReferenceRecord badge);

    /// <summary>
    /// Returns zone metadata promoted into the catalog.
    /// </summary>
    IReadOnlyDictionary<string, ZoneReferenceRecord> GetZones();

    /// <summary>
    /// Returns promoted badge/plaque locations for a badge catalog item id.
    /// </summary>
    IReadOnlyList<BadgeLocationReferenceRecord> GetBadgeLocations(string badgeCatalogItemId);

    /// <summary>Supplemental route-ordering provenance when integrated into the catalog.</summary>
    RouteOrderingProvenanceRecord? RouteOrderingProvenance { get; }

    /// <summary>History Plaque collection route metadata promoted for future UI use.</summary>
    IReadOnlyList<HistoryPlaqueRouteCollectionRecord> GetHistoryPlaqueRouteCollections();

    /// <summary>History Plaque route stops promoted for future UI use.</summary>
    IReadOnlyList<HistoryPlaqueRouteStopRecord> GetHistoryPlaqueRouteStops();

    /// <summary>Directed prerequisite edges between accolade badges.</summary>
    IReadOnlyList<BadgeAccoladeRequirementRecord> GetBadgeAccoladeRequirements();

    /// <summary>Promoted prerequisite edges for one accolade badge id.</summary>
    IReadOnlyList<BadgeAccoladeRequirementRecord> GetAccoladeRequirements(string accoladeBadgeId);

    /// <summary>
    /// Searches catalog items. Defaults to current Homecoming content only for Enhancements.
    /// </summary>
    IReadOnlyList<ItemReferenceSearchResult> Search(
        string searchText,
        ReferenceItemFamily? family = null,
        int maximumResults = 50,
        ReferenceCatalogQueryScope queryScope = ReferenceCatalogQueryScope.CurrentHomecoming);
}
