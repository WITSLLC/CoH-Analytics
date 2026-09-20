using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

public sealed partial class GameplaySessionManager
{
    private readonly HashSet<MonitoringContextId> _drainProcessingFailures = [];

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
}
