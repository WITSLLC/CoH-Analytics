namespace CoHAnalytics.Models;

/// <summary>
/// Canonical combat domain event. Aggregators still consume legacy <see cref="CombatEvent"/>
/// where the adapter has a truthful mapping.
/// </summary>
public sealed record CanonicalCombatEvent
{
    public required EventProvenance Provenance { get; init; }

    public required long Sequence { get; init; }

    public required DateTimeOffset ObservedAt { get; init; }

    public DateTime? SourceTimestamp { get; init; }

    public required CombatEventFamily Family { get; init; }

    public required CombatGrammarId GrammarId { get; init; }

    public required ActorRef Actor { get; init; }

    public ActorRef? Target { get; init; }

    public string? PowerName { get; init; }

    public CombatScaledAmount Amount { get; init; } = CombatScaledAmount.Zero;

    public MagnitudeKind Magnitude { get; init; } = MagnitudeKind.None;

    public DamageType? DamageType { get; init; }

    public DeliveryFlags Delivery { get; init; }

    /// <summary>Exact effect-suffix text from the current parser, when present.</summary>
    public string? EffectSuffix { get; init; }

    public CombatAttackOutcome? Outcome { get; init; }

    public long? DisplayedChanceHundredths { get; init; }

    public long? RollHundredths { get; init; }

    public string? SourceChannel { get; init; }

    public required MirrorClassification MirrorClass { get; init; }

    public EventFacets Facets { get; init; }

    /// <summary>Surfaced mez verb (Hold, Stun, Immobilize) when the grammar captures one.</summary>
    public string? StatusName { get; init; }

    public EventOccurrenceRef? DuplicateOf { get; init; }
}
