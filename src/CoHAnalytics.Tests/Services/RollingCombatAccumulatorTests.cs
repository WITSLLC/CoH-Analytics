using System.Diagnostics;
using CoHAnalytics.Models;
using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Services;

public sealed class RollingCombatAccumulatorTests
{
    private static readonly DateTimeOffset SessionStart = LocalAt(2026, 8, 9, 12, 0, 0);

    private static DateTimeOffset LocalAt(int year, int month, int day, int hour, int minute, int second = 0) =>
        new(new DateTime(year, month, day, hour, minute, second, DateTimeKind.Local));

    [Fact]
    public void One_minute_window_uses_exact_numerator_and_denominator()
    {
        var rolling = new RollingCombatAccumulator();
        var contextId = MonitoringContextId.CreateNew();
        var reference = SessionStart.AddSeconds(60);

        for (var second = 1; second <= 60; second++)
        {
            rolling.Apply(Event(
                contextId,
                second,
                SessionStart.AddSeconds(second),
                SessionStart.AddSeconds(second),
                amountHundredths: 100));
        }

        var snapshot = rolling.ToSnapshot(SessionStart, reference, timingEndAt: null);
        var window = snapshot.OneMinute;

        Assert.Equal(RollingCombatAvailability.Available, window.Availability);
        Assert.Equal(new CombatScaledAmount(6000), window.DamageDealt);
        Assert.Equal(100, window.DamagePerSecondHundredths);
        Assert.Equal(TimeSpan.FromSeconds(60), window.EffectiveDenominator);
    }

    [Fact]
    public void Ten_minute_default_preset_is_populated_in_snapshot()
    {
        var aggregator = new CombatAggregator();
        var contextId = MonitoringContextId.CreateNew();
        aggregator.Apply(Event(contextId, 1, SessionStart.AddSeconds(120), SessionStart.AddSeconds(120), 10_000));

        var snapshot = aggregator.ToSnapshot(
            SessionStart,
            SessionStart.AddMinutes(2),
            timingEndAt: null,
            CombatActivityDefaults.IdleThreshold);

        Assert.Equal(RollingCombatPresets.TenMinutes, snapshot.Rolling.TenMinutes.WindowMinutes);
        Assert.Equal(new CombatScaledAmount(10_000), snapshot.Rolling.TenMinutes.DamageDealt);
    }

    [Fact]
    public void Fifteen_minute_window_retains_full_horizon()
    {
        var rolling = new RollingCombatAccumulator();
        var contextId = MonitoringContextId.CreateNew();
        var reference = SessionStart.AddMinutes(15);

        rolling.Apply(Event(contextId, 1, SessionStart.AddSeconds(30), SessionStart.AddSeconds(30), 1_000));
        rolling.Apply(Event(
            contextId,
            2,
            SessionStart.AddMinutes(14).AddSeconds(30),
            SessionStart.AddMinutes(14).AddSeconds(30),
            2_000));

        var snapshot = rolling.ToSnapshot(SessionStart, reference, timingEndAt: null);

        Assert.Equal(new CombatScaledAmount(3_000), snapshot.FifteenMinutes.DamageDealt);
        Assert.Equal(TimeSpan.FromMinutes(15), snapshot.FifteenMinutes.EffectiveDenominator);
    }

    [Fact]
    public void Damage_older_than_window_is_evicted_from_selected_preset()
    {
        var rolling = new RollingCombatAccumulator();
        var contextId = MonitoringContextId.CreateNew();
        var reference = SessionStart.AddMinutes(6);

        rolling.Apply(Event(contextId, 1, SessionStart.AddSeconds(30), SessionStart.AddSeconds(30), 5_000));
        rolling.Apply(Event(
            contextId,
            2,
            SessionStart.AddMinutes(5).AddSeconds(30),
            SessionStart.AddMinutes(5).AddSeconds(30),
            1_000));

        var fiveMinute = rolling.ToSnapshot(SessionStart, reference, timingEndAt: null).FiveMinutes;
        Assert.Equal(new CombatScaledAmount(1_000), fiveMinute.DamageDealt);
    }

    [Fact]
    public void Preset_switching_does_not_reset_retained_history()
    {
        var rolling = new RollingCombatAccumulator();
        var contextId = MonitoringContextId.CreateNew();
        var reference = SessionStart.AddMinutes(12);

        rolling.Apply(Event(contextId, 1, SessionStart.AddMinutes(2), SessionStart.AddMinutes(2), 2_000));
        rolling.Apply(Event(contextId, 2, SessionStart.AddMinutes(8), SessionStart.AddMinutes(8), 8_000));

        var snapshot = rolling.ToSnapshot(SessionStart, reference, timingEndAt: null);

        Assert.Equal(new CombatScaledAmount(8_000), snapshot.FiveMinutes.DamageDealt);
        Assert.Equal(new CombatScaledAmount(10_000), snapshot.TenMinutes.DamageDealt);
        Assert.Equal(new CombatScaledAmount(10_000), snapshot.FifteenMinutes.DamageDealt);
    }

    [Fact]
    public void Warm_up_uses_actual_session_age_when_shorter_than_window()
    {
        var rolling = new RollingCombatAccumulator();
        var contextId = MonitoringContextId.CreateNew();
        var reference = SessionStart.AddSeconds(90);

        rolling.Apply(Event(contextId, 1, SessionStart.AddSeconds(30), SessionStart.AddSeconds(30), 9_000));

        var window = rolling.ToSnapshot(SessionStart, reference, timingEndAt: null).TenMinutes;

        Assert.Equal(TimeSpan.FromSeconds(90), window.EffectiveDenominator);
        Assert.Equal(RollingCombatAvailability.WarmingUp, window.Availability);
    }

    [Fact]
    public void Idle_wall_clock_seconds_lower_rolling_dps()
    {
        var rolling = new RollingCombatAccumulator();
        var contextId = MonitoringContextId.CreateNew();
        var fightEnd = SessionStart.AddMinutes(5);

        for (var second = 1; second <= 300; second++)
        {
            rolling.Apply(Event(
                contextId,
                second,
                SessionStart.AddSeconds(second),
                SessionStart.AddSeconds(second),
                amountHundredths: 100));
        }

        var activeOnly = rolling.ToSnapshot(SessionStart, fightEnd, timingEndAt: null).FiveMinutes;
        var withIdle = rolling.ToSnapshot(SessionStart, SessionStart.AddMinutes(10), timingEndAt: null).TenMinutes;

        Assert.True(withIdle.DamagePerSecondHundredths < activeOnly.DamagePerSecondHundredths);
        Assert.Equal(TimeSpan.FromMinutes(10), withIdle.EffectiveDenominator);
    }

    [Fact]
    public void Multi_session_rolling_buffers_remain_isolated()
    {
        var rollingA = new RollingCombatAccumulator();
        var rollingB = new RollingCombatAccumulator();
        var contextId = MonitoringContextId.CreateNew();
        var reference = SessionStart.AddMinutes(2);

        rollingA.Apply(Event(contextId, 1, SessionStart.AddMinutes(1), SessionStart.AddMinutes(1), 4_000));
        rollingB.Apply(Event(contextId, 2, SessionStart.AddMinutes(1), SessionStart.AddMinutes(1), 9_000));

        var snapshotA = rollingA.ToSnapshot(SessionStart, reference, timingEndAt: null).TenMinutes;
        var snapshotB = rollingB.ToSnapshot(SessionStart, reference, timingEndAt: null).TenMinutes;

        Assert.Equal(new CombatScaledAmount(4_000), snapshotA.DamageDealt);
        Assert.Equal(new CombatScaledAmount(9_000), snapshotB.DamageDealt);
    }

    [Fact]
    public void New_gameplay_session_gets_empty_rolling_buffer()
    {
        var aggregator = new CombatAggregator();
        var contextId = MonitoringContextId.CreateNew();
        aggregator.Apply(Event(contextId, 1, SessionStart.AddMinutes(1), SessionStart.AddMinutes(1), 5_000));

        var fresh = new CombatAggregator();
        var snapshot = fresh.ToSnapshot(SessionStart.AddMinutes(5), SessionStart.AddMinutes(6), null, CombatActivityDefaults.IdleThreshold);

        Assert.Equal(RollingCombatAvailability.NoCombatData, snapshot.Rolling.TenMinutes.Availability);
        Assert.Equal(CombatScaledAmount.Zero, snapshot.Rolling.TenMinutes.DamageDealt);
    }

    [Fact]
    public void Backfill_events_use_source_timestamp_buckets_not_observed_now()
    {
        var rolling = new RollingCombatAccumulator();
        var contextId = MonitoringContextId.CreateNew();
        var catchUpAt = SessionStart.AddMinutes(10);

        for (var minute = 0; minute < 10; minute++)
        {
            rolling.Apply(Event(
                contextId,
                minute + 1,
                catchUpAt,
                SessionStart.AddMinutes(minute),
                amountHundredths: 1_000));
        }

        var window = rolling.ToSnapshot(SessionStart, catchUpAt, timingEndAt: null).TenMinutes;
        Assert.Equal(new CombatScaledAmount(10_000), window.DamageDealt);
        Assert.True(window.DamagePerSecondHundredths < 200);
    }

    [Fact]
    public void Date_only_source_timestamp_marks_rolling_unavailable()
    {
        var rolling = new RollingCombatAccumulator();
        var contextId = MonitoringContextId.CreateNew();

        rolling.Apply(new CombatEvent
        {
            ContextId = contextId,
            ParserSequence = 1,
            ObservedAt = SessionStart.AddMinutes(1),
            SourceTimestamp = new DateTime(2026, 8, 9, 0, 0, 0, DateTimeKind.Unspecified),
            Kind = CombatEventKind.DamageDealt,
            GrammarId = CombatGrammarId.Dmg01YouHitWithPower,
            Amount = new CombatScaledAmount(1_000)
        });

        var snapshot = rolling.ToSnapshot(SessionStart, SessionStart.AddMinutes(2), timingEndAt: null);
        Assert.True(snapshot.TimestampPrecisionUnavailable);
        Assert.Equal(
            RollingCombatAvailability.UnavailableTimestampPrecision,
            snapshot.TenMinutes.Availability);
    }

    [Fact]
    public void Delta_positive_negative_and_neutral_deadband()
    {
        var positive = RollingCombatPresentation.BuildDelta(20_000, 10_000, TimeSpan.FromMinutes(30), 10);
        var negative = RollingCombatPresentation.BuildDelta(8_000, 10_000, TimeSpan.FromMinutes(30), 10);
        var neutral = RollingCombatPresentation.BuildDelta(10_200, 10_000, TimeSpan.FromMinutes(30), 10);

        Assert.True(positive.IsPositive);
        Assert.True(negative.IsNegative);
        Assert.True(neutral.IsNeutral);
    }

    [Fact]
    public void Delta_suppressed_before_two_times_window()
    {
        var delta = RollingCombatPresentation.BuildDelta(20_000, 10_000, TimeSpan.FromMinutes(15), 10);
        Assert.True(delta.IsUnavailable);
    }

    [Fact]
    public void Dense_combat_workload_keeps_rolling_insertion_fast()
    {
        var rolling = new RollingCombatAccumulator();
        var contextId = MonitoringContextId.CreateNew();
        var stopwatch = Stopwatch.StartNew();

        for (var index = 0; index < 100_000; index++)
        {
            var at = SessionStart.AddMilliseconds(index * 10);
            rolling.Apply(Event(contextId, index + 1, at, at, amountHundredths: 10));
        }

        stopwatch.Stop();
        var eventsPerSecond = 100_000 / Math.Max(stopwatch.Elapsed.TotalSeconds, 0.001);

        Assert.True(eventsPerSecond > 50_000, $"events/sec={eventsPerSecond:F0}");
        Assert.True(RollingCombatAccumulator.EstimateMemoryBytes() <= 112_000);
        Assert.True(rolling.EventsAppliedToRolling > 0);
    }

    [Fact]
    public void Live_bracket_timestamp_accepts_events_when_observed_at_is_utc()
    {
        var rolling = new RollingCombatAccumulator();
        var contextId = MonitoringContextId.CreateNew();
        var localObserved = new DateTime(2026, 8, 9, 17, 5, 30, DateTimeKind.Local);
        var observedAt = new DateTimeOffset(localObserved);
        var sessionStart = observedAt.AddMinutes(-2);
        var bracketTimestamp = new DateTime(2026, 8, 9, 17, 5, 0, DateTimeKind.Unspecified);

        rolling.Apply(new CombatEvent
        {
            ContextId = contextId,
            ParserSequence = 1,
            ObservedAt = observedAt,
            SourceTimestamp = bracketTimestamp,
            Kind = CombatEventKind.DamageDealt,
            GrammarId = CombatGrammarId.Dmg01YouHitWithPower,
            Amount = new CombatScaledAmount(1_300)
        });

        Assert.Equal(1, rolling.EventsAppliedToRolling);

        var window = rolling.ToSnapshot(sessionStart, observedAt, timingEndAt: null).TenMinutes;
        Assert.NotEqual(RollingCombatAvailability.NoCombatData, window.Availability);
        Assert.True(window.DamagePerSecondHundredths > 0);
        Assert.NotEqual(
            "—",
            RollingCombatPresentation.BuildHero(
                new CombatSnapshot { Rolling = rolling.ToSnapshot(sessionStart, observedAt, timingEndAt: null) },
                TimeSpan.FromMinutes(2),
                RollingCombatPresets.TenMinutes).RateValue);
    }

    [Fact]
    public void Full_timestamp_live_event_populates_rolling_snapshot()
    {
        var rolling = new RollingCombatAccumulator();
        var contextId = MonitoringContextId.CreateNew();
        var observedAt = LocalAt(2026, 8, 9, 17, 5, 30);
        var sourceTimestamp = new DateTime(2026, 8, 9, 17, 5, 28, DateTimeKind.Unspecified);

        rolling.Apply(new CombatEvent
        {
            ContextId = contextId,
            ParserSequence = 1,
            ObservedAt = observedAt,
            SourceTimestamp = sourceTimestamp,
            Kind = CombatEventKind.DamageDealt,
            GrammarId = CombatGrammarId.Dmg01YouHitWithPower,
            Amount = new CombatScaledAmount(2_000)
        });

        var snapshot = rolling.ToSnapshot(observedAt.AddMinutes(-2), observedAt, timingEndAt: null);
        Assert.Equal(1, rolling.EventsAppliedToRolling);
        Assert.True(snapshot.TenMinutes.DamageDealt.Hundredths > 0);
    }

    [Fact]
    public void Untimestamped_live_event_uses_observed_at_bucket()
    {
        var rolling = new RollingCombatAccumulator();
        var contextId = MonitoringContextId.CreateNew();
        var observedAt = LocalAt(2026, 8, 9, 17, 6, 0);

        rolling.Apply(new CombatEvent
        {
            ContextId = contextId,
            ParserSequence = 1,
            ObservedAt = observedAt,
            Kind = CombatEventKind.DamageDealt,
            GrammarId = CombatGrammarId.Dmg01YouHitWithPower,
            Amount = new CombatScaledAmount(500)
        });

        Assert.Equal(1, rolling.EventsAppliedToRolling);
    }

    [Fact]
    public void Stale_backfill_outside_horizon_increments_session_only_not_rolling()
    {
        var aggregator = new CombatAggregator();
        var contextId = MonitoringContextId.CreateNew();
        var catchUpAt = LocalAt(2026, 8, 9, 17, 20, 0);
        var staleSource = new DateTime(2026, 8, 9, 16, 0, 0, DateTimeKind.Unspecified);

        aggregator.Apply(new CombatEvent
        {
            ContextId = contextId,
            ParserSequence = 1,
            ObservedAt = catchUpAt,
            SourceTimestamp = staleSource,
            Kind = CombatEventKind.DamageDealt,
            GrammarId = CombatGrammarId.Dmg01YouHitWithPower,
            Amount = new CombatScaledAmount(4_000)
        });

        var snapshot = aggregator.ToSnapshot(
            catchUpAt.AddMinutes(-30),
            catchUpAt,
            timingEndAt: null,
            CombatActivityDefaults.IdleThreshold);

        Assert.Equal(1, aggregator.EventsApplied);
        Assert.Equal(0, aggregator.Rolling.EventsAppliedToRolling);
        Assert.Equal(RollingCombatAvailability.NoCombatData, snapshot.Rolling.TenMinutes.Availability);
        Assert.True(snapshot.DamageDealt.Hundredths > 0);
    }

    [Fact]
    public void Warm_up_window_exposes_rolling_dps_rate_before_ten_minutes_elapse()
    {
        var rolling = new RollingCombatAccumulator();
        var contextId = MonitoringContextId.CreateNew();
        var reference = SessionStart.AddMinutes(2);
        rolling.Apply(Event(contextId, 1, SessionStart.AddMinutes(1), SessionStart.AddMinutes(1), 12_000));

        var combat = new CombatSnapshot
        {
            Rolling = rolling.ToSnapshot(SessionStart, reference, timingEndAt: null)
        };
        var hero = RollingCombatPresentation.BuildHero(
            combat,
            TimeSpan.FromMinutes(2),
            RollingCombatPresets.TenMinutes);

        Assert.Equal(RollingCombatAvailability.WarmingUp, combat.Rolling.TenMinutes.Availability);
        Assert.NotEqual("—", hero.RateValue);
        Assert.NotEqual("No qualifying combat data yet.", hero.Detail);
    }

    private static CombatEvent Event(
        MonitoringContextId contextId,
        long sequence,
        DateTimeOffset observedAt,
        DateTimeOffset sourceAt,
        long amountHundredths = 0) =>
        new()
        {
            ContextId = contextId,
            ParserSequence = sequence,
            ObservedAt = observedAt,
            SourceTimestamp = sourceAt.LocalDateTime,
            Kind = CombatEventKind.DamageDealt,
            GrammarId = CombatGrammarId.Dmg01YouHitWithPower,
            Amount = new CombatScaledAmount(amountHundredths)
        };
}
