using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

/// <summary>
/// Observes discovered Homecoming account log directories and reports candidate chat-log
/// sources and their activity.
/// </summary>
/// <remarks>
/// This service reports observations only. It does not parse chat content, read chat lines,
/// identify characters, create or choose monitoring contexts, claim a source for a context,
/// correlate a process to an account, prompt the user, or create parser workers. Deciding
/// what to do with a candidate belongs to the future monitoring session manager.
/// </remarks>
public interface ILogActivityService
{
    /// <summary>The most recently published immutable snapshot.</summary>
    LogActivitySnapshot Current { get; }

    /// <summary>Whether observation is currently started.</summary>
    bool IsRunning { get; }

    /// <summary>
    /// Message describing the most recent whole-scan failure, or <see langword="null"/> when the
    /// latest scan completed. A per-source problem is reported on the candidate instead.
    /// </summary>
    string? LastScanFailureMessage { get; }

    /// <summary>
    /// Raised when the published snapshot became semantically different. Orchestration
    /// consumers must treat this as an invalidation hint and pull <see cref="Current"/>.
    /// </summary>
    event EventHandler<LogActivityChangedEventArgs>? ActivityChanged;

    Task StartAsync(CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs one reconciliation scan. Concurrent requests collapse: at most one scan runs at
    /// a time, and a request arriving during a scan causes exactly one follow-up scan.
    /// </summary>
    Task<LogActivitySnapshot> ScanAsync(CancellationToken cancellationToken = default);

    /// <summary>An in-memory developer-facing observation report. Never contains chat text.</summary>
    LogActivityDiagnostics GetDiagnostics();
}
