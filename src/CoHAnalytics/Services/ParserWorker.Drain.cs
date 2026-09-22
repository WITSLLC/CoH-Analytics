using System.Threading.Channels;
using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

public sealed partial class ParserWorker
{
    private sealed record OutputBatch(IReadOnlyList<ParserRawEvent> Lines, ParserFence? Fence,
        TaskCompletionSource Completion);
    private Channel<OutputBatch>? _output;
    private Task? _outputTask;
    private ParserFence? _fence;
    private bool _outputFailed;
    public event EventHandler<ParserBoundaryEventArgs>? BoundaryAvailable;

    private void StartOutput()
    {
        if (_output is not null) return;
        _output = Channel.CreateBounded<OutputBatch>(new BoundedChannelOptions(_options.EventQueueCapacity)
        { SingleReader = true, SingleWriter = true });
        _outputTask = Task.Run(EmitOutputAsync);
    }

    // Called only under the I/O gate. The emitter never takes this gate while invoking subscribers.
    private Task QueueOutputLocked(IReadOnlyList<ParserRawEvent> lines, ParserFence? fence = null)
    {
        if (lines.Count == 0 && fence is null) return Task.CompletedTask;
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (_outputFailed || _output?.Writer.TryWrite(new(lines.ToArray(), fence, done)) != true)
        {
            _outputFailed = true;
            fence?.Invalidate(DrainOutcome.Overloaded);
            FaultLocked("output_overflow", "Ordered worker output was rejected.");
            done.TrySetResult();
        }
        return done.Task;
    }

    private async Task EmitOutputAsync()
    {
        await foreach (var batch in _output!.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            try
            {
                PublishEvents(batch.Lines);
                if (batch.Fence is { } fence)
                {
                    if (BoundaryAvailable is { } handler) handler(this, new(fence));
                    else fence.Invalidate(DrainOutcome.ServiceStopped);
                }
            }
            catch (Exception)
            {
                batch.Fence?.Invalidate(DrainOutcome.ProcessingFailed);
                await _gate.WaitAsync().ConfigureAwait(false);
                try { _outputFailed = true; FaultLocked("output_delivery_failed", "Worker output subscriber failed."); }
                finally { _gate.Release(); }
            }
            finally { batch.Completion.TrySetResult(); }
        }
    }

    private void InvalidateFenceLocked(DrainOutcome reason)
    {
        _fence?.Invalidate(reason);
        _fence = null;
    }

    private async Task ReleaseFenceAsync(ParserFence fence, bool close)
    {
        ParserWorkerSnapshot? changed = null;
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!ReferenceEquals(_fence, fence)) return;
            _fence = null;
            if (close)
            {
                FinalizeActiveSegmentLocked(ParserSourceSegmentState.Completed);
                CloseStreamLocked();
                _state = ParserWorkerState.Stopped;
                _lifetimeCancellation?.Cancel();
                changed = UpdateSnapshotLocked();
            }
        }
        finally { _gate.Release(); }
        PublishState(changed);
    }

    public async Task<ParserDrainResult> PauseAndDrainThroughAsync(ParserSourcePosition? boundary = null,
        CancellationToken cancellationToken = default)
    {
        ParserFence? acquired = null;
        var events = new List<ParserRawEvent>();
        try
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!_started || _disposed || _state == ParserWorkerState.Stopped) return new(DrainOutcome.ServiceStopped);
                if (_state == ParserWorkerState.Faulted || _outputFailed) return new(DrainOutcome.ParserFault);
                if (_fence is not null) return new(DrainOutcome.AlreadyHeld);
                if (_activeSegment is null || _stream is null || _boundSource is null) return new(DrainOutcome.ContextGone);
                var identity = new ParserSourcePosition(WorkerId, _boundSource, _activeSegment.SourceSegmentId, _appliedGeneration, 0);
                if (boundary is not null && boundary with { ByteOffset = 0 } != identity) return new(DrainOutcome.SourceChanged);
                var limit = boundary?.ByteOffset ?? _stream.Length;
                if (_stream.Length < _cursor) return new(DrainOutcome.BoundaryUnreachable);
                if (limit < _cursor) return new(DrainOutcome.BoundaryAlreadyPassed);
                if (limit > _stream.Length) return new(DrainOutcome.BoundaryUnreachable);
                // Once the gate is acquired this finite read owns admission until its marker is released.
                await ReadAvailableLockedAsync(events, cancellationToken, limit).ConfigureAwait(false);
                if (_cursor != limit) return new(DrainOutcome.BoundaryUnreachable);
                acquired = new ParserFence(ContextId, identity with { ByteOffset = _lineStartOffset }, limit,
                    _lastEventSequence, ReleaseFenceAsync);
                _fence = acquired;
                _ = QueueOutputLocked(events, acquired);
                events.Clear();
                UpdateSnapshotLocked();
            }
            finally
            {
                // A cancelled finite read may already have framed lines. Never discard that prefix.
                if (events.Count != 0) _ = QueueOutputLocked(events);
                _gate.Release();
            }
            var outcome = await acquired.Transport.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            if (outcome == DrainOutcome.Success && acquired.IsHeld) return new(outcome, acquired);
            await acquired.AbortAsync().ConfigureAwait(false);
            return new(outcome == DrainOutcome.Success ? DrainOutcome.SourceChanged : outcome);
        }
        catch (OperationCanceledException)
        {
            if (acquired is not null) await acquired.AbortAsync().ConfigureAwait(false);
            return new(DrainOutcome.Cancelled);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.DecoderFallbackException)
        {
            if (acquired is not null) await acquired.AbortAsync().ConfigureAwait(false);
            await FaultAsync("drain_source_failed", "The pinned source could not be drained.").ConfigureAwait(false);
            return new(DrainOutcome.ParserFault);
        }
    }
}
