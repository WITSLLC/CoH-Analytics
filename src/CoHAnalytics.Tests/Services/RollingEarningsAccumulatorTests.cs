using System.Diagnostics;
using CoHAnalytics.Models;
using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Services;

public sealed class RollingEarningsAccumulatorTests
{
    private static readonly DateTimeOffset SessionStart = LocalAt(2026, 8, 9, 12, 0, 0);

    private static DateTimeOffset LocalAt(int year, int month, int day, int hour, int minute, int second = 0) =>
        new(new DateTime(year, month, day, hour, minute, second, DateTimeKind.Local));

    private static readonly LogSourceId Source = LogSourceId.Create(
        "acct-1",
        "acct-1",
        @"C:\fake\chatlog.txt",
        new DateOnly(2026, 8, 9));

    [Fact]
    public void One_minute_window_uses_exact_xp_rate_numerator_and_denominator()
    {
        var rolling = new RollingEarningsAccumulator();
        var reference = SessionStart.AddSeconds(60);

        for (var second = 1; second <= 60; second++)
        {
            rolling.Apply(Event(second, SessionStart.AddSeconds(second)), 10, 0);
        }

        var window = rolling.ToSnapshot(SessionStart, reference, timingEndAt: null).OneMinute;

        Assert.Equal(RollingEarningsAvailability.Available, window.Availability);
        Assert.Equal(600, window.ExperienceGained);
        Assert.Equal(TimeSpan.FromSeconds(60), window.EffectiveDenominator);
        Assert.Equal(
            "36K/hr",
            GameplaySessionTelemetryPresentation.FormatRatePerHour(
                window.ExperienceGained,
                window.EffectiveDenominator));
    }

    [Fact]
    public void One_minute_window_uses_exact_influence_rate_numerator_and_denominator()
    {
        var rolling = new RollingEarningsAccumulator();
        var reference = SessionStart.AddSeconds(60);

        for (var second = 1; second <= 60; second++)
        {
            rolling.Apply(Event(second, SessionStart.AddSeconds(second)), 0, 5);
        }

        var window = rolling.ToSnapshot(SessionStart, reference, timingEndAt: null).OneMinute;

        Assert.Equal(300, window.InfluenceGained);
        Assert.Equal(
            "18K/hr",
            GameplaySessionTelemetryPresentation.FormatRatePerHour(
                window.InfluenceGained,
                window.EffectiveDenominator));
    }

    [Fact]
    public void Warm_up_uses_actual_session_age_when_shorter_than_window()
    {
        var rolling = new RollingEarningsAccumulator();
        var reference = SessionStart.AddSeconds(90);

        rolling.Apply(Event(1, SessionStart.AddSeconds(30)), 900, 0);

        var window = rolling.ToSnapshot(SessionStart, reference, timingEndAt: null).TenMinutes;

        Assert.Equal(TimeSpan.FromSeconds(90), window.EffectiveDenominator);
        Assert.Equal(RollingEarningsAvailability.WarmingUp, window.Availability);
        Assert.Equal(
            "36K/hr",
            RollingEarningsPresentation.BuildHero(
                rolling.ToSnapshot(SessionStart, reference, timingEndAt: null),
                RollingCombatPresets.TenMinutes).ExperienceRateValue);
    }

    [Fact]
    public void Idle_wall_clock_seconds_lower_rolling_earnings_rates()
    {
        var rolling = new RollingEarningsAccumulator();
        var earnEnd = SessionStart.AddMinutes(5);

        for (var second = 1; second <= 300; second++)
        {
            rolling.Apply(Event(second, SessionStart.AddSeconds(second)), 10, 0);
        }

        var activeOnly = rolling.ToSnapshot(SessionStart, earnEnd, timingEndAt: null).FiveMinutes;
        var withIdle = rolling.ToSnapshot(SessionStart, SessionStart.AddMinutes(10), timingEndAt: null).TenMinutes;

        var activeRate = GameplaySessionTelemetryPresentation.CalculateRatePerHour(
            activeOnly.ExperienceGained,
            activeOnly.EffectiveDenominator);
        var idleRate = GameplaySessionTelemetryPresentation.CalculateRatePerHour(
            withIdle.ExperienceGained,
            withIdle.EffectiveDenominator);

        Assert.True(idleRate < activeRate);
        Assert.Equal(TimeSpan.FromMinutes(10), withIdle.EffectiveDenominator);
    }

    [Fact]
    public void Earnings_older_than_window_are_evicted_from_selected_preset()
    {
        var rolling = new RollingEarningsAccumulator();
        var reference = SessionStart.AddMinutes(6);

        rolling.Apply(Event(1, SessionStart.AddSeconds(30)), 5_000, 0);
        rolling.Apply(Event(2, SessionStart.AddMinutes(5).AddSeconds(30)), 1_000, 0);

        var fiveMinute = rolling.ToSnapshot(SessionStart, reference, timingEndAt: null).FiveMinutes;
        Assert.Equal(1_000, fiveMinute.ExperienceGained);
    }

    [Fact]
    public void Preset_switching_does_not_reset_retained_history()
    {
        var rolling = new RollingEarningsAccumulator();
        var reference = SessionStart.AddMinutes(12);

        rolling.Apply(Event(1, SessionStart.AddMinutes(2)), 2_000, 0);
        rolling.Apply(Event(2, SessionStart.AddMinutes(8)), 8_000, 0);

        var snapshot = rolling.ToSnapshot(SessionStart, reference, timingEndAt: null);

        Assert.Equal(8_000, snapshot.FiveMinutes.ExperienceGained);
        Assert.Equal(10_000, snapshot.TenMinutes.ExperienceGained);
        Assert.Equal(10_000, snapshot.FifteenMinutes.ExperienceGained);
    }

    [Fact]
    public void Multi_session_rolling_buffers_remain_isolated()
    {
        var rollingA = new RollingEarningsAccumulator();
        var rollingB = new RollingEarningsAccumulator();
        var reference = SessionStart.AddMinutes(2);

        rollingA.Apply(Event(1, SessionStart.AddMinutes(1)), 4_000, 0);
        rollingB.Apply(Event(1, SessionStart.AddMinutes(1)), 9_000, 0);

        var snapshotA = rollingA.ToSnapshot(SessionStart, reference, timingEndAt: null).TenMinutes;
        var snapshotB = rollingB.ToSnapshot(SessionStart, reference, timingEndAt: null).TenMinutes;

        Assert.Equal(4_000, snapshotA.ExperienceGained);
        Assert.Equal(9_000, snapshotB.ExperienceGained);
    }

    [Fact]
    public void New_gameplay_session_gets_empty_rolling_buffer()
    {
        var used = new RollingEarningsAccumulator();
        used.Apply(Event(1, SessionStart.AddMinutes(1)), 5_000, 0);

        var fresh = new RollingEarningsAccumulator();
        var snapshot = fresh.ToSnapshot(SessionStart.AddMinutes(5), SessionStart.AddMinutes(6), timingEndAt: null);

        Assert.Equal(RollingEarningsAvailability.NoEarningsData, snapshot.TenMinutes.Availability);
        Assert.Equal(0, snapshot.TenMinutes.ExperienceGained);
    }

    [Fact]
    public void Backfill_events_use_source_timestamp_buckets_not_observed_now()
    {
        var rolling = new RollingEarningsAccumulator();
        var catchUpAt = SessionStart.AddMinutes(10);

        for (var minute = 0; minute < 10; minute++)
        {
            rolling.Apply(
                Event(minute + 1, catchUpAt, SessionStart.AddMinutes(minute).DateTime),
                1_000,
                0);
        }

        var window = rolling.ToSnapshot(SessionStart, catchUpAt, timingEndAt: null).TenMinutes;
        Assert.Equal(10_000, window.ExperienceGained);
        Assert.True(
            GameplaySessionTelemetryPresentation.CalculateRatePerHour(
                window.ExperienceGained,
                window.EffectiveDenominator) < 70_000);
    }

    [Fact]
    public void Date_only_source_timestamp_marks_rolling_unavailable()
    {
        var rolling = new RollingEarningsAccumulator();

        rolling.Apply(
            Event(1, SessionStart.AddMinutes(1), new DateTime(2026, 8, 9, 0, 0, 0, DateTimeKind.Unspecified)),
            100,
            0);

        var snapshot = rolling.ToSnapshot(SessionStart, SessionStart.AddMinutes(2), timingEndAt: null);
        Assert.True(snapshot.TimestampPrecisionUnavailable);
        Assert.Equal(
            RollingEarningsAvailability.UnavailableTimestampPrecision,
            snapshot.TenMinutes.Availability);
    }

    [Fact]
    public void Dense_earnings_workload_keeps_rolling_insertion_fast()
    {
        var rolling = new RollingEarningsAccumulator();
        var stopwatch = Stopwatch.StartNew();

        for (var index = 0; index < 100_000; index++)
        {
            var at = SessionStart.AddMilliseconds(index * 10);
            rolling.Apply(Event(index + 1, at), 1, 1);
        }

        stopwatch.Stop();
        var eventsPerSecond = 100_000 / Math.Max(stopwatch.Elapsed.TotalSeconds, 0.001);

        Assert.True(eventsPerSecond > 50_000, $"events/sec={eventsPerSecond:F0}");
        Assert.True(RollingEarningsAccumulator.EstimateMemoryBytes() <= 80_000);
        Assert.True(rolling.EventsAppliedToRolling > 0);
    }

    [Fact]
    public void Live_bracket_timestamp_accepts_xp_when_observed_at_is_utc()
    {
        var rolling = new RollingEarningsAccumulator();
        var localObserved = new DateTime(2026, 8, 9, 17, 5, 30, DateTimeKind.Local);
        var observedAt = new DateTimeOffset(localObserved);
        var sessionStart = observedAt.AddMinutes(-2);
        var bracketTimestamp = new DateTime(2026, 8, 9, 17, 5, 0, DateTimeKind.Unspecified);

        rolling.Apply(Event(1, observedAt, bracketTimestamp), 1_500, 250);

        Assert.Equal(1, rolling.EventsAppliedToRolling);
        var hero = RollingEarningsPresentation.BuildHero(
            rolling.ToSnapshot(sessionStart, observedAt, timingEndAt: null),
            RollingCombatPresets.TenMinutes);
        Assert.NotEqual("—", hero.ExperienceRateValue);
        Assert.NotEqual("—", hero.InfluenceRateValue);
        Assert.NotEqual("No qualifying earnings data yet.", hero.Detail);
    }

    [Fact]
    public void Stale_backfill_outside_horizon_rejects_rolling_earnings_bucket()
    {
        var rolling = new RollingEarningsAccumulator();
        var catchUpAt = LocalAt(2026, 8, 9, 17, 20, 0);
        var staleSource = new DateTime(2026, 8, 9, 16, 0, 0, DateTimeKind.Unspecified);

        rolling.Apply(Event(1, catchUpAt, staleSource), 5_000, 500);

        Assert.Equal(0, rolling.EventsAppliedToRolling);
        Assert.Equal(
            RollingEarningsAvailability.NoEarningsData,
            rolling.ToSnapshot(catchUpAt.AddMinutes(-30), catchUpAt, timingEndAt: null).TenMinutes.Availability);
    }

    [Fact]
    public void Warm_up_window_exposes_rolling_earnings_rates_before_ten_minutes_elapse()
    {
        var rolling = new RollingEarningsAccumulator();
        var reference = SessionStart.AddMinutes(2);
        rolling.Apply(Event(1, SessionStart.AddMinutes(1)), 12_000, 1_200);

        var hero = RollingEarningsPresentation.BuildHero(
            rolling.ToSnapshot(SessionStart, reference, timingEndAt: null),
            RollingCombatPresets.TenMinutes);

        Assert.Equal(RollingEarningsAvailability.WarmingUp, rolling.ToSnapshot(SessionStart, reference, timingEndAt: null).TenMinutes.Availability);
        Assert.NotEqual("—", hero.ExperienceRateValue);
        Assert.NotEqual("—", hero.InfluenceRateValue);
    }

    private static ParserEvent Event(
        long sequence,
        DateTimeOffset observedAt,
        DateTime? sourceTimestamp = null) =>
        new()
        {
            ContextId = MonitoringContextId.CreateNew(),
            SourceId = Source,
            SourceSegmentId = ParserSourceSegmentId.CreateNew(),
            BindingGeneration = 1,
            Sequence = sequence,
            ObservedAt = observedAt,
            RawLine = "You gain experience.",
            SourceByteStart = 0,
            SourceByteEnd = 1,
            LineStatus = ParserLineStatus.Complete,
            EventKind = ParserEventKind.TimestampedLine,
            ClassificationStatus = ParserClassificationStatus.Recognized,
            ClassificationRuleId = "xp",
            SourceTimestamp = sourceTimestamp ?? observedAt.LocalDateTime
        };
}

public sealed class TrackedEarningsAccumulatorTests
{
    private static readonly DateTimeOffset StartedAt =
        new(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Pause_excludes_paused_time_and_earnings_from_tracked_snapshot()
    {
        var tracked = new TrackedEarningsAccumulator();
        tracked.Start(StartedAt);
        tracked.Apply(1_000, 100);
        tracked.Pause(StartedAt.AddMinutes(2));

        tracked.Apply(9_999, 9_999);

        var snapshot = tracked.ToSnapshot(StartedAt.AddMinutes(5));
        Assert.True(snapshot.IsPaused);
        Assert.Equal(1_000, snapshot.ExperienceGained);
        Assert.Equal(100, snapshot.InfluenceGained);
        Assert.Equal(TimeSpan.FromMinutes(2), snapshot.ActiveElapsed);
    }

    [Fact]
    public void Resume_continues_accumulating_after_pause()
    {
        var tracked = new TrackedEarningsAccumulator();
        tracked.Start(StartedAt);
        tracked.Apply(500, 50);
        tracked.Pause(StartedAt.AddMinutes(1));
        tracked.Resume(StartedAt.AddMinutes(3));
        tracked.Apply(500, 50);

        var snapshot = tracked.ToSnapshot(StartedAt.AddMinutes(4));
        Assert.Equal(1_000, snapshot.ExperienceGained);
        Assert.Equal(100, snapshot.InfluenceGained);
        Assert.Equal(TimeSpan.FromMinutes(2), snapshot.ActiveElapsed);
    }
}
