using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

public interface IGameRuntimeService : IDisposable
{
    GameRuntimeStatus CurrentStatus { get; }

    int RunningClientCount { get; }

    IReadOnlyList<HomecomingProcessInstance> RunningClients { get; }

    string? LastErrorMessage { get; }

    event EventHandler<GameRuntimeStatusChangedEventArgs>? StatusChanged;

    Task RefreshAsync(CancellationToken cancellationToken = default);

    Task LaunchAsync(CancellationToken cancellationToken = default);

    void Start();

    void Stop();
}
