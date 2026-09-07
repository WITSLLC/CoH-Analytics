using CoHAnalytics.ReferenceDataGenerator;

namespace CoHAnalytics.Tests.ReferenceDataGenerator;

[Trait("Category", PrivateResearchTestEnvironment.Category)]
public sealed class ExplorationRouteLocationReconciliationTests
{
    private static readonly (string InternalName, string BadgeId, string ZoneId)[] MissingRouteLocationBadges =
    [
        ("BloodyBayTour6", "BAD-01975", "zone-bloody-bay"),
        ("CroatoaTour8", "BAD-02092", "zone-croatoa"),
        ("FaultlineTour2", "BAD-02260", "zone-echo-faultline"),
        ("GrandvilleTour4", "BAD-02345", "zone-grandville"),
        ("KingsRowTour8", "BAD-02573", "zone-kings-row"),
        ("KingsRowTour3", "BAD-02568", "zone-kings-row"),
        ("MercyIslandTour6", "BAD-02658", "zone-mercy-island"),
        ("P_Longshoreman", "BAD-02923", "zone-neutropolis"),
        ("SirensCallTour1", "BAD-03180", "zone-siren-s-call"),
        ("ChantryTour1", "BAD-02034", "zone-the-chantry"),
        ("StormPalaceTour8", "BAD-03254", "zone-the-storm-palace")
    ];

    [Fact]
    public void Build_PromotesResearchExplorationLocationsForGenderVariantBadges()
    {
        var research = BadgeResearchPackageLoader.Load(GetResearchRoot());
        var candidates = MissingRouteLocationBadges
            .Select(entry => CreateCandidate(entry.InternalName, entry.BadgeId))
            .ToArray();

        var artifacts = HomecomingBadgePromotionSupport.Build(candidates, research);

        foreach (var (internalName, badgeId, zoneId) in MissingRouteLocationBadges)
        {
            var location = Assert.Single(
                artifacts.BadgeLocations,
                value => value.BadgeCatalogItemId == badgeId && value.ZoneId == zoneId);
            Assert.Equal("ExplorationBadge", location.MarkerType);
            Assert.Equal(0, location.LocationIndex);
            Assert.NotNull(location.CoordinateX);
            Assert.NotNull(location.CoordinateY);
            Assert.NotNull(location.CoordinateZ);
            Assert.Contains(internalName, candidates.Select(candidate => candidate.HomecomingSourceId));
        }
    }

    private static HomecomingBadgeCandidateRecord CreateCandidate(string internalName, string badgeId) =>
        new(
            badgeId,
            internalName,
            1,
            1,
            "DEFS/BADGES/BADGES_TOURISM.DEF",
            "TOURISM",
            "P_HERO",
            "Localized {Hero.gender=male Title|Variant}",
            "P_VILLAIN",
            "Localized {Hero.gender=male Title|Variant}",
            null,
            null,
            null,
            null,
            "badge_tourist_01",
            null,
            nameof(HomecomingBadgeMatchStatus.NewFromHomecoming),
            []);

    private static string GetResearchRoot() => PrivateResearchTestEnvironment.RequireResearchRoot();
}
