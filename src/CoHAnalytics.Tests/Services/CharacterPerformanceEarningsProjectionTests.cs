using CoHAnalytics.Models;
using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Services;

public sealed class CharacterPerformanceEarningsProjectionTests
{
    private static readonly DateTimeOffset StartUtc =
        new(2026, 8, 18, 18, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset EndUtc =
        new(2026, 8, 18, 18, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Exact_earnings_delta_matches_current_minus_baseline()
    {
        var baseline = Totals(
            startedAtUtc: StartUtc,
            experienceGained: 100_000,
            gameplayInfluenceGained: 50_000);
        var current = Totals(experienceGained: 250_000, gameplayInfluenceGained: 140_000);

        var result = CharacterPerformanceEarningsProjection.Project(baseline, current, EndUtc);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Delta);
        Assert.Equal(StartUtc, result.Delta.StartedAtUtc);
        Assert.Equal(EndUtc, result.Delta.EndedAtUtc);
        Assert.Equal(TimeSpan.FromMinutes(30), result.Delta.ObservedDuration);
        Assert.Equal(150_000, result.Delta.ExperienceGained);
        Assert.Equal(90_000, result.Delta.GameplayInfluenceGained);
    }

    [Fact]
    public void Positive_duration_zero_earnings_succeeds()
    {
        var baseline = Totals(
            startedAtUtc: StartUtc,
            experienceGained: 100_000,
            gameplayInfluenceGained: 50_000);
        var current = Totals(
            experienceGained: 100_000,
            gameplayInfluenceGained: 50_000);

        var result = CharacterPerformanceEarningsProjection.Project(baseline, current, EndUtc);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Delta);
        Assert.Equal(0, result.Delta.ExperienceGained);
        Assert.Equal(0, result.Delta.GameplayInfluenceGained);
        Assert.Equal(TimeSpan.FromMinutes(30), result.Delta.ObservedDuration);
    }

    [Fact]
    public void Xp_only_delta_succeeds_when_influence_is_zero()
    {
        var baseline = Totals(startedAtUtc: StartUtc, experienceGained: 10_000);
        var current = Totals(experienceGained: 25_000);

        var delta = AssertSuccess(
            CharacterPerformanceEarningsProjection.Project(baseline, current, EndUtc));

        Assert.Equal(15_000, delta.ExperienceGained);
        Assert.Equal(0, delta.GameplayInfluenceGained);
    }

    [Fact]
    public void Influence_only_delta_succeeds_when_xp_is_zero()
    {
        var baseline = Totals(startedAtUtc: StartUtc, gameplayInfluenceGained: 8_000);
        var current = Totals(gameplayInfluenceGained: 15_000);

        var delta = AssertSuccess(
            CharacterPerformanceEarningsProjection.Project(baseline, current, EndUtc));

        Assert.Equal(0, delta.ExperienceGained);
        Assert.Equal(7_000, delta.GameplayInfluenceGained);
    }

    [Fact]
    public void Experience_regression_fails()
    {
        var baseline = Totals(startedAtUtc: StartUtc, experienceGained: 100_000);
        var current = Totals(experienceGained: 99_999);

        var result = CharacterPerformanceEarningsProjection.Project(baseline, current, EndUtc);

        Assert.Equal(CharacterPerformanceEarningsProjectionOutcome.CounterRegression, result.Outcome);
        Assert.Contains("experienceGained regressed", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Gameplay_influence_regression_fails()
    {
        var baseline = Totals(startedAtUtc: StartUtc, gameplayInfluenceGained: 50_000);
        var current = Totals(gameplayInfluenceGained: 49_999);

        var result = CharacterPerformanceEarningsProjection.Project(baseline, current, EndUtc);

        Assert.Equal(CharacterPerformanceEarningsProjectionOutcome.CounterRegression, result.Outcome);
        Assert.Contains("gameplayInfluenceGained regressed", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void End_before_start_fails()
    {
        var baseline = Totals(startedAtUtc: StartUtc, experienceGained: 1_000);
        var current = Totals(experienceGained: 2_000);
        var endedBeforeStart = StartUtc.AddMinutes(-5);

        var result = CharacterPerformanceEarningsProjection.Project(baseline, current, endedBeforeStart);

        Assert.Equal(CharacterPerformanceEarningsProjectionOutcome.InvalidBoundary, result.Outcome);
        Assert.Contains("endedAtUtc must be later than startedAtUtc", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Zero_duration_boundary_fails()
    {
        var baseline = Totals(startedAtUtc: StartUtc, experienceGained: 1_000);
        var current = Totals(experienceGained: 2_000);

        var result = CharacterPerformanceEarningsProjection.Project(baseline, current, StartUtc);

        Assert.Equal(CharacterPerformanceEarningsProjectionOutcome.InvalidBoundary, result.Outcome);
        Assert.Contains("endedAtUtc must be later than startedAtUtc", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Large_counters_project_without_overflow()
    {
        var baseline = Totals(
            startedAtUtc: StartUtc,
            experienceGained: 2_000_000_000,
            gameplayInfluenceGained: 1_500_000_000);
        var current = Totals(
            experienceGained: 2_500_000_000,
            gameplayInfluenceGained: 1_900_000_000);

        var delta = AssertSuccess(
            CharacterPerformanceEarningsProjection.Project(baseline, current, EndUtc));

        Assert.Equal(500_000_000, delta.ExperienceGained);
        Assert.Equal(400_000_000, delta.GameplayInfluenceGained);
    }

    [Fact]
    public void Boundary_timestamps_preserve_exact_utc_precision()
    {
        var startedAt = new DateTimeOffset(2026, 8, 18, 18, 0, 0, 123, TimeSpan.Zero);
        var endedAt = new DateTimeOffset(2026, 8, 18, 18, 0, 30, 456, TimeSpan.Zero);
        var baseline = Totals(startedAtUtc: startedAt, experienceGained: 1);
        var current = Totals(experienceGained: 2);

        var delta = AssertSuccess(
            CharacterPerformanceEarningsProjection.Project(baseline, current, endedAt));

        Assert.Equal(startedAt, delta.StartedAtUtc);
        Assert.Equal(endedAt, delta.EndedAtUtc);
        Assert.Equal(TimeSpan.FromMilliseconds(30_333), delta.ObservedDuration);
    }

    [Fact]
    public void Short_positive_duration_projects_without_live_warm_up_requirement()
    {
        var startedAt = StartUtc;
        var endedAt = StartUtc.AddSeconds(30);
        var baseline = Totals(startedAtUtc: startedAt, experienceGained: 1_000);
        var current = Totals(experienceGained: 5_000, gameplayInfluenceGained: 500);

        var delta = AssertSuccess(
            CharacterPerformanceEarningsProjection.Project(baseline, current, endedAt));

        Assert.Equal(TimeSpan.FromSeconds(30), delta.ObservedDuration);
        Assert.Equal(4_000, delta.ExperienceGained);
        Assert.Equal(500, delta.GameplayInfluenceGained);
    }

    [Fact]
    public void Baseline_without_start_timestamp_fails()
    {
        var baseline = Totals(experienceGained: 1_000);
        var current = Totals(experienceGained: 2_000);

        var result = CharacterPerformanceEarningsProjection.Project(baseline, current, EndUtc);

        Assert.Equal(CharacterPerformanceEarningsProjectionOutcome.InvalidBaseline, result.Outcome);
        Assert.Contains("startedAtUtc is required", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void From_gameplay_session_snapshot_uses_session_cumulative_fields()
    {
        var snapshot = new GameplaySessionSnapshot
        {
            SessionId = GameplaySessionId.CreateNew(),
            ContextId = MonitoringContextId.CreateNew(),
            LifecycleState = GameplaySessionLifecycleState.Active,
            CharacterIdentityConfidence = CharacterIdentityConfidence.Confirmed,
            CharacterIdentityResolutionState = CharacterIdentityResolutionState.Resolved,
            StartedAt = new DateTimeOffset(2026, 8, 18, 17, 45, 0, TimeSpan.FromHours(-4)),
            SessionExperienceGained = 42_000,
            SessionGameplayInfluenceGained = 9_500
        };

        var totals = CharacterPerformanceEarningsTotals.FromGameplaySessionSnapshot(snapshot);

        Assert.Equal(snapshot.StartedAt.ToUniversalTime(), totals.StartedAtUtc);
        Assert.Equal(42_000, totals.ExperienceGained);
        Assert.Equal(9_500, totals.GameplayInfluenceGained);
    }

    [Fact]
    public void Negative_current_earnings_fail_before_boundary_check()
    {
        var baseline = Totals(startedAtUtc: StartUtc, experienceGained: 1_000);
        var current = Totals(experienceGained: -1);

        var result = CharacterPerformanceEarningsProjection.Project(baseline, current, EndUtc);

        Assert.Equal(CharacterPerformanceEarningsProjectionOutcome.InvalidCurrent, result.Outcome);
        Assert.Contains("earnings totals must be non-negative", result.Detail, StringComparison.Ordinal);
    }

    private static CharacterPerformanceEarningsDelta AssertSuccess(
        CharacterPerformanceEarningsProjectionResult result)
    {
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Delta);
        return result.Delta!;
    }

    private static CharacterPerformanceEarningsTotals Totals(
        DateTimeOffset? startedAtUtc = null,
        long experienceGained = 0,
        long gameplayInfluenceGained = 0) =>
        new()
        {
            StartedAtUtc = startedAtUtc,
            ExperienceGained = experienceGained,
            GameplayInfluenceGained = gameplayInfluenceGained
        };
}
