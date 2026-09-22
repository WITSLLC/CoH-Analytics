using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

public sealed partial class ParserManager
{
    // A failed prefix cannot be repaired by a later successful line in that context.
    private readonly Dictionary<MonitoringContextId, DrainOutcome> _drainFailures = [];
    private readonly Dictionary<Guid, ParserFence> _pendingBoundaries = [];

    private void FailDrainContext(MonitoringContextId context, DrainOutcome outcome)
    {
        lock (_stateLock)
        {
            _drainFailures.TryAdd(context, outcome);
            foreach (var fence in _pendingBoundaries.Values.Where(f => f.ContextId == context)) fence.Invalidate(outcome);
        }
    }

    private void OnBoundaryAvailable(object? sender, ParserBoundaryEventArgs args)
    {
        var fence = args.Fence;
        lock (_stateLock)
        {
            if (!_running) { fence.Invalidate(DrainOutcome.ServiceStopped); return; }
            if (_drainFailures.TryGetValue(fence.ContextId, out var failed)) { fence.Invalidate(failed); return; }
            _pendingBoundaries[fence.RequestId] = fence;
            if (_eventChannel is not null && QueuePressureAdmission.TryPublish(_eventChannel.Writer,
                    new ParserOutput(Boundary: fence), _eventQueueTracker))
            {
                Interlocked.Increment(ref _queuedEventCount);
                return;
            }
            _pendingBoundaries.Remove(fence.RequestId);
            _drainFailures.TryAdd(fence.ContextId, DrainOutcome.Overloaded);
            fence.Invalidate(DrainOutcome.Overloaded);
        }
    }

    private void CompleteBoundary(ParserFence fence)
    {
        lock (_stateLock)
        {
            _pendingBoundaries.Remove(fence.RequestId);
            var outcome = !_running ? DrainOutcome.ServiceStopped
                : _drainFailures.GetValueOrDefault(fence.ContextId, DrainOutcome.Success);
            if (!_workers.TryGetValue(fence.ContextId, out var worker) || worker.WorkerId != fence.Position.WorkerId)
                outcome = DrainOutcome.ContextGone;
            fence.Transport.TrySetResult(outcome);
        }
    }

    public async Task<ParserDrainResult> PauseAndDrainThroughAsync(MonitoringContextId contextId,
        ParserSourcePosition? boundary = null, CancellationToken cancellationToken = default)
    {
        IParserWorker? worker;
        lock (_stateLock)
        {
            if (!_running) return new(DrainOutcome.ServiceStopped);
            if (_drainFailures.TryGetValue(contextId, out var failure)) return new(failure);
            if (!_workers.TryGetValue(contextId, out worker)) return new(DrainOutcome.ContextGone);
        }
        return await worker.PauseAndDrainThroughAsync(boundary, cancellationToken).ConfigureAwait(false);
    }
}
