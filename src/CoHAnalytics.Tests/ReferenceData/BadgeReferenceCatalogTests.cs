using System.Text;
using System.Text.Json;
using CoHAnalytics.ReferenceData;

namespace CoHAnalytics.Tests.ReferenceData;

public sealed class BadgeReferenceCatalogTests
{
    [Fact]
    public void Loader_AcceptsMinimalBadgeCatalogSlice()
    {
        var json = """
            {
              "manifest": {
                "catalogVersion": "item-ref-test",
                "homecomingCompatibility": { "buildMin": "Issue 28", "buildMax": "Issue 28" }
              },
              "items": [
                {
                  "catalogItemId": "BAD-00001",
                  "family": "Badge",
                  "subtype": "Exploration",
                  "currentDisplayName": "Atlas Tour Guide",
                  "activeStatus": "Active",
                  "verificationStatus": "VerifiedMultiSource",
                  "icon": "badge_tourist_01.tga"
                }
              ],
              "aliases": [],
              "enhancementSets": [],
              "badges": [
                {
                  "catalogItemId": "BAD-00001",
                  "homecomingSourceId": "AtlasParkExplorer",
                  "setTitleId": 1517,
                  "canonicalCategory": "Exploration",
                  "badgeType": 1,
                  "referenceKind": "ExplorationBadge",
                  "heroName": "Atlas Tour Guide",
                  "villainName": "Atlas Tour Guide",
                  "verificationStatus": "VerifiedMultiSource"
                }
              ],
              "zones": [
                {
                  "zoneId": "zone-atlas-park",
                  "displayName": "Atlas Park",
                  "explorationCompletionBadgeIds": ["BAD-00001"]
                }
              ],
              "badgeLocations": [
                {
                  "badgeCatalogItemId": "BAD-00001",
                  "zoneId": "zone-atlas-park",
                  "coordinateX": 128.5,
                  "coordinateY": 16.4,
                  "coordinateZ": -233,
                  "thumbtackCommand": "/thumbtack 128.5 16.4 -233",
                  "markerType": "ExplorationBadge",
                  "locationRole": "ExplorationMarker",
                  "coordinateSemantics": "SurveyedMarkerCenter",
                  "verificationStatus": "VerifiedMultiSource",
                  "locationIndex": 0
                }
              ],
              "badgeAccoladeRequirements": []
            }
            """;

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var result = ItemReferenceCatalogLoader.Load(stream);
        Assert.True(result.Succeeded, result.FailureReason);
        Assert.NotNull(result.Badges);
        Assert.Single(result.Badges!);
        Assert.Single(result.Zones!);
        Assert.Single(result.BadgeLocations!);

        var catalog = ItemReferenceCatalog.FromLoadResult(result);
        Assert.True(catalog.TryGetBadgeById("BAD-00001", out var badge));
        Assert.Equal("AtlasParkExplorer", badge.HomecomingSourceId);
        Assert.True(catalog.TryGetBadgeByHomecomingSourceId("AtlasParkExplorer", out badge));
        Assert.Equal("BAD-00001", badge.CatalogItemId);
        Assert.Single(catalog.GetBadges());
        Assert.Single(catalog.GetZones());
        Assert.Single(catalog.GetBadgeLocations("BAD-00001"));
    }
}
