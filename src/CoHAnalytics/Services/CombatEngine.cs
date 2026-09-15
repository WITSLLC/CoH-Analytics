using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

/// <summary>
/// Session-scoped combat analytics. Consumes only logical canonical events
/// (<c>DuplicateOf == null</c>). UI-agnostic; live and historical replay share this type.
/// </summary>
public sealed class CombatEngine
{
    public const int MaxTrackedPowers = 512;
    public const int MaxTrackedTargets = 256;
    public const int MaxTrackedPets = 64;
    public const int MaxTrackedDamageTypes = 64;
    public const string OverflowBucketKey = "Other";
    private const string OverflowIdentity = "\0overflow";

    private readonly Dictionary<PowerCubeKey, PowerAccumulator> _powers = [];
    private readonly Dictionary<string, DamageTypeAccumulator> _damageTypes = [];
    private readonly Dictionary<string, DamageTypeAccumulator> _incomingDamageTypes = [];
    private readonly Dictionary<string, TargetAccumulator> _targets = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PetAccumulator> _pets = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<ConfirmedPowerKey, long> _confirmedPlayerPowers = [];
    private readonly CombatAccuracyAccumulator _selfAccuracy = new();
    private readonly CombatAccuracyAccumulator _petAccuracy = new();
    private readonly ActorAccumulator _self = new();
    private readonly ActorAccumulator _ownedPetsAggregate = new();

    private SessionAccumulator _session;
    private bool _frozen;
    private bool _coverageLimited;
    private CanonicalCombatEvent? _firstObservation;
    private CanonicalCombatEvent? _lastObservation;
    private bool _missingOutgoingType;
    private bool _missingIncomingType;
    private bool _missingTarget;
    private CombatAnalyticsProjection? _stableProjection;

    public long LogicalEventsApplied { get; private set; }

    public long DuplicateOccurrencesIgnored { get; private set; }

    public void Apply(CanonicalCombatEvent canonicalEvent)
    {
        ArgumentNullException.ThrowIfNull(canonicalEvent);
        if (_frozen)
        {
            return;
        }

        _stableProjection = null;
        if (canonicalEvent.DuplicateOf is not null)
        {
            DuplicateOccurrencesIgnored++;
            return;
        }

        LogicalEventsApplied++;
        NoteObserved(canonicalEvent);
        ApplyLogical(canonicalEvent);
    }

    public void Freeze() => _frozen = true;

    internal void MarkCoverageLimited()
    {
        _coverageLimited = true;
        _stableProjection = null;
    }

    public CombatAnalyticsProjection Project() => Project(capture: null);

    public CombatAnalyticsProjection Project(SegmentClockCapture? capture)
    {
        _stableProjection ??= ProjectStable();
        return _stableProjection with
        {
            Clock = AttachRateMetrics(BuildClock(capture), _stableProjection.Session.Metrics)
        };
    }

    private CombatAnalyticsProjection ProjectStable()
    {
        var powers = _powers.Values
            .Select(item => item.Project())
            .OrderBy(row => row.Scope)
            .ThenBy(row => row.Direction)
            .ThenBy(row => row.PetNormalizedName, StringComparer.Ordinal)
            .ThenBy(row => row.PowerName, StringComparer.Ordinal)
            .ToList();

        var damageTypes = _damageTypes.Values
            .Select(item => item.Project())
            .OrderBy(row => row.IsOverflow)
            .ThenBy(row => row.DamageType.Text, StringComparer.Ordinal)
            .ToList();

        var actors = new List<CombatActorSummary>
        {
            _self.Project(CombatAnalyticsScope.Self, petNormalizedName: null, petDisplayName: null, _selfAccuracy.ToSnapshot()),
            _ownedPetsAggregate.Project(
                CombatAnalyticsScope.OwnPetsAggregate,
                petNormalizedName: null,
                petDisplayName: null,
                accuracy: _petAccuracy.ToSnapshot(), coverageLimited: _pets.Count > 0)
        };
        actors.AddRange(
            _pets.Values
                .Select(item => item.Project())
                .OrderBy(row => row.IsOverflow)
                .ThenBy(row => row.PetNormalizedName, StringComparer.Ordinal));

        var targets = _targets.Values
            .Select(item => item.Project())
            .OrderBy(row => row.IsOverflow)
            .ThenBy(row => row.NormalizedTargetName, StringComparer.Ordinal)
            .ToList();

        var accuracy = _selfAccuracy.ToSnapshot();
        var metrics = BuildSessionMetrics(accuracy);
        return new CombatAnalyticsProjection
        {
            AnalyticsSemanticVersion = AnalyticsSemanticVersion.Current,
            LogicalEventsApplied = LogicalEventsApplied,
            DuplicateOccurrencesIgnored = DuplicateOccurrencesIgnored,
            Session = _session.Project(
                accuracy,
                _coverageLimited,
                metrics),
            Clock = SegmentClock.Empty,
            Powers = Freeze(powers),
            DamageTypes = Freeze(damageTypes),
            DamageTypeBreakdown = TypeBreakdown(damageTypes, _missingOutgoingType),
            IncomingDamageTypeBreakdown = TypeBreakdown(_incomingDamageTypes.Values.Select(item => item.Project()).ToList(), _missingIncomingType),
            IncomingDamageTypes = Freeze(_incomingDamageTypes.Values.Select(item => item.Project())
                .OrderBy(item => item.IsOverflow).ThenBy(item => item.DamageType.Text, StringComparer.Ordinal).ToList()),
            Actors = Freeze(actors),
            Targets = Freeze(targets),
            CoverageLimited = _coverageLimited
        };
    }

    private void ApplyLogical(CanonicalCombatEvent canonicalEvent)
    {
        if (canonicalEvent.Amount.Hundredths < 0)
        {
            _coverageLimited = true;
            return;
        }
        if (canonicalEvent.Family is CombatEventFamily.EnvironmentDamage or CombatEventFamily.Unparsed
            or CombatEventFamily.CompanionMissSummary)
        {
            return; // Retained evidence, unsupported analytical magnitude/resolution.
        }
        if (canonicalEvent.Family == CombatEventFamily.RechargeCandidate)
        {
            ApplyRechargeCandidate(canonicalEvent);
            return;
        }

        ApplySessionFacets(canonicalEvent);
        ApplyOwnerActors(canonicalEvent);
        ApplyAccuracy(canonicalEvent);

        if (canonicalEvent.IsPlayerPowerActivationEvidence)
        {
            var key = ConfirmedPowerKey.From(canonicalEvent);
            if (_confirmedPlayerPowers.Count < MaxTrackedPowers || _confirmedPlayerPowers.ContainsKey(key))
                _confirmedPlayerPowers.TryAdd(key, canonicalEvent.Provenance.ParserSequence);
            else _coverageLimited = true;
        }

        var outgoing = canonicalEvent with { Actor = OutgoingActor(canonicalEvent) ?? ActorRef.Unknown };
        if (HasOutgoingPerspective(canonicalEvent) && TryResolvePowerCube(outgoing, out var power))
        {
            power.Apply(outgoing);
        }
        if (HasIncomingPerspective(canonicalEvent))
        {
            var incoming = canonicalEvent with { Actor = IncomingActor(canonicalEvent) ?? ActorRef.Unknown };
            if (TryResolvePowerCube(incoming, out var incomingPower, CombatAnalyticsDirection.Incoming))
                incomingPower.Apply(incoming);
            ApplyDamageType(incoming, _incomingDamageTypes, EventFacets.DamageReceived);
        }

        ApplyDamageType(canonicalEvent, _damageTypes, EventFacets.DamageDealt);
        ApplyOutgoingTarget(canonicalEvent);
    }

    private void ApplyRechargeCandidate(CanonicalCombatEvent canonicalEvent)
    {
        if (string.IsNullOrWhiteSpace(canonicalEvent.PowerName)
            || !_confirmedPlayerPowers.TryGetValue(ConfirmedPowerKey.From(canonicalEvent), out var activationSequence)
            || canonicalEvent.Provenance.ParserSequence <= activationSequence)
        {
            _session.UnmatchedRechargeCandidateCount++;
            _session.ObservedUnmatchedRecharge = true;
            return;
        }

        if (canonicalEvent.PowerStateTransition == PowerStateTransition.RechargeCompletedObserved)
        {
            _session.ObservedConfirmedRecharge = true;
            _session.ConfirmedRechargeCompletedCount++;
            if (TryGetSelfPower(canonicalEvent.PowerName, out var power))
            {
                power.ConfirmedRechargeCompletedCount++;
            }
        }
        else if (canonicalEvent.PowerStateTransition == PowerStateTransition.StillRechargingObserved)
        {
            _session.ObservedConfirmedStillRecharging = true;
            _session.ConfirmedStillRechargingCount++;
            if (TryGetSelfPower(canonicalEvent.PowerName, out var power))
            {
                power.ConfirmedStillRechargingCount++;
            }
        }
    }

    private void ApplySessionFacets(CanonicalCombatEvent canonicalEvent)
    {
        var facets = canonicalEvent.Facets;
        var amount = canonicalEvent.Amount;
        var outgoingActor = OutgoingActor(canonicalEvent);
        var incomingActor = IncomingActor(canonicalEvent);
        var ownedPetPerspective = incomingActor?.Type == ActorType.OwnPet;
        var ownerOutgoing = outgoingActor is not null && IsOwnerActor(outgoingActor.Type);

        if (facets.HasFlag(EventFacets.DamageDealt) || facets.HasFlag(EventFacets.DamageReceived))
        {
            _session.DamageEventCount++;
        }

        if (facets.HasFlag(EventFacets.DamageDealt) && ownerOutgoing)
        {
            _session.ObservedDamageDealt = true;
            _session.DamageDealt = AddMagnitude(_session.DamageDealt, amount);
            if (outgoingActor?.Type == ActorType.Self)
            {
                _session.ObservedDamageDealtSelf = true;
                _session.DamageDealtSelf = AddMagnitude(_session.DamageDealtSelf, amount);
            }
            else
            {
                _session.ObservedDamageDealtOwnedPets = true;
                _session.DamageDealtOwnedPets = AddMagnitude(_session.DamageDealtOwnedPets, amount);
            }
        }

        if (facets.HasFlag(EventFacets.DamageReceived) && incomingActor?.Type is ActorType.Self or ActorType.OwnPet)
        {
            if (ownedPetPerspective)
            {
                _session.ObservedDamageReceivedOwnedPets = true;
                _session.DamageReceivedOwnedPets = AddMagnitude(_session.DamageReceivedOwnedPets, amount);
            }
            else
            {
                _session.ObservedDamageReceived = true;
                _session.DamageReceived = AddMagnitude(_session.DamageReceived, amount);
            }
        }

        if (facets.HasFlag(EventFacets.HealDelivered) || facets.HasFlag(EventFacets.HealReceived))
        {
            _session.HealEventCount++;
        }

        if (facets.HasFlag(EventFacets.HealDelivered) && ownerOutgoing)
        {
            _session.ObservedHealingDealt = true;
            _session.HealingDealt = AddMagnitude(_session.HealingDealt, amount);
            if (outgoingActor?.Type == ActorType.Self)
            {
                _session.HealingDealtSelf = AddMagnitude(_session.HealingDealtSelf, amount);
            }
            else if (outgoingActor?.Type == ActorType.OwnPet)
            {
                _session.HealingDealtOwnedPets = AddMagnitude(_session.HealingDealtOwnedPets, amount);
            }
        }

        if (facets.HasFlag(EventFacets.HealReceived) && incomingActor?.Type is ActorType.Self or ActorType.OwnPet)
        {
            if (ownedPetPerspective)
            {
                _session.HealingReceivedOwnedPets = AddMagnitude(_session.HealingReceivedOwnedPets, amount);
            }
            else
            {
                _session.ObservedHealingReceived = true;
                _session.HealingReceived = AddMagnitude(_session.HealingReceived, amount);
            }
        }

        if (facets.HasFlag(EventFacets.EnduranceGrantDealt)
            || facets.HasFlag(EventFacets.EnduranceGrantReceived))
        {
            _session.EnduranceEventCount++;
        }

        if (facets.HasFlag(EventFacets.EnduranceGrantDealt) && ownerOutgoing)
        {
            _session.ObservedEnduranceGranted = true;
            _session.EnduranceGranted = AddMagnitude(_session.EnduranceGranted, amount);
            if (outgoingActor?.Type == ActorType.Self)
            {
                _session.EnduranceGrantedSelf = AddMagnitude(_session.EnduranceGrantedSelf, amount);
            }
            else if (outgoingActor?.Type == ActorType.OwnPet)
            {
                _session.EnduranceGrantedOwnedPets = AddMagnitude(_session.EnduranceGrantedOwnedPets, amount);
            }
        }

        if (facets.HasFlag(EventFacets.EnduranceGrantReceived) && incomingActor?.Type is ActorType.Self or ActorType.OwnPet)
        {
            if (ownedPetPerspective)
            {
                _session.EnduranceReceivedOwnedPets = AddMagnitude(_session.EnduranceReceivedOwnedPets, amount);
            }
            else
            {
                _session.ObservedEnduranceReceived = true;
                _session.EnduranceReceived = AddMagnitude(_session.EnduranceReceived, amount);
            }
        }

        if (facets.HasFlag(EventFacets.Activation) && canonicalEvent.Actor.Type == ActorType.Self)
        {
            _session.ObservedActivation = true;
            _session.ActivationCount++;
        }

        if (facets.HasFlag(EventFacets.AttackResolution))
        {
            _session.ObservedAttackResolution = true;
            _session.AttackResolutionCount++;
        }

        if (facets.HasFlag(EventFacets.Defeat))
        {
            _session.DefeatCount++;
            if (canonicalEvent.Actor.Type == ActorType.Self)
            {
                _session.MyDefeatCount++;
            }
        }

        if (facets.HasFlag(EventFacets.Mez))
        {
            _session.MezCount++;
        }

        if (facets.HasFlag(EventFacets.Knock))
        {
            _session.KnockCount++;
        }
    }

    private void ApplyOwnerActors(CanonicalCombatEvent canonicalEvent)
    {
        canonicalEvent = canonicalEvent with
        {
            Actor = OutgoingActor(canonicalEvent) ?? ActorRef.Unknown,
            Target = IncomingActor(canonicalEvent)
        };
        if (canonicalEvent.Actor.Type == ActorType.Self)
        {
            _self.ApplyOutgoing(canonicalEvent);
        }
        else if (canonicalEvent.Actor.Type == ActorType.OwnPet)
        {
            _ownedPetsAggregate.ApplyOutgoing(canonicalEvent);
            GetOrCreatePet(canonicalEvent.Actor).ApplyOutgoing(canonicalEvent);
        }

        if (canonicalEvent.Target?.Type == ActorType.OwnPet)
        {
            GetOrCreatePet(canonicalEvent.Target).ApplyIncoming(canonicalEvent);
            _ownedPetsAggregate.ApplyIncoming(canonicalEvent);
        }
        else if (canonicalEvent.Target?.Type == ActorType.Self && (canonicalEvent.Facets.HasFlag(EventFacets.DamageReceived)
                 || canonicalEvent.Facets.HasFlag(EventFacets.HealReceived)
                 || canonicalEvent.Facets.HasFlag(EventFacets.EnduranceGrantReceived)))
        {
            _self.ApplyIncoming(canonicalEvent);
        }
    }

    private void ApplyAccuracy(CanonicalCombatEvent canonicalEvent)
    {
        if (canonicalEvent.Family != CombatEventFamily.AttackResolution)
        {
            return;
        }

        if (canonicalEvent.Actor.Type == ActorType.Self)
        {
            _selfAccuracy.Apply(canonicalEvent);
        }
        else if (canonicalEvent.Actor.Type == ActorType.OwnPet)
        {
            _petAccuracy.Apply(canonicalEvent);
            GetOrCreatePet(canonicalEvent.Actor).Accuracy.Apply(canonicalEvent);
        }
    }

    private bool TryResolvePowerCube(CanonicalCombatEvent canonicalEvent, out PowerAccumulator power,
        CombatAnalyticsDirection direction = CombatAnalyticsDirection.Outgoing)
    {
        power = null!;
        if (string.IsNullOrWhiteSpace(canonicalEvent.PowerName))
        {
            return false;
        }

        if (canonicalEvent.Actor.Type == ActorType.Self)
        {
            power = GetOrCreatePower(
                new PowerCubeKey(CombatAnalyticsScope.Self, canonicalEvent.PowerName, PetNormalizedName: null, direction),
                canonicalEvent.PowerName,
                petNormalizedName: null,
                petDisplayName: null);
            return true;
        }

        if (canonicalEvent.Actor.Type != ActorType.OwnPet)
        {
            return false;
        }

        var petName = ResolvePetNormalizedName(canonicalEvent.Actor);
        if (petName is null)
        {
            return false;
        }

        power = GetOrCreatePower(
            new PowerCubeKey(CombatAnalyticsScope.PerPet, canonicalEvent.PowerName, petName.ToUpperInvariant(), direction),
            canonicalEvent.PowerName,
            petName,
            canonicalEvent.Actor.DisplayName);

        GetOrCreatePower(
            new PowerCubeKey(CombatAnalyticsScope.OwnPetsAggregate, canonicalEvent.PowerName, PetNormalizedName: null, direction),
            canonicalEvent.PowerName,
            petNormalizedName: null,
            petDisplayName: null)
            .Apply(canonicalEvent);
        return true;
    }

    private bool TryGetSelfPower(string powerName, out PowerAccumulator power) =>
        _powers.TryGetValue(new PowerCubeKey(CombatAnalyticsScope.Self, powerName, PetNormalizedName: null), out power!)
        || _powers.TryGetValue(new PowerCubeKey(CombatAnalyticsScope.Self, OverflowIdentity, null), out power!);

    private PowerAccumulator GetOrCreatePower(
        PowerCubeKey key,
        string powerName,
        string? petNormalizedName,
        string? petDisplayName)
    {
        if (_powers.TryGetValue(key, out var existing))
        {
            return existing;
        }

        if (_powers.Count >= MaxTrackedPowers)
        {
            _coverageLimited = true;
            var overflowKey = new PowerCubeKey(key.Scope, OverflowIdentity, null, key.Direction);
            if (!_powers.TryGetValue(overflowKey, out var overflow))
            {
                overflow = new PowerAccumulator(key.Scope, OverflowBucketKey, null, null, key.Direction)
                {
                    IsOverflow = true,
                    CoverageLimited = true
                };
                _powers[overflowKey] = overflow;
            }

            overflow.CoverageLimited = true;
            return overflow;
        }

        var created = new PowerAccumulator(key.Scope, powerName, petNormalizedName, petDisplayName, key.Direction);
        _powers[key] = created;
        return created;
    }

    private PetAccumulator GetOrCreatePet(ActorRef actor)
    {
        _coverageLimited = true; // Name rollup is not resolved instance identity.
        var normalized = ResolvePetNormalizedName(actor) ?? OverflowIdentity;
        if (_pets.TryGetValue(normalized, out var existing))
        {
            return existing;
        }

        if (_pets.Count >= MaxTrackedPets)
        {
            _coverageLimited = true;
            if (!_pets.TryGetValue(OverflowIdentity, out var overflow))
            {
                overflow = new PetAccumulator(OverflowBucketKey, OverflowBucketKey)
                {
                    IsOverflow = true,
                    CoverageLimited = true
                };
                _pets[OverflowIdentity] = overflow;
            }

            overflow.CoverageLimited = true;
            return overflow;
        }

        var created = new PetAccumulator(normalized, actor.DisplayName) { CoverageLimited = true };
        _pets[normalized] = created;
        return created;
    }

    private void ApplyDamageType(CanonicalCombatEvent canonicalEvent,
        Dictionary<string, DamageTypeAccumulator> totals, EventFacets direction)
    {
        if (!canonicalEvent.Facets.HasFlag(direction)
            || !IsOwnerActor(canonicalEvent.Actor.Type))
        {
            return;
        }

        if (canonicalEvent.DamageType is not { } damageType)
        {
            if (direction == EventFacets.DamageDealt) _missingOutgoingType = true;
            else _missingIncomingType = true;
            return;
        }

        var key = DamageTypeKey(damageType);
        if (!totals.TryGetValue(key, out var accumulator))
        {
            if (totals.Count >= MaxTrackedDamageTypes)
            {
                _coverageLimited = true;
                if (!totals.TryGetValue(OverflowIdentity, out accumulator))
                {
                    accumulator = new DamageTypeAccumulator(new DamageType(OverflowBucketKey))
                    {
                        IsOverflow = true
                    };
                    totals[OverflowIdentity] = accumulator;
                }
            }
            else
            {
                accumulator = new DamageTypeAccumulator(damageType);
                totals[key] = accumulator;
            }
        }

        accumulator.Amount = AddMagnitude(accumulator.Amount, canonicalEvent.Amount);
        accumulator.EventCount++;
    }

    private void ApplyOutgoingTarget(CanonicalCombatEvent canonicalEvent)
    {
        if (!canonicalEvent.Facets.HasFlag(EventFacets.DamageDealt)
            || !IsOwnerActor(canonicalEvent.Actor.Type))
        {
            return;
        }

        if (!TryResolveTargetKey(canonicalEvent, out var normalized, out var displayName))
        {
            _missingTarget = true;
            return;
        }

        if (!_targets.TryGetValue(normalized, out var accumulator))
        {
            if (_targets.Count >= MaxTrackedTargets)
            {
                _coverageLimited = true;
                if (!_targets.TryGetValue(OverflowIdentity, out accumulator))
                {
                    accumulator = new TargetAccumulator(OverflowBucketKey, OverflowBucketKey)
                    {
                        IsOverflow = true
                    };
                    _targets[OverflowIdentity] = accumulator;
                }
            }
            else
            {
                accumulator = new TargetAccumulator(normalized, displayName);
                _targets[normalized] = accumulator;
            }
        }

        accumulator.DamageDealt = AddMagnitude(accumulator.DamageDealt, canonicalEvent.Amount);
        accumulator.EventCount++;
    }

    private bool TryResolvePowerCubeRead(CanonicalCombatEvent canonicalEvent, out PowerAccumulator power)
    {
        power = null!;
        if (string.IsNullOrWhiteSpace(canonicalEvent.PowerName))
        {
            return false;
        }

        if (canonicalEvent.Actor.Type == ActorType.Self)
        {
            return _powers.TryGetValue(
                new PowerCubeKey(CombatAnalyticsScope.Self, canonicalEvent.PowerName, null),
                out power!);
        }

        if (canonicalEvent.Actor.Type != ActorType.OwnPet)
        {
            return false;
        }

        var petName = ResolvePetNormalizedName(canonicalEvent.Actor);
        return petName is not null
            && _powers.TryGetValue(
                new PowerCubeKey(CombatAnalyticsScope.PerPet, canonicalEvent.PowerName, petName),
                out power!);
    }

    private static bool TryResolveTargetKey(
        CanonicalCombatEvent canonicalEvent,
        out string normalized,
        out string? displayName)
    {
        normalized = null!;
        displayName = null;
        var target = canonicalEvent.Target;
        if (target is null
            || target.Type is ActorType.Environment
            || string.IsNullOrWhiteSpace(target.DisplayName)
            || canonicalEvent.Family is CombatEventFamily.EnvironmentDamage
                or CombatEventFamily.RechargeCandidate)
        {
            return false;
        }

        displayName = target.DisplayName;
        normalized = CharacterNameNormalizer.Normalize(target.DisplayName);
        return true;
    }

    private static string? ResolvePetNormalizedName(ActorRef actor)
    {
        if (actor.PetKey is { NormalizedPetName: { Length: > 0 } fromKey })
        {
            return fromKey;
        }

        return string.IsNullOrWhiteSpace(actor.DisplayName)
            ? null
            : CharacterNameNormalizer.Normalize(actor.DisplayName);
    }

    private static bool IsOwnerActor(ActorType type) => type is ActorType.Self or ActorType.OwnPet;

    private static CombatScaledAmount AddMagnitude(CombatScaledAmount left, CombatScaledAmount right) =>
        new(checked(left.Hundredths + right.Hundredths));

    private static bool HasIncomingPerspective(CanonicalCombatEvent item) =>
        (item.Facets & (EventFacets.DamageReceived | EventFacets.HealReceived | EventFacets.EnduranceGrantReceived)) != 0
        || (item.Family == CombatEventFamily.AttackResolution && !IsOwnerActor(item.Actor.Type));

    private static bool HasOutgoingPerspective(CanonicalCombatEvent item) =>
        (item.Facets & (EventFacets.DamageDealt | EventFacets.HealDelivered | EventFacets.EnduranceGrantDealt
            | EventFacets.Activation | EventFacets.Defeat | EventFacets.Mez | EventFacets.Knock)) != 0
        || (item.Family == CombatEventFamily.AttackResolution && IsOwnerActor(item.Actor.Type));

    private static ActorRef? OutgoingActor(CanonicalCombatEvent item) =>
        item.Family is CombatEventFamily.HealReceived or CombatEventFamily.EnduranceGrantReceived
            ? item.Target : item.Actor;

    private static ActorRef? IncomingActor(CanonicalCombatEvent item) =>
        item.Family is CombatEventFamily.HealDealt or CombatEventFamily.EnduranceGrantDealt
            ? item.Actor : item.Target;

    private static string DamageTypeKey(DamageType damageType) =>
        string.Concat(
            damageType.Text,
            "\0",
            damageType.IsUnresistable ? "1" : "0",
            "\0",
            damageType.IsUnique ? "1" : "0");

    private void NoteObserved(CanonicalCombatEvent item)
    {
        // Observation time is local ingestion time. Sequence only breaks ties within its scope.
        if (_firstObservation is null || item.ObservedAt < _firstObservation.ObservedAt
            || (item.ObservedAt == _firstObservation.ObservedAt && SameSourceScope(item, _firstObservation)
                && item.Provenance.ParserSequence < _firstObservation.Provenance.ParserSequence))
            _firstObservation = item;
        if (_lastObservation is null || item.ObservedAt > _lastObservation.ObservedAt
            || (item.ObservedAt == _lastObservation.ObservedAt
                && (!SameSourceScope(item, _lastObservation)
                    || item.Provenance.ParserSequence > _lastObservation.Provenance.ParserSequence)))
            _lastObservation = item;
    }

    private static bool SameSourceScope(CanonicalCombatEvent a, CanonicalCombatEvent b) =>
        a.Provenance.ContextId == b.Provenance.ContextId
        && a.Provenance.SourceId == b.Provenance.SourceId
        && a.Provenance.AccountStableId == b.Provenance.AccountStableId
        && a.Provenance.SourceSegmentId == b.Provenance.SourceSegmentId
        && a.Provenance.BindingGeneration == b.Provenance.BindingGeneration;

    private SegmentClock BuildClock(SegmentClockCapture? capture)
    {
        var wall = Metric<TimeSpan>.NotCaptured();
        var end = capture?.CaptureEndUtc ?? capture?.AsOfUtc;
        if (capture?.CaptureStartUtc is { } start && end is { } bound && bound >= start)
            wall = Metric<TimeSpan>.Available(bound - start, MetricEvidence.DerivedFromObserved,
                denominator: RateDenominatorKind.WallClock);
        var span = _firstObservation is { } first && _lastObservation is { } last
            ? Metric<TimeSpan>.Available(last.ObservedAt - first.ObservedAt, MetricEvidence.DerivedFromObserved)
            : Metric<TimeSpan>.NotCaptured();
        var tracked = capture?.TrackedPauseAdjustedDuration is { } duration && duration >= TimeSpan.Zero
            ? Metric<TimeSpan>.Available(duration, MetricEvidence.DerivedFromObserved,
                denominator: RateDenominatorKind.TrackedPauseAdjusted)
            : Metric<TimeSpan>.NotCaptured();
        return new SegmentClock
        {
            CaptureStartUtc = capture?.CaptureStartUtc,
            CaptureEndUtc = capture?.CaptureEndUtc,
            AsOfUtc = capture?.CaptureEndUtc ?? capture?.AsOfUtc,
            FirstObservation = _firstObservation?.Provenance,
            LastObservation = _lastObservation?.Provenance,
            WallClockDuration = wall,
            ObservedAnalyticalSpan = span,
            TrackedPauseAdjustedDuration = tracked
        };
    }

    private static SegmentClock AttachRateMetrics(SegmentClock clock, CombatSessionMetricSet metrics)
    {
        var rate = Metric<long>.NotCaptured();
        if (clock.WallClockDuration.Value is { } wall && wall.Ticks >= TimeSpan.TicksPerMillisecond
            && metrics.DamageDealt.HasCompleteValue && metrics.DamageDealt.Value is { } dealt)
        {
            // Tick precision avoids denominator truncation; Int128 avoids intermediate overflow.
            var hundredths = (Int128)dealt.Hundredths * TimeSpan.TicksPerSecond / wall.Ticks;
            if (hundredths <= long.MaxValue)
                rate = Metric<long>.Available((long)hundredths, MetricEvidence.DerivedFromObserved,
                    denominator: RateDenominatorKind.WallClock);
        }
        return clock with { WallClockDamagePerSecondHundredths = rate };
    }

    private CombatSessionMetricSet BuildSessionMetrics(CombatAccuracyScopeSnapshot accuracy)
    {
        var petCoverage = new CoverageInfo { PetNameRollup = true };
        var targetOverflow = _targets.Values.Any(item => item.IsOverflow);
        var namedTargets = _targets.Values.Count(item => !item.IsOverflow);
        var metrics = CombatSessionMetricSet.Baseline() with
        {
            DamageDealt = ObservedAmount(_session.ObservedDamageDealt, _session.DamageDealt),
            DamageDealtSelf = ObservedAmount(_session.ObservedDamageDealtSelf, _session.DamageDealtSelf),
            DamageDealtOwnedPets = ObservedAmount(
                _session.ObservedDamageDealtOwnedPets,
                _session.DamageDealtOwnedPets,
                coverage: petCoverage),
            DamageReceived = ObservedAmount(_session.ObservedDamageReceived, _session.DamageReceived),
            DamageReceivedOwnedPets = ObservedAmount(
                _session.ObservedDamageReceivedOwnedPets,
                _session.DamageReceivedOwnedPets,
                coverage: petCoverage),
            HealingDealt = ObservedAmount(_session.ObservedHealingDealt, _session.HealingDealt),
            HealingReceived = ObservedAmount(_session.ObservedHealingReceived, _session.HealingReceived),
            EnduranceGranted = ObservedAmount(_session.ObservedEnduranceGranted, _session.EnduranceGranted),
            EnduranceReceived = ObservedAmount(_session.ObservedEnduranceReceived, _session.EnduranceReceived),
            DamageEventCount = ObservedCount(_session.DamageEventCount > 0, _session.DamageEventCount),
            ActivationCount = ObservedCount(_session.ObservedActivation, _session.ActivationCount),
            AttackResolutionCount = ObservedCount(_session.ObservedAttackResolution, _session.AttackResolutionCount),
            ConfirmedRechargeCompletedCount = _session.ObservedConfirmedRecharge
                ? Metric<long>.Available(_session.ConfirmedRechargeCompletedCount, MetricEvidence.DerivedFromObserved)
                : Metric<long>.NotCaptured(),
            ConfirmedStillRechargingCount = _session.ObservedConfirmedStillRecharging
                ? Metric<long>.Available(_session.ConfirmedStillRechargingCount, MetricEvidence.DerivedFromObserved)
                : Metric<long>.NotCaptured(),
            UnmatchedRechargeCandidateCount = ObservedCount(
                _session.ObservedUnmatchedRecharge,
                _session.UnmatchedRechargeCandidateCount),
            DistinctTargetCount = targetOverflow || _missingTarget
                ? Metric<long>.Incomplete(
                    namedTargets,
                    coverage: new CoverageInfo { Overflow = targetOverflow, MissingTarget = _missingTarget, LowerBound = true })
                : ObservedCount(namedTargets > 0, namedTargets),
            Accuracy = accuracy.Attempts > 0 || accuracy.Autohits > 0
                ? MetricRef<CombatAccuracyScopeSnapshot>.Available(accuracy)
                : MetricRef<CombatAccuracyScopeSnapshot>.NotCaptured(),
            CompanionMissResolutionCount = Metric<long>.Unsupported()
        };

        return metrics;
    }

    private static MetricRef<IReadOnlyList<CombatDamageTypeTotal>> TypeBreakdown(
        List<CombatDamageTypeTotal> rows, bool missing)
    {
        var coverage = new CoverageInfo { MissingDamageType = missing, Overflow = rows.Any(row => row.IsOverflow) };
        var frozen = Freeze(rows.OrderBy(row => row.IsOverflow).ThenBy(row => row.DamageType.Text, StringComparer.Ordinal).ToList());
        return coverage.IsPartial
            ? MetricRef<IReadOnlyList<CombatDamageTypeTotal>>.Incomplete(frozen, coverage)
            : rows.Count > 0 ? MetricRef<IReadOnlyList<CombatDamageTypeTotal>>.Available(frozen)
            : MetricRef<IReadOnlyList<CombatDamageTypeTotal>>.NotCaptured();
    }

    private static Metric<CombatScaledAmount> ObservedAmount(
        bool observed,
        CombatScaledAmount value,
        CoverageInfo? coverage = null)
    {
        if (!observed)
        {
            return Metric<CombatScaledAmount>.NotCaptured();
        }

        return Metric<CombatScaledAmount>.Available(value, coverage: coverage);
    }

    private static Metric<long> ObservedCount(bool observed, long value) =>
        observed ? Metric<long>.Available(value) : Metric<long>.NotCaptured();

    private static IReadOnlyList<T> Freeze<T>(List<T> items) =>
        items.Count == 0 ? Array.Empty<T>() : Array.AsReadOnly(items.ToArray());

    private readonly record struct PowerCubeKey(
        CombatAnalyticsScope Scope,
        string PowerName,
        string? PetNormalizedName,
        CombatAnalyticsDirection Direction = CombatAnalyticsDirection.Outgoing);

    private readonly record struct ConfirmedPowerKey(
        string PowerName,
        MonitoringContextId ContextId,
        string SourceId,
        string AccountStableId,
        Guid SourceSegmentId,
        long BindingGeneration)
    {
        public static ConfirmedPowerKey From(CanonicalCombatEvent canonicalEvent) =>
            new(
                canonicalEvent.PowerName ?? string.Empty,
                canonicalEvent.Provenance.ContextId,
                canonicalEvent.Provenance.SourceId,
                canonicalEvent.Provenance.AccountStableId,
                canonicalEvent.Provenance.SourceSegmentId,
                canonicalEvent.Provenance.BindingGeneration);
    }

    private struct SessionAccumulator
    {
        public CombatScaledAmount DamageDealt;
        public CombatScaledAmount DamageDealtSelf;
        public CombatScaledAmount DamageDealtOwnedPets;
        public CombatScaledAmount DamageReceived;
        public CombatScaledAmount DamageReceivedOwnedPets;
        public CombatScaledAmount HealingDealt;
        public CombatScaledAmount HealingDealtSelf;
        public CombatScaledAmount HealingDealtOwnedPets;
        public CombatScaledAmount HealingReceived;
        public CombatScaledAmount HealingReceivedOwnedPets;
        public CombatScaledAmount EnduranceGranted;
        public CombatScaledAmount EnduranceGrantedSelf;
        public CombatScaledAmount EnduranceGrantedOwnedPets;
        public CombatScaledAmount EnduranceReceived;
        public CombatScaledAmount EnduranceReceivedOwnedPets;
        public long DamageEventCount;
        public long HealEventCount;
        public long EnduranceEventCount;
        public long ActivationCount;
        public long AttackResolutionCount;
        public long DefeatCount;
        public long MyDefeatCount;
        public long MezCount;
        public long KnockCount;
        public long ConfirmedRechargeCompletedCount;
        public long ConfirmedStillRechargingCount;
        public long UnmatchedRechargeCandidateCount;
        public bool ObservedDamageDealt;
        public bool ObservedDamageDealtSelf;
        public bool ObservedDamageDealtOwnedPets;
        public bool ObservedDamageReceived;
        public bool ObservedDamageReceivedOwnedPets;
        public bool ObservedHealingDealt;
        public bool ObservedHealingReceived;
        public bool ObservedEnduranceGranted;
        public bool ObservedEnduranceReceived;
        public bool ObservedActivation;
        public bool ObservedAttackResolution;
        public bool ObservedConfirmedRecharge;
        public bool ObservedConfirmedStillRecharging;
        public bool ObservedUnmatchedRecharge;

        public CombatSessionSummary Project(
            CombatAccuracyScopeSnapshot accuracy,
            bool coverageLimited,
            CombatSessionMetricSet metrics) =>
            new()
            {
                DamageDealt = DamageDealt,
                DamageDealtSelf = DamageDealtSelf,
                DamageDealtOwnedPets = DamageDealtOwnedPets,
                DamageReceived = DamageReceived,
                DamageReceivedOwnedPets = DamageReceivedOwnedPets,
                HealingDealt = HealingDealt,
                HealingDealtSelf = HealingDealtSelf,
                HealingDealtOwnedPets = HealingDealtOwnedPets,
                HealingReceived = HealingReceived,
                HealingReceivedOwnedPets = HealingReceivedOwnedPets,
                EnduranceGranted = EnduranceGranted,
                EnduranceGrantedSelf = EnduranceGrantedSelf,
                EnduranceGrantedOwnedPets = EnduranceGrantedOwnedPets,
                EnduranceReceived = EnduranceReceived,
                EnduranceReceivedOwnedPets = EnduranceReceivedOwnedPets,
                DamageEventCount = DamageEventCount,
                HealEventCount = HealEventCount,
                EnduranceEventCount = EnduranceEventCount,
                ActivationCount = ActivationCount,
                AttackResolutionCount = AttackResolutionCount,
                DefeatCount = DefeatCount,
                MyDefeatCount = MyDefeatCount,
                MezCount = MezCount,
                KnockCount = KnockCount,
                ConfirmedRechargeCompletedCount = ConfirmedRechargeCompletedCount,
                ConfirmedStillRechargingCount = ConfirmedStillRechargingCount,
                UnmatchedRechargeCandidateCount = UnmatchedRechargeCandidateCount,
                Accuracy = accuracy,
                Metrics = metrics,
                CoverageLimited = coverageLimited
            };
    }

    private sealed class PowerAccumulator
    {
        private readonly Dictionary<string, DamageTypeAccumulator> _damageTypes = [];
        private readonly HashSet<string> _targets = new(StringComparer.OrdinalIgnoreCase);
        private bool _targetOverflow;
        private bool _missingTarget;
        private bool _missingType;
        private bool _observedDamage;
        private bool _observedHealing;
        private bool _observedEndurance;

        public PowerAccumulator(
            CombatAnalyticsScope scope,
            string powerName,
            string? petNormalizedName,
            string? petDisplayName, CombatAnalyticsDirection direction)
        {
            Scope = scope;
            PowerName = powerName;
            PetNormalizedName = petNormalizedName;
            PetDisplayName = petDisplayName;
            Direction = direction;
        }

        public CombatAnalyticsScope Scope { get; }
        public CombatAnalyticsDirection Direction { get; }

        public string PowerName { get; }

        public string? PetNormalizedName { get; }

        public string? PetDisplayName { get; }

        public CombatScaledAmount? TotalMagnitude { get; private set; } = CombatScaledAmount.Zero;
        public MagnitudeKind? TotalMagnitudeKind { get; private set; } = MagnitudeKind.None;

        public CombatScaledAmount DamageMagnitude { get; private set; }

        public CombatScaledAmount HealingMagnitude { get; private set; }

        public CombatScaledAmount EnduranceMagnitude { get; private set; }

        public long EventCount { get; private set; }

        public long HitResolutionCount { get; private set; }

        public long ActivationCount { get; private set; }

        public CombatScaledAmount? LargestHit { get; private set; }

        public CombatScaledAmount DirectAmount { get; private set; }

        public CombatScaledAmount DotAmount { get; private set; }

        public long ConfirmedRechargeCompletedCount { get; set; }

        public long ConfirmedStillRechargingCount { get; set; }

        public bool IsOverflow { get; init; }

        public bool CoverageLimited { get; set; }

        public void Apply(CanonicalCombatEvent canonicalEvent)
        {
            if (canonicalEvent.Facets.HasFlag(EventFacets.Activation))
            {
                ActivationCount++;
            }

            if (canonicalEvent.Facets.HasFlag(EventFacets.AttackResolution))
            {
                HitResolutionCount++;
            }

            if (canonicalEvent.Magnitude == MagnitudeKind.None)
            {
                return;
            }

            EventCount++;
            if (TotalMagnitudeKind == MagnitudeKind.None) TotalMagnitudeKind = canonicalEvent.Magnitude;
            if (TotalMagnitudeKind != canonicalEvent.Magnitude)
            {
                TotalMagnitudeKind = null;
                TotalMagnitude = null;
            }
            else if (TotalMagnitude is { } total) TotalMagnitude = AddMagnitude(total, canonicalEvent.Amount);
            if (canonicalEvent.Magnitude == MagnitudeKind.HitPoints)
            {
                if (canonicalEvent.Facets.HasFlag(EventFacets.DamageDealt)
                    || canonicalEvent.Facets.HasFlag(EventFacets.DamageReceived))
                {
                    if (LargestHit is not { } current || canonicalEvent.Amount.Hundredths > current.Hundredths)
                        LargestHit = canonicalEvent.Amount;
                    if (canonicalEvent.Delivery.HasFlag(DeliveryFlags.DoT)) DotAmount = AddMagnitude(DotAmount, canonicalEvent.Amount);
                    else DirectAmount = AddMagnitude(DirectAmount, canonicalEvent.Amount);
                    _observedDamage = true;
                    DamageMagnitude = AddMagnitude(DamageMagnitude, canonicalEvent.Amount);
                    if (canonicalEvent.DamageType is { } damageType)
                    {
                        NoteDamageType(damageType, canonicalEvent.Amount);
                    }
                    else _missingType = true;
                    if (Direction == CombatAnalyticsDirection.Outgoing)
                    {
                        if (TryResolveTargetKey(canonicalEvent, out var normalized, out _)) NoteTarget(normalized);
                        else _missingTarget = true;
                    }
                }

                if (canonicalEvent.Facets.HasFlag(EventFacets.HealDelivered)
                    || canonicalEvent.Facets.HasFlag(EventFacets.HealReceived))
                {
                    _observedHealing = true;
                    HealingMagnitude = AddMagnitude(HealingMagnitude, canonicalEvent.Amount);
                }
            }
            else if (canonicalEvent.Magnitude == MagnitudeKind.Endurance)
            {
                _observedEndurance = true;
                EnduranceMagnitude = AddMagnitude(EnduranceMagnitude, canonicalEvent.Amount);
            }
        }

        public void NoteTarget(string normalizedTargetName)
        {
            if (_targets.Count >= MaxTrackedTargets && !_targets.Contains(normalizedTargetName))
            {
                CoverageLimited = true;
                _targetOverflow = true;
                return;
            }

            _targets.Add(normalizedTargetName);
        }

        public CombatPowerAnalysisRow Project() =>
            new()
            {
                Scope = Scope,
                Direction = Direction,
                PowerName = PowerName,
                PetNormalizedName = PetNormalizedName,
                PetDisplayName = PetDisplayName,
                TotalMagnitude = TotalMagnitude,
                TotalMagnitudeKind = TotalMagnitudeKind,
                DamageMagnitude = DamageMagnitude,
                HealingMagnitude = HealingMagnitude,
                EnduranceMagnitude = EnduranceMagnitude,
                EventCount = EventCount,
                HitResolutionCount = HitResolutionCount,
                ActivationCount = ActivationCount,
                LargestHit = LargestHit,
                DirectAmount = DirectAmount,
                DotAmount = DotAmount,
                DamageTypes = Freeze(
                    _damageTypes.Values
                        .Select(item => item.Project())
                        .OrderBy(row => row.DamageType.Text, StringComparer.Ordinal)
                        .ToList()),
                DistinctTargetCount = _targets.Count,
                TotalMagnitudeMetric = TotalMagnitude is null
                    ? Metric<CombatScaledAmount>.Unsupported()
                    : TotalMagnitudeKind == MagnitudeKind.None ? Metric<CombatScaledAmount>.NotCaptured()
                    : Metric<CombatScaledAmount>.Available(TotalMagnitude.Value),
                DamageMagnitudeMetric = ObservedAmount(_observedDamage, DamageMagnitude),
                HealingMagnitudeMetric = ObservedAmount(_observedHealing, HealingMagnitude),
                EnduranceMagnitudeMetric = ObservedAmount(_observedEndurance, EnduranceMagnitude),
                DamageTypeBreakdown = TypeBreakdown(_damageTypes.Values.Select(item => item.Project()).ToList(), _missingType),
                DistinctTargetCountMetric = _targetOverflow || _missingTarget
                    ? Metric<long>.Incomplete(
                        _targets.Count,
                        coverage: new CoverageInfo { Overflow = _targetOverflow, MissingTarget = _missingTarget, LowerBound = true })
                    : ObservedCount(_targets.Count > 0, _targets.Count),
                ConfirmedRechargeCompletedCount = ConfirmedRechargeCompletedCount,
                ConfirmedStillRechargingCount = ConfirmedStillRechargingCount,
                IsOverflow = IsOverflow,
                CoverageLimited = CoverageLimited
            };

        private void NoteDamageType(DamageType damageType, CombatScaledAmount amount)
        {
            var key = DamageTypeKey(damageType);
            if (!_damageTypes.TryGetValue(key, out var accumulator))
            {
                if (_damageTypes.Count >= MaxTrackedDamageTypes)
                {
                    CoverageLimited = true;
                    if (!_damageTypes.TryGetValue(OverflowIdentity, out accumulator))
                    {
                        accumulator = new DamageTypeAccumulator(new DamageType(OverflowBucketKey))
                        {
                            IsOverflow = true
                        };
                        _damageTypes[OverflowIdentity] = accumulator;
                    }
                }
                else
                {
                    accumulator = new DamageTypeAccumulator(damageType);
                    _damageTypes[key] = accumulator;
                }
            }

            accumulator.Amount = AddMagnitude(accumulator.Amount, amount);
            accumulator.EventCount++;
        }
    }

    private sealed class DamageTypeAccumulator
    {
        public DamageTypeAccumulator(DamageType damageType) => DamageType = damageType;

        public DamageType DamageType { get; }

        public CombatScaledAmount Amount { get; set; }

        public long EventCount { get; set; }

        public bool IsOverflow { get; init; }

        public CombatDamageTypeTotal Project() =>
            new()
            {
                DamageType = DamageType,
                Amount = Amount,
                EventCount = EventCount,
                IsOverflow = IsOverflow
            };
    }

    private sealed class TargetAccumulator
    {
        public TargetAccumulator(string normalizedTargetName, string? displayName)
        {
            NormalizedTargetName = normalizedTargetName;
            DisplayName = displayName;
        }

        public string NormalizedTargetName { get; }

        public string? DisplayName { get; }

        public CombatScaledAmount DamageDealt { get; set; }

        public long EventCount { get; set; }

        public bool IsOverflow { get; init; }

        public CombatTargetSummary Project() =>
            new()
            {
                NormalizedTargetName = NormalizedTargetName,
                DisplayName = DisplayName,
                DamageDealt = DamageDealt,
                EventCount = EventCount,
                IsOverflow = IsOverflow
            };
    }

    private sealed class ActorAccumulator
    {
        public CombatScaledAmount DamageDealt { get; private set; }

        public CombatScaledAmount DamageReceived { get; private set; }

        public CombatScaledAmount HealingDealt { get; private set; }

        public CombatScaledAmount HealingReceived { get; private set; }

        public CombatScaledAmount EnduranceGranted { get; private set; }

        public CombatScaledAmount EnduranceReceived { get; private set; }

        public long ActivationCount { get; private set; }

        public void ApplyOutgoing(CanonicalCombatEvent canonicalEvent)
        {
            var facets = canonicalEvent.Facets;
            var amount = canonicalEvent.Amount;
            if (facets.HasFlag(EventFacets.DamageDealt))
            {
                DamageDealt = AddMagnitude(DamageDealt, amount);
            }

            if (facets.HasFlag(EventFacets.HealDelivered))
            {
                HealingDealt = AddMagnitude(HealingDealt, amount);
            }

            if (facets.HasFlag(EventFacets.EnduranceGrantDealt))
            {
                EnduranceGranted = AddMagnitude(EnduranceGranted, amount);
            }

            if (facets.HasFlag(EventFacets.Activation))
            {
                ActivationCount++;
            }
        }

        public void ApplyIncoming(CanonicalCombatEvent canonicalEvent)
        {
            var facets = canonicalEvent.Facets;
            var amount = canonicalEvent.Amount;
            if (facets.HasFlag(EventFacets.DamageReceived))
            {
                DamageReceived = AddMagnitude(DamageReceived, amount);
            }

            if (facets.HasFlag(EventFacets.HealReceived))
            {
                HealingReceived = AddMagnitude(HealingReceived, amount);
            }

            if (facets.HasFlag(EventFacets.EnduranceGrantReceived))
            {
                EnduranceReceived = AddMagnitude(EnduranceReceived, amount);
            }
        }

        public CombatActorSummary Project(
            CombatAnalyticsScope scope,
            string? petNormalizedName,
            string? petDisplayName,
            CombatAccuracyScopeSnapshot accuracy,
            bool coverageLimited = false,
            bool isOverflow = false) =>
            new()
            {
                Scope = scope,
                PetNormalizedName = petNormalizedName,
                PetDisplayName = petDisplayName,
                DamageDealt = DamageDealt,
                DamageReceived = DamageReceived,
                HealingDealt = HealingDealt,
                HealingReceived = HealingReceived,
                EnduranceGranted = EnduranceGranted,
                EnduranceReceived = EnduranceReceived,
                ActivationCount = ActivationCount,
                Accuracy = accuracy,
                CoverageLimited = coverageLimited,
                IsOverflow = isOverflow
            };
    }

    private sealed class PetAccumulator
    {
        private readonly ActorAccumulator _actor = new();

        public PetAccumulator(string normalizedName, string? displayName)
        {
            NormalizedName = normalizedName;
            DisplayName = displayName;
        }

        public string NormalizedName { get; }

        public string? DisplayName { get; }

        public CombatAccuracyAccumulator Accuracy { get; } = new();

        public bool CoverageLimited { get; set; }

        public bool IsOverflow { get; init; }

        public void ApplyOutgoing(CanonicalCombatEvent canonicalEvent) => _actor.ApplyOutgoing(canonicalEvent);

        public void ApplyIncoming(CanonicalCombatEvent canonicalEvent) => _actor.ApplyIncoming(canonicalEvent);

        public CombatActorSummary Project() =>
            _actor.Project(
                CombatAnalyticsScope.PerPet,
                NormalizedName,
                DisplayName,
                Accuracy.ToSnapshot(),
                CoverageLimited,
                IsOverflow);
    }
}
