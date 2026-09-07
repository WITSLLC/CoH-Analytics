namespace CoHAnalytics.Orchestration.Contracts;

public interface IApplicationContributorLifecycle
{
    Task StartAsync(CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);
}
