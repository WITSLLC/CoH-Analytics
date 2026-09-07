using System.IO;
using System.Text;
using CoHAnalytics.Models;
using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Services;

internal sealed class FakeMonitoringSessionManager : IMonitoringSessionManager
{
    public MonitoringSessionManagerSnapshot Current { get; private set; } = MonitoringSessionManagerSnapshot.Empty;

    public bool IsRuntimeAvailable { get; set; } = true;

    public int StateChangedSubscriptionCount { get; private set; }

    private event EventHandler<MonitoringSessionManagerChangedEventArgs>? StateChangedCore;

    public event EventHandler<MonitoringSessionManagerChangedEventArgs>? StateChanged
    {
        add
        {
            StateChangedSubscriptionCount++;
            StateChangedCore += value;
        }
        remove
        {
            StateChangedSubscriptionCount--;
            StateChangedCore -= value;
        }
    }

    public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public void SetInitial(MonitoringSessionManagerSnapshot snapshot) => Current = snapshot;

    public void Publish(MonitoringSessionManagerSnapshot snapshot)
    {
        Current = snapshot;
        StateChangedCore?.Invoke(this, new MonitoringSessionManagerChangedEventArgs(snapshot));
    }

    public MonitoringSourceClaimResult ClaimSource(MonitoringContextId contextId, LogSourceId sourceId) =>
        throw new NotSupportedException();

    public MonitoringSourceClaimResult ReleaseSource(MonitoringContextId contextId) =>
        throw new NotSupportedException();

    public MonitoringSourceClaimResult RemoveContext(MonitoringContextId contextId) =>
        throw new NotSupportedException();

    public MonitoringSourceDecision AcceptOffer(MonitoringSourceOfferId offerId) =>
        throw new NotSupportedException();

    public MonitoringSourceDecision DeclineOffer(MonitoringSourceOfferId offerId) =>
        throw new NotSupportedException();

    public MonitoringSourceDecision ReconsiderSource(LogSourceId sourceId) =>
        throw new NotSupportedException();

    public void ResetForNewRuntimeGeneration(bool observedZeroClientCountAfterNonZero = true)
    {
        Current = MonitoringSessionManagerSnapshot.Empty;
        StateChangedCore?.Invoke(this, new MonitoringSessionManagerChangedEventArgs(Current));
    }
}

internal sealed class ParserTestDirectory : IDisposable
{
    public ParserTestDirectory()
    {
        Root = Path.Combine(Path.GetTempPath(), "coh-analytics-parser", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public string CreateFile(string fileName = "chatlog 2026-08-04.txt", byte[]? content = null)
    {
        var path = Path.Combine(Root, fileName);
        File.WriteAllBytes(path, content ?? []);
        return path;
    }

    public void Append(string path, string content) => Append(path, Encoding.UTF8.GetBytes(content));

    public void Append(string path, byte[] content)
    {
        using var stream = new FileStream(
            path,
            FileMode.Append,
            FileAccess.Write,
            FileShare.ReadWrite | FileShare.Delete);
        stream.Write(content);
        stream.Flush(true);
    }

    public void Replace(string path, byte[] content)
    {
        File.Delete(path);
        File.WriteAllBytes(path, content);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch
        {
        }
    }
}

internal static class ParserTestSnapshots
{
    public static LogSourceId Source(
        string path,
        string accountId = "acct-1",
        int identityGeneration = 0,
        DateOnly? logDate = null) =>
        LogSourceId.Create(
            accountId,
            accountId,
            path,
            logDate ?? new DateOnly(2026, 8, 4),
            identityGeneration);

    public static MonitoringContextSnapshot Context(
        MonitoringContextId contextId,
        MonitoringContextState state,
        LogSourceId? source,
        long bindingGeneration,
        MonitoringSourceTransitionKind transition,
        LogSourceId? previousSource = null,
        long startupRecoveryStartOffset = 0,
        LogSourceId? startupRecoveryPredecessorSource = null,
        long startupRecoveryPredecessorStartOffset = 0,
        HomecomingProcessInstance? processInstance = null) =>
        new()
        {
            ContextId = contextId,
            State = state,
            AccountStableId = source?.AccountStableId,
            AccountDisplayName = source?.AccountDisplayName,
            CurrentSourceId = source,
            PreviousSourceId = previousSource,
            SourceBindingGeneration = bindingGeneration,
            LastSourceBindingTransitionKind = transition,
            CreatedAt = DateTimeOffset.UnixEpoch,
            LastStateChangedAt = DateTimeOffset.UnixEpoch,
            SourceAssignedAt = source is null ? null : DateTimeOffset.UnixEpoch,
            StartupRecoveryStartOffset = startupRecoveryStartOffset,
            StartupRecoveryPredecessorSourceId = startupRecoveryPredecessorSource,
            StartupRecoveryPredecessorStartOffset = startupRecoveryPredecessorStartOffset,
            ProcessInstance = processInstance,
            LastSourceTransitionAt = DateTimeOffset.UnixEpoch
        };

    public static MonitoringSessionManagerSnapshot Snapshot(
        long revision,
        params MonitoringContextSnapshot[] contexts) =>
        MonitoringSessionManagerSnapshot.Create(
            contexts,
            [],
            0,
            0,
            DateTimeOffset.UnixEpoch.AddSeconds(revision),
            revision);

    public static ParserManagerOptions FastOptions(
        int readBufferSize = 16,
        int maximumLineBytes = 1024,
        int eventQueueCapacity = 128,
        int eventBatchSize = 32,
        int monitoringSnapshotQueueCapacity = 64) =>
        new()
        {
            PollInterval = TimeSpan.FromMilliseconds(10),
            ReadBufferSize = readBufferSize,
            MaximumLineBytes = maximumLineBytes,
            EventQueueCapacity = eventQueueCapacity,
            EventBatchSize = eventBatchSize,
            MonitoringSnapshotQueueCapacity = monitoringSnapshotQueueCapacity,
            MaximumRecentSegments = 8
        };

    public static async Task WaitUntilAsync(Func<bool> condition, int timeoutMilliseconds = 3000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException("The parser test condition was not reached.");
            }

            await Task.Delay(10);
        }
    }
}
