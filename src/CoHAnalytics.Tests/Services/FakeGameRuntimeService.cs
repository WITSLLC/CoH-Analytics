using CoHAnalytics.Models;
using CoHAnalytics.Services;
using CoHAnalytics.Services.Diagnostics;

namespace CoHAnalytics.Tests.Services;

/// <summary>
/// Deterministic <see cref="IGameRuntimeService"/> double shared across the monitoring session
/// manager tests, so runtime transitions can be raised directly without a real Homecoming
/// process or launcher.
/// </summary>
internal sealed class FakeGameRuntimeService : IGameRuntimeService
{
    private static readonly IReadOnlyList<HomecomingProcessInstance> EmptyRunningClients =
        Array.Empty<HomecomingProcessInstance>();

    public GameRuntimeStatus CurrentStatus { get; set; } = GameRuntimeStatus.Unconfigured;

    public int RunningClientCount { get; set; }

    public IReadOnlyList<HomecomingProcessInstance> RunningClients { get; set; } = EmptyRunningClients;

    public string? LastErrorMessage { get; set; }

    public event EventHandler<GameRuntimeStatusChangedEventArgs>? StatusChanged;

    public void RaiseStatusChanged(
        GameRuntimeStatus previous,
        GameRuntimeStatus next,
        int runningClientCount = 0)
    {
        RaiseStatusChanged(previous, next, CreateUniformClients(runningClientCount));
    }

    public void RaiseStatusChanged(
        GameRuntimeStatus previous,
        GameRuntimeStatus next,
        IReadOnlyList<HomecomingProcessInstance> runningClients)
    {
        var previousClients = RunningClients;
        var previousCount = RunningClientCount;
        CurrentStatus = next;
        RunningClients = OrderClients(runningClients);
        RunningClientCount = RunningClients.Count;
        DiagnosticEventSubscriberDispatch.InvokeOrdered(
            StatusChanged,
            this,
            new GameRuntimeStatusChangedEventArgs(
                previous,
                next,
                RunningClientCount,
                previousCount,
                RunningClients,
                previousClients));
    }

    public static HomecomingProcessInstance CreateClient(
        int processId,
        DateTimeOffset processStartTime,
        string executablePath = @"C:\Games\Homecoming\homecoming\bin\win64\cityofheroes.exe") =>
        new()
        {
            ProcessId = processId,
            ProcessStartTime = processStartTime,
            ExecutablePath = executablePath
        };

    public static IReadOnlyList<HomecomingProcessInstance> CreateUniformClients(int count)
    {
        if (count <= 0)
        {
            return EmptyRunningClients;
        }

        var clients = new HomecomingProcessInstance[count];
        for (var index = 0; index < count; index++)
        {
            clients[index] = CreateClient(
                3_000 + index,
                new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero).AddMinutes(index));
        }

        return clients;
    }

    private static IReadOnlyList<HomecomingProcessInstance> OrderClients(
        IReadOnlyList<HomecomingProcessInstance> clients) =>
        clients.OrderBy(client => client.ProcessId).ToArray();

    public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task LaunchAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public void Start()
    {
    }

    public void Stop()
    {
    }

    public void Dispose()
    {
    }
}
