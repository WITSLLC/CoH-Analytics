using System.IO;
using System.Text;
using CoHAnalytics.Models;
using CoHAnalytics.Orchestration.Contracts;
using CoHAnalytics.ReferenceData;
using CoHAnalytics.Services;
using CoHAnalytics.Tests.Services;
using CoHAnalytics.ViewModels.Workspaces;

namespace CoHAnalytics.Tests.Workspaces;

public sealed class ReferenceViewModelSectionSwitchingTests
{
    private static readonly IItemReferenceCatalog ProductionCatalog =
        ItemReferenceCatalogFactory.LoadEmbeddedProduction();

    [Fact]
    public void Switching_to_recipes_replaces_badge_roots_with_recipe_roots()
    {
        using var viewModel = CreateViewModel();
        viewModel.SelectSectionChipCommand.Execute(ReferenceSectionId.Badges);
        Assert.Contains(
            viewModel.TreeRootNodes,
            node => node.NodeKey == ReferenceBadgeBrowseSupport.ExplorationRootNodeKey);

        viewModel.SelectSectionChipCommand.Execute(ReferenceSectionId.Recipes);
        Assert.DoesNotContain(
            viewModel.TreeRootNodes,
            node => node.NodeKey == ReferenceBadgeBrowseSupport.ExplorationRootNodeKey);
        Assert.Contains(
            viewModel.TreeRootNodes,
            node => node.NodeKey == ReferenceRecipeBrowseSupport.SetsRootNodeKey);
    }

    [Fact]
    public void Switching_to_enhancements_replaces_recipe_roots_with_enhancement_roots()
    {
        using var viewModel = CreateViewModel();
        viewModel.SelectSectionChipCommand.Execute(ReferenceSectionId.Recipes);
        Assert.Contains(
            viewModel.TreeRootNodes,
            node => node.NodeKey == ReferenceRecipeBrowseSupport.SetsRootNodeKey);

        viewModel.SelectSectionChipCommand.Execute(ReferenceSectionId.Enhancements);
        Assert.DoesNotContain(
            viewModel.TreeRootNodes,
            node => node.NodeKey == ReferenceRecipeBrowseSupport.SetsRootNodeKey);
        Assert.Contains(
            viewModel.TreeRootNodes,
            node => node.NodeKey == ReferenceEnhancementBrowseSupport.SetsRootNodeKey);
    }

    [Fact]
    public void Switching_back_to_badges_restores_badge_roots()
    {
        using var viewModel = CreateViewModel();
        viewModel.SelectSectionChipCommand.Execute(ReferenceSectionId.Badges);
        viewModel.SelectSectionChipCommand.Execute(ReferenceSectionId.Recipes);
        viewModel.SelectSectionChipCommand.Execute(ReferenceSectionId.Enhancements);
        viewModel.SelectSectionChipCommand.Execute(ReferenceSectionId.Badges);

        Assert.Contains(
            viewModel.TreeRootNodes,
            node => node.NodeKey == ReferenceBadgeBrowseSupport.ExplorationRootNodeKey);
        Assert.NotEmpty(viewModel.TreeRootNodes);
    }

    [Fact]
    public void Selected_detail_belongs_to_active_section()
    {
        using var viewModel = CreateViewModel();
        viewModel.SelectSectionChipCommand.Execute(ReferenceSectionId.Enhancements);
        var enhancementSet = viewModel.TreeRootNodes
            .Single(node => node.NodeKey == ReferenceEnhancementBrowseSupport.SetsRootNodeKey)
            .Children
            .First(child => child.Kind == ReferenceEnhancementBrowseNodeKind.Category)
            .Children
            .First(child => child.Kind == ReferenceEnhancementBrowseNodeKind.Set);
        viewModel.SelectedNode = enhancementSet;

        Assert.Equal(ReferenceEnhancementDetailKind.Set, viewModel.Detail.Kind);
        viewModel.SelectSectionChipCommand.Execute(ReferenceSectionId.Recipes);
        Assert.Equal(ReferenceEnhancementDetailKind.Empty, viewModel.Detail.Kind);
        Assert.Equal("Recipes", viewModel.Detail.Title);
    }

    [Fact]
    public void Switching_sections_does_not_leave_stale_badge_detail()
    {
        using var viewModel = CreateViewModel();
        viewModel.SelectSectionChipCommand.Execute(ReferenceSectionId.Badges);
        var explorationRoot = viewModel.TreeRootNodes
            .Single(node => node.NodeKey == ReferenceBadgeBrowseSupport.ExplorationRootNodeKey);
        var zoneNode = explorationRoot.Children.First();
        var badgeNode = zoneNode.Children.First(node => node.Kind == ReferenceEnhancementBrowseNodeKind.Badge);
        viewModel.SelectedNode = badgeNode;
        Assert.Equal(ReferenceEnhancementDetailKind.Badge, viewModel.Detail.Kind);

        viewModel.SelectSectionChipCommand.Execute(ReferenceSectionId.Recipes);
        Assert.Null(viewModel.SelectedNode);
        Assert.NotEqual(ReferenceEnhancementDetailKind.Badge, viewModel.Detail.Kind);
    }

    [Fact]
    public void Returning_to_badges_preserves_populated_tree()
    {
        using var viewModel = CreateViewModel();
        viewModel.SelectSectionChipCommand.Execute(ReferenceSectionId.Badges);
        var initialRootCount = viewModel.TreeRootNodes.Count;
        viewModel.SelectSectionChipCommand.Execute(ReferenceSectionId.Recipes);
        viewModel.SelectSectionChipCommand.Execute(ReferenceSectionId.Badges);

        Assert.Equal(initialRootCount, viewModel.TreeRootNodes.Count);
        Assert.Contains(
            viewModel.TreeRootNodes,
            node => node.NodeKey == ReferenceBadgeBrowseSupport.AccoladesRootNodeKey);
    }

    [Fact]
    public void Live_badge_acquisition_updates_after_section_switching()
    {
        const string json =
            """
            {
              "manifest": {
                "catalogVersion": "item-ref-section-switch-acquisition-test",
                "homecomingCompatibility": { "buildMin": "Issue 28", "buildMax": "Issue 28" }
              },
              "items": [
                {
                  "catalogItemId": "BAD-92001",
                  "family": "Badge",
                  "subtype": "Exploration",
                  "currentDisplayName": "Section Switch Badge",
                  "activeStatus": "Active",
                  "verificationStatus": "VerifiedMultiSource",
                  "icon": "badge_tourist_01"
                }
              ],
              "aliases": [],
              "enhancementSets": [],
              "badges": [
                {
                  "catalogItemId": "BAD-92001",
                  "homecomingSourceId": "AtlasParkTour1",
                  "canonicalCategory": "Exploration",
                  "badgeType": 1,
                  "referenceKind": "ExplorationBadge",
                  "heroName": "Section Switch Badge",
                  "villainName": "Section Switch Badge",
                  "heroIcon": "badge_tourist_01",
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
                  "badgeCatalogItemId": "BAD-92001",
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
        var (repository, characterId) = CreateBadgeRepository();
        var viewed = new RecordingViewedContextService();
        var gameplay = new RecordingGameplaySessionManager();
        using var viewModel = CreateViewModel(catalog, viewed, repository, gameplay);

        viewed.SetCharacter(characterId, "acct-a");
        viewModel.SelectSectionChipCommand.Execute(ReferenceSectionId.Badges);
        var badgeNode = FindBadgeNode(viewModel, "BAD-92001");
        viewModel.SelectedNode = badgeNode;

        viewModel.SelectSectionChipCommand.Execute(ReferenceSectionId.Recipes);
        viewModel.SelectSectionChipCommand.Execute(ReferenceSectionId.Badges);
        var refreshedBadgeNode = FindBadgeNode(viewModel, "BAD-92001");
        Assert.False(refreshedBadgeNode.IsAcquired);

        repository.RecordAcquisition(
            characterId,
            "acct-a",
            "BAD-92001",
            "Section Switch Badge",
            DateTimeOffset.UtcNow);
        gameplay.RaiseStateChanged();

        Assert.True(refreshedBadgeNode.IsAcquired);
    }

    [Fact]
    public void Returning_to_badges_clears_cross_section_selection_coherently()
    {
        using var viewModel = CreateViewModel();
        viewModel.SelectSectionChipCommand.Execute(ReferenceSectionId.Badges);
        var explorationRoot = viewModel.TreeRootNodes
            .Single(node => node.NodeKey == ReferenceBadgeBrowseSupport.ExplorationRootNodeKey);
        var zoneNode = explorationRoot.Children.First();
        var badgeNode = zoneNode.Children.First(node => node.Kind == ReferenceEnhancementBrowseNodeKind.Badge);
        viewModel.SelectedNode = badgeNode;

        viewModel.SelectSectionChipCommand.Execute(ReferenceSectionId.Recipes);
        viewModel.SelectSectionChipCommand.Execute(ReferenceSectionId.Badges);

        Assert.Null(viewModel.SelectedNode);
        Assert.Equal(ReferenceEnhancementDetailKind.Empty, viewModel.Detail.Kind);
        Assert.NotEmpty(viewModel.TreeRootNodes);
    }

    private static (CharacterBadgeAcquisitionRepository Repository, CharacterRecordId CharacterId) CreateBadgeRepository()
    {
        var dataDirectory = Path.Combine(
            Path.GetTempPath(),
            "coh-reference-section-switch",
            Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dataDirectory);
        var repository = new CharacterBadgeAcquisitionRepository(
            new CharacterBadgeAcquisitionRepositoryOptions { DataDirectory = dataDirectory });
        return (repository, CharacterRecordId.CreateNew());
    }

    private static ReferenceViewModel CreateViewModel() =>
        new(
            new TestGameplaySessionContextSupport.FakeApplicationOrchestrator(),
            new FakeGameRuntimeService(),
            ProductionCatalog);

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
