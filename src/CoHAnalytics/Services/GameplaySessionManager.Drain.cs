using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

public sealed partial class GameplaySessionManager
{
    private readonly HashSet<MonitoringContextId> _drainProcessingFailures = [];

    public Task<GameplaySessionOperationResult> FinishSessionAsync(
        MonitoringContextId contextId,
        GameplaySessionId expectedSessionId,
        ParserSourcePosition? boundary = null,
        CancellationToken cancellationToken = default) =>
        FinishSessionCoreAsync(
            contextId,
            expectedSessionId,
            boundary,
            cancellationToken,
            "Manual finish session.",
            closeFenceWhenComplete: false);

    public Task<GameplaySessionOperationResult> FinishSessionForAuthoritativeProcessExitAsync(
        MonitoringContextId contextId,
        HomecomingProcessInstance expectedProcess,
        GameplaySessionId expectedSessionId,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(expectedProcess);
        if (IsOnProcessorCallbackStack())
        {
            return Task.FromResult(
                GameplaySessionOperationResult.Failure(GameplaySessionOutcome.ReentrantCommandRejected));
        }

        var context = _monitoringSessionManager.Current.Contexts.FirstOrDefault(candidate =>
            candidate.ContextId == contextId);
        if (context is null || context.State == MonitoringContextState.Stopped)
        {
            return Task.FromResult(
                GameplaySessionOperationResult.Failure(GameplaySessionOutcome.NoActiveSession));
        }

        if (context.ProcessInstance is not { } bound
            || !IsSameProcess(bound, expectedProcess))
        {
            return Task.FromResult(
                GameplaySessionOperationResult.Failure(GameplaySessionOutcome.NoActiveSession, "Stale process binding."));
        }

        var boundCount = _monitoringSessionManager.Current.Contexts.Count(candidate =>
            candidate.State != MonitoringContextState.Stopped
            && candidate.ProcessInstance is { } process
            && IsSameProcess(process, expectedProcess));
        if (boundCount != 1)
        {
            return Task.FromResult(
                GameplaySessionOperationResult.Failure(GameplaySessionOutcome.NoActiveSession, "Process ownership is ambiguous."));
        }

        var session = Current.Sessions.FirstOrDefault(candidate =>
            candidate.ContextId == contextId
            && candidate.SessionId == expectedSessionId
            && candidate.LifecycleState is GameplaySessionLifecycleState.Active
                or GameplaySessionLifecycleState.Suspended);
        if (session is null)
        {
            return Task.FromResult(
                GameplaySessionOperationResult.Failure(GameplaySessionOutcome.NoActiveSession));
        }

        return FinishSessionCoreAsync(
            contextId,
            expectedSessionId,
            boundary: null,
            cancellationToken,
            "Authoritative process exit.",
            closeFenceWhenComplete: true);
    }

    private async Task<GameplaySessionOperationResult> FinishSessionCoreAsync(
        MonitoringContextId contextId,
        GameplaySessionId expectedSessionId,
        ParserSourcePosition? boundary,
        CancellationToken cancellationToken,
        string reason,
        bool closeFenceWhenComplete)
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
            if (!TryAdmitWork(WorkItem.FinishSession(contextId, expectedSessionId, fence, completion, reason)))
            {
                await ReleaseFinishFenceAsync(fence, closeFenceWhenComplete, success: false).ConfigureAwait(false);
                return WithClosedParserFence(
                    GameplaySessionOperationResult.Failure(GameplaySessionOutcome.Overloaded),
                    closeFenceWhenComplete);
            }

            _options.TestHooks?.AfterCommandAdmission?.Invoke();
            var result = await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            await ReleaseFinishFenceAsync(fence, closeFenceWhenComplete, result.IsSuccess).ConfigureAwait(false);
            return WithClosedParserFence(result, closeFenceWhenComplete);
        }
        catch (OperationCanceledException)
        {
            await ReleaseFinishFenceAsync(fence, closeFenceWhenComplete, success: false).ConfigureAwait(false);
            if (completion.Task.IsCompletedSuccessfully)
            {
                return WithClosedParserFence(completion.Task.Result, closeFenceWhenComplete);
            }

            return WithClosedParserFence(
                GameplaySessionOperationResult.Failure(
                    GameplaySessionOutcome.ProcessingFailed,
                    DrainOutcome.Cancelled.ToString()),
                closeFenceWhenComplete);
        }
    }

    private static Task ReleaseFinishFenceAsync(ParserFence fence, bool closeFenceWhenComplete, bool success)
    {
        if (closeFenceWhenComplete)
        {
            return fence.CloseAsync();
        }

        return success ? fence.ResumeAsync() : fence.AbortAsync();
    }

    private static GameplaySessionOperationResult WithClosedParserFence(
        GameplaySessionOperationResult result,
        bool closedParserFence) =>
        closedParserFence ? result with { ClosedParserFence = true } : result;

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
            }
            else
            {
                var persist = FinalizeSessionLocked(
                    mutableContext!,
                    session!,
                    item.FinishReason ?? "Manual finish session.");
                if (persist is not null && persist.IsSuccess)
                {
                    CompleteCommand(item.Completion!, GameplaySessionOperationResult.Success());
                }
                else
                {
                    CompleteCommand(
                        item.Completion!,
                        GameplaySessionOperationResult.Failure(
                            GameplaySessionOutcome.ProcessingFailed,
                            persist?.Detail ?? persist?.Outcome.ToString() ?? "Historical segment was not persisted."));
                }
            }
        }

        PublishSnapshotIfChanged();
    }

    private static GameplaySessionOutcome ToGameplayOutcome(DrainOutcome outcome) => outcome switch
    {
        DrainOutcome.ContextGone or DrainOutcome.SessionChanged or DrainOutcome.SourceChanged => GameplaySessionOutcome.NoActiveSession,
        DrainOutcome.Overloaded => GameplaySessionOutcome.Overloaded,
        DrainOutcome.ServiceStopped => GameplaySessionOutcome.ServiceStopped,
        _ => GameplaySessionOutcome.ProcessingFailed
    };

    internal static bool IsSameProcess(HomecomingProcessInstance left, HomecomingProcessInstance right) =>
        left.Equals(right)
        || left.IsMetadataRefinementOf(right)
        || right.IsMetadataRefinementOf(left);
}
