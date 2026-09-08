using System.Collections.ObjectModel;
using System.Windows;
using CoHAnalytics.Models;
using CoHAnalytics.Navigation;
using CoHAnalytics.Orchestration.Contracts;
using CoHAnalytics.Orchestration.Models;
using CoHAnalytics.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CoHAnalytics.ViewModels.Workspaces;

public partial class DashboardViewModel : WorkspaceViewModelBase, IDisposable
{
    private readonly IApplicationOrchestrator _orchestrator;
    private readonly IGameRuntimeService _gameRuntimeService;
    private readonly SettingsService _settingsService;
    private readonly HomecomingInstallationService _homecomingInstallationService;
    private readonly HomecomingAccountDiscoveryService _accountDiscoveryService;
    private readonly MidsInstallationService _midsInstallationService;
    private readonly IAccountsWorkspaceNavigation _accountsWorkspaceNavigation;
    private readonly ApplicationActivityLogService _activityLogService;
    private readonly AccountAnonymityService _accountAnonymityService;
    private readonly Dictionary<string, ApplicationContributorLifecycleState> _providerLifecycles = new(StringComparer.Ordinal);
    private long _lastAppliedRevision = -1;
    private bool _disposed;

    public DashboardViewModel(
        IApplicationOrchestrator orchestrator,
        IGameRuntimeService gameRuntimeService,
        SettingsService settingsService,
        HomecomingInstallationService homecomingInstallationService,
        HomecomingAccountDiscoveryService accountDiscoveryService,
        MidsInstallationService midsInstallationService,
        IAccountsWorkspaceNavigation accountsWorkspaceNavigation,
        ApplicationActivityLogService activityLogService,
        AccountAnonymityService? accountAnonymityService = null)
    {
        _orchestrator = orchestrator;
        _gameRuntimeService = gameRuntimeService;
        _settingsService = settingsService;
        _homecomingInstallationService = homecomingInstallationService;
        _accountDiscoveryService = accountDiscoveryService;
        _midsInstallationService = midsInstallationService;
        _accountsWorkspaceNavigation = accountsWorkspaceNavigation;
        _activityLogService = activityLogService;
        _accountAnonymityService = accountAnonymityService ?? new AccountAnonymityService();
        _gameRuntimeService.StatusChanged += OnRuntimeStatusChanged;
        _orchestrator.SnapshotChanged += OnSnapshotChanged;
        _activityLogService.Changed += OnActivityLogChanged;
        _accountAnonymityService.Changed += OnAccountAnonymityChanged;

        Accounts = new ObservableCollection<AccountCardViewModel>(
            BuildRecentAccountCards(_accountDiscoveryService.Accounts));

        MonitoringTiles = [];
        RecentActivity = [];
        Locations = [];

        EnvironmentTiles =
        [
            CreateHomecomingEnvironmentTile(),
            CreateMidsEnvironmentTile(),
            CreateEnhancementDatabaseEnvironmentTile(),
            CreateBuildLibraryEnvironmentTile()
        ];

        ApplyRuntimeStatus(_gameRuntimeService.CurrentStatus, _gameRuntimeService.LastErrorMessage);
        RefreshRecentActivity();
        if (_gameRuntimeService.CurrentStatus == GameRuntimeStatus.Running)
        {
            _activityLogService.Record("homecoming.detected", "Homecoming detected");
        }

        ApplySnapshot(_orchestrator.Current);
    }

    public override string Title => "Dashboard";

    public string Subtitle => "Live monitoring overview for your Homecoming accounts.";

    public string ApplicationVersionLabel => ApplicationMetadata.VersionLabel;

    [ObservableProperty]
    private string _appStatusHeadline = "Initializing";

    [ObservableProperty]
    private string _appStatusDetail = "Initializing";

    [ObservableProperty]
    private string _appStatusSecondaryText = "Starting services";

    [ObservableProperty]
    private DashboardStatusKind _appStatusKind = DashboardStatusKind.Information;

    [ObservableProperty]
    private string _monitoringHeadline = "STATUS";

    [ObservableProperty]
    private string _monitoringMessage = "Initializing application status.";

    [ObservableProperty]
    private DashboardStatusKind _monitoringBannerKind = DashboardStatusKind.Information;

    public ObservableCollection<AccountCardViewModel> Accounts { get; }

    public ObservableCollection<MonitoringTileViewModel> MonitoringTiles { get; }

    public ObservableCollection<DashboardActivityItemViewModel> RecentActivity { get; }

    public bool HasRecentActivity => RecentActivity.Count > 0;

    public ObservableCollection<DashboardLocationViewModel> Locations { get; }

    public ObservableCollection<EnvironmentTileViewModel> EnvironmentTiles { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GameStatusLabel))]
    private GameStatusState _gameStatus = GameStatusState.Off;

    private GameRuntimeStatus _runtimeStatus = GameRuntimeStatus.Unconfigured;

    public string GameStatusLabel => GameStatus switch
    {
        GameStatusState.Running => "Online",
        GameStatusState.Off => "Offline",
        GameStatusState.Error when _runtimeStatus == GameRuntimeStatus.Unconfigured => "Not Configured",
        GameStatusState.Error => "Error",
        _ => "Unknown"
    };

    public string GameStatusToolTip => _runtimeStatus switch
    {
        GameRuntimeStatus.Running =>
            "Homecoming Game Status" + Environment.NewLine + Environment.NewLine +
            "Online — Homecoming client detected." + Environment.NewLine + Environment.NewLine +
            "Click to open the Homecoming Launcher.",
        GameRuntimeStatus.Off =>
            "Homecoming Game Status" + Environment.NewLine + Environment.NewLine +
            "Offline — No Homecoming client detected." + Environment.NewLine + Environment.NewLine +
            "Click to open the Homecoming Launcher.",
        GameRuntimeStatus.Unconfigured =>
            "Homecoming Game Status" + Environment.NewLine + Environment.NewLine +
            "Homecoming installation not configured." + Environment.NewLine + Environment.NewLine +
            "Open Settings to configure your installation.",
        GameRuntimeStatus.Error =>
            "Homecoming Game Status" + Environment.NewLine + Environment.NewLine +
            "Unable to determine Homecoming status." + Environment.NewLine + Environment.NewLine +
            "See App Status for details.",
        _ =>
            "Homecoming Game Status" + Environment.NewLine + Environment.NewLine +
            "Unable to determine Homecoming status." + Environment.NewLine + Environment.NewLine +
            "See App Status for details."
    };

    [RelayCommand]
    private async Task LaunchGameAsync()
    {
        await _gameRuntimeService.LaunchAsync().ConfigureAwait(true);
        ApplyRuntimeStatus(_gameRuntimeService.CurrentStatus, _gameRuntimeService.LastErrorMessage);
    }

    [RelayCommand]
    private void ViewAccountDetails(string? stableId)
    {
        if (string.IsNullOrWhiteSpace(stableId))
        {
            return;
        }

        _accountsWorkspaceNavigation.OpenAccountsWorkspace(stableId);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _gameRuntimeService.StatusChanged -= OnRuntimeStatusChanged;
        _orchestrator.SnapshotChanged -= OnSnapshotChanged;
        _activityLogService.Changed -= OnActivityLogChanged;
        _accountAnonymityService.Changed -= OnAccountAnonymityChanged;
    }

    internal void ApplySnapshot(ApplicationStateSnapshot snapshot)
    {
        if (snapshot.Revision < _lastAppliedRevision)
        {
            return;
        }

        if (snapshot.Revision == _lastAppliedRevision && _lastAppliedRevision >= 0)
        {
            return;
        }

        _lastAppliedRevision = snapshot.Revision;

        var presentation = DashboardApplicationStatusMapper.Map(snapshot);

        AppStatusHeadline = presentation.AppStatusHeadline;
        AppStatusDetail = presentation.AppStatusDetail;
        AppStatusSecondaryText = presentation.AppStatusSecondaryText;
        AppStatusKind = presentation.AppStatusKind;
        MonitoringHeadline = presentation.MonitoringBannerHeadline;
        MonitoringMessage = presentation.MonitoringBannerMessage;
        MonitoringBannerKind = presentation.MonitoringBannerKind;

        MonitoringTiles.Clear();
        foreach (var card in presentation.MonitoringCards)
        {
            MonitoringTiles.Add(card);
        }

        RecordProviderLifecycleChanges(snapshot);
        RecordApplicationStatus(snapshot, presentation);
    }

    private void OnSnapshotChanged(object? sender, ApplicationStateChangedEventArgs e)
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
                dispatcher.Invoke(() => ApplySnapshot(e.Snapshot));
                return;
            }
        }

        ApplySnapshot(e.Snapshot);
    }

    private void OnRuntimeStatusChanged(object? sender, GameRuntimeStatusChangedEventArgs e)
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
                dispatcher.Invoke(() => ApplyRuntimeTransition(e));
                return;
            }
        }

        ApplyRuntimeTransition(e);
    }

    private void ApplyRuntimeTransition(GameRuntimeStatusChangedEventArgs e)
    {
        if (e.NewStatus == GameRuntimeStatus.Running
            && e.PreviousStatus != GameRuntimeStatus.Running)
        {
            _activityLogService.Record("homecoming.detected", "Homecoming detected");
        }
        else if (e.PreviousStatus == GameRuntimeStatus.Running
                 && e.NewStatus != GameRuntimeStatus.Running)
        {
            _activityLogService.Record("homecoming.stopped", "Homecoming stopped");
        }

        ApplyRuntimeStatus(e.NewStatus, _gameRuntimeService.LastErrorMessage);
    }

    private void OnActivityLogChanged(object? sender, EventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            if (!dispatcher.HasShutdownStarted && !dispatcher.HasShutdownFinished)
            {
                dispatcher.Invoke(RefreshRecentActivity);
            }

            return;
        }

        RefreshRecentActivity();
    }

    private void OnAccountAnonymityChanged(object? sender, EventArgs e)
    {
        void Refresh()
        {
            Accounts.Clear();
            foreach (var account in BuildRecentAccountCards(_accountDiscoveryService.Accounts))
            {
                Accounts.Add(account);
            }

            RefreshLocations();
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(Refresh);
            return;
        }

        Refresh();
    }

    private void RefreshRecentActivity()
    {
        RecentActivity.Clear();
        foreach (var entry in _activityLogService.Entries.Reverse().Take(4))
        {
            RecentActivity.Add(new DashboardActivityItemViewModel(entry));
        }

        OnPropertyChanged(nameof(HasRecentActivity));
    }

    private void RecordProviderLifecycleChanges(ApplicationStateSnapshot snapshot)
    {
        RecordLifecycle(
            snapshot,
            ApplicationProviders.Parser,
            "parser.started",
            "Parser started",
            "parser.stopped",
            "Parser stopped");
        RecordLifecycle(
            snapshot,
            ApplicationProviders.MonitoringSessionManager,
            "monitoring.started",
            "Monitoring started",
            "monitoring.suspended",
            "Monitoring suspended");
    }

    private void RecordLifecycle(
        ApplicationStateSnapshot snapshot,
        string providerId,
        string startedEventType,
        string startedMessage,
        string stoppedEventType,
        string stoppedMessage)
    {
        var provider = snapshot.Providers.FirstOrDefault(item =>
            string.Equals(item.ProviderId, providerId, StringComparison.Ordinal));
        if (provider is null)
        {
            return;
        }

        var isRunning = provider.LifecycleState == ApplicationContributorLifecycleState.Running;
        if (!_providerLifecycles.TryGetValue(providerId, out var previous))
        {
            if (isRunning)
            {
                _activityLogService.Record(startedEventType, startedMessage, provider.ObservedAt);
            }
        }
        else
        {
            var wasRunning = previous == ApplicationContributorLifecycleState.Running;
            if (!wasRunning && isRunning)
            {
                _activityLogService.Record(startedEventType, startedMessage, provider.ObservedAt);
            }
            else if (wasRunning && !isRunning)
            {
                _activityLogService.Record(stoppedEventType, stoppedMessage, provider.ObservedAt);
            }
        }

        _providerLifecycles[providerId] = provider.LifecycleState;
    }

    private void RecordApplicationStatus(
        ApplicationStateSnapshot snapshot,
        DashboardApplicationStatusMapper.Presentation presentation)
    {
        if (snapshot.Revision <= 0)
        {
            return;
        }

        if (snapshot.PrimaryIssue is { } issue)
        {
            _activityLogService.Record(
                $"application.issue.{issue.Code}",
                issue.Summary,
                issue.CreatedAt);
            return;
        }

        _activityLogService.Record(
            $"application.state.{snapshot.State.ToString().ToLowerInvariant()}",
            presentation.AppStatusDetail,
            snapshot.UpdatedAt);
    }

    private void ApplyRuntimeStatus(GameRuntimeStatus runtimeStatus, string? errorMessage)
    {
        _runtimeStatus = runtimeStatus;

        GameStatus = runtimeStatus switch
        {
            GameRuntimeStatus.Running => GameStatusState.Running,
            GameRuntimeStatus.Off => GameStatusState.Off,
            GameRuntimeStatus.Unconfigured => GameStatusState.Error,
            GameRuntimeStatus.Error => GameStatusState.Error,
            _ => GameStatusState.Error
        };

        OnPropertyChanged(nameof(GameStatusToolTip));
        UpdateHomecomingEnvironmentTile();
        RefreshLocations();
    }

    private void RefreshLocations()
    {
        Locations.Clear();
        Locations.Add(new DashboardLocationViewModel(
            "CoH Analytics Data",
            GetApplicationDataRoot()));

        if (_homecomingInstallationService.CurrentInstallation is { } installation)
        {
            Locations.Add(new DashboardLocationViewModel(
                "Homecoming Installation",
                installation.InstallRoot));
        }

        AddAccountLocations(
            "City of Heroes Chat Logs",
            _accountDiscoveryService.Accounts.Where(account => account.HasLogsFolder),
            "Logs");
        AddAccountLocations(
            "Build Import",
            _accountDiscoveryService.Accounts.Where(account => account.HasBuildsFolder),
            "Builds");
    }

    private string GetApplicationDataRoot()
    {
        var settingsDirectory = Path.GetDirectoryName(_settingsService.SettingsPath);
        return string.IsNullOrWhiteSpace(settingsDirectory)
            ? ApplicationDataPaths.GetApplicationRoot()
            : settingsDirectory;
    }

    private void AddAccountLocations(
        string title,
        IEnumerable<HomecomingAccount> accounts,
        string childDirectoryName)
    {
        var orderedAccounts = accounts
            .OrderBy(account => account.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(account => account.DisplayName, StringComparer.Ordinal)
            .ThenBy(account => account.FolderPath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(account => account.FolderPath, StringComparer.Ordinal)
            .ToArray();

        for (var index = 0; index < orderedAccounts.Length; index++)
        {
            var locationTitle = orderedAccounts.Length == 1
                ? title
                : $"{title} ({index + 1} of {orderedAccounts.Length})";
            Locations.Add(new DashboardLocationViewModel(
                locationTitle,
                GetAccountLocationDisplayPath(orderedAccounts[index], childDirectoryName)));
        }
    }

    private string GetAccountLocationDisplayPath(
        HomecomingAccount account,
        string childDirectoryName)
    {
        if (!_accountAnonymityService.IsEnabled)
        {
            return Path.Combine(account.FolderPath, childDirectoryName);
        }

        var accountFolderPath = account.FolderPath.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        var accountFolderName = Path.GetFileName(accountFolderPath);
        var maskedFolderName = _accountAnonymityService.MaskForPresentation(
            accountFolderName,
            fallback: "Masked account");
        var accountsRoot = Path.GetDirectoryName(accountFolderPath);

        return string.IsNullOrWhiteSpace(accountsRoot)
            ? Path.Combine(maskedFolderName, childDirectoryName)
            : Path.Combine(accountsRoot, maskedFolderName, childDirectoryName);
    }

    private IEnumerable<AccountCardViewModel> BuildRecentAccountCards(IEnumerable<HomecomingAccount> accounts) =>
        HomecomingAccountPresentation
            .SelectRecentAccounts(accounts)
            .Select(ToAccountCardViewModel);

    private AccountCardViewModel ToAccountCardViewModel(HomecomingAccount account)
    {
        return account.Status switch
        {
            HomecomingAccountStatus.Ready when account.HasHistoricalLogs && account.NewestLogTimestamp is not null =>
                new AccountCardViewModel
                {
                    StableId = account.StableId,
                    Name = _accountAnonymityService.MaskForPresentation(account.DisplayName),
                    StatusLabel = "READY",
                    StatusKind = DashboardStatusKind.Success,
                    Detail = $"Newest log: {account.NewestLogTimestamp.Value:yyyy-MM-dd HH:mm:ss}"
                },
            HomecomingAccountStatus.Ready =>
                new AccountCardViewModel
                {
                    StableId = account.StableId,
                    Name = _accountAnonymityService.MaskForPresentation(account.DisplayName),
                    StatusLabel = "READY",
                    StatusKind = DashboardStatusKind.Success,
                    Detail = "Logs folder detected."
                },
            _ =>
                new AccountCardViewModel
                {
                    StableId = account.StableId,
                    Name = _accountAnonymityService.MaskForPresentation(account.DisplayName),
                    StatusLabel = "NO LOGS",
                    StatusKind = DashboardStatusKind.Warning,
                    Detail = "No Logs folder detected."
                }
        };
    }

    private EnvironmentTileViewModel CreateHomecomingEnvironmentTile()
    {
        var (primaryText, statusLabel, statusKind) = GetHomecomingEnvironmentDisplay();
        return new EnvironmentTileViewModel(
            "Homecoming Install",
            primaryText,
            statusLabel,
            "Assets/Images/Environment/homecoming-install.png",
            iconRenderSize: 65,
            statusKind: statusKind);
    }

    private void UpdateHomecomingEnvironmentTile()
    {
        if (EnvironmentTiles.Count == 0)
        {
            return;
        }

        EnvironmentTiles[0] = CreateHomecomingEnvironmentTile();
    }

    private (string PrimaryText, string StatusLabel, DashboardStatusKind StatusKind) GetHomecomingEnvironmentDisplay()
    {
        if (_homecomingInstallationService.LastDiscovery.MultipleInstallAmbiguity)
        {
            return ("Multiple installations found", "UNKNOWN", DashboardStatusKind.Warning);
        }

        var installation = _homecomingInstallationService.CurrentInstallation;
        if (installation is not null)
        {
            return (installation.InstallRoot, "VALID", DashboardStatusKind.Success);
        }

        return ("Not discovered", "UNKNOWN", DashboardStatusKind.Warning);
    }

    private EnvironmentTileViewModel CreateMidsEnvironmentTile()
    {
        var (primaryText, statusLabel, statusKind) = GetMidsEnvironmentDisplay();
        return new EnvironmentTileViewModel(
            "Mids Reborn Install",
            primaryText,
            statusLabel,
            "Assets/Images/Environment/mids-install.png",
            iconRenderSize: 65,
            statusKind: statusKind);
    }

    private void UpdateMidsEnvironmentTile()
    {
        if (EnvironmentTiles.Count < 2)
        {
            return;
        }

        EnvironmentTiles[1] = CreateMidsEnvironmentTile();
    }

    private (string PrimaryText, string StatusLabel, DashboardStatusKind StatusKind) GetMidsEnvironmentDisplay()
    {
        var status = _midsInstallationService.GetStatus();

        if (status.MultipleInstallAmbiguity)
        {
            return ("Multiple installations found", "NOT FOUND", DashboardStatusKind.Warning);
        }

        if (status.IsValid && !string.IsNullOrWhiteSpace(status.InstallPath))
        {
            return (status.InstallPath, "VALID", DashboardStatusKind.Success);
        }

        return ("Mids Reborn installation not detected", "NOT FOUND", DashboardStatusKind.Warning);
    }

    private EnvironmentTileViewModel CreateEnhancementDatabaseEnvironmentTile()
    {
        var (primaryText, statusLabel, statusKind) = GetEnhancementDatabaseEnvironmentDisplay();
        return new EnvironmentTileViewModel(
            "Enhancement Database (EnhDB)",
            primaryText,
            statusLabel,
            "Assets/Images/Environment/enhancement-database.png",
            iconRenderSize: 56,
            statusKind: statusKind);
    }

    private (string PrimaryText, string StatusLabel, DashboardStatusKind StatusKind) GetEnhancementDatabaseEnvironmentDisplay()
    {
        var status = _midsInstallationService.GetStatus();

        if (status.IsValid && !string.IsNullOrWhiteSpace(status.HomecomingDatabaseVersion))
        {
            return ($"Homecoming DB {status.HomecomingDatabaseVersion}", "VALID", DashboardStatusKind.Success);
        }

        return ("Homecoming database not detected", "NOT FOUND", DashboardStatusKind.Warning);
    }

    private static EnvironmentTileViewModel CreateBuildLibraryEnvironmentTile()
    {
        return new EnvironmentTileViewModel(
            "Build Library (Workspace)",
            "Build library location not configured",
            "NOT CONFIGURED",
            "Assets/Images/Environment/build-library.png",
            iconRenderSize: 56,
            statusKind: DashboardStatusKind.Warning);
    }
}

public sealed class DashboardActivityItemViewModel(ApplicationActivityEntry entry)
{
    public string TimestampLabel { get; } = entry.Timestamp.ToLocalTime().ToString("HH:mm");

    public string EventType { get; } = entry.EventType;

    public string DisplayMessage { get; } = entry.DisplayMessage;
}

public sealed class DashboardLocationViewModel(string title, string displayPath)
{
    public string Title { get; } = title;

    public string DisplayPath { get; } = displayPath;
}

public enum GameStatusState
{
    Off,
    Idle,
    Running,
    Error
}

public enum DashboardStatusKind
{
    Success,
    Warning,
    Error,
    Information
}

public sealed class AccountCardViewModel
{
    public required string StableId { get; init; }

    public required string Name { get; init; }

    public required string StatusLabel { get; init; }

    public required DashboardStatusKind StatusKind { get; init; }

    public required string Detail { get; init; }

    public bool IsHighlighted { get; init; }
}

public sealed class MonitoringTileViewModel(
    string title,
    string value,
    string? vectorIconPath = null,
    string? subtitle = null,
    string? rasterIconPath = null)
{
    public string Title { get; } = title;

    public string Value { get; } = value;

    public string? VectorIconPath { get; } = vectorIconPath;

    public string? RasterIconPath { get; } = rasterIconPath;

    public string? Subtitle { get; } = subtitle;
}

public sealed class EnvironmentTileViewModel(
    string title,
    string primaryText,
    string statusLabel,
    string iconPath,
    double iconRenderSize,
    DashboardStatusKind statusKind = DashboardStatusKind.Success)
{
    public string Title { get; } = title;

    public string PrimaryText { get; } = primaryText;

    public string StatusLabel { get; } = statusLabel;

    public string IconPath { get; } = iconPath;

    public double IconRenderSize { get; } = iconRenderSize;

    public DashboardStatusKind StatusKind { get; } = statusKind;

    public bool HasStatus => !string.IsNullOrWhiteSpace(StatusLabel);
}
