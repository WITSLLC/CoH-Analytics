using System.Diagnostics;
using CoHAnalytics.Observations;
using CoHAnalytics.Orchestration.Contracts;
using CoHAnalytics.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CoHAnalytics.ViewModels.Workspaces;

public sealed partial class DiagnosticsViewModel : WorkspaceEnvironmentStatusViewModelBase, IDisposable
{
    private readonly IAcquisitionObservationService _observationService;
    private bool _disposed;

    public DiagnosticsViewModel(
        IApplicationOrchestrator orchestrator,
        IGameRuntimeService gameRuntimeService,
        IAcquisitionObservationService observationService)
        : base(orchestrator, gameRuntimeService)
    {
        _observationService = observationService;
        Title = "Diagnostics";
        Description = "Acquisition capture, parser, and monitoring health (read-only).";
        WireEnvironmentStatus();
        _observationService.Changed += OnObservationChanged;
        RefreshCaptureStatus();
    }

    public override string Title { get; }

    public string Description { get; }

    public string CaptureSummaryDescription { get; } =
        "Captures item and reward receipts and surfaces anything the reference catalog cannot identify.";

    [ObservableProperty]
    private string _captureStatusLabel = "Unavailable";

    [ObservableProperty]
    private DashboardStatusKind _captureStatusKind = DashboardStatusKind.Information;

    [ObservableProperty]
    private long _acquisitionsSeen;

    [ObservableProperty]
    private long _acquisitionsResolved;

    [ObservableProperty]
    private long _acquisitionsUnclassified;

    [ObservableProperty]
    private long _droppedObservations;

    [ObservableProperty]
    private string _lastCaptureLabel = "—";

    [ObservableProperty]
    private string _catalogVersionLabel = "—";

    [RelayCommand]
    private void OpenObservationsFolder()
    {
        var path = _observationService.GetStatus().ObservationsRoot;
        TryOpenFolder(path);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _observationService.Changed -= OnObservationChanged;
        UnwireEnvironmentStatus();
    }

    private void OnObservationChanged(object? sender, EventArgs e) => RefreshCaptureStatus();

    private void RefreshCaptureStatus()
    {
        var status = _observationService.GetStatus();
        CaptureStatusLabel = status.Health switch
        {
            AcquisitionCaptureHealth.Active => "Active",
            AcquisitionCaptureHealth.Degraded => "Degraded",
            _ => "Unavailable"
        };
        CaptureStatusKind = status.Health switch
        {
            AcquisitionCaptureHealth.Active => DashboardStatusKind.Success,
            AcquisitionCaptureHealth.Degraded => DashboardStatusKind.Warning,
            _ => DashboardStatusKind.Information
        };

        AcquisitionsSeen = status.AcquisitionsSeen;
        AcquisitionsResolved = status.AcquisitionsResolved;
        AcquisitionsUnclassified = status.AcquisitionsNeedingClassification;
        DroppedObservations = status.DroppedObservations;
        LastCaptureLabel = status.LastCaptureAtUtc?.ToLocalTime().ToString("HH:mm:ss") ?? "—";
        CatalogVersionLabel = status.CatalogVersion ?? "—";
    }

    private static void TryOpenFolder(string path)
    {
        try
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch
        {
        }
    }
}
