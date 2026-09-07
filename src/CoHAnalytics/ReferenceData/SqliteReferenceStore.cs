namespace CoHAnalytics.ReferenceData;

/// <summary>SQLite-backed item reference catalog loaded from a generated reference database.</summary>
internal sealed class SqliteReferenceStore : IItemReferenceCatalog
{
    private IItemReferenceCatalog _inner;

    public SqliteReferenceStore(string databasePath)
    {
        var result = SqliteReferenceCatalogLoader.Load(databasePath);
        _inner = ItemReferenceCatalog.FromLoadResult(result);
    }

    private IItemReferenceCatalog Current => Volatile.Read(ref _inner);

    public bool IsLoaded => Current.IsLoaded;

    public string? LoadFailureReason => Current.LoadFailureReason;

    public ItemReferenceManifest? Manifest => Current.Manifest;

    public bool TryResolve(
        string rawObservedText,
        out ItemReferenceResolution resolution,
        ReferenceCatalogQueryScope queryScope = ReferenceCatalogQueryScope.CurrentHomecoming) =>
        Current.TryResolve(rawObservedText, out resolution, queryScope);

    public bool TryGetById(string catalogItemId, out ItemReferenceRecord item) =>
        Current.TryGetById(catalogItemId, out item);

    public bool TryGetEnhancementSetById(string catalogItemId, out EnhancementSetReferenceRecord set) =>
        Current.TryGetEnhancementSetById(catalogItemId, out set);

    public IReadOnlyDictionary<string, EnhancementSetReferenceRecord> GetEnhancementSets(
        ReferenceCatalogQueryScope queryScope = ReferenceCatalogQueryScope.CurrentHomecoming) =>
        Current.GetEnhancementSets(queryScope);

    public IReadOnlyList<ItemReferenceRecord> GetEnhancements(
        ReferenceCatalogQueryScope queryScope = ReferenceCatalogQueryScope.CurrentHomecoming) =>
        Current.GetEnhancements(queryScope);

    public IReadOnlyList<ItemReferenceRecord> GetRecipes() =>
        Current.GetRecipes();

    public IReadOnlyList<BadgeReferenceRecord> GetBadges() =>
        Current.GetBadges();

    public bool TryGetBadgeById(string catalogItemId, out BadgeReferenceRecord badge) =>
        Current.TryGetBadgeById(catalogItemId, out badge);

    public bool TryGetBadgeByHomecomingSourceId(string homecomingSourceId, out BadgeReferenceRecord badge) =>
        Current.TryGetBadgeByHomecomingSourceId(homecomingSourceId, out badge);

    public IReadOnlyDictionary<string, ZoneReferenceRecord> GetZones() =>
        Current.GetZones();

    public IReadOnlyList<BadgeLocationReferenceRecord> GetBadgeLocations(string badgeCatalogItemId) =>
        Current.GetBadgeLocations(badgeCatalogItemId);

    public RouteOrderingProvenanceRecord? RouteOrderingProvenance => Current.RouteOrderingProvenance;

    public IReadOnlyList<HistoryPlaqueRouteCollectionRecord> GetHistoryPlaqueRouteCollections() =>
        Current.GetHistoryPlaqueRouteCollections();

    public IReadOnlyList<HistoryPlaqueRouteStopRecord> GetHistoryPlaqueRouteStops() =>
        Current.GetHistoryPlaqueRouteStops();

    public IReadOnlyList<BadgeAccoladeRequirementRecord> GetBadgeAccoladeRequirements() =>
        Current.GetBadgeAccoladeRequirements();

    public IReadOnlyList<BadgeAccoladeRequirementRecord> GetAccoladeRequirements(string accoladeBadgeId) =>
        Current.GetAccoladeRequirements(accoladeBadgeId);

    public IReadOnlyList<ItemReferenceSearchResult> Search(
        string searchText,
        ReferenceItemFamily? family = null,
        int maximumResults = 50,
        ReferenceCatalogQueryScope queryScope = ReferenceCatalogQueryScope.CurrentHomecoming) =>
        Current.Search(searchText, family, maximumResults, queryScope);

    internal bool TryReloadFromDatabase(string databasePath, out string? failureReason)
    {
        var candidate = ItemReferenceCatalog.FromLoadResult(SqliteReferenceCatalogLoader.Load(databasePath));
        if (!candidate.IsLoaded)
        {
            failureReason = candidate.LoadFailureReason ?? "Catalog reload failed.";
            return false;
        }

        Interlocked.Exchange(ref _inner, candidate);
        failureReason = null;
        return true;
    }
}
