using CoHAnalytics.Orchestration.Models;

namespace CoHAnalytics.Orchestration.Contracts;

public interface IApplicationOrchestrator
{
    ApplicationStateSnapshot Current { get; }

    event EventHandler<ApplicationStateChangedEventArgs>? SnapshotChanged;

    Task RefreshAsync(string? providerId = null, CancellationToken cancellationToken = default);
}
