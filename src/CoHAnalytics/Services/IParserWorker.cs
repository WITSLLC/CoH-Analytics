using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

/// <summary>Owns incremental parser I/O for exactly one monitoring context.</summary>
public interface IParserWorker : IAsyncDisposable
{
    ParserWorkerId WorkerId { get; }

    MonitoringContextId ContextId { get; }

    ParserWorkerSnapshot Current { get; }

    event EventHandler<ParserWorkerChangedEventArgs>? StateChanged;

    event EventHandler<ParserRawEventAvailableEventArgs>? RawEventAvailable;

    Task StartAsync(CancellationToken cancellationToken = default);

    Task ApplyContextAsync(MonitoringContextSnapshot context, CancellationToken cancellationToken = default);

    Task FaultAsync(string code, string message, CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);
}
