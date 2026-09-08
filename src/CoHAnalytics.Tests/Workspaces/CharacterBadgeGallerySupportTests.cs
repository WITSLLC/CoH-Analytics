using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CoHAnalytics.Homecoming;
using CoHAnalytics.Models;
using CoHAnalytics.ReferenceData;
using CoHAnalytics.Services;
using CoHAnalytics.ViewModels.Workspaces;

namespace CoHAnalytics.Tests.Workspaces;

public sealed class CharacterBadgeGallerySupportTests
{
    private const string GalleryFixtureJson =
        """
        {
          "manifest": {
            "catalogVersion": "item-ref-badge-gallery-test",
            "homecomingCompatibility": { "buildMin": "Issue 28", "buildMax": "Issue 28" }
          },
          "items": [
            {
              "catalogItemId": "BAD-91001",
              "family": "Badge",
              "subtype": "Accolades",
              "currentDisplayName": "Atlas Tour Guide",
              "activeStatus": "Active",
              "verificationStatus": "VerifiedMultiSource",
              "icon": "Badge_HeroExploreAccolade"
            },
            {
              "catalogItemId": "BAD-91002",
              "family": "Badge",
              "subtype": "Exploration",
              "currentDisplayName": "Alpha Badge",
              "activeStatus": "Active",
              "verificationStatus": "VerifiedMultiSource",
              "icon": "badge_tourist_01"
            },
            {
              "catalogItemId": "BAD-91003",
              "family": "Badge",
              "subtype": "Achievement",
              "currentDisplayName": "Achievement Badge",
              "activeStatus": "Active",
              "verificationStatus": "VerifiedMultiSource",
              "icon": "badge_achievement_test"
            },
            {
              "catalogItemId": "BAD-91004",
              "family": "Badge",
              "subtype": "Exploration",
              "currentDisplayName": "Wide Badge",
              "activeStatus": "Active",
              "verificationStatus": "VerifiedMultiSource",
              "icon": "Badge_HeroExploreAccolade"
            },
            {
              "catalogItemId": "BAD-91005",
              "family": "Badge",
              "subtype": "Exploration",
              "currentDisplayName": "Tourism Badge",
              "activeStatus": "Active",
              "verificationStatus": "VerifiedMultiSource",
              "icon": "badge_tourism_test"
            }
          ],
          "aliases": [],
          "enhancementSets": [],
          "badges": [
            {
              "catalogItemId": "BAD-91001",
              "homecomingSourceId": "AtlasParkExplorer",
              "canonicalCategory": "Accolades",
              "badgeType": 5,
              "referenceKind": "Accolade",
              "heroName": "Atlas Tour Guide",
              "villainName": "Atlas Tour Guide",
              "heroIcon": "Badge_HeroExploreAccolade",
              "verificationStatus": "VerifiedMultiSource"
            },
            {
              "catalogItemId": "BAD-91002",
              "homecomingSourceId": "AtlasParkTour1",
              "canonicalCategory": "Exploration",
              "badgeType": 1,
              "referenceKind": "ExplorationBadge",
              "heroName": "Alpha Badge",
              "villainName": "Alpha Badge",
              "heroIcon": "badge_tourist_01",
              "verificationStatus": "VerifiedMultiSource"
            },
            {
              "catalogItemId": "BAD-91003",
              "homecomingSourceId": "AchievementTest",
              "canonicalCategory": "ACHIEVEMENT",
              "badgeType": 2,
              "referenceKind": "Achievement",
              "heroName": "Achievement Badge",
              "villainName": "Achievement Badge",
              "heroIcon": "badge_achievement_test",
              "verificationStatus": "VerifiedMultiSource"
            },
            {
              "catalogItemId": "BAD-91004",
              "homecomingSourceId": "WideBadge",
              "canonicalCategory": "Exploration",
              "badgeType": 1,
              "referenceKind": "ExplorationBadge",
              "heroName": "Wide Badge",
              "villainName": "Wide Badge",
              "heroIcon": "Badge_HeroExploreAccolade",
              "verificationStatus": "VerifiedMultiSource"
            },
            {
              "catalogItemId": "BAD-91005",
              "homecomingSourceId": "TourismBadge",
              "canonicalCategory": "TOURISM",
              "badgeType": 1,
              "referenceKind": "ExplorationBadge",
              "heroName": "Tourism Badge",
              "villainName": "Tourism Badge",
              "heroIcon": "badge_tourism_test",
              "verificationStatus": "VerifiedMultiSource"
            }
          ],
          "zones": [],
          "badgeLocations": []
        }
        """;

    private readonly IItemReferenceCatalog _catalog = LoadCatalog(GalleryFixtureJson);

    [Fact]
    public void Build_reports_total_count_and_singular_label()
    {
        var result = CharacterBadgeGallerySupport.Build(
            ["BAD-91001"],
            _catalog,
            null);

        Assert.Equal(1, result.TotalCount);
        Assert.Equal("1 Badge Earned", result.CountLabel);
    }

    [Fact]
    public void Build_groups_acquired_badges_by_category_and_hides_empty_categories()
    {
        var result = CharacterBadgeGallerySupport.Build(
            ["BAD-91001", "BAD-91002", "BAD-91003"],
            _catalog,
            null);

        Assert.Equal(3, result.TotalCount);
        Assert.Equal(3, result.Categories.Count);
        Assert.Equal("Accolades", result.Categories[0].CategoryName);
        Assert.Equal("Exploration", result.Categories[1].CategoryName);
        Assert.Equal("Achievement", result.Categories[2].CategoryName);
        Assert.Single(result.Categories[0].Badges);
        Assert.Single(result.Categories[1].Badges);
        Assert.Equal("Alpha Badge", result.Categories[1].Badges[0].DisplayName);
    }

    [Fact]
    public void Build_places_unknown_catalog_badges_in_other_category()
    {
        var result = CharacterBadgeGallerySupport.Build(
            ["BAD-UNKNOWN"],
            _catalog,
            null);

        Assert.Single(result.Categories);
        Assert.Equal("Other", result.Categories[0].CategoryName);
        Assert.Equal("BAD-UNKNOWN", result.Categories[0].Badges[0].DisplayName);
        Assert.Equal(1, result.OtherCategoryCount);
    }

    [Fact]
    public void Build_maps_unrecognized_canonical_category_to_other()
    {
        var result = CharacterBadgeGallerySupport.Build(
            ["BAD-91005"],
            _catalog,
            null);

        Assert.Single(result.Categories);
        Assert.Equal("Other", result.Categories[0].CategoryName);
        Assert.Equal("Tourism Badge", result.Categories[0].Badges[0].DisplayName);
        Assert.Equal(1, result.OtherCategoryCount);
    }

    [Fact]
    public void Build_resolves_badge_artwork_through_installed_game_asset_provider()
    {
        var assets = new FakeInstalledGameAssetProvider();
        var result = CharacterBadgeGallerySupport.Build(
            ["BAD-91002"],
            _catalog,
            assets);

        Assert.NotNull(result.Categories[0].Badges[0].IconSource);
        Assert.Contains("badge_tourist_01", assets.ResolvedIdentities, StringComparer.Ordinal);
    }

    [Fact]
    public void Build_marks_accolade_artwork_as_wide()
    {
        var result = CharacterBadgeGallerySupport.Build(
            ["BAD-91004"],
            _catalog,
            null);

        Assert.True(result.Categories[0].Badges[0].UseWideBadgeIcon);
    }

    [Fact]
    public void Build_sorts_badges_alphabetically_within_category()
    {
        var result = CharacterBadgeGallerySupport.Build(
            ["BAD-91002", "BAD-91004"],
            _catalog,
            null);

        var exploration = result.Categories.Single(group => group.CategoryName == "Exploration");
        Assert.Equal("Alpha Badge", exploration.Badges[0].DisplayName);
        Assert.Equal("Wide Badge", exploration.Badges[1].DisplayName);
    }

    [Fact]
    public void Build_sorts_veteran_badges_by_canonical_level_progression()
    {
        var catalog = ItemReferenceCatalogFactory.LoadEmbeddedProduction();
        var result = CharacterBadgeGallerySupport.Build(
            ["BAD-03406", "BAD-03404", "BAD-03405"],
            catalog,
            null);

        var veteran = result.Categories.Single(group => group.CategoryName == "Veteran");
        Assert.Collection(
            veteran.Badges,
            badge => Assert.Equal("Enduring", badge.DisplayName),
            badge => Assert.Equal("Genuine", badge.DisplayName),
            badge => Assert.Equal("Distinguished", badge.DisplayName));
    }

    [Theory]
    [InlineData("Defiler")]
    [InlineData("Purifier")]
    public void Build_uses_exact_explicit_log_receipt_title(string observedTitle)
    {
        var catalog = ItemReferenceCatalogFactory.LoadEmbeddedProduction();
        var result = CharacterBadgeGallerySupport.Build(
            [CreateAcquisition("BAD-03191", observedTitle, CharacterBadgeAcquisitionProvenance.LogReceipt)],
            catalog,
            null);

        Assert.Equal(observedTitle, Assert.Single(result.Categories).Badges.Single().DisplayName);
    }

    [Theory]
    [InlineData(CharacterBadgeAcquisitionProvenance.BuildSourceId)]
    [InlineData(CharacterBadgeAcquisitionProvenance.LegacyUnknown)]
    public void Build_uses_neutral_name_when_actual_awarded_title_is_unproven(
        CharacterBadgeAcquisitionProvenance provenance)
    {
        var catalog = ItemReferenceCatalogFactory.LoadEmbeddedProduction();
        var result = CharacterBadgeGallerySupport.Build(
            [CreateAcquisition("BAD-03191", "Purifier", provenance)],
            catalog,
            null);

        Assert.Equal(
            "Purifier / Defiler",
            Assert.Single(result.Categories).Badges.Single().DisplayName);
    }

    private static CharacterBadgeAcquisitionEntry CreateAcquisition(
        string badgeId,
        string observedTitle,
        CharacterBadgeAcquisitionProvenance provenance) =>
        new()
        {
            CatalogItemId = badgeId,
            FirstObservedAt = new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero),
            ObservedTitle = observedTitle,
            Provenance = provenance
        };

    private static IItemReferenceCatalog LoadCatalog(string json)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var result = ItemReferenceCatalogLoader.Load(stream);
        Assert.True(result.Succeeded, result.FailureReason);
        return ItemReferenceCatalog.FromLoadResult(result);
    }

    private sealed class FakeInstalledGameAssetProvider : IInstalledGameAssetProvider
    {
        public List<string> ResolvedIdentities { get; } = [];

        public ImageSource? TryResolve(string? iconIdentity)
        {
            if (string.IsNullOrWhiteSpace(iconIdentity))
            {
                return null;
            }

            ResolvedIdentities.Add(iconIdentity);
            var image = new WriteableBitmap(1, 1, 96, 96, PixelFormats.Bgra32, null);
            image.Freeze();
            return image;
        }
    }
}
