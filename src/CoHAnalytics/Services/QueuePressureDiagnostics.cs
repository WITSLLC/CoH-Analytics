namespace CoHAnalytics.Services;

using System.Threading.Channels;

/// <summary>Payload-free invariant classification for one bounded queue-pressure snapshot.</summary>
public enum QueuePressureInvariantClassification
{
    ConsistentDrained,
    ConsistentQueued,
    ConsistentInFlight,
    PhantomDepth,
    CounterMismatch,
    Overflowed
}

/// <summary>Immutable, payload-free queue-pressure observation for one bounded work queue.</summary>
public sealed record QueuePressureDiagnostics
{
    public required long LifecycleEpoch { get; init; }

    public required int Capacity { get; init; }

    public required int CurrentDepth { get; init; }

    public required int PeakDepth { get; init; }

    public required int InFlightCount { get; init; }

    public required long AcceptedCount { get; init; }

    public required long CompletedCount { get; init; }

    public required long RejectedCount { get; init; }

    public required long AbandonedCount { get; init; }

    public required bool Overflowed { get; init; }

    public bool IsDrained =>
        CurrentDepth == 0
        && InFlightCount == 0
        && AcceptedCount == CompletedCount + AbandonedCount;

    public long SettledCount => CompletedCount + AbandonedCount;

    public QueuePressureInvariantClassification ClassifyInvariant()
    {
        if (Overflowed)
        {
            return QueuePressureInvariantClassification.Overflowed;
        }

        if (IsDrained)
        {
            return QueuePressureInvariantClassification.ConsistentDrained;
        }

        if (CurrentDepth > 0
            && InFlightCount == 0
            && AcceptedCount == SettledCount)
        {
            return QueuePressureInvariantClassification.PhantomDepth;
        }

        if (InFlightCount > 0 && AcceptedCount > SettledCount)
        {
            return QueuePressureInvariantClassification.ConsistentInFlight;
        }

        if (CurrentDepth > 0 && AcceptedCount > SettledCount)
        {
            return QueuePressureInvariantClassification.ConsistentQueued;
        }

        return QueuePressureInvariantClassification.CounterMismatch;
    }

    public bool MatchesAccountingIdentity() =>
        AcceptedCount == CompletedCount + AbandonedCount + CurrentDepth + InFlightCount;

    public static QueuePressureDiagnostics Idle(int capacity, long lifecycleEpoch = 0) =>
        new()
        {
            LifecycleEpoch = lifecycleEpoch,
            Capacity = capacity,
            CurrentDepth = 0,
            PeakDepth = 0,
            InFlightCount = 0,
            AcceptedCount = 0,
            CompletedCount = 0,
            RejectedCount = 0,
            AbandonedCount = 0,
            Overflowed = false
        };
}

/// <summary>Immutable, payload-free observation for the pending committed-event buffer.</summary>
public sealed record PendingCommittedEventDiagnostics
{
    public required int Capacity { get; init; }

    public required int CurrentCount { get; init; }

    public required int PeakCount { get; init; }

    public required long DiscardedCount { get; init; }

    public static PendingCommittedEventDiagnostics Empty(int capacity) =>
        new()
        {
            Capacity = capacity,
            CurrentCount = 0,
            PeakCount = 0,
            DiscardedCount = 0
        };
}

internal sealed class QueuePressureTracker
{
    private readonly object _sync = new();
    private readonly int _capacity;
    private long _lifecycleEpoch;
    private int _currentDepth;
    private int _peakDepth;
    private int _inFlightCount;
    private long _acceptedCount;
    private long _completedCount;
    private long _rejectedCount;
    private long _abandonedCount;
    private bool _overflowed;

    public QueuePressureTracker(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(capacity, 0);
        _capacity = capacity;
    }

    public void Reset(long lifecycleEpoch)
    {
        lock (_sync)
        {
            _lifecycleEpoch = lifecycleEpoch;
            _currentDepth = 0;
            _peakDepth = 0;
            _inFlightCount = 0;
            _acceptedCount = 0;
            _completedCount = 0;
            _rejectedCount = 0;
            _abandonedCount = 0;
            _overflowed = false;
        }
    }

    public void RecordAccepted() => BeginAdmission();

    public void BeginAdmission()
    {
        lock (_sync)
        {
            _acceptedCount++;
            _currentDepth++;
            if (_currentDepth > _peakDepth)
            {
                _peakDepth = _currentDepth;
            }
        }
    }

    public void CancelAdmission()
    {
        lock (_sync)
        {
            if (_acceptedCount > 0)
            {
                _acceptedCount--;
            }

            if (_currentDepth > 0)
            {
                _currentDepth--;
            }
        }
    }

    public bool MatchesAccountingIdentity() =>
        Snapshot().MatchesAccountingIdentity();

    public void RecordRejected()
    {
        lock (_sync)
        {
            _rejectedCount++;
        }
    }

    public void LatchOverflow()
    {
        lock (_sync)
        {
            _overflowed = true;
        }
    }

    public void RecordDequeued()
    {
        lock (_sync)
        {
            if (_currentDepth > 0)
            {
                _currentDepth--;
            }

            _inFlightCount++;
        }
    }

    public void RecordCompleted()
    {
        lock (_sync)
        {
            if (_inFlightCount > 0)
            {
                _inFlightCount--;
            }

            _completedCount++;
        }
    }

    public void RecordAbandoned()
    {
        lock (_sync)
        {
            if (_inFlightCount > 0)
            {
                _inFlightCount--;
            }

            _abandonedCount++;
        }
    }

    public void AbandonUnfinishedAtShutdown()
    {
        lock (_sync)
        {
            if (_currentDepth > 0 || _inFlightCount > 0)
            {
                _abandonedCount += _currentDepth + _inFlightCount;
                _currentDepth = 0;
                _inFlightCount = 0;
            }
        }
    }

    public QueuePressureDiagnostics Snapshot()
    {
        lock (_sync)
        {
            return new QueuePressureDiagnostics
            {
                LifecycleEpoch = _lifecycleEpoch,
                Capacity = _capacity,
                CurrentDepth = _currentDepth,
                PeakDepth = _peakDepth,
                InFlightCount = _inFlightCount,
                AcceptedCount = _acceptedCount,
                CompletedCount = _completedCount,
                RejectedCount = _rejectedCount,
                AbandonedCount = _abandonedCount,
                Overflowed = _overflowed
            };
        }
    }
}

internal static class QueuePressureAdmission
{
    public static bool TryPublish<T>(ChannelWriter<T> writer, T item, QueuePressureTracker tracker)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(tracker);

        tracker.BeginAdmission();
        if (writer.TryWrite(item))
        {
            return true;
        }

        tracker.CancelAdmission();
        return false;
    }

    public static async Task PublishAsync<T>(
        ChannelWriter<T> writer,
        T item,
        QueuePressureTracker tracker,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(tracker);

        tracker.BeginAdmission();
        try
        {
            await writer.WriteAsync(item, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            tracker.CancelAdmission();
            throw;
        }
    }
}
