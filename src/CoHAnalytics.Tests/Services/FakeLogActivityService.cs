using CoHAnalytics.Models;
using CoHAnalytics.Services;
using CoHAnalytics.Services.Diagnostics;

namespace CoHAnalytics.Tests.Services;

/// <summary>
/// A test double for <see cref="ILogActivityService"/>, allowing a test to inject a controlled
/// <see cref="LogActivitySnapshot"/> and raise <see cref="ActivityChanged"/> deterministically,
/// without any real filesystem observation.
/// </summary>
internal sealed class FakeLogActivityService : ILogActivityService
{
    public LogActivitySnapshot Current { get; set; } = LogActivitySnapshot.Empty;

    public bool IsRunning { get; set; }

    public string? LastScanFailureMessage { get; set; }

    public event EventHandler<LogActivityChangedEventArgs>? ActivityChanged;

    public void RaiseActivityChanged() =>
        DiagnosticEventSubscriberDispatch.InvokeOrdered(
            ActivityChanged,
            this,
            new LogActivityChangedEventArgs(Current));


    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        IsRunning = true;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        IsRunning = false;
        return Task.CompletedTask;
    }

    public Task<LogActivitySnapshot> ScanAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Current);

    public LogActivityDiagnostics GetDiagnostics() =>
        new()
        {
            PollInterval = TimeSpan.FromSeconds(1),
            InactivityThreshold = TimeSpan.FromSeconds(30),
            IsPollingEnabled = false,
            IsRunning = IsRunning,
            ScanCount = 0,
            LastSnapshotRevision = Current.Revision,
            SuppressedNoOpScanCount = 0,
            Accounts = [],
            Sources = [],
            RecentScanFailures = []
        };
}
