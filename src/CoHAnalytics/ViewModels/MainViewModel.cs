using System.Collections.ObjectModel;
using System.Windows;
using CoHAnalytics.Homecoming;
using CoHAnalytics.Navigation;
using CoHAnalytics.Observations;
using CoHAnalytics.Orchestration.Contracts;
using CoHAnalytics.Orchestration.Models;
using CoHAnalytics.ReferenceData;
using CoHAnalytics.Services;
using CoHAnalytics.Shell;
using CoHAnalytics.Themes;
using CoHAnalytics.ViewModels.Workspaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CoHAnalytics.ViewModels;

public partial class MainViewModel : ObservableObject, IAccountsWorkspaceNavigation, IDisposable
{
    private readonly Dictionary<WorkspaceId, WorkspaceViewModelBase> _workspaces;
    private bool _disposed;
    private readonly IApplicationOrchestrator _orchestrator;
    private readonly AccountsViewModel _accountsViewModel;
    private readonly IViewedContextService _viewedContextService;
    private readonly IExternalUriService _externalUriService;

    public MainViewModel(
        IApplicationOrchestrator orchestrator,
        IGameRuntimeService gameRuntimeService,
        HomecomingInstallationService homecomingInstallationService,
        SettingsService settingsService,
        IFolderInteractionService folderInteractionService,
        HomecomingAccountDiscoveryService accountDiscoveryService,
        MidsInstallationService midsInstallationService,
        CharacterRepository characterRepository,
        MonitoringSessionManager monitoringSessionManager,
        GameplaySessionManager gameplaySessionManager,
        GameplaySessionIdentityReadService gameplaySessionIdentityReadService,
        ViewedContextService viewedContextService,
        GameplaySessionContextResolver gameplaySessionContextResolver,
        LogActivityService logActivityService,
        ApplicationActivityLogService activityLogService,
        IItemReferenceCatalog itemReferenceCatalog,
        IAcquisitionObservationService acquisitionObservationService,
        AcquisitionClassificationService acquisitionClassificationService,
        AccountAnonymityService accountAnonymityService,
        IInternalFeatureGate internalFeatureGate,
        ISessionStore sessionStore,
        IEnhancementIconCompositor enhancementIconCompositor,
        IHomecomingBoostMetadataProvider boostMetadataProvider,
        IInstalledGameAssetProvider installedGameAssetProvider,
        CharacterBadgeAcquisitionRepository characterBadgeAcquisitionRepository,
        ICharacterHistoricalPerformanceReadService characterHistoricalPerformanceReadService,
        ICharacterPerformanceObservationRepository characterPerformanceObservationRepository,
        CharacterBuildImportService characterBuildImportService,
        IBuiltInCharacterIconService builtInCharacterIconService,
        IExternalUriService externalUriService,
        ICustomCharacterIconService? customCharacterIconService = null)
    {
        NavigationItems = new ObservableCollection<NavigationItem>(
            NavigationItem.CreateDefaultNavigation(
                ThemeCatalog.Current,
                includeInternalTools: internalFeatureGate.IsDeveloperMode));

        _orchestrator = orchestrator;
        _viewedContextService = viewedContextService;
        _externalUriService = externalUriService;
        _orchestrator.SnapshotChanged += OnOrchestratorSnapshotChanged;

        _accountsViewModel = new AccountsViewModel(
            accountDiscoveryService,
            characterRepository,
            orchestrator,
            gameRuntimeService,
            monitoringSessionManager,
            gameplaySessionManager,
            gameplaySessionIdentityReadService,
            viewedContextService,
            logActivityService,
            characterBuildImportService,
            accountAnonymityService,
            characterBadgeAcquisitionRepository,
            itemReferenceCatalog,
            installedGameAssetProvider,
            builtInCharacterIconService,
            customCharacterIconService);

        _workspaces = new Dictionary<WorkspaceId, WorkspaceViewModelBase>
        {
            [WorkspaceId.Dashboard] = new DashboardViewModel(
                orchestrator,
                gameRuntimeService,
                settingsService,
                homecomingInstallationService,
                accountDiscoveryService,
                midsInstallationService,
                this,
                activityLogService,
                accountAnonymityService),
            [WorkspaceId.LiveSession] = new LiveSessionViewModel(
                orchestrator,
                gameRuntimeService,
                gameplaySessionManager,
                gameplaySessionIdentityReadService,
                gameplaySessionContextResolver,
                viewedContextService,
                sessionStore,
                itemReferenceCatalog: itemReferenceCatalog,
                enhancementIconCompositor: enhancementIconCompositor,
                boostMetadataProvider: boostMetadataProvider,
                installedGameAssetProvider: installedGameAssetProvider,
                accountAnonymityService: accountAnonymityService),
            [WorkspaceId.Accounts] = _accountsViewModel,
            [WorkspaceId.Analytics] = new AnalyticsViewModel(
                orchestrator,
                gameRuntimeService,
                gameplaySessionIdentityReadService,
                gameplaySessionContextResolver,
                viewedContextService,
                accountAnonymityService,
                characterHistoricalPerformanceReadService,
                characterPerformanceObservationRepository,
                characterRepository,
                accountDiscoveryService,
                new HistoricalSegmentDeleteConfirmationService()),
            [WorkspaceId.Reference] = new ReferenceViewModel(
                orchestrator,
                gameRuntimeService,
                itemReferenceCatalog,
                enhancementIconCompositor,
                boostMetadataProvider,
                installedGameAssetProvider,
                viewedContextService,
                characterBadgeAcquisitionRepository,
                gameplaySessionManager),
            [WorkspaceId.Diagnostics] = new DiagnosticsViewModel(
                orchestrator,
                gameRuntimeService,
                acquisitionObservationService),
            [WorkspaceId.Settings] = new SettingsViewModel(
                orchestrator,
                gameRuntimeService,
                homecomingInstallationService,
                settingsService,
                sessionStore,
                folderInteractionService),
        };

        if (internalFeatureGate.IsDeveloperMode)
        {
            _workspaces[WorkspaceId.InternalTools] = new InternalToolsViewModel(
                orchestrator,
                gameRuntimeService,
                accountAnonymityService,
                acquisitionObservationService,
                itemReferenceCatalog,
                acquisitionClassificationService);
        }

        SelectedWorkspace = _workspaces[WorkspaceId.Dashboard];
        ActiveWorkspaceLabel = SelectedWorkspace.Title;
        ApplyParserStatus(_orchestrator.Current);
    }

    public ObservableCollection<NavigationItem> NavigationItems { get; }

    [ObservableProperty]
    private WorkspaceViewModelBase _selectedWorkspace = null!;

    [ObservableProperty]
    private WorkspaceId _selectedWorkspaceId = WorkspaceId.Dashboard;

    [ObservableProperty]
    private string _activeWorkspaceLabel = string.Empty;

    [ObservableProperty]
    private string _parserStatusLabel = string.Empty;

    public string ApplicationVersion => ApplicationMetadata.ProductVersionLabel;

    /// <summary>
    /// Shell-level viewed navigation context for character-aware workspaces.
    /// </summary>
    public IViewedContextService ViewedContext => _viewedContextService;

    [RelayCommand]
    private void OpenSupportApp()
    {
        _ = _externalUriService.TryOpenUri(ApplicationExternalLinks.SupportAppUri, out _);
    }

    [RelayCommand]
    private void Navigate(WorkspaceId workspaceId)
    {
        if (!_workspaces.TryGetValue(workspaceId, out var workspace))
        {
            return;
        }

        if (SelectedWorkspace is ReferenceViewModel leavingReference)
        {
            leavingReference.SetWorkspaceActive(false);
        }

        SelectedWorkspaceId = workspaceId;
        SelectedWorkspace = workspace;
        ActiveWorkspaceLabel = workspace.Title;

        if (workspace is ReferenceViewModel enteringReference)
        {
            enteringReference.SetWorkspaceActive(true);
        }
    }

    public void OpenAccountsWorkspace(string? accountStableId = null)
    {
        if (!string.IsNullOrWhiteSpace(accountStableId))
        {
            _viewedContextService.SelectViewedAccount(accountStableId);
        }

        _accountsViewModel.RequestAccountSelection(accountStableId);
        Navigate(WorkspaceId.Accounts);
    }

    private void OnOrchestratorSnapshotChanged(object? sender, ApplicationStateChangedEventArgs e)
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
                dispatcher.Invoke(() => ApplyParserStatus(e.Snapshot));
                return;
            }
        }

        ApplyParserStatus(e.Snapshot);
    }

    private void ApplyParserStatus(ApplicationStateSnapshot snapshot)
    {
        ParserStatusLabel = DashboardApplicationStatusMapper.MapParserStatusLabel(snapshot);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _orchestrator.SnapshotChanged -= OnOrchestratorSnapshotChanged;

        foreach (var workspace in _workspaces.Values)
        {
            if (workspace is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }
}
