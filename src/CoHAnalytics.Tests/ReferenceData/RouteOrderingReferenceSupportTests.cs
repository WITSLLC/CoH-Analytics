using CoHAnalytics.ReferenceData;

namespace CoHAnalytics.Tests.ReferenceData;

public sealed class RouteOrderingReferenceSupportTests
{
    [Fact]
    public void OrderExplorationMemberBadges_places_route_covered_badges_before_uncovered()
    {
        var catalog = LoadCatalog(
            """
            {
              "manifest": { "catalogVersion": "test", "homecomingCompatibility": { "buildMin": "Issue 28", "buildMax": "Issue 28" } },
              "items": [
                { "catalogItemId": "BAD-00001", "family": "Badge", "subtype": "Exploration", "currentDisplayName": "Zulu", "activeStatus": "Active", "verificationStatus": "VerifiedMultiSource" },
                { "catalogItemId": "BAD-00002", "family": "Badge", "subtype": "Exploration", "currentDisplayName": "Alpha", "activeStatus": "Active", "verificationStatus": "VerifiedMultiSource" },
                { "catalogItemId": "BAD-00003", "family": "Badge", "subtype": "Exploration", "currentDisplayName": "Mike", "activeStatus": "Active", "verificationStatus": "VerifiedMultiSource" }
              ],
              "aliases": [],
              "enhancementSets": [],
              "badges": [
                { "catalogItemId": "BAD-00001", "homecomingSourceId": "A", "canonicalCategory": "Exploration", "badgeType": 1, "referenceKind": "ExplorationBadge", "heroName": "Zulu", "villainName": "Zulu", "verificationStatus": "VerifiedMultiSource" },
                { "catalogItemId": "BAD-00002", "homecomingSourceId": "B", "canonicalCategory": "Exploration", "badgeType": 1, "referenceKind": "ExplorationBadge", "heroName": "Alpha", "villainName": "Alpha", "verificationStatus": "VerifiedMultiSource" },
                { "catalogItemId": "BAD-00003", "homecomingSourceId": "C", "canonicalCategory": "Exploration", "badgeType": 1, "referenceKind": "ExplorationBadge", "heroName": "Mike", "villainName": "Mike", "verificationStatus": "VerifiedMultiSource" }
              ],
              "zones": [{ "zoneId": "zone-test", "displayName": "Test Zone" }],
              "badgeLocations": [
                { "badgeCatalogItemId": "BAD-00001", "zoneId": "zone-test", "locationIndex": 0, "explorationRouteOrder": 2, "verificationStatus": "VerifiedMultiSource" },
                { "badgeCatalogItemId": "BAD-00002", "zoneId": "zone-test", "locationIndex": 0, "explorationRouteOrder": 1, "verificationStatus": "VerifiedMultiSource" },
                { "badgeCatalogItemId": "BAD-00003", "zoneId": "zone-test", "locationIndex": 0, "verificationStatus": "VerifiedMultiSource" }
              ],
              "badgeAccoladeRequirements": []
            }
            """);

        var ordered = RouteOrderingReferenceSupport
            .OrderExplorationMemberBadges(
                catalog,
                "zone-test",
                catalog.GetBadges())
            .Select(badge => badge.HeroName)
            .ToArray();

        Assert.Equal(["Alpha", "Zulu", "Mike"], ordered);
    }

    [Fact]
    public void OrderExplorationMemberBadges_preserves_exact_route_order()
    {
        var catalog = LoadCatalog(
            """
            {
              "manifest": { "catalogVersion": "test", "homecomingCompatibility": { "buildMin": "Issue 28", "buildMax": "Issue 28" } },
              "items": [
                { "catalogItemId": "BAD-00001", "family": "Badge", "subtype": "Exploration", "currentDisplayName": "Zulu", "activeStatus": "Active", "verificationStatus": "VerifiedMultiSource" },
                { "catalogItemId": "BAD-00002", "family": "Badge", "subtype": "Exploration", "currentDisplayName": "Alpha", "activeStatus": "Active", "verificationStatus": "VerifiedMultiSource" },
                { "catalogItemId": "BAD-00003", "family": "Badge", "subtype": "Exploration", "currentDisplayName": "Mike", "activeStatus": "Active", "verificationStatus": "VerifiedMultiSource" }
              ],
              "aliases": [],
              "enhancementSets": [],
              "badges": [
                { "catalogItemId": "BAD-00001", "homecomingSourceId": "A", "canonicalCategory": "Exploration", "badgeType": 1, "referenceKind": "ExplorationBadge", "heroName": "Third", "villainName": "Third", "verificationStatus": "VerifiedMultiSource" },
                { "catalogItemId": "BAD-00002", "homecomingSourceId": "B", "canonicalCategory": "Exploration", "badgeType": 1, "referenceKind": "ExplorationBadge", "heroName": "First", "villainName": "First", "verificationStatus": "VerifiedMultiSource" },
                { "catalogItemId": "BAD-00003", "homecomingSourceId": "C", "canonicalCategory": "Exploration", "badgeType": 1, "referenceKind": "ExplorationBadge", "heroName": "Second", "villainName": "Second", "verificationStatus": "VerifiedMultiSource" }
              ],
              "zones": [{ "zoneId": "zone-test", "displayName": "Test Zone" }],
              "badgeLocations": [
                { "badgeCatalogItemId": "BAD-00001", "zoneId": "zone-test", "locationIndex": 0, "explorationRouteOrder": 3, "verificationStatus": "VerifiedMultiSource" },
                { "badgeCatalogItemId": "BAD-00002", "zoneId": "zone-test", "locationIndex": 0, "explorationRouteOrder": 1, "verificationStatus": "VerifiedMultiSource" },
                { "badgeCatalogItemId": "BAD-00003", "zoneId": "zone-test", "locationIndex": 0, "explorationRouteOrder": 2, "verificationStatus": "VerifiedMultiSource" }
              ],
              "badgeAccoladeRequirements": []
            }
            """);

        var ordered = RouteOrderingReferenceSupport
            .OrderExplorationMemberBadges(
                catalog,
                "zone-test",
                catalog.GetBadges())
            .Select(badge => badge.HeroName)
            .ToArray();

        Assert.Equal(["First", "Second", "Third"], ordered);
    }

    private static IItemReferenceCatalog LoadCatalog(string json)
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
        var result = ItemReferenceCatalogLoader.Load(stream);
        Assert.True(result.Succeeded, result.FailureReason);
        return ItemReferenceCatalog.FromLoadResult(result);
    }
}
