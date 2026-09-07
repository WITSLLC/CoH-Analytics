using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

/// <summary>
/// Mutable tracked-scope combat accumulator with explicit start/pause/resume/stop lifecycle.
/// </summary>
public sealed class TrackedCombatAccumulator
{
    private CombatScaledAmount _damageDealt;
    private CombatScaledAmount _damageReceived;
    private CombatScaledAmount _healingDealt;
    private CombatScaledAmount _healingReceived;
    private long _totalDefeated;
    private long _myDefeats;
    private long _powerActivations;
    private readonly CombatAccuracyAccumulator _accuracy = new();
    private DateTimeOffset? _startedAt;
    private TimeSpan _accumulatedPauseDuration;
    private DateTimeOffset? _pauseStartedAt;
    private DateTimeOffset? _frozenAt;
    private TrackedCombatLifecycleState _state = TrackedCombatLifecycleState.Idle;

    public TrackedCombatLifecycleState State => _state;

    public void Start(DateTimeOffset startedAt)
    {
        Reset();
        _startedAt = startedAt;
        _state = TrackedCombatLifecycleState.Running;
    }

    public void Pause(DateTimeOffset pausedAt)
    {
        if (_state != TrackedCombatLifecycleState.Running)
        {
            return;
        }

        _pauseStartedAt = pausedAt;
        _state = TrackedCombatLifecycleState.Paused;
    }

    public void Resume(DateTimeOffset resumedAt)
    {
        if (_state != TrackedCombatLifecycleState.Paused || _pauseStartedAt is null)
        {
            return;
        }

        _accumulatedPauseDuration += resumedAt - _pauseStartedAt.Value;
        _pauseStartedAt = null;
        _state = TrackedCombatLifecycleState.Running;
    }

    public void Stop(DateTimeOffset stoppedAt)
    {
        if (_state == TrackedCombatLifecycleState.Idle)
        {
            return;
        }

        if (_state == TrackedCombatLifecycleState.Paused && _pauseStartedAt is not null)
        {
            _accumulatedPauseDuration += stoppedAt - _pauseStartedAt.Value;
            _pauseStartedAt = null;
        }

        _frozenAt = stoppedAt;
        _state = TrackedCombatLifecycleState.Frozen;
    }

    public void Reset()
    {
        _damageDealt = CombatScaledAmount.Zero;
        _damageReceived = CombatScaledAmount.Zero;
        _healingDealt = CombatScaledAmount.Zero;
        _healingReceived = CombatScaledAmount.Zero;
        _totalDefeated = 0;
        _myDefeats = 0;
        _powerActivations = 0;
        _accuracy.Reset();
        _startedAt = null;
        _accumulatedPauseDuration = TimeSpan.Zero;
        _pauseStartedAt = null;
        _frozenAt = null;
        _state = TrackedCombatLifecycleState.Idle;
    }

    public void Apply(CombatEvent combatEvent)
    {
        if (_state != TrackedCombatLifecycleState.Running)
        {
            return;
        }

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
    }

    public TimeSpan CalculateActiveElapsed(DateTimeOffset referenceAt)
    {
        if (_startedAt is null)
        {
            return TimeSpan.Zero;
        }

        var end = _state switch
        {
            TrackedCombatLifecycleState.Frozen => _frozenAt ?? referenceAt,
            TrackedCombatLifecycleState.Paused when _pauseStartedAt is not null => _pauseStartedAt.Value,
            _ => referenceAt
        };

        var elapsed = end - _startedAt.Value - _accumulatedPauseDuration;
        return elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed;
    }

    public TrackedCombatScopeSnapshot ToSnapshot(DateTimeOffset referenceAt) =>
        new()
        {
            IsTracking = _state is TrackedCombatLifecycleState.Running or TrackedCombatLifecycleState.Paused,
            IsPaused = _state == TrackedCombatLifecycleState.Paused,
            StartedAt = _startedAt,
            ActiveElapsed = CalculateActiveElapsed(referenceAt),
            DamageDealt = _damageDealt,
            DamageReceived = _damageReceived,
            HealingDealt = _healingDealt,
            HealingReceived = _healingReceived,
            TotalDefeated = _totalDefeated,
            MyDefeats = _myDefeats,
            PowerActivations = _powerActivations,
            DamagePerSecondHundredths = CombatAggregator.CalculateDamagePerSecondHundredths(
                _damageDealt,
                CalculateActiveElapsed(referenceAt)),
            Accuracy = _accuracy.ToSnapshot()
        };
}
