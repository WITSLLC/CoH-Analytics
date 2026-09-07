using System.IO;
using System.Text;
using CoHAnalytics.Models;
using CoHAnalytics.Orchestration.Contracts;
using CoHAnalytics.ReferenceData;
using CoHAnalytics.Services;
using CoHAnalytics.Tests.Services;
using CoHAnalytics.ViewModels.Workspaces;

namespace CoHAnalytics.Tests.Workspaces;

public sealed class ReferenceViewModelBadgeAcquisitionRefreshTests
{
    private const string GalleryFixtureJson =
        """
        {
          "manifest": {
            "catalogVersion": "item-ref-badge-refresh-test",
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
            }
          ],
          "zones": [
            {
              "zoneId": "zone-atlas-park",
              "displayName": "Atlas Park",
              "explorationCompletionBadgeIds": ["BAD-91001"]
            }
          ],
          "badgeLocations": [
            {
              "badgeCatalogItemId": "BAD-91002",
              "zoneId": "zone-atlas-park",
              "coordinateX": 10,
              "coordinateY": 20,
              "coordinateZ": 30,
              "thumbtackCommand": "/thumbtack 10 20 30",
              "markerType": "ExplorationBadge",
              "locationRole": "ExplorationMarker",
              "coordinateSemantics": "SurveyedMarkerCenter",
              "verificationStatus": "VerifiedMultiSource",
              "locationIndex": 0
            }
          ],
          "badgeAccoladeRequirements": [
            {
              "accoladeBadgeId": "BAD-91001",
              "prerequisiteBadgeId": "BAD-91002",
              "prerequisiteIndex": 0,
              "logicGroup": 0,
              "requirementLogicStatus": "Verified"
            }
          ]
        }
        """;

    [Fact]
    public void Live_acquisition_updates_detail_and_tree_without_navigation()
    {
        var catalog = LoadCatalog(GalleryFixtureJson);
        var (repository, characterA) = CreateBadgeRepository();
        var viewed = new RecordingViewedContextService();
        var gameplay = new RecordingGameplaySessionManager();
        using var viewModel = CreateViewModel(catalog, viewed, repository, gameplay);

        viewed.SetCharacter(characterA, "acct-a");
        viewModel.SelectSectionChipCommand.Execute(ReferenceSectionId.Badges);
        var alphaNode = FindBadgeNode(viewModel, "BAD-91002");
        viewModel.SelectedNode = alphaNode;

        Assert.False(alphaNode.IsAcquired);
        Assert.Equal("Not acquired", viewModel.Detail.AcquiredStateLabel);

        repository.RecordAcquisition(
            characterA,
            "acct-a",
            "BAD-91002",
            "Alpha Badge",
            DateTimeOffset.UtcNow);
        gameplay.RaiseStateChanged();

        Assert.True(alphaNode.IsAcquired);
        Assert.Equal("Acquired", viewModel.Detail.AcquiredStateLabel);
        Assert.Same(alphaNode, viewModel.SelectedNode);
        Assert.NotEmpty(viewModel.TreeRootNodes);
    }

    [Fact]
    public void Accolade_acquisition_implies_member_badge_acquired_in_reference_tree()
    {
        var catalog = LoadCatalog(GalleryFixtureJson);
        var (repository, characterA) = CreateBadgeRepository();
        var viewed = new RecordingViewedContextService();
        using var viewModel = CreateViewModel(catalog, viewed, repository);

        viewed.SetCharacter(characterA, "acct-a");
        viewModel.SelectSectionChipCommand.Execute(ReferenceSectionId.Badges);
        var alphaNode = FindBadgeNode(viewModel, "BAD-91002");
        viewModel.SelectedNode = alphaNode;

        Assert.False(alphaNode.IsAcquired);
        Assert.Equal("Not acquired", viewModel.Detail.AcquiredStateLabel);

        repository.RecordAcquisition(
            characterA,
            "acct-a",
            "BAD-91001",
            "Atlas Tour Guide",
            DateTimeOffset.UtcNow);
        viewed.SetCharacter(characterA, "acct-a");
        alphaNode = FindBadgeNode(viewModel, "BAD-91002");
        viewModel.SelectedNode = alphaNode;

        Assert.True(alphaNode.IsAcquired);
        Assert.Equal("Acquired", viewModel.Detail.AcquiredStateLabel);
    }

    [Fact]
    public void Live_acquisition_preserves_expanded_badge_path()
    {
        var catalog = LoadCatalog(GalleryFixtureJson);
        var (repository, characterA) = CreateBadgeRepository();
        var viewed = new RecordingViewedContextService();
        var gameplay = new RecordingGameplaySessionManager();
        using var viewModel = CreateViewModel(catalog, viewed, repository, gameplay);

        viewed.SetCharacter(characterA, "acct-a");
        viewModel.SelectSectionChipCommand.Execute(ReferenceSectionId.Badges);

        var explorationRoot = viewModel.TreeRootNodes
            .Single(node => node.NodeKey == ReferenceBadgeBrowseSupport.ExplorationRootNodeKey);
        explorationRoot.IsExpanded = true;
        var atlasZone = explorationRoot.Children.Single(node => node.DisplayName == "Atlas Park");
        atlasZone.IsExpanded = true;
        var alphaNode = FindBadgeNode(viewModel, "BAD-91002");
        viewModel.SelectedNode = alphaNode;

        repository.RecordAcquisition(
            characterA,
            "acct-a",
            "BAD-91002",
            "Alpha Badge",
            DateTimeOffset.UtcNow);
        gameplay.RaiseStateChanged();

        Assert.True(explorationRoot.IsExpanded);
        Assert.True(atlasZone.IsExpanded);
        Assert.Same(alphaNode, viewModel.SelectedNode);
    }

    [Fact]
    public void Workspace_reentry_preserves_tree_roots_and_selection_state()
    {
        var catalog = LoadCatalog(GalleryFixtureJson);
        var (repository, characterA) = CreateBadgeRepository();
        var viewed = new RecordingViewedContextService();
        var gameplay = new RecordingGameplaySessionManager();
        using var viewModel = CreateViewModel(catalog, viewed, repository, gameplay);

        viewed.SetCharacter(characterA, "acct-a");
        viewModel.SelectSectionChipCommand.Execute(ReferenceSectionId.Badges);
        var alphaNode = FindBadgeNode(viewModel, "BAD-91002");
        viewModel.SelectedNode = alphaNode;

        repository.RecordAcquisition(
            characterA,
            "acct-a",
            "BAD-91002",
            "Alpha Badge",
            DateTimeOffset.UtcNow);
        gameplay.RaiseStateChanged();

        Assert.NotEmpty(viewModel.TreeRootNodes);
        Assert.Contains(
            viewModel.TreeRootNodes,
            node => node.NodeKey == ReferenceBadgeBrowseSupport.ExplorationRootNodeKey);
        Assert.Same(alphaNode, viewModel.SelectedNode);
        Assert.Equal("Acquired", viewModel.Detail.AcquiredStateLabel);
    }

    [Fact]
    public void Viewed_context_rebuild_restores_tree_after_character_switch()
    {
        var catalog = LoadCatalog(GalleryFixtureJson);
        var (repository, characterA) = CreateBadgeRepository();
        var characterB = CharacterRecordId.CreateNew();
        var viewed = new RecordingViewedContextService();
        using var viewModel = CreateViewModel(catalog, viewed, repository);

        viewModel.SelectSectionChipCommand.Execute(ReferenceSectionId.Badges);
        repository.RecordAcquisition(
            characterA,
            "acct-a",
            "BAD-91002",
            "Alpha Badge",
            DateTimeOffset.UtcNow);
        viewed.SetCharacter(characterA, "acct-a");

        var alphaForA = FindBadgeNode(viewModel, "BAD-91002");
        viewModel.SelectedNode = alphaForA;
        Assert.True(alphaForA.IsAcquired);

        viewed.SetCharacter(characterB, "acct-a");
        var alphaForB = FindBadgeNode(viewModel, "BAD-91002");
        Assert.False(alphaForB.IsAcquired);
        Assert.NotEmpty(viewModel.TreeRootNodes);
        Assert.Contains(
            viewModel.TreeRootNodes,
            node => node.NodeKey == ReferenceBadgeBrowseSupport.HistoryPlaquesRootNodeKey);
        Assert.Contains(
            viewModel.TreeRootNodes,
            node => node.NodeKey == ReferenceBadgeBrowseSupport.AccoladesRootNodeKey);
    }

    [Fact]
    public void Acquisition_for_other_character_does_not_update_viewed_character_reference()
    {
        var catalog = LoadCatalog(GalleryFixtureJson);
        var (repository, characterA) = CreateBadgeRepository();
        var characterB = CharacterRecordId.CreateNew();
        var viewed = new RecordingViewedContextService();
        var gameplay = new RecordingGameplaySessionManager();
        using var viewModel = CreateViewModel(catalog, viewed, repository, gameplay);

        viewed.SetCharacter(characterA, "acct-a");
        viewModel.SelectSectionChipCommand.Execute(ReferenceSectionId.Badges);
        var alphaNode = FindBadgeNode(viewModel, "BAD-91002");
        viewModel.SelectedNode = alphaNode;
        var initialTreeCount = viewModel.TreeRootNodes.Count;

        repository.RecordAcquisition(
            characterB,
            "acct-a",
            "BAD-91002",
            "Alpha Badge",
            DateTimeOffset.UtcNow);
        gameplay.RaiseStateChanged();

        Assert.False(alphaNode.IsAcquired);
        Assert.Equal("Not acquired", viewModel.Detail.AcquiredStateLabel);
        Assert.Equal(initialTreeCount, viewModel.TreeRootNodes.Count);
    }

    [Fact]
    public void Generic_gameplay_state_changes_do_not_rebuild_badge_tree()
    {
        var catalog = LoadCatalog(GalleryFixtureJson);
        var (repository, characterA) = CreateBadgeRepository();
        var viewed = new RecordingViewedContextService();
        var gameplay = new RecordingGameplaySessionManager();
        using var viewModel = CreateViewModel(catalog, viewed, repository, gameplay);

        viewed.SetCharacter(characterA, "acct-a");
        viewModel.SelectSectionChipCommand.Execute(ReferenceSectionId.Badges);
        var alphaNode = FindBadgeNode(viewModel, "BAD-91002");
        viewModel.SelectedNode = alphaNode;
        var rootReference = viewModel.TreeRootNodes[0];

        gameplay.RaiseStateChanged();
        gameplay.RaiseStateChanged();
        gameplay.RaiseStateChanged();

        Assert.Same(rootReference, viewModel.TreeRootNodes[0]);
        Assert.Same(alphaNode, viewModel.SelectedNode);
        Assert.False(viewModel.IsRebuildingBrowseTree);
    }

    private static (CharacterBadgeAcquisitionRepository Repository, CharacterRecordId CharacterId) CreateBadgeRepository()
    {
        var dataDirectory = Path.Combine(
            Path.GetTempPath(),
            "coh-reference-badge-refresh",
            Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dataDirectory);
        var repository = new CharacterBadgeAcquisitionRepository(
            new CharacterBadgeAcquisitionRepositoryOptions { DataDirectory = dataDirectory });
        return (repository, CharacterRecordId.CreateNew());
    }

    private static ReferenceViewModel CreateViewModel(
        IItemReferenceCatalog catalog,
        RecordingViewedContextService viewed,
        CharacterBadgeAcquisitionRepository repository,
        RecordingGameplaySessionManager? gameplay = null) =>
        new(
            new TestGameplaySessionContextSupport.FakeApplicationOrchestrator(),
            new FakeGameRuntimeService(),
            catalog,
            viewedContextService: viewed,
            characterBadgeAcquisitionRepository: repository,
            gameplaySessionManager: gameplay);

    private static IItemReferenceCatalog LoadCatalog(string json)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var result = ItemReferenceCatalogLoader.Load(stream);
        Assert.True(result.Succeeded, result.FailureReason);
        return ItemReferenceCatalog.FromLoadResult(result);
    }

    private static ReferenceEnhancementTreeNodeViewModel FindBadgeNode(
        ReferenceViewModel viewModel,
        string badgeId)
    {
        foreach (var root in viewModel.TreeRootNodes)
        {
        foreach (var node in EnumerateNodes(root))
        {
            if (node.BadgeId == badgeId
                && node.Kind is ReferenceEnhancementBrowseNodeKind.Badge
                    or ReferenceEnhancementBrowseNodeKind.BadgeAccolade)
            {
                return node;
            }
        }
        }

        throw new InvalidOperationException($"Badge node '{badgeId}' was not found.");
    }

    private static IEnumerable<ReferenceEnhancementTreeNodeViewModel> EnumerateNodes(
        ReferenceEnhancementTreeNodeViewModel node)
    {
        yield return node;
        foreach (var child in node.Children)
        {
            foreach (var descendant in EnumerateNodes(child))
            {
                yield return descendant;
            }
        }
    }

    private sealed class RecordingViewedContextService : IViewedContextService
    {
        public ViewedContextState Current { get; private set; } = ViewedContextState.Empty;

        public event EventHandler<ViewedContextChangedEventArgs>? Changed;

        public void SelectViewedAccount(string accountStableId) =>
            Current = Current with { AccountStableId = accountStableId };

        public void SelectViewedCharacter(string accountStableId, CharacterRecordId characterRecordId) =>
            SetCharacter(characterRecordId, accountStableId);

        public void SetCharacter(CharacterRecordId characterRecordId, string accountStableId)
        {
            Current = Current with
            {
                AccountStableId = accountStableId,
                CharacterRecordId = characterRecordId,
                IsFollowingLive = false
            };
            Changed?.Invoke(this, new ViewedContextChangedEventArgs { State = Current });
        }

        public void ReturnToLive() => Current = ViewedContextState.Empty;

        public void SelectGameplaySessionContext(MonitoringContextId contextId)
        {
        }

        public void ResetForNewRuntimeGeneration() => Current = ViewedContextState.Empty;
    }

    private sealed class RecordingGameplaySessionManager : IGameplaySessionManager
    {
        public GameplaySessionManagerSnapshot Current { get; } = GameplaySessionManagerSnapshot.Empty;

        public event EventHandler<GameplaySessionManagerChangedEventArgs>? StateChanged;

        public event EventHandler<GameplaySessionEventsAvailableEventArgs>? CommittedEventsAvailable
        {
            add { }
            remove { }
        }

        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public GameplaySessionOperationResult ConfirmCharacter(
            MonitoringContextId contextId,
            CharacterRecordId characterRecordId) =>
            GameplaySessionOperationResult.Success();

        public GameplaySessionOperationResult ClearIdentity(MonitoringContextId contextId) =>
            GameplaySessionOperationResult.Success();

        public GameplaySessionOperationResult StartTrackedCombat(MonitoringContextId contextId) =>
            GameplaySessionOperationResult.Success();

        public GameplaySessionOperationResult PauseTrackedCombat(MonitoringContextId contextId) =>
            GameplaySessionOperationResult.Success();

        public GameplaySessionOperationResult ResumeTrackedCombat(MonitoringContextId contextId) =>
            GameplaySessionOperationResult.Success();

        public GameplaySessionOperationResult StopTrackedCombat(MonitoringContextId contextId) =>
            GameplaySessionOperationResult.Success();

        public GameplaySessionOperationResult ResetTrackedCombat(MonitoringContextId contextId) =>
            GameplaySessionOperationResult.Success();

        public void ResetForNewRuntimeGeneration()
        {
        }

        public GameplaySessionDiagnostics GetDiagnostics() =>
            GameplaySessionTestInfrastructure.IdleGameplayDiagnostics(isRunning: false);

        public void RaiseStateChanged() =>
            StateChanged?.Invoke(this, new GameplaySessionManagerChangedEventArgs(Current));
    }
}
