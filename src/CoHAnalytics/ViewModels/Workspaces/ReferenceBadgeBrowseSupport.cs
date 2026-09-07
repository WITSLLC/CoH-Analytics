using System.Text.RegularExpressions;
using System.Windows.Media;
using CoHAnalytics.Homecoming;
using CoHAnalytics.ReferenceData;

namespace CoHAnalytics.ViewModels.Workspaces;

public static class ReferenceBadgeBrowseSupport
{
    public const string ExplorationRootNodeKey = "branch:badge-exploration";

    public const string HistoryPlaquesRootNodeKey = "branch:badge-history-plaques";

    public const string HistoryPlaquesByBadgeNodeKey = "branch:badge-history-plaques-by-badge";

    public const string HistoryPlaquesByZoneNodeKey = "branch:badge-history-plaques-by-zone";

    public const string AccoladesRootNodeKey = "branch:badge-accolades";

    public const string AccoladeCategoryNodeKeyPrefix = "badge-accolade-category:";

    private const string PublishedSingleZoneOrderStatus = "PublishedSingleZoneOrder";

    public static ReferenceEnhancementBrowseTree BuildBrowseTree(
        IItemReferenceCatalog catalog,
        IReadOnlySet<string>? acquiredBadgeIds = null)
    {
        acquiredBadgeIds ??= new HashSet<string>(StringComparer.Ordinal);

        var explorationRoot = BuildExplorationRoot(catalog, acquiredBadgeIds);
        var historyPlaquesRoot = BuildHistoryPlaquesRoot(catalog, acquiredBadgeIds);
        var accoladesRoot = BuildAccoladesRoot(catalog, acquiredBadgeIds);
        var nodesByKey = new Dictionary<string, ReferenceEnhancementBrowseNode>(StringComparer.Ordinal);
        IndexNodes(explorationRoot, nodesByKey);
        IndexNodes(historyPlaquesRoot, nodesByKey);
        IndexNodes(accoladesRoot, nodesByKey);

        return new ReferenceEnhancementBrowseTree
        {
            RootNodes = [explorationRoot, historyPlaquesRoot, accoladesRoot],
            NodesByKey = nodesByKey,
            Census = new ReferenceEnhancementBrowseCensus()
        };
    }

    private static ReferenceEnhancementBrowseNode BuildExplorationRoot(
        IItemReferenceCatalog catalog,
        IReadOnlySet<string> acquiredBadgeIds)
    {
        var badgesByZone = BuildExplorationBadgesByZone(catalog);
        var zoneNodes = new List<ReferenceEnhancementBrowseNode>();

        foreach (var zoneEntry in ResolveZoneEntries(catalog, badgesByZone))
        {
            var memberBadgeIds = badgesByZone.TryGetValue(zoneEntry.ZoneId, out var members)
                ? members
                : new HashSet<string>(StringComparer.Ordinal);
            if (memberBadgeIds.Count == 0)
            {
                continue;
            }

            var completionBadgeIds = ResolveZoneCompletionBadgeIds(
                catalog,
                zoneEntry.ZoneRecord,
                memberBadgeIds);
            var completionSet = completionBadgeIds.ToHashSet(StringComparer.Ordinal);
            var childNodes = new List<ReferenceEnhancementBrowseNode>();

            foreach (var completionBadgeId in completionBadgeIds)
            {
                if (!catalog.TryGetBadgeById(completionBadgeId, out var completionBadge))
                {
                    continue;
                }

                childNodes.Add(CreateBadgeNode(
                    catalog,
                    completionBadge,
                    zoneEntry.ZoneId,
                    isZoneCompletion: true,
                    acquiredBadgeIds));
            }

            foreach (var memberBadge in RouteOrderingReferenceSupport.OrderExplorationMemberBadges(
                         catalog,
                         zoneEntry.ZoneId,
                         memberBadgeIds
                             .Where(id => !completionSet.Contains(id))
                             .Select(id => catalog.TryGetBadgeById(id, out var badge) ? badge : null)
                             .Where(badge => badge is not null)!))
            {
                childNodes.Add(CreateBadgeNode(
                    catalog,
                    memberBadge,
                    zoneEntry.ZoneId,
                    isZoneCompletion: false,
                    acquiredBadgeIds));
            }

            if (childNodes.Count == 0)
            {
                continue;
            }

            zoneNodes.Add(new ReferenceEnhancementBrowseNode
            {
                Kind = ReferenceEnhancementBrowseNodeKind.BadgeZone,
                DisplayName = zoneEntry.DisplayName,
                NodeKey = CreateZoneNodeKey(zoneEntry.ZoneId),
                ParentNodeKey = ExplorationRootNodeKey,
                Children = childNodes
            });
        }

        var rootNode = new ReferenceEnhancementBrowseNode
        {
            Kind = ReferenceEnhancementBrowseNodeKind.BadgeExplorationRoot,
            DisplayName = "Exploration",
            NodeKey = ExplorationRootNodeKey,
            Children = zoneNodes
        };

        return rootNode;
    }

    private static ReferenceEnhancementBrowseNode BuildHistoryPlaquesRoot(
        IItemReferenceCatalog catalog,
        IReadOnlySet<string> acquiredBadgeIds)
    {
        var collectionsByName = catalog.GetHistoryPlaqueRouteCollections()
            .ToDictionary(collection => collection.CollectionName, StringComparer.Ordinal);
        var stopsByCollection = catalog.GetHistoryPlaqueRouteStops()
            .GroupBy(stop => stop.CollectionName, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

        var collectionNodes = catalog.GetHistoryPlaqueRouteCollections()
            .OrderBy(collection => collection.CollectionName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(collection => collection.CollectionName, StringComparer.Ordinal)
            .Select(collection => BuildPlaqueCollectionNode(
                catalog,
                collection,
                stopsByCollection.GetValueOrDefault(collection.CollectionName) ?? [],
                acquiredBadgeIds,
                HistoryPlaquesByBadgeNodeKey))
            .Where(node => node is not null)
            .Cast<ReferenceEnhancementBrowseNode>()
            .ToList();

        var byHistoryBadgeBranch = new ReferenceEnhancementBrowseNode
        {
            Kind = ReferenceEnhancementBrowseNodeKind.BadgeHistoryPlaquesByBadgeBranch,
            DisplayName = "By History Badge",
            NodeKey = HistoryPlaquesByBadgeNodeKey,
            ParentNodeKey = HistoryPlaquesRootNodeKey,
            Children = collectionNodes
        };

        var byZoneBranch = BuildHistoryPlaquesByZoneBranch(
            catalog,
            collectionsByName,
            catalog.GetHistoryPlaqueRouteStops());

        return new ReferenceEnhancementBrowseNode
        {
            Kind = ReferenceEnhancementBrowseNodeKind.BadgeHistoryPlaquesRoot,
            DisplayName = "History Plaques",
            NodeKey = HistoryPlaquesRootNodeKey,
            Children = [byHistoryBadgeBranch, byZoneBranch]
        };
    }

    private static ReferenceEnhancementBrowseNode BuildHistoryPlaquesByZoneBranch(
        IItemReferenceCatalog catalog,
        IReadOnlyDictionary<string, HistoryPlaqueRouteCollectionRecord> collectionsByName,
        IReadOnlyList<HistoryPlaqueRouteStopRecord> stops)
    {
        var stopsByZone = stops
            .GroupBy(stop => stop.ZoneId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

        var zoneNodes = stopsByZone
            .Select(entry => (ZoneId: entry.Key, DisplayName: ResolveZoneDisplayName(catalog, entry.Key)))
            .OrderBy(entry => entry.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.ZoneId, StringComparer.Ordinal)
            .Select(entry =>
            {
                var zoneNodeKey = CreateHistoryPlaqueZoneNodeKey(entry.ZoneId);
                var plaqueNodes = OrderPlaquesForZoneView(
                        catalog,
                        collectionsByName,
                        stopsByZone[entry.ZoneId])
                    .Select(stop =>
                    {
                        var collection = collectionsByName.GetValueOrDefault(stop.CollectionName);
                        if (collection is null)
                        {
                            return null;
                        }

                        return CreatePlaqueNode(
                            catalog,
                            collection,
                            stop,
                            zoneNodeKey,
                            CreateZonePlaqueDisplayName(catalog, collection, stop));
                    })
                    .Where(node => node is not null)
                    .Cast<ReferenceEnhancementBrowseNode>()
                    .ToList();

                if (plaqueNodes.Count == 0)
                {
                    return null;
                }

                return new ReferenceEnhancementBrowseNode
                {
                    Kind = ReferenceEnhancementBrowseNodeKind.BadgeHistoryPlaqueZone,
                    DisplayName = entry.DisplayName,
                    NodeKey = zoneNodeKey,
                    ParentNodeKey = HistoryPlaquesByZoneNodeKey,
                    PlaqueZoneId = entry.ZoneId,
                    Children = plaqueNodes
                };
            })
            .Where(node => node is not null)
            .Cast<ReferenceEnhancementBrowseNode>()
            .ToList();

        return new ReferenceEnhancementBrowseNode
        {
            Kind = ReferenceEnhancementBrowseNodeKind.BadgeHistoryPlaquesByZoneBranch,
            DisplayName = "By Zone",
            NodeKey = HistoryPlaquesByZoneNodeKey,
            ParentNodeKey = HistoryPlaquesRootNodeKey,
            Children = zoneNodes
        };
    }

    private static ReferenceEnhancementBrowseNode BuildAccoladesRoot(
        IItemReferenceCatalog catalog,
        IReadOnlySet<string> acquiredBadgeIds)
    {
        var categoryReference = AccoladeCategoryReferenceSupport.LoadEmbeddedProduction();
        var accoladesByCategory = new Dictionary<string, List<BadgeReferenceRecord>>(StringComparer.Ordinal);

        foreach (var accolade in catalog.GetBadges()
                     .Where(badge => badge.ReferenceKind == ReferenceBadgeKind.Accolade))
        {
            var categoryId = categoryReference.ResolveCategoryId(accolade.CatalogItemId);
            if (!accoladesByCategory.TryGetValue(categoryId, out var members))
            {
                members = [];
                accoladesByCategory[categoryId] = members;
            }

            members.Add(accolade);
        }

        var categoryNodes = new List<ReferenceEnhancementBrowseNode>();
        foreach (var category in categoryReference.Categories)
        {
            if (!accoladesByCategory.TryGetValue(category.Id, out var members)
                || members.Count == 0)
            {
                continue;
            }

            var categoryNodeKey = CreateAccoladeCategoryNodeKey(category.Id);
            categoryNodes.Add(new ReferenceEnhancementBrowseNode
            {
                Kind = ReferenceEnhancementBrowseNodeKind.BadgeAccoladeCategory,
                DisplayName = category.DisplayName,
                NodeKey = categoryNodeKey,
                ParentNodeKey = AccoladesRootNodeKey,
                CategoryKey = category.Id,
                Children = members
                    .OrderBy(badge => badge.HeroName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(badge => badge.CatalogItemId, StringComparer.Ordinal)
                    .Select(badge => CreateAccoladeBrowseNode(
                        catalog,
                        badge,
                        acquiredBadgeIds,
                        categoryNodeKey))
                    .ToList()
            });
        }

        return new ReferenceEnhancementBrowseNode
        {
            Kind = ReferenceEnhancementBrowseNodeKind.BadgeAccoladesRoot,
            DisplayName = "Accolades",
            NodeKey = AccoladesRootNodeKey,
            Children = categoryNodes
        };
    }

    private static ReferenceEnhancementBrowseNode CreateAccoladeBrowseNode(
        IItemReferenceCatalog catalog,
        BadgeReferenceRecord badge,
        IReadOnlySet<string> acquiredBadgeIds,
        string parentNodeKey) =>
        new()
        {
            Kind = ReferenceEnhancementBrowseNodeKind.BadgeAccolade,
            DisplayName = badge.HeroName,
            NodeKey = CreateAccoladeNodeKey(badge.CatalogItemId),
            ParentNodeKey = parentNodeKey,
            BadgeId = badge.CatalogItemId,
            IconIdentity = badge.HeroIcon,
            IsAcquired = acquiredBadgeIds.Contains(badge.CatalogItemId)
        };

    private static ReferenceEnhancementBrowseNode? BuildPlaqueCollectionNode(
        IItemReferenceCatalog catalog,
        HistoryPlaqueRouteCollectionRecord collection,
        IReadOnlyList<HistoryPlaqueRouteStopRecord> stops,
        IReadOnlySet<string> acquiredBadgeIds,
        string parentNodeKey)
    {
        if (stops.Count == 0
            || !catalog.TryGetBadgeById(collection.CompletionBadgeId, out var completionBadge))
        {
            return null;
        }

        var collectionNodeKey = CreatePlaqueCollectionNodeKey(collection.CollectionName);
        var childNodes = new List<ReferenceEnhancementBrowseNode>
        {
            CreateCollectionCompletionBadgeNode(
                catalog,
                collection.CollectionName,
                completionBadge,
                collectionNodeKey,
                acquiredBadgeIds)
        };

        foreach (var plaqueNode in BuildOrderedPlaqueNodes(catalog, collection, stops, collectionNodeKey))
        {
            childNodes.Add(plaqueNode);
        }

        return new ReferenceEnhancementBrowseNode
        {
            Kind = ReferenceEnhancementBrowseNodeKind.BadgePlaqueCollection,
            DisplayName = collection.CollectionName,
            NodeKey = collectionNodeKey,
            ParentNodeKey = parentNodeKey,
            PlaqueCollectionName = collection.CollectionName,
            Children = childNodes
        };
    }

    private static IEnumerable<HistoryPlaqueRouteStopRecord> OrderPlaquesForZoneView(
        IItemReferenceCatalog catalog,
        IReadOnlyDictionary<string, HistoryPlaqueRouteCollectionRecord> collectionsByName,
        IReadOnlyList<HistoryPlaqueRouteStopRecord> stops) =>
        stops
            .OrderBy(stop => ResolveHistoryPlaqueZoneRouteOrder(catalog, stop) ?? int.MaxValue)
            .ThenBy(stop => stop.SourceZoneRouteOrder ?? int.MaxValue)
            .ThenBy(stop => stop.RouteOrder ?? int.MaxValue)
            .ThenBy(
                stop => collectionsByName.TryGetValue(stop.CollectionName, out var collection)
                    ? CreateZonePlaqueDisplayName(catalog, collection, stop)
                    : stop.CollectionName,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(stop => stop.LocationIndex, Comparer<int>.Default);

    private static IEnumerable<ReferenceEnhancementBrowseNode> BuildOrderedPlaqueNodes(
        IItemReferenceCatalog catalog,
        HistoryPlaqueRouteCollectionRecord collection,
        IReadOnlyList<HistoryPlaqueRouteStopRecord> stops,
        string collectionNodeKey)
    {
        var zoneIds = stops
            .Select(stop => stop.ZoneId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var useZoneSegments = zoneIds.Length > 1;
        var usePublishedCollectionOrder = collection.PublishedCollectionOrderAvailable
            && string.Equals(collection.OrderingStatus, PublishedSingleZoneOrderStatus, StringComparison.Ordinal);

        if (usePublishedCollectionOrder)
        {
            foreach (var stop in OrderPlaquesForPublishedSingleZone(stops))
            {
                yield return CreatePlaqueNode(
                    catalog,
                    collection,
                    stop,
                    collectionNodeKey);
            }

            yield break;
        }

        foreach (var zone in zoneIds
                     .Select(zoneId => (ZoneId: zoneId, DisplayName: ResolveZoneDisplayName(catalog, zoneId)))
                     .OrderBy(entry => entry.DisplayName, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(entry => entry.ZoneId, StringComparer.Ordinal))
        {
            var zoneStops = stops
                .Where(stop => string.Equals(stop.ZoneId, zone.ZoneId, StringComparison.Ordinal))
                .ToArray();
            var orderedZoneStops = OrderPlaquesWithinZoneSegment(zoneStops).ToArray();
            if (orderedZoneStops.Length == 0)
            {
                continue;
            }

            if (useZoneSegments)
            {
                var zoneNodeKey = CreatePlaqueZoneSegmentNodeKey(collection.CollectionName, zone.ZoneId);
                yield return new ReferenceEnhancementBrowseNode
                {
                    Kind = ReferenceEnhancementBrowseNodeKind.BadgePlaqueZoneSegment,
                    DisplayName = zone.DisplayName,
                    NodeKey = zoneNodeKey,
                    ParentNodeKey = collectionNodeKey,
                    PlaqueCollectionName = collection.CollectionName,
                    PlaqueZoneId = zone.ZoneId,
                    Children = orderedZoneStops
                        .Select(stop => CreatePlaqueNode(catalog, collection, stop, zoneNodeKey))
                        .ToArray()
                };
            }
            else
            {
                foreach (var stop in orderedZoneStops)
                {
                    yield return CreatePlaqueNode(
                        catalog,
                        collection,
                        stop,
                        collectionNodeKey);
                }
            }
        }
    }

    private static IEnumerable<HistoryPlaqueRouteStopRecord> OrderPlaquesForPublishedSingleZone(
        IEnumerable<HistoryPlaqueRouteStopRecord> stops) =>
        stops
            .OrderBy(stop => stop.RouteOrder ?? int.MaxValue)
            .ThenBy(stop => stop.PlaqueName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(stop => stop.LocationIndex, Comparer<int>.Default);

    private static IEnumerable<HistoryPlaqueRouteStopRecord> OrderPlaquesWithinZoneSegment(
        IEnumerable<HistoryPlaqueRouteStopRecord> stops) =>
        stops
            .OrderBy(stop => stop.SourceZoneRouteOrder ?? int.MaxValue)
            .ThenBy(stop => stop.RouteOrder ?? int.MaxValue)
            .ThenBy(stop => stop.PlaqueName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(stop => stop.LocationIndex, Comparer<int>.Default);

    private static ReferenceEnhancementBrowseNode CreateCollectionCompletionBadgeNode(
        IItemReferenceCatalog catalog,
        string collectionName,
        BadgeReferenceRecord badge,
        string collectionNodeKey,
        IReadOnlySet<string> acquiredBadgeIds) =>
        new()
        {
            Kind = ReferenceEnhancementBrowseNodeKind.Badge,
            DisplayName = badge.HeroName,
            NodeKey = CreateCollectionCompletionBadgeNodeKey(collectionName, badge.CatalogItemId),
            ParentNodeKey = collectionNodeKey,
            BadgeId = badge.CatalogItemId,
            IconIdentity = badge.HeroIcon,
            PlaqueCollectionName = collectionName,
            IsCollectionCompletionBadge = true,
            IsZoneCompletionBadge = true,
            IsAcquired = acquiredBadgeIds.Contains(badge.CatalogItemId)
        };

    private static ReferenceEnhancementBrowseNode CreatePlaqueNode(
        IItemReferenceCatalog catalog,
        HistoryPlaqueRouteCollectionRecord collection,
        HistoryPlaqueRouteStopRecord stop,
        string parentNodeKey,
        string? displayNameOverride = null)
    {
        var location = ResolvePlaqueLocation(
            catalog,
            collection.CompletionBadgeId,
            stop.ZoneId,
            stop.LocationIndex);
        var displayName = displayNameOverride
            ?? (!string.IsNullOrWhiteSpace(stop.PlaqueName)
                ? stop.PlaqueName
                : location?.TriggerDescription ?? $"Plaque {stop.LocationIndex + 1}");

        return new ReferenceEnhancementBrowseNode
        {
            Kind = ReferenceEnhancementBrowseNodeKind.HistoryPlaque,
            DisplayName = displayName,
            NodeKey = CreatePlaqueNodeKey(collection.CollectionName, stop.ZoneId, stop.LocationIndex),
            ParentNodeKey = parentNodeKey,
            BadgeId = collection.CompletionBadgeId,
            PlaqueCollectionName = collection.CollectionName,
            PlaqueZoneId = stop.ZoneId,
            PlaqueLocationIndex = stop.LocationIndex,
            SecondaryLine = location is null ? null : ResolveDisplayThumbtackCommand(location)
        };
    }

    private static BadgeLocationReferenceRecord? ResolvePlaqueLocation(
        IItemReferenceCatalog catalog,
        string completionBadgeId,
        string zoneId,
        int locationIndex)
    {
        foreach (var location in catalog.GetBadgeLocations(completionBadgeId))
        {
            if (!string.Equals(location.ZoneId, zoneId, StringComparison.Ordinal)
                || location.LocationIndex != locationIndex
                || !string.Equals(location.MarkerType, "HistoryPlaque", StringComparison.Ordinal))
            {
                continue;
            }

            return location;
        }

        return null;
    }

    public static ReferenceEnhancementDetailModel BuildDetail(
        IItemReferenceCatalog catalog,
        ReferenceEnhancementBrowseNode? selectedNode,
        IReadOnlySet<string>? acquiredBadgeIds,
        IInstalledGameAssetProvider? installedGameAssetProvider)
    {
        if (selectedNode?.Kind == ReferenceEnhancementBrowseNodeKind.BadgeExplorationRoot)
        {
            return new ReferenceEnhancementDetailModel
            {
                Kind = ReferenceEnhancementDetailKind.Branch,
                Title = "Exploration",
                Summary = "Select a zone to browse Exploration badges."
            };
        }

        if (selectedNode?.Kind == ReferenceEnhancementBrowseNodeKind.BadgeHistoryPlaquesRoot)
        {
            return new ReferenceEnhancementDetailModel
            {
                Kind = ReferenceEnhancementDetailKind.Branch,
                Title = "History Plaques",
                Summary = "Browse History Plaques by History Badge collection or by physical zone."
            };
        }

        if (selectedNode?.Kind == ReferenceEnhancementBrowseNodeKind.BadgeHistoryPlaquesByBadgeBranch)
        {
            return new ReferenceEnhancementDetailModel
            {
                Kind = ReferenceEnhancementDetailKind.Branch,
                Title = "By History Badge",
                Summary = "Select a History Badge collection to browse completion badges and individual plaques."
            };
        }

        if (selectedNode?.Kind == ReferenceEnhancementBrowseNodeKind.BadgeHistoryPlaquesByZoneBranch)
        {
            return new ReferenceEnhancementDetailModel
            {
                Kind = ReferenceEnhancementDetailKind.Branch,
                Title = "By Zone",
                Summary = "Select a zone to browse every promoted History Plaque located there."
            };
        }

        if (selectedNode?.Kind == ReferenceEnhancementBrowseNodeKind.BadgeHistoryPlaqueZone)
        {
            return new ReferenceEnhancementDetailModel
            {
                Kind = ReferenceEnhancementDetailKind.Category,
                Title = selectedNode.DisplayName,
                Summary = "Select a plaque to view details."
            };
        }

        if (selectedNode?.Kind == ReferenceEnhancementBrowseNodeKind.BadgeAccoladesRoot)
        {
            return new ReferenceEnhancementDetailModel
            {
                Kind = ReferenceEnhancementDetailKind.Branch,
                Title = "Accolades",
                Summary = "Select a category or Accolade to view requirements, rewards, and acquisition state."
            };
        }

        if (selectedNode?.Kind == ReferenceEnhancementBrowseNodeKind.BadgeAccoladeCategory)
        {
            return new ReferenceEnhancementDetailModel
            {
                Kind = ReferenceEnhancementDetailKind.Category,
                Title = selectedNode.DisplayName,
                Summary = "Select an Accolade to view requirements, rewards, and acquisition state."
            };
        }

        if (selectedNode?.Kind == ReferenceEnhancementBrowseNodeKind.BadgeAccolade
            && !string.IsNullOrWhiteSpace(selectedNode.BadgeId)
            && catalog.TryGetBadgeById(selectedNode.BadgeId, out var accoladeBadge))
        {
            return BuildAccoladeDetail(
                catalog,
                accoladeBadge,
                acquiredBadgeIds,
                installedGameAssetProvider);
        }

        if (selectedNode?.Kind == ReferenceEnhancementBrowseNodeKind.BadgePlaqueCollection)
        {
            return new ReferenceEnhancementDetailModel
            {
                Kind = ReferenceEnhancementDetailKind.Category,
                Title = selectedNode.DisplayName,
                Summary = "Select the collection completion badge or an individual plaque."
            };
        }

        if (selectedNode?.Kind == ReferenceEnhancementBrowseNodeKind.BadgePlaqueZoneSegment)
        {
            return new ReferenceEnhancementDetailModel
            {
                Kind = ReferenceEnhancementDetailKind.Category,
                Title = selectedNode.DisplayName,
                Summary = "Select a plaque to view details."
            };
        }

        if (selectedNode?.Kind == ReferenceEnhancementBrowseNodeKind.BadgeZone)
        {
            return new ReferenceEnhancementDetailModel
            {
                Kind = ReferenceEnhancementDetailKind.Category,
                Title = selectedNode.DisplayName,
                Summary = "Select a badge to view details."
            };
        }

        if (selectedNode?.Kind == ReferenceEnhancementBrowseNodeKind.HistoryPlaque)
        {
            var plaqueLocation = ResolvePlaqueLocation(
                catalog,
                selectedNode.BadgeId ?? string.Empty,
                selectedNode.PlaqueZoneId ?? string.Empty,
                selectedNode.PlaqueLocationIndex ?? -1);
            var thumbtack = plaqueLocation is null ? null : ResolveDisplayThumbtackCommand(plaqueLocation);

            return new ReferenceEnhancementDetailModel
            {
                Kind = ReferenceEnhancementDetailKind.HistoryPlaque,
                Title = selectedNode.DisplayName,
                CategoryLabel = selectedNode.PlaqueCollectionName,
                ZoneLabel = !string.IsNullOrWhiteSpace(selectedNode.PlaqueZoneId)
                    ? ResolveZoneDisplayName(catalog, selectedNode.PlaqueZoneId!)
                    : null,
                ShowZone = !string.IsNullOrWhiteSpace(selectedNode.PlaqueZoneId),
                ThumbtackCommand = thumbtack,
                ShowThumbtack = !string.IsNullOrWhiteSpace(thumbtack),
                LocationNote = plaqueLocation?.TriggerDescription,
                ShowLocationNote = !string.IsNullOrWhiteSpace(plaqueLocation?.TriggerDescription),
                ShowAcquiredState = false
            };
        }

        if (selectedNode?.Kind == ReferenceEnhancementBrowseNodeKind.Badge
            && selectedNode.IsCollectionCompletionBadge
            && !string.IsNullOrWhiteSpace(selectedNode.BadgeId)
            && catalog.TryGetBadgeById(selectedNode.BadgeId, out var completionBadge)
            && !string.IsNullOrWhiteSpace(selectedNode.PlaqueCollectionName))
        {
            return BuildHistoryCollectionCompletionBadgeDetail(
                catalog,
                completionBadge,
                selectedNode.PlaqueCollectionName!,
                acquiredBadgeIds,
                installedGameAssetProvider);
        }

        if (selectedNode?.Kind != ReferenceEnhancementBrowseNodeKind.Badge
            || string.IsNullOrWhiteSpace(selectedNode.BadgeId)
            || !catalog.TryGetBadgeById(selectedNode.BadgeId, out var badge))
        {
            return new ReferenceEnhancementDetailModel
            {
                Kind = ReferenceEnhancementDetailKind.Empty,
                Title = "Badges",
                Summary = "Select a zone, collection, Accolade, or badge to view details."
            };
        }

        var zoneId = selectedNode.IsCollectionCompletionBadge
            ? null
            : ResolveZoneIdFromNodeKey(selectedNode.NodeKey, selectedNode.ParentNodeKey);
        var zoneLabel = selectedNode.IsCollectionCompletionBadge
            ? null
            : !string.IsNullOrWhiteSpace(zoneId)
                ? ResolveZoneDisplayName(catalog, zoneId!)
                : null;
        var location = selectedNode.IsCollectionCompletionBadge
            ? null
            : ResolvePrimaryLocation(catalog, badge.CatalogItemId, zoneId);
        var acquired = acquiredBadgeIds?.Contains(badge.CatalogItemId) == true;

        return new ReferenceEnhancementDetailModel
        {
            Kind = ReferenceEnhancementDetailKind.Badge,
            Title = badge.HeroName,
            Summary = badge.HeroDescription,
            CategoryLabel = selectedNode.IsCollectionCompletionBadge
                ? selectedNode.PlaqueCollectionName
                : null,
            AcquiredStateLabel = acquired ? "Acquired" : "Not acquired",
            ShowAcquiredState = true,
            ZoneLabel = zoneLabel,
            ShowZone = !string.IsNullOrWhiteSpace(zoneLabel),
            ThumbtackCommand = selectedNode.IsCollectionCompletionBadge
                ? null
                : selectedNode.SecondaryLine,
            ShowThumbtack = !selectedNode.IsCollectionCompletionBadge
                && !string.IsNullOrWhiteSpace(selectedNode.SecondaryLine),
            LocationNote = location?.TriggerDescription,
            ShowLocationNote = !selectedNode.IsCollectionCompletionBadge
                && !string.IsNullOrWhiteSpace(location?.TriggerDescription),
            RewardText = selectedNode.IsZoneCompletionBadge
                ? badge.RewardText
                : null,
            ShowReward = selectedNode.IsZoneCompletionBadge
                && !string.IsNullOrWhiteSpace(badge.RewardText),
            IconIdentity = badge.HeroIcon,
            IconSource = ResolveBadgeIconSource(installedGameAssetProvider, badge.HeroIcon),
            IconPlaceholderLetter = string.IsNullOrWhiteSpace(badge.HeroName)
                ? "?"
                : char.ToUpperInvariant(badge.HeroName[0]).ToString(),
            UseWideBadgeIcon = selectedNode.IsZoneCompletionBadge
                || badge.IsZoneCompletionBadge
                || UsesWideBadgeArtwork(badge.HeroIcon)
        };
    }

    public static ReferenceEnhancementBrowseNode? FindNodeForBadgeId(
        ReferenceEnhancementBrowseTree tree,
        string badgeCatalogItemId)
    {
        if (string.IsNullOrWhiteSpace(badgeCatalogItemId))
        {
            return null;
        }

        return tree.NodesByKey.Values.FirstOrDefault(node =>
            (node.Kind == ReferenceEnhancementBrowseNodeKind.Badge
                || node.Kind == ReferenceEnhancementBrowseNodeKind.BadgeAccolade)
            && string.Equals(node.BadgeId, badgeCatalogItemId, StringComparison.Ordinal));
    }

    public static string CreateAccoladeNodeKey(string badgeId) => $"badge-accolade:{badgeId}";

    public static string CreateAccoladeCategoryNodeKey(string categoryId) =>
        $"{AccoladeCategoryNodeKeyPrefix}{categoryId}";

    private static ReferenceEnhancementDetailModel BuildHistoryCollectionCompletionBadgeDetail(
        IItemReferenceCatalog catalog,
        BadgeReferenceRecord badge,
        string collectionName,
        IReadOnlySet<string>? acquiredBadgeIds,
        IInstalledGameAssetProvider? installedGameAssetProvider)
    {
        var acquired = acquiredBadgeIds?.Contains(badge.CatalogItemId) == true;
        var requirementIntro = BuildHistoryCollectionRequirementText(catalog, badge, collectionName);

        return new ReferenceEnhancementDetailModel
        {
            Kind = ReferenceEnhancementDetailKind.Badge,
            Title = badge.HeroName,
            Summary = badge.HeroDescription,
            CategoryLabel = "History Badge",
            AcquiredStateLabel = acquired ? "Acquired" : "Not acquired",
            ShowAcquiredState = true,
            RewardText = badge.RewardText,
            ShowReward = !string.IsNullOrWhiteSpace(badge.RewardText),
            RequirementIntroText = requirementIntro,
            ShowRequirementIntro = !string.IsNullOrWhiteSpace(requirementIntro),
            IconIdentity = badge.HeroIcon,
            IconSource = ResolveBadgeIconSource(installedGameAssetProvider, badge.HeroIcon),
            IconPlaceholderLetter = string.IsNullOrWhiteSpace(badge.HeroName)
                ? "?"
                : char.ToUpperInvariant(badge.HeroName[0]).ToString(),
            UseWideBadgeIcon = UsesWideBadgeArtwork(badge.HeroIcon)
        };
    }

    private static string? BuildHistoryCollectionRequirementText(
        IItemReferenceCatalog catalog,
        BadgeReferenceRecord badge,
        string collectionName)
    {
        if (badge.RequirementLogicStatus == ReferenceRequirementLogicStatus.Verified
            && !string.IsNullOrWhiteSpace(badge.RequirementText))
        {
            return BadgeRewardTextSupport.NormalizeRequirementText(badge.RequirementText);
        }

        var plaqueCount = catalog.GetHistoryPlaqueRouteStops()
            .Count(stop => string.Equals(stop.CollectionName, collectionName, StringComparison.Ordinal));
        if (plaqueCount <= 0)
        {
            return null;
        }

        return $"Read all {plaqueCount} {collectionName} history plaques.";
    }

    private static string CreateZonePlaqueDisplayName(
        IItemReferenceCatalog catalog,
        HistoryPlaqueRouteCollectionRecord collection,
        HistoryPlaqueRouteStopRecord stop)
    {
        var plaqueName = !string.IsNullOrWhiteSpace(stop.PlaqueName)
            ? stop.PlaqueName
            : ResolvePlaqueLocation(
                catalog,
                collection.CompletionBadgeId,
                stop.ZoneId,
                stop.LocationIndex)?.TriggerDescription
                ?? $"Plaque {stop.LocationIndex + 1}";

        return $"{collection.CollectionName} — {plaqueName}";
    }

    private static int? ResolveHistoryPlaqueZoneRouteOrder(
        IItemReferenceCatalog catalog,
        HistoryPlaqueRouteStopRecord stop)
    {
        foreach (var location in catalog.GetBadgeLocations(stop.CompletionBadgeId))
        {
            if (!string.Equals(location.ZoneId, stop.ZoneId, StringComparison.Ordinal)
                || location.LocationIndex != stop.LocationIndex
                || !string.Equals(location.MarkerType, "HistoryPlaque", StringComparison.Ordinal))
            {
                continue;
            }

            return location.ExplorationRouteOrder;
        }

        return null;
    }

    private static ReferenceEnhancementDetailModel BuildAccoladeDetail(
        IItemReferenceCatalog catalog,
        BadgeReferenceRecord badge,
        IReadOnlySet<string>? acquiredBadgeIds,
        IInstalledGameAssetProvider? installedGameAssetProvider)
    {
        var rewardText = BadgeRewardTextSupport.NormalizeAccoladeRewardPower(
            badge.RewardText,
            badge.HeroDescription,
            badge.VillainDescription);
        var requirementPresentation = BuildAccoladeRequirementPresentation(
            catalog,
            badge,
            acquiredBadgeIds);

        return new ReferenceEnhancementDetailModel
        {
            Kind = ReferenceEnhancementDetailKind.Accolade,
            Title = badge.HeroName,
            Summary = badge.HeroDescription,
            AcquiredStateLabel = acquiredBadgeIds?.Contains(badge.CatalogItemId) == true
                ? "Acquired"
                : "Not acquired",
            ShowAcquiredState = true,
            RewardText = rewardText,
            ShowReward = !string.IsNullOrWhiteSpace(rewardText),
            IconIdentity = badge.HeroIcon,
            IconSource = ResolveBadgeIconSource(installedGameAssetProvider, badge.HeroIcon),
            IconPlaceholderLetter = string.IsNullOrWhiteSpace(badge.HeroName)
                ? "?"
                : char.ToUpperInvariant(badge.HeroName[0]).ToString(),
            UseWideBadgeIcon = UsesWideBadgeArtwork(badge.HeroIcon),
            RequirementIntroText = requirementPresentation.IntroText,
            ShowRequirementIntro = !string.IsNullOrWhiteSpace(requirementPresentation.IntroText),
            RequirementLogicNote = requirementPresentation.LogicNote,
            ShowRequirementLogicNote = !string.IsNullOrWhiteSpace(requirementPresentation.LogicNote),
            AccoladeRequirements = requirementPresentation.Requirements,
            ShowAccoladeRequirements = requirementPresentation.Requirements.Count > 0
        };
    }

    private static (string? IntroText, string? LogicNote, IReadOnlyList<ReferenceAccoladeRequirementItemDisplay> Requirements)
        BuildAccoladeRequirementPresentation(
            IItemReferenceCatalog catalog,
            BadgeReferenceRecord accolade,
            IReadOnlySet<string>? acquiredBadgeIds)
    {
        var structuredRequirements = catalog.GetAccoladeRequirements(accolade.CatalogItemId);
        var useStructuredLogic = accolade.RequirementLogicPattern is ReferenceRequirementLogicPattern.And
            or ReferenceRequirementLogicPattern.Or;
        var connector = accolade.RequirementLogicPattern == ReferenceRequirementLogicPattern.Or
            ? "OR"
            : "AND";
        var requirements = new List<ReferenceAccoladeRequirementItemDisplay>();

        if (useStructuredLogic)
        {
            for (var index = 0; index < structuredRequirements.Count; index++)
            {
                var requirement = structuredRequirements[index];
                if (!catalog.TryGetBadgeById(requirement.PrerequisiteBadgeId, out var prerequisiteBadge))
                {
                    continue;
                }

                var acquired = acquiredBadgeIds?.Contains(prerequisiteBadge.CatalogItemId) == true;
                requirements.Add(new ReferenceAccoladeRequirementItemDisplay
                {
                    BadgeId = prerequisiteBadge.CatalogItemId,
                    DisplayName = prerequisiteBadge.HeroName,
                    AcquiredStateLabel = acquired ? "Acquired" : "Not acquired",
                    ShowAcquiredState = true,
                    LogicConnectorAfter = index < structuredRequirements.Count - 1
                        ? connector
                        : null
                });
            }
        }

        string? introText = null;
        if (!string.IsNullOrWhiteSpace(accolade.RequirementText)
            && (accolade.RequirementLogicPattern == ReferenceRequirementLogicPattern.TextOnly
                || requirements.Count == 0
                || accolade.RequirementLogicStatus is ReferenceRequirementLogicStatus.Partial
                    or ReferenceRequirementLogicStatus.Unverified))
        {
            introText = BadgeRewardTextSupport.NormalizeRequirementText(accolade.RequirementText);
        }

        string? logicNote = null;
        if (accolade.RequirementLogicStatus is ReferenceRequirementLogicStatus.Partial
            or ReferenceRequirementLogicStatus.Unverified)
        {
            logicNote = "Requirement logic is not fully verified in promoted data.";
        }
        else if (useStructuredLogic && requirements.Count > 0)
        {
            logicNote = accolade.RequirementLogicPattern == ReferenceRequirementLogicPattern.Or
                ? "Any one of the following:"
                : "All of the following:";
        }

        return (introText, logicNote, requirements);
    }

    public static string CreateZoneNodeKey(string zoneId) => $"badge-zone:{zoneId}";

    public static string CreatePlaqueCollectionNodeKey(string collectionName) =>
        $"badge-plaque-collection:{collectionName}";

    public static string CreateHistoryPlaqueZoneNodeKey(string zoneId) =>
        $"badge-history-plaque-zone:{zoneId}";

    public static string CreatePlaqueZoneSegmentNodeKey(string collectionName, string zoneId) =>
        $"badge-plaque-zone:{collectionName}:{zoneId}";

    public static string CreatePlaqueNodeKey(string collectionName, string zoneId, int locationIndex) =>
        $"badge-plaque:{collectionName}:{zoneId}:{locationIndex}";

    public static string CreateCollectionCompletionBadgeNodeKey(string collectionName, string badgeId) =>
        $"badge:{badgeId}:plaque-collection:{collectionName}";

    internal static string? ResolveDisplayThumbtackCommand(BadgeLocationReferenceRecord location)
    {
        if (!location.CoordinateX.HasValue
            || !location.CoordinateY.HasValue
            || !location.CoordinateZ.HasValue)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(location.ThumbtackCommand))
        {
            return null;
        }

        var command = location.ThumbtackCommand.Trim().Trim('`');
        return command.StartsWith("/thumbtack", StringComparison.OrdinalIgnoreCase)
            ? command
            : null;
    }

    private static Dictionary<string, HashSet<string>> BuildExplorationBadgesByZone(IItemReferenceCatalog catalog)
    {
        var badgesByZone = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (var badge in catalog.GetBadges())
        {
            if (badge.ReferenceKind != ReferenceBadgeKind.ExplorationBadge)
            {
                continue;
            }

            foreach (var zone in catalog.GetZones().Values)
            {
                if (!HasExplorationMembershipEvidenceForZone(catalog, zone.ZoneId, badge))
                {
                    continue;
                }

                if (!badgesByZone.TryGetValue(zone.ZoneId, out var members))
                {
                    members = new HashSet<string>(StringComparer.Ordinal);
                    badgesByZone[zone.ZoneId] = members;
                }

                members.Add(badge.CatalogItemId);
            }
        }

        return badgesByZone;
    }

    internal static bool HasExplorationMembershipEvidenceForZone(
        IItemReferenceCatalog catalog,
        string zoneId,
        BadgeReferenceRecord badge)
    {
        if (string.IsNullOrWhiteSpace(zoneId))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(badge.CompletionBadgeId))
        {
            return TryInferZoneIdFromTourSourceId(badge.HomecomingSourceId, out var tourOnlyZoneId)
                && string.Equals(tourOnlyZoneId, zoneId, StringComparison.Ordinal);
        }

        var locations = catalog.GetBadgeLocations(badge.CatalogItemId);
        if (locations.Count > 0
            && !locations.All(location => string.Equals(location.ZoneId, zoneId, StringComparison.Ordinal)))
        {
            return false;
        }

        if (IsZoneExplorationCompletionBadge(catalog, zoneId, badge.CompletionBadgeId))
        {
            return true;
        }

        return TryInferZoneIdFromTourSourceId(badge.HomecomingSourceId, out var tourZoneId)
            && string.Equals(tourZoneId, zoneId, StringComparison.Ordinal);
    }

    internal static bool IsZoneExplorationCompletionBadge(
        IItemReferenceCatalog catalog,
        string zoneId,
        string completionBadgeId)
    {
        if (string.IsNullOrWhiteSpace(zoneId) || string.IsNullOrWhiteSpace(completionBadgeId))
        {
            return false;
        }

        if (!catalog.TryGetBadgeById(completionBadgeId, out var completionBadge))
        {
            return false;
        }

        catalog.GetZones().TryGetValue(zoneId, out var zone);
        if (zone?.ExplorationCompletionBadgeIds is { Count: > 0 } explicitIds)
        {
            return explicitIds.Any(id => string.Equals(id, completionBadgeId, StringComparison.Ordinal));
        }

        if (!string.IsNullOrWhiteSpace(completionBadge.ZoneId))
        {
            return string.Equals(completionBadge.ZoneId, zoneId, StringComparison.Ordinal);
        }

        if (TryInferZoneIdFromExplorerSourceId(completionBadge.HomecomingSourceId, out var explorerZoneId))
        {
            return string.Equals(explorerZoneId, zoneId, StringComparison.Ordinal);
        }

        return IsCanonicalExplorationCompletionForZone(catalog, zoneId, completionBadge);
    }

    private static IEnumerable<(string ZoneId, string DisplayName, ZoneReferenceRecord? ZoneRecord)> ResolveZoneEntries(
        IItemReferenceCatalog catalog,
        IReadOnlyDictionary<string, HashSet<string>> badgesByZone)
    {
        var zoneIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var zone in catalog.GetZones().Values)
        {
            zoneIds.Add(zone.ZoneId);
        }

        foreach (var zoneId in badgesByZone.Keys)
        {
            zoneIds.Add(zoneId);
        }

        return zoneIds
            .Select(zoneId =>
            {
                catalog.GetZones().TryGetValue(zoneId, out var zoneRecord);
                return (
                    zoneId,
                    ResolveZoneDisplayName(catalog, zoneId),
                    zoneRecord);
            })
            .OrderBy(entry => entry.Item2, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Item1, StringComparer.Ordinal);
    }

    private static List<string> ResolveZoneCompletionBadgeIds(
        IItemReferenceCatalog catalog,
        ZoneReferenceRecord? zone,
        IReadOnlyCollection<string> memberBadgeIds)
    {
        var completionIds = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var zoneId = zone?.ZoneId;

        if (zone?.ExplorationCompletionBadgeIds is { Count: > 0 } explicitIds)
        {
            foreach (var badgeId in explicitIds)
            {
                if (seen.Add(badgeId))
                {
                    completionIds.Add(badgeId);
                }
            }

            return SortCompletionBadgeIds(catalog, completionIds);
        }

        foreach (var memberBadgeId in memberBadgeIds)
        {
            if (!catalog.TryGetBadgeById(memberBadgeId, out var memberBadge)
                || string.IsNullOrWhiteSpace(memberBadge.CompletionBadgeId))
            {
                continue;
            }

            if (!IsExplorationCompletionValidForZone(
                    catalog,
                    zoneId,
                    memberBadge.CompletionBadgeId,
                    memberBadgeIds))
            {
                continue;
            }

            if (seen.Add(memberBadge.CompletionBadgeId))
            {
                completionIds.Add(memberBadge.CompletionBadgeId);
            }
        }

        if (!string.IsNullOrWhiteSpace(zoneId))
        {
            foreach (var badge in catalog.GetBadges())
            {
                if (!IsCanonicalExplorationCompletionForZone(catalog, zoneId, badge))
                {
                    continue;
                }

                if (seen.Add(badge.CatalogItemId))
                {
                    completionIds.Add(badge.CatalogItemId);
                }
            }
        }

        return SortCompletionBadgeIds(catalog, completionIds);
    }

    internal static bool IsExplorationCompletionValidForZone(
        IItemReferenceCatalog catalog,
        string? zoneId,
        string completionBadgeId,
        IReadOnlyCollection<string> memberBadgeIds)
    {
        if (string.IsNullOrWhiteSpace(completionBadgeId))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(zoneId))
        {
            return true;
        }

        if (!catalog.TryGetBadgeById(completionBadgeId, out var completionBadge))
        {
            return false;
        }

        catalog.GetZones().TryGetValue(zoneId, out var zone);
        if (zone?.ExplorationCompletionBadgeIds is { Count: > 0 } explicitIds)
        {
            return explicitIds.Any(id => string.Equals(id, completionBadgeId, StringComparison.Ordinal));
        }

        if (!string.IsNullOrWhiteSpace(completionBadge.ZoneId))
        {
            return string.Equals(completionBadge.ZoneId, zoneId, StringComparison.Ordinal);
        }

        if (TryInferZoneIdFromExplorerSourceId(completionBadge.HomecomingSourceId, out var explorerZoneId))
        {
            return string.Equals(explorerZoneId, zoneId, StringComparison.Ordinal);
        }

        if (IsCanonicalExplorationCompletionForZone(catalog, zoneId, completionBadge))
        {
            return true;
        }

        var agreeingMembers = memberBadgeIds.Count(memberId =>
            catalog.TryGetBadgeById(memberId, out var member)
            && string.Equals(member.CompletionBadgeId, completionBadgeId, StringComparison.Ordinal));

        return agreeingMembers >= 2;
    }

    private static bool IsCanonicalExplorationCompletionForZone(
        IItemReferenceCatalog catalog,
        string zoneId,
        BadgeReferenceRecord badge)
    {
        if (badge.IsZoneCompletionBadge
            && !string.IsNullOrWhiteSpace(badge.ZoneId)
            && string.Equals(badge.ZoneId, zoneId, StringComparison.Ordinal))
        {
            return true;
        }

        if (badge.ReferenceKind != ReferenceBadgeKind.Accolade
            || string.IsNullOrWhiteSpace(badge.HomecomingSourceId)
            || !badge.HomecomingSourceId.EndsWith("Explorer", StringComparison.Ordinal))
        {
            return false;
        }

        return TryInferZoneIdFromExplorerSourceId(badge.HomecomingSourceId, out var explorerZoneId)
            && string.Equals(explorerZoneId, zoneId, StringComparison.Ordinal);
    }

    private static bool TryInferZoneIdFromExplorerSourceId(string? homecomingSourceId, out string zoneId)
    {
        zoneId = string.Empty;
        if (string.IsNullOrWhiteSpace(homecomingSourceId)
            || !homecomingSourceId.EndsWith("Explorer", StringComparison.Ordinal))
        {
            return false;
        }

        var zoneName = SplitCamelCase(homecomingSourceId[..^"Explorer".Length]);
        if (string.IsNullOrWhiteSpace(zoneName))
        {
            return false;
        }

        zoneId = CreateZoneIdFromDisplayName(zoneName);
        return zoneId.Length > 0;
    }

    internal static bool TryInferZoneIdFromTourSourceId(string? homecomingSourceId, out string zoneId)
    {
        zoneId = string.Empty;
        if (string.IsNullOrWhiteSpace(homecomingSourceId)
            || string.Equals(homecomingSourceId, "MissionArchitectTourism", StringComparison.Ordinal))
        {
            return false;
        }

        var tourMatch = Regex.Match(homecomingSourceId, @"^(.+?)Tour\d+$");
        if (tourMatch.Success)
        {
            return TryMapTourPrefixToZoneId(tourMatch.Groups[1].Value, out zoneId);
        }

        var parkstrollerMatch = Regex.Match(homecomingSourceId, @"^(.+?)_Parkstroller$");
        if (parkstrollerMatch.Success)
        {
            return TryMapTourPrefixToZoneId(parkstrollerMatch.Groups[1].Value, out zoneId);
        }

        return false;
    }

    private static bool TryMapTourPrefixToZoneId(string tourPrefix, out string zoneId)
    {
        if (string.Equals(tourPrefix, "DungeonLabyrinth", StringComparison.Ordinal))
        {
            zoneId = "zone-the-labyrinth-of-fog";
            return true;
        }

        if (tourPrefix.EndsWith("Zone", StringComparison.Ordinal) && tourPrefix.Length > 4)
        {
            var withoutZoneSuffix = tourPrefix[..^4];
            var zoneNameWithoutSuffix = SplitCamelCase(withoutZoneSuffix);
            if (!string.IsNullOrWhiteSpace(zoneNameWithoutSuffix))
            {
                zoneId = CreateZoneIdFromDisplayName(zoneNameWithoutSuffix);
                if (zoneId.Length > 0)
                {
                    return true;
                }
            }
        }

        var zoneName = SplitCamelCase(tourPrefix);
        if (string.IsNullOrWhiteSpace(zoneName))
        {
            zoneId = string.Empty;
            return false;
        }

        zoneId = CreateZoneIdFromDisplayName(zoneName);
        return zoneId.Length > 0;
    }

    private static string SplitCamelCase(string value) =>
        Regex.Replace(value, "([a-z])([A-Z])", "$1 $2");

    private static string CreateZoneIdFromDisplayName(string displayName)
    {
        var normalized = Regex.Replace(displayName.Trim().ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
        return normalized.Length == 0 ? string.Empty : $"zone-{normalized}";
    }

    private static List<string> SortCompletionBadgeIds(
        IItemReferenceCatalog catalog,
        List<string> completionIds)
    {
        completionIds.Sort((left, right) =>
        {
            if (!catalog.TryGetBadgeById(left, out var leftBadge))
            {
                return string.Compare(left, right, StringComparison.Ordinal);
            }

            if (!catalog.TryGetBadgeById(right, out var rightBadge))
            {
                return string.Compare(left, right, StringComparison.Ordinal);
            }

            return string.Compare(leftBadge.HeroName, rightBadge.HeroName, StringComparison.OrdinalIgnoreCase);
        });

        return completionIds;
    }

    private static ReferenceEnhancementBrowseNode CreateBadgeNode(
        IItemReferenceCatalog catalog,
        BadgeReferenceRecord badge,
        string zoneId,
        bool isZoneCompletion,
        IReadOnlySet<string> acquiredBadgeIds) =>
        new()
        {
            Kind = ReferenceEnhancementBrowseNodeKind.Badge,
            DisplayName = badge.HeroName,
            NodeKey = $"badge:{badge.CatalogItemId}:zone:{zoneId}",
            ParentNodeKey = CreateZoneNodeKey(zoneId),
            BadgeId = badge.CatalogItemId,
            IconIdentity = badge.HeroIcon,
            SecondaryLine = ResolveThumbtackForBadgeInZone(catalog, badge.CatalogItemId, zoneId),
            IsAcquired = acquiredBadgeIds.Contains(badge.CatalogItemId),
            IsZoneCompletionBadge = isZoneCompletion
        };

    private static string? ResolveThumbtackForBadgeInZone(
        IItemReferenceCatalog catalog,
        string badgeCatalogItemId,
        string zoneId)
    {
        foreach (var location in catalog.GetBadgeLocations(badgeCatalogItemId))
        {
            if (!string.Equals(location.ZoneId, zoneId, StringComparison.Ordinal))
            {
                continue;
            }

            var command = ResolveDisplayThumbtackCommand(location);
            if (command is not null)
            {
                return command;
            }
        }

        return null;
    }

    private static BadgeLocationReferenceRecord? ResolvePrimaryLocation(
        IItemReferenceCatalog catalog,
        string badgeCatalogItemId,
        string? zoneId)
    {
        var locations = catalog.GetBadgeLocations(badgeCatalogItemId);
        if (locations.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(zoneId))
        {
            var zoneMatch = locations.FirstOrDefault(location =>
                string.Equals(location.ZoneId, zoneId, StringComparison.Ordinal));
            if (zoneMatch is not null)
            {
                return zoneMatch;
            }
        }

        return locations[0];
    }

    private static string ResolveZoneDisplayName(IItemReferenceCatalog catalog, string zoneId)
    {
        if (catalog.GetZones().TryGetValue(zoneId, out var zone))
        {
            return zone.DisplayName;
        }

        return HumanizeZoneId(zoneId);
    }

    private static string HumanizeZoneId(string zoneId)
    {
        if (zoneId.StartsWith("zone-", StringComparison.Ordinal))
        {
            zoneId = zoneId["zone-".Length..];
        }

        return string.Join(
            ' ',
            zoneId.Split('-', StringSplitOptions.RemoveEmptyEntries)
                .Select(token => char.ToUpperInvariant(token[0]) + token[1..]));
    }

    private static string? ResolveZoneIdFromNodeKey(string nodeKey, string? parentNodeKey)
    {
        const string zoneSuffix = ":zone:";
        var zoneIndex = nodeKey.LastIndexOf(zoneSuffix, StringComparison.Ordinal);
        if (zoneIndex >= 0)
        {
            return nodeKey[(zoneIndex + zoneSuffix.Length)..];
        }

        if (!string.IsNullOrWhiteSpace(parentNodeKey)
            && parentNodeKey.StartsWith("badge-zone:", StringComparison.Ordinal))
        {
            return parentNodeKey["badge-zone:".Length..];
        }

        return null;
    }

    private static ImageSource? ResolveBadgeIconSource(
        IInstalledGameAssetProvider? installedGameAssetProvider,
        string? iconIdentity)
    {
        if (installedGameAssetProvider is null || string.IsNullOrWhiteSpace(iconIdentity))
        {
            return null;
        }

        return installedGameAssetProvider.TryResolve(iconIdentity);
    }

    private static bool UsesWideBadgeArtwork(string? iconIdentity) =>
        !string.IsNullOrWhiteSpace(iconIdentity)
        && (iconIdentity.Contains("Accolade", StringComparison.OrdinalIgnoreCase)
            || iconIdentity.Contains("accolade", StringComparison.OrdinalIgnoreCase));

    private static void IndexNodes(
        ReferenceEnhancementBrowseNode node,
        IDictionary<string, ReferenceEnhancementBrowseNode> nodesByKey)
    {
        if (!nodesByKey.ContainsKey(node.NodeKey))
        {
            nodesByKey[node.NodeKey] = node;
        }

        foreach (var child in node.Children)
        {
            IndexNodes(child, nodesByKey);
        }
    }
}
