using CoHAnalytics.Models;
using CoHAnalytics.ReferenceData;

namespace CoHAnalytics.Services;

public interface IBadgeAcquisitionResolver
{
    BadgeAcquiredEvent Resolve(string observedBadgeTitle);
}

internal sealed class BadgeAcquisitionResolver : IBadgeAcquisitionResolver
{
    private readonly IItemReferenceCatalog _catalog;

    public BadgeAcquisitionResolver(IItemReferenceCatalog catalog)
    {
        _catalog = catalog;
    }

    public BadgeAcquiredEvent Resolve(string observedBadgeTitle)
    {
        if (string.IsNullOrWhiteSpace(observedBadgeTitle))
        {
            return Unresolved(observedBadgeTitle);
        }

        if (!_catalog.TryResolve(observedBadgeTitle, out var resolution)
            || resolution.Item.Family != ReferenceItemFamily.Badge)
        {
            return Unresolved(observedBadgeTitle);
        }

        return new BadgeAcquiredEvent
        {
            ObservedBadgeTitle = observedBadgeTitle,
            ResolvedCatalogItemId = resolution.Item.CatalogItemId,
            ResolutionState = AcquisitionIdentityResolutionState.Resolved,
            CatalogVersion = resolution.CatalogVersion,
            MatchedAliasText = resolution.MatchedAliasText
        };
    }

    private static BadgeAcquiredEvent Unresolved(string observedBadgeTitle) =>
        new()
        {
            ObservedBadgeTitle = observedBadgeTitle,
            ResolutionState = AcquisitionIdentityResolutionState.Unresolved
        };
}
