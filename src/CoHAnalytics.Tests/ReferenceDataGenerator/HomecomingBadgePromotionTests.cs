using System.Text.Json;
using CoHAnalytics.ReferenceData;
using CoHAnalytics.ReferenceDataGenerator;

namespace CoHAnalytics.Tests.ReferenceDataGenerator;

[Trait("Category", PrivateResearchTestEnvironment.Category)]
public sealed class HomecomingBadgePromotionSupportTests
{
    [Fact]
    public void CreateZoneId_NormalizesDisplayName()
    {
        Assert.Equal("zone-atlas-park", HomecomingBadgePromotionSupport.CreateZoneId("Atlas Park"));
        Assert.Equal("zone-founders-falls", HomecomingBadgePromotionSupport.CreateZoneId("Founders' Falls"));
    }

    [Fact]
    public void Build_AssignsDeterministicBadgeDocumentsFromCandidates()
    {
        var candidates = new[]
        {
            new HomecomingBadgeCandidateRecord(
                "BAD-00001",
                "AtlasParkTour1",
                86,
                1,
                "DEFS/BADGES/BADGES_TOURISM.DEF",
                "TOURISM",
                "P_HERO",
                "Atlas Tour Guide",
                "P_VILLAIN",
                "Atlas Tour Guide",
                null,
                null,
                null,
                null,
                "badge_tourist_01",
                null,
                nameof(HomecomingBadgeMatchStatus.NewFromHomecoming),
                [])
        };

        var research = BadgeResearchPackageLoader.Load(GetFixtureResearchRoot());
        var artifacts = HomecomingBadgePromotionSupport.Build(candidates, research);

        var badge = Assert.Single(artifacts.Badges);
        Assert.Equal("BAD-00001", badge.CatalogItemId);
        Assert.Equal("AtlasParkTour1", badge.HomecomingSourceId);
        Assert.Equal(nameof(ReferenceBadgeKind.ExplorationBadge), badge.ReferenceKind);
        Assert.Equal("BAD-00001", Assert.Single(artifacts.Items).CatalogItemId);
    }

    [Fact]
    public void Build_DoesNotAssignExplorationCompletionToMultiZoneTourismMembers()
    {
        var candidates = new[]
        {
            new HomecomingBadgeCandidateRecord(
                "BAD-00010",
                "MissionArchitectTourism",
                1080,
                1,
                "DEFS/BADGES/BADGES_TOURISM.DEF",
                "TOURISM",
                "P_HERO",
                "Thrill Seeker",
                "P_VILLAIN",
                "Thrill Seeker",
                null,
                null,
                null,
                null,
                "badge_tourist_01",
                null,
                nameof(HomecomingBadgeMatchStatus.NewFromHomecoming),
                []),
            new HomecomingBadgeCandidateRecord(
                "BAD-00011",
                "BrickstownExplorer",
                1571,
                5,
                "DEFS/BADGES/BADGES_ACCOLADE.DEF",
                "ACCOLADE",
                "P_HERO",
                "Zig Warden",
                "P_VILLAIN",
                "Zig Warden",
                null,
                null,
                null,
                null,
                "Badge_HeroExploreAccolade",
                null,
                nameof(HomecomingBadgeMatchStatus.NewFromHomecoming),
                [])
        };

        var research = BadgeResearchPackageLoader.Load(GetFixtureResearchRoot());
        var artifacts = HomecomingBadgePromotionSupport.Build(candidates, research);
        var thrillSeeker = artifacts.Badges.Single(badge => badge.CatalogItemId == "BAD-00010");
        Assert.Null(thrillSeeker.CompletionBadgeId);
        Assert.Equal(nameof(ReferenceBadgeKind.ArchitectEntertainment), thrillSeeker.ReferenceKind);
        Assert.Equal("Architect Entertainment", thrillSeeker.CanonicalCategory);
    }

    [Fact]
    public void Build_AddsHeroReceiptAliasWhenDisplayNameCombinesHeroAndVillainTitles()
    {
        var candidates = new[]
        {
            new HomecomingBadgeCandidateRecord(
                "BAD-01994",
                "BrickstownExplorer",
                1571,
                5,
                "DEFS/BADGES/BADGES_ACCOLADE.DEF",
                "ACCOLADE",
                "P_HERO",
                "Zig Warden",
                "P_VILLAIN",
                "{Hero.gender=male King|Queen} of the Zig",
                null,
                null,
                null,
                null,
                "Badge_HeroExploreAccolade",
                null,
                nameof(HomecomingBadgeMatchStatus.MatchedExisting),
                [])
        };

        var research = BadgeResearchPackageLoader.Load(GetFixtureResearchRoot());
        var artifacts = HomecomingBadgePromotionSupport.Build(candidates, research);
        var audit = HomecomingBadgePromotionSupport.AuditLogReceiptAliasCoverage(
            artifacts.Items,
            artifacts.Badges,
            research);

        Assert.Contains(
            artifacts.Aliases,
            alias => alias.CatalogItemId == "BAD-01994"
                && alias.Text == "Zig Warden"
                && string.Equals(alias.NameKind, nameof(ReferenceAliasNameKind.LogReceipt), StringComparison.Ordinal));
        Assert.Contains(
            artifacts.Aliases,
            alias => alias.CatalogItemId == "BAD-01994"
                && alias.Text == "King of the Zig");
        Assert.Contains(
            artifacts.Aliases,
            alias => alias.CatalogItemId == "BAD-01994"
                && alias.Text == "Queen of the Zig");
        Assert.Empty(audit.MissingSafeReceiptAliases);
    }

    [Fact]
    public void Build_DoesNotAddAmbiguousReceiptAliasWhenHeroTitleCollides()
    {
        var candidates = new[]
        {
            new HomecomingBadgeCandidateRecord(
                "BAD-00001",
                "AtlasParkTour1",
                86,
                1,
                "DEFS/BADGES/BADGES_TOURISM.DEF",
                "TOURISM",
                "P_HERO",
                "Shared Title",
                "P_VILLAIN",
                "Shared Title",
                null,
                null,
                null,
                null,
                "badge_tourist_01",
                null,
                nameof(HomecomingBadgeMatchStatus.NewFromHomecoming),
                []),
            new HomecomingBadgeCandidateRecord(
                "BAD-00002",
                "AtlasParkTour2",
                87,
                1,
                "DEFS/BADGES/BADGES_TOURISM.DEF",
                "TOURISM",
                "P_HERO",
                "Shared Title",
                "P_VILLAIN",
                "Shared Title",
                null,
                null,
                null,
                null,
                "badge_tourist_01",
                null,
                nameof(HomecomingBadgeMatchStatus.NewFromHomecoming),
                [])
        };

        var research = BadgeResearchPackageLoader.Load(GetFixtureResearchRoot());
        var artifacts = HomecomingBadgePromotionSupport.Build(candidates, research);

        Assert.DoesNotContain(
            artifacts.Aliases,
            alias => string.Equals(alias.Text, "Shared Title", StringComparison.Ordinal));
    }

    [Fact]
    public void Production_catalog_has_no_missing_safe_log_receipt_aliases()
    {
        var catalogPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "CoHAnalytics", "ReferenceData", "item-catalog.v1.json"));
        var research = BadgeResearchPackageLoader.Load(GetFixtureResearchRoot());
        var document = JsonSerializer.Deserialize<ItemReferenceCatalogDocument>(
            File.ReadAllBytes(catalogPath),
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            })
            ?? throw new InvalidOperationException("Catalog document is empty.");

        var candidates = document.Badges
            .Where(badge =>
                !string.IsNullOrWhiteSpace(badge.CatalogItemId)
                && !string.IsNullOrWhiteSpace(badge.HomecomingSourceId))
            .Select(badge => new HomecomingBadgeCandidateRecord(
                badge.CatalogItemId,
                badge.HomecomingSourceId!,
                badge.SetTitleId ?? 0,
                badge.BadgeType ?? 0,
                "catalog-audit",
                badge.CanonicalCategory ?? nameof(ReferenceBadgeKind.Other),
                "catalog",
                badge.HeroName ?? string.Empty,
                "catalog",
                badge.VillainName ?? badge.HeroName ?? string.Empty,
                null,
                badge.HeroDescription,
                null,
                badge.VillainDescription,
                badge.HeroIcon,
                badge.VillainIcon,
                nameof(HomecomingBadgeMatchStatus.MatchedExisting),
                []))
            .ToArray();

        var artifacts = HomecomingBadgePromotionSupport.Build(candidates, research);
        var audit = HomecomingBadgePromotionSupport.AuditLogReceiptAliasCoverage(
            artifacts.Items,
            artifacts.Badges,
            research);

        Assert.Empty(audit.MissingSafeReceiptAliases);
        Assert.True(audit.AlreadyResolvableByPreferredReceipt > 0);
        Assert.True(audit.SafeAliasesAdded > audit.BadgesAudited);
    }

    private static string GetFixtureResearchRoot() => PrivateResearchTestEnvironment.RequireResearchRoot();
}

[Trait("Category", PrivateResearchTestEnvironment.Category)]
[Collection(LiveInstallPromotionCollection.Name)]
public sealed class HomecomingBadgePromotionCommandTests
{
    [Fact]
    [Trait("Category", "LiveInstall")]
    public void LiveInstall_Promotion_PreservesNewerManifestAndStableIds()
    {
        var catalogPath = Path.Combine(Path.GetTempPath(), $"coh-badge-promote-{Guid.NewGuid():N}.json");
        try
        {
            using (var embedded = typeof(ItemReferenceCatalogFactory).Assembly.GetManifestResourceStream(
                       ItemReferenceCatalogFactory.ProductionCatalogResourceName)
                   ?? throw new InvalidOperationException("Embedded production catalog missing."))
            using (var output = File.Create(catalogPath))
            {
                embedded.CopyTo(output);
            }

            PromotionManifestOwnershipTestSupport.WriteFutureManifest(catalogPath);
            var beforeStableIds = PromotionManifestOwnershipTestSupport.ReadStableIds(catalogPath, "Badge");

            var result = HomecomingBadgePromotionCommand.Promote(
                LiveInstallTestEnvironment.InstallRoot,
                catalogPath,
                GetFixtureResearchRoot());

            Assert.Equal(result.CatalogSha256, result.DeterminismSha256);
            Assert.True(result.Stats.BadgesPromoted > 0);
            PromotionManifestOwnershipTestSupport.AssertFutureManifestPreserved(catalogPath);
            Assert.Equal(
                beforeStableIds,
                PromotionManifestOwnershipTestSupport.ReadStableIds(catalogPath, "Badge"));
        }
        finally
        {
            if (File.Exists(catalogPath))
            {
                File.Delete(catalogPath);
            }
        }
    }

    private static string GetFixtureResearchRoot() => PrivateResearchTestEnvironment.RequireResearchRoot();
}
