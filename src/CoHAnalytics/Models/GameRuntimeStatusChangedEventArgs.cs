namespace CoHAnalytics.Models;

public sealed class GameRuntimeStatusChangedEventArgs : EventArgs
{
    public GameRuntimeStatusChangedEventArgs(
        GameRuntimeStatus previousStatus,
        GameRuntimeStatus newStatus,
        int runningClientCount,
        int previousRunningClientCount,
        IReadOnlyList<HomecomingProcessInstance>? runningClients = null,
        IReadOnlyList<HomecomingProcessInstance>? previousRunningClients = null)
    {
        PreviousStatus = previousStatus;
        NewStatus = newStatus;
        RunningClientCount = runningClientCount;
        PreviousRunningClientCount = previousRunningClientCount;
        RunningClients = runningClients ?? Array.Empty<HomecomingProcessInstance>();
        PreviousRunningClients = previousRunningClients ?? Array.Empty<HomecomingProcessInstance>();
    }

    public GameRuntimeStatus PreviousStatus { get; }

    public GameRuntimeStatus NewStatus { get; }

    public int RunningClientCount { get; }

    public int PreviousRunningClientCount { get; }

    public IReadOnlyList<HomecomingProcessInstance> RunningClients { get; }

    public IReadOnlyList<HomecomingProcessInstance> PreviousRunningClients { get; }
}
