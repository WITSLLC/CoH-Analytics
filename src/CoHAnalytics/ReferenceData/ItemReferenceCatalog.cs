namespace CoHAnalytics.ReferenceData;

internal sealed class ItemReferenceCatalog : IItemReferenceCatalog
{
    private readonly Dictionary<string, ItemReferenceRecord> _items;
    private readonly Dictionary<string, EnhancementSetReferenceRecord> _enhancementSets;
    private readonly Dictionary<string, BadgeReferenceRecord> _badges;
    private readonly Dictionary<string, ZoneReferenceRecord> _zones;
    private readonly Dictionary<string, List<BadgeLocationReferenceRecord>> _badgeLocationsByBadgeId;
    private readonly RouteOrderingProvenanceRecord? _routeOrderingProvenance;
    private readonly IReadOnlyList<HistoryPlaqueRouteCollectionRecord> _historyPlaqueRouteCollections;
    private readonly IReadOnlyList<HistoryPlaqueRouteStopRecord> _historyPlaqueRouteStops;
    private readonly IReadOnlyList<BadgeAccoladeRequirementRecord> _badgeAccoladeRequirements;
    private readonly Dictionary<string, List<BadgeAccoladeRequirementRecord>> _accoladeRequirementsByAccoladeId;
    private readonly Dictionary<string, string> _badgeIdsByHomecomingSourceId;
    private readonly Dictionary<string, (string CatalogItemId, string MatchedAliasText)> _aliasIndex;
    private readonly Dictionary<string, (string CatalogItemId, string MatchedAliasText)> _allAliasIndex;

    private ItemReferenceCatalog(
        ItemReferenceManifest manifest,
        Dictionary<string, ItemReferenceRecord> items,
        Dictionary<string, EnhancementSetReferenceRecord> enhancementSets,
        Dictionary<string, BadgeReferenceRecord> badges,
        Dictionary<string, ZoneReferenceRecord> zones,
        IReadOnlyList<BadgeLocationReferenceRecord> badgeLocations,
        RouteOrderingProvenanceRecord? routeOrderingProvenance,
        IReadOnlyList<HistoryPlaqueRouteCollectionRecord> historyPlaqueRouteCollections,
        IReadOnlyList<HistoryPlaqueRouteStopRecord> historyPlaqueRouteStops,
        IReadOnlyList<BadgeAccoladeRequirementRecord> badgeAccoladeRequirements,
        Dictionary<string, (string CatalogItemId, string MatchedAliasText)> aliasIndex,
        Dictionary<string, (string CatalogItemId, string MatchedAliasText)> allAliasIndex)
    {
        Manifest = manifest;
        _items = items;
        _enhancementSets = enhancementSets;
        _badges = badges;
        _zones = zones;
        _badgeLocationsByBadgeId = badgeLocations
            .GroupBy(location => location.BadgeCatalogItemId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(location => location.LocationIndex).ToList(),
                StringComparer.Ordinal);
        _routeOrderingProvenance = routeOrderingProvenance;
        _historyPlaqueRouteCollections = historyPlaqueRouteCollections;
        _historyPlaqueRouteStops = historyPlaqueRouteStops;
        _badgeAccoladeRequirements = badgeAccoladeRequirements;
        _accoladeRequirementsByAccoladeId = badgeAccoladeRequirements
            .GroupBy(requirement => requirement.AccoladeBadgeId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderBy(requirement => requirement.PrerequisiteIndex)
                    .ThenBy(requirement => requirement.PrerequisiteBadgeId, StringComparer.Ordinal)
                    .ToList(),
                StringComparer.Ordinal);
        _badgeIdsByHomecomingSourceId = badges.Values.ToDictionary(
            badge => badge.HomecomingSourceId,
            badge => badge.CatalogItemId,
            StringComparer.Ordinal);
        _aliasIndex = aliasIndex;
        _allAliasIndex = allAliasIndex;
        IsLoaded = true;
        LoadFailureReason = null;
    }

    public bool IsLoaded { get; }

    public string? LoadFailureReason { get; }

    public ItemReferenceManifest? Manifest { get; }

    public IReadOnlyDictionary<string, EnhancementSetReferenceRecord> EnhancementSets =>
        GetEnhancementSets(ReferenceCatalogQueryScope.CurrentHomecoming);

    internal IReadOnlyDictionary<string, ItemReferenceRecord> DebugItems => _items;

    public bool TryResolve(
        string rawObservedText,
        out ItemReferenceResolution resolution,
        ReferenceCatalogQueryScope queryScope = ReferenceCatalogQueryScope.CurrentHomecoming)
    {
        resolution = null!;

        if (!IsLoaded)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(rawObservedText))
        {
            return false;
        }

        var lookupKey = ItemReferenceLookup.NormalizeLookupKey(rawObservedText);
        if (string.IsNullOrEmpty(lookupKey))
        {
            return false;
        }

        var aliasIndex = queryScope == ReferenceCatalogQueryScope.AllHomecomingIdentities
            ? _allAliasIndex
            : _aliasIndex;

        if (!aliasIndex.TryGetValue(lookupKey, out var aliasMatch))
        {
            return false;
        }

        if (!_items.TryGetValue(aliasMatch.CatalogItemId, out var item))
        {
            return false;
        }

        resolution = new ItemReferenceResolution
        {
            Item = item,
            CatalogVersion = Manifest!.CatalogVersion,
            MatchedAliasText = aliasMatch.MatchedAliasText
        };

        return true;
    }

    public bool TryGetById(string catalogItemId, out ItemReferenceRecord item) =>
        _items.TryGetValue(catalogItemId, out item!);

    public bool TryGetEnhancementSetById(string catalogItemId, out EnhancementSetReferenceRecord set) =>
        _enhancementSets.TryGetValue(catalogItemId, out set!);

    public IReadOnlyDictionary<string, EnhancementSetReferenceRecord> GetEnhancementSets(
        ReferenceCatalogQueryScope queryScope = ReferenceCatalogQueryScope.CurrentHomecoming)
    {
        if (queryScope == ReferenceCatalogQueryScope.AllHomecomingIdentities)
        {
            return _enhancementSets;
        }

        return _enhancementSets
            .Where(pair => ReferenceServerAvailabilitySupport.IsCurrentHomecoming(pair.Value.ServerAvailability))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    }

    public IReadOnlyList<ItemReferenceRecord> GetEnhancements(
        ReferenceCatalogQueryScope queryScope = ReferenceCatalogQueryScope.CurrentHomecoming)
    {
        if (!IsLoaded)
        {
            return [];
        }

        return _items.Values
            .Where(item => item.Family == ReferenceItemFamily.Enhancement)
            .Where(item => ReferenceServerAvailabilitySupport.MatchesQueryScope(item.ServerAvailability, queryScope))
            .OrderBy(item => item.CurrentDisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.CatalogItemId, StringComparer.Ordinal)
            .ToArray();
    }

    public IReadOnlyList<ItemReferenceRecord> GetRecipes()
    {
        if (!IsLoaded)
        {
            return [];
        }

        return _items.Values
            .Where(item => item.Family == ReferenceItemFamily.Recipe)
            .OrderBy(item => item.CurrentDisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.CatalogItemId, StringComparer.Ordinal)
            .ToArray();
    }

    public IReadOnlyList<BadgeReferenceRecord> GetBadges()
    {
        if (!IsLoaded)
        {
            return [];
        }

        return _badges.Values
            .OrderBy(badge => badge.HeroName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(badge => badge.CatalogItemId, StringComparer.Ordinal)
            .ToArray();
    }

    public bool TryGetBadgeById(string catalogItemId, out BadgeReferenceRecord badge) =>
        _badges.TryGetValue(catalogItemId, out badge!);

    public bool TryGetBadgeByHomecomingSourceId(string homecomingSourceId, out BadgeReferenceRecord badge)
    {
        if (!_badgeIdsByHomecomingSourceId.TryGetValue(homecomingSourceId, out var catalogItemId)
            || !_badges.TryGetValue(catalogItemId, out var found))
        {
            badge = null!;
            return false;
        }

        badge = found;
        return true;
    }

    public IReadOnlyDictionary<string, ZoneReferenceRecord> GetZones() => _zones;

    public IReadOnlyList<BadgeLocationReferenceRecord> GetBadgeLocations(string badgeCatalogItemId)
    {
        if (string.IsNullOrWhiteSpace(badgeCatalogItemId))
        {
            return [];
        }

        return _badgeLocationsByBadgeId.TryGetValue(badgeCatalogItemId, out var locations)
            ? locations
            : [];
    }

    public RouteOrderingProvenanceRecord? RouteOrderingProvenance => _routeOrderingProvenance;

    public IReadOnlyList<HistoryPlaqueRouteCollectionRecord> GetHistoryPlaqueRouteCollections() =>
        _historyPlaqueRouteCollections;

    public IReadOnlyList<HistoryPlaqueRouteStopRecord> GetHistoryPlaqueRouteStops() =>
        _historyPlaqueRouteStops;

    public IReadOnlyList<BadgeAccoladeRequirementRecord> GetBadgeAccoladeRequirements() =>
        _badgeAccoladeRequirements;

    public IReadOnlyList<BadgeAccoladeRequirementRecord> GetAccoladeRequirements(string accoladeBadgeId)
    {
        if (string.IsNullOrWhiteSpace(accoladeBadgeId))
        {
            return [];
        }

        return _accoladeRequirementsByAccoladeId.TryGetValue(accoladeBadgeId, out var requirements)
            ? requirements
            : [];
    }

    public IReadOnlyList<ItemReferenceSearchResult> Search(
        string searchText,
        ReferenceItemFamily? family = null,
        int maximumResults = 50,
        ReferenceCatalogQueryScope queryScope = ReferenceCatalogQueryScope.CurrentHomecoming)
    {
        if (string.IsNullOrWhiteSpace(searchText) || maximumResults <= 0)
        {
            return [];
        }

        var term = searchText.Trim();
        var normalizedTerm = ItemReferenceLookup.NormalizeLookupKey(term);
        var aliasIndex = queryScope == ReferenceCatalogQueryScope.AllHomecomingIdentities
            ? _allAliasIndex
            : _aliasIndex;
        var aliasesByItem = aliasIndex.Values
            .GroupBy(alias => alias.CatalogItemId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(alias => alias.MatchedAliasText).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                StringComparer.Ordinal);

        return _items.Values
            .Where(item => family is null || item.Family == family)
            .Where(item =>
                item.Family is not ReferenceItemFamily.Enhancement
                || ReferenceServerAvailabilitySupport.MatchesQueryScope(item.ServerAvailability, queryScope))
            .Select(item =>
            {
                aliasesByItem.TryGetValue(item.CatalogItemId, out var aliases);
                aliases ??= [];
                var matchedAlias = aliases.FirstOrDefault(alias =>
                    alias.Contains(term, StringComparison.OrdinalIgnoreCase));
                var score = ScoreSearchMatch(item, aliases, term, normalizedTerm);
                return (Item: item, MatchedAlias: matchedAlias, Score: score);
            })
            .Where(candidate => candidate.Score < int.MaxValue)
            .OrderBy(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Item.CurrentDisplayName, StringComparer.OrdinalIgnoreCase)
            .Take(maximumResults)
            .Select(candidate => new ItemReferenceSearchResult
            {
                CatalogItemId = candidate.Item.CatalogItemId,
                Family = candidate.Item.Family,
                Subtype = candidate.Item.Subtype,
                CurrentDisplayName = candidate.Item.CurrentDisplayName,
                MatchedText = candidate.MatchedAlias,
                EnhancementSetId = candidate.Item.EnhancementSetId,
                EnhancementSetName = candidate.Item.EnhancementSetId is not null
                    && _enhancementSets.TryGetValue(candidate.Item.EnhancementSetId, out var set)
                        ? set.CurrentDisplayName
                        : null,
                Variant = candidate.Item.Variant
            })
            .ToArray();
    }

    private static int ScoreSearchMatch(
        ItemReferenceRecord item,
        IReadOnlyList<string> aliases,
        string term,
        string normalizedTerm)
    {
        if (string.Equals(item.CatalogItemId, term, StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                ItemReferenceLookup.NormalizeLookupKey(item.CurrentDisplayName),
                normalizedTerm,
                StringComparison.Ordinal)
            || aliases.Any(alias => string.Equals(
                ItemReferenceLookup.NormalizeLookupKey(alias),
                normalizedTerm,
                StringComparison.Ordinal)))
        {
            return 0;
        }

        if (item.CatalogItemId.StartsWith(term, StringComparison.OrdinalIgnoreCase)
            || item.CurrentDisplayName.StartsWith(term, StringComparison.OrdinalIgnoreCase)
            || aliases.Any(alias => alias.StartsWith(term, StringComparison.OrdinalIgnoreCase)))
        {
            return 1;
        }

        if (item.CatalogItemId.Contains(term, StringComparison.OrdinalIgnoreCase)
            || item.CurrentDisplayName.Contains(term, StringComparison.OrdinalIgnoreCase)
            || aliases.Any(alias => alias.Contains(term, StringComparison.OrdinalIgnoreCase)))
        {
            return 2;
        }

        return int.MaxValue;
    }

    internal static IItemReferenceCatalog FromLoadResult(ItemReferenceCatalogLoadResult result)
    {
        if (!result.Succeeded ||
            result.Manifest is null ||
            result.Items is null ||
            result.EnhancementSets is null ||
            result.Badges is null ||
            result.AliasIndex is null ||
            result.AllAliasIndex is null)
        {
            return new FailedItemReferenceCatalog(result.FailureReason ?? "Catalog load failed.");
        }

        return new ItemReferenceCatalog(
            result.Manifest,
            result.Items,
            result.EnhancementSets,
            result.Badges,
            result.Zones ?? new Dictionary<string, ZoneReferenceRecord>(StringComparer.Ordinal),
            result.BadgeLocations ?? [],
            result.RouteOrderingProvenance,
            result.HistoryPlaqueRouteCollections ?? [],
            result.HistoryPlaqueRouteStops ?? [],
            result.BadgeAccoladeRequirements ?? [],
            result.AliasIndex,
            result.AllAliasIndex);
    }
}

internal sealed class FailedItemReferenceCatalog : IItemReferenceCatalog
{
    public FailedItemReferenceCatalog(string loadFailureReason)
    {
        LoadFailureReason = loadFailureReason;
    }

    public bool IsLoaded => false;

    public string? LoadFailureReason { get; }

    public ItemReferenceManifest? Manifest => null;

    public bool TryResolve(
        string rawObservedText,
        out ItemReferenceResolution resolution,
        ReferenceCatalogQueryScope queryScope = ReferenceCatalogQueryScope.CurrentHomecoming)
    {
        resolution = null!;
        return false;
    }

    public bool TryGetById(string catalogItemId, out ItemReferenceRecord item)
    {
        item = null!;
        return false;
    }

    public bool TryGetEnhancementSetById(string catalogItemId, out EnhancementSetReferenceRecord set)
    {
        set = null!;
        return false;
    }

    public IReadOnlyDictionary<string, EnhancementSetReferenceRecord> GetEnhancementSets(
        ReferenceCatalogQueryScope queryScope = ReferenceCatalogQueryScope.CurrentHomecoming) =>
        new Dictionary<string, EnhancementSetReferenceRecord>(StringComparer.Ordinal);

    public IReadOnlyList<ItemReferenceRecord> GetEnhancements(
        ReferenceCatalogQueryScope queryScope = ReferenceCatalogQueryScope.CurrentHomecoming) => [];

    public IReadOnlyList<ItemReferenceRecord> GetRecipes() => [];

    public IReadOnlyList<BadgeReferenceRecord> GetBadges() => [];

    public bool TryGetBadgeById(string catalogItemId, out BadgeReferenceRecord badge)
    {
        badge = null!;
        return false;
    }

    public bool TryGetBadgeByHomecomingSourceId(string homecomingSourceId, out BadgeReferenceRecord badge)
    {
        badge = null!;
        return false;
    }

    public IReadOnlyDictionary<string, ZoneReferenceRecord> GetZones() =>
        new Dictionary<string, ZoneReferenceRecord>(StringComparer.Ordinal);

    public IReadOnlyList<BadgeLocationReferenceRecord> GetBadgeLocations(string badgeCatalogItemId) => [];

    public RouteOrderingProvenanceRecord? RouteOrderingProvenance => null;

    public IReadOnlyList<HistoryPlaqueRouteCollectionRecord> GetHistoryPlaqueRouteCollections() => [];

    public IReadOnlyList<HistoryPlaqueRouteStopRecord> GetHistoryPlaqueRouteStops() => [];

    public IReadOnlyList<BadgeAccoladeRequirementRecord> GetBadgeAccoladeRequirements() => [];

    public IReadOnlyList<BadgeAccoladeRequirementRecord> GetAccoladeRequirements(string accoladeBadgeId) => [];

    public IReadOnlyList<ItemReferenceSearchResult> Search(
        string searchText,
        ReferenceItemFamily? family = null,
        int maximumResults = 50,
        ReferenceCatalogQueryScope queryScope = ReferenceCatalogQueryScope.CurrentHomecoming) => [];
}
