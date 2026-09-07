using CoHAnalytics.Models;
using CoHAnalytics.Services.Diagnostics;

namespace CoHAnalytics.Services;

public sealed class HomecomingRuntimeService : IGameRuntimeService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private static readonly IReadOnlyList<HomecomingProcessInstance> EmptyRunningClients =
        Array.Empty<HomecomingProcessInstance>();

    private readonly HomecomingInstallationService _installationService;
    private readonly HomecomingLauncherService _launcherService;
    private readonly IDiagnosticLog? _diagnosticLog;
    private readonly Func<LogActivitySnapshot>? _logActivitySnapshotProvider;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly object _stateSync = new();

    private Timer? _pollTimer;
    private bool _disposed;
    private GameRuntimeStatus _currentStatus = GameRuntimeStatus.Unconfigured;
    private int _runningClientCount;
    private IReadOnlyList<HomecomingProcessInstance> _runningClients = EmptyRunningClients;

    public HomecomingRuntimeService(
        HomecomingInstallationService installationService,
        HomecomingLauncherService launcherService,
        IDiagnosticLog? diagnosticLog = null,
        Func<LogActivitySnapshot>? logActivitySnapshotProvider = null)
    {
        _installationService = installationService;
        _launcherService = launcherService;
        _diagnosticLog = diagnosticLog;
        _logActivitySnapshotProvider = logActivitySnapshotProvider;
    }

    public GameRuntimeStatus CurrentStatus
    {
        get
        {
            lock (_stateSync)
            {
                return _currentStatus;
            }
        }
    }

    public int RunningClientCount
    {
        get
        {
            lock (_stateSync)
            {
                return _runningClientCount;
            }
        }
    }

    public IReadOnlyList<HomecomingProcessInstance> RunningClients
    {
        get
        {
            lock (_stateSync)
            {
                return _runningClients;
            }
        }
    }

    public string? LastErrorMessage { get; private set; }

    public event EventHandler<GameRuntimeStatusChangedEventArgs>? StatusChanged;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _pollTimer ??= new Timer(
            PollCallback,
            null,
            TimeSpan.Zero,
            PollInterval);
    }

    public void Stop()
    {
        if (_pollTimer is null)
        {
            return;
        }

        _pollTimer.Change(Timeout.Infinite, Timeout.Infinite);
        _pollTimer.Dispose();
        _pollTimer = null;
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!await _refreshGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var installation = _installationService.CurrentInstallation
                               ?? _installationService.DiscoverAndPersist();

            GameRuntimeStatus nextStatus;
            IReadOnlyList<HomecomingProcessInstance> nextClients;

            if (installation is null)
            {
                nextStatus = GameRuntimeStatus.Unconfigured;
                nextClients = EmptyRunningClients;
            }
            else
            {
                try
                {
                    var clients = await Task.Run(
                        () => HomecomingClientDetector.DetectRunningClients(installation),
                        cancellationToken).ConfigureAwait(false);

                    nextClients = HomecomingClientDetector.ToProcessInstances(clients);
                    nextStatus = nextClients.Count > 0 ? GameRuntimeStatus.Running : GameRuntimeStatus.Off;
                }
                catch
                {
                    nextStatus = GameRuntimeStatus.Error;
                    nextClients = EmptyRunningClients;
                }
            }

            ApplyStatus(nextStatus, nextClients);
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    public async Task LaunchAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var errorMessage = await _launcherService.LaunchAsync(cancellationToken).ConfigureAwait(false);
        LastErrorMessage = errorMessage;

        if (!string.IsNullOrWhiteSpace(errorMessage))
        {
            if (_installationService.CurrentInstallation is null)
            {
                ApplyStatus(GameRuntimeStatus.Unconfigured, EmptyRunningClients);
            }
            else
            {
                ApplyStatus(GameRuntimeStatus.Error, RunningClients);
            }
        }

        await RefreshAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
        _refreshGate.Dispose();
    }

    private void PollCallback(object? state)
    {
        if (_disposed)
        {
            return;
        }

        _ = RefreshAsync();
    }

    internal void ApplyStatus(
        GameRuntimeStatus nextStatus,
        IReadOnlyList<HomecomingProcessInstance> nextClients)
    {
        if (_disposed)
        {
            return;
        }

        GameRuntimeStatus previousStatus;
        int previousCount;
        IReadOnlyList<HomecomingProcessInstance> previousClients;

        lock (_stateSync)
        {
            previousStatus = _currentStatus;
            previousCount = _runningClientCount;
            previousClients = _runningClients;
            _currentStatus = nextStatus;
            _runningClientCount = nextClients.Count;
            _runningClients = nextClients;
        }

        var statusChanged = previousStatus != nextStatus;
        var clientsChanged = previousCount != nextClients.Count
            || !HomecomingProcessInstance.SequenceEqualByIdentity(previousClients, nextClients);
        if (!statusChanged && !clientsChanged)
        {
            return;
        }

        if (clientsChanged)
        {
            WriteDiagnostic(new RuntimeClientSetChangedDiagnosticEvent
            {
                PreviousStatus = previousStatus,
                NextStatus = nextStatus,
                PreviousClients = ToDiagnosticClients(previousClients),
                NextClients = ToDiagnosticClients(nextClients)
            });
        }

        if (statusChanged)
        {
            WriteDiagnostic(new RuntimeStatusChangedDiagnosticEvent
            {
                PreviousStatus = previousStatus,
                NextStatus = nextStatus
            });
        }

        if (clientsChanged)
        {
            WriteDiagnostic(CreateStateSnapshot(nextClients));
        }

        StatusChanged?.Invoke(
            this,
            new GameRuntimeStatusChangedEventArgs(
                previousStatus,
                nextStatus,
                nextClients.Count,
                previousCount,
                nextClients,
                previousClients));
    }

    private DiagnosticsStateSnapshotCapturedDiagnosticEvent CreateStateSnapshot(
        IReadOnlyList<HomecomingProcessInstance> runningClients)
    {
        IReadOnlyList<LogSourceCandidate> logSources = [];
        try
        {
            logSources = _logActivitySnapshotProvider?.Invoke().Candidates ?? [];
        }
        catch
        {
            // Snapshot collection is diagnostics-only and cannot affect runtime transitions.
        }

        return new DiagnosticsStateSnapshotCapturedDiagnosticEvent
        {
            RunningClients = ToDiagnosticClients(runningClients),
            LogSources =
            [
                .. logSources.Select(source => new DiagnosticLogSource
                {
                    SourceId = source.SourceId.Value,
                    AccountStableId = source.AccountStableId,
                    SourceFileName = source.FileName,
                    ActivityState = source.ActivityState,
                    Length = source.Length,
                    LastGrowthAt = source.LastGrowthAt?.ToUniversalTime()
                })
            ]
        };
    }

    private static IReadOnlyList<DiagnosticRuntimeClient> ToDiagnosticClients(
        IReadOnlyList<HomecomingProcessInstance> clients) =>
        [
            .. clients.Select(client => new DiagnosticRuntimeClient
            {
                ProcessId = client.ProcessId,
                ProcessStartTime = client.ProcessStartTime.ToUniversalTime()
            })
        ];

    private void WriteDiagnostic(DiagnosticEvent diagnosticEvent)
    {
        try
        {
            _diagnosticLog?.Write(diagnosticEvent);
        }
        catch
        {
            // Diagnostics are observational and must never affect runtime behavior.
        }
    }
}
