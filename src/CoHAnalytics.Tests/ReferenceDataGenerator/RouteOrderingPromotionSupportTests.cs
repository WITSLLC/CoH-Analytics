using System.Text.Json;
using CoHAnalytics.ReferenceData;
using CoHAnalytics.ReferenceDataGenerator;

namespace CoHAnalytics.Tests.ReferenceDataGenerator;

public sealed class RouteOrderingPromotionSupportTests
{
    [Fact]
    public void Apply_promotes_exploration_route_order_and_history_plaque_metadata()
    {
        var catalog = new ItemReferenceCatalogDocument
        {
            Manifest = new ItemReferenceManifestDocument
            {
                CatalogVersion = "item-ref-test",
                HomecomingCompatibility = new ItemReferenceHomecomingCompatibilityDocument
                {
                    BuildMin = "Issue 28",
                    BuildMax = "Issue 28"
                }
            },
            Badges =
            [
                new BadgeReferenceRecordDocument
                {
                    CatalogItemId = "BAD-00001",
                    HomecomingSourceId = "Alpha",
                    ReferenceKind = "ExplorationBadge",
                    HeroName = "Alpha",
                    VillainName = "Alpha",
                    VerificationStatus = "VerifiedMultiSource"
                },
                new BadgeReferenceRecordDocument
                {
                    CatalogItemId = "BAD-02758",
                    HomecomingSourceId = "Academic",
                    ReferenceKind = "HistoryPlaqueCollection",
                    HeroName = "Academic",
                    VillainName = "Academic",
                    VerificationStatus = "VerifiedMultiSource"
                }
            ],
            Zones =
            [
                new ZoneReferenceRecordDocument { ZoneId = "zone-test", DisplayName = "Test Zone" },
                new ZoneReferenceRecordDocument { ZoneId = "zone-other", DisplayName = "Other Zone" }
            ],
            BadgeLocations =
            [
                new BadgeLocationReferenceRecordDocument
                {
                    BadgeCatalogItemId = "BAD-00001",
                    ZoneId = "zone-test",
                    LocationIndex = 0,
                    VerificationStatus = "VerifiedMultiSource"
                }
            ]
        };

        var package = new RouteOrderingPackage(
            new RouteOrderingSourceDocument
            {
                Name = "Optimal badge/plaque collection path maps and popmenu",
                Author = "AboveTheChemist",
                Version = "Map package 1.2.1 / popmenu 20250708.01"
            },
            [
                new RouteOrderingExplorationZoneDocument
                {
                    ZoneId = "zone-test",
                    Stops =
                    [
                        new RouteOrderingExplorationStopDocument
                        {
                            RouteOrder = 7,
                            BadgeId = "BAD-00001",
                            LocationIdentity = "BAD-00001:location:0",
                            LocationIndex = 0,
                            SourceProject = "Optimal badge/plaque collection path maps and popmenu",
                            SourceVersion = "Map package 1.2.1 / popmenu 20250708.01",
                            MappingConfidence = "Exact"
                        }
                    ]
                }
            ],
            [
                new RouteOrderingHistoryCollectionDocument
                {
                    Collection = "Academic",
                    CompletionBadgeId = "BAD-02758",
                    PublishedCollectionOrderAvailable = false,
                    OrderingStatus = "UnresolvedNoPublishedCrossZoneOrder",
                    Stops =
                    [
                        new RouteOrderingHistoryStopDocument
                        {
                            InventoryOrder = 1,
                            RouteOrder = null,
                            PlaqueIdentity = "BAD-02758:location:0",
                            CompletionBadgeId = "BAD-02758",
                            ZoneId = "zone-other",
                            SourceZoneRouteOrder = 2,
                            MappingConfidence = "Exact"
                        }
                    ]
                },
                new RouteOrderingHistoryCollectionDocument
                {
                    Collection = "Christie Consolidation",
                    CompletionBadgeId = "BAD-02554",
                    PublishedCollectionOrderAvailable = true,
                    OrderingStatus = "PublishedSingleZoneOrder",
                    Stops =
                    [
                        new RouteOrderingHistoryStopDocument
                        {
                            InventoryOrder = 1,
                            RouteOrder = 5,
                            PlaqueIdentity = "BAD-02554:location:0",
                            CompletionBadgeId = "BAD-02554",
                            ZoneId = "zone-test",
                            SourceZoneRouteOrder = 11,
                            MappingConfidence = "Exact"
                        }
                    ]
                }
            ]);

        var result = RouteOrderingPromotionSupport.Apply(catalog, package);

        Assert.Equal(1, result.ExplorationZonesApplied);
        Assert.Equal(1, result.ExplorationStopsApplied);
        Assert.Equal(2, result.HistoryPlaqueCollectionsApplied);
        Assert.Equal(2, result.HistoryPlaqueStopsApplied);

        var location = Assert.Single(catalog.BadgeLocations);
        Assert.Equal(7, location.ExplorationRouteOrder);
        Assert.Equal("Exact", location.RouteMappingConfidence);
        Assert.NotNull(catalog.RouteOrderingProvenance);
        Assert.Equal("AboveTheChemist", catalog.RouteOrderingProvenance.Author);

        var academic = catalog.HistoryPlaqueRouteCollections.Single(collection =>
            collection.CollectionName == "Academic");
        Assert.False(academic.PublishedCollectionOrderAvailable);
        Assert.Equal("UnresolvedNoPublishedCrossZoneOrder", academic.OrderingStatus);

        var academicStop = catalog.HistoryPlaqueRouteStops.Single(stop => stop.CollectionName == "Academic");
        Assert.Null(academicStop.RouteOrder);
        Assert.Equal(2, academicStop.SourceZoneRouteOrder);

        var singleZone = catalog.HistoryPlaqueRouteCollections.Single(collection =>
            collection.CollectionName == "Christie Consolidation");
        Assert.True(singleZone.PublishedCollectionOrderAvailable);
        Assert.Equal(5, catalog.HistoryPlaqueRouteStops.Single(stop =>
            stop.CollectionName == "Christie Consolidation").RouteOrder);
    }

    [Fact]
    public void Apply_resolves_exploration_route_order_when_research_badge_id_collides_with_achievement_badge()
    {
        var catalog = new ItemReferenceCatalogDocument
        {
            Manifest = new ItemReferenceManifestDocument
            {
                CatalogVersion = "item-ref-test",
                HomecomingCompatibility = new ItemReferenceHomecomingCompatibilityDocument
                {
                    BuildMin = "Issue 28",
                    BuildMax = "Issue 28"
                }
            },
            Badges =
            [
                new BadgeReferenceRecordDocument
                {
                    CatalogItemId = "BAD-00004",
                    HomecomingSourceId = "1750Badges",
                    ReferenceKind = nameof(ReferenceBadgeKind.Other),
                    HeroName = "Still Standing",
                    VillainName = "Still Standing",
                    VerificationStatus = "SecondaryOnly"
                },
                new BadgeReferenceRecordDocument
                {
                    CatalogItemId = "BAD-03186",
                    HomecomingSourceId = "SirensCallTour7",
                    ReferenceKind = nameof(ReferenceBadgeKind.ExplorationBadge),
                    HeroName = "Still Standing",
                    VillainName = "Still Standing",
                    VerificationStatus = "SecondaryOnly"
                }
            ],
            Zones =
            [
                new ZoneReferenceRecordDocument { ZoneId = "zone-siren-s-call", DisplayName = "Siren's Call" }
            ],
            BadgeLocations =
            [
                new BadgeLocationReferenceRecordDocument
                {
                    BadgeCatalogItemId = "BAD-03186",
                    ZoneId = "zone-siren-s-call",
                    LocationIndex = 0,
                    VerificationStatus = "VerifiedMultiSource"
                }
            ]
        };

        var package = new RouteOrderingPackage(
            new RouteOrderingSourceDocument { Name = "test", Author = "test", Version = "1" },
            [
                new RouteOrderingExplorationZoneDocument
                {
                    ZoneId = "zone-siren-s-call",
                    Stops =
                    [
                        new RouteOrderingExplorationStopDocument
                        {
                            RouteOrder = 2,
                            BadgeId = "BAD-00004",
                            BadgeName = "Still Standing",
                            HomecomingSourceId = "1750Badges",
                            LocationIdentity = "BAD-00004:location:0",
                            LocationIndex = 0,
                            MappingConfidence = "Exact"
                        }
                    ]
                }
            ],
            []);

        var result = RouteOrderingPromotionSupport.Apply(catalog, package);

        Assert.Equal(1, result.ExplorationStopsApplied);
        var location = Assert.Single(catalog.BadgeLocations);
        Assert.Equal("BAD-03186", location.BadgeCatalogItemId);
        Assert.Equal(2, location.ExplorationRouteOrder);
    }
}
