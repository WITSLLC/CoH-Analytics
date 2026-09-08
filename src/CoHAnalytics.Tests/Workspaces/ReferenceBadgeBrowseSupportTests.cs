using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CoHAnalytics.Homecoming;
using CoHAnalytics.Models;
using CoHAnalytics.Orchestration.Contracts;
using CoHAnalytics.ReferenceData;
using CoHAnalytics.Services;
using CoHAnalytics.Tests.Services;
using CoHAnalytics.ViewModels.Workspaces;

namespace CoHAnalytics.Tests.Workspaces;

public sealed class ReferenceBadgeBrowseSupportTests
{
    [Fact]
    public void Production_reference_uses_neutral_alternate_name_and_canonical_acquired_state()
    {
        var catalog = ItemReferenceCatalogFactory.LoadEmbeddedProduction();
        var tree = ReferenceBadgeBrowseSupport.BuildBrowseTree(
            catalog,
            new HashSet<string>(["BAD-03191"], StringComparer.Ordinal));
        var nodes = tree.NodesByKey.Values
            .Where(node => node.BadgeId == "BAD-03191")
            .ToArray();

        Assert.NotEmpty(nodes);
        Assert.All(nodes, node => Assert.Equal("Purifier / Defiler", node.DisplayName));
        Assert.All(nodes, node => Assert.True(node.IsAcquired));
    }

    private const string ExplorationFixtureJson =
        """
        {
          "manifest": {
            "catalogVersion": "item-ref-badge-exploration-test",
            "homecomingCompatibility": { "buildMin": "Issue 28", "buildMax": "Issue 28" }
          },
          "items": [
            {
              "catalogItemId": "BAD-90001",
              "family": "Badge",
              "subtype": "Accolades",
              "currentDisplayName": "Atlas Tour Guide",
              "activeStatus": "Active",
              "verificationStatus": "VerifiedMultiSource",
              "icon": "Badge_HeroExploreAccolade"
            },
            {
              "catalogItemId": "BAD-90002",
              "family": "Badge",
              "subtype": "Exploration",
              "currentDisplayName": "Zebra Badge",
              "activeStatus": "Active",
              "verificationStatus": "VerifiedMultiSource",
              "icon": "badge_tourist_01"
            },
            {
              "catalogItemId": "BAD-90003",
              "family": "Badge",
              "subtype": "Exploration",
              "currentDisplayName": "Alpha Badge",
              "activeStatus": "Active",
              "verificationStatus": "VerifiedMultiSource",
              "icon": "badge_tourist_01"
            },
            {
              "catalogItemId": "BAD-90004",
              "family": "Badge",
              "subtype": "Exploration",
              "currentDisplayName": "Lucid Dreamer",
              "activeStatus": "Active",
              "verificationStatus": "VerifiedMultiSource",
              "icon": "badge_i28_tourism_labyrinth"
            },
            {
              "catalogItemId": "BAD-90005",
              "family": "Badge",
              "subtype": "Exploration",
              "currentDisplayName": "Wide Badge",
              "activeStatus": "Active",
              "verificationStatus": "VerifiedMultiSource",
              "icon": "badge_wide_test"
            },
            {
              "catalogItemId": "BAD-90010",
              "family": "Badge",
              "subtype": "Accolades",
              "currentDisplayName": "No Reward Accolade",
              "activeStatus": "Active",
              "verificationStatus": "VerifiedMultiSource",
              "icon": "badge_atlas_set_01"
            },
            {
              "catalogItemId": "BAD-90011",
              "family": "Badge",
              "subtype": "Accolades",
              "currentDisplayName": "Choice Accolade",
              "activeStatus": "Active",
              "verificationStatus": "VerifiedMultiSource",
              "icon": "badge_atlas_set_01"
            }
          ],
          "aliases": [],
          "enhancementSets": [],
          "badges": [
            {
              "catalogItemId": "BAD-90001",
              "homecomingSourceId": "AtlasParkExplorer",
              "canonicalCategory": "Accolades",
              "badgeType": 5,
              "referenceKind": "Accolade",
              "heroName": "Atlas Tour Guide",
              "villainName": "Atlas Tour Guide",
              "heroIcon": "Badge_HeroExploreAccolade",
              "rewardText": "5 Reward Merits",
              "heroDescription": "Exploration completion Accolade for Atlas Park.",
              "verificationStatus": "VerifiedMultiSource",
              "requirementLogicStatus": "Verified",
              "requirementLogicPattern": "And"
            },
            {
              "catalogItemId": "BAD-90002",
              "homecomingSourceId": "AtlasParkTour2",
              "canonicalCategory": "Exploration",
              "badgeType": 1,
              "referenceKind": "ExplorationBadge",
              "heroName": "Zebra Badge",
              "villainName": "Zebra Badge",
              "heroIcon": "badge_tourist_01",
              "completionBadgeId": "BAD-90001",
              "verificationStatus": "VerifiedMultiSource"
            },
            {
              "catalogItemId": "BAD-90003",
              "homecomingSourceId": "AtlasParkTour1",
              "canonicalCategory": "Exploration",
              "badgeType": 1,
              "referenceKind": "ExplorationBadge",
              "heroName": "Alpha Badge",
              "villainName": "Alpha Badge",
              "heroIcon": "badge_tourist_01",
              "completionBadgeId": "BAD-90001",
              "verificationStatus": "VerifiedMultiSource"
            },
            {
              "catalogItemId": "BAD-90004",
              "homecomingSourceId": "LucidDreamer",
              "canonicalCategory": "Exploration",
              "badgeType": 1,
              "referenceKind": "ExplorationBadge",
              "heroName": "Lucid Dreamer",
              "villainName": "Lucid Dreamer",
              "heroIcon": "badge_i28_tourism_labyrinth",
              "completionBadgeId": "BAD-90001",
              "verificationStatus": "VerifiedMultiSource"
            },
            {
              "catalogItemId": "BAD-90005",
              "homecomingSourceId": "WideTestZoneTour1",
              "canonicalCategory": "Exploration",
              "badgeType": 1,
              "referenceKind": "ExplorationBadge",
              "heroName": "Wide Badge",
              "villainName": "Wide Badge",
              "heroIcon": "badge_wide_test",
              "verificationStatus": "VerifiedMultiSource"
            },
            {
              "catalogItemId": "BAD-90010",
              "homecomingSourceId": "NoRewardAccolade",
              "canonicalCategory": "Accolades",
              "badgeType": 5,
              "referenceKind": "Accolade",
              "heroName": "No Reward Accolade",
              "villainName": "No Reward Accolade",
              "heroIcon": "badge_atlas_set_01",
              "heroDescription": "Accolade without a promoted reward.",
              "verificationStatus": "VerifiedMultiSource",
              "requirementLogicStatus": "Verified",
              "requirementLogicPattern": "TextOnly",
              "requirementText": "Complete the promoted text-only requirement."
            },
            {
              "catalogItemId": "BAD-90011",
              "homecomingSourceId": "ChoiceAccolade",
              "canonicalCategory": "Accolades",
              "badgeType": 5,
              "referenceKind": "Accolade",
              "heroName": "Choice Accolade",
              "villainName": "Choice Accolade",
              "heroIcon": "badge_atlas_set_01",
              "heroDescription": "Accolade with promoted OR requirements.",
              "verificationStatus": "VerifiedMultiSource",
              "requirementLogicStatus": "Verified",
              "requirementLogicPattern": "Or"
            }
          ],
          "zones": [
            {
              "zoneId": "zone-atlas-park",
              "displayName": "Atlas Park",
              "explorationCompletionBadgeIds": ["BAD-90001"]
            },
            {
              "zoneId": "zone-wide-test",
              "displayName": "Wide Test Zone",
              "explorationCompletionBadgeIds": []
            }
          ],
          "badgeLocations": [
            {
              "badgeCatalogItemId": "BAD-90003",
              "zoneId": "zone-atlas-park",
              "coordinateX": 128.5,
              "coordinateY": 16.4,
              "coordinateZ": -233,
              "thumbtackCommand": "/thumbtack 128.5 16.4 -233",
              "markerType": "ExplorationBadge",
              "locationRole": "ExplorationMarker",
              "coordinateSemantics": "SurveyedMarkerCenter",
              "verificationStatus": "VerifiedMultiSource",
              "locationIndex": 0,
              "explorationRouteOrder": 2,
              "routeSourceProject": "Optimal badge/plaque collection path maps and popmenu",
              "routeSourceVersion": "Map package 1.2.1 / popmenu 20250708.01",
              "routeMappingConfidence": "Exact",
              "triggerDescription": "Alpha trigger"
            },
            {
              "badgeCatalogItemId": "BAD-90002",
              "zoneId": "zone-atlas-park",
              "coordinateX": 10,
              "coordinateY": 20,
              "coordinateZ": 30,
              "thumbtackCommand": "/thumbtack 10 20 30",
              "markerType": "ExplorationBadge",
              "locationRole": "ExplorationMarker",
              "coordinateSemantics": "SurveyedMarkerCenter",
              "verificationStatus": "VerifiedMultiSource",
              "locationIndex": 0,
              "explorationRouteOrder": 3,
              "routeSourceProject": "Optimal badge/plaque collection path maps and popmenu",
              "routeSourceVersion": "Map package 1.2.1 / popmenu 20250708.01",
              "routeMappingConfidence": "Exact"
            },
            {
              "badgeCatalogItemId": "BAD-90004",
              "zoneId": "zone-atlas-park",
              "thumbtackCommand": "N/A — no fixed coordinate",
              "markerType": "ExplorationBadge",
              "locationRole": "ExplorationMarker",
              "coordinateSemantics": "SurveyedMarkerCenter",
              "verificationStatus": "VerifiedMultiSource",
              "locationIndex": 0,
              "triggerDescription": "Entry trigger only"
            },
            {
              "badgeCatalogItemId": "BAD-90005",
              "zoneId": "zone-wide-test",
              "coordinateX": 1,
              "coordinateY": 2,
              "coordinateZ": 3,
              "thumbtackCommand": "/thumbtack 1 2 3",
              "markerType": "ExplorationBadge",
              "locationRole": "ExplorationMarker",
              "coordinateSemantics": "SurveyedMarkerCenter",
              "verificationStatus": "VerifiedMultiSource",
              "locationIndex": 0
            }
          ],
          "badgeAccoladeRequirements": [
            {
              "accoladeBadgeId": "BAD-90001",
              "prerequisiteBadgeId": "BAD-90003",
              "prerequisiteIndex": 0,
              "logicGroup": 0,
              "requirementLogicStatus": "Verified"
            },
            {
              "accoladeBadgeId": "BAD-90001",
              "prerequisiteBadgeId": "BAD-90002",
              "prerequisiteIndex": 1,
              "logicGroup": 0,
              "requirementLogicStatus": "Verified"
            },
            {
              "accoladeBadgeId": "BAD-90011",
              "prerequisiteBadgeId": "BAD-90003",
              "prerequisiteIndex": 0,
              "logicGroup": 0,
              "requirementLogicStatus": "Verified"
            },
            {
              "accoladeBadgeId": "BAD-90011",
              "prerequisiteBadgeId": "BAD-90002",
              "prerequisiteIndex": 1,
              "logicGroup": 0,
              "requirementLogicStatus": "Verified"
            }
          ]
        }
        """;

    private readonly IItemReferenceCatalog _fixtureCatalog = LoadCatalog(ExplorationFixtureJson);
    private readonly IItemReferenceCatalog _productionCatalog = ItemReferenceCatalogFactory.LoadEmbeddedProduction();

    [Fact]
    public void Badges_chip_is_enabled()
    {
        using var viewModel = CreateViewModel();
        var badges = viewModel.SectionChips.Single(chip => chip.SectionId == ReferenceSectionId.Badges);
        Assert.True(badges.IsEnabled);
    }

    [Fact]
    public void Exploration_root_exists()
    {
        var tree = ReferenceBadgeBrowseSupport.BuildBrowseTree(_fixtureCatalog);
        var root = tree.RootNodes.Single(node =>
            node.Kind == ReferenceEnhancementBrowseNodeKind.BadgeExplorationRoot);
        Assert.Equal("Exploration", root.DisplayName);
        Assert.Equal(ReferenceBadgeBrowseSupport.ExplorationRootNodeKey, root.NodeKey);
    }

    [Fact]
    public void History_plaques_root_exists()
    {
        var tree = ReferenceBadgeBrowseSupport.BuildBrowseTree(_fixtureCatalog);
        var root = tree.RootNodes.Single(node =>
            node.Kind == ReferenceEnhancementBrowseNodeKind.BadgeHistoryPlaquesRoot);
        Assert.Equal("History Plaques", root.DisplayName);
        Assert.Equal(ReferenceBadgeBrowseSupport.HistoryPlaquesRootNodeKey, root.NodeKey);
    }

    [Fact]
    public void Badge_browse_tree_exposes_exploration_history_and_accolade_roots()
    {
        var tree = ReferenceBadgeBrowseSupport.BuildBrowseTree(_fixtureCatalog);
        Assert.Equal(3, tree.RootNodes.Count);
        Assert.Contains(tree.RootNodes, node => node.NodeKey == ReferenceBadgeBrowseSupport.ExplorationRootNodeKey);
        Assert.Contains(tree.RootNodes, node => node.NodeKey == ReferenceBadgeBrowseSupport.HistoryPlaquesRootNodeKey);
        Assert.Contains(tree.RootNodes, node => node.NodeKey == ReferenceBadgeBrowseSupport.AccoladesRootNodeKey);
    }

    [Fact]
    public void Accolades_root_exists()
    {
        var tree = ReferenceBadgeBrowseSupport.BuildBrowseTree(_fixtureCatalog);
        var root = tree.RootNodes.Single(node =>
            node.Kind == ReferenceEnhancementBrowseNodeKind.BadgeAccoladesRoot);
        Assert.Equal("Accolades", root.DisplayName);
        Assert.Equal(ReferenceBadgeBrowseSupport.AccoladesRootNodeKey, root.NodeKey);
    }

    [Fact]
    public void Zones_are_built_from_promoted_badge_data()
    {
        var tree = ReferenceBadgeBrowseSupport.BuildBrowseTree(_fixtureCatalog);
        var zoneNodes = tree.NodesByKey.Values
            .Where(node => node.Kind == ReferenceEnhancementBrowseNodeKind.BadgeZone)
            .ToArray();

        Assert.Contains(zoneNodes, node => node.DisplayName == "Atlas Park");
        Assert.Contains(zoneNodes, node => node.DisplayName == "Wide Test Zone");
    }

    [Fact]
    public void Zone_completion_badge_appears_first()
    {
        var tree = ReferenceBadgeBrowseSupport.BuildBrowseTree(_fixtureCatalog);
        var atlasZone = tree.NodesByKey.Values.Single(node =>
            node.Kind == ReferenceEnhancementBrowseNodeKind.BadgeZone
            && node.DisplayName == "Atlas Park");

        var firstChild = atlasZone.Children[0];
        Assert.Equal(ReferenceEnhancementBrowseNodeKind.Badge, firstChild.Kind);
        Assert.Equal("BAD-90001", firstChild.BadgeId);
        Assert.Equal("Atlas Tour Guide", firstChild.DisplayName);
        Assert.True(firstChild.IsZoneCompletionBadge);
    }

    [Fact]
    public void Individual_exploration_badges_follow_completion_badge()
    {
        var tree = ReferenceBadgeBrowseSupport.BuildBrowseTree(_fixtureCatalog);
        var atlasZone = tree.NodesByKey.Values.Single(node =>
            node.Kind == ReferenceEnhancementBrowseNodeKind.BadgeZone
            && node.DisplayName == "Atlas Park");

        var memberNames = atlasZone.Children
            .Skip(1)
            .Select(child => child.DisplayName)
            .ToArray();

        Assert.Equal(["Alpha Badge", "Zebra Badge", "Lucid Dreamer"], memberNames);
    }

    [Fact]
    public void Route_covered_exploration_badges_follow_published_route_order()
    {
        var tree = ReferenceBadgeBrowseSupport.BuildBrowseTree(_fixtureCatalog);
        var atlasZone = tree.NodesByKey.Values.Single(node =>
            node.Kind == ReferenceEnhancementBrowseNodeKind.BadgeZone
            && node.DisplayName == "Atlas Park");

        var memberNames = atlasZone.Children
            .Skip(1)
            .Select(child => child.DisplayName)
            .ToArray();

        Assert.Equal(["Alpha Badge", "Zebra Badge", "Lucid Dreamer"], memberNames);
    }

    [Fact]
    public void Zone_without_route_data_uses_alphabetical_fallback()
    {
        var tree = ReferenceBadgeBrowseSupport.BuildBrowseTree(_fixtureCatalog);
        var wideZone = tree.NodesByKey.Values.Single(node =>
            node.Kind == ReferenceEnhancementBrowseNodeKind.BadgeZone
            && node.DisplayName == "Wide Test Zone");

        Assert.Equal("Wide Badge", Assert.Single(wideZone.Children).DisplayName);
    }

    [Fact]
    public void Individual_badges_sort_deterministically()
    {
        var first = ReferenceBadgeBrowseSupport.BuildBrowseTree(_fixtureCatalog);
        var second = ReferenceBadgeBrowseSupport.BuildBrowseTree(_fixtureCatalog);

        var firstNames = first.NodesByKey.Values
            .Where(node => node.Kind == ReferenceEnhancementBrowseNodeKind.BadgeZone
                && node.DisplayName == "Atlas Park")
            .Single()
            .Children
            .Skip(1)
            .Select(child => child.DisplayName)
            .ToArray();

        var secondNames = second.NodesByKey.Values
            .Where(node => node.Kind == ReferenceEnhancementBrowseNodeKind.BadgeZone
                && node.DisplayName == "Atlas Park")
            .Single()
            .Children
            .Skip(1)
            .Select(child => child.DisplayName)
            .ToArray();

        Assert.Equal(firstNames, secondNames);
    }

    [Fact]
    public void Thumbtack_command_matches_promoted_data()
    {
        var tree = ReferenceBadgeBrowseSupport.BuildBrowseTree(_fixtureCatalog);
        var alphaNode = tree.NodesByKey.Values.Single(node => node.BadgeId == "BAD-90003");
        Assert.Equal("/thumbtack 128.5 16.4 -233", alphaNode.SecondaryLine);
    }

    [Fact]
    public void Coordinate_null_badge_has_no_thumbtack()
    {
        var tree = ReferenceBadgeBrowseSupport.BuildBrowseTree(_fixtureCatalog);
        var nullNode = tree.NodesByKey.Values.Single(node => node.BadgeId == "BAD-90004");
        Assert.Null(nullNode.SecondaryLine);
    }

    [Fact]
    public void Lucid_dreamer_remains_coordinate_null_in_production_catalog()
    {
        Assert.True(_productionCatalog.IsLoaded, _productionCatalog.LoadFailureReason);
        Assert.True(_productionCatalog.TryGetBadgeById("BAD-02228", out var badge));
        Assert.Equal("Lucid Dreamer", badge.HeroName);

        var tree = ReferenceBadgeBrowseSupport.BuildBrowseTree(_productionCatalog);
        var lucidNode = tree.NodesByKey.Values.Single(node => node.BadgeId == "BAD-02228");
        Assert.Null(lucidNode.SecondaryLine);
    }

    [Fact]
    public void Acquired_badge_id_renders_completed_state()
    {
        var acquired = new HashSet<string>(StringComparer.Ordinal) { "BAD-90003" };
        var tree = ReferenceBadgeBrowseSupport.BuildBrowseTree(_fixtureCatalog, acquired);
        var alphaNode = tree.NodesByKey.Values.Single(node => node.BadgeId == "BAD-90003");
        Assert.True(alphaNode.IsAcquired);
    }

    [Fact]
    public void Unacquired_badge_id_renders_normal_state()
    {
        var tree = ReferenceBadgeBrowseSupport.BuildBrowseTree(_fixtureCatalog);
        var alphaNode = tree.NodesByKey.Values.Single(node => node.BadgeId == "BAD-90003");
        Assert.False(alphaNode.IsAcquired);
    }

    [Fact]
    public void Zone_completion_badge_is_not_inferred_from_child_completion()
    {
        var acquired = new HashSet<string>(StringComparer.Ordinal)
        {
            "BAD-90003",
            "BAD-90002",
            "BAD-90004"
        };

        var tree = ReferenceBadgeBrowseSupport.BuildBrowseTree(_fixtureCatalog, acquired);
        var completionNode = FindExplorationBadgeNode(tree, "BAD-90001", isZoneCompletion: true);
        Assert.False(completionNode.IsAcquired);
    }

    [Fact]
    public void Completion_is_isolated_per_character()
    {
        var repository = new CharacterBadgeAcquisitionRepository(
            new CharacterBadgeAcquisitionRepositoryOptions
            {
                DataDirectory = Path.Combine(Path.GetTempPath(), $"coh-badge-ref-{Guid.NewGuid():N}")
            });

        var characterA = CharacterRecordId.CreateNew();
        var characterB = CharacterRecordId.CreateNew();
        repository.RecordAcquisition(characterA, "acct", "BAD-90003", "Alpha Badge", DateTimeOffset.UtcNow);

        var viewed = new RecordingViewedContextService();
        using var viewModel = CreateViewModel(viewed, repository);
        viewModel.SelectSectionChipCommand.Execute(ReferenceSectionId.Badges);

        viewed.SetCharacter(characterA);
        var alphaForA = FindBadgeNode(viewModel, "BAD-90003");
        Assert.True(alphaForA.IsAcquired);

        viewed.SetCharacter(characterB);
        var alphaForB = FindBadgeNode(viewModel, "BAD-90003");
        Assert.False(alphaForB.IsAcquired);
    }

    [Fact]
    public void No_selected_character_still_allows_browsing()
    {
        var viewed = new RecordingViewedContextService();
        using var viewModel = CreateViewModel(viewed, repository: null);
        viewModel.SelectSectionChipCommand.Execute(ReferenceSectionId.Badges);

        Assert.NotEmpty(viewModel.TreeRootNodes);
        Assert.All(
            EnumerateBadgeNodes(viewModel),
            node => Assert.False(node.IsAcquired));
    }

    [Fact]
    public void Badge_artwork_resolves_through_installed_game_asset_provider()
    {
        var assets = new FakeInstalledGameAssetProvider();
        assets.Register("badge_tourist_01", CreateBitmap(64, 64));

        var tree = ReferenceBadgeBrowseSupport.BuildBrowseTree(_fixtureCatalog);
        var alphaBrowseNode = tree.NodesByKey.Values.Single(node => node.BadgeId == "BAD-90003");
        var treeNode = new ReferenceEnhancementTreeNodeViewModel(
            alphaBrowseNode,
            _fixtureCatalog,
            installedGameAssetProvider: assets);

        Assert.Contains("badge_tourist_01", assets.Requests);
        Assert.NotNull(treeNode.IconSource);
    }

    [Fact]
    public void Enhancement_icon_compositor_is_not_used_for_badges()
    {
        var compositor = new RecordingEnhancementIconCompositor();
        var assets = new FakeInstalledGameAssetProvider();
        assets.Register("badge_tourist_01", CreateBitmap(64, 64));

        var tree = ReferenceBadgeBrowseSupport.BuildBrowseTree(_fixtureCatalog);
        var alphaBrowseNode = tree.NodesByKey.Values.Single(node => node.BadgeId == "BAD-90003");
        _ = new ReferenceEnhancementTreeNodeViewModel(
            alphaBrowseNode,
            _fixtureCatalog,
            compositor,
            installedGameAssetProvider: assets);

        Assert.Empty(compositor.Requests);
    }

    [Fact]
    public void Wide_badge_artwork_preserves_aspect_ratio_in_detail_model()
    {
        var assets = new FakeInstalledGameAssetProvider();
        assets.Register("Badge_HeroExploreAccolade", CreateBitmap(128, 64));

        var tree = ReferenceBadgeBrowseSupport.BuildBrowseTree(_fixtureCatalog);
        var completionNode = FindExplorationBadgeNode(tree, "BAD-90001", isZoneCompletion: true);
        var detail = ReferenceBadgeBrowseSupport.BuildDetail(
            _fixtureCatalog,
            completionNode,
            acquiredBadgeIds: null,
            assets);

        Assert.True(detail.UseWideBadgeIcon);
        Assert.NotNull(detail.IconSource);
        Assert.True(detail.IconSource.Width > detail.IconSource.Height);
    }

    [Fact]
    public void Completion_badge_with_known_reward_exposes_reward_in_detail()
    {
        var tree = ReferenceBadgeBrowseSupport.BuildBrowseTree(_fixtureCatalog);
        var completionNode = FindExplorationBadgeNode(tree, "BAD-90001", isZoneCompletion: true);
        var detail = ReferenceBadgeBrowseSupport.BuildDetail(
            _fixtureCatalog,
            completionNode,
            acquiredBadgeIds: null,
            installedGameAssetProvider: null);

        Assert.True(detail.ShowReward);
        Assert.Equal("5 Reward Merits", detail.RewardText);
    }

    [Fact]
    public void Reward_information_remains_on_canonical_badge_identity()
    {
        Assert.True(_fixtureCatalog.TryGetBadgeById("BAD-90001", out var badge));
        Assert.Equal("5 Reward Merits", badge.RewardText);
        Assert.Equal("AtlasParkExplorer", badge.HomecomingSourceId);
    }

    [Fact]
    public void Badge_without_known_reward_does_not_fabricate_reward_in_detail()
    {
        var tree = ReferenceBadgeBrowseSupport.BuildBrowseTree(_fixtureCatalog);
        var memberNode = tree.NodesByKey.Values.Single(node => node.BadgeId == "BAD-90003");
        var detail = ReferenceBadgeBrowseSupport.BuildDetail(
            _fixtureCatalog,
            memberNode,
            acquiredBadgeIds: null,
            installedGameAssetProvider: null);

        Assert.False(detail.ShowReward);
        Assert.Null(detail.RewardText);
    }

    [Fact]
    public void Thumbtack_copy_produces_exact_displayed_command()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var viewModel = CreateViewModel();
                viewModel.SelectSectionChipCommand.Execute(ReferenceSectionId.Badges);
                viewModel.SelectedNode = FindBadgeNode(viewModel, "BAD-90003");

                const string command = "/thumbtack 128.5 16.4 -233";
                Assert.Equal(command, viewModel.Detail.ThumbtackCommand);
                viewModel.CopyThumbtackCommand.Execute(null);

                var clipboardText = TryGetClipboardText();
                if (clipboardText is not null)
                {
                    Assert.Equal(command, clipboardText);
                }
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "Thumbtack copy test timed out.");
        if (failure is not null)
        {
            throw failure;
        }
    }

    [Fact]
    public void Atlas_park_patriot_badge_thumbtack_copy_uses_production_detail_command()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var viewModel = CreateProductionViewModel();
                viewModel.SelectSectionChipCommand.Execute(ReferenceSectionId.Badges);

                var explorationRoot = viewModel.TreeRootNodes.Single(node =>
                    node.NodeKey == ReferenceBadgeBrowseSupport.ExplorationRootNodeKey);
                var atlasZone = explorationRoot.Children.Single(node => node.DisplayName == "Atlas Park");
                var patriotNode = atlasZone.Children.Single(node => node.BadgeId == "BAD-01922");
                viewModel.SelectedNode = patriotNode;

                const string command = "/thumbtack 164 -767.7 -673";
                Assert.Equal(command, viewModel.Detail.ThumbtackCommand);
                viewModel.CopyThumbtackCommand.Execute(null);

                var clipboardText = TryGetClipboardText();
                if (clipboardText is not null)
                {
                    Assert.Equal(command, clipboardText);
                }
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "Atlas Park thumbtack copy test timed out.");
        if (failure is not null)
        {
            throw failure;
        }
    }

    [Fact]
    public void Coordinate_null_badge_exposes_no_thumbtack_or_copy_affordance_in_detail()
    {
        var tree = ReferenceBadgeBrowseSupport.BuildBrowseTree(_fixtureCatalog);
        var nullNode = tree.NodesByKey.Values.Single(node => node.BadgeId == "BAD-90004");
        var detail = ReferenceBadgeBrowseSupport.BuildDetail(
            _fixtureCatalog,
            nullNode,
            acquiredBadgeIds: null,
            installedGameAssetProvider: null);

        Assert.False(detail.ShowThumbtack);
        Assert.Null(detail.ThumbtackCommand);
    }

    [Fact]
    public void Catalog_exposes_zones_and_badge_locations()
    {
        Assert.True(_fixtureCatalog.GetZones().ContainsKey("zone-atlas-park"));
        Assert.Single(_fixtureCatalog.GetBadgeLocations("BAD-90003"));
        Assert.Single(_fixtureCatalog.GetBadgeLocations("BAD-90004"));
    }

    [Fact]
    public void Production_history_plaque_collections_surface()
    {
        Assert.True(_productionCatalog.IsLoaded, _productionCatalog.LoadFailureReason);
        Assert.Equal(29, _productionCatalog.GetHistoryPlaqueRouteCollections().Count);

        var tree = ReferenceBadgeBrowseSupport.BuildBrowseTree(_productionCatalog);
        var historyRoot = FindHistoryPlaquesRoot(tree);
        Assert.Equal(2, historyRoot.Children.Count);
        Assert.Equal("By History Badge", historyRoot.Children[0].DisplayName);
        Assert.Equal("By Zone", historyRoot.Children[1].DisplayName);

        var byHistoryBadge = historyRoot.Children.Single(node =>
            node.NodeKey == ReferenceBadgeBrowseSupport.HistoryPlaquesByBadgeNodeKey);
        Assert.Equal(29, byHistoryBadge.Children.Count);
    }

    [Fact]
    public void Production_history_plaque_records_surface()
    {
        Assert.True(_productionCatalog.IsLoaded, _productionCatalog.LoadFailureReason);
        Assert.Equal(179, _productionCatalog.GetHistoryPlaqueRouteStops().Count);

        var tree = ReferenceBadgeBrowseSupport.BuildBrowseTree(_productionCatalog);
        Assert.Equal(179, CountHistoryPlaqueNodes(tree));
    }

    [Fact]
    public void Plaque_collection_completion_badge_appears_first()
    {
        var tree = ReferenceBadgeBrowseSupport.BuildBrowseTree(_productionCatalog);
        var christie = FindPlaqueCollection(tree, "Christie Consolidation");

        var firstChild = christie.Children[0];
        Assert.Equal(ReferenceEnhancementBrowseNodeKind.Badge, firstChild.Kind);
        Assert.Equal("BAD-02554", firstChild.BadgeId);
        Assert.True(firstChild.IsCollectionCompletionBadge);
    }

    [Fact]
    public void Single_zone_collection_uses_published_route_order()
    {
        var tree = ReferenceBadgeBrowseSupport.BuildBrowseTree(_productionCatalog);
        var christie = FindPlaqueCollection(tree, "Christie Consolidation");

        var plaqueNames = christie.Children
            .Skip(1)
            .Where(child => child.Kind == ReferenceEnhancementBrowseNodeKind.HistoryPlaque)
            .Select(child => child.DisplayName)
            .ToArray();

        Assert.Equal(
            [
                "Christie Consolidation plaque 5",
                "Christie Consolidation plaque 4",
                "Christie Consolidation plaque 2",
                "Christie Consolidation plaque 3",
                "Christie Consolidation plaque 1",
                "Christie Consolidation plaque 9",
                "Christie Consolidation plaque 8",
                "Christie Consolidation plaque 7",
                "Christie Consolidation plaque 6"
            ],
            plaqueNames);
    }

    [Fact]
    public void Multi_zone_collection_groups_zone_segments_without_cross_zone_ordering()
    {
        var tree = ReferenceBadgeBrowseSupport.BuildBrowseTree(_productionCatalog);
        var academic = FindPlaqueCollection(tree, "Academic");

        var zoneSegments = academic.Children
            .Skip(1)
            .Where(child => child.Kind == ReferenceEnhancementBrowseNodeKind.BadgePlaqueZoneSegment)
            .ToArray();

        Assert.Equal(3, zoneSegments.Length);
        Assert.Equal(
            ["Abandoned Sewer Network", "Peregrine Island", "Rikti War Zone"],
            zoneSegments.Select(segment => segment.DisplayName).ToArray());

        var riktiSegment = zoneSegments.Single(segment => segment.DisplayName == "Rikti War Zone");
        Assert.Equal("Academic plaque 2", Assert.Single(riktiSegment.Children).DisplayName);

        var peregrineSegment = zoneSegments.Single(segment => segment.DisplayName == "Peregrine Island");
        Assert.Equal("Academic plaque 1", Assert.Single(peregrineSegment.Children).DisplayName);

        var sewerSegment = zoneSegments.Single(segment => segment.DisplayName == "Abandoned Sewer Network");
        Assert.Equal("Academic plaque 3", Assert.Single(sewerSegment.Children).DisplayName);
    }

    [Fact]
    public void History_plaque_ordering_is_deterministic()
    {
        var first = ReferenceBadgeBrowseSupport.BuildBrowseTree(_productionCatalog);
        var second = ReferenceBadgeBrowseSupport.BuildBrowseTree(_productionCatalog);

        var firstKeys = CollectHistoryPlaqueNodeKeys(first);
        var secondKeys = CollectHistoryPlaqueNodeKeys(second);
        Assert.Equal(firstKeys, secondKeys);
    }

    [Fact]
    public void Plaque_collection_completion_badge_uses_persisted_acquisition_state()
    {
        var acquired = new HashSet<string>(StringComparer.Ordinal) { "BAD-02554" };
        var tree = ReferenceBadgeBrowseSupport.BuildBrowseTree(_productionCatalog, acquired);
        var completionNode = tree.NodesByKey.Values.Single(node =>
            node.Kind == ReferenceEnhancementBrowseNodeKind.Badge
            && node.IsCollectionCompletionBadge
            && string.Equals(node.BadgeId, "BAD-02554", StringComparison.Ordinal));

        Assert.True(completionNode.IsAcquired);
    }

    [Fact]
    public void Individual_history_plaques_do_not_fabricate_acquisition_state()
    {
        var acquired = new HashSet<string>(StringComparer.Ordinal) { "BAD-02554" };
        var tree = ReferenceBadgeBrowseSupport.BuildBrowseTree(_productionCatalog, acquired);

        Assert.All(
            tree.NodesByKey.Values.Where(node => node.Kind == ReferenceEnhancementBrowseNodeKind.HistoryPlaque),
            node => Assert.False(node.IsAcquired));
    }

    [Fact]
    public void Plaque_collection_completion_is_not_inferred_from_child_plaques()
    {
        var tree = ReferenceBadgeBrowseSupport.BuildBrowseTree(_productionCatalog);
        var completionNode = tree.NodesByKey.Values.Single(node =>
            node.Kind == ReferenceEnhancementBrowseNodeKind.Badge
            && node.IsCollectionCompletionBadge
            && string.Equals(node.BadgeId, "BAD-02554", StringComparison.Ordinal));

        Assert.False(completionNode.IsAcquired);
    }

    [Fact]
    public void History_plaque_detail_exposes_thumbtack_and_no_acquired_state()
    {
        var tree = ReferenceBadgeBrowseSupport.BuildBrowseTree(_productionCatalog);
        var plaqueNode = tree.NodesByKey.Values.Single(node =>
            node.Kind == ReferenceEnhancementBrowseNodeKind.HistoryPlaque
            && node.DisplayName == "Christie Consolidation plaque 5");

        var detail = ReferenceBadgeBrowseSupport.BuildDetail(
            _productionCatalog,
            plaqueNode,
            acquiredBadgeIds: new HashSet<string>(StringComparer.Ordinal) { "BAD-02554" },
            installedGameAssetProvider: null);

        Assert.Equal(ReferenceEnhancementDetailKind.HistoryPlaque, detail.Kind);
        Assert.Equal("Christie Consolidation", detail.CategoryLabel);
        Assert.Equal("Kallisti Wharf", detail.ZoneLabel);
        Assert.Equal("/thumbtack 5782.43 53.15 4699", detail.ThumbtackCommand);
        Assert.True(detail.ShowThumbtack);
        Assert.False(detail.ShowAcquiredState);
    }

    [Fact]
    public void History_plaque_without_coordinates_exposes_no_thumbtack_or_copy_affordance()
    {
        var tree = ReferenceBadgeBrowseSupport.BuildBrowseTree(_fixtureCatalog);
        var lucidNode = tree.NodesByKey.Values.Single(node => node.BadgeId == "BAD-90004");
        var detail = ReferenceBadgeBrowseSupport.BuildDetail(
            _fixtureCatalog,
            lucidNode,
            acquiredBadgeIds: null,
            installedGameAssetProvider: null);

        Assert.False(detail.ShowThumbtack);
        Assert.Null(detail.ThumbtackCommand);
    }

    [Fact]
    public void History_plaque_completion_badge_detail_shows_collection_requirements_not_plaque_location()
    {
        var tree = ReferenceBadgeBrowseSupport.BuildBrowseTree(_productionCatalog);
        var academic = FindPlaqueCollection(tree, "Academic");
        var completionNode = academic.Children[0];
        var detail = ReferenceBadgeBrowseSupport.BuildDetail(
            _productionCatalog,
            completionNode,
            acquiredBadgeIds: null,
            installedGameAssetProvider: null);

        Assert.Equal("Academic", detail.Title);
        Assert.Equal("History Badge", detail.CategoryLabel);
        Assert.True(detail.ShowRequirementIntro);
        Assert.Equal("Read all 3 Academic history plaques.", detail.RequirementIntroText);
        Assert.False(detail.ShowZone);
        Assert.False(detail.ShowThumbtack);
        Assert.False(detail.ShowLocationNote);
        Assert.Null(detail.LocationNote);
        Assert.True(detail.ShowAcquiredState);
    }

    [Fact]
    public void History_plaques_by_zone_surfaces_all_zones_and_cross_collection_plaques()
    {
        var tree = ReferenceBadgeBrowseSupport.BuildBrowseTree(_productionCatalog);
        var byZone = FindHistoryPlaquesByZoneBranch(tree);
        var zoneIds = _productionCatalog.GetHistoryPlaqueRouteStops()
            .Select(stop => stop.ZoneId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(zoneIds.Length, byZone.Children.Count);
        Assert.Equal(
            zoneIds.OrderBy(zoneId => zoneId, StringComparer.Ordinal).ToArray(),
            byZone.Children
                .Select(zone => zone.PlaqueZoneId!)
                .OrderBy(zoneId => zoneId, StringComparer.Ordinal)
                .ToArray());

        var peregrine = byZone.Children.Single(zone => zone.DisplayName == "Peregrine Island");
        Assert.True(peregrine.Children.Count > 1);
        Assert.Contains(
            peregrine.Children,
            plaque => plaque.DisplayName.StartsWith("Academic —", StringComparison.Ordinal));
    }

    [Fact]
    public void History_plaque_nodes_share_identity_between_by_badge_and_by_zone_trees()
    {
        var tree = ReferenceBadgeBrowseSupport.BuildBrowseTree(_productionCatalog);
        var academic = FindPlaqueCollection(tree, "Academic");
        var plaqueFromCollection = EnumerateCollectionPlaqueNodes(academic).First();

        var byZone = FindHistoryPlaquesByZoneBranch(tree);
        var zonePlaque = byZone.Children
            .SelectMany(zone => zone.Children)
            .Single(child => child.NodeKey == plaqueFromCollection.NodeKey);

        Assert.Equal(plaqueFromCollection.NodeKey, zonePlaque.NodeKey);
        Assert.NotEqual(plaqueFromCollection.DisplayName, zonePlaque.DisplayName);
        Assert.StartsWith("Academic —", zonePlaque.DisplayName, StringComparison.Ordinal);
    }

    [Fact]
    public void History_plaque_zone_labels_identify_collection_and_plaque()
    {
        var tree = ReferenceBadgeBrowseSupport.BuildBrowseTree(_productionCatalog);
        var byZone = FindHistoryPlaquesByZoneBranch(tree);
        var rikti = byZone.Children.Single(zone => zone.DisplayName == "Rikti War Zone");
        var academicPlaque = rikti.Children.Single(child =>
            string.Equals(child.PlaqueCollectionName, "Academic", StringComparison.Ordinal));

        Assert.Equal("Academic — Academic plaque 2", academicPlaque.DisplayName);
    }

    [Fact]
    public void Exploration_and_accolade_roots_remain_unchanged()
    {
        var tree = ReferenceBadgeBrowseSupport.BuildBrowseTree(_productionCatalog);
        Assert.Equal(3, tree.RootNodes.Count);
        Assert.Equal(ReferenceBadgeBrowseSupport.ExplorationRootNodeKey, tree.RootNodes[0].NodeKey);
        Assert.Equal(ReferenceBadgeBrowseSupport.HistoryPlaquesRootNodeKey, tree.RootNodes[1].NodeKey);
        Assert.Equal(ReferenceBadgeBrowseSupport.AccoladesRootNodeKey, tree.RootNodes[2].NodeKey);
        Assert.Equal(8, FindAccoladesRoot(tree).Children.Count);
    }

    [Fact]
    public void Production_exploration_accolade_uses_and_semantics()
    {
        var tree = ReferenceBadgeBrowseSupport.BuildBrowseTree(_productionCatalog);
        var detail = ReferenceBadgeBrowseSupport.BuildDetail(
            _productionCatalog,
            FindAccoladeNode(tree, "BAD-01917"),
            acquiredBadgeIds: null,
            installedGameAssetProvider: null);

        Assert.Equal("All of the following:", detail.RequirementLogicNote);
        Assert.Equal(8, detail.AccoladeRequirements.Count);
        Assert.Equal(7, detail.AccoladeRequirements.Count(item => item.LogicConnectorAfter == "AND"));
        Assert.Null(detail.AccoladeRequirements.Last().LogicConnectorAfter);
    }

    [Fact]
    public void Alchemist_contains_caregiver_exactly_once()
    {
        var tree = ReferenceBadgeBrowseSupport.BuildBrowseTree(_productionCatalog);
        var detail = ReferenceBadgeBrowseSupport.BuildDetail(
            _productionCatalog,
            FindAccoladeNode(tree, "BAD-02094"),
            acquiredBadgeIds: null,
            installedGameAssetProvider: null);

        Assert.Equal(2, detail.AccoladeRequirements.Count);
        Assert.Single(
            detail.AccoladeRequirements,
            item => item.BadgeId == "BAD-02104" && item.DisplayName == "Caregiver / Pain Specialist");
    }

    [Fact]
    public void Production_accolade_requirements_have_no_duplicate_edges()
    {
        var duplicates = _productionCatalog.GetBadgeAccoladeRequirements()
            .GroupBy(requirement => $"{requirement.AccoladeBadgeId}|{requirement.PrerequisiteBadgeId}", StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .ToArray();

        Assert.Empty(duplicates);
    }

    [Fact]
    public void Production_accolades_surface()
    {
        Assert.True(_productionCatalog.IsLoaded, _productionCatalog.LoadFailureReason);
        Assert.Equal(145, _productionCatalog.GetBadges().Count(badge => badge.ReferenceKind == ReferenceBadgeKind.Accolade));

        var tree = ReferenceBadgeBrowseSupport.BuildBrowseTree(_productionCatalog);
        var accoladesRoot = FindAccoladesRoot(tree);
        Assert.Equal(8, accoladesRoot.Children.Count);
        Assert.Equal(145, tree.NodesByKey.Values.Count(node =>
            node.Kind == ReferenceEnhancementBrowseNodeKind.BadgeAccolade));
    }

    [Fact]
    public void Production_accolade_requirement_relationships_surface()
    {
        Assert.True(_productionCatalog.IsLoaded, _productionCatalog.LoadFailureReason);
        Assert.Equal(740, _productionCatalog.GetBadgeAccoladeRequirements().Count);
    }

    [Fact]
    public void Accolade_detail_shows_description_and_reward_when_present()
    {
        var tree = ReferenceBadgeBrowseSupport.BuildBrowseTree(_fixtureCatalog);
        var accoladeNode = FindAccoladeNode(tree, "BAD-90001");
        var detail = ReferenceBadgeBrowseSupport.BuildDetail(
            _fixtureCatalog,
            accoladeNode,
            acquiredBadgeIds: null,
            installedGameAssetProvider: null);

        Assert.Equal(ReferenceEnhancementDetailKind.Accolade, detail.Kind);
        Assert.Equal("Exploration completion Accolade for Atlas Park.", detail.Summary);
        Assert.True(detail.ShowReward);
        Assert.Equal("5 Reward Merits", detail.RewardText);
    }

    [Fact]
    public void Accolade_without_reward_does_not_show_reward_section()
    {
        var tree = ReferenceBadgeBrowseSupport.BuildBrowseTree(_fixtureCatalog);
        var accoladeNode = FindAccoladeNode(tree, "BAD-90010");
        var detail = ReferenceBadgeBrowseSupport.BuildDetail(
            _fixtureCatalog,
            accoladeNode,
            acquiredBadgeIds: null,
            installedGameAssetProvider: null);

        Assert.False(detail.ShowReward);
        Assert.Null(detail.RewardText);
        Assert.True(detail.ShowRequirementIntro);
    }

    [Fact]
    public void Accolade_requirements_surface_with_and_connectors()
    {
        var tree = ReferenceBadgeBrowseSupport.BuildBrowseTree(_fixtureCatalog);
        var accoladeNode = FindAccoladeNode(tree, "BAD-90001");
        var detail = ReferenceBadgeBrowseSupport.BuildDetail(
            _fixtureCatalog,
            accoladeNode,
            acquiredBadgeIds: null,
            installedGameAssetProvider: null);

        Assert.True(detail.ShowAccoladeRequirements);
        Assert.Equal(["Alpha Badge", "Zebra Badge"], detail.AccoladeRequirements.Select(item => item.DisplayName));
        Assert.Equal(["AND"], detail.AccoladeRequirements
            .Select(item => item.LogicConnectorAfter)
            .Where(connector => connector is not null));
        Assert.Equal("All of the following:", detail.RequirementLogicNote);
    }

    [Fact]
    public void Accolade_or_logic_is_preserved()
    {
        var tree = ReferenceBadgeBrowseSupport.BuildBrowseTree(_fixtureCatalog);
        var accoladeNode = FindAccoladeNode(tree, "BAD-90011");
        var detail = ReferenceBadgeBrowseSupport.BuildDetail(
            _fixtureCatalog,
            accoladeNode,
            acquiredBadgeIds: null,
            installedGameAssetProvider: null);

        Assert.Equal("Any one of the following:", detail.RequirementLogicNote);
        Assert.Equal(["OR"], detail.AccoladeRequirements
            .Select(item => item.LogicConnectorAfter)
            .Where(connector => connector is not null)
            .Distinct());
    }

    [Fact]
    public void Accolade_prerequisite_acquisition_state_uses_persisted_badge_state()
    {
        var acquired = new HashSet<string>(StringComparer.Ordinal) { "BAD-90003" };
        var tree = ReferenceBadgeBrowseSupport.BuildBrowseTree(_fixtureCatalog, acquired);
        var detail = ReferenceBadgeBrowseSupport.BuildDetail(
            _fixtureCatalog,
            FindAccoladeNode(tree, "BAD-90001"),
            acquired,
            installedGameAssetProvider: null);

        var alpha = detail.AccoladeRequirements.Single(item => item.BadgeId == "BAD-90003");
        var zebra = detail.AccoladeRequirements.Single(item => item.BadgeId == "BAD-90002");
        Assert.Equal("Acquired", alpha.AcquiredStateLabel);
        Assert.Equal("Not acquired", zebra.AcquiredStateLabel);
    }

    [Fact]
    public void Accolade_acquired_state_uses_canonical_persisted_state()
    {
        var acquired = new HashSet<string>(StringComparer.Ordinal) { "BAD-90001" };
        var tree = ReferenceBadgeBrowseSupport.BuildBrowseTree(_fixtureCatalog, acquired);
        var accoladeNode = FindAccoladeNode(tree, "BAD-90001");
        Assert.True(accoladeNode.IsAcquired);

        var detail = ReferenceBadgeBrowseSupport.BuildDetail(
            _fixtureCatalog,
            accoladeNode,
            acquired,
            installedGameAssetProvider: null);
        Assert.Equal("Acquired", detail.AcquiredStateLabel);
    }

    [Fact]
    public void Shared_accolade_identity_keeps_acquisition_state_across_exploration_and_accolades()
    {
        var acquired = new HashSet<string>(StringComparer.Ordinal) { "BAD-01917" };
        var tree = ReferenceBadgeBrowseSupport.BuildBrowseTree(_productionCatalog, acquired);

        var explorationCompletion = tree.NodesByKey.Values.Single(node =>
            node.Kind == ReferenceEnhancementBrowseNodeKind.Badge
            && node.IsZoneCompletionBadge
            && node.BadgeId == "BAD-01917");
        var accoladeNode = FindAccoladeNode(tree, "BAD-01917");

        Assert.True(explorationCompletion.IsAcquired);
        Assert.True(accoladeNode.IsAcquired);
    }

    [Fact]
    public void Accolade_detail_does_not_add_progress_metrics()
    {
        var tree = ReferenceBadgeBrowseSupport.BuildBrowseTree(_fixtureCatalog);
        var detail = ReferenceBadgeBrowseSupport.BuildDetail(
            _fixtureCatalog,
            FindAccoladeNode(tree, "BAD-90001"),
            acquiredBadgeIds: null,
            installedGameAssetProvider: null);

        Assert.DoesNotContain('%', detail.Summary ?? string.Empty);
        Assert.All(detail.AccoladeRequirements, item =>
            Assert.DoesNotContain('%', item.AcquiredStateLabel ?? string.Empty));
    }

    [Fact]
    public void Brickstown_resolves_zig_warden_as_exploration_completion()
    {
        var tree = ReferenceBadgeBrowseSupport.BuildBrowseTree(_productionCatalog);
        var completionBadgeIds = GetExplorationZoneCompletionBadgeIds(tree, "zone-brickstown");
        Assert.Contains("BAD-01994", completionBadgeIds);
    }

    [Fact]
    public void Peregrine_island_resolves_portal_corp_analyst_as_exploration_completion()
    {
        var tree = ReferenceBadgeBrowseSupport.BuildBrowseTree(_productionCatalog);
        var completionBadgeIds = GetExplorationZoneCompletionBadgeIds(tree, "zone-peregrine-island");
        Assert.Contains("BAD-03001", completionBadgeIds);
    }

    [Fact]
    public void Peregrine_island_does_not_contain_zig_warden()
    {
        var tree = ReferenceBadgeBrowseSupport.BuildBrowseTree(_productionCatalog);
        var zoneNode = FindExplorationZoneNode(tree, "zone-peregrine-island");
        var badgeIds = zoneNode.Children
            .Where(node => node.Kind == ReferenceEnhancementBrowseNodeKind.Badge && node.BadgeId is not null)
            .Select(node => node.BadgeId!)
            .ToList();
        Assert.DoesNotContain("BAD-01994", badgeIds);
    }

    [Fact]
    public void Thrill_seeker_is_not_a_zone_exploration_member()
    {
        Assert.True(_productionCatalog.TryGetBadgeById("BAD-02666", out var thrillSeeker));
        Assert.Null(thrillSeeker.CompletionBadgeId);
        Assert.Equal(ReferenceBadgeKind.ArchitectEntertainment, thrillSeeker.ReferenceKind);

        var locations = _productionCatalog.GetBadgeLocations("BAD-02666");
        Assert.NotEmpty(locations);

        var tree = ReferenceBadgeBrowseSupport.BuildBrowseTree(_productionCatalog);

        var brickstownZone = FindExplorationZoneNode(tree, "zone-brickstown");
        Assert.DoesNotContain(
            brickstownZone.Children,
            node => node.BadgeId == "BAD-02666");

        var peregrineZone = FindExplorationZoneNode(tree, "zone-peregrine-island");
        Assert.DoesNotContain(
            peregrineZone.Children,
            node => node.BadgeId == "BAD-02666");

        Assert.Contains("BAD-01994", GetExplorationZoneCompletionBadgeIds(tree, "zone-brickstown"));
        Assert.Contains("BAD-03001", GetExplorationZoneCompletionBadgeIds(tree, "zone-peregrine-island"));
        Assert.DoesNotContain(
            GetExplorationZoneCompletionBadgeIds(tree, "zone-peregrine-island"),
            badgeId => badgeId == "BAD-01994");
    }

    [Fact]
    public void Generic_location_only_badge_is_excluded_from_zone_exploration_tree()
    {
        const string json =
            """
            {
              "manifest": {
                "catalogVersion": "item-ref-location-only-member-test",
                "homecomingCompatibility": { "buildMin": "Issue 28", "buildMax": "Issue 28" }
              },
              "items": [
                {
                  "catalogItemId": "BAD-85001",
                  "family": "Badge",
                  "subtype": "Exploration",
                  "currentDisplayName": "Generic Trigger",
                  "activeStatus": "Active",
                  "verificationStatus": "VerifiedMultiSource",
                  "icon": "badge_tourist_01"
                }
              ],
              "aliases": [],
              "enhancementSets": [],
              "badges": [
                {
                  "catalogItemId": "BAD-85001",
                  "homecomingSourceId": "GenericTriggerBadge",
                  "canonicalCategory": "Exploration",
                  "badgeType": 1,
                  "referenceKind": "ExplorationBadge",
                  "heroName": "Generic Trigger",
                  "villainName": "Generic Trigger",
                  "heroIcon": "badge_tourist_01",
                  "verificationStatus": "VerifiedMultiSource"
                }
              ],
              "zones": [
                {
                  "zoneId": "zone-brickstown",
                  "displayName": "Brickstown",
                  "explorationCompletionBadgeIds": []
                }
              ],
              "badgeLocations": [
                {
                  "badgeCatalogItemId": "BAD-85001",
                  "zoneId": "zone-brickstown",
                  "coordinateX": 1,
                  "coordinateY": 2,
                  "coordinateZ": 3,
                  "thumbtackCommand": "/thumbtack 1 2 3",
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

        var catalog = LoadCatalog(json);
        var tree = ReferenceBadgeBrowseSupport.BuildBrowseTree(catalog);
        Assert.DoesNotContain(
            tree.NodesByKey.Values,
            node => node.Kind == ReferenceEnhancementBrowseNodeKind.Badge && node.BadgeId == "BAD-85001");
        Assert.NotEmpty(catalog.GetBadgeLocations("BAD-85001"));
    }

    [Fact]
    public void Defensive_resolver_rejects_malformed_cross_zone_completion_badge_id()
    {
        const string json =
            """
            {
              "manifest": {
                "catalogVersion": "item-ref-zone-completion-guard-test",
                "homecomingCompatibility": { "buildMin": "Issue 28", "buildMax": "Issue 28" }
              },
              "items": [
                {
                  "catalogItemId": "BAD-81001",
                  "family": "Badge",
                  "subtype": "Accolades",
                  "currentDisplayName": "Brick Explorer",
                  "activeStatus": "Active",
                  "verificationStatus": "VerifiedMultiSource",
                  "icon": "Badge_HeroExploreAccolade"
                },
                {
                  "catalogItemId": "BAD-81002",
                  "family": "Badge",
                  "subtype": "Exploration",
                  "currentDisplayName": "Peregrine Member",
                  "activeStatus": "Active",
                  "verificationStatus": "VerifiedMultiSource",
                  "icon": "badge_tourist_01"
                },
                {
                  "catalogItemId": "BAD-81003",
                  "family": "Badge",
                  "subtype": "Exploration",
                  "currentDisplayName": "Peregrine Tour",
                  "activeStatus": "Active",
                  "verificationStatus": "VerifiedMultiSource",
                  "icon": "badge_tourist_01"
                },
                {
                  "catalogItemId": "BAD-83001",
                  "family": "Badge",
                  "subtype": "Accolades",
                  "currentDisplayName": "Peregrine Explorer",
                  "activeStatus": "Active",
                  "verificationStatus": "VerifiedMultiSource",
                  "icon": "Badge_HeroExploreAccolade"
                }
              ],
              "aliases": [],
              "enhancementSets": [],
              "badges": [
                {
                  "catalogItemId": "BAD-81001",
                  "homecomingSourceId": "BrickstownExplorer",
                  "canonicalCategory": "Accolades",
                  "badgeType": 5,
                  "referenceKind": "Accolade",
                  "heroName": "Brick Explorer",
                  "villainName": "Brick Explorer",
                  "heroIcon": "Badge_HeroExploreAccolade",
                  "verificationStatus": "VerifiedMultiSource"
                },
                {
                  "catalogItemId": "BAD-81002",
                  "homecomingSourceId": "MissionArchitectTourism",
                  "canonicalCategory": "Exploration",
                  "badgeType": 1,
                  "referenceKind": "ExplorationBadge",
                  "heroName": "Peregrine Member",
                  "villainName": "Peregrine Member",
                  "heroIcon": "badge_tourist_01",
                  "completionBadgeId": "BAD-81001",
                  "verificationStatus": "VerifiedMultiSource"
                },
                {
                  "catalogItemId": "BAD-81003",
                  "homecomingSourceId": "PeregrineIslandTour1",
                  "canonicalCategory": "Exploration",
                  "badgeType": 1,
                  "referenceKind": "ExplorationBadge",
                  "heroName": "Peregrine Tour",
                  "villainName": "Peregrine Tour",
                  "heroIcon": "badge_tourist_01",
                  "completionBadgeId": "BAD-83001",
                  "verificationStatus": "VerifiedMultiSource"
                },
                {
                  "catalogItemId": "BAD-83001",
                  "homecomingSourceId": "PeregrineIslandExplorer",
                  "canonicalCategory": "Accolades",
                  "badgeType": 5,
                  "referenceKind": "Accolade",
                  "heroName": "Peregrine Explorer",
                  "villainName": "Peregrine Explorer",
                  "heroIcon": "Badge_HeroExploreAccolade",
                  "verificationStatus": "VerifiedMultiSource"
                }
              ],
              "zones": [
                {
                  "zoneId": "zone-peregrine-island",
                  "displayName": "Peregrine Island",
                  "explorationCompletionBadgeIds": []
                }
              ],
              "badgeLocations": [
                {
                  "badgeCatalogItemId": "BAD-81002",
                  "zoneId": "zone-peregrine-island",
                  "coordinateX": 1,
                  "coordinateY": 2,
                  "coordinateZ": 3,
                  "thumbtackCommand": "/thumbtack 1 2 3",
                  "markerType": "ExplorationBadge",
                  "locationRole": "ExplorationMarker",
                  "coordinateSemantics": "SurveyedMarkerCenter",
                  "verificationStatus": "VerifiedMultiSource",
                  "locationIndex": 0
                },
                {
                  "badgeCatalogItemId": "BAD-81003",
                  "zoneId": "zone-peregrine-island",
                  "coordinateX": 4,
                  "coordinateY": 5,
                  "coordinateZ": 6,
                  "thumbtackCommand": "/thumbtack 4 5 6",
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

        var catalog = LoadCatalog(json);
        var tree = ReferenceBadgeBrowseSupport.BuildBrowseTree(catalog);
        var completionBadgeIds = GetExplorationZoneCompletionBadgeIds(tree, "zone-peregrine-island");
        Assert.Contains("BAD-83001", completionBadgeIds);
        Assert.DoesNotContain("BAD-81001", completionBadgeIds);
    }

    [Fact]
    public void Valid_same_zone_member_completion_inference_still_works()
    {
        const string json =
            """
            {
              "manifest": {
                "catalogVersion": "item-ref-same-zone-completion-test",
                "homecomingCompatibility": { "buildMin": "Issue 28", "buildMax": "Issue 28" }
              },
              "items": [
                {
                  "catalogItemId": "BAD-84001",
                  "family": "Badge",
                  "subtype": "Accolades",
                  "currentDisplayName": "Atlas Explorer",
                  "activeStatus": "Active",
                  "verificationStatus": "VerifiedMultiSource",
                  "icon": "Badge_HeroExploreAccolade"
                },
                {
                  "catalogItemId": "BAD-84002",
                  "family": "Badge",
                  "subtype": "Exploration",
                  "currentDisplayName": "Atlas Tour A",
                  "activeStatus": "Active",
                  "verificationStatus": "VerifiedMultiSource",
                  "icon": "badge_tourist_01"
                },
                {
                  "catalogItemId": "BAD-84003",
                  "family": "Badge",
                  "subtype": "Exploration",
                  "currentDisplayName": "Atlas Tour B",
                  "activeStatus": "Active",
                  "verificationStatus": "VerifiedMultiSource",
                  "icon": "badge_tourist_01"
                }
              ],
              "aliases": [],
              "enhancementSets": [],
              "badges": [
                {
                  "catalogItemId": "BAD-84001",
                  "homecomingSourceId": "AtlasParkExplorer",
                  "canonicalCategory": "Accolades",
                  "badgeType": 5,
                  "referenceKind": "Accolade",
                  "heroName": "Atlas Explorer",
                  "villainName": "Atlas Explorer",
                  "heroIcon": "Badge_HeroExploreAccolade",
                  "verificationStatus": "VerifiedMultiSource"
                },
                {
                  "catalogItemId": "BAD-84002",
                  "homecomingSourceId": "AtlasParkTour1",
                  "canonicalCategory": "Exploration",
                  "badgeType": 1,
                  "referenceKind": "ExplorationBadge",
                  "heroName": "Atlas Tour A",
                  "villainName": "Atlas Tour A",
                  "heroIcon": "badge_tourist_01",
                  "completionBadgeId": "BAD-84001",
                  "verificationStatus": "VerifiedMultiSource"
                },
                {
                  "catalogItemId": "BAD-84003",
                  "homecomingSourceId": "AtlasParkTour2",
                  "canonicalCategory": "Exploration",
                  "badgeType": 1,
                  "referenceKind": "ExplorationBadge",
                  "heroName": "Atlas Tour B",
                  "villainName": "Atlas Tour B",
                  "heroIcon": "badge_tourist_01",
                  "completionBadgeId": "BAD-84001",
                  "verificationStatus": "VerifiedMultiSource"
                }
              ],
              "zones": [
                {
                  "zoneId": "zone-atlas-park",
                  "displayName": "Atlas Park",
                  "explorationCompletionBadgeIds": []
                }
              ],
              "badgeLocations": [
                {
                  "badgeCatalogItemId": "BAD-84002",
                  "zoneId": "zone-atlas-park",
                  "coordinateX": 1,
                  "coordinateY": 2,
                  "coordinateZ": 3,
                  "thumbtackCommand": "/thumbtack 1 2 3",
                  "markerType": "ExplorationBadge",
                  "locationRole": "ExplorationMarker",
                  "coordinateSemantics": "SurveyedMarkerCenter",
                  "verificationStatus": "VerifiedMultiSource",
                  "locationIndex": 0
                },
                {
                  "badgeCatalogItemId": "BAD-84003",
                  "zoneId": "zone-atlas-park",
                  "coordinateX": 4,
                  "coordinateY": 5,
                  "coordinateZ": 6,
                  "thumbtackCommand": "/thumbtack 4 5 6",
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

        var catalog = LoadCatalog(json);
        var tree = ReferenceBadgeBrowseSupport.BuildBrowseTree(catalog);
        var completionBadgeIds = GetExplorationZoneCompletionBadgeIds(tree, "zone-atlas-park");
        Assert.Contains("BAD-84001", completionBadgeIds);
    }

    [Fact]
    public void Zones_with_explicit_completion_ids_continue_to_work()
    {
        const string json =
            """
            {
              "manifest": {
                "catalogVersion": "item-ref-explicit-zone-completion-test",
                "homecomingCompatibility": { "buildMin": "Issue 28", "buildMax": "Issue 28" }
              },
              "items": [
                {
                  "catalogItemId": "BAD-82001",
                  "family": "Badge",
                  "subtype": "Accolades",
                  "currentDisplayName": "Explicit Explorer",
                  "activeStatus": "Active",
                  "verificationStatus": "VerifiedMultiSource",
                  "icon": "Badge_HeroExploreAccolade"
                },
                {
                  "catalogItemId": "BAD-82002",
                  "family": "Badge",
                  "subtype": "Exploration",
                  "currentDisplayName": "Explicit Tour",
                  "activeStatus": "Active",
                  "verificationStatus": "VerifiedMultiSource",
                  "icon": "badge_tourist_01"
                }
              ],
              "aliases": [],
              "enhancementSets": [],
              "badges": [
                {
                  "catalogItemId": "BAD-82001",
                  "homecomingSourceId": "AtlasParkExplorer",
                  "canonicalCategory": "Accolades",
                  "badgeType": 5,
                  "referenceKind": "Accolade",
                  "heroName": "Explicit Explorer",
                  "villainName": "Explicit Explorer",
                  "heroIcon": "Badge_HeroExploreAccolade",
                  "verificationStatus": "VerifiedMultiSource"
                },
                {
                  "catalogItemId": "BAD-82002",
                  "homecomingSourceId": "AtlasParkTour1",
                  "canonicalCategory": "Exploration",
                  "badgeType": 1,
                  "referenceKind": "ExplorationBadge",
                  "heroName": "Explicit Tour",
                  "villainName": "Explicit Tour",
                  "heroIcon": "badge_tourist_01",
                  "completionBadgeId": "BAD-99999",
                  "verificationStatus": "VerifiedMultiSource"
                }
              ],
              "zones": [
                {
                  "zoneId": "zone-atlas-park",
                  "displayName": "Atlas Park",
                  "explorationCompletionBadgeIds": ["BAD-82001"]
                }
              ],
              "badgeLocations": [
                {
                  "badgeCatalogItemId": "BAD-82002",
                  "zoneId": "zone-atlas-park",
                  "coordinateX": 1,
                  "coordinateY": 2,
                  "coordinateZ": 3,
                  "thumbtackCommand": "/thumbtack 1 2 3",
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

        var catalog = LoadCatalog(json);
        var tree = ReferenceBadgeBrowseSupport.BuildBrowseTree(catalog);
        var completionBadgeIds = GetExplorationZoneCompletionBadgeIds(tree, "zone-atlas-park");
        Assert.Equal(["BAD-82001"], completionBadgeIds);
        Assert.DoesNotContain("BAD-99999", completionBadgeIds);
    }

    private static ReferenceEnhancementBrowseNode FindExplorationZoneNode(
        ReferenceEnhancementBrowseTree tree,
        string zoneId) =>
        tree.NodesByKey.Values.Single(node =>
            node.Kind == ReferenceEnhancementBrowseNodeKind.BadgeZone
            && string.Equals(node.NodeKey, $"badge-zone:{zoneId}", StringComparison.Ordinal));

    private static List<string> GetExplorationZoneCompletionBadgeIds(
        ReferenceEnhancementBrowseTree tree,
        string zoneId) =>
        FindExplorationZoneNode(tree, zoneId)
            .Children
            .Where(node =>
                node.Kind == ReferenceEnhancementBrowseNodeKind.Badge
                && node.IsZoneCompletionBadge
                && node.BadgeId is not null)
            .Select(node => node.BadgeId!)
            .ToList();

    private static ReferenceEnhancementBrowseNode FindExplorationBadgeNode(
        ReferenceEnhancementBrowseTree tree,
        string badgeId,
        bool? isZoneCompletion = null) =>
        tree.NodesByKey.Values.Single(node =>
            node.Kind == ReferenceEnhancementBrowseNodeKind.Badge
            && string.Equals(node.BadgeId, badgeId, StringComparison.Ordinal)
            && (isZoneCompletion is null || node.IsZoneCompletionBadge == isZoneCompletion));

    private static ReferenceEnhancementBrowseNode FindAccoladesRoot(ReferenceEnhancementBrowseTree tree) =>
        tree.RootNodes.Single(node => node.Kind == ReferenceEnhancementBrowseNodeKind.BadgeAccoladesRoot);

    private static ReferenceEnhancementBrowseNode FindAccoladeNode(
        ReferenceEnhancementBrowseTree tree,
        string badgeId) =>
        tree.NodesByKey.Values.Single(node =>
            node.Kind == ReferenceEnhancementBrowseNodeKind.BadgeAccolade
            && string.Equals(node.BadgeId, badgeId, StringComparison.Ordinal));

    private static ReferenceEnhancementBrowseNode FindHistoryPlaquesRoot(ReferenceEnhancementBrowseTree tree) =>
        tree.RootNodes.Single(node => node.Kind == ReferenceEnhancementBrowseNodeKind.BadgeHistoryPlaquesRoot);

    private static IEnumerable<ReferenceEnhancementBrowseNode> EnumerateCollectionPlaqueNodes(
        ReferenceEnhancementBrowseNode collectionNode)
    {
        foreach (var child in collectionNode.Children)
        {
            if (child.Kind == ReferenceEnhancementBrowseNodeKind.HistoryPlaque)
            {
                yield return child;
            }

            if (child.Kind == ReferenceEnhancementBrowseNodeKind.BadgePlaqueZoneSegment)
            {
                foreach (var plaque in child.Children.Where(node =>
                             node.Kind == ReferenceEnhancementBrowseNodeKind.HistoryPlaque))
                {
                    yield return plaque;
                }
            }
        }
    }

    private static ReferenceEnhancementBrowseNode FindHistoryPlaquesByZoneBranch(ReferenceEnhancementBrowseTree tree) =>
        FindHistoryPlaquesRoot(tree).Children.Single(node =>
            node.NodeKey == ReferenceBadgeBrowseSupport.HistoryPlaquesByZoneNodeKey);

    private static ReferenceEnhancementBrowseNode FindPlaqueCollection(
        ReferenceEnhancementBrowseTree tree,
        string collectionName) =>
        tree.NodesByKey.Values.Single(node =>
            node.Kind == ReferenceEnhancementBrowseNodeKind.BadgePlaqueCollection
            && string.Equals(node.DisplayName, collectionName, StringComparison.Ordinal));

    private static int CountHistoryPlaqueNodes(ReferenceEnhancementBrowseTree tree) =>
        tree.NodesByKey.Values.Count(node => node.Kind == ReferenceEnhancementBrowseNodeKind.HistoryPlaque);

    private static IReadOnlyList<string> CollectHistoryPlaqueNodeKeys(ReferenceEnhancementBrowseTree tree) =>
        tree.NodesByKey.Values
            .Where(node => node.Kind == ReferenceEnhancementBrowseNodeKind.HistoryPlaque)
            .Select(node => node.NodeKey)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();

    private static ReferenceEnhancementTreeNodeViewModel FindBadgeNode(
        ReferenceViewModel viewModel,
        string badgeId)
    {
        var node = EnumerateBadgeNodes(viewModel).Single(node => node.BadgeId == badgeId);
        return node;
    }

    private static IEnumerable<ReferenceEnhancementTreeNodeViewModel> EnumerateBadgeNodes(
        ReferenceViewModel viewModel)
    {
        foreach (var root in viewModel.TreeRootNodes)
        {
            foreach (var node in EnumerateNodes(root))
            {
                if (node.Kind == ReferenceEnhancementBrowseNodeKind.Badge)
                {
                    yield return node;
                }
            }
        }
    }

    private static IEnumerable<ReferenceEnhancementTreeNodeViewModel> EnumerateNodes(
        ReferenceEnhancementTreeNodeViewModel node)
    {
        yield return node;
        foreach (var child in node.Children.SelectMany(EnumerateNodes))
        {
            yield return child;
        }
    }

    private static IItemReferenceCatalog LoadCatalog(string json)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var result = ItemReferenceCatalogLoader.Load(stream);
        Assert.True(result.Succeeded, result.FailureReason);
        return ItemReferenceCatalog.FromLoadResult(result);
    }

    private static string? TryGetClipboardText()
    {
        const int clipboardOpenError = unchecked((int)0x800401D0);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                return Clipboard.GetText();
            }
            catch (COMException ex) when (ex.HResult == clipboardOpenError && attempt < 4)
            {
                Thread.Sleep(20 * (attempt + 1));
            }
            catch (COMException ex) when (ex.HResult == clipboardOpenError)
            {
                return null;
            }
        }

        return null;
    }

    private static ReferenceViewModel CreateViewModel(
        IViewedContextService? viewedContextService = null,
        ICharacterBadgeAcquisitionRepository? repository = null) =>
        new(
            new TestGameplaySessionContextSupport.FakeApplicationOrchestrator(),
            new FakeGameRuntimeService(),
            ItemReferenceCatalog.FromLoadResult(ItemReferenceCatalogLoader.Load(
                new MemoryStream(Encoding.UTF8.GetBytes(ExplorationFixtureJson)))),
            viewedContextService: viewedContextService,
            characterBadgeAcquisitionRepository: repository);

    private static ReferenceViewModel CreateProductionViewModel() =>
        new(
            new TestGameplaySessionContextSupport.FakeApplicationOrchestrator(),
            new FakeGameRuntimeService(),
            ItemReferenceCatalogFactory.LoadEmbeddedProduction());

    private static BitmapSource CreateBitmap(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        return BitmapSource.Create(
            width,
            height,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixels,
            width * 4);
    }

    private sealed class RecordingViewedContextService : IViewedContextService
    {
        public ViewedContextState Current { get; private set; } = ViewedContextState.Empty;

        public event EventHandler<ViewedContextChangedEventArgs>? Changed;

        public void SelectViewedAccount(string accountStableId) =>
            Current = Current with { AccountStableId = accountStableId };

        public void SelectViewedCharacter(string accountStableId, CharacterRecordId characterRecordId) =>
            SetCharacter(characterRecordId, accountStableId);

        public void SetCharacter(CharacterRecordId characterRecordId, string accountStableId = "acct")
        {
            Current = Current with
            {
                AccountStableId = accountStableId,
                CharacterRecordId = characterRecordId
            };
            Changed?.Invoke(this, new ViewedContextChangedEventArgs { State = Current });
        }

        public void ReturnToLive() => Current = ViewedContextState.Empty;

        public void SelectGameplaySessionContext(MonitoringContextId contextId)
        {
        }

        public void ResetForNewRuntimeGeneration() => Current = ViewedContextState.Empty;
    }

    private sealed class FakeInstalledGameAssetProvider : IInstalledGameAssetProvider
    {
        public List<string?> Requests { get; } = [];

        private readonly Dictionary<string, ImageSource> _resolved = new(StringComparer.OrdinalIgnoreCase);

        public void Register(string iconIdentity, ImageSource source) => _resolved[iconIdentity] = source;

        public ImageSource? TryResolve(string? iconIdentity)
        {
            Requests.Add(iconIdentity);
            return string.IsNullOrWhiteSpace(iconIdentity)
                ? null
                : _resolved.GetValueOrDefault(iconIdentity);
        }
    }

    private sealed class RecordingEnhancementIconCompositor : IEnhancementIconCompositor
    {
        public List<EnhancementIconCompositionRequest> Requests { get; } = [];

        public ImageSource? TryCompose(EnhancementIconCompositionRequest request)
        {
            Requests.Add(request);
            return null;
        }
    }
}
