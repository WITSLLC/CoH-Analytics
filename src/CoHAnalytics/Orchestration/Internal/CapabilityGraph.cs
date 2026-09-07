using CoHAnalytics.Orchestration.Models;

namespace CoHAnalytics.Orchestration.Internal;

internal sealed class CapabilityGraph
{
    private readonly Dictionary<string, string> _producerByCapability = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ApplicationContributorDescriptor> _descriptors = new(StringComparer.Ordinal);
    private readonly List<string> _topologicalOrder = [];
    private readonly List<string> _cycleParticipants = [];

    public IReadOnlyDictionary<string, string> ProducerByCapability => _producerByCapability;

    public IReadOnlyList<string> TopologicalOrder => _topologicalOrder;

    public int IndexOf(string providerId) => _topologicalOrder.IndexOf(providerId);

    public IReadOnlyList<string> ReverseTopologicalOrder => _topologicalOrder.AsEnumerable().Reverse().ToList();

    public IReadOnlyList<string> CycleParticipants => _cycleParticipants;

    public bool HasCycle => _cycleParticipants.Count > 0;

    public void Clear()
    {
        _producerByCapability.Clear();
        _descriptors.Clear();
        _topologicalOrder.Clear();
        _cycleParticipants.Clear();
    }

    public void Add(ApplicationContributorDescriptor descriptor)
    {
        _descriptors[descriptor.ProviderId] = descriptor;
        foreach (var capability in descriptor.Produces)
        {
            _producerByCapability[capability] = descriptor.ProviderId;
        }
    }

    public void Remove(string providerId)
    {
        if (!_descriptors.Remove(providerId, out var descriptor))
        {
            return;
        }

        foreach (var capability in descriptor.Produces)
        {
            if (_producerByCapability.TryGetValue(capability, out var producer)
                && string.Equals(producer, providerId, StringComparison.Ordinal))
            {
                _producerByCapability.Remove(capability);
            }
        }
    }

    public bool TryGetProducer(string capabilityId, out string? providerId) =>
        _producerByCapability.TryGetValue(capabilityId, out providerId);

    public bool RebuildTopologicalOrder(out string? cycleError)
    {
        _topologicalOrder.Clear();
        _cycleParticipants.Clear();
        cycleError = null;

        var adjacency = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var indegree = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var providerId in _descriptors.Keys)
        {
            adjacency[providerId] = new HashSet<string>(StringComparer.Ordinal);
            indegree[providerId] = 0;
        }

        foreach (var (providerId, descriptor) in _descriptors)
        {
            foreach (var required in descriptor.Requires)
            {
                if (!_producerByCapability.TryGetValue(required, out var producer))
                {
                    continue;
                }

                if (string.Equals(producer, providerId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (adjacency[producer].Add(providerId))
                {
                    indegree[providerId]++;
                }
            }
        }

        var queue = new SortedSet<string>(
            indegree.Where(pair => pair.Value == 0).Select(pair => pair.Key),
            StringComparer.Ordinal);

        while (queue.Count > 0)
        {
            var next = queue.Min!;
            queue.Remove(next);
            _topologicalOrder.Add(next);

            foreach (var dependent in adjacency[next].OrderBy(id => id, StringComparer.Ordinal))
            {
                indegree[dependent]--;
                if (indegree[dependent] == 0)
                {
                    queue.Add(dependent);
                }
            }
        }

        if (_topologicalOrder.Count != _descriptors.Count)
        {
            _cycleParticipants.AddRange(
                _descriptors.Keys
                    .Where(id => !_topologicalOrder.Contains(id, StringComparer.Ordinal))
                    .OrderBy(id => id, StringComparer.Ordinal));
            cycleError = $"Required-dependency cycle involving: {string.Join(", ", _cycleParticipants)}";
            return false;
        }

        return true;
    }

    public CapabilityState EvaluateCapability(
        string capabilityId,
        Func<string, ContributorHealth?> getProducerHealth,
        Func<string, bool> isProducerStale)
    {
        if (!_producerByCapability.TryGetValue(capabilityId, out var producerId))
        {
            return CapabilityState.Missing;
        }

        return EvaluateState(getProducerHealth(producerId), isProducerStale(producerId));
    }

    /// <summary>
    /// Maps a producer's health and staleness to a capability state using the ordered
    /// precedence in §9.1 of the governing architecture: known failure outranks freshness,
    /// so a producer that reports <see cref="ContributorHealth.Error"/> or
    /// <see cref="ContributorHealth.Unavailable"/> resolves to <see cref="CapabilityState.Unhealthy"/>
    /// even when its contribution is stale. Only a <see cref="ContributorHealth.Ready"/>
    /// producer can resolve to <see cref="CapabilityState.Stale"/>.
    /// </summary>
    public static CapabilityState EvaluateState(ContributorHealth? producerHealth, bool isProducerStale) =>
        producerHealth switch
        {
            ContributorHealth.Error or ContributorHealth.Unavailable => CapabilityState.Unhealthy,
            ContributorHealth.Degraded => CapabilityState.SatisfiedDegraded,
            ContributorHealth.Ready => isProducerStale ? CapabilityState.Stale : CapabilityState.Satisfied,
            _ => CapabilityState.Unknown
        };
}
