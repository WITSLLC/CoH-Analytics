using System.Windows;
using CoHAnalytics.Models;
using CoHAnalytics.Orchestration.Contracts;
using CoHAnalytics.Orchestration.Models;
using CoHAnalytics.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CoHAnalytics.ViewModels.Workspaces;

/// <summary>
/// Shared Homecoming runtime and application status presentation for non-Dashboard workspaces.
/// </summary>
public abstract partial class WorkspaceEnvironmentStatusViewModelBase : WorkspaceViewModelBase
{
    private readonly IApplicationOrchestrator _orchestrator;
    private readonly IGameRuntimeService _gameRuntimeService;
    private GameRuntimeStatus _runtimeStatus = GameRuntimeStatus.Unconfigured;

    protected WorkspaceEnvironmentStatusViewModelBase(
        IApplicationOrchestrator orchestrator,
        IGameRuntimeService gameRuntimeService)
    {
        _orchestrator = orchestrator;
        _gameRuntimeService = gameRuntimeService;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GameStatusLabel))]
    [NotifyPropertyChangedFor(nameof(GameStatusToolTip))]
    private GameStatusState _gameStatus = GameStatusState.Off;

    [ObservableProperty]
    private string _appStatusHeadline = "Initializing";

    [ObservableProperty]
    private string _appStatusDetail = "Initializing";

    [ObservableProperty]
    private string _appStatusSecondaryText = "Starting services";

    [ObservableProperty]
    private DashboardStatusKind _appStatusKind = DashboardStatusKind.Information;

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

    protected IGameRuntimeService GameRuntimeService => _gameRuntimeService;

    protected IApplicationOrchestrator Orchestrator => _orchestrator;

    protected void WireEnvironmentStatus()
    {
        _gameRuntimeService.StatusChanged += OnRuntimeStatusChanged;
        _orchestrator.SnapshotChanged += OnOrchestratorSnapshotChanged;
        ApplyEnvironmentFromRuntime(_gameRuntimeService.CurrentStatus);
        ApplyEnvironmentFromSnapshot(_orchestrator.Current);
    }

    protected void UnwireEnvironmentStatus()
    {
        _gameRuntimeService.StatusChanged -= OnRuntimeStatusChanged;
        _orchestrator.SnapshotChanged -= OnOrchestratorSnapshotChanged;
    }

    protected void ApplyEnvironmentFromRuntime(GameRuntimeStatus runtimeStatus)
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
    }

    protected void ApplyEnvironmentFromSnapshot(ApplicationStateSnapshot snapshot)
    {
        var presentation = DashboardApplicationStatusMapper.Map(snapshot);
        AppStatusHeadline = presentation.AppStatusHeadline;
        AppStatusDetail = presentation.AppStatusDetail;
        AppStatusSecondaryText = presentation.AppStatusSecondaryText;
        AppStatusKind = presentation.AppStatusKind;
    }

    [RelayCommand]
    private async Task LaunchGameAsync()
    {
        await _gameRuntimeService.LaunchAsync().ConfigureAwait(true);
        ApplyEnvironmentFromRuntime(_gameRuntimeService.CurrentStatus);
    }

    private void OnRuntimeStatusChanged(object? sender, GameRuntimeStatusChangedEventArgs e) =>
        DispatchEnvironmentRefresh(() => ApplyEnvironmentFromRuntime(e.NewStatus));

    private void OnOrchestratorSnapshotChanged(object? sender, ApplicationStateChangedEventArgs e) =>
        DispatchEnvironmentRefresh(() => ApplyEnvironmentFromSnapshot(e.Snapshot));

    private void DispatchEnvironmentRefresh(Action refreshAction)
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
}
