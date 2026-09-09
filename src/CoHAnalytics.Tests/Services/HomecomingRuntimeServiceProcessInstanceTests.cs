using System.Diagnostics;
using CoHAnalytics.Models;
using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Services;

public sealed class HomecomingRuntimeServiceProcessInstanceTests : IDisposable
{
    private static readonly DateTimeOffset StartA = new(2026, 8, 14, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset StartB = new(2026, 8, 14, 11, 0, 0, TimeSpan.Zero);

    private readonly string _installRoot;

    public HomecomingRuntimeServiceProcessInstanceTests()
    {
        _installRoot = HomecomingRuntimeTestSupport.CreateInstallRoot();
    }

    public void Dispose()
    {
        Directory.Delete(_installRoot, recursive: true);
    }

    [Fact]
    public async Task RefreshAsync_exposes_one_process_instance()
    {
        var clientPath = HomecomingRuntimeTestSupport.ClientExecutablePath(_installRoot);
        HomecomingRuntimeTestSupport.WriteProcessJson(_installRoot, (Environment.ProcessId, clientPath));

        using var currentProcess = Process.GetCurrentProcess();
        var expectedStartTime = new DateTimeOffset(currentProcess.StartTime);
        var service = CreateService();

        await service.RefreshAsync();

        Assert.Equal(1, service.RunningClientCount);
        Assert.Equal(service.RunningClientCount, service.RunningClients.Count);
        var client = Assert.Single(service.RunningClients);
        Assert.Equal(Environment.ProcessId, client.ProcessId);
        Assert.Equal(clientPath, client.ExecutablePath);
        Assert.Equal(expectedStartTime, client.ProcessStartTime);
    }

    [Fact]
    public async Task RefreshAsync_exposes_two_simultaneous_process_instances()
    {
        var aliveProcessIds = HomecomingRuntimeTestSupport.GetAliveProcessIds(2);
        var clientPath = HomecomingRuntimeTestSupport.ClientExecutablePath(_installRoot);
        HomecomingRuntimeTestSupport.WriteProcessJson(
            _installRoot,
            (aliveProcessIds[0], clientPath),
            (aliveProcessIds[1], clientPath));

        var service = CreateService();

        await service.RefreshAsync();

        Assert.Equal(2, service.RunningClientCount);
        Assert.Equal(service.RunningClientCount, service.RunningClients.Count);
        Assert.Equal(
            service.RunningClients.Select(client => client.ProcessId),
            service.RunningClients.Select(client => client.ProcessId).OrderBy(processId => processId));
        Assert.Equal(2, service.RunningClients.Select(client => client.ProcessStartTime).Distinct().Count());
    }

    [Fact]
    public async Task RefreshAsync_orders_running_clients_by_process_id()
    {
        var aliveProcessIds = HomecomingRuntimeTestSupport.GetAliveProcessIds(2);
        var clientPath = HomecomingRuntimeTestSupport.ClientExecutablePath(_installRoot);
        HomecomingRuntimeTestSupport.WriteProcessJson(
            _installRoot,
            (aliveProcessIds[1], clientPath),
            (aliveProcessIds[0], clientPath));

        var service = CreateService();

        await service.RefreshAsync();

        Assert.True(service.RunningClients[0].ProcessId < service.RunningClients[1].ProcessId);
    }

    [Fact]
    public async Task Count_transition_one_to_zero_preserves_off_status_semantics()
    {
        var clientPath = HomecomingRuntimeTestSupport.ClientExecutablePath(_installRoot);
        HomecomingRuntimeTestSupport.WriteProcessJson(_installRoot, (Environment.ProcessId, clientPath));
        var service = CreateService();
        await service.RefreshAsync();
        Assert.Equal(GameRuntimeStatus.Running, service.CurrentStatus);

        File.Delete(Path.Combine(_installRoot, "settings", "launcher", "process.json"));
        await service.RefreshAsync();

        Assert.Equal(GameRuntimeStatus.Off, service.CurrentStatus);
        Assert.Equal(0, service.RunningClientCount);
        Assert.Empty(service.RunningClients);
    }

    [Fact]
    public async Task Count_transition_zero_to_one_preserves_running_status_semantics()
    {
        var clientPath = HomecomingRuntimeTestSupport.ClientExecutablePath(_installRoot);
        var service = CreateService();
        await service.RefreshAsync();
        Assert.Equal(GameRuntimeStatus.Off, service.CurrentStatus);

        HomecomingRuntimeTestSupport.WriteProcessJson(_installRoot, (Environment.ProcessId, clientPath));
        await service.RefreshAsync();

        Assert.Equal(GameRuntimeStatus.Running, service.CurrentStatus);
        Assert.Single(service.RunningClients);
    }

    [Fact]
    public void Count_one_to_one_process_replacement_emits_observable_runtime_change()
    {
        var runtime = new FakeGameRuntimeService { CurrentStatus = GameRuntimeStatus.Running, RunningClientCount = 1 };
        GameRuntimeStatusChangedEventArgs? observed = null;
        runtime.StatusChanged += (_, args) => observed = args;

        var previousClient = FakeGameRuntimeService.CreateClient(3_524, StartA);
        var nextClient = FakeGameRuntimeService.CreateClient(6_356, StartB);
        runtime.RunningClients = [previousClient];

        runtime.RaiseStatusChanged(
            GameRuntimeStatus.Running,
            GameRuntimeStatus.Running,
            [nextClient]);

        Assert.NotNull(observed);
        Assert.Equal(1, observed!.PreviousRunningClientCount);
        Assert.Equal(1, observed.RunningClientCount);
        Assert.Equal([previousClient], observed.PreviousRunningClients);
        Assert.Equal([nextClient], observed.RunningClients);
    }

    [Fact]
    public void Count_transition_one_to_two_emits_expected_snapshot()
    {
        var runtime = new FakeGameRuntimeService { CurrentStatus = GameRuntimeStatus.Running, RunningClientCount = 1 };
        GameRuntimeStatusChangedEventArgs? observed = null;
        runtime.StatusChanged += (_, args) => observed = args;
        runtime.RunningClients =
        [
            FakeGameRuntimeService.CreateClient(3_524, StartA)
        ];

        runtime.RaiseStatusChanged(
            GameRuntimeStatus.Running,
            GameRuntimeStatus.Running,
            [
                FakeGameRuntimeService.CreateClient(3_524, StartA),
                FakeGameRuntimeService.CreateClient(6_356, StartB)
            ]);

        Assert.NotNull(observed);
        Assert.Equal(1, observed!.PreviousRunningClientCount);
        Assert.Equal(2, observed.RunningClientCount);
        Assert.Equal(2, observed.RunningClients.Count);
    }

    [Fact]
    public void Count_transition_two_to_one_emits_expected_snapshot()
    {
        var runtime = new FakeGameRuntimeService { CurrentStatus = GameRuntimeStatus.Running, RunningClientCount = 2 };
        GameRuntimeStatusChangedEventArgs? observed = null;
        runtime.StatusChanged += (_, args) => observed = args;
        runtime.RunningClients =
        [
            FakeGameRuntimeService.CreateClient(3_524, StartA),
            FakeGameRuntimeService.CreateClient(6_356, StartB)
        ];

        runtime.RaiseStatusChanged(
            GameRuntimeStatus.Running,
            GameRuntimeStatus.Running,
            [FakeGameRuntimeService.CreateClient(3_524, StartA)]);

        Assert.NotNull(observed);
        Assert.Equal(2, observed!.PreviousRunningClientCount);
        Assert.Equal(1, observed.RunningClientCount);
        Assert.Single(observed.RunningClients);
    }

    [Fact]
    public async Task Process_replacement_with_unchanged_count_emits_status_changed_event()
    {
        var aliveProcessIds = HomecomingRuntimeTestSupport.GetAliveProcessIds(2);
        var clientPath = HomecomingRuntimeTestSupport.ClientExecutablePath(_installRoot);
        HomecomingRuntimeTestSupport.WriteProcessJson(_installRoot, (aliveProcessIds[0], clientPath));

        var service = CreateService();
        GameRuntimeStatusChangedEventArgs? observed = null;
        service.StatusChanged += (_, args) => observed = args;

        await service.RefreshAsync();
        observed = null;

        HomecomingRuntimeTestSupport.WriteProcessJson(_installRoot, (aliveProcessIds[1], clientPath));
        await service.RefreshAsync();

        Assert.NotNull(observed);
        Assert.Equal(GameRuntimeStatus.Running, observed!.PreviousStatus);
        Assert.Equal(GameRuntimeStatus.Running, observed.NewStatus);
        Assert.Equal(1, observed.PreviousRunningClientCount);
        Assert.Equal(1, observed.RunningClientCount);
        Assert.NotEqual(
            observed.PreviousRunningClients.Single().ProcessId,
            observed.RunningClients.Single().ProcessId);
    }

    private HomecomingRuntimeService CreateService()
    {
        var settings = new SettingsService();
        var installationService = new HomecomingInstallationService(settings);
        HomecomingRuntimeTestSupport.SetCurrentInstallation(
            installationService,
            new HomecomingInstallation
            {
                InstallRoot = _installRoot,
                LauncherPath = Path.Combine(_installRoot, "Launcher.exe")
            });

        return new HomecomingRuntimeService(
            installationService,
            new HomecomingLauncherService(settings, installationService));
    }
}
