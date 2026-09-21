using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using CoHAnalytics.Models;
using CoHAnalytics.Orchestration.Contracts;
using CoHAnalytics.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CoHAnalytics.ViewModels.Workspaces;

/// <summary>
/// Analytics workspace with XCOM-style chip navigation.
/// Default chip: <see cref="AnalyticsChipId.Overview"/>.
/// </summary>
public sealed partial class AnalyticsViewModel : WorkspaceEnvironmentStatusViewModelBase, IDisposable
{
    private readonly IGameplaySessionIdentityReadService _identityReadService;
    private readonly IGameplaySessionContextResolver _gameplaySessionContextResolver;
    private readonly IViewedContextService _viewedContextService;
    private readonly ICharacterHistoricalPerformanceReadService? _historicalPerformanceReadService;
    private readonly ICharacterPerformanceObservationRepository? _performanceObservationRepository;
    private readonly ICharacterRepository? _characterRepository;
    private readonly HomecomingAccountDiscoveryService? _accountDiscoveryService;
    private readonly IHistoricalSegmentDeleteConfirmationService? _segmentDeleteConfirmationService;
    private readonly AccountAnonymityService _accountAnonymityService;
    private readonly DispatcherTimer? _refreshTimer;
    private long _includedHistoricalSegmentCount;
    private bool _disposed;

    public AnalyticsViewModel(
        IApplicationOrchestrator orchestrator,
        IGameRuntimeService gameRuntimeService,
        IGameplaySessionIdentityReadService identityReadService,
        IGameplaySessionContextResolver gameplaySessionContextResolver,
        IViewedContextService viewedContextService,
        AccountAnonymityService? accountAnonymityService = null,
        ICharacterHistoricalPerformanceReadService? historicalPerformanceReadService = null,
        ICharacterPerformanceObservationRepository? performanceObservationRepository = null,
        ICharacterRepository? characterRepository = null,
        HomecomingAccountDiscoveryService? accountDiscoveryService = null,
        IHistoricalSegmentDeleteConfirmationService? segmentDeleteConfirmationService = null)
        : base(orchestrator, gameRuntimeService)
    {
        _identityReadService = identityReadService;
        _gameplaySessionContextResolver = gameplaySessionContextResolver;
        _viewedContextService = viewedContextService;
        _historicalPerformanceReadService = historicalPerformanceReadService;
        _performanceObservationRepository = performanceObservationRepository;
        _characterRepository = characterRepository;
        _accountDiscoveryService = accountDiscoveryService;
        _segmentDeleteConfirmationService = segmentDeleteConfirmationService;
        _accountAnonymityService = accountAnonymityService ?? new AccountAnonymityService();

        Chips =
        [
            new AnalyticsChipViewModel("Overview", AnalyticsChipId.Overview, isActive: true),
            new AnalyticsChipViewModel("Combat", AnalyticsChipId.Combat)
        ];
        SelectedChip = AnalyticsChipId.Overview;

        WireEnvironmentStatus();
        _identityReadService.Changed += OnIdentityReadChanged;
        _viewedContextService.Changed += OnViewedContextChanged;
        _accountAnonymityService.Changed += OnAccountAnonymityChanged;
        if (_historicalPerformanceReadService is not null)
        {
            _historicalPerformanceReadService.Changed += OnHistoricalPerformanceChanged;
        }
        RefreshPresentation();

        if (Application.Current?.Dispatcher is { } dispatcher)
        {
            _refreshTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _refreshTimer.Tick += OnRefreshTick;
            _refreshTimer.Start();
        }
    }

    public override string Title => "Analytics";

    public string Subtitle => "Deep session, build, farm, and performance analysis";

    public ObservableCollection<AnalyticsChipViewModel> Chips { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOverviewSelected))]
    [NotifyPropertyChangedFor(nameof(IsEarningsSelected))]
    [NotifyPropertyChangedFor(nameof(IsCombatSelected))]
    [NotifyPropertyChangedFor(nameof(ShowOverviewContent))]
    [NotifyPropertyChangedFor(nameof(ShowEarningsContent))]
    [NotifyPropertyChangedFor(nameof(ShowCombatContent))]
    private AnalyticsChipId _selectedChip;

    public bool IsOverviewSelected => SelectedChip == AnalyticsChipId.Overview;

    public bool IsEarningsSelected => SelectedChip == AnalyticsChipId.Earnings;

    public bool IsCombatSelected => SelectedChip == AnalyticsChipId.Combat;

    public bool ShowOverviewContent => IsOverviewSelected;

    public bool ShowEarningsContent => IsEarningsSelected;

    public bool ShowCombatContent => IsCombatSelected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowContextSummary))]
    [NotifyPropertyChangedFor(nameof(ShowCombatContent))]
    private bool _hasActiveSession;

    public bool ShowContextSummary => HasActiveSession;

    [ObservableProperty]
    private string _contextCharacterLabel = "—";

    [ObservableProperty]
    private string _contextAccountLabel = "—";

    [ObservableProperty]
    private string _contextSessionStateLabel = "—";

    [ObservableProperty]
    private bool _contextSessionIsLive;

    [ObservableProperty]
    private string _combatStatusHeadline = "Idle";

    [ObservableProperty]
    private string _combatStatusDetail = "No recent combat activity";

    [ObservableProperty]
    private string _sessionDpsLabel = "—";

    [ObservableProperty]
    private string _sessionDamageDealtLabel = "—";

    [ObservableProperty]
    private string _trackedDpsLabel = "—";

    [ObservableProperty]
    private string _trackedDamageDealtLabel = "—";

    [ObservableProperty]
    private string _trackedStateLabel = "Not running";

    [ObservableProperty]
    private int _selectedRollingWindowMinutes = RollingCombatPresets.DefaultMinutes;

    [ObservableProperty]
    private string _rollingDpsLabel = "—";

    [ObservableProperty]
    private string _rollingDamageDealtLabel = "—";

    [ObservableProperty]
    private string _rollingWindowLabel = $"{RollingCombatPresets.DefaultMinutes} min";

    [ObservableProperty]
    private string _rollingDeltaLabel = "—";

    [ObservableProperty]
    private bool _rollingDeltaIsPositive;

    [ObservableProperty]
    private bool _rollingDeltaIsNegative;

    [ObservableProperty]
    private bool _rollingDeltaIsNeutral;

    [ObservableProperty]
    private bool _rollingDeltaIsUnavailable = true;

    [ObservableProperty]
    private string? _rollingDetail;

    [ObservableProperty]
    private string _accuracyAttemptsLabel = "—";

    [ObservableProperty]
    private string _accuracyHitsLabel = "—";

    [ObservableProperty]
    private string _accuracyMissesLabel = "—";

    [ObservableProperty]
    private string _accuracyHitPercentLabel = "—";

    [ObservableProperty]
    private string _accuracyAverageChanceLabel = "—";

    [ObservableProperty]
    private string _accuracyAverageRollLabel = "—";

    [ObservableProperty]
    private string? _accuracyForcedHitsLabel;

    [ObservableProperty]
    private string? _accuracyAutohitsLabel;

    [ObservableProperty]
    private string? _accuracyDetail;

    [ObservableProperty]
    private bool _accuracyShowMetrics;

    [ObservableProperty]
    private bool _accuracyShowSecondaryMetrics;

    [ObservableProperty]
    private CombatAccuracyScopePresentation _sessionAccuracy = new();

    [ObservableProperty]
    private CombatAccuracyScopePresentation _trackedAccuracy = new();

    [ObservableProperty]
    private CombatAccuracyScopePresentation _rollingAccuracy = new();

    [ObservableProperty]
    private CombatEnemyScopePresentation _sessionEnemies = new();

    [ObservableProperty]
    private CombatEnemyScopePresentation _trackedEnemies = new();

    [ObservableProperty]
    private CombatEnemyScopePresentation _rollingEnemies = new();

    [ObservableProperty]
    private AnalyticsOverviewMetricsPresentation _overview =
        AnalyticsOverviewPresentation.BuildNoCharacter();

    public ObservableCollection<AnalyticsHistoricalSegmentRowViewModel> HistoricalSegments { get; } = [];

    [ObservableProperty]
    private bool _isHistoricalSegmentManagerExpanded;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteSelectedHistoricalSegmentCommand))]
    private AnalyticsHistoricalSegmentRowViewModel? _selectedHistoricalSegment;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHistoricalSegmentError))]
    private string? _historicalSegmentErrorMessage;

    public bool HasHistoricalSegmentError => !string.IsNullOrWhiteSpace(HistoricalSegmentErrorMessage);

    [ObservableProperty]
    private bool _hasViewedCharacter;

    [ObservableProperty]
    private string _includedObservedDurationLabel = "—";

    public bool HasHistoricalSegments => HistoricalSegments.Count > 0;

    public bool ShowHistoricalEmptyState => HasViewedCharacter && !HasHistoricalSegments;

    public string OverviewSegmentCountLabel =>
        $"{_includedHistoricalSegmentCount.ToString("N0", CultureInfo.InvariantCulture)} of "
        + HistoricalSegments.Count.ToString("N0", CultureInfo.InvariantCulture);

    public ObservableCollection<AnalyticsRollingPresetViewModel> RollingPresets { get; } =
        CreateRollingPresets();

    public string SessionDpsCaption => PrimaryPerformanceMetric.BetaDamage.RateLabel;

    public string SessionDamageDealtCaption => PrimaryPerformanceMetric.BetaDamage.TotalLabel;

    public string TrackedDpsCaption => PrimaryPerformanceMetric.BetaDamage.RateLabel;

    public string TrackedDamageDealtCaption => PrimaryPerformanceMetric.BetaDamage.TotalLabel;

    public string EarningsPlaceholderMessage =>
        "Detailed XP, Influence, reward, and loot analysis will appear here.";

    public string TrackedSessionHint =>
        "Tracked session controls are available on Live Session.";

    [RelayCommand]
    private void SelectChip(AnalyticsChipId chipId)
    {
        if (chipId == AnalyticsChipId.Earnings)
        {
            chipId = AnalyticsChipId.Overview;
        }

        if (SelectedChip == chipId)
        {
            return;
        }

        SelectedChip = chipId;
        foreach (var chip in Chips)
        {
            chip.IsActive = chip.ChipId == chipId;
        }
    }

    [RelayCommand]
    private void SelectRollingPreset(int windowMinutes)
    {
        if (!RollingCombatPresets.SupportedMinutes.Contains(windowMinutes)
            || SelectedRollingWindowMinutes == windowMinutes)
        {
            return;
        }

        SelectedRollingWindowMinutes = windowMinutes;
        foreach (var preset in RollingPresets)
        {
            preset.IsActive = preset.WindowMinutes == windowMinutes;
        }

        RefreshLivePresentation();
    }

    [RelayCommand]
    private void SelectHistoricalSegment(AnalyticsHistoricalSegmentRowViewModel? segment)
    {
        if (segment is null)
        {
            return;
        }

        SelectedHistoricalSegment = ReferenceEquals(SelectedHistoricalSegment, segment)
            ? null
            : segment;
    }

    [RelayCommand]
    private void ToggleHistoricalSegmentExclusion(AnalyticsHistoricalSegmentRowViewModel? segment)
    {
        if (segment is null || _performanceObservationRepository is null)
        {
            return;
        }

        var result = _performanceObservationRepository.SetIncludeInOverview(
            segment.GameplaySessionId,
            segment.SegmentOrdinal,
            !segment.IncludeInOverview);

        if (result.IsSuccess)
        {
            HistoricalSegmentErrorMessage = null;
            return;
        }

        HistoricalSegmentErrorMessage =
            "The segment exclusion setting could not be saved. Your previous setting was restored.";
        DispatchRefresh(() => RefreshHistoricalOverview(segment.SegmentKey));
    }

    [RelayCommand]
    private void ToggleHistoricalSegmentManager()
    {
        if (HasHistoricalSegments)
        {
            IsHistoricalSegmentManagerExpanded = !IsHistoricalSegmentManagerExpanded;
        }
    }

    private bool CanDeleteSelectedHistoricalSegment() => SelectedHistoricalSegment is not null;

    [RelayCommand(CanExecute = nameof(CanDeleteSelectedHistoricalSegment))]
    private void DeleteSelectedHistoricalSegment()
    {
        var segment = SelectedHistoricalSegment;
        if (segment is null
            || _performanceObservationRepository is null
            || _segmentDeleteConfirmationService is null)
        {
            return;
        }

        var confirmed = _segmentDeleteConfirmationService.ConfirmDelete(
            new HistoricalSegmentDeleteConfirmationRequest
            {
                CharacterLabel = segment.CharacterLabel,
                AccountLabel = segment.AccountLabel,
                DateTimeLabel = segment.ConfirmationDateTimeLabel
            });
        if (!confirmed)
        {
            return;
        }

        var result = _performanceObservationRepository.Delete(
            segment.GameplaySessionId,
            segment.SegmentOrdinal);
        if (!result.IsSuccess)
        {
            HistoricalSegmentErrorMessage =
                "The historical segment could not be deleted. No historical data was changed.";
            DispatchRefresh(() => RefreshHistoricalOverview(segment.SegmentKey));
            return;
        }

        HistoricalSegmentErrorMessage = null;
        if (result.Outcome == CharacterPerformanceObservationDeleteOutcome.NotFound)
        {
            DispatchRefresh(() => RefreshHistoricalOverview());
        }
    }

    private void OnIdentityReadChanged(object? sender, GameplaySessionIdentityReadModelChangedEventArgs e) =>
        DispatchRefresh(RefreshPresentation);

    private void OnViewedContextChanged(object? sender, ViewedContextChangedEventArgs e) =>
        DispatchRefresh(RefreshPresentation);

    private void OnAccountAnonymityChanged(object? sender, EventArgs e) =>
        DispatchRefresh(RefreshPresentation);

    private void OnHistoricalPerformanceChanged(object? sender, EventArgs e) =>
        DispatchRefresh(() => RefreshHistoricalOverview());

    private void OnRefreshTick(object? sender, EventArgs e) => RefreshLivePresentation();

    private void RefreshPresentation()
    {
        RefreshLivePresentation();
        RefreshHistoricalOverview();
    }

    private void RefreshLivePresentation()
    {
        var selection = _gameplaySessionContextResolver.ResolveForLiveMonitoring();
        var context = selection.Context;
        HasActiveSession = context?.HasActiveSession == true;

        if (context is null || !context.HasActiveSession)
        {
            ContextCharacterLabel = "—";
            ContextAccountLabel = "—";
            ContextSessionStateLabel = "No active session";
            ContextSessionIsLive = false;
            ApplyUnavailableCombatPresentation();
            return;
        }

        ContextCharacterLabel = string.IsNullOrWhiteSpace(context.CharacterDisplayName)
            ? "Unknown character"
            : context.CharacterDisplayName;
        var accountName = string.IsNullOrWhiteSpace(context.AccountDisplayName)
            ? context.AccountStableId
            : context.AccountDisplayName;
        ContextAccountLabel = _accountAnonymityService.MaskForPresentation(accountName);
        ContextSessionStateLabel = context.SessionTimingEndAt is null ? "Live" : "Last session";
        ContextSessionIsLive = context.SessionTimingEndAt is null;

        ApplyCombatPresentation(context);
    }

    private void ApplyUnavailableCombatPresentation()
    {
        var metrics = CombatAnalyticsPresentation.BuildUnavailableMetrics(SelectedRollingWindowMinutes);
        ApplyCombatMetrics(metrics);
    }

    private void ApplyCombatPresentation(LiveMonitoringContextIdentityReadModel context)
    {
        var referenceAt = DateTimeOffset.UtcNow;
        var metrics = CombatAnalyticsPresentation.BuildMetrics(
            context,
            referenceAt,
            SelectedRollingWindowMinutes);
        ApplyCombatMetrics(metrics);
    }

    private void ApplyCombatMetrics(AnalyticsCombatMetricsPresentation metrics)
    {
        CombatStatusHeadline = metrics.Status.Headline;
        CombatStatusDetail = metrics.Status.Detail;
        SessionDpsLabel = metrics.Session.RateValue;
        SessionDamageDealtLabel = metrics.Session.TotalValue;
        TrackedDpsLabel = metrics.Tracked.RateValue;
        TrackedDamageDealtLabel = metrics.Tracked.TotalValue;
        TrackedStateLabel = metrics.TrackedStateLabel;
        RollingDpsLabel = metrics.Rolling.RateValue;
        RollingDamageDealtLabel = metrics.Rolling.DamageDealtValue;
        RollingWindowLabel = metrics.Rolling.WindowLabel;
        RollingDetail = metrics.Rolling.Detail;
        RollingDeltaLabel = metrics.Rolling.Delta.Label;
        RollingDeltaIsPositive = metrics.Rolling.Delta.IsPositive;
        RollingDeltaIsNegative = metrics.Rolling.Delta.IsNegative;
        RollingDeltaIsNeutral = metrics.Rolling.Delta.IsNeutral;
        RollingDeltaIsUnavailable = metrics.Rolling.Delta.IsUnavailable;
        SessionAccuracy = metrics.SessionAccuracy;
        TrackedAccuracy = metrics.TrackedAccuracy;
        RollingAccuracy = metrics.RollingAccuracy;
        SessionEnemies = metrics.SessionEnemies;
        TrackedEnemies = metrics.TrackedEnemies;
        RollingEnemies = metrics.RollingEnemies;
        AccuracyAttemptsLabel = metrics.Accuracy.AttemptsLabel;
        AccuracyHitsLabel = metrics.Accuracy.HitsLabel;
        AccuracyMissesLabel = metrics.Accuracy.MissesLabel;
        AccuracyHitPercentLabel = metrics.Accuracy.HitPercentLabel;
        AccuracyAverageChanceLabel = metrics.Accuracy.AverageChanceLabel;
        AccuracyAverageRollLabel = metrics.Accuracy.AverageRollLabel;
        AccuracyForcedHitsLabel = metrics.Accuracy.ForcedHitsLabel;
        AccuracyAutohitsLabel = metrics.Accuracy.AutohitsLabel;
        AccuracyDetail = metrics.Accuracy.Detail;
        AccuracyShowMetrics = metrics.Accuracy.ShowMetrics;
        AccuracyShowSecondaryMetrics = metrics.Accuracy.ShowSecondaryMetrics;
    }

    private void RefreshHistoricalOverview(string? preferredSelectedSegmentKey = null)
    {
        var selectedSegmentKey = preferredSelectedSegmentKey
            ?? SelectedHistoricalSegment?.SegmentKey;
        var characterRecordId = _viewedContextService.Current.CharacterRecordId;
        HasViewedCharacter = characterRecordId is not null;
        if (characterRecordId is null)
        {
            _includedHistoricalSegmentCount = 0;
            IncludedObservedDurationLabel = "—";
            Overview = AnalyticsOverviewPresentation.BuildNoCharacter();
            SelectedHistoricalSegment = null;
            HistoricalSegments.Clear();
            NotifyHistoricalSegmentStateChanged();
            return;
        }

        var snapshot = _historicalPerformanceReadService?.GetLifetime(characterRecordId)
            ?? CharacterHistoricalPerformanceSnapshot.Empty(characterRecordId);
        _includedHistoricalSegmentCount = snapshot.ObservationCount;
        IncludedObservedDurationLabel = AnalyticsOverviewPresentation.FormatObservedDuration(
            snapshot.ObservedDuration);
        Overview = AnalyticsOverviewPresentation.Build(snapshot);
        var segments = _historicalPerformanceReadService?.GetSegments(characterRecordId) ?? [];

        HistoricalSegments.Clear();
        AnalyticsHistoricalSegmentRowViewModel? selectedSegment = null;
        foreach (var segment in segments)
        {
            var row = CreateHistoricalSegmentRow(segment);
            HistoricalSegments.Add(row);
            if (string.Equals(row.SegmentKey, selectedSegmentKey, StringComparison.Ordinal))
            {
                selectedSegment = row;
            }
        }
        SelectedHistoricalSegment = selectedSegment;

        NotifyHistoricalSegmentStateChanged();
    }

    private AnalyticsHistoricalSegmentRowViewModel CreateHistoricalSegmentRow(
        CharacterHistoricalPerformanceSegment segment)
    {
        var observation = segment.Observation;
        var character = _characterRepository?.TryGetRecord(segment.CanonicalCharacterRecordId);
        var account = character is null
            ? null
            : _accountDiscoveryService?.Accounts.FirstOrDefault(candidate =>
                string.Equals(candidate.StableId, character.AccountStableId, StringComparison.Ordinal));
        var presentation = AnalyticsOverviewPresentation.Build(segment.Metrics);

        return new AnalyticsHistoricalSegmentRowViewModel
        {
            GameplaySessionId = observation.GameplaySessionId,
            SegmentOrdinal = observation.SegmentOrdinal,
            StartedAtLabel = observation.StartedAtUtc.ToLocalTime().ToString(
                "MM/dd hh:mm tt",
                CultureInfo.CurrentCulture),
            ConfirmationDateTimeLabel = observation.StartedAtUtc.ToLocalTime().ToString(
                "MMMM d, yyyy h:mm tt",
                CultureInfo.CurrentCulture),
            AccountLabel = _accountAnonymityService.MaskForPresentation(account?.DisplayName),
            CharacterLabel = string.IsNullOrWhiteSpace(character?.CurrentDisplayName)
                ? "Unknown character"
                : character.CurrentDisplayName,
            IncludeInOverview = observation.IncludeInOverview,
            ExperiencePerHourLabel = RemoveMetricSuffix(presentation.ExperiencePerHourLabel, "/hr"),
            InfluencePerHourLabel = RemoveMetricSuffix(presentation.InfluencePerHourLabel, "/hr"),
            DamagePerSecondLabel = RemoveMetricSuffix(presentation.HistoricalDpsLabel, " DPS"),
            AccuracyLabel = presentation.HitPercentLabel
        };
    }

    partial void OnSelectedHistoricalSegmentChanged(
        AnalyticsHistoricalSegmentRowViewModel? value)
    {
        foreach (var row in HistoricalSegments)
        {
            var isSelected = ReferenceEquals(row, value);
            row.IsSelected = isSelected;
            row.IsExpanded = isSelected;
        }
    }

    private void NotifyHistoricalSegmentStateChanged()
    {
        if (!HasHistoricalSegments)
        {
            IsHistoricalSegmentManagerExpanded = false;
        }

        OnPropertyChanged(nameof(HasHistoricalSegments));
        OnPropertyChanged(nameof(ShowHistoricalEmptyState));
        OnPropertyChanged(nameof(OverviewSegmentCountLabel));
    }

    private static string RemoveMetricSuffix(string label, string suffix) =>
        label.EndsWith(suffix, StringComparison.Ordinal)
            ? label[..^suffix.Length]
            : label;

    private static ObservableCollection<AnalyticsRollingPresetViewModel> CreateRollingPresets() =>
        new(RollingCombatPresets.SupportedMinutes.Select(minutes =>
            new AnalyticsRollingPresetViewModel(minutes, isActive: minutes == RollingCombatPresets.DefaultMinutes)));

    private void DispatchRefresh(Action refreshAction)
    {
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

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_refreshTimer is not null)
        {
            _refreshTimer.Tick -= OnRefreshTick;
            _refreshTimer.Stop();
        }

        _identityReadService.Changed -= OnIdentityReadChanged;
        _viewedContextService.Changed -= OnViewedContextChanged;
        _accountAnonymityService.Changed -= OnAccountAnonymityChanged;
        if (_historicalPerformanceReadService is not null)
        {
            _historicalPerformanceReadService.Changed -= OnHistoricalPerformanceChanged;
        }
        UnwireEnvironmentStatus();
    }
}

public sealed partial class AnalyticsHistoricalSegmentRowViewModel : ObservableObject
{
    public required GameplaySessionId GameplaySessionId { get; init; }

    public required int SegmentOrdinal { get; init; }

    public string SegmentKey => GameplaySessionId + ":" + SegmentOrdinal;

    public required string StartedAtLabel { get; init; }

    public required string ConfirmationDateTimeLabel { get; init; }

    public required string AccountLabel { get; init; }

    public required string CharacterLabel { get; init; }

    public required bool IncludeInOverview { get; init; }

    public bool IsExcluded => !IncludeInOverview;

    public required string ExperiencePerHourLabel { get; init; }

    public required string InfluencePerHourLabel { get; init; }

    public required string DamagePerSecondLabel { get; init; }

    public required string AccuracyLabel { get; init; }

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _isSelected;
}

public sealed partial class AnalyticsChipViewModel : ObservableObject
{
    public AnalyticsChipViewModel(string label, AnalyticsChipId chipId, bool isActive = false)
    {
        Label = label;
        ChipId = chipId;
        _isActive = isActive;
    }

    public string Label { get; }

    public AnalyticsChipId ChipId { get; }

    [ObservableProperty]
    private bool _isActive;
}

public sealed partial class AnalyticsRollingPresetViewModel : ObservableObject
{
    public AnalyticsRollingPresetViewModel(int windowMinutes, bool isActive = false)
    {
        WindowMinutes = windowMinutes;
        Label = $"{windowMinutes} min";
        _isActive = isActive;
    }

    public int WindowMinutes { get; }

    public string Label { get; }

    [ObservableProperty]
    private bool _isActive;
}
