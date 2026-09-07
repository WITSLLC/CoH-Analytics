using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

/// <summary>
/// Mutable tracked-scope earnings accumulator with explicit start/pause/resume/stop lifecycle.
/// </summary>
public sealed class TrackedEarningsAccumulator
{
    private long _experienceGained;
    private long _influenceGained;
    private DateTimeOffset? _startedAt;
    private TimeSpan _accumulatedPauseDuration;
    private DateTimeOffset? _pauseStartedAt;
    private DateTimeOffset? _frozenAt;
    private TrackedCombatLifecycleState _state = TrackedCombatLifecycleState.Idle;

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
        _experienceGained = 0;
        _influenceGained = 0;
        _startedAt = null;
        _accumulatedPauseDuration = TimeSpan.Zero;
        _pauseStartedAt = null;
        _frozenAt = null;
        _state = TrackedCombatLifecycleState.Idle;
    }

    public void Apply(long experienceGained, long influenceGained)
    {
        if (_state != TrackedCombatLifecycleState.Running)
        {
            return;
        }

        _experienceGained += experienceGained;
        _influenceGained += influenceGained;
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

    public TrackedEarningsScopeSnapshot ToSnapshot(DateTimeOffset referenceAt) =>
        new()
        {
            IsTracking = _state is TrackedCombatLifecycleState.Running or TrackedCombatLifecycleState.Paused,
            IsPaused = _state == TrackedCombatLifecycleState.Paused,
            StartedAt = _startedAt,
            ActiveElapsed = CalculateActiveElapsed(referenceAt),
            ExperienceGained = _experienceGained,
            InfluenceGained = _influenceGained
        };
}
