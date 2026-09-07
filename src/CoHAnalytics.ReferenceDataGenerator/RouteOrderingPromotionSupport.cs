using CoHAnalytics.ReferenceData;

namespace CoHAnalytics.ReferenceDataGenerator;

internal static class RouteOrderingPromotionSupport
{
    internal static RouteOrderingPromotionResult Apply(
        ItemReferenceCatalogDocument catalog,
        RouteOrderingPackage package)
    {
        var locationIndex = catalog.BadgeLocations
            .Where(location => !string.IsNullOrWhiteSpace(location.BadgeCatalogItemId)
                && !string.IsNullOrWhiteSpace(location.ZoneId)
                && location.LocationIndex.HasValue)
            .ToDictionary(
                location => CreateLocationKey(
                    location.BadgeCatalogItemId!,
                    location.ZoneId!,
                    location.LocationIndex!.Value),
                location => location,
                StringComparer.Ordinal);
        var badgeById = catalog.Badges
            .Where(badge => !string.IsNullOrWhiteSpace(badge.CatalogItemId))
            .ToDictionary(badge => badge.CatalogItemId!, StringComparer.Ordinal);

        var explorationStopsApplied = 0;
        var explorationZonesApplied = 0;
        foreach (var zoneRoute in package.Exploration)
        {
            if (string.IsNullOrWhiteSpace(zoneRoute.ZoneId) || zoneRoute.Stops is not { Count: > 0 })
            {
                continue;
            }

            var zoneApplied = false;
            foreach (var stop in zoneRoute.Stops)
            {
                if (!TryResolveExplorationLocation(
                        badgeById,
                        locationIndex,
                        zoneRoute.ZoneId,
                        stop,
                        out var location))
                {
                    continue;
                }

                location.ExplorationRouteOrder = stop.RouteOrder;
                location.RouteSourceProject = stop.SourceProject;
                location.RouteSourceVersion = stop.SourceVersion;
                location.RouteSourceUrl = stop.SourceUrl;
                location.RouteMappingConfidence = stop.MappingConfidence;
                explorationStopsApplied++;
                zoneApplied = true;
            }

            if (zoneApplied)
            {
                explorationZonesApplied++;
            }
        }

        catalog.RouteOrderingProvenance = new RouteOrderingProvenanceRecordDocument
        {
            SourceName = package.Source.Name,
            Author = package.Source.Author,
            Version = package.Source.Version,
            MapsThreadUrl = package.Source.MapsThreadUrl,
            PopmenuThreadUrl = package.Source.PopmenuThreadUrl,
            OrderingSemantics = package.Source.OrderingSemantics,
            Retrieved = package.Source.Retrieved
        };

        catalog.HistoryPlaqueRouteCollections = [];
        catalog.HistoryPlaqueRouteStops = [];
        var plaqueStopsApplied = 0;
        foreach (var collection in package.HistoryPlaques)
        {
            if (string.IsNullOrWhiteSpace(collection.Collection)
                || string.IsNullOrWhiteSpace(collection.CompletionBadgeId))
            {
                continue;
            }

            catalog.HistoryPlaqueRouteCollections.Add(new HistoryPlaqueRouteCollectionRecordDocument
            {
                CollectionName = collection.Collection,
                CompletionBadgeId = collection.CompletionBadgeId,
                PublishedCollectionOrderAvailable = collection.PublishedCollectionOrderAvailable,
                OrderingStatus = collection.OrderingStatus,
                OrderingNotes = collection.OrderingNotes
            });

            foreach (var stop in collection.Stops ?? [])
            {
                if (string.IsNullOrWhiteSpace(stop.PlaqueIdentity)
                    || !RouteOrderingReferenceSupport.TryParseLocationIdentity(
                        stop.PlaqueIdentity,
                        out _,
                        out var locationIndexValue))
                {
                    continue;
                }

                catalog.HistoryPlaqueRouteStops.Add(new HistoryPlaqueRouteStopRecordDocument
                {
                    CollectionName = collection.Collection,
                    InventoryOrder = stop.InventoryOrder,
                    RouteOrder = stop.RouteOrder,
                    CompletionBadgeId = stop.CompletionBadgeId ?? collection.CompletionBadgeId,
                    ZoneId = stop.ZoneId,
                    LocationIndex = locationIndexValue,
                    PlaqueName = stop.PlaqueName,
                    SourceZoneRouteOrder = stop.SourceZoneRouteOrder,
                    RouteMappingConfidence = stop.MappingConfidence,
                    RouteSourceProject = stop.SourceProject,
                    RouteSourceVersion = stop.SourceVersion,
                    RouteSourceUrl = stop.SourceUrl
                });
                plaqueStopsApplied++;
            }
        }

        return new RouteOrderingPromotionResult(
            explorationZonesApplied,
            explorationStopsApplied,
            catalog.HistoryPlaqueRouteCollections.Count,
            plaqueStopsApplied);
    }

    private static bool TryResolveExplorationLocation(
        IReadOnlyDictionary<string, BadgeReferenceRecordDocument> badgeById,
        IReadOnlyDictionary<string, BadgeLocationReferenceRecordDocument> locationIndex,
        string zoneId,
        RouteOrderingExplorationStopDocument stop,
        out BadgeLocationReferenceRecordDocument location)
    {
        if (TryResolveLocationKey(zoneId, stop, out var locationKey)
            && locationIndex.TryGetValue(locationKey, out location!))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(stop.HomecomingSourceId))
        {
            var sourceMatch = badgeById.Values
                .FirstOrDefault(badge => string.Equals(
                    badge.HomecomingSourceId,
                    stop.HomecomingSourceId,
                    StringComparison.Ordinal));
            if (sourceMatch is not null
                && locationIndex.TryGetValue(
                    CreateLocationKey(sourceMatch.CatalogItemId!, zoneId, stop.LocationIndex),
                    out location!))
            {
                return true;
            }
        }

        if (string.IsNullOrWhiteSpace(stop.BadgeName))
        {
            location = null!;
            return false;
        }

        var nameMatches = locationIndex.Values
            .Where(candidate => string.Equals(candidate.ZoneId, zoneId, StringComparison.Ordinal)
                && candidate.LocationIndex == stop.LocationIndex
                && badgeById.TryGetValue(candidate.BadgeCatalogItemId!, out var badge)
                && string.Equals(
                    badge.ReferenceKind,
                    nameof(ReferenceBadgeKind.ExplorationBadge),
                    StringComparison.Ordinal)
                && BadgeNameMatchesStop(badge, stop.BadgeName))
            .ToArray();

        if (nameMatches.Length == 1)
        {
            location = nameMatches[0];
            return true;
        }

        location = null!;
        return false;
    }

    private static bool BadgeNameMatchesStop(BadgeReferenceRecordDocument badge, string badgeName)
    {
        if (string.Equals(badge.HeroName, badgeName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(badge.VillainName, badgeName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(badge.HeroName) || string.IsNullOrWhiteSpace(badge.VillainName))
        {
            return false;
        }

        var combined = string.Equals(badge.HeroName, badge.VillainName, StringComparison.Ordinal)
            ? badge.HeroName
            : $"{badge.HeroName} / {badge.VillainName}";
        return string.Equals(combined, badgeName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryResolveLocationKey(
        string zoneId,
        RouteOrderingExplorationStopDocument stop,
        out string locationKey)
    {
        locationKey = string.Empty;
        string badgeId;
        int locationIndexValue;

        if (!string.IsNullOrWhiteSpace(stop.LocationIdentity)
            && RouteOrderingReferenceSupport.TryParseLocationIdentity(
                stop.LocationIdentity,
                out badgeId,
                out locationIndexValue))
        {
            locationKey = CreateLocationKey(badgeId, zoneId, locationIndexValue);
            return true;
        }

        if (string.IsNullOrWhiteSpace(stop.BadgeId))
        {
            return false;
        }

        badgeId = stop.BadgeId;
        locationIndexValue = stop.LocationIndex;
        locationKey = CreateLocationKey(badgeId, zoneId, locationIndexValue);
        return true;
    }

    private static string CreateLocationKey(string badgeId, string zoneId, int locationIndex) =>
        $"{badgeId}|{zoneId}|{locationIndex}";
}

internal sealed record RouteOrderingPromotionResult(
    int ExplorationZonesApplied,
    int ExplorationStopsApplied,
    int HistoryPlaqueCollectionsApplied,
    int HistoryPlaqueStopsApplied);
