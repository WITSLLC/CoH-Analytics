using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CoHAnalytics.Models;
using CoHAnalytics.Orchestration.Contracts;
using CoHAnalytics.ReferenceData;
using CoHAnalytics.Services;
using CoHAnalytics.Homecoming;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CoHAnalytics.ViewModels.Workspaces;

/// <summary>Data state of the Live Session workspace. The layout is identical in every state.</summary>
public enum LiveSessionWorkspaceState
{
    /// <summary>No gameplay session has been observed yet.</summary>
    Waiting,

    /// <summary>A gameplay session is active and telemetry is updating.</summary>
    Live,

    /// <summary>The gameplay session ended; the last values remain on screen as a summary.</summary>
    Frozen
}

public sealed partial class LiveSessionViewModel : WorkspaceEnvironmentStatusViewModelBase, IDisposable
{
    private const string WaitingDurationLabel = "--:--:--";
    private const string ZeroLabel = "0";

    /// <summary>
    /// Fixed currency rows in approved presentation order. <c>SourceKey</c> matches the
    /// singular name emitted by reward currency telemetry; rows without a match read zero.
    /// </summary>
    private static readonly (
        string DisplayName,
        string SourceKey,
        bool StartsGroup,
        LiveSessionCurrencyNameColor NameColor)[] CurrencyRowDefinitions =
    [
        ("Reward Merits", "Reward Merit", false, LiveSessionCurrencyNameColor.Bronze),
        ("Vanguard Merits", "Vanguard Merit", false, LiveSessionCurrencyNameColor.Gold),
        ("Astral Merits", "Astral Merit", true, LiveSessionCurrencyNameColor.Blue),
        ("Empyrean Merits", "Empyrean Merit", false, LiveSessionCurrencyNameColor.Orange),
        ("Incarnate Threads", "Incarnate Thread", true, LiveSessionCurrencyNameColor.White),
        ("Incarnate Shards", "Incarnate Shard", false, LiveSessionCurrencyNameColor.White),
        ("Unstable Aether", "Unstable Aether", true, LiveSessionCurrencyNameColor.Amber),
        ("Prismatic Aether", "Prismatic Aether", false, LiveSessionCurrencyNameColor.Purple),
        ("Nightmare Obol", "Nightmare Obol", false, LiveSessionCurrencyNameColor.Orange)
    ];

    private static readonly (string DisplayName, string SourceKey, bool StartsGroup)[] SpecialRewardRowDefinitions =
    [
        ("Enhancement Converters", "Enhancement Converter", false),
        ("Enhancement Catalysts", "Enhancement Catalyst", false),
        ("Enhancement Unslotters", "Enhancement Unslotter", false)
    ];

    private readonly IGameplaySessionManager _gameplaySessionManager;
    private readonly IGameplaySessionIdentityReadService _identityReadService;
    private readonly IGameplaySessionContextResolver _gameplaySessionContextResolver;
    private readonly IViewedContextService _viewedContextService;
    private readonly ISessionStore _sessionStore;
    private readonly IItemReferenceCatalog? _itemReferenceCatalog;
    private readonly IEnhancementIconCompositor? _enhancementIconCompositor;
    private readonly IHomecomingBoostMetadataProvider? _boostMetadataProvider;
    private readonly IInstalledGameAssetProvider? _installedGameAssetProvider;
    private readonly AccountAnonymityService _accountAnonymityService;
    private readonly DispatcherTimer? _telemetryRefreshTimer;

    private DateTimeOffset? _sessionStartedAt;
    private DateTimeOffset? _sessionTimingEndAt;
    private Guid? _currentLiveSessionId;
    private bool _liveSessionPersisted;
    private LiveMonitoringContextIdentityReadModel? _lastKnownActiveContext;
    private long _sessionExperienceGained;
    private long _sessionGameplayInfluenceGained;

    /// <summary>
    /// Presentation baselines established by Clear Session. Performance totals and duration are
    /// measured from these offsets while collections continue to show authoritative session totals.
    /// </summary>
    private long _baselineExperienceGained;

    private long _baselineGameplayInfluenceGained;

    private CombatScaledAmount _baselineDamageDealt;

    private DateTimeOffset? _presentationDurationAnchor;

    private MonitoringContextId? _displayedContextId;

    private readonly Dictionary<MonitoringContextId, LiveSessionContextPresentationState> _presentationByContext =
        new();

    private readonly Dictionary<MonitoringContextId, ContextLiveSessionPersistenceState> _persistenceByContext =
        new();

    private Dictionary<string, long> _baselineCurrencyTotals =
        new(StringComparer.OrdinalIgnoreCase);

    private Dictionary<string, long> _baselineSalvageTotals =
        new(StringComparer.OrdinalIgnoreCase);

    private Dictionary<string, long> _baselineEnhancementTotals =
        new(StringComparer.OrdinalIgnoreCase);

    private Dictionary<string, long> _baselineRecipeTotals =
        new(StringComparer.OrdinalIgnoreCase);

    private Dictionary<string, long> _baselineInspirationTotals =
        new(StringComparer.OrdinalIgnoreCase);

    private bool _isTrackedSessionFrozen;

    private Guid? _currentTrackedSessionId;

    private bool _trackedSessionPersisted;

    private long _trackedBaselinePresentationExperience;

    private long _trackedBaselinePresentationInfluence;

    private Dictionary<string, long> _trackedBaselineCurrencyTotals =
        new(StringComparer.OrdinalIgnoreCase);

    private Dictionary<string, long> _trackedBaselineSalvageTotals =
        new(StringComparer.OrdinalIgnoreCase);

    private Dictionary<string, long> _trackedBaselineEnhancementTotals =
        new(StringComparer.OrdinalIgnoreCase);

    private Dictionary<string, long> _trackedBaselineRecipeTotals =
        new(StringComparer.OrdinalIgnoreCase);

    private Dictionary<string, long> _trackedBaselineInspirationTotals =
        new(StringComparer.OrdinalIgnoreCase);

    private DateTimeOffset? _trackedStartedAt;

    private DateTimeOffset? _trackedGameplaySessionStartedAt;

    private TimeSpan _trackedAccumulatedPauseDuration;

    private DateTimeOffset? _trackedPauseStartedAt;

    private long _lastTrackedExperience;

    private long _lastTrackedInfluence;

    private TimeSpan _lastTrackedElapsed;

    private Dictionary<string, long> _lastTrackedCurrencyTotals =
        new(StringComparer.OrdinalIgnoreCase);

    private Dictionary<string, long> _lastTrackedSalvageTotals =
        new(StringComparer.OrdinalIgnoreCase);

    private Dictionary<string, long> _lastTrackedEnhancementTotals =
        new(StringComparer.OrdinalIgnoreCase);

    private Dictionary<string, long> _lastTrackedRecipeTotals =
        new(StringComparer.OrdinalIgnoreCase);

    private Dictionary<string, long> _lastTrackedInspirationTotals =
        new(StringComparer.OrdinalIgnoreCase);

    private bool _disposed;

    public LiveSessionViewModel(
        IApplicationOrchestrator orchestrator,
        IGameRuntimeService gameRuntimeService,
        IGameplaySessionManager gameplaySessionManager,
        IGameplaySessionIdentityReadService identityReadService,
        IGameplaySessionContextResolver gameplaySessionContextResolver,
        IViewedContextService viewedContextService,
        ISessionStore? sessionStore = null,
        IItemReferenceCatalog? itemReferenceCatalog = null,
        IEnhancementIconCompositor? enhancementIconCompositor = null,
        IHomecomingBoostMetadataProvider? boostMetadataProvider = null,
        IInstalledGameAssetProvider? installedGameAssetProvider = null,
        AccountAnonymityService? accountAnonymityService = null)
        : base(orchestrator, gameRuntimeService)
    {
        _gameplaySessionManager = gameplaySessionManager;
        _identityReadService = identityReadService;
        _gameplaySessionContextResolver = gameplaySessionContextResolver;
        _viewedContextService = viewedContextService;
        _sessionStore = sessionStore ?? new SessionStore();
        _itemReferenceCatalog = itemReferenceCatalog;
        _enhancementIconCompositor = enhancementIconCompositor;
        _boostMetadataProvider = boostMetadataProvider;
        _installedGameAssetProvider = installedGameAssetProvider;
        _accountAnonymityService = accountAnonymityService ?? new AccountAnonymityService();
        Contexts = new ObservableCollection<LiveSessionContextPanelViewModel>();
        SessionTabs = new ObservableCollection<LiveSessionTabViewModel>();
        Currencies = CreateCurrencyRows(CurrencyRowDefinitions);
        SpecialRewards = CreateRows(SpecialRewardRowDefinitions);

        WireEnvironmentStatus();
        GameRuntimeService.StatusChanged += OnGameRuntimeStatusChanged;
        _identityReadService.Changed += OnIdentityReadChanged;
        _viewedContextService.Changed += OnViewedContextChanged;
        _accountAnonymityService.Changed += OnAccountAnonymityChanged;
        RefreshContexts();

        if (Application.Current?.Dispatcher is { } dispatcher)
        {
            _telemetryRefreshTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _telemetryRefreshTimer.Tick += OnTelemetryRefreshTick;
            _telemetryRefreshTimer.Start();
        }

        RefreshContexts();
    }

    public override string Title => "Live Session";

    public string Subtitle => "Runtime monitoring and active character identity";

    public ObservableCollection<LiveSessionContextPanelViewModel> Contexts { get; }

    public ObservableCollection<LiveSessionTabViewModel> SessionTabs { get; }

    [ObservableProperty]
    private bool _showSessionTabs;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNoContextNotice))]
    [NotifyPropertyChangedFor(nameof(ShowViewedContextStrip))]
    [NotifyPropertyChangedFor(nameof(ViewedContextPanel))]
    [NotifyPropertyChangedFor(nameof(ViewedAccountLabel))]
    [NotifyPropertyChangedFor(nameof(ViewedCharacterLabel))]
    [NotifyPropertyChangedFor(nameof(ViewedStatusLabel))]
    [NotifyPropertyChangedFor(nameof(ViewedRequiresManualSelection))]
    [NotifyPropertyChangedFor(nameof(ViewedIdentityStatusKind))]
    private bool _hasContexts;

    public bool ShowNoContextNotice => !HasContexts;

    public bool ShowViewedContextStrip => HasContexts;

    /// <summary>
    /// Identity panel for the gameplay session currently selected through the shared resolver.
    /// </summary>
    public LiveSessionContextPanelViewModel? ViewedContextPanel =>
        GetActiveContext() is { } context
            ? Contexts.FirstOrDefault(panel => panel.ContextId == context.ContextId)
            : null;

    public string ViewedAccountLabel =>
        GetActiveContext() is { } context
            ? FormatViewedAccountLabel(context)
            : string.Empty;

    public string ViewedCharacterLabel =>
        GetActiveContext() is { } context
            && context.CharacterIdentityResolutionState == CharacterIdentityResolutionState.Resolved
            && !string.IsNullOrWhiteSpace(context.CharacterDisplayName)
            ? context.CharacterDisplayName!
            : "Unknown";

    public string ViewedStatusLabel =>
        GetActiveContext() is { } context
            ? FormatViewedStatusLabel(context)
            : string.Empty;

    public bool ViewedRequiresManualSelection =>
        GameRuntimeService.CurrentStatus == GameRuntimeStatus.Running
        && (ViewedContextPanel?.RequiresManualSelection ?? false);

    public DashboardStatusKind ViewedIdentityStatusKind =>
        ViewedContextPanel?.IdentityStatusKind ?? DashboardStatusKind.Information;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SessionStateLabel))]
    [NotifyCanExecuteChangedFor(nameof(ClearSessionCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearSessionAndRewardsCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveLiveSessionCommand))]
    private LiveSessionWorkspaceState _workspaceState = LiveSessionWorkspaceState.Waiting;

    public string SessionStateLabel => WorkspaceState switch
    {
        LiveSessionWorkspaceState.Live => "Live",
        LiveSessionWorkspaceState.Frozen => "Last Session",
        _ => "Waiting for gameplay session"
    };

    [ObservableProperty]
    private string _sessionDurationLabel = WaitingDurationLabel;

    [ObservableProperty]
    private string _sessionExperienceLabel = ZeroLabel;

    [ObservableProperty]
    private string _sessionExperienceRateLabel = ZeroLabel;

    [ObservableProperty]
    private string _sessionGameplayInfluenceLabel = ZeroLabel;

    [ObservableProperty]
    private string _sessionGameplayInfluenceRateLabel = ZeroLabel;

    [ObservableProperty]
    private string _sessionPrimaryPerformanceRateLabel = "—";

    [ObservableProperty]
    private string _sessionPrimaryPerformanceTotalLabel = "—";

    public string SessionPrimaryPerformanceRateCaption => PrimaryPerformanceMetric.BetaDamage.RateLabel;

    public string SessionPrimaryPerformanceTotalCaption => PrimaryPerformanceMetric.BetaDamage.TotalLabel;

    public ObservableCollection<LiveSessionAggregateRowViewModel> Currencies { get; }

    public ObservableCollection<LiveSessionAggregateRowViewModel> SpecialRewards { get; }

    /// <summary>Aggregated recipe acquisitions. Populated by a future session aggregation slice.</summary>
    public ObservableCollection<LiveSessionItemRowViewModel> Recipes { get; } = [];

    public ObservableCollection<LiveSessionItemRowViewModel> SalvageDrops { get; } = [];

    public ObservableCollection<LiveSessionItemRowViewModel> EnhancementDrops { get; } = [];

    public ObservableCollection<LiveSessionItemRowViewModel> InspirationDrops { get; } = [];

    public string SalvageSectionHeader => $"Salvage ({SalvageDrops.Count})";

    public string EnhancementSectionHeader => $"Enhancements ({EnhancementDrops.Count})";

    public string InspirationSectionHeader => $"Inspirations ({InspirationDrops.Count})";

    [ObservableProperty]
    private bool _isSalvageExpanded = true;

    [ObservableProperty]
    private bool _isEnhancementsExpanded;

    [ObservableProperty]
    private bool _isInspirationsExpanded;

    [ObservableProperty]
    private bool _hasRecipes;

    [ObservableProperty]
    private bool _hasSalvageDrops;

    [ObservableProperty]
    private bool _hasEnhancementDrops;

    [ObservableProperty]
    private bool _hasInspirationDrops;

    [ObservableProperty]
    private bool _hasAnyLoot;

    [ObservableProperty]
    private bool _hasSpecialRewards;

    /// <summary>Independent benchmark timer measuring a user-defined window inside a gameplay session.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TrackedSessionStatusLabel))]
    [NotifyPropertyChangedFor(nameof(TrackedSessionPauseResumeLabel))]
    [NotifyPropertyChangedFor(nameof(TrackedSessionStatusLabel))]
    [NotifyCanExecuteChangedFor(nameof(StartTrackedSessionCommand))]
    [NotifyCanExecuteChangedFor(nameof(ToggleTrackedSessionPauseCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveTrackedSessionCommand))]
    private bool _isTrackedSessionRunning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TrackedSessionStatusLabel))]
    [NotifyPropertyChangedFor(nameof(TrackedSessionStatusLabel))]
    [NotifyPropertyChangedFor(nameof(TrackedSessionPauseResumeLabel))]
    [NotifyCanExecuteChangedFor(nameof(SaveTrackedSessionCommand))]
    private bool _isTrackedSessionPaused;

    public string TrackedSessionStatusLabel => IsTrackedSessionRunning
        ? IsTrackedSessionPaused ? "Paused" : "Running"
        : _isTrackedSessionFrozen ? "Stopped" : "Not Started";

    public string TrackedSessionPauseResumeLabel => IsTrackedSessionPaused ? "Resume" : "Pause";

    [ObservableProperty]
    private string _trackedSessionDurationLabel = WaitingDurationLabel;

    [ObservableProperty]
    private string _trackedSessionExperienceLabel = ZeroLabel;

    [ObservableProperty]
    private string _trackedSessionExperienceRateLabel = ZeroLabel;

    [ObservableProperty]
    private string _trackedSessionGameplayInfluenceLabel = ZeroLabel;

    [ObservableProperty]
    private string _trackedSessionGameplayInfluenceRateLabel = ZeroLabel;

    [ObservableProperty]
    private string _trackedSessionPrimaryPerformanceRateLabel = "—";

    [ObservableProperty]
    private string _trackedSessionPrimaryPerformanceTotalLabel = "—";

    public string TrackedSessionPrimaryPerformanceRateCaption =>
        PrimaryPerformanceMetric.BetaDamage.RateLabel;

    public string TrackedSessionPrimaryPerformanceTotalCaption =>
        PrimaryPerformanceMetric.BetaDamage.TotalLabel;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        GameRuntimeService.StatusChanged -= OnGameRuntimeStatusChanged;
        _identityReadService.Changed -= OnIdentityReadChanged;
        _viewedContextService.Changed -= OnViewedContextChanged;
        _accountAnonymityService.Changed -= OnAccountAnonymityChanged;
        if (_telemetryRefreshTimer is not null)
        {
            _telemetryRefreshTimer.Tick -= OnTelemetryRefreshTick;
            _telemetryRefreshTimer.Stop();
        }

        UnwireEnvironmentStatus();
    }

    /// <summary>
    /// Resets the Live Session performance presentation window. Authoritative gameplay-session
    /// totals, collections, rolling analytics, tracked session, and Analytics remain unchanged.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanClearSession))]
    private void ClearSession()
    {
        var active = GetActiveContext();
        if (active is null)
        {
            return;
        }

        _gameplaySessionManager.CaptureHistoricalPerformanceBoundary(active.ContextId);
        RebasePerformancePresentationBaseline(active);
        SavePresentationBaselinesForContext(active.ContextId);
        ApplySessionPresentation(active);
        RefreshTelemetryPresentation();
    }

    /// <summary>
    /// Rebases Total Session performance and reward presentation. Authoritative gameplay-session
    /// totals remain unchanged.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanClearSession))]
    private void ClearSessionAndRewards()
    {
        var active = GetActiveContext();
        if (active is null)
        {
            return;
        }

        _gameplaySessionManager.CaptureHistoricalPerformanceBoundary(active.ContextId);
        RebasePerformancePresentationBaseline(active);
        RebaseRewardPresentationBaseline(active);
        SavePresentationBaselinesForContext(active.ContextId);
        ApplySessionPresentation(active);
        RefreshTelemetryPresentation();
    }

    private bool CanClearSession() => WorkspaceState is not LiveSessionWorkspaceState.Waiting;

    [RelayCommand(CanExecute = nameof(CanSaveLiveSession))]
    private void SaveLiveSession()
    {
        var document = BuildLiveSessionDocument(usePresentationTotals: true);
        if (document is null)
        {
            return;
        }

        _sessionStore.SaveSession(document);
    }

    private bool CanSaveLiveSession() => WorkspaceState is not LiveSessionWorkspaceState.Waiting;

    [RelayCommand(CanExecute = nameof(CanStartTrackedSession))]
    private void StartTrackedSession()
    {
        var active = GetActiveContext();
        if (active is null)
        {
            return;
        }

        _isTrackedSessionFrozen = false;
        _currentTrackedSessionId = Guid.NewGuid();
        _trackedSessionPersisted = false;
        OnPropertyChanged(nameof(TrackedSessionStatusLabel));
        _trackedBaselinePresentationExperience =
            active.SessionExperienceGained - _baselineExperienceGained;
        _trackedBaselinePresentationInfluence =
            active.SessionGameplayInfluenceGained - _baselineGameplayInfluenceGained;
        _trackedBaselineCurrencyTotals = ToCurrencyQuantityDictionary(active.RewardCurrencyTotals);
        _trackedBaselineSalvageTotals = ToItemQuantityDictionary(active.SalvageTotals);
        _trackedBaselineEnhancementTotals = ToItemQuantityDictionary(active.EnhancementTotals);
        _trackedBaselineRecipeTotals = ToItemQuantityDictionary(active.RecipeTotals);
        _trackedBaselineInspirationTotals = ToItemQuantityDictionary(active.InspirationTotals);
        _trackedStartedAt = DateTimeOffset.UtcNow;
        _trackedGameplaySessionStartedAt = active.SessionStartedAt;
        _trackedAccumulatedPauseDuration = TimeSpan.Zero;
        _trackedPauseStartedAt = null;
        ClearTrackedEconomySnapshots();
        IsTrackedSessionRunning = true;
        IsTrackedSessionPaused = false;
        _gameplaySessionManager.StartTrackedCombat(active.ContextId);
        RefreshTrackedSessionPresentation(active);
    }

    private bool CanStartTrackedSession() => !IsTrackedSessionRunning;

    [RelayCommand(CanExecute = nameof(IsTrackedSessionRunning))]
    private void ToggleTrackedSessionPause()
    {
        if (!IsTrackedSessionRunning)
        {
            return;
        }

        if (IsTrackedSessionPaused)
        {
            if (_trackedPauseStartedAt is not null)
            {
                _trackedAccumulatedPauseDuration += DateTimeOffset.UtcNow - _trackedPauseStartedAt.Value;
                _trackedPauseStartedAt = null;
            }

            IsTrackedSessionPaused = false;
            var active = GetActiveContext();
            if (active is not null)
            {
                _gameplaySessionManager.ResumeTrackedCombat(active.ContextId);
                RebaseTrackedBaselinesAfterPause(active);
                RefreshTrackedSessionPresentation(active);
            }

            return;
        }

        var activeBeforePause = GetActiveContext();
        if (activeBeforePause is not null)
        {
            RefreshTrackedSessionPresentation(activeBeforePause);
            _gameplaySessionManager.PauseTrackedCombat(activeBeforePause.ContextId);
        }

        IsTrackedSessionPaused = true;
        _trackedPauseStartedAt = DateTimeOffset.UtcNow;
    }

    [RelayCommand(CanExecute = nameof(CanSaveTrackedSession))]
    private void SaveTrackedSession()
    {
        var document = BuildTrackedSessionDocument();
        if (document is null)
        {
            return;
        }

        _sessionStore.SaveSession(document);
    }

    private bool CanSaveTrackedSession() => IsTrackedSessionRunning || _isTrackedSessionFrozen;

    [RelayCommand]
    private void ResetTrackedSession()
    {
        IsTrackedSessionRunning = false;
        IsTrackedSessionPaused = false;
        _isTrackedSessionFrozen = false;
        _trackedBaselinePresentationExperience = 0;
        _trackedBaselinePresentationInfluence = 0;
        _trackedBaselineCurrencyTotals.Clear();
        _trackedBaselineSalvageTotals.Clear();
        _trackedBaselineEnhancementTotals.Clear();
        _trackedBaselineRecipeTotals.Clear();
        _trackedBaselineInspirationTotals.Clear();
        _trackedStartedAt = null;
        _trackedGameplaySessionStartedAt = null;
        _trackedAccumulatedPauseDuration = TimeSpan.Zero;
        _trackedPauseStartedAt = null;
        _lastTrackedExperience = 0;
        _lastTrackedInfluence = 0;
        _lastTrackedElapsed = TimeSpan.Zero;
        ClearTrackedEconomySnapshots();
        _currentTrackedSessionId = null;
        _trackedSessionPersisted = false;
        var active = GetActiveContext();
        if (active is not null)
        {
            _gameplaySessionManager.ResetTrackedCombat(active.ContextId);
        }

        ResetTrackedSessionPresentation();
    }

    private bool _observedNonZeroClientCount;
    private bool _observedZeroClientCountAfterNonZero;

    private void OnGameRuntimeStatusChanged(object? sender, GameRuntimeStatusChangedEventArgs e)
    {
        if (LiveRuntimeGenerationService.TryConsumeNewGenerationBoundary(
                e,
                ref _observedNonZeroClientCount,
                ref _observedZeroClientCountAfterNonZero))
        {
            ResetLiveRuntimePresentationForNewGeneration();
        }

        DispatchRefresh(RefreshContexts);
    }

    private void ResetLiveRuntimePresentationForNewGeneration()
    {
        _lastKnownActiveContext = null;
        _displayedContextId = null;
        _persistenceByContext.Clear();
        _presentationByContext.Clear();
        _sessionStartedAt = null;
        _sessionTimingEndAt = null;
        _liveSessionPersisted = false;
        _currentLiveSessionId = null;

        if (IsTrackedSessionRunning)
        {
            var active = GetActiveContext();
            if (active is not null)
            {
                _gameplaySessionManager.StopTrackedCombat(active.ContextId);
            }
        }

        IsTrackedSessionRunning = false;
        IsTrackedSessionPaused = false;
        _isTrackedSessionFrozen = false;
        _trackedGameplaySessionStartedAt = null;
        ResetTrackedSessionPresentation();

        ResetGameplaySessionPresentation();
    }

    private void OnIdentityReadChanged(object? sender, GameplaySessionIdentityReadModelChangedEventArgs e) =>
        DispatchRefresh(RefreshContexts);

    private void OnViewedContextChanged(object? sender, ViewedContextChangedEventArgs e) =>
        DispatchRefresh(RefreshContexts);

    private void OnAccountAnonymityChanged(object? sender, EventArgs e) =>
        DispatchRefresh(RefreshContexts);

    private void OnTelemetryRefreshTick(object? sender, EventArgs e) =>
        DispatchRefresh(RefreshTelemetryPresentation);

    private void DispatchRefresh(Action refreshAction)
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

    private void RefreshContexts()
    {
        var snapshot = _identityReadService.Current;
        var pickerStateByContext = Contexts.ToDictionary(
            context => context.ContextId,
            context => (
                context.IsPickerOpen,
                SelectedCharacterId: context.SelectedPickerCharacter?.RecordId,
                context.SelectionMessage));

        Contexts.Clear();

        foreach (var context in snapshot.Contexts)
        {
            var panel = LiveSessionContextPanelViewModel.FromReadModel(context, _accountAnonymityService);
            if (panel.RequiresManualSelection
                && pickerStateByContext.TryGetValue(panel.ContextId, out var pickerState))
            {
                panel.IsPickerOpen = pickerState.IsPickerOpen;
                panel.SelectedPickerCharacter = panel.PickerCharacters.FirstOrDefault(character =>
                    character.RecordId == pickerState.SelectedCharacterId);
                panel.SelectionMessage = pickerState.SelectionMessage;
            }

            Contexts.Add(panel);
        }

        HasContexts = Contexts.Count > 0;
        NotifyViewedContextStripChanged();
        RefreshSessionTabs();
        ApplySessionTelemetry();
    }

    private void NotifyViewedContextStripChanged()
    {
        OnPropertyChanged(nameof(ViewedContextPanel));
        OnPropertyChanged(nameof(ViewedAccountLabel));
        OnPropertyChanged(nameof(ViewedCharacterLabel));
        OnPropertyChanged(nameof(ViewedStatusLabel));
        OnPropertyChanged(nameof(ViewedRequiresManualSelection));
        OnPropertyChanged(nameof(ViewedIdentityStatusKind));
    }

    private string FormatViewedAccountLabel(LiveMonitoringContextIdentityReadModel context)
    {
        var accountName = string.IsNullOrWhiteSpace(context.AccountDisplayName)
            ? context.AccountStableId
            : context.AccountDisplayName;
        return _accountAnonymityService.MaskForPresentation(accountName, "Unknown");
    }

    private static string FormatViewedStatusLabel(LiveMonitoringContextIdentityReadModel context)
    {
        if (IsOfflineEndedSession(context))
        {
            return context.IdentityStatusLabel;
        }

        if (context.IsConfirmed)
        {
            return "Confirmed";
        }

        if (context.RequiresManualSelection)
        {
            return "Needs Attention";
        }

        return context.IdentityStatusLabel;
    }

    private static bool IsOfflineEndedSession(LiveMonitoringContextIdentityReadModel context) =>
        context.ContextState == MonitoringContextState.RuntimeSuspended
        || context.SessionLifecycleState is GameplaySessionLifecycleState.Suspended
            or GameplaySessionLifecycleState.Finalized;

    [RelayCommand]
    private void SelectSessionTab(LiveSessionTabViewModel? tab)
    {
        if (tab is null)
        {
            return;
        }

        _viewedContextService.SelectGameplaySessionContext(tab.ContextId);
    }

    private void RefreshSessionTabs()
    {
        var applicable = GameplaySessionContextResolver
            .GetApplicableContexts(_identityReadService.Current)
            .OrderBy(context => context.ContextId.Value)
            .ToArray();
        var selectedContextId = ResolveSelectedContext().Context?.ContextId;
        SessionTabs.Clear();
        if (applicable.Length < 2)
        {
            ShowSessionTabs = false;
            return;
        }

        ShowSessionTabs = true;
        foreach (var context in applicable)
        {
            SessionTabs.Add(LiveSessionTabViewModel.FromContext(
                context,
                isSelected: context.ContextId == selectedContextId,
                accountAnonymityService: _accountAnonymityService));
        }
    }

    /// <summary>
    /// Selects the active gameplay session and moves the workspace between states. A session
    /// that ends is frozen in place rather than cleared, and a new session replaces it
    /// automatically without prompting.
    /// </summary>
    private void ApplySessionTelemetry()
    {
        var active = ResolveSelectedContext().Context;

        if (active is null)
        {
            if (WorkspaceState is LiveSessionWorkspaceState.Live)
            {
                if (_displayedContextId is { } displayedId
                    && _persistenceByContext.TryGetValue(displayedId, out var persistence)
                    && persistence.LastKnownContext is not null)
                {
                    TryPersistCompletedLiveSession(persistence.LastKnownContext, displayedId);
                }
                else
                {
                    TryPersistCompletedLiveSession(_lastKnownActiveContext, _displayedContextId);
                }

                WorkspaceState = LiveSessionWorkspaceState.Frozen;
                FreezeTrackedSession();
            }

            RefreshSessionTabs();
            return;
        }

        if (IsTrackedSessionRunning
            && _trackedGameplaySessionStartedAt is not null
            && active.SessionStartedAt != _trackedGameplaySessionStartedAt)
        {
            FreezeTrackedSession();
        }

        SwitchPresentationContext(active.ContextId);
        HandleActiveContextSessionLifecycle(active);

        _lastKnownActiveContext = active;
        _sessionStartedAt = active.SessionStartedAt;
        _sessionTimingEndAt = active.SessionTimingEndAt;
        ApplySessionPresentation(active);

        if (IsOfflineEndedSession(active))
        {
            WorkspaceState = LiveSessionWorkspaceState.Frozen;
            FreezeTrackedSession();
        }
        else
        {
            WorkspaceState = LiveSessionWorkspaceState.Live;
            RefreshTelemetryPresentation();
        }

        RefreshSessionTabs();
        SaveLiveSessionCommand.NotifyCanExecuteChanged();
    }

    private void SwitchPresentationContext(MonitoringContextId contextId)
    {
        if (_displayedContextId == contextId)
        {
            return;
        }

        if (_displayedContextId is { } previousContextId)
        {
            SavePresentationBaselinesForContext(previousContextId);
        }

        _displayedContextId = contextId;
        LoadPresentationBaselines(contextId);
    }

    private void HandleActiveContextSessionLifecycle(LiveMonitoringContextIdentityReadModel active)
    {
        var contextId = active.ContextId;
        if (!_persistenceByContext.TryGetValue(contextId, out var persistence))
        {
            persistence = new ContextLiveSessionPersistenceState();
            _persistenceByContext[contextId] = persistence;
        }

        if (persistence.SessionStartedAt is not null
            && persistence.SessionStartedAt != active.SessionStartedAt)
        {
            if (persistence.LastKnownContext is not null)
            {
                TryPersistCompletedLiveSession(persistence.LastKnownContext, contextId);
            }

            _presentationByContext.Remove(contextId);
            if (_displayedContextId == contextId)
            {
                LoadPresentationBaselines(contextId);
            }

            persistence.LiveSessionId = Guid.NewGuid();
            persistence.Persisted = false;
        }
        else if (persistence.LiveSessionId is null)
        {
            persistence.LiveSessionId = Guid.NewGuid();
            persistence.Persisted = false;
        }

        persistence.SessionStartedAt = active.SessionStartedAt;
        persistence.LastKnownContext = active;
        _currentLiveSessionId = persistence.LiveSessionId;
        _liveSessionPersisted = persistence.Persisted;
    }

    private void ApplySessionPresentation(LiveMonitoringContextIdentityReadModel active)
    {
        _sessionExperienceGained = active.SessionExperienceGained - _baselineExperienceGained;
        _sessionGameplayInfluenceGained = active.SessionGameplayInfluenceGained - _baselineGameplayInfluenceGained;

        SessionExperienceLabel = FormatQuantity(_sessionExperienceGained);
        SessionGameplayInfluenceLabel = FormatQuantity(_sessionGameplayInfluenceGained);
        ApplySessionPrimaryPerformance(active);
        ApplyTrackedCombatPrimaryPerformance(active);
        ApplyTotalsToRows(Currencies, active.RewardCurrencyTotals, _baselineCurrencyTotals);
        HasSpecialRewards = ApplyTotalsToRows(SpecialRewards, active.RewardCurrencyTotals, _baselineCurrencyTotals);
        ApplyItemTotals(
            SalvageDrops,
            active.SalvageTotals,
            _baselineSalvageTotals,
            ReferenceItemFamily.Salvage);
        ApplyItemTotals(
            EnhancementDrops,
            active.EnhancementTotals,
            _baselineEnhancementTotals,
            ReferenceItemFamily.Enhancement);
        ApplyItemTotals(
            Recipes,
            active.RecipeTotals,
            _baselineRecipeTotals,
            ReferenceItemFamily.Recipe);
        ApplyItemTotals(
            InspirationDrops,
            active.InspirationTotals,
            _baselineInspirationTotals,
            ReferenceItemFamily.Inspiration);
        RefreshCollectionFlags();
    }

    private void RebasePerformancePresentationBaseline(LiveMonitoringContextIdentityReadModel active)
    {
        _baselineExperienceGained = active.SessionExperienceGained;
        _baselineGameplayInfluenceGained = active.SessionGameplayInfluenceGained;
        _baselineDamageDealt = active.Combat.DamageDealt;
        _presentationDurationAnchor = DateTimeOffset.UtcNow;
    }

    private void RebaseRewardPresentationBaseline(LiveMonitoringContextIdentityReadModel active)
    {
        _baselineCurrencyTotals = ToCurrencyQuantityDictionary(active.RewardCurrencyTotals);
        _baselineSalvageTotals = ToItemQuantityDictionary(active.SalvageTotals);
        _baselineEnhancementTotals = ToItemQuantityDictionary(active.EnhancementTotals);
        _baselineRecipeTotals = ToItemQuantityDictionary(active.RecipeTotals);
        _baselineInspirationTotals = ToItemQuantityDictionary(active.InspirationTotals);
    }

    private void ResetPresentationBaselines()
    {
        ClearPresentationBaselineFields();
        if (_displayedContextId is { } contextId)
        {
            _presentationByContext.Remove(contextId);
        }
    }

    private void SavePresentationBaselinesForContext(MonitoringContextId contextId)
    {
        _presentationByContext[contextId] = CapturePresentationBaselineState();
    }

    private void LoadPresentationBaselines(MonitoringContextId contextId)
    {
        if (_presentationByContext.TryGetValue(contextId, out var saved))
        {
            ApplyPresentationBaselineState(saved);
            return;
        }

        ClearPresentationBaselineFields();
    }

    private LiveSessionContextPresentationState CapturePresentationBaselineState() =>
        new()
        {
            BaselineExperienceGained = _baselineExperienceGained,
            BaselineGameplayInfluenceGained = _baselineGameplayInfluenceGained,
            BaselineDamageDealt = _baselineDamageDealt,
            PresentationDurationAnchor = _presentationDurationAnchor,
            BaselineCurrencyTotals = CloneQuantityDictionary(_baselineCurrencyTotals),
            BaselineSalvageTotals = CloneQuantityDictionary(_baselineSalvageTotals),
            BaselineEnhancementTotals = CloneQuantityDictionary(_baselineEnhancementTotals),
            BaselineRecipeTotals = CloneQuantityDictionary(_baselineRecipeTotals),
            BaselineInspirationTotals = CloneQuantityDictionary(_baselineInspirationTotals)
        };

    private void ApplyPresentationBaselineState(LiveSessionContextPresentationState state)
    {
        _baselineExperienceGained = state.BaselineExperienceGained;
        _baselineGameplayInfluenceGained = state.BaselineGameplayInfluenceGained;
        _baselineDamageDealt = state.BaselineDamageDealt;
        _presentationDurationAnchor = state.PresentationDurationAnchor;
        _baselineCurrencyTotals = CloneQuantityDictionary(state.BaselineCurrencyTotals);
        _baselineSalvageTotals = CloneQuantityDictionary(state.BaselineSalvageTotals);
        _baselineEnhancementTotals = CloneQuantityDictionary(state.BaselineEnhancementTotals);
        _baselineRecipeTotals = CloneQuantityDictionary(state.BaselineRecipeTotals);
        _baselineInspirationTotals = CloneQuantityDictionary(state.BaselineInspirationTotals);
    }

    private void ClearPresentationBaselineFields()
    {
        _baselineExperienceGained = 0;
        _baselineGameplayInfluenceGained = 0;
        _baselineDamageDealt = CombatScaledAmount.Zero;
        _presentationDurationAnchor = null;
        _baselineCurrencyTotals.Clear();
        _baselineSalvageTotals.Clear();
        _baselineEnhancementTotals.Clear();
        _baselineRecipeTotals.Clear();
        _baselineInspirationTotals.Clear();
    }

    /// <summary>
    /// Recomputes duration and rates. Frozen sessions keep their final values, so the
    /// elapsed clock stops when the gameplay session ends.
    /// </summary>
    private void RefreshTelemetryPresentation()
    {
        if (WorkspaceState is not LiveSessionWorkspaceState.Live || _sessionStartedAt is null)
        {
            return;
        }

        var durationStart = _presentationDurationAnchor ?? _sessionStartedAt.Value;
        var elapsed = GameplaySessionTelemetryPresentation.GetElapsedDuration(
            durationStart,
            DateTimeOffset.UtcNow,
            _sessionTimingEndAt);

        SessionDurationLabel = GameplaySessionTelemetryPresentation.FormatDuration(elapsed);
        SessionExperienceRateLabel = GameplaySessionTelemetryPresentation.FormatRatePerHour(
            _sessionExperienceGained,
            elapsed);
        SessionGameplayInfluenceRateLabel = GameplaySessionTelemetryPresentation.FormatRatePerHour(
            _sessionGameplayInfluenceGained,
            elapsed);

        var active = GetActiveContext();
        if (active is not null)
        {
            ApplySessionPrimaryPerformance(active, elapsed);
            RefreshTrackedSessionPresentation(active);
        }
    }

    private void FreezeTrackedSession()
    {
        if (!IsTrackedSessionRunning)
        {
            return;
        }

        if (_trackedPauseStartedAt is not null)
        {
            _trackedAccumulatedPauseDuration += DateTimeOffset.UtcNow - _trackedPauseStartedAt.Value;
            _trackedPauseStartedAt = null;
        }

        if (!IsTrackedSessionPaused
            && _lastKnownActiveContext is not null
            && (_trackedGameplaySessionStartedAt is null
                || _lastKnownActiveContext.SessionStartedAt == _trackedGameplaySessionStartedAt))
        {
            RefreshTrackedSessionPresentation(_lastKnownActiveContext);
        }

        if (_lastKnownActiveContext is not null)
        {
            _gameplaySessionManager.StopTrackedCombat(_lastKnownActiveContext.ContextId);
        }

        IsTrackedSessionRunning = false;
        IsTrackedSessionPaused = false;
        _isTrackedSessionFrozen = true;
        TryPersistCompletedTrackedSession();
        OnPropertyChanged(nameof(TrackedSessionStatusLabel));
        SaveTrackedSessionCommand.NotifyCanExecuteChanged();
        StartTrackedSessionCommand.NotifyCanExecuteChanged();
        ToggleTrackedSessionPauseCommand.NotifyCanExecuteChanged();
    }

    private void RefreshTrackedSessionPresentation(LiveMonitoringContextIdentityReadModel active)
    {
        if (!IsTrackedSessionRunning && !_isTrackedSessionFrozen)
        {
            return;
        }

        if (_isTrackedSessionFrozen || IsTrackedSessionPaused)
        {
            return;
        }

        long experience;
        long influence;
        TimeSpan elapsed;
        if (active.TrackedEarnings.IsTracking)
        {
            experience = active.TrackedEarnings.ExperienceGained;
            influence = active.TrackedEarnings.InfluenceGained;
            // Duration must tick from the UI wall-clock path; ActiveElapsed is only refreshed when
            // GameplaySessionManager publishes a dirty snapshot (e.g. on reward events).
            elapsed = CalculateTrackedElapsed();
        }
        else
        {
            var presentationExperience = active.SessionExperienceGained - _baselineExperienceGained;
            var presentationInfluence = active.SessionGameplayInfluenceGained - _baselineGameplayInfluenceGained;
            experience = presentationExperience - _trackedBaselinePresentationExperience;
            influence = presentationInfluence - _trackedBaselinePresentationInfluence;
            if (experience < 0)
            {
                experience = 0;
            }

            if (influence < 0)
            {
                influence = 0;
            }

            elapsed = CalculateTrackedElapsed();
        }

        _lastTrackedExperience = experience;
        _lastTrackedInfluence = influence;
        _lastTrackedElapsed = elapsed;
        _lastTrackedCurrencyTotals = SubtractQuantityDictionaries(
            ToCurrencyQuantityDictionary(active.RewardCurrencyTotals),
            _trackedBaselineCurrencyTotals);
        _lastTrackedSalvageTotals = SubtractQuantityDictionaries(
            ToItemQuantityDictionary(active.SalvageTotals),
            _trackedBaselineSalvageTotals);
        _lastTrackedEnhancementTotals = SubtractQuantityDictionaries(
            ToItemQuantityDictionary(active.EnhancementTotals),
            _trackedBaselineEnhancementTotals);
        _lastTrackedRecipeTotals = SubtractQuantityDictionaries(
            ToItemQuantityDictionary(active.RecipeTotals),
            _trackedBaselineRecipeTotals);
        _lastTrackedInspirationTotals = SubtractQuantityDictionaries(
            ToItemQuantityDictionary(active.InspirationTotals),
            _trackedBaselineInspirationTotals);

        TrackedSessionDurationLabel = GameplaySessionTelemetryPresentation.FormatDuration(elapsed);
        TrackedSessionExperienceLabel = FormatQuantity(experience);
        TrackedSessionGameplayInfluenceLabel = FormatQuantity(influence);
        TrackedSessionExperienceRateLabel = active.TrackedEarnings.IsTracking
            ? SessionEarningsPresentation.FormatTrackedExperienceRatePerHour(active.TrackedEarnings)
            : GameplaySessionTelemetryPresentation.FormatRatePerHour(experience, elapsed);
        TrackedSessionGameplayInfluenceRateLabel = active.TrackedEarnings.IsTracking
            ? SessionEarningsPresentation.FormatTrackedInfluenceRatePerHour(active.TrackedEarnings)
            : GameplaySessionTelemetryPresentation.FormatRatePerHour(influence, elapsed);
        ApplyTrackedPrimaryPerformance(active.Combat.Tracked);
    }

    private void ApplySessionPrimaryPerformance(
        LiveMonitoringContextIdentityReadModel active,
        TimeSpan? elapsed = null)
    {
        var presentationElapsed = elapsed ?? GetPresentationElapsed(active);

        if (_presentationDurationAnchor is null)
        {
            var metric = PrimaryPerformancePresentation.BuildSession(
                active.Combat,
                presentationElapsed,
                HasSessionCombatData(active));
            SessionPrimaryPerformanceRateLabel = metric.RateValue;
            SessionPrimaryPerformanceTotalLabel = metric.TotalValue;
            return;
        }

        var presentationDamage = GetPresentationDamageDealt(active.Combat.DamageDealt);
        var sessionDamagePerSecondHundredths = GameplaySessionTelemetryPresentation.CanShowRates(presentationElapsed)
            ? CombatAggregator.CalculateSessionDamagePerSecondHundredths(
                presentationDamage,
                presentationElapsed)
            : 0L;
        var presentationCombat = active.Combat with
        {
            DamageDealt = presentationDamage,
            SessionDamagePerSecondHundredths = sessionDamagePerSecondHundredths
        };
        var presentationMetric = PrimaryPerformancePresentation.BuildSession(
            presentationCombat,
            presentationElapsed,
            hasCombatData: true);
        SessionPrimaryPerformanceRateLabel = presentationMetric.RateValue;
        SessionPrimaryPerformanceTotalLabel = presentationMetric.TotalValue;
    }

    private TimeSpan GetPresentationElapsed(LiveMonitoringContextIdentityReadModel active) =>
        GameplaySessionTelemetryPresentation.GetElapsedDuration(
            _presentationDurationAnchor ?? active.SessionStartedAt ?? DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            active.SessionTimingEndAt);

    private CombatScaledAmount GetPresentationDamageDealt(CombatScaledAmount authoritativeDamage)
    {
        var hundredths = authoritativeDamage.Hundredths - _baselineDamageDealt.Hundredths;
        return hundredths <= 0 ? CombatScaledAmount.Zero : new CombatScaledAmount(hundredths);
    }

    private void ApplyTrackedCombatPrimaryPerformance(LiveMonitoringContextIdentityReadModel active)
    {
        if (!IsTrackedSessionRunning && !_isTrackedSessionFrozen && !IsTrackedSessionPaused)
        {
            return;
        }

        ApplyTrackedPrimaryPerformance(active.Combat.Tracked);
    }

    private void ApplyTrackedPrimaryPerformance(TrackedCombatScopeSnapshot tracked)
    {
        var metric = PrimaryPerformancePresentation.BuildTracked(tracked);
        TrackedSessionPrimaryPerformanceRateLabel = metric.RateValue;
        TrackedSessionPrimaryPerformanceTotalLabel = metric.TotalValue;
    }

    private static bool HasSessionCombatData(LiveMonitoringContextIdentityReadModel active) =>
        CombatAnalyticsPresentation.HasSessionCombatData(active);

    private TimeSpan CalculateTrackedElapsed()
    {
        if (_trackedStartedAt is null)
        {
            return TimeSpan.Zero;
        }

        var end = IsTrackedSessionPaused && _trackedPauseStartedAt is not null
            ? _trackedPauseStartedAt.Value
            : DateTimeOffset.UtcNow;

        var elapsed = end - _trackedStartedAt.Value - _trackedAccumulatedPauseDuration;
        return elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed;
    }

    private void ResetTrackedSessionPresentation()
    {
        TrackedSessionDurationLabel = WaitingDurationLabel;
        TrackedSessionExperienceLabel = ZeroLabel;
        TrackedSessionExperienceRateLabel = ZeroLabel;
        TrackedSessionGameplayInfluenceLabel = ZeroLabel;
        TrackedSessionGameplayInfluenceRateLabel = ZeroLabel;
        TrackedSessionPrimaryPerformanceRateLabel = "—";
        TrackedSessionPrimaryPerformanceTotalLabel = "—";
        OnPropertyChanged(nameof(TrackedSessionStatusLabel));
        SaveTrackedSessionCommand.NotifyCanExecuteChanged();
        StartTrackedSessionCommand.NotifyCanExecuteChanged();
        ToggleTrackedSessionPauseCommand.NotifyCanExecuteChanged();
    }

    private LiveMonitoringContextIdentityReadModel? GetActiveContext() =>
        ResolveSelectedContext().Context;

    private GameplaySessionContextSelection ResolveSelectedContext() =>
        _gameplaySessionContextResolver.ResolveForLiveMonitoring();

    private void ResetGameplaySessionPresentation()
    {
        _sessionStartedAt = null;
        _sessionTimingEndAt = null;
        ResetPresentationBaselines();
        _sessionExperienceGained = 0;
        _sessionGameplayInfluenceGained = 0;

        SessionDurationLabel = WaitingDurationLabel;
        SessionExperienceLabel = ZeroLabel;
        SessionExperienceRateLabel = ZeroLabel;
        SessionGameplayInfluenceLabel = ZeroLabel;
        SessionGameplayInfluenceRateLabel = ZeroLabel;
        SessionPrimaryPerformanceRateLabel = "—";
        SessionPrimaryPerformanceTotalLabel = "—";

        foreach (var row in Currencies)
        {
            row.QuantityLabel = ZeroLabel;
        }

        foreach (var row in SpecialRewards)
        {
            row.QuantityLabel = ZeroLabel;
        }

        Recipes.Clear();
        SalvageDrops.Clear();
        EnhancementDrops.Clear();
        InspirationDrops.Clear();
        HasSpecialRewards = false;
        RefreshCollectionFlags();

        WorkspaceState = LiveSessionWorkspaceState.Waiting;
    }

    private void RefreshCollectionFlags()
    {
        HasRecipes = Recipes.Count > 0;
        HasSalvageDrops = SalvageDrops.Count > 0;
        HasEnhancementDrops = EnhancementDrops.Count > 0;
        HasInspirationDrops = InspirationDrops.Count > 0;
        HasAnyLoot = HasSalvageDrops || HasEnhancementDrops || HasInspirationDrops;
        OnPropertyChanged(nameof(SalvageSectionHeader));
        OnPropertyChanged(nameof(EnhancementSectionHeader));
        OnPropertyChanged(nameof(InspirationSectionHeader));
    }

    /// <summary>Applies totals to fixed rows and reports whether any row is non-zero.</summary>
    private static bool ApplyTotalsToRows(
        IEnumerable<LiveSessionAggregateRowViewModel> rows,
        IReadOnlyList<GameplaySessionRewardCurrencyTotal> totals,
        IReadOnlyDictionary<string, long> baselineTotals)
    {
        var anyNonZero = false;
        foreach (var row in rows)
        {
            var quantity = totals
                .FirstOrDefault(total => string.Equals(
                    total.CurrencyDisplayName,
                    row.SourceKey,
                    StringComparison.OrdinalIgnoreCase))
                ?.Quantity ?? 0;

            quantity -= baselineTotals.GetValueOrDefault(row.SourceKey);
            if (quantity < 0)
            {
                quantity = 0;
            }

            anyNonZero |= quantity > 0;
            row.QuantityLabel = FormatQuantity(quantity);
        }

        return anyNonZero;
    }

    private void ApplyItemTotals(
        ObservableCollection<LiveSessionItemRowViewModel> rows,
        IReadOnlyList<GameplaySessionItemTotal> totals,
        IReadOnlyDictionary<string, long> baselineTotals,
        ReferenceItemFamily expectedFamily)
    {
        rows.Clear();
        foreach (var total in totals)
        {
            var quantity = total.Quantity - baselineTotals.GetValueOrDefault(total.DisplayName);
            if (quantity <= 0)
            {
                continue;
            }

            rows.Add(LiveSessionLootPresentationSupport.CreateRow(
                _itemReferenceCatalog,
                _enhancementIconCompositor,
                _boostMetadataProvider,
                _installedGameAssetProvider,
                total.DisplayName,
                FormatQuantity(quantity),
                expectedFamily));
        }
    }

    private static Dictionary<string, long> ToCurrencyQuantityDictionary(
        IReadOnlyList<GameplaySessionRewardCurrencyTotal> totals)
    {
        var dictionary = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var total in totals)
        {
            dictionary[total.CurrencyDisplayName] = total.Quantity;
        }

        return dictionary;
    }

    private static Dictionary<string, long> ToItemQuantityDictionary(
        IReadOnlyList<GameplaySessionItemTotal> totals)
    {
        var dictionary = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var total in totals)
        {
            dictionary[total.DisplayName] = total.Quantity;
        }

        return dictionary;
    }

    private static Dictionary<string, long> CloneQuantityDictionary(
        Dictionary<string, long> source) =>
        new(source, StringComparer.OrdinalIgnoreCase);

    private static ObservableCollection<LiveSessionAggregateRowViewModel> CreateRows(
        IEnumerable<(string DisplayName, string SourceKey, bool StartsGroup)> definitions)
    {
        var rows = new ObservableCollection<LiveSessionAggregateRowViewModel>();
        foreach (var (displayName, sourceKey, startsGroup) in definitions)
        {
            rows.Add(new LiveSessionAggregateRowViewModel
            {
                DisplayName = displayName,
                SourceKey = sourceKey,
                StartsGroup = startsGroup
            });
        }

        return rows;
    }

    private static ObservableCollection<LiveSessionAggregateRowViewModel> CreateCurrencyRows(
        IEnumerable<(
            string DisplayName,
            string SourceKey,
            bool StartsGroup,
            LiveSessionCurrencyNameColor NameColor)> definitions)
    {
        var rows = new ObservableCollection<LiveSessionAggregateRowViewModel>();
        foreach (var (displayName, sourceKey, startsGroup, nameColor) in definitions)
        {
            rows.Add(new LiveSessionAggregateRowViewModel
            {
                DisplayName = displayName,
                SourceKey = sourceKey,
                StartsGroup = startsGroup,
                NameColor = nameColor
            });
        }

        return rows;
    }

    private static string FormatQuantity(long value) => value.ToString("N0", CultureInfo.CurrentCulture);

    private void TryPersistCompletedLiveSession(
        LiveMonitoringContextIdentityReadModel? context,
        MonitoringContextId? contextId = null)
    {
        contextId ??= context?.ContextId ?? _displayedContextId;
        if (contextId is not { } resolvedContextId
            || !_persistenceByContext.TryGetValue(resolvedContextId, out var persistence)
            || persistence.Persisted
            || persistence.LiveSessionId is null
            || context?.SessionStartedAt is null)
        {
            return;
        }

        _currentLiveSessionId = persistence.LiveSessionId;
        _liveSessionPersisted = persistence.Persisted;
        var document = BuildLiveSessionDocument(usePresentationTotals: false, context);
        if (document is null)
        {
            return;
        }

        _sessionStore.PersistCompletedLiveSession(document);
        persistence.Persisted = true;
        _liveSessionPersisted = true;
    }

    private void TryPersistCompletedTrackedSession()
    {
        if (_trackedSessionPersisted || _currentTrackedSessionId is null || _trackedStartedAt is null)
        {
            return;
        }

        var document = BuildTrackedSessionDocument();
        if (document is null)
        {
            return;
        }

        _sessionStore.PersistCompletedTrackedSession(document);
        _trackedSessionPersisted = true;
    }

    private PersistedSessionDocument? BuildLiveSessionDocument(
        bool usePresentationTotals,
        LiveMonitoringContextIdentityReadModel? context = null)
    {
        context ??= GetActiveContext() ?? _lastKnownActiveContext;
        if (context?.SessionStartedAt is null || _currentLiveSessionId is null)
        {
            return null;
        }

        var startedAt = context.SessionStartedAt.Value;
        var endedAt = context.SessionTimingEndAt ?? DateTimeOffset.UtcNow;
        var durationStart = usePresentationTotals
            ? _presentationDurationAnchor ?? startedAt
            : startedAt;
        var elapsed = GameplaySessionTelemetryPresentation.GetElapsedDuration(
            durationStart,
            endedAt,
            context.SessionTimingEndAt);
        var experience = usePresentationTotals
            ? _sessionExperienceGained
            : context.SessionExperienceGained;
        var influence = usePresentationTotals
            ? _sessionGameplayInfluenceGained
            : context.SessionGameplayInfluenceGained;

        return new PersistedSessionDocument
        {
            SessionId = _currentLiveSessionId.Value,
            SessionType = SessionType.Live,
            Account = ResolveAccountLabel(context),
            Character = context.CharacterDisplayName,
            StartedAtUtc = usePresentationTotals && _presentationDurationAnchor is not null
                ? _presentationDurationAnchor.Value
                : startedAt,
            EndedAtUtc = endedAt,
            DurationSeconds = (long)Math.Round(elapsed.TotalSeconds),
            Earnings = new SessionEarnings
            {
                Experience = experience,
                Influence = influence
            },
            Currencies = usePresentationTotals
                ? ToPersistedCurrencies(SubtractQuantityDictionaries(
                    ToCurrencyQuantityDictionary(context.RewardCurrencyTotals),
                    _baselineCurrencyTotals))
                : ToPersistedCurrencies(ToCurrencyQuantityDictionary(context.RewardCurrencyTotals)),
            Loot = usePresentationTotals
                ? BuildPersistedLoot(
                    SubtractQuantityDictionaries(
                        ToItemQuantityDictionary(context.SalvageTotals),
                        _baselineSalvageTotals),
                    SubtractQuantityDictionaries(
                        ToItemQuantityDictionary(context.RecipeTotals),
                        _baselineRecipeTotals),
                    SubtractQuantityDictionaries(
                        ToItemQuantityDictionary(context.EnhancementTotals),
                        _baselineEnhancementTotals),
                    SubtractQuantityDictionaries(
                        ToItemQuantityDictionary(context.InspirationTotals),
                        _baselineInspirationTotals))
                : BuildPersistedLoot(
                    ToItemQuantityDictionary(context.SalvageTotals),
                    ToItemQuantityDictionary(context.RecipeTotals),
                    ToItemQuantityDictionary(context.EnhancementTotals),
                    ToItemQuantityDictionary(context.InspirationTotals))
        };
    }

    private PersistedSessionDocument? BuildTrackedSessionDocument()
    {
        var active = GetActiveContext() ?? _lastKnownActiveContext;
        if (active is null || _currentTrackedSessionId is null || _trackedStartedAt is null)
        {
            return null;
        }

        var endedAt = _trackedStartedAt.Value + _lastTrackedElapsed;
        if (endedAt < _trackedStartedAt.Value)
        {
            endedAt = _trackedStartedAt.Value;
        }

        return new PersistedSessionDocument
        {
            SessionId = _currentTrackedSessionId.Value,
            SessionType = SessionType.Tracked,
            Account = ResolveAccountLabel(active),
            Character = active.CharacterDisplayName,
            StartedAtUtc = _trackedStartedAt.Value,
            EndedAtUtc = endedAt,
            DurationSeconds = (long)Math.Round(_lastTrackedElapsed.TotalSeconds),
            Earnings = new SessionEarnings
            {
                Experience = _lastTrackedExperience,
                Influence = _lastTrackedInfluence
            },
            Currencies = ToPersistedCurrencies(_lastTrackedCurrencyTotals),
            Loot = BuildPersistedLoot(
                _lastTrackedSalvageTotals,
                _lastTrackedRecipeTotals,
                _lastTrackedEnhancementTotals,
                _lastTrackedInspirationTotals)
        };
    }

    private void RebaseTrackedBaselinesAfterPause(LiveMonitoringContextIdentityReadModel active)
    {
        var presentationExperience = active.SessionExperienceGained - _baselineExperienceGained;
        var presentationInfluence = active.SessionGameplayInfluenceGained - _baselineGameplayInfluenceGained;
        _trackedBaselinePresentationExperience = presentationExperience - _lastTrackedExperience;
        _trackedBaselinePresentationInfluence = presentationInfluence - _lastTrackedInfluence;

        RebaseQuantityBaseline(
            _trackedBaselineCurrencyTotals,
            ToCurrencyQuantityDictionary(active.RewardCurrencyTotals),
            _lastTrackedCurrencyTotals);
        RebaseQuantityBaseline(
            _trackedBaselineSalvageTotals,
            ToItemQuantityDictionary(active.SalvageTotals),
            _lastTrackedSalvageTotals);
        RebaseQuantityBaseline(
            _trackedBaselineEnhancementTotals,
            ToItemQuantityDictionary(active.EnhancementTotals),
            _lastTrackedEnhancementTotals);
        RebaseQuantityBaseline(
            _trackedBaselineRecipeTotals,
            ToItemQuantityDictionary(active.RecipeTotals),
            _lastTrackedRecipeTotals);
        RebaseQuantityBaseline(
            _trackedBaselineInspirationTotals,
            ToItemQuantityDictionary(active.InspirationTotals),
            _lastTrackedInspirationTotals);
    }

    private void ClearTrackedEconomySnapshots()
    {
        _lastTrackedCurrencyTotals.Clear();
        _lastTrackedSalvageTotals.Clear();
        _lastTrackedEnhancementTotals.Clear();
        _lastTrackedRecipeTotals.Clear();
        _lastTrackedInspirationTotals.Clear();
    }

    private static void RebaseQuantityBaseline(
        Dictionary<string, long> baseline,
        Dictionary<string, long> current,
        Dictionary<string, long> lastTracked)
    {
        baseline.Clear();
        foreach (var key in current.Keys.Union(lastTracked.Keys, StringComparer.OrdinalIgnoreCase))
        {
            var quantity = current.GetValueOrDefault(key) - lastTracked.GetValueOrDefault(key);
            if (quantity != 0)
            {
                baseline[key] = quantity;
            }
        }
    }

    private static Dictionary<string, long> SubtractQuantityDictionaries(
        Dictionary<string, long> current,
        IReadOnlyDictionary<string, long> baseline)
    {
        var result = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, quantity) in current)
        {
            var delta = quantity - baseline.GetValueOrDefault(name);
            if (delta > 0)
            {
                result[name] = delta;
            }
        }

        return result;
    }

    private static IReadOnlyList<PersistedSessionCurrencyTotal> ToPersistedCurrencies(
        IReadOnlyDictionary<string, long> totals) =>
        totals
            .Where(pair => pair.Value > 0 && !string.IsNullOrWhiteSpace(pair.Key))
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => new PersistedSessionCurrencyTotal
            {
                Name = pair.Key,
                Quantity = pair.Value
            })
            .ToArray();

    private static PersistedSessionLootTotals BuildPersistedLoot(
        IReadOnlyDictionary<string, long> salvage,
        IReadOnlyDictionary<string, long> recipes,
        IReadOnlyDictionary<string, long> enhancements,
        IReadOnlyDictionary<string, long> inspirations) =>
        new()
        {
            Salvage = ToPersistedLootItems(salvage),
            Recipes = ToPersistedLootItems(recipes),
            Enhancements = ToPersistedLootItems(enhancements),
            Inspirations = ToPersistedLootItems(inspirations)
        };

    private static IReadOnlyList<PersistedSessionLootItem> ToPersistedLootItems(
        IReadOnlyDictionary<string, long> totals) =>
        totals
            .Where(pair => pair.Value > 0 && !string.IsNullOrWhiteSpace(pair.Key))
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => new PersistedSessionLootItem
            {
                Name = pair.Key,
                Quantity = pair.Value
            })
            .ToArray();

    private static string? ResolveAccountLabel(LiveMonitoringContextIdentityReadModel context) =>
        string.IsNullOrWhiteSpace(context.AccountDisplayName)
            ? context.AccountStableId
            : context.AccountDisplayName;

    [RelayCommand]
    private void OpenCharacterPicker()
    {
        var contextPanel = ViewedContextPanel;
        if (contextPanel is null)
        {
            return;
        }

        contextPanel.IsPickerOpen = true;
        contextPanel.SelectedPickerCharacter = contextPanel.PickerCharacters.FirstOrDefault();
    }

    [RelayCommand]
    private void CloseCharacterPicker()
    {
        var contextPanel = ViewedContextPanel;
        if (contextPanel is null)
        {
            return;
        }

        contextPanel.IsPickerOpen = false;
        contextPanel.SelectionMessage = null;
    }

    [RelayCommand]
    private void ConfirmSelectedCharacter()
    {
        var contextPanel = ViewedContextPanel;
        if (contextPanel?.SelectedPickerCharacter is null)
        {
            return;
        }

        var result = _gameplaySessionManager.ConfirmCharacter(
            contextPanel.ContextId,
            contextPanel.SelectedPickerCharacter.RecordId);

        if (result.IsSuccess)
        {
            contextPanel.IsPickerOpen = false;
            contextPanel.SelectionMessage = null;
            return;
        }

        contextPanel.SelectionMessage = result.Detail ?? result.Outcome.ToString();
    }

    private sealed class LiveSessionContextPresentationState
    {
        public long BaselineExperienceGained;

        public long BaselineGameplayInfluenceGained;

        public CombatScaledAmount BaselineDamageDealt;

        public DateTimeOffset? PresentationDurationAnchor;

        public Dictionary<string, long> BaselineCurrencyTotals =
            new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, long> BaselineSalvageTotals =
            new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, long> BaselineEnhancementTotals =
            new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, long> BaselineRecipeTotals =
            new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, long> BaselineInspirationTotals =
            new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class ContextLiveSessionPersistenceState
    {
        public Guid? LiveSessionId;

        public bool Persisted;

        public DateTimeOffset? SessionStartedAt;

        public LiveMonitoringContextIdentityReadModel? LastKnownContext;
    }
}

/// <summary>Compact Live Session tab for selecting among concurrently active gameplay sessions.</summary>
public sealed class LiveSessionTabViewModel
{
    public required MonitoringContextId ContextId { get; init; }

    public required string PrimaryLabel { get; init; }

    public string? SecondaryLabel { get; init; }

    public required bool IsSelected { get; init; }

    public static LiveSessionTabViewModel FromContext(
        LiveMonitoringContextIdentityReadModel context,
        bool isSelected,
        AccountAnonymityService? accountAnonymityService = null)
    {
        var hasCharacter = context.IsConfirmed
            && !string.IsNullOrWhiteSpace(context.CharacterDisplayName);
        var primary = hasCharacter
            ? context.CharacterDisplayName!
            : "Character Unknown";
        string? secondary = null;
        if (!string.IsNullOrWhiteSpace(context.AccountDisplayName))
        {
            secondary = context.AccountDisplayName;
        }
        else if (!string.IsNullOrWhiteSpace(context.AccountStableId))
        {
            secondary = context.AccountStableId;
        }

        if (secondary is not null)
        {
            secondary = (accountAnonymityService ?? new AccountAnonymityService())
                .MaskForPresentation(secondary);
        }

        return new LiveSessionTabViewModel
        {
            ContextId = context.ContextId,
            PrimaryLabel = primary,
            SecondaryLabel = secondary,
            IsSelected = isSelected
        };
    }
}

/// <summary>Character identity for one monitoring context, shown in the identity strip.</summary>
public sealed partial class LiveSessionContextPanelViewModel : ObservableObject
{
    public required MonitoringContextId ContextId { get; init; }

    public required string AccountLabel { get; init; }

    public required string IdentityStatusLabel { get; init; }

    public required DashboardStatusKind IdentityStatusKind { get; init; }

    public required bool RequiresManualSelection { get; init; }

    public required bool IsConfirmed { get; init; }

    public required int CandidateCount { get; init; }

    public ObservableCollection<CharacterPickerOptionViewModel> PickerCharacters { get; init; } = [];

    [ObservableProperty]
    private bool _isPickerOpen;

    [ObservableProperty]
    private CharacterPickerOptionViewModel? _selectedPickerCharacter;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectionMessage))]
    private string? _selectionMessage;

    public bool HasSelectionMessage => !string.IsNullOrWhiteSpace(SelectionMessage);

    public static LiveSessionContextPanelViewModel FromReadModel(
        LiveMonitoringContextIdentityReadModel model,
        AccountAnonymityService? accountAnonymityService = null)
    {
        var accountName = string.IsNullOrWhiteSpace(model.AccountDisplayName)
            ? model.AccountStableId
            : model.AccountDisplayName;
        var panel = new LiveSessionContextPanelViewModel
        {
            ContextId = model.ContextId,
            AccountLabel = (accountAnonymityService ?? new AccountAnonymityService())
                .MaskForPresentation(accountName, "Unbound account"),
            IdentityStatusLabel = model.IdentityStatusLabel,
            IdentityStatusKind = MapStatusKind(model),
            RequiresManualSelection = model.RequiresManualSelection,
            IsConfirmed = model.IsConfirmed,
            CandidateCount = model.CandidateCount
        };

        foreach (var character in model.PickerCharacters)
        {
            panel.PickerCharacters.Add(new CharacterPickerOptionViewModel
            {
                RecordId = character.RecordId,
                DisplayName = character.DisplayName,
                LastObservedLabel = FormatLastObserved(character.LastObservedAt)
            });
        }

        return panel;
    }

    private static DashboardStatusKind MapStatusKind(LiveMonitoringContextIdentityReadModel model)
    {
        if (model.IsConfirmed)
        {
            return DashboardStatusKind.Success;
        }

        if (model.CharacterIdentityResolutionState == CharacterIdentityResolutionState.IdentityRequired
            || model.CharacterIdentityResolutionState == CharacterIdentityResolutionState.Conflicted)
        {
            return DashboardStatusKind.Warning;
        }

        return DashboardStatusKind.Information;
    }

    private static string FormatLastObserved(DateTimeOffset timestamp)
    {
        if (timestamp == default)
        {
            return "Last played: Not yet observed";
        }

        return $"Last played: {timestamp.ToLocalTime():yyyy-MM-dd}";
    }
}

public sealed class CharacterPickerOptionViewModel
{
    public required CharacterRecordId RecordId { get; init; }

    public required string DisplayName { get; init; }

    public required string LastObservedLabel { get; init; }
}

/// <summary>One fixed-position aggregate row (currency or special reward).</summary>
public sealed partial class LiveSessionAggregateRowViewModel : ObservableObject
{
    public required string DisplayName { get; init; }

    /// <summary>Singular telemetry name this row reads its total from.</summary>
    public required string SourceKey { get; init; }

    /// <summary>Renders a divider above the row to separate related groups.</summary>
    public bool StartsGroup { get; init; }

    public LiveSessionCurrencyNameColor NameColor { get; init; }

    [ObservableProperty]
    private string _quantityLabel = "0";
}

public enum LiveSessionCurrencyNameColor
{
    Default,
    Bronze,
    Gold,
    Blue,
    Orange,
    White,
    Amber,
    Purple
}

/// <summary>One aggregated item row rendered exactly as Homecoming emitted the name.</summary>
public sealed class LiveSessionItemRowViewModel
{
    public required string DisplayName { get; init; }

    public required string QuantityLabel { get; init; }

    /// <summary>Canonical rarity code for <see cref="ReferenceRarityPresentation"/> brush resolution.</summary>
    public string? RarityCode { get; init; }

    /// <summary>Theme brush resource key for loot row foreground presentation.</summary>
    public string? PresentationBrushKey { get; init; }

    public ImageSource? IconSource { get; init; }

    public bool HasIcon => IconSource is not null;
}
