using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

/// <summary>
/// Mutable session-scope combat accumulator. Consumes only normalized <see cref="CombatEvent"/> values.
/// </summary>
public sealed class CombatAggregator
{
    public TrackedCombatAccumulator Tracked { get; } = new();

    public RollingCombatAccumulator Rolling { get; } = new();
    /// <summary>
    /// Event kinds that refresh combat activity. All current semantic combat kinds except reserved
    /// <see cref="CombatEventKind.AttackResolution"/> and <see cref="CombatEventKind.Unparsed"/>.
    /// </summary>
    public static readonly CombatEventKind[] ActivityEventKinds =
    [
        CombatEventKind.DamageDealt,
        CombatEventKind.DamageReceived,
        CombatEventKind.HealingDealt,
        CombatEventKind.HealingReceived,
        CombatEventKind.PowerActivation,
        CombatEventKind.Defeat
    ];

    private static readonly HashSet<CombatEventKind> ActivityEventKindSet = ActivityEventKinds.ToHashSet();

    private CombatScaledAmount _damageDealt;
    private CombatScaledAmount _damageReceived;
    private CombatScaledAmount _healingDealt;
    private CombatScaledAmount _healingReceived;
    private long _totalDefeated;
    private long _myDefeats;
    private long _powerActivations;
    private readonly CombatAccuracyAccumulator _accuracy = new();
    private DateTimeOffset? _lastCombatAt;
    private DateTimeOffset? _engagementStartedAt;
    private bool _frozen;

    public long EventsApplied { get; private set; }

    public void Apply(CombatEvent combatEvent)
    {
        if (_frozen)
        {
            return;
        }

        EventsApplied++;

        switch (combatEvent.Kind)
        {
            case CombatEventKind.DamageDealt:
                _damageDealt += combatEvent.Amount;
                break;
            case CombatEventKind.DamageReceived:
                _damageReceived += combatEvent.Amount;
                break;
            case CombatEventKind.HealingDealt:
                _healingDealt += combatEvent.Amount;
                break;
            case CombatEventKind.HealingReceived:
                _healingReceived += combatEvent.Amount;
                break;
            case CombatEventKind.Defeat:
                _totalDefeated++;
                if (combatEvent.ActorRole == CombatActorRole.Self)
                {
                    _myDefeats++;
                }
                break;
            case CombatEventKind.PowerActivation:
                _powerActivations++;
                break;
            case CombatEventKind.AttackResolution:
                _accuracy.Apply(combatEvent);
                break;
        }

        if (ActivityEventKindSet.Contains(combatEvent.Kind))
        {
            RefreshActivityLocked(combatEvent.ObservedAt);
        }

        Rolling.Apply(combatEvent);
    }

    public void Freeze(DateTimeOffset frozenAt)
    {
        _frozen = true;
        _lastCombatAt ??= frozenAt;
        Rolling.Freeze();
    }

    public CombatSnapshot ToSnapshot(
        DateTimeOffset sessionStartedAt,
        DateTimeOffset referenceAt,
        DateTimeOffset? timingEndAt,
        TimeSpan idleThreshold)
    {
        var isInCombat = !_frozen
            && _lastCombatAt is not null
            && referenceAt - _lastCombatAt.Value < idleThreshold;

        var engagementDuration = TimeSpan.Zero;
        if (isInCombat && _engagementStartedAt is not null)
        {
            engagementDuration = referenceAt - _engagementStartedAt.Value;
            if (engagementDuration < TimeSpan.Zero)
            {
                engagementDuration = TimeSpan.Zero;
            }
        }

        var elapsed = GameplaySessionTelemetryPresentation.GetElapsedDuration(
            sessionStartedAt,
            referenceAt,
            timingEndAt);

        return new CombatSnapshot
        {
            DamageDealt = _damageDealt,
            DamageReceived = _damageReceived,
            HealingDealt = _healingDealt,
            HealingReceived = _healingReceived,
            TotalDefeated = _totalDefeated,
            MyDefeats = _myDefeats,
            PowerActivations = _powerActivations,
            IsInCombat = isInCombat,
            LastCombatAt = _lastCombatAt,
            CurrentEngagementDuration = engagementDuration,
            SessionDamagePerSecondHundredths = CalculateDamagePerSecondHundredths(_damageDealt, elapsed),
            Tracked = Tracked.ToSnapshot(referenceAt),
            Rolling = Rolling.ToSnapshot(sessionStartedAt, referenceAt, timingEndAt),
            Accuracy = _accuracy.ToSnapshot()
        };
    }

    internal static long CalculateDamagePerSecondHundredths(
        CombatScaledAmount damageDealt,
        TimeSpan elapsed)
    {
        if (elapsed <= TimeSpan.Zero || damageDealt.Hundredths <= 0)
        {
            return 0;
        }

        return damageDealt.Hundredths * 1000 / (long)elapsed.TotalMilliseconds;
    }

    internal static long CalculateSessionDamagePerSecondHundredths(
        CombatScaledAmount damageDealt,
        TimeSpan elapsed) => CalculateDamagePerSecondHundredths(damageDealt, elapsed);

    private void RefreshActivityLocked(DateTimeOffset observedAt)
    {
        if (_lastCombatAt is not null
            && observedAt - _lastCombatAt.Value >= CombatActivityDefaults.IdleThreshold)
        {
            _engagementStartedAt = observedAt;
        }
        else
        {
            _engagementStartedAt ??= observedAt;
        }

        _lastCombatAt = observedAt;
    }
}
