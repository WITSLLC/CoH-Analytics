using CoHAnalytics.Orchestration.Diagnostics;

namespace CoHAnalytics.Orchestration.Internal;

internal sealed class EventHistoryBuffer(int capacity)
{
    private readonly List<ApplicationOrchestrationEvent> _events = [];
    private long _sequence;

    public int Capacity { get; } = Math.Max(1, capacity);

    public long NextSequence => _sequence + 1;

    public ApplicationOrchestrationEvent Append(
        DateTimeOffset timestamp,
        ApplicationOrchestrationEventKind kind,
        string summary,
        string? providerId = null,
        string? relatedCapabilityId = null,
        long? snapshotRevision = null,
        string? detail = null)
    {
        var evt = new ApplicationOrchestrationEvent(
            ++_sequence,
            timestamp,
            kind,
            summary)
        {
            ProviderId = providerId,
            RelatedCapabilityId = relatedCapabilityId,
            SnapshotRevision = snapshotRevision,
            Detail = detail
        };

        _events.Add(evt);
        while (_events.Count > Capacity)
        {
            _events.RemoveAt(0);
        }

        return evt;
    }

    public IReadOnlyList<ApplicationOrchestrationEvent> Snapshot() => _events.ToArray();

    public void Clear()
    {
        _events.Clear();
        _sequence = 0;
    }
}
