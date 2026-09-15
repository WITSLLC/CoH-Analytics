using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

/// <summary>
/// Canonical-stage mirror dedup. Phase A consults the allowlist; Phase B collapses only when
/// independent discriminator evidence plus semantic and provenance constraints all hold.
/// Empty policy is a valid production state.
/// </summary>
public sealed class Deduplicator
{
    /// <summary>
    /// Bounded look-ahead used to discover allowlisted complementary pairs so a sequence-band
    /// miss can be counted as held rather than silently skipped. Not independent proof.
    /// </summary>
    public const int CandidateSequenceLookahead = 16;

    private readonly MirrorCompatibilityPolicy _policy;

    public Deduplicator()
        : this(MirrorCompatibilityPolicy.Version1)
    {
    }

    public Deduplicator(MirrorCompatibilityPolicy policy)
    {
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
    }

    public DedupResult Deduplicate(IReadOnlyList<CanonicalCombatEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);

        if (events.Count == 0 || _policy.EnabledRules.Count == 0)
        {
            return DedupResult.PassThrough(events, _policy.Version);
        }

        var output = events.ToArray();
        // Already-linked occurrences (including their survivors) cannot participate again.
        var linkedSurvivors = events.Where(item => item.DuplicateOf is not null)
            .Select(item => item.DuplicateOf!).ToHashSet();
        var unavailable = events.Select(item => item.DuplicateOf is not null
            || linkedSurvivors.Contains(ToOccurrenceRef(item))).ToArray();
        var candidates = new List<(int Left, int Right, MirrorCompatibilityRule Rule, bool Proven)>();
        var provenCounterparts = new int[output.Length];
        var collapsed = 0L;
        var held = 0L;
        var collapsedByRule = new Dictionary<string, long>(StringComparer.Ordinal);
        var heldByRule = new Dictionary<string, long>(StringComparer.Ordinal);
        var collapsedByFamily = new Dictionary<CombatEventFamily, long>();
        var heldByFamily = new Dictionary<CombatEventFamily, long>();

        for (var index = 0; index < output.Length; index++)
        {
            if (unavailable[index] || !_policy.IsPhaseAEligible(output[index].Family))
            {
                continue;
            }

            for (var candidateIndex = index + 1; candidateIndex < output.Length; candidateIndex++)
            {
                if (unavailable[candidateIndex])
                {
                    continue;
                }

                if (!SameScope(output[index], output[candidateIndex]))
                {
                    continue;
                }

                var rule = _policy.TryGetRule(output[index].Family, output[candidateIndex].Family);
                if (rule is null)
                {
                    continue;
                }

                var sequenceDistance = SequenceDistance(output[index], output[candidateIndex]);
                if (sequenceDistance > CandidateSequenceLookahead)
                {
                    continue;
                }

                var proven = TryProveSameLogicalEvent(rule, output[index], output[candidateIndex]);
                candidates.Add((index, candidateIndex, rule, proven));
                if (proven)
                {
                    provenCounterparts[index]++;
                    provenCounterparts[candidateIndex]++;
                }
            }
        }

        // Only mutually unique proofs may collapse. Ambiguous alternatives all stay intact,
        // independent of input ordering; each unordered eligible pair is diagnosed once.
        foreach (var (index, candidateIndex, rule, proven) in candidates)
        {
            if (!proven || provenCounterparts[index] != 1 || provenCounterparts[candidateIndex] != 1)
            {
                held++;
                Increment(heldByRule, rule.RuleId);
                Increment(heldByFamily, output[index].Family);
                Increment(heldByFamily, output[candidateIndex].Family);
                continue;
            }

            var earlierIsIndex = IsEarlierOccurrence(output[index], output[candidateIndex]);
            var survivorIndex = earlierIsIndex ? index : candidateIndex;
            var duplicateIndex = earlierIsIndex ? candidateIndex : index;
            var survivor = UnionSurvivor(output[survivorIndex], output[duplicateIndex], rule);
            output[survivorIndex] = survivor;
            output[duplicateIndex] = output[duplicateIndex] with { DuplicateOf = ToOccurrenceRef(survivor) };
            collapsed++;
            Increment(collapsedByRule, rule.RuleId);
            Increment(collapsedByFamily, output[index].Family);
            Increment(collapsedByFamily, output[candidateIndex].Family);
        }

        return new DedupResult
        {
            Events = output,
            Diagnostics = new DedupDiagnostics
            {
                PolicyVersion = _policy.Version,
                MirrorCollapsedCount = collapsed,
                MirrorCandidatesHeldCount = held,
                CollapsedByRule = collapsedByRule,
                HeldByRule = heldByRule,
                CollapsedByFamily = collapsedByFamily,
                HeldByFamily = heldByFamily
            }
        };
    }

    private static bool TryProveSameLogicalEvent(
        MirrorCompatibilityRule rule,
        CanonicalCombatEvent left,
        CanonicalCombatEvent right)
    {
        if (!rule.MatchesFamilies(left.Family, right.Family))
        {
            return false;
        }

        if (rule.RequiredDiscriminator != MirrorDiscriminatorKind.ActualSourceChannel
            || !rule.ChannelsAreComplementary(left.Family, left.SourceChannel, right.Family, right.SourceChannel)
            || left.SourceChannel != left.Provenance.SourceChannel
            || right.SourceChannel != right.Provenance.SourceChannel)
        {
            return false;
        }

        if (!AreMirrorCounterparts(left, right))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(left.PowerName) || string.IsNullOrWhiteSpace(right.PowerName)
            || !string.Equals(left.PowerName, right.PowerName, StringComparison.Ordinal))
        {
            return false;
        }

        if (!left.Amount.Equals(right.Amount)
            || left.Magnitude != right.Magnitude
            || !DamageTypesEqual(left.DamageType, right.DamageType)
            || ((left.Family is CombatEventFamily.DamageDealt or CombatEventFamily.DamageReceived
                    || right.Family is CombatEventFamily.DamageDealt or CombatEventFamily.DamageReceived)
                && left.DamageType is null))
        {
            return false;
        }

        if (!TimestampsAgree(left, right))
        {
            return false;
        }

        if (SequenceDistance(left, right) == 0 || SequenceDistance(left, right) > rule.MaxSequenceDistance)
        {
            return false;
        }

        return HasDistinctByteRange(left, right);
    }

    private static CanonicalCombatEvent UnionSurvivor(
        CanonicalCombatEvent survivor,
        CanonicalCombatEvent duplicate,
        MirrorCompatibilityRule rule)
    {
        var facets = survivor.Facets | duplicate.Facets | rule.FacetUnion;
        return survivor with { Facets = facets, DuplicateOf = null };
    }

    private static bool AreMirrorCounterparts(CanonicalCombatEvent left, CanonicalCombatEvent right) =>
        ActorsMatch(left.Actor, right.Target) && ActorsMatch(left.Target, right.Actor);

    private static bool ActorsMatch(ActorRef? left, ActorRef? right)
    {
        if (left is null || right is null || left.Type != right.Type)
        {
            return false;
        }

        if (left.Type == ActorType.Self)
        {
            return true;
        }

        if (left.Type is ActorType.OwnPet or ActorType.OtherPet
            && left.PetKey is not null
            && right.PetKey is not null)
        {
            return !string.IsNullOrWhiteSpace(left.PetKey.NormalizedPetName)
                && left.PetKey.OwnerRecordId == right.PetKey.OwnerRecordId
                && left.PetKey.InstanceOrdinal == right.PetKey.InstanceOrdinal
                && string.Equals(
                left.PetKey.NormalizedPetName,
                right.PetKey.NormalizedPetName,
                StringComparison.OrdinalIgnoreCase);
        }

        if (string.IsNullOrWhiteSpace(left.DisplayName) || string.IsNullOrWhiteSpace(right.DisplayName))
        {
            return false;
        }

        return CharacterNameNormalizer.NamesMatch(
            CharacterNameNormalizer.Normalize(left.DisplayName),
            CharacterNameNormalizer.Normalize(right.DisplayName));
    }

    private static bool DamageTypesEqual(DamageType? left, DamageType? right)
    {
        if (left is null && right is null)
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        return left.Equals(right);
    }

    private static bool TimestampsAgree(CanonicalCombatEvent left, CanonicalCombatEvent right)
    {
        if (left.SourceTimestamp is { } leftSource && right.SourceTimestamp is { } rightSource)
        {
            return leftSource == rightSource;
        }

        return false;
    }

    private static bool SameScope(CanonicalCombatEvent left, CanonicalCombatEvent right) =>
        !string.IsNullOrWhiteSpace(left.Provenance.SourceId)
        && string.Equals(left.Provenance.SourceId, right.Provenance.SourceId, StringComparison.Ordinal)
        && left.Provenance.AccountStableId == right.Provenance.AccountStableId
        && left.Provenance.SourceSegmentId == right.Provenance.SourceSegmentId
        && left.Provenance.BindingGeneration == right.Provenance.BindingGeneration
        && left.Provenance.ContextId == right.Provenance.ContextId;

    private static bool HasDistinctByteRange(CanonicalCombatEvent left, CanonicalCombatEvent right) =>
        left.Provenance.ByteStart >= 0 && right.Provenance.ByteStart >= 0
        && left.Provenance.ByteEnd > left.Provenance.ByteStart
        && right.Provenance.ByteEnd > right.Provenance.ByteStart
        && (left.Provenance.ByteEnd <= right.Provenance.ByteStart
            || right.Provenance.ByteEnd <= left.Provenance.ByteStart);

    private static decimal SequenceDistance(CanonicalCombatEvent left, CanonicalCombatEvent right) =>
        Math.Abs((decimal)left.Provenance.ParserSequence - right.Provenance.ParserSequence);

    private static bool IsEarlierOccurrence(CanonicalCombatEvent left, CanonicalCombatEvent right)
    {
        if (left.Provenance.ParserSequence != right.Provenance.ParserSequence)
        {
            return left.Provenance.ParserSequence < right.Provenance.ParserSequence;
        }

        return left.Provenance.ByteStart <= right.Provenance.ByteStart;
    }

    private static EventOccurrenceRef ToOccurrenceRef(CanonicalCombatEvent survivor) =>
        new()
        {
            SourceId = survivor.Provenance.SourceId,
            SourceSegmentId = survivor.Provenance.SourceSegmentId,
            BindingGeneration = survivor.Provenance.BindingGeneration,
            ParserSequence = survivor.Provenance.ParserSequence
        };

    private static void Increment(Dictionary<string, long> counts, string key) =>
        counts[key] = counts.GetValueOrDefault(key) + 1;

    private static void Increment(Dictionary<CombatEventFamily, long> counts, CombatEventFamily key) =>
        counts[key] = counts.GetValueOrDefault(key) + 1;
}

public sealed record DedupResult
{
    /// <summary>
    /// Audit occurrences, including duplicates. Never aggregate this collection directly:
    /// DuplicateOf != null contributes neither physical magnitude nor directional facets.
    /// Use LogicalEvents for both magnitude and facet aggregation.
    /// </summary>
    public required IReadOnlyList<CanonicalCombatEvent> Events { get; init; }

    /// <summary>One physical magnitude per survivor, with each directional facet present once.</summary>
    public IEnumerable<CanonicalCombatEvent> LogicalEvents => Events.Where(item => item.DuplicateOf is null);

    public required DedupDiagnostics Diagnostics { get; init; }

    public static DedupResult PassThrough(IReadOnlyList<CanonicalCombatEvent> events, int policyVersion) =>
        new()
        {
            Events = events,
            Diagnostics = DedupDiagnostics.Empty(policyVersion)
        };
}

public sealed record DedupDiagnostics
{
    public required int PolicyVersion { get; init; }

    public long MirrorCollapsedCount { get; init; }

    public long MirrorCandidatesHeldCount { get; init; }

    public IReadOnlyDictionary<string, long> CollapsedByRule { get; init; } =
        new Dictionary<string, long>();

    public IReadOnlyDictionary<string, long> HeldByRule { get; init; } =
        new Dictionary<string, long>();

    /// <summary>Pair participation per family; the sum is twice MirrorCollapsedCount.</summary>
    public IReadOnlyDictionary<CombatEventFamily, long> CollapsedByFamily { get; init; } =
        new Dictionary<CombatEventFamily, long>();

    /// <summary>Pair participation per family; the sum is twice MirrorCandidatesHeldCount.</summary>
    public IReadOnlyDictionary<CombatEventFamily, long> HeldByFamily { get; init; } =
        new Dictionary<CombatEventFamily, long>();

    public static DedupDiagnostics Empty(int policyVersion) =>
        new() { PolicyVersion = policyVersion };
}
