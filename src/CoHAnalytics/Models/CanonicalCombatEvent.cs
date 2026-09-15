namespace CoHAnalytics.Models;

/// <summary>
/// Canonical combat domain event. CombatEngine consumes logical survivors
/// (<c>DuplicateOf == null</c>). Legacy <see cref="CombatEvent"/> remains a compatibility mapping.
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

    /// <summary>
    /// Exact surfaced name. For RechargeCandidate this is unverified candidate text, not
    /// proof of a power. Preserve it for later same-session correlation with player activation.
    /// </summary>
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

    /// <summary>
    /// Activation or the wording of an unconfirmed recharge candidate. A recharge observation
    /// does not establish a power or usable lifecycle sequence. Null means not lifecycle evidence.
    /// </summary>
    public PowerStateTransition? PowerStateTransition { get; init; }

    /// <summary>
    /// Independent player-power evidence. Pet activations and recharge-shaped text cannot
    /// establish a player power. This stateless model never confirms recharge candidates.
    /// </summary>
    public bool IsPlayerPowerActivationEvidence => Family == CombatEventFamily.Activation
        && Actor.Type == ActorType.Self
        && PowerStateTransition == global::CoHAnalytics.Models.PowerStateTransition.Activated
        && !string.IsNullOrWhiteSpace(PowerName);

    public EventOccurrenceRef? DuplicateOf { get; init; }
}
