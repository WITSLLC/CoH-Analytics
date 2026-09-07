using CoHAnalytics.Observations;
using CoHAnalytics.Tests.Orchestration;
using CoHAnalytics.Tests.Services;
using CoHAnalytics.ViewModels.Workspaces;

namespace CoHAnalytics.Tests.Workspaces;

public sealed class DiagnosticsViewModelTests
{
    [Fact]
    public void Refresh_maps_capture_counters_and_user_facing_status()
    {
        var lastCapture = new DateTimeOffset(2026, 8, 10, 17, 6, 42, TimeSpan.Zero);
        var observation = new FakeAcquisitionObservationService
        {
            Status = new AcquisitionObservationStatus
            {
                Health = AcquisitionCaptureHealth.Active,
                AcquisitionsSeen = 184,
                AcquisitionsResolved = 12,
                AcquisitionsPresentedUnresolved = 4,
                AcquisitionsUnresolved = 7,
                DroppedObservations = 0,
                LastCaptureAtUtc = lastCapture,
                CatalogVersion = "item-ref-2.2.1",
                ObservationsRoot = @"C:\fake\observations"
            }
        };

        using var viewModel = CreateViewModel(observation);

        Assert.Equal("Active", viewModel.CaptureStatusLabel);
        Assert.Equal(DashboardStatusKind.Success, viewModel.CaptureStatusKind);
        Assert.Equal(184, viewModel.AcquisitionsSeen);
        Assert.Equal(12, viewModel.AcquisitionsResolved);
        Assert.Equal(11, viewModel.AcquisitionsUnclassified);
        Assert.Equal(0, viewModel.DroppedObservations);
        Assert.Equal(lastCapture.ToLocalTime().ToString("HH:mm:ss"), viewModel.LastCaptureLabel);
        Assert.Equal("item-ref-2.2.1", viewModel.CatalogVersionLabel);
        Assert.Contains("reference catalog", viewModel.CaptureSummaryDescription, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Degraded_health_maps_to_warning_status_kind()
    {
        var observation = new FakeAcquisitionObservationService
        {
            Status = new AcquisitionObservationStatus
            {
                Health = AcquisitionCaptureHealth.Degraded
            }
        };

        using var viewModel = CreateViewModel(observation);

        Assert.Equal("Degraded", viewModel.CaptureStatusLabel);
        Assert.Equal(DashboardStatusKind.Warning, viewModel.CaptureStatusKind);
    }

    [Fact]
    public void Open_observations_folder_command_is_available()
    {
        using var viewModel = CreateViewModel(new FakeAcquisitionObservationService());

        Assert.True(viewModel.OpenObservationsFolderCommand.CanExecute(null));
    }

    [Fact]
    public void Observation_changed_event_refreshes_counters()
    {
        var observation = new FakeAcquisitionObservationService
        {
            Status = new AcquisitionObservationStatus
            {
                Health = AcquisitionCaptureHealth.Active,
                AcquisitionsSeen = 1
            }
        };

        using var viewModel = CreateViewModel(observation);
        Assert.Equal(1, viewModel.AcquisitionsSeen);

        observation.Status = observation.Status with { AcquisitionsSeen = 9, AcquisitionsUnresolved = 3 };
        observation.RaiseChanged();

        Assert.Equal(9, viewModel.AcquisitionsSeen);
        Assert.Equal(3, viewModel.AcquisitionsUnclassified);
    }

    private static DiagnosticsViewModel CreateViewModel(FakeAcquisitionObservationService observation) =>
        new(
            new TestGameplaySessionContextSupport.FakeApplicationOrchestrator(),
            new FakeGameRuntimeService(),
            observation);

    private sealed class FakeAcquisitionObservationService : IAcquisitionObservationService
    {
        public AcquisitionObservationStatus Status { get; set; } = new();

        public event EventHandler? Changed;

        public AcquisitionObservationStatus GetStatus() => Status;

        public void RecordClassificationAttempt(bool resolved)
        {
        }

        public bool TryRecordUnresolvedAcquisition(AcquisitionObservationRecord observation) => true;

        public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
    }
}
