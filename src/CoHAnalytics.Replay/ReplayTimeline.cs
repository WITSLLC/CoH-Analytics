namespace CoHAnalytics.Replay;

public enum ReplayTimelineRetentionClass
{
    Anchor,
    Milestone,
    Sample
}

public enum ReplayTimelineCategory
{
    Run,
    Workspace,
    Replay,
    Pipeline,
    Pressure,
    Drain,
    Fault,
    Analytics
}

public sealed record ReplayTimelineEvent(
    long Sequence,
    long ElapsedMilliseconds,
    ReplayTimelineCategory Category,
    string Code,
    int? ContextOrdinal,
    long? Value,
    string? Unit,
    ReplayTimelineRetentionClass RetentionClass);

public sealed class ReplayTimeline
{
    public const int Capacity = 256;

    private readonly TimeProvider _timeProvider;
    private readonly List<ReplayTimelineEvent> _events = [];
    private readonly object _sync = new();
    private long _sequence;
    private long _startTimestamp;
    private bool _started;
    private bool _truncationEventRecorded;

    public ReplayTimeline(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public int DroppedEventCount { get; private set; }

    public bool Truncated { get; private set; }

    public IReadOnlyList<ReplayTimelineEvent> Events => _events;

    public void Record(
        ReplayTimelineCategory category,
        string code,
        ReplayTimelineRetentionClass retentionClass,
        int? contextOrdinal = null,
        long? value = null,
        string? unit = null)
    {
        lock (_sync)
        {
            RecordCore(category, code, retentionClass, contextOrdinal, value, unit);
        }
    }

    private void RecordCore(
        ReplayTimelineCategory category,
        string code,
        ReplayTimelineRetentionClass retentionClass,
        int? contextOrdinal = null,
        long? value = null,
        string? unit = null)
    {
        if (!_started)
        {
            _startTimestamp = _timeProvider.GetTimestamp();
            _started = true;
        }

        if (retentionClass == ReplayTimelineRetentionClass.Anchor
            && code == "run.started"
            && _events.Any(entry => entry.Code == "run.started"))
        {
            return;
        }

        if (retentionClass == ReplayTimelineRetentionClass.Anchor && IsTerminalAnchor(code))
        {
            _events.RemoveAll(existing => IsTerminalAnchor(existing.Code));
        }

        var elapsed = GetElapsedMilliseconds();
        var entry = new ReplayTimelineEvent(
            ++_sequence,
            elapsed,
            category,
            code,
            contextOrdinal,
            value,
            unit,
            retentionClass);

        if (!TryMakeRoomFor(entry))
        {
            return;
        }

        _events.Add(entry);
    }

    private bool TryMakeRoomFor(ReplayTimelineEvent entry)
    {
        while (_events.Count >= Capacity)
        {
            if (!Truncated)
            {
                Truncated = true;
                EnsureTruncationEvent();
            }

            if (TryEvictOldest(ReplayTimelineRetentionClass.Sample))
            {
                continue;
            }

            if (TryEvictOldestNonAnchorMilestone())
            {
                continue;
            }

            if (IsTerminalAnchor(entry.Code))
            {
                return true;
            }

            DroppedEventCount++;
            return false;
        }

        return true;
    }

    private bool TryEvictOldest(ReplayTimelineRetentionClass retentionClass)
    {
        for (var index = 0; index < _events.Count; index++)
        {
            if (_events[index].RetentionClass == retentionClass)
            {
                _events.RemoveAt(index);
                DroppedEventCount++;
                return true;
            }
        }

        return false;
    }

    private bool TryEvictOldestNonAnchorMilestone()
    {
        for (var index = 0; index < _events.Count; index++)
        {
            var candidate = _events[index];
            if (candidate.RetentionClass == ReplayTimelineRetentionClass.Milestone
                && !IsProtectedAnchor(candidate.Code))
            {
                _events.RemoveAt(index);
                DroppedEventCount++;
                return true;
            }
        }

        return false;
    }

    private void EnsureTruncationEvent()
    {
        if (_truncationEventRecorded)
        {
            return;
        }

        _truncationEventRecorded = true;
        var truncation = new ReplayTimelineEvent(
            ++_sequence,
            GetElapsedMilliseconds(),
            ReplayTimelineCategory.Run,
            "timeline.truncated",
            null,
            DroppedEventCount,
            "events",
            ReplayTimelineRetentionClass.Milestone);
        _events.Add(truncation);
    }

    private long GetElapsedMilliseconds()
    {
        if (!_started)
        {
            return 0;
        }

        var delta = _timeProvider.GetTimestamp() - _startTimestamp;
        return (long)(delta * 1000.0 / _timeProvider.TimestampFrequency);
    }

    private static bool IsTerminalAnchor(string code) =>
        code is "run.completed" or "run.failed";

    private static bool IsProtectedAnchor(string code) =>
        code is "run.started" or "run.completed" or "run.failed";
}
