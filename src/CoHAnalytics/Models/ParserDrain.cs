namespace CoHAnalytics.Models;

/// <summary>Runtime source identity. ByteOffset is exclusive; never compare offsets across identities.</summary>
public sealed record ParserSourcePosition(ParserWorkerId WorkerId, LogSourceId SourceId,
    ParserSourceSegmentId SourceSegmentId, long BindingGeneration, long ByteOffset);

public enum DrainOutcome
{
    Success, ContextGone, SessionChanged, SourceChanged, BoundaryUnreachable, BoundaryAlreadyPassed,
    ParserFault, ProcessingFailed, Overloaded, ServiceStopped, Cancelled, AlreadyHeld
}

public sealed record ParserDrainResult(DrainOutcome Outcome, ParserFence? Fence = null);
public sealed record GameplayDrainResult(DrainOutcome Outcome, ParserFence? Fence = null);

/// <summary>Runtime-only admission lease. Disposal/abort resumes; Close stops this worker only.</summary>
public sealed class ParserFence : IAsyncDisposable
{
    private readonly Func<ParserFence, bool, Task> _release;
    private readonly TaskCompletionSource<DrainOutcome> _invalidated = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _released;
    private readonly object _releaseLock = new();
    private Task? _releaseTask;
    internal ParserFence(MonitoringContextId contextId, ParserSourcePosition position, long requestedLimit,
        long lastSequence, Func<ParserFence, bool, Task> release)
    {
        ContextId = contextId; Position = position; RequestedLimit = requestedLimit;
        LastSequence = lastSequence; _release = release;
    }
    public Guid RequestId { get; } = Guid.NewGuid();
    public MonitoringContextId ContextId { get; }
    public ParserSourcePosition Position { get; }
    public long RequestedLimit { get; }
    public long EffectiveBoundary => Position.ByteOffset;
    public long LastSequence { get; }
    public bool IsHeld => Volatile.Read(ref _released) == 0 && !_invalidated.Task.IsCompleted;
    internal Task<DrainOutcome> Invalidated => _invalidated.Task;
    internal TaskCompletionSource<DrainOutcome> Transport { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal void Invalidate(DrainOutcome reason)
    {
        _invalidated.TrySetResult(reason);
        Transport.TrySetResult(reason);
    }
    public Task ResumeAsync() => ReleaseAsync(false);
    public Task AbortAsync() => ReleaseAsync(false);
    public Task CloseAsync() => ReleaseAsync(true);
    private Task ReleaseAsync(bool close)
    {
        lock (_releaseLock)
        {
            if (_releaseTask is not null) return _releaseTask;
            Volatile.Write(ref _released, 1);
            Invalidate(DrainOutcome.Cancelled);
            return _releaseTask = _release(this, close);
        }
    }
    public async ValueTask DisposeAsync() => await AbortAsync().ConfigureAwait(false);
}

public sealed class ParserBoundaryEventArgs(ParserFence fence) : EventArgs
{
    public ParserFence Fence { get; } = fence;
}

internal sealed record ParserOutput(ParserRawEvent? Data = null, ParserFence? Boundary = null);
