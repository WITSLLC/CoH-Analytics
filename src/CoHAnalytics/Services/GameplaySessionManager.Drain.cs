using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

public sealed partial class GameplaySessionManager
{
    private readonly HashSet<MonitoringContextId> _drainProcessingFailures = [];

    public async Task<GameplaySessionOperationResult> FinishSessionAsync(
        MonitoringContextId contextId,
        GameplaySessionId expectedSessionId,
        ParserSourcePosition? boundary = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsOnProcessorCallbackStack()) return GameplaySessionOperationResult.Failure(GameplaySessionOutcome.ReentrantCommandRejected);

        var drain = await DrainThroughAsync(contextId, expectedSessionId, boundary, cancellationToken).ConfigureAwait(false);
        if (drain.Outcome != DrainOutcome.Success)
        {
            return GameplaySessionOperationResult.Failure(ToGameplayOutcome(drain.Outcome), drain.Outcome.ToString());
        }

        if (drain.Fence is not { } fence)
        {
            return GameplaySessionOperationResult.Failure(GameplaySessionOutcome.ProcessingFailed);
        }

        var completion = new TaskCompletionSource<GameplaySessionOperationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            if (!TryAdmitWork(WorkItem.FinishSession(contextId, expectedSessionId, fence, completion)))
            {
                await fence.AbortAsync().ConfigureAwait(false);
                return GameplaySessionOperationResult.Failure(GameplaySessionOutcome.Overloaded);
            }

            _options.TestHooks?.AfterCommandAdmission?.Invoke();
            var result = await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            if (result.IsSuccess)
            {
                await fence.ResumeAsync().ConfigureAwait(false);
            }
            else
            {
                await fence.AbortAsync().ConfigureAwait(false);
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            await fence.AbortAsync().ConfigureAwait(false);
            return GameplaySessionOperationResult.Failure(GameplaySessionOutcome.ProcessingFailed, DrainOutcome.Cancelled.ToString());
        }
    }

    public async Task<GameplayDrainResult> DrainThroughAsync(MonitoringContextId contextId,
        GameplaySessionId expectedSessionId, ParserSourcePosition? boundary = null,
        CancellationToken cancellationToken = default)
    {
        if (IsOnProcessorCallbackStack()) return new(DrainOutcome.ProcessingFailed);
        long epoch;
        Task stopped;
        lock (_stateLock)
        {
            if (!_isRunning || _stopInitiated) return new(DrainOutcome.ServiceStopped);
            if (_contexts.GetValueOrDefault(contextId)?.ActiveSession?.SessionId != expectedSessionId)
                return new(DrainOutcome.SessionChanged);
            if (_drainProcessingFailures.Contains(contextId)) return new(DrainOutcome.ProcessingFailed);
            epoch = _lifecycleEpoch;
            stopped = _stopCompletion!.Task;
        }
        using var transportCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var transport = _parserManager.PauseAndDrainThroughAsync(contextId, boundary, transportCancellation.Token);
        if (await Task.WhenAny(transport, stopped).ConfigureAwait(false) == stopped)
            transportCancellation.Cancel();
        var parser = await transport.ConfigureAwait(false);
        if (stopped.IsCompleted)
        {
            if (parser.Fence is not null) await parser.Fence.AbortAsync().ConfigureAwait(false);
            return new(DrainOutcome.ServiceStopped);
        }
        if (parser.Outcome != DrainOutcome.Success) return new(parser.Outcome);
        if (parser.Fence is not { } fence) return new(DrainOutcome.ProcessingFailed);
        var completion = new TaskCompletionSource<DrainOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            TryAdmitWork(new WorkItem
            {
                Kind = WorkItemKind.DrainBoundary, ContextId = contextId, ExpectedSessionId = expectedSessionId,
                DrainFence = fence, DrainEpoch = epoch, DrainCompletion = completion
            });
            var winner = await Task.WhenAny(completion.Task, fence.Invalidated).WaitAsync(cancellationToken).ConfigureAwait(false);
            var outcome = await winner.ConfigureAwait(false);
            if (outcome == DrainOutcome.Success && fence.IsHeld) return new(outcome, fence);
            await fence.AbortAsync().ConfigureAwait(false);
            return new(outcome == DrainOutcome.Success ? DrainOutcome.SourceChanged : outcome);
        }
        catch (OperationCanceledException)
        {
            await fence.AbortAsync().ConfigureAwait(false);
            return new(DrainOutcome.Cancelled);
        }
    }

    private void ProcessDrainBoundary(WorkItem item)
    {
        lock (_stateLock)
        {
            var fence = item.DrainFence!;
            var context = _monitoringSessionManager.Current.Contexts.FirstOrDefault(c => c.ContextId == item.ContextId);
            var session = _contexts.GetValueOrDefault(item.ContextId!)?.ActiveSession;
            var position = fence.Position;
            var outcome = item.DrainEpoch != _lifecycleEpoch || !_isRunning || _stopInitiated ? DrainOutcome.ServiceStopped
                : context is null || context.State == MonitoringContextState.Stopped ? DrainOutcome.ContextGone
                : session is null || session.SessionId != item.ExpectedSessionId ? DrainOutcome.SessionChanged
                : _drainProcessingFailures.Contains(item.ContextId!) ? DrainOutcome.ProcessingFailed
                : !fence.IsHeld || context.CurrentSourceId != position.SourceId
                    || context.SourceBindingGeneration != position.BindingGeneration
                    || (session.CandidateSourceSegmentId is not null && session.CandidateSourceSegmentId != position.SourceSegmentId)
                    ? DrainOutcome.SourceChanged : DrainOutcome.Success;
            item.DrainCompletion!.TrySetResult(outcome);
        }
    }

    private void ProcessFinishSession(WorkItem item)
    {
        lock (_stateLock)
        {
            var fence = item.DrainFence!;
            var contextSnapshot = _monitoringSessionManager.Current.Contexts.FirstOrDefault(c => c.ContextId == item.ContextId);
            var mutableContext = _contexts.GetValueOrDefault(item.ContextId!);
            var session = mutableContext?.ActiveSession;
            var position = fence.Position;
            var outcome = !_isRunning || _stopInitiated ? DrainOutcome.ServiceStopped
                : contextSnapshot is null || contextSnapshot.State == MonitoringContextState.Stopped ? DrainOutcome.ContextGone
                : session is null || session.SessionId != item.ExpectedSessionId ? DrainOutcome.SessionChanged
                : _drainProcessingFailures.Contains(item.ContextId!) ? DrainOutcome.ProcessingFailed
                : !fence.IsHeld || contextSnapshot.CurrentSourceId != position.SourceId
                    || contextSnapshot.SourceBindingGeneration != position.BindingGeneration
                    || (session.CandidateSourceSegmentId is not null && session.CandidateSourceSegmentId != position.SourceSegmentId)
                    ? DrainOutcome.SourceChanged : DrainOutcome.Success;

            if (outcome != DrainOutcome.Success)
            {
                CompleteCommand(
                    item.Completion!,
                    GameplaySessionOperationResult.Failure(ToGameplayOutcome(outcome), outcome.ToString()));
                return;
            }

            var persist = FinalizeSessionLocked(mutableContext!, session!, "Manual finish session.");
            if (persist is not null && persist.IsSuccess)
            {
                CompleteCommand(item.Completion!, GameplaySessionOperationResult.Success());
                return;
            }

            CompleteCommand(
                item.Completion!,
                GameplaySessionOperationResult.Failure(
                    GameplaySessionOutcome.ProcessingFailed,
                    persist?.Detail ?? persist?.Outcome.ToString() ?? "Historical segment was not persisted."));
        }
    }

    private static GameplaySessionOutcome ToGameplayOutcome(DrainOutcome outcome) => outcome switch
    {
        DrainOutcome.ContextGone or DrainOutcome.SessionChanged or DrainOutcome.SourceChanged => GameplaySessionOutcome.NoActiveSession,
        DrainOutcome.Overloaded => GameplaySessionOutcome.Overloaded,
        DrainOutcome.ServiceStopped => GameplaySessionOutcome.ServiceStopped,
        _ => GameplaySessionOutcome.ProcessingFailed
    };
}
