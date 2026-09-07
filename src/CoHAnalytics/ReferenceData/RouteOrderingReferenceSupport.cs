namespace CoHAnalytics.ReferenceData;

/// <summary>Helpers for supplemental route-order metadata stored on badge locations.</summary>
public static class RouteOrderingReferenceSupport
{
    public static int? ResolveExplorationRouteOrder(
        IItemReferenceCatalog catalog,
        string zoneId,
        string badgeCatalogItemId)
    {
        foreach (var location in catalog.GetBadgeLocations(badgeCatalogItemId))
        {
            if (!string.Equals(location.ZoneId, zoneId, StringComparison.Ordinal))
            {
                continue;
            }

            if (location.ExplorationRouteOrder.HasValue)
            {
                return location.ExplorationRouteOrder;
            }
        }

        return null;
    }

    public static IEnumerable<BadgeReferenceRecord> OrderExplorationMemberBadges(
        IItemReferenceCatalog catalog,
        string zoneId,
        IEnumerable<BadgeReferenceRecord> memberBadges)
    {
        return memberBadges
            .OrderBy(badge => ResolveExplorationRouteOrder(catalog, zoneId, badge.CatalogItemId) ?? int.MaxValue)
            .ThenBy(badge => badge.HeroName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(badge => badge.CatalogItemId, StringComparer.Ordinal);
    }

    public static bool TryParseLocationIdentity(string locationIdentity, out string badgeId, out int locationIndex)
    {
        badgeId = string.Empty;
        locationIndex = -1;
        if (string.IsNullOrWhiteSpace(locationIdentity))
        {
            return false;
        }

        var parts = locationIdentity.Split(':', StringSplitOptions.TrimEntries);
        if (parts.Length != 3
            || !parts[0].StartsWith("BAD-", StringComparison.Ordinal)
            || !string.Equals(parts[1], "location", StringComparison.OrdinalIgnoreCase)
            || !int.TryParse(parts[2], out locationIndex))
        {
            return false;
        }

        badgeId = parts[0];
        return true;
    }
}
