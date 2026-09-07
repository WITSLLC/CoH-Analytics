using CoHAnalytics.Models;
using CoHAnalytics.Replay;

namespace CoHAnalytics.Tests.Replay;

public sealed class ReplayLifecycleTests
{
    [Fact]
    public async Task WaitAsync_completes_when_signaled_condition_becomes_true()
    {
        var signal = new ReplayLifecycleSignal();
        var waiter = new ReplayLifecycleWaiter(TimeProvider.System);
        var refreshObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ready = false;

        var wait = waiter.WaitAsync(
            "test condition",
            () => ready,
            _ =>
            {
                refreshObserved.TrySetResult();
                return Task.CompletedTask;
            },
            () => $"ready={ready}",
            signal,
            TimeSpan.FromMinutes(1),
            CancellationToken.None);

        await refreshObserved.Task;
        Assert.False(wait.IsCompleted);

        ready = true;
        signal.Pulse();

        await wait;
    }

    [Fact]
    public async Task WaitAsync_throws_explicit_failure_when_condition_times_out()
    {
        var time = new ManualReplayTimeProvider();
        var waiter = new ReplayLifecycleWaiter(time);

        var exception = await Assert.ThrowsAsync<ReplayLifecycleTimeoutException>(() => waiter.WaitAsync(
            "parser drain",
            () => false,
            _ => Task.CompletedTask,
            () => "processed=2/3, queued=1",
            new ReplayLifecycleSignal(),
            TimeSpan.FromSeconds(7),
            CancellationToken.None));

        Assert.Equal("parser drain", exception.ConditionName);
        Assert.Equal(TimeSpan.FromSeconds(7), exception.Timeout);
        Assert.Equal("processed=2/3, queued=1", exception.LastObservedState);
        Assert.Contains("parser drain", exception.Message, StringComparison.Ordinal);
        Assert.Contains("processed=2/3, queued=1", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Seeded_context_readiness_requires_exact_account_source_and_ready_state()
    {
        var now = new DateTimeOffset(2026, 8, 5, 12, 0, 0, TimeSpan.Zero);
        var contextId = MonitoringContextId.CreateNew();
        var sourceId = LogSourceId.Create(
            "account-01",
            "Replay Account",
            Path.Combine(Path.GetTempPath(), "replay", "chatlog 2026-08-05.txt"),
            new DateOnly(2026, 8, 5));
        var context = new MonitoringContextSnapshot
        {
            ContextId = contextId,
            State = MonitoringContextState.WaitingForSource,
            AccountStableId = null,
            CurrentSourceId = null,
            SourceBindingGeneration = 1,
            CreatedAt = now,
            LastStateChangedAt = now
        };

        Assert.False(IsReady(context));
        Assert.False(IsReady(context with
        {
            State = MonitoringContextState.Ready,
            AccountStableId = "wrong-account",
            CurrentSourceId = sourceId
        }));
        Assert.False(IsReady(context with
        {
            State = MonitoringContextState.Ready,
            AccountStableId = "account-01",
            CurrentSourceId = sourceId.NextGeneration()
        }));
        var readyContext = context with
        {
            State = MonitoringContextState.Ready,
            AccountStableId = "account-01",
            CurrentSourceId = sourceId
        };
        Assert.False(IsReady(readyContext, includeParserWorker: false));
        Assert.True(IsReady(readyContext));

        bool IsReady(MonitoringContextSnapshot candidate, bool includeParserWorker = true)
        {
            var workers = candidate.CurrentSourceId is null || !includeParserWorker
                ? Array.Empty<ParserWorkerSnapshot>()
                :
                [
                    new ParserWorkerSnapshot
                    {
                        WorkerId = ParserWorkerId.CreateNew(),
                        ContextId = contextId,
                        State = ParserWorkerState.WaitingForData,
                        CurrentSourceId = candidate.CurrentSourceId,
                        AppliedSourceBindingGeneration = 1,
                        LastAppliedTransitionKind = MonitoringSourceTransitionKind.SourceAssigned,
                        RecentSegments = [],
                        TotalBytesRead = 0,
                        TotalLinesEmitted = 0,
                        LastEventSequence = 0
                    }
                ];
            return ReplayLifecycleConditions.IsSeededContextReady(
                MonitoringSessionManagerSnapshot.Create([candidate], [], 0, 0, now, 1),
                ParserManagerSnapshot.Create(workers, null, now, 1),
                contextId,
                "account-01",
                sourceId);
        }
    }
}
