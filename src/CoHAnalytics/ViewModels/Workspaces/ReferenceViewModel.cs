using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using CoHAnalytics.Homecoming;
using CoHAnalytics.Models;
using CoHAnalytics.Orchestration.Contracts;
using CoHAnalytics.ReferenceData;
using CoHAnalytics.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CoHAnalytics.ViewModels.Workspaces;

public sealed partial class ReferenceViewModel : WorkspaceEnvironmentStatusViewModelBase, IDisposable
{
    private readonly IItemReferenceCatalog _itemReferenceCatalog;
    private readonly IEnhancementIconCompositor? _enhancementIconCompositor;
    private readonly IInstalledGameAssetProvider? _installedGameAssetProvider;
    private readonly IHomecomingBoostMetadataProvider? _boostMetadataProvider;
    private readonly IEnhancementHelpResolver? _enhancementHelpResolver;
    private readonly IViewedContextService? _viewedContextService;
    private readonly ICharacterBadgeAcquisitionRepository? _characterBadgeAcquisitionRepository;
    private readonly IGameplaySessionManager? _gameplaySessionManager;
    private ReferenceEnhancementBrowseTree? _browseTree;
    private ReferenceEnhancementBrowseBounds? _browseBounds;
    private readonly Dictionary<string, ReferenceEnhancementTreeNodeViewModel> _nodesByKey = new(StringComparer.Ordinal);
    private CharacterRecordId? _trackedCharacterRecordId;
    private HashSet<string> _trackedAcquiredBadgeIds = new(StringComparer.Ordinal);
    private bool _disposed;
    private bool _isRebuildingBrowseTree;
    private bool _pendingSectionRebuild;
    private bool _isWorkspaceActive;
    private string? _lastSelectedNodeKey;

    public bool IsRebuildingBrowseTree => _isRebuildingBrowseTree;

    public ReferenceViewModel(
        IApplicationOrchestrator orchestrator,
        IGameRuntimeService gameRuntimeService,
        IItemReferenceCatalog itemReferenceCatalog,
        IEnhancementIconCompositor? enhancementIconCompositor = null,
        IHomecomingBoostMetadataProvider? boostMetadataProvider = null,
        IInstalledGameAssetProvider? installedGameAssetProvider = null,
        IViewedContextService? viewedContextService = null,
        ICharacterBadgeAcquisitionRepository? characterBadgeAcquisitionRepository = null,
        IGameplaySessionManager? gameplaySessionManager = null)
        : base(orchestrator, gameRuntimeService)
    {
        _itemReferenceCatalog = itemReferenceCatalog;
        _enhancementIconCompositor = enhancementIconCompositor;
        _boostMetadataProvider = boostMetadataProvider;
        _installedGameAssetProvider = installedGameAssetProvider;
        _viewedContextService = viewedContextService;
        _characterBadgeAcquisitionRepository = characterBadgeAcquisitionRepository;
        _gameplaySessionManager = gameplaySessionManager;
        try
        {
            _enhancementHelpResolver = ItemReferenceCatalogFactory.CreateEmbeddedProductionResolver();
        }
        catch (InvalidOperationException)
        {
            _enhancementHelpResolver = null;
        }

        Detail = new ReferenceEnhancementDetailViewModel();

        SectionChips =
        [
            new ReferenceSectionChipViewModel("Enhancements", ReferenceSectionId.Enhancements, isEnabled: true, isActive: true),
            new ReferenceSectionChipViewModel("Recipes", ReferenceSectionId.Recipes, isEnabled: true),
            new ReferenceSectionChipViewModel("Salvage", ReferenceSectionId.Salvage, isEnabled: false),
            new ReferenceSectionChipViewModel("Inspirations", ReferenceSectionId.Inspirations, isEnabled: false),
            new ReferenceSectionChipViewModel("Badges", ReferenceSectionId.Badges, isEnabled: true),
            new ReferenceSectionChipViewModel("Powers", ReferenceSectionId.Powers, isEnabled: false)
        ];
        // Beta presentation: unfinished sections stay disabled and are hidden in ReferenceView.

        WireEnvironmentStatus();
        if (_viewedContextService is not null)
        {
            _viewedContextService.Changed += OnViewedContextChanged;
        }

        if (_gameplaySessionManager is not null)
        {
            _gameplaySessionManager.StateChanged += OnGameplaySessionStateChanged;
        }

        InitializeBrowseFilters();
        LoadBrowseTree();
        SyncAcquisitionTracking();
    }

    public override string Title => "Reference";

    public string Subtitle => "Searchable game knowledge workspace.";

    public ReferenceEnhancementDetailViewModel Detail { get; }

    public ObservableCollection<ReferenceEnhancementTreeNodeViewModel> TreeRootNodes { get; } = [];

    public ObservableCollection<ReferenceSectionChipViewModel> SectionChips { get; }

    public ObservableCollection<ReferenceSearchResultViewModel> SearchResults { get; } = [];

    public ObservableCollection<int> LevelFilterOptions { get; } = [];

    public ObservableCollection<ReferenceEnhancementRarityFilterOption> RarityFilterOptions { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSearchResults))]
    [NotifyPropertyChangedFor(nameof(ShowBrowseTree))]
    [NotifyPropertyChangedFor(nameof(ShowLoadError))]
    private bool _isCatalogLoaded;

    [ObservableProperty]
    private string? _catalogLoadFailureReason;

    [ObservableProperty]
    private string? _catalogVersionLabel;

    [ObservableProperty]
    private ReferenceEnhancementTreeNodeViewModel? _selectedNode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSearchResults))]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private int _currentEnhancementCount;

    [ObservableProperty]
    private int _currentSetCount;

    [ObservableProperty]
    private int _commonInventionEnhancementCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEnhancementBrowseFilters))]
    private ReferenceSectionId _activeSection = ReferenceSectionId.Enhancements;

    [ObservableProperty]
    private int _catalogMinimumLevel = 1;

    [ObservableProperty]
    private int _catalogMaximumLevel = EnhancementHelpResolverBoosterMultipliers.MaxBoostedPresentationLevel;

    [ObservableProperty]
    private int _enhancementMinLevel = 1;

    [ObservableProperty]
    private int _enhancementMaxLevel = EnhancementHelpResolverBoosterMultipliers.MaxBoostedPresentationLevel;

    [ObservableProperty]
    private ReferenceEnhancementRarityFilterOption? _selectedRarityFilter;

    public bool ShowEnhancementBrowseFilters =>
        ReferenceEnhancementBrowseSupport.ShouldShowEnhancementBrowseFilters(ActiveSection, IsCatalogLoaded);

    public bool ShowSearchResults => !string.IsNullOrWhiteSpace(SearchText) && SearchResults.Count > 0;

    public bool ShowBrowseTree => IsCatalogLoaded;

    public bool ShowLoadError => !IsCatalogLoaded;

    partial void OnSelectedNodeChanged(ReferenceEnhancementTreeNodeViewModel? value)
    {
        _lastSelectedNodeKey = value?.NodeKey;

        if (value is not null)
        {
            ExpandAncestors(value.NodeKey);
            if (value.Kind == ReferenceEnhancementBrowseNodeKind.Set
                || value.Kind == ReferenceEnhancementBrowseNodeKind.Enhancement
                || value.Kind == ReferenceEnhancementBrowseNodeKind.Recipe
                || value.Kind == ReferenceEnhancementBrowseNodeKind.BadgeZone
                || value.Kind == ReferenceEnhancementBrowseNodeKind.BadgePlaqueCollection
                || value.Kind == ReferenceEnhancementBrowseNodeKind.BadgePlaqueZoneSegment
                || value.Kind == ReferenceEnhancementBrowseNodeKind.BadgeHistoryPlaquesByBadgeBranch
                || value.Kind == ReferenceEnhancementBrowseNodeKind.BadgeHistoryPlaquesByZoneBranch
                || value.Kind == ReferenceEnhancementBrowseNodeKind.BadgeHistoryPlaqueZone
                || value.Kind == ReferenceEnhancementBrowseNodeKind.BadgeAccoladeCategory)
            {
                value.IsExpanded = true;
            }
        }

        RefreshDetail();
    }

    partial void OnSearchTextChanged(string value)
    {
        ApplySearch(value);
    }

    partial void OnEnhancementMinLevelChanged(int value)
    {
        if (!_isWorkspaceActive || _isRebuildingBrowseTree)
        {
            return;
        }

        if (value > EnhancementMaxLevel)
        {
            EnhancementMaxLevel = value;
            return;
        }

        RebuildBrowseTree();
    }

    partial void OnEnhancementMaxLevelChanged(int value)
    {
        if (!_isWorkspaceActive || _isRebuildingBrowseTree)
        {
            return;
        }

        if (value < EnhancementMinLevel)
        {
            EnhancementMinLevel = value;
            return;
        }

        RebuildBrowseTree();
    }

    partial void OnSelectedRarityFilterChanged(ReferenceEnhancementRarityFilterOption? value)
    {
        if (!_isWorkspaceActive || _isRebuildingBrowseTree)
        {
            return;
        }

        RebuildBrowseTree();
    }

    public void SetWorkspaceActive(bool isActive)
    {
        _isWorkspaceActive = isActive;
        if (isActive)
        {
            EnsureWorkspaceActivated();
        }
    }

    public void EnsureWorkspaceActivated()
    {
        if (!_itemReferenceCatalog.IsLoaded)
        {
            IsCatalogLoaded = false;
            CatalogLoadFailureReason = _itemReferenceCatalog.LoadFailureReason;
            return;
        }

        IsCatalogLoaded = true;
        CatalogLoadFailureReason = null;

        if (_browseBounds is null)
        {
            InitializeBrowseFilters();
        }

        if (TreeRootNodes.Count == 0 || _browseTree is null)
        {
            RebuildBrowseTree(resetSelection: false);
            return;
        }

        ReconcileBrowsePresentation();
    }

    partial void OnActiveSectionChanged(ReferenceSectionId value)
    {
        if (_isRebuildingBrowseTree)
        {
            _pendingSectionRebuild = true;
            return;
        }

        ApplyActiveSectionChange();
    }

    private void ApplyActiveSectionChange()
    {
        SearchText = string.Empty;
        InitializeBrowseFilters();
        RebuildBrowseTree(resetSelection: true);
    }

    [RelayCommand]
    private void SelectSectionChip(ReferenceSectionId sectionId)
    {
        foreach (var chip in SectionChips)
        {
            chip.IsActive = chip.SectionId == sectionId;
        }

        ActiveSection = sectionId;
    }

    [RelayCommand]
    private void NavigateToNode(string? nodeKey)
    {
        if (string.IsNullOrWhiteSpace(nodeKey))
        {
            return;
        }

        SelectNodeByKey(nodeKey);
        SearchText = string.Empty;
    }

    [RelayCommand]
    private void SelectSearchResult(ReferenceSearchResultViewModel? result)
    {
        if (result is null)
        {
            return;
        }

        var targetNodeKey = result.TargetNodeKey;
        DispatchDetailNavigation(() =>
        {
            SelectNodeByKey(targetNodeKey);
            SearchText = string.Empty;
        });
    }

    [RelayCommand]
    private void SelectSetFromDetail(string? setId) =>
        DispatchDetailNavigation(() => NavigateToEnhancementOrSet(setId, isSet: true));

    [RelayCommand]
    private void SelectEnhancementFromDetail(string? enhancementId) =>
        DispatchDetailNavigation(() => NavigateToEnhancementOrSet(enhancementId, isSet: false));

    [RelayCommand]
    private void SelectProducedEnhancementFromDetail(string? enhancementId)
    {
        if (string.IsNullOrWhiteSpace(enhancementId))
        {
            return;
        }

        DispatchDetailNavigation(() =>
        {
            SelectSectionChip(ReferenceSectionId.Enhancements);
            NavigateToEnhancementOrSet(enhancementId, isSet: false);
        });
    }

    [RelayCommand]
    private void SelectBranchItem(ReferenceEnhancementBranchSummaryItem? item)
    {
        if (item is null)
        {
            return;
        }

        var targetNodeKey = item.TargetNodeKey;
        DispatchDetailNavigation(() => NavigateToNode(targetNodeKey));
    }

    [RelayCommand]
    private void CopyThumbtack()
    {
        var thumbtackCommand = Detail.ThumbtackCommand;
        if (string.IsNullOrWhiteSpace(thumbtackCommand))
        {
            return;
        }

        TrySetClipboardText(thumbtackCommand);
    }

    private static void TrySetClipboardText(string text)
    {
        const int clipboardOpenError = unchecked((int)0x800401D0);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Clipboard.SetText(text);
                return;
            }
            catch (COMException ex) when (ex.HResult == clipboardOpenError)
            {
                if (attempt >= 4)
                {
                    return;
                }

                Thread.Sleep(20 * (attempt + 1));
            }
        }
    }

    private void DispatchDetailNavigation(Action navigationAction)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
        {
            navigationAction();
            return;
        }

        dispatcher.BeginInvoke(DispatcherPriority.Input, navigationAction);
    }

    public void LoadBrowseTree()
    {
        TreeRootNodes.Clear();
        _nodesByKey.Clear();
        SearchResults.Clear();

        IsCatalogLoaded = _itemReferenceCatalog.IsLoaded;
        CatalogLoadFailureReason = _itemReferenceCatalog.LoadFailureReason;
        CatalogVersionLabel = _itemReferenceCatalog.Manifest?.CatalogVersion;

        if (!IsCatalogLoaded)
        {
            Detail.Apply(new ReferenceEnhancementDetailModel
            {
                Kind = ReferenceEnhancementDetailKind.LoadError,
                Title = "Reference unavailable",
                ErrorMessage = CatalogLoadFailureReason ?? "The reference catalog failed to load."
            });
            return;
        }

        InitializeBrowseFilters();
        RebuildBrowseTree(resetSelection: true);
    }

    private void InitializeBrowseFilters()
    {
        if (!_itemReferenceCatalog.IsLoaded)
        {
            return;
        }

        _browseBounds = ActiveSection switch
        {
            ReferenceSectionId.Recipes => ReferenceRecipeBrowseSupport.GetBrowseBounds(_itemReferenceCatalog),
            ReferenceSectionId.Badges => new ReferenceEnhancementBrowseBounds
            {
                MinimumLevel = 1,
                MaximumLevel = ReferenceRecipeBrowseSupport.MaxReferenceLevel
            },
            _ => ReferenceEnhancementBrowseSupport.GetBrowseBounds(_itemReferenceCatalog)
        };
        CatalogMinimumLevel = _browseBounds.MinimumLevel;
        CatalogMaximumLevel = _browseBounds.MaximumLevel;

        LevelFilterOptions.Clear();
        for (var level = CatalogMinimumLevel; level <= CatalogMaximumLevel; level++)
        {
            LevelFilterOptions.Add(level);
        }

        RarityFilterOptions.Clear();
        if (ActiveSection is not ReferenceSectionId.Badges)
        {
            var rarityOptions = ActiveSection == ReferenceSectionId.Recipes
                ? ReferenceRecipeBrowseSupport.GetRarityFilterOptions(_itemReferenceCatalog)
                : ReferenceEnhancementBrowseSupport.GetRarityFilterOptions(_itemReferenceCatalog);
            foreach (var option in rarityOptions)
            {
                RarityFilterOptions.Add(option);
            }
        }

        _isRebuildingBrowseTree = true;
        EnhancementMinLevel = CatalogMinimumLevel;
        EnhancementMaxLevel = CatalogMaximumLevel;
        SelectedRarityFilter = RarityFilterOptions.FirstOrDefault();
        _isRebuildingBrowseTree = false;
    }

    private void RebuildBrowseTree(bool resetSelection = false)
    {
        if (!_itemReferenceCatalog.IsLoaded || _browseBounds is null)
        {
            return;
        }

        var buildingSection = ActiveSection;
        var previousNodeKey = resetSelection ? null : SelectedNode?.NodeKey;
        var expandedNodeKeys = resetSelection ? null : CollectExpandedNodeKeys();
        var filter = ReferenceEnhancementBrowseSupport.NormalizeFilter(
            new ReferenceEnhancementBrowseFilter
            {
                MinLevel = EnhancementMinLevel,
                MaxLevel = EnhancementMaxLevel,
                Rarity = SelectedRarityFilter?.Rarity
            },
            _browseBounds);
        if (ActiveSection == ReferenceSectionId.Recipes
            && filter.MaxLevel > ReferenceRecipeBrowseSupport.MaxReferenceLevel)
        {
            filter = new ReferenceEnhancementBrowseFilter
            {
                MinLevel = Math.Min(filter.MinLevel, ReferenceRecipeBrowseSupport.MaxReferenceLevel),
                MaxLevel = ReferenceRecipeBrowseSupport.MaxReferenceLevel,
                Rarity = filter.Rarity
            };
        }

        _isRebuildingBrowseTree = true;
        if (filter.MinLevel != EnhancementMinLevel)
        {
            EnhancementMinLevel = filter.MinLevel;
        }

        if (filter.MaxLevel != EnhancementMaxLevel)
        {
            EnhancementMaxLevel = filter.MaxLevel;
        }

        TreeRootNodes.Clear();
        _nodesByKey.Clear();

        _browseTree = buildingSection switch
        {
            ReferenceSectionId.Recipes => ReferenceRecipeBrowseSupport.BuildBrowseTree(
                _itemReferenceCatalog,
                filter),
            ReferenceSectionId.Badges => ReferenceBadgeBrowseSupport.BuildBrowseTree(
                _itemReferenceCatalog,
                GetAcquiredBadgeIds()),
            _ => ReferenceEnhancementBrowseSupport.BuildBrowseTree(_itemReferenceCatalog, filter)
        };
        CurrentEnhancementCount = _browseTree.Census.CurrentEnhancementCount;
        CurrentSetCount = _browseTree.Census.CurrentSetCount;
        CommonInventionEnhancementCount = _browseTree.Census.CommonInventionEnhancementCount;

        foreach (var rootNode in _browseTree.RootNodes)
        {
            var rootViewModel = new ReferenceEnhancementTreeNodeViewModel(
                rootNode,
                _itemReferenceCatalog,
                _enhancementIconCompositor,
                _boostMetadataProvider,
                _installedGameAssetProvider);
            IndexNodeViewModels(rootViewModel);
            TreeRootNodes.Add(rootViewModel);
        }

        if (resetSelection)
        {
            SelectedNode = null;
            Detail.Apply(CreateEmptyDetail());
        }
        else
        {
            var replacementKey = buildingSection switch
            {
                ReferenceSectionId.Recipes => ReferenceRecipeBrowseSupport.TryResolveReplacementNodeKey(
                    _browseTree,
                    previousNodeKey),
                ReferenceSectionId.Badges => previousNodeKey,
                _ => ReferenceEnhancementBrowseSupport.TryResolveReplacementNodeKey(
                    _browseTree,
                    previousNodeKey)
            };
            if (!string.IsNullOrWhiteSpace(replacementKey)
                && SelectNodeByKey(replacementKey, preserveSearchHighlights: true))
            {
                RestoreExpandedNodeKeys(expandedNodeKeys);
                ExpandAncestors(replacementKey);
            }
            else
            {
                SelectedNode = null;
                Detail.Apply(CreateEmptyDetail());
            }
        }

        _isRebuildingBrowseTree = false;

        if (_pendingSectionRebuild)
        {
            _pendingSectionRebuild = false;
            ApplyActiveSectionChange();
            return;
        }

        if (ActiveSection != buildingSection)
        {
            RebuildBrowseTree(resetSelection: true);
            return;
        }

        if (ActiveSection == ReferenceSectionId.Badges)
        {
            SyncAcquisitionTracking();
        }

        ApplySearch(SearchText);
    }

    private void ReconcileBrowsePresentation()
    {
        if (_browseTree is null)
        {
            return;
        }

        if (SelectedNode is not null)
        {
            if (!_nodesByKey.ContainsKey(SelectedNode.NodeKey))
            {
                SelectedNode = null;
            }
            else if (!ReferenceEquals(SelectedNode, _nodesByKey[SelectedNode.NodeKey]))
            {
                SelectNodeByKey(SelectedNode.NodeKey, preserveSearchHighlights: true);
            }
        }

        if (SelectedNode is null && !string.IsNullOrWhiteSpace(_lastSelectedNodeKey))
        {
            if (!SelectNodeByKey(_lastSelectedNodeKey, preserveSearchHighlights: true))
            {
                _lastSelectedNodeKey = null;
                Detail.Apply(CreateEmptyDetail());
            }
        }
        else if (SelectedNode is not null
            && !IsNodeKindCompatibleWithSection(SelectedNode.Kind, ActiveSection))
        {
            SelectedNode = null;
            _lastSelectedNodeKey = null;
            Detail.Apply(CreateEmptyDetail());
        }
        else
        {
            RefreshDetail();
        }

        if (TreeRootNodes.Count == 0)
        {
            SelectedNode = null;
            _lastSelectedNodeKey = null;
            Detail.Apply(CreateEmptyDetail());
            return;
        }

        if (SelectedNode is null
            && Detail.Kind is not ReferenceEnhancementDetailKind.Empty
            and not ReferenceEnhancementDetailKind.LoadError)
        {
            Detail.Apply(CreateEmptyDetail());
        }
    }

    private void RefreshDetail()
    {
        if (_browseTree is null)
        {
            return;
        }

        if (SelectedNode is not null && !IsNodeKindCompatibleWithSection(SelectedNode.Kind, ActiveSection))
        {
            SelectedNode = null;
            Detail.Apply(CreateEmptyDetail());
            return;
        }

        var selectedBrowseNode = SelectedNode is null
            ? null
            : _browseTree.NodesByKey.GetValueOrDefault(SelectedNode.NodeKey);

        int? presentationLevel = SelectedNode?.Kind switch
        {
            ReferenceEnhancementBrowseNodeKind.Level => SelectedNode.PresentationLevel,
            _ => null
        };

        if (ActiveSection == ReferenceSectionId.Recipes)
        {
            Detail.Apply(ReferenceRecipeBrowseSupport.BuildDetail(
                _itemReferenceCatalog,
                selectedBrowseNode,
                _browseTree,
                _enhancementIconCompositor,
                _boostMetadataProvider,
                _installedGameAssetProvider,
                presentationLevel,
                _enhancementHelpResolver));
            return;
        }

        if (ActiveSection == ReferenceSectionId.Badges)
        {
            Detail.Apply(ReferenceBadgeBrowseSupport.BuildDetail(
                _itemReferenceCatalog,
                selectedBrowseNode,
                GetAcquiredBadgeIds(),
                _installedGameAssetProvider));
            return;
        }

        Detail.Apply(ReferenceEnhancementBrowseSupport.BuildDetail(
            _itemReferenceCatalog,
            selectedBrowseNode,
            _browseTree,
            _enhancementHelpResolver,
            presentationLevel,
            _enhancementIconCompositor,
            _boostMetadataProvider));
    }

    private void ApplySearch(string searchText)
    {
        SearchResults.Clear();
        ClearSearchHighlights();

        if (_browseTree is null || string.IsNullOrWhiteSpace(searchText))
        {
            return;
        }

        var term = searchText.Trim();
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);
        if (ActiveSection == ReferenceSectionId.Recipes)
        {
            ApplyRecipeSearch(term, seenKeys);
            return;
        }

        if (ActiveSection == ReferenceSectionId.Badges)
        {
            ApplyBadgeSearch(term, seenKeys);
            return;
        }

        foreach (var set in _itemReferenceCatalog
                     .GetEnhancementSets(ReferenceCatalogQueryScope.CurrentHomecoming)
                     .Values
                     .Where(set => set.CurrentDisplayName.Contains(term, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(set => set.CurrentDisplayName, StringComparer.OrdinalIgnoreCase)
                     .Take(20))
        {
            var node = ReferenceEnhancementBrowseSupport.FindNodeForSetId(_browseTree, set.CatalogItemId);
            if (node is null || !seenKeys.Add(node.NodeKey))
            {
                continue;
            }

            SearchResults.Add(new ReferenceSearchResultViewModel(
                set.CurrentDisplayName,
                node.NodeKey,
                "Enhancement Sets"));
        }

        foreach (var result in _itemReferenceCatalog.Search(
                     term,
                     ReferenceItemFamily.Enhancement,
                     maximumResults: 30,
                     queryScope: ReferenceCatalogQueryScope.CurrentHomecoming))
        {
            var node = ReferenceEnhancementBrowseSupport.FindNodeForEnhancementId(_browseTree, result.CatalogItemId);
            if (node is null || !seenKeys.Add(node.NodeKey))
            {
                continue;
            }

            var groupLabel = string.IsNullOrWhiteSpace(result.EnhancementSetName)
                ? "Common Invention Enhancements"
                : "Enhancements";

            SearchResults.Add(new ReferenceSearchResultViewModel(
                result.CurrentDisplayName,
                node.NodeKey,
                groupLabel));
        }
    }

    private void ApplyBadgeSearch(string term, HashSet<string> seenKeys)
    {
        if (_browseTree is null)
        {
            return;
        }

        foreach (var zoneNode in _browseTree.NodesByKey.Values
                     .Where(node => node.Kind == ReferenceEnhancementBrowseNodeKind.BadgeZone
                         && node.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(node => node.DisplayName, StringComparer.OrdinalIgnoreCase)
                     .Take(20))
        {
            if (!seenKeys.Add(zoneNode.NodeKey))
            {
                continue;
            }

            SearchResults.Add(new ReferenceSearchResultViewModel(
                zoneNode.DisplayName,
                zoneNode.NodeKey,
                "Exploration Zones"));
        }

        foreach (var badgeNode in _browseTree.NodesByKey.Values
                     .Where(node => (node.Kind is ReferenceEnhancementBrowseNodeKind.Badge
                             or ReferenceEnhancementBrowseNodeKind.BadgeAccolade)
                         && node.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(node => node.DisplayName, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(node => node.NodeKey, StringComparer.Ordinal)
                     .Take(30))
        {
            if (!seenKeys.Add(badgeNode.NodeKey))
            {
                continue;
            }

            var groupLabel = ResolveBadgeSearchGroupLabel(badgeNode);

            SearchResults.Add(new ReferenceSearchResultViewModel(
                badgeNode.DisplayName,
                badgeNode.NodeKey,
                groupLabel));
        }

        foreach (var collectionNode in _browseTree.NodesByKey.Values
                     .Where(node => node.Kind == ReferenceEnhancementBrowseNodeKind.BadgePlaqueCollection
                         && node.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(node => node.DisplayName, StringComparer.OrdinalIgnoreCase)
                     .Take(20))
        {
            if (!seenKeys.Add(collectionNode.NodeKey))
            {
                continue;
            }

            SearchResults.Add(new ReferenceSearchResultViewModel(
                collectionNode.DisplayName,
                collectionNode.NodeKey,
                "History Plaque Collections"));
        }

        foreach (var plaqueNode in _browseTree.NodesByKey.Values
                     .Where(node => node.Kind == ReferenceEnhancementBrowseNodeKind.HistoryPlaque
                         && node.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(node => node.DisplayName, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(node => node.NodeKey, StringComparer.Ordinal)
                     .Take(30))
        {
            if (!seenKeys.Add(plaqueNode.NodeKey))
            {
                continue;
            }

            SearchResults.Add(new ReferenceSearchResultViewModel(
                plaqueNode.DisplayName,
                plaqueNode.NodeKey,
                plaqueNode.PlaqueCollectionName ?? "History Plaques"));
        }
    }

    private string ResolveBadgeSearchGroupLabel(ReferenceEnhancementBrowseNode badgeNode)
    {
        if (badgeNode.IsCollectionCompletionBadge)
        {
            return badgeNode.PlaqueCollectionName ?? "History Plaque Collections";
        }

        if (badgeNode.Kind == ReferenceEnhancementBrowseNodeKind.BadgeAccolade)
        {
            if (!string.IsNullOrWhiteSpace(badgeNode.ParentNodeKey)
                && _browseTree!.NodesByKey.TryGetValue(badgeNode.ParentNodeKey, out var categoryNode)
                && categoryNode.Kind == ReferenceEnhancementBrowseNodeKind.BadgeAccoladeCategory)
            {
                return $"Accolades — {categoryNode.DisplayName}";
            }

            return "Accolades";
        }

        var zoneNodeKey = badgeNode.ParentNodeKey;
        if (!string.IsNullOrWhiteSpace(zoneNodeKey)
            && _browseTree!.NodesByKey.TryGetValue(zoneNodeKey, out var zoneNode))
        {
            return zoneNode.DisplayName;
        }

        return "Exploration";
    }

    private void ApplyRecipeSearch(string term, HashSet<string> seenKeys)
    {
        if (_browseTree is null)
        {
            return;
        }

        foreach (var setNode in _browseTree.NodesByKey.Values
                     .Where(node => node.Kind == ReferenceEnhancementBrowseNodeKind.Set
                         && node.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(node => node.DisplayName, StringComparer.OrdinalIgnoreCase)
                     .Take(20))
        {
            if (!seenKeys.Add(setNode.NodeKey))
            {
                continue;
            }

            SearchResults.Add(new ReferenceSearchResultViewModel(
                setNode.DisplayName,
                setNode.NodeKey,
                "Recipe Sets"));
        }

        foreach (var result in _itemReferenceCatalog.Search(
                     term,
                     ReferenceItemFamily.Recipe,
                     maximumResults: 30))
        {
            var node = ReferenceRecipeBrowseSupport.FindNodeForRecipeId(_browseTree, result.CatalogItemId);
            if (node is null || !seenKeys.Add(node.NodeKey))
            {
                continue;
            }

            var groupLabel = string.IsNullOrWhiteSpace(result.EnhancementSetName)
                ? "Common Invention Recipes"
                : "Recipes";

            SearchResults.Add(new ReferenceSearchResultViewModel(
                result.CurrentDisplayName,
                node.NodeKey,
                groupLabel));
        }
    }

    private bool SelectNodeByKey(string nodeKey, bool preserveSearchHighlights = false)
    {
        if (!_nodesByKey.TryGetValue(nodeKey, out var node))
        {
            return false;
        }

        ClearSelection(TreeRootNodes);
        node.IsSelected = true;
        SelectedNode = node;
        ExpandAncestors(nodeKey);
        if (node.Kind is ReferenceEnhancementBrowseNodeKind.Set
            or ReferenceEnhancementBrowseNodeKind.Enhancement
            or ReferenceEnhancementBrowseNodeKind.Recipe
            or ReferenceEnhancementBrowseNodeKind.Badge
            or ReferenceEnhancementBrowseNodeKind.BadgeAccolade
            or ReferenceEnhancementBrowseNodeKind.HistoryPlaque)
        {
            node.IsExpanded = true;
        }

        if (!preserveSearchHighlights)
        {
            node.IsSearchHighlighted = true;
        }

        return true;
    }

    private void NavigateToEnhancementOrSet(string? id, bool isSet)
    {
        if (_browseTree is null || string.IsNullOrWhiteSpace(id))
        {
            return;
        }

        var node = isSet
            ? ReferenceEnhancementBrowseSupport.FindNodeForSetId(_browseTree, id)
            : ReferenceEnhancementBrowseSupport.FindNodeForEnhancementId(_browseTree, id);

        if (node is null)
        {
            return;
        }

        SelectNodeByKey(node.NodeKey);
    }

    private void ExpandAncestors(string nodeKey)
    {
        if (_browseTree is null)
        {
            return;
        }

        foreach (var ancestorKey in ReferenceEnhancementBrowseSupport.ExpandAncestorNodeKeys(
                     nodeKey,
                     _browseTree.NodesByKey))
        {
            if (_nodesByKey.TryGetValue(ancestorKey, out var ancestor))
            {
                ancestor.IsExpanded = true;
            }
        }
    }

    private void IndexNodeViewModels(ReferenceEnhancementTreeNodeViewModel node)
    {
        _nodesByKey[node.NodeKey] = node;
        foreach (var child in node.Children)
        {
            IndexNodeViewModels(child);
        }
    }

    private static void ClearSelection(IEnumerable<ReferenceEnhancementTreeNodeViewModel> nodes)
    {
        foreach (var node in nodes)
        {
            node.IsSelected = false;
            ClearSelection(node.Children);
        }
    }

    private ReferenceEnhancementDetailModel CreateEmptyDetail() =>
        ActiveSection switch
        {
            ReferenceSectionId.Recipes => new ReferenceEnhancementDetailModel
            {
                Kind = ReferenceEnhancementDetailKind.Empty,
                Title = "Recipes",
                Summary = "Select a Recipe or Recipe Set to view details."
            },
            ReferenceSectionId.Badges => new ReferenceEnhancementDetailModel
            {
                Kind = ReferenceEnhancementDetailKind.Empty,
                Title = "Badges",
                Summary = "Select Exploration zones, History Plaque collections by badge or zone, Accolades, or badges to browse."
            },
            _ => new ReferenceEnhancementDetailModel
            {
                Kind = ReferenceEnhancementDetailKind.Empty,
                Title = "Enhancements",
                Summary = "Select an Enhancement Set or Enhancement to view details."
            }
        };

    private static bool IsNodeKindCompatibleWithSection(
        ReferenceEnhancementBrowseNodeKind nodeKind,
        ReferenceSectionId section)
    {
        switch (section)
        {
            case ReferenceSectionId.Badges:
                return nodeKind is ReferenceEnhancementBrowseNodeKind.BadgeExplorationRoot
                    or ReferenceEnhancementBrowseNodeKind.BadgeZone
                    or ReferenceEnhancementBrowseNodeKind.Badge
                    or ReferenceEnhancementBrowseNodeKind.BadgeHistoryPlaquesRoot
                    or ReferenceEnhancementBrowseNodeKind.BadgePlaqueCollection
                    or ReferenceEnhancementBrowseNodeKind.BadgePlaqueZoneSegment
                    or ReferenceEnhancementBrowseNodeKind.HistoryPlaque
                    or ReferenceEnhancementBrowseNodeKind.BadgeAccoladesRoot
                    or ReferenceEnhancementBrowseNodeKind.BadgeAccolade
                    or ReferenceEnhancementBrowseNodeKind.BadgeAccoladeCategory
                    or ReferenceEnhancementBrowseNodeKind.BadgeHistoryPlaquesByBadgeBranch
                    or ReferenceEnhancementBrowseNodeKind.BadgeHistoryPlaquesByZoneBranch
                    or ReferenceEnhancementBrowseNodeKind.BadgeHistoryPlaqueZone;
            case ReferenceSectionId.Recipes:
                return nodeKind is ReferenceEnhancementBrowseNodeKind.RootSetsBranch
                    or ReferenceEnhancementBrowseNodeKind.RootCommonBranch
                    or ReferenceEnhancementBrowseNodeKind.Category
                    or ReferenceEnhancementBrowseNodeKind.Set
                    or ReferenceEnhancementBrowseNodeKind.CommonGroup
                    or ReferenceEnhancementBrowseNodeKind.Enhancement
                    or ReferenceEnhancementBrowseNodeKind.Level
                    or ReferenceEnhancementBrowseNodeKind.Recipe;
            case ReferenceSectionId.Enhancements:
                return nodeKind is ReferenceEnhancementBrowseNodeKind.RootSetsBranch
                    or ReferenceEnhancementBrowseNodeKind.RootCommonBranch
                    or ReferenceEnhancementBrowseNodeKind.Category
                    or ReferenceEnhancementBrowseNodeKind.Set
                    or ReferenceEnhancementBrowseNodeKind.CommonGroup
                    or ReferenceEnhancementBrowseNodeKind.Enhancement
                    or ReferenceEnhancementBrowseNodeKind.Level;
            default:
                return false;
        }
    }

    private HashSet<string> GetAcquiredBadgeIds()
    {
        var characterRecordId = _viewedContextService?.Current.CharacterRecordId;
        if (characterRecordId is null || _characterBadgeAcquisitionRepository is null)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        var acquiredBadgeIds = _characterBadgeAcquisitionRepository
            .GetAcquiredBadgeIds(characterRecordId)
            .ToHashSet(StringComparer.Ordinal);
        ExpandAcquiredBadgeIdsFromAccolades(acquiredBadgeIds);
        return acquiredBadgeIds;
    }

    private void ExpandAcquiredBadgeIdsFromAccolades(HashSet<string> acquiredBadgeIds)
    {
        if (!_itemReferenceCatalog.IsLoaded || acquiredBadgeIds.Count == 0)
        {
            return;
        }

        foreach (var accoladeBadgeId in acquiredBadgeIds.ToArray())
        {
            foreach (var requirement in _itemReferenceCatalog.GetAccoladeRequirements(accoladeBadgeId))
            {
                if (!string.IsNullOrWhiteSpace(requirement.PrerequisiteBadgeId))
                {
                    acquiredBadgeIds.Add(requirement.PrerequisiteBadgeId);
                }
            }
        }
    }

    private void OnViewedContextChanged(object? sender, ViewedContextChangedEventArgs e)
    {
        if (ActiveSection != ReferenceSectionId.Badges || !_itemReferenceCatalog.IsLoaded)
        {
            return;
        }

        RebuildBrowseTree(resetSelection: false);
        SyncAcquisitionTracking();
    }

    private void OnGameplaySessionStateChanged(object? sender, GameplaySessionManagerChangedEventArgs e) =>
        DispatchReferenceRefresh(TryRefreshBadgeAcquisitionFromGameplaySession);

    private void TryRefreshBadgeAcquisitionFromGameplaySession()
    {
        if (ActiveSection != ReferenceSectionId.Badges
            || !_itemReferenceCatalog.IsLoaded
            || _viewedContextService?.Current.CharacterRecordId is null)
        {
            return;
        }

        var currentCharacterRecordId = _viewedContextService.Current.CharacterRecordId;
        var currentAcquiredBadgeIds = GetAcquiredBadgeIds();
        if (_trackedCharacterRecordId != currentCharacterRecordId)
        {
            _trackedCharacterRecordId = currentCharacterRecordId;
        }

        if (currentAcquiredBadgeIds.SetEquals(_trackedAcquiredBadgeIds))
        {
            return;
        }

        _trackedAcquiredBadgeIds = currentAcquiredBadgeIds;
        RefreshBadgeAcquisitionPresentation();
    }

    private void RefreshBadgeAcquisitionPresentation()
    {
        if (ActiveSection != ReferenceSectionId.Badges)
        {
            return;
        }

        var acquiredBadgeIds = GetAcquiredBadgeIds();
        foreach (var node in _nodesByKey.Values)
        {
            if (string.IsNullOrWhiteSpace(node.BadgeId))
            {
                continue;
            }

            node.ApplyAcquiredState(acquiredBadgeIds.Contains(node.BadgeId));
        }

        RefreshDetail();
    }

    private void SyncAcquisitionTracking()
    {
        _trackedCharacterRecordId = _viewedContextService?.Current.CharacterRecordId;
        _trackedAcquiredBadgeIds = GetAcquiredBadgeIds();
    }

    private HashSet<string> CollectExpandedNodeKeys()
    {
        var expandedNodeKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in _nodesByKey.Values)
        {
            if (node.IsExpanded)
            {
                expandedNodeKeys.Add(node.NodeKey);
            }
        }

        return expandedNodeKeys;
    }

    private void RestoreExpandedNodeKeys(HashSet<string>? expandedNodeKeys)
    {
        if (expandedNodeKeys is null)
        {
            return;
        }

        foreach (var nodeKey in expandedNodeKeys)
        {
            if (_nodesByKey.TryGetValue(nodeKey, out var node))
            {
                node.IsExpanded = true;
            }
        }
    }

    private void DispatchReferenceRefresh(Action refreshAction)
    {
        if (_disposed)
        {
            return;
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null)
        {
            if (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
            {
                return;
            }

            if (!dispatcher.CheckAccess())
            {
                dispatcher.BeginInvoke(refreshAction);
                return;
            }
        }

        refreshAction();
    }

    private void ClearSearchHighlights()
    {
        foreach (var node in _nodesByKey.Values)
        {
            node.IsSearchHighlighted = false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_viewedContextService is not null)
        {
            _viewedContextService.Changed -= OnViewedContextChanged;
        }

        if (_gameplaySessionManager is not null)
        {
            _gameplaySessionManager.StateChanged -= OnGameplaySessionStateChanged;
        }

        UnwireEnvironmentStatus();
    }
}
