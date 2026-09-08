using CoHAnalytics.Models;
using CoHAnalytics.ReferenceData;
using CoHAnalytics.Services;
using System.Text;

namespace CoHAnalytics.Tests.Services;

public sealed class BadgeAcquisitionResolverTests
{
    private static IItemReferenceCatalog LoadCatalog(string json)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var result = ItemReferenceCatalogLoader.Load(stream);
        Assert.True(result.Succeeded, result.FailureReason);
        return ItemReferenceCatalog.FromLoadResult(result);
    }

    [Fact]
    public void Hero_display_title_resolves_to_canonical_badge_id()
    {
        var catalog = LoadCatalog("""
            {
              "manifest": {
                "catalogVersion": "item-ref-test",
                "homecomingCompatibility": { "buildMin": "Issue 28", "buildMax": "Issue 28" }
              },
              "items": [{
                "catalogItemId": "BAD-00001",
                "family": "Badge",
                "subtype": "Exploration",
                "currentDisplayName": "Atlas Tour Guide",
                "activeStatus": "Active",
                "verificationStatus": "VerifiedMultiSource"
              }],
              "aliases": [{
                "catalogItemId": "BAD-00001",
                "locale": "en",
                "text": "Atlas Tour Guide",
                "nameKind": "Display",
                "isPreferred": true
              }],
              "enhancementSets": [],
              "badges": [{
                "catalogItemId": "BAD-00001",
                "homecomingSourceId": "AtlasParkExplorer",
                "setTitleId": 1517,
                "canonicalCategory": "Exploration",
                "badgeType": 1,
                "referenceKind": "ExplorationBadge",
                "heroName": "Atlas Tour Guide",
                "villainName": "Atlas Tour Guide",
                "verificationStatus": "VerifiedMultiSource"
              }],
              "zones": [],
              "badgeLocations": [],
              "badgeAccoladeRequirements": []
            }
            """);

        var resolver = new BadgeAcquisitionResolver(catalog);
        var result = resolver.Resolve("Atlas Tour Guide");

        Assert.Equal(AcquisitionIdentityResolutionState.Resolved, result.ResolutionState);
        Assert.Equal("BAD-00001", result.ResolvedCatalogItemId);
    }

    [Fact]
    public void Villain_display_title_resolves_when_catalog_alias_exists()
    {
        var catalog = LoadCatalog("""
            {
              "manifest": {
                "catalogVersion": "item-ref-test",
                "homecomingCompatibility": { "buildMin": "Issue 28", "buildMax": "Issue 28" }
              },
              "items": [{
                "catalogItemId": "BAD-00002",
                "family": "Badge",
                "subtype": "Exploration",
                "currentDisplayName": "Outcast Explorer",
                "activeStatus": "Active",
                "verificationStatus": "VerifiedMultiSource"
              }],
              "aliases": [
                {
                  "catalogItemId": "BAD-00002",
                  "locale": "en",
                  "text": "Outcast Explorer",
                  "nameKind": "Display",
                  "isPreferred": true
                },
                {
                  "catalogItemId": "BAD-00002",
                  "locale": "en",
                  "text": "Outcast Tour Guide",
                  "nameKind": "LogReceipt",
                  "isPreferred": false
                }
              ],
              "enhancementSets": [],
              "badges": [{
                "catalogItemId": "BAD-00002",
                "homecomingSourceId": "OutcastExplorer",
                "setTitleId": 1518,
                "canonicalCategory": "Exploration",
                "badgeType": 1,
                "referenceKind": "ExplorationBadge",
                "heroName": "Outcast Explorer",
                "villainName": "Outcast Tour Guide",
                "verificationStatus": "VerifiedMultiSource"
              }],
              "zones": [],
              "badgeLocations": [],
              "badgeAccoladeRequirements": []
            }
            """);

        var resolver = new BadgeAcquisitionResolver(catalog);
        var result = resolver.Resolve("Outcast Tour Guide");

        Assert.Equal(AcquisitionIdentityResolutionState.Resolved, result.ResolutionState);
        Assert.Equal("BAD-00002", result.ResolvedCatalogItemId);
    }

    [Fact]
    public void Unknown_title_is_not_guessed()
    {
        var catalog = LoadCatalog("""
            {
              "manifest": {
                "catalogVersion": "item-ref-test",
                "homecomingCompatibility": { "buildMin": "Issue 28", "buildMax": "Issue 28" }
              },
              "items": [],
              "aliases": [],
              "enhancementSets": [],
              "badges": [],
              "zones": [],
              "badgeLocations": [],
              "badgeAccoladeRequirements": []
            }
            """);

        var resolver = new BadgeAcquisitionResolver(catalog);
        var result = resolver.Resolve("Totally Unknown Badge Title");

        Assert.Equal(AcquisitionIdentityResolutionState.Unresolved, result.ResolutionState);
        Assert.Null(result.ResolvedCatalogItemId);
    }

    [Fact]
    public void Production_exploration_history_and_accolade_titles_resolve()
    {
        var catalog = ItemReferenceCatalogFactory.LoadEmbeddedProduction();
        var resolver = new BadgeAcquisitionResolver(catalog);

        Assert.Equal(AcquisitionIdentityResolutionState.Resolved, resolver.Resolve("Atlas Tour Guide").ResolutionState);
        Assert.Equal(AcquisitionIdentityResolutionState.Resolved, resolver.Resolve("Bicentennial").ResolutionState);
        Assert.Equal(AcquisitionIdentityResolutionState.Resolved, resolver.Resolve("Master Plumber").ResolutionState);
        Assert.Equal(AcquisitionIdentityResolutionState.Resolved, resolver.Resolve("Nutrient-Rich").ResolutionState);
    }

    [Fact]
    public void Zig_Warden_resolves_to_canonical_badge_id()
    {
        var catalog = ItemReferenceCatalogFactory.LoadEmbeddedProduction();
        var resolver = new BadgeAcquisitionResolver(catalog);
        var result = resolver.Resolve("Zig Warden");

        Assert.Equal(AcquisitionIdentityResolutionState.Resolved, result.ResolutionState);
        Assert.Equal("BAD-01994", result.ResolvedCatalogItemId);
    }

    [Fact]
    public void Enduring_and_Genuine_veteran_receipts_still_resolve()
    {
        var catalog = ItemReferenceCatalogFactory.LoadEmbeddedProduction();
        var resolver = new BadgeAcquisitionResolver(catalog);

        Assert.Equal("BAD-03404", resolver.Resolve("Enduring").ResolvedCatalogItemId);
        Assert.Equal("BAD-03405", resolver.Resolve("Genuine").ResolvedCatalogItemId);
    }

    [Fact]
    public void Villain_gender_variant_receipts_resolve_to_shared_badge_id()
    {
        var catalog = ItemReferenceCatalogFactory.LoadEmbeddedProduction();
        var resolver = new BadgeAcquisitionResolver(catalog);

        Assert.Equal("BAD-01994", resolver.Resolve("King of the Zig").ResolvedCatalogItemId);
        Assert.Equal("BAD-01994", resolver.Resolve("Queen of the Zig").ResolvedCatalogItemId);
    }

    [Fact]
    public void Defiler_receipt_resolves_to_purifier_identity_without_changing_observed_title()
    {
        var catalog = ItemReferenceCatalogFactory.LoadEmbeddedProduction();
        var result = new BadgeAcquisitionResolver(catalog).Resolve("Defiler");

        Assert.Equal(AcquisitionIdentityResolutionState.Resolved, result.ResolutionState);
        Assert.Equal("BAD-03191", result.ResolvedCatalogItemId);
        Assert.Equal("Defiler", result.ObservedBadgeTitle);
    }
}
