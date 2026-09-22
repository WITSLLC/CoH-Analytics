using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

/// <summary>
/// Session-local ordered input to dedup. Empty policy delivers immediately. Enabled rules
/// retain a bounded connected sequence window across live calls, closing at a scope change,
/// a gap beyond the candidate band, or session finalization. Capacity exhaustion keeps all
/// occurrences and disables merging for the rest of this session rather than guessing.
/// </summary>
internal sealed class SessionCombatStream
{
    internal const int MaxPendingEvents = 256;
    private readonly MirrorCompatibilityPolicy _policy;
    private readonly List<CanonicalCombatEvent> _pending = [];
    private readonly Dictionary<Scope, (long Sequence, long ByteEnd)> _watermarks = [];
    private Scope? _scope;
    private long _lastSequence;
    private bool _keepAll;

    public SessionCombatStream(MirrorCompatibilityPolicy policy) => _policy = policy;
    public bool CoverageLimited { get; private set; }
    public bool LastPushAccepted { get; private set; }

    public IReadOnlyList<CanonicalCombatEvent> Push(CanonicalCombatEvent item)
    {
        var scope = Scope.From(item);
        var sequence = item.Provenance.ParserSequence;
        LastPushAccepted = false;
        if (_watermarks.TryGetValue(scope, out var previous)
            && (sequence <= previous.Sequence || item.Provenance.ByteStart < previous.ByteEnd))
        {
            return []; // Reprocessing an already accepted scoped occurrence.
        }
        _watermarks[scope] = (sequence, item.Provenance.ByteEnd);
        LastPushAccepted = true;
        if (_policy.EnabledRules.Count == 0 || _keepAll)
        {
            return item.DuplicateOf is null ? [item] : [];
        }

        IReadOnlyList<CanonicalCombatEvent> ready = [];
        if (_scope != scope || (decimal)sequence - _lastSequence > Deduplicator.CandidateSequenceLookahead)
        {
            ready = Flush();
        }
        _scope = scope;
        _lastSequence = sequence;
        _pending.Add(item);
        if (_pending.Count >= MaxPendingEvents)
        {
            _keepAll = true;
            CoverageLimited = true;
            var all = ready.Concat(_pending.Where(item => item.DuplicateOf is null)).ToArray();
            _pending.Clear();
            return all;
        }
        return ready;
    }

    public IReadOnlyList<CanonicalCombatEvent> Flush()
    {
        if (_pending.Count == 0) return [];
        var result = new Deduplicator(_policy).Deduplicate(_pending).LogicalEvents.ToArray();
        _pending.Clear();
        return result;
    }

    private readonly record struct Scope(MonitoringContextId Context, string Source, string Account,
        Guid Segment, long Binding)
    {
        public static Scope From(CanonicalCombatEvent item) => new(item.Provenance.ContextId,
            item.Provenance.SourceId, item.Provenance.AccountStableId, item.Provenance.SourceSegmentId,
            item.Provenance.BindingGeneration);
    }
}
