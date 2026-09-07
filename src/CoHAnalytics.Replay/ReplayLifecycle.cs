using CoHAnalytics.Models;
using CoHAnalytics.Services;

namespace CoHAnalytics.Replay;

public enum ReplayDrainOutcome
{
    Completed,
    TimedOut,
    Cancelled,
    Faulted
}

public sealed record ReplayDrainDiagnostics(
    bool MonitoringReady,
    long ProcessedLines,
    long ExpectedLines,
    int ParserQueuedEvents,
    bool ParserWorkersReady,
    long RawObserved,
    long ClassifiedObserved,
    long CommittedObserved,
    bool GameplayQuiescent,
    int PendingCommittedEvents,
    int FailedContextCount,
    bool ParserOverflowed,
    bool GameplayOverloaded,
    IReadOnlyList<string> UnsatisfiedStages,
    ReplayDrainTimeoutDiagnostics? TimeoutDetail = null);

/// <summary>Payload-free drain-timeout snapshot for tail-drain disambiguation.</summary>
public sealed record ReplayDrainTimeoutDiagnostics(
    QueuePressureDiagnostics ParserEventQueue,
    QueuePressureDiagnostics ParserMonitoringQueue,
    QueuePressureInvariantClassification ParserEventQueueInvariant,
    bool ParserComplete,
    bool GameplayIsQuiescent,
    bool ClassifiedCaughtUp,
    bool OracleCaughtUp,
    bool OverallDrainComplete,
    long GameplayLastAcceptedWorkSequence,
    long GameplayLastCompletedWorkSequence,
    int GameplayActiveProcessorCallbackCount,
    int GameplayPendingCommittedEventCount,
    QueuePressureDiagnostics GameplayWorkQueue,
    long OracleRawObserved,
    long OracleClassifiedObserved,
    long OracleCommittedObserved,
    int OracleLateObservationCount,
    bool OracleConsumerFault)
{
    public string ToCompactDiagnosticString()
    {
        static string Queue(string label, QueuePressureDiagnostics queue) =>
            $"{label}(epoch={queue.LifecycleEpoch},cap={queue.Capacity},acc={queue.AcceptedCount},"
            + $"cmp={queue.CompletedCount},rej={queue.RejectedCount},abn={queue.AbandonedCount},"
            + $"dep={queue.CurrentDepth},ifl={queue.InFlightCount},pk={queue.PeakDepth},"
            + $"ofl={(queue.Overflowed ? 1 : 0)},drn={(queue.IsDrained ? 1 : 0)})";

        return string.Join(
            ' ',
            [
                $"parser-event-inv={ParserEventQueueInvariant}",
                Queue("parser-event", ParserEventQueue),
                Queue("parser-monitoring", ParserMonitoringQueue),
                Queue("gameplay-work", GameplayWorkQueue),
                $"parser-complete={(ParserComplete ? 1 : 0)}",
                $"gameplay-is-quiescent={(GameplayIsQuiescent ? 1 : 0)}",
                $"classified-caught-up={(ClassifiedCaughtUp ? 1 : 0)}",
                $"oracle-caught-up={(OracleCaughtUp ? 1 : 0)}",
                $"overall-drain-complete={(OverallDrainComplete ? 1 : 0)}",
                $"gameplay(acc={GameplayLastAcceptedWorkSequence},cmp={GameplayLastCompletedWorkSequence},"
                + $"active={GameplayActiveProcessorCallbackCount},pending={GameplayPendingCommittedEventCount})",
                $"oracle(raw={OracleRawObserved},cls={OracleClassifiedObserved},cmt={OracleCommittedObserved},"
                + $"late={OracleLateObservationCount},fault={(OracleConsumerFault ? 1 : 0)})"
            ]);
    }
}

public sealed record ReplayDrainResult(
    ReplayDrainOutcome Outcome,
    ReplayDrainDiagnostics Diagnostics);

internal sealed record ReplayLifecycleTimeouts
{
    public TimeSpan SeededContextReady { get; init; } = TimeSpan.FromSeconds(10);

    public TimeSpan ParserProgress { get; init; } = TimeSpan.FromSeconds(10);

    public TimeSpan InactiveSource { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan Drain { get; init; } = TimeSpan.FromSeconds(30);
}

internal sealed class ReplayLifecycleTimeoutException : TimeoutException
{
    public ReplayLifecycleTimeoutException(
        string conditionName,
        TimeSpan timeout,
        string lastObservedState)
        : base(
            $"Replay lifecycle wait '{conditionName}' timed out after {timeout}. "
            + $"Last observed state: {lastObservedState}")
    {
        ConditionName = conditionName;
        Timeout = timeout;
        LastObservedState = lastObservedState;
    }

    public string ConditionName { get; }

    public TimeSpan Timeout { get; }

    public string LastObservedState { get; }
}

internal sealed class ReplayLifecycleSignal
{
    private readonly object _sync = new();
    private TaskCompletionSource _next = CreateCompletion();

    public Task WaitForChangeAsync()
    {
        lock (_sync)
        {
            return _next.Task;
        }
    }

    public void Pulse()
    {
        TaskCompletionSource completion;
        lock (_sync)
        {
            completion = _next;
            _next = CreateCompletion();
        }

        completion.TrySetResult();
    }

    private static TaskCompletionSource CreateCompletion() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}

internal sealed class ReplayLifecycleWaiter(TimeProvider timeProvider)
{
    private readonly TimeProvider _timeProvider = timeProvider;

    public async Task WaitAsync(
        string conditionName,
        Func<bool> condition,
        Func<CancellationToken, Task> refreshAsync,
        Func<string> describeState,
        ReplayLifecycleSignal signal,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conditionName);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);

        var startedAt = _timeProvider.GetTimestamp();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (condition())
            {
                return;
            }

            var change = signal.WaitForChangeAsync();
            await refreshAsync(cancellationToken).ConfigureAwait(false);
            if (condition())
            {
                return;
            }

            var elapsed = _timeProvider.GetElapsedTime(startedAt);
            var remaining = timeout - elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                throw new ReplayLifecycleTimeoutException(conditionName, timeout, describeState());
            }

            try
            {
                await change.WaitAsync(remaining, _timeProvider, cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                if (condition())
                {
                    return;
                }

                throw new ReplayLifecycleTimeoutException(conditionName, timeout, describeState());
            }
        }
    }
}

internal static class ReplayLifecycleConditions
{
    public static bool IsSeededContextReady(
        MonitoringSessionManagerSnapshot snapshot,
        ParserManagerSnapshot parserSnapshot,
        MonitoringContextId contextId,
        string expectedAccountStableId,
        LogSourceId expectedSourceId)
    {
        var context = snapshot.Contexts.SingleOrDefault(candidate => candidate.ContextId == contextId);
        var worker = parserSnapshot.Workers.SingleOrDefault(candidate => candidate.ContextId == contextId);
        return context is
        {
            State: MonitoringContextState.Ready,
            CurrentSourceId: not null
        }
            && string.Equals(
                context.AccountStableId,
                expectedAccountStableId,
                StringComparison.Ordinal)
            && context.CurrentSourceId == expectedSourceId
            && worker is
            {
                State: ParserWorkerState.WaitingForData or ParserWorkerState.Reading,
                CurrentSourceId: not null
            }
            && worker.CurrentSourceId == expectedSourceId
            && worker.AppliedSourceBindingGeneration >= context.SourceBindingGeneration;
    }
}
