using CoHAnalytics.Models;
using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Services;

public sealed class CharacterPerformanceCombatProjectionTests
{
    [Fact]
    public void Exact_combat_delta_matches_current_minus_baseline()
    {
        var baseline = Totals(
            damageHundredths: 1_000_025,
            attempts: 100,
            hits: 90,
            rolledAttempts: 85,
            chanceSum: 800_000,
            rollSum: 420_000,
            forcedHits: 5,
            autohits: 12,
            totalDefeated: 20,
            myDefeats: 15);

        var current = Totals(
            damageHundredths: 1_550_075,
            attempts: 150,
            hits: 137,
            rolledAttempts: 127,
            chanceSum: 1_200_000,
            rollSum: 600_000,
            forcedHits: 10,
            autohits: 18,
            totalDefeated: 32,
            myDefeats: 23);

        var result = CharacterPerformanceCombatProjection.Project(baseline, current);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Delta);
        Assert.Equal(new CombatScaledAmount(550_050), result.Delta.DamageDealt);
        Assert.Equal(50, result.Delta.Attempts);
        Assert.Equal(47, result.Delta.Hits);
        Assert.Equal(42, result.Delta.RolledAttempts);
        Assert.Equal(400_000, result.Delta.DisplayedChanceSumHundredths);
        Assert.Equal(180_000, result.Delta.RollSumHundredths);
        Assert.Equal(5, result.Delta.ForcedHits);
        Assert.Equal(6, result.Delta.Autohits);
        Assert.Equal(12, result.Delta.TotalDefeated);
        Assert.Equal(8, result.Delta.MyDefeats);
    }

    [Fact]
    public void Baseline_equals_current_produces_valid_zero_delta()
    {
        var totals = Totals(
            damageHundredths: 500_000,
            attempts: 40,
            hits: 35,
            rolledAttempts: 30,
            chanceSum: 300_000,
            rollSum: 150_000,
            forcedHits: 2,
            autohits: 4,
            totalDefeated: 8,
            myDefeats: 5);

        var result = CharacterPerformanceCombatProjection.Project(totals, totals);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Delta);
        Assert.Equal(CombatScaledAmount.Zero, result.Delta.DamageDealt);
        Assert.Equal(0, result.Delta.Attempts);
        Assert.Equal(0, result.Delta.Hits);
        Assert.Equal(0, result.Delta.RolledAttempts);
        Assert.Equal(0, result.Delta.DisplayedChanceSumHundredths);
        Assert.Equal(0, result.Delta.RollSumHundredths);
        Assert.Equal(0, result.Delta.ForcedHits);
        Assert.Equal(0, result.Delta.Autohits);
        Assert.Equal(0, result.Delta.TotalDefeated);
        Assert.Equal(0, result.Delta.MyDefeats);
    }

    [Fact]
    public void Damage_regression_fails()
    {
        var result = CharacterPerformanceCombatProjection.Project(
            Totals(damageHundredths: 10_000),
            Totals(damageHundredths: 9_000));

        Assert.False(result.IsSuccess);
        Assert.Equal(CharacterPerformanceCombatProjectionOutcome.CounterRegression, result.Outcome);
        Assert.Contains("damageDealt regressed", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Attempts_regression_fails()
    {
        var result = CharacterPerformanceCombatProjection.Project(
            Totals(attempts: 100),
            Totals(attempts: 95));

        Assert.Equal(CharacterPerformanceCombatProjectionOutcome.CounterRegression, result.Outcome);
        Assert.Contains("attempts regressed", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Hits_regression_fails()
    {
        var result = CharacterPerformanceCombatProjection.Project(
            Totals(attempts: 100, hits: 90),
            Totals(attempts: 100, hits: 89));

        Assert.Equal(CharacterPerformanceCombatProjectionOutcome.CounterRegression, result.Outcome);
        Assert.Contains("hits regressed", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Rolled_attempts_regression_fails()
    {
        var result = CharacterPerformanceCombatProjection.Project(
            Totals(attempts: 100, hits: 90, rolledAttempts: 85),
            Totals(attempts: 100, hits: 90, rolledAttempts: 80));

        Assert.Equal(CharacterPerformanceCombatProjectionOutcome.CounterRegression, result.Outcome);
        Assert.Contains("rolledAttempts regressed", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Chance_sum_regression_fails()
    {
        var result = CharacterPerformanceCombatProjection.Project(
            Totals(chanceSum: 800_000),
            Totals(chanceSum: 799_999));

        Assert.Equal(CharacterPerformanceCombatProjectionOutcome.CounterRegression, result.Outcome);
        Assert.Contains("displayedChanceSumHundredths regressed", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Roll_sum_regression_fails()
    {
        var result = CharacterPerformanceCombatProjection.Project(
            Totals(rollSum: 420_000),
            Totals(rollSum: 419_999));

        Assert.Equal(CharacterPerformanceCombatProjectionOutcome.CounterRegression, result.Outcome);
        Assert.Contains("rollSumHundredths regressed", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Forced_hits_regression_fails()
    {
        var result = CharacterPerformanceCombatProjection.Project(
            Totals(attempts: 10, hits: 8, forcedHits: 3),
            Totals(attempts: 10, hits: 8, forcedHits: 2));

        Assert.Equal(CharacterPerformanceCombatProjectionOutcome.CounterRegression, result.Outcome);
        Assert.Contains("forcedHits regressed", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Autohits_regression_fails()
    {
        var result = CharacterPerformanceCombatProjection.Project(
            Totals(autohits: 12),
            Totals(autohits: 11));

        Assert.Equal(CharacterPerformanceCombatProjectionOutcome.CounterRegression, result.Outcome);
        Assert.Contains("autohits regressed", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Total_defeated_regression_fails()
    {
        var result = CharacterPerformanceCombatProjection.Project(
            Totals(totalDefeated: 20, myDefeats: 15),
            Totals(totalDefeated: 18, myDefeats: 15));

        Assert.Equal(CharacterPerformanceCombatProjectionOutcome.CounterRegression, result.Outcome);
        Assert.Contains("totalDefeated regressed", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void My_defeats_regression_fails()
    {
        var result = CharacterPerformanceCombatProjection.Project(
            Totals(totalDefeated: 20, myDefeats: 15),
            Totals(totalDefeated: 20, myDefeats: 14));

        Assert.Equal(CharacterPerformanceCombatProjectionOutcome.CounterRegression, result.Outcome);
        Assert.Contains("myDefeats regressed", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Invalid_derived_delta_rejects_when_hits_delta_exceeds_attempts_delta()
    {
        var baseline = Totals(attempts: 100, hits: 90);
        var current = Totals(attempts: 105, hits: 100);

        var result = CharacterPerformanceCombatProjection.Project(baseline, current);

        Assert.False(result.IsSuccess);
        Assert.Equal(CharacterPerformanceCombatProjectionOutcome.InvalidDelta, result.Outcome);
        Assert.Contains("hits delta cannot exceed attempts delta", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Forced_hit_delta_preserves_attempt_hit_forced_ordering()
    {
        var baseline = Totals(attempts: 10, hits: 8, forcedHits: 2);
        var current = Totals(attempts: 15, hits: 13, forcedHits: 5);

        var delta = AssertSuccess(CharacterPerformanceCombatProjection.Project(baseline, current));

        Assert.Equal(5, delta.Attempts);
        Assert.Equal(5, delta.Hits);
        Assert.Equal(3, delta.ForcedHits);
        Assert.True(delta.ForcedHits <= delta.Hits);
        Assert.True(delta.Hits <= delta.Attempts);
    }

    [Fact]
    public void Autohit_only_increase_keeps_attempts_and_hits_at_zero()
    {
        var baseline = Totals();
        var current = Totals(autohits: 4);

        var delta = AssertSuccess(CharacterPerformanceCombatProjection.Project(baseline, current));

        Assert.Equal(0, delta.Attempts);
        Assert.Equal(0, delta.Hits);
        Assert.Equal(4, delta.Autohits);
    }

    [Fact]
    public void Enemy_attribution_delta_accepts_total_and_my_defeats()
    {
        var baseline = Totals(totalDefeated: 10, myDefeats: 5);
        var current = Totals(totalDefeated: 20, myDefeats: 8);

        var delta = AssertSuccess(CharacterPerformanceCombatProjection.Project(baseline, current));

        Assert.Equal(10, delta.TotalDefeated);
        Assert.Equal(3, delta.MyDefeats);
        Assert.True(delta.MyDefeats <= delta.TotalDefeated);
    }

    [Fact]
    public void From_combat_snapshot_uses_session_accuracy_not_tracked_or_rolling()
    {
        var snapshot = new CombatSnapshot
        {
            DamageDealt = new CombatScaledAmount(12_345),
            TotalDefeated = 7,
            MyDefeats = 4,
            Accuracy = new CombatAccuracyScopeSnapshot
            {
                Attempts = 11,
                Hits = 9,
                RolledAttempts = 8,
                DisplayedChanceSumHundredths = 88_000,
                RollSumHundredths = 44_000,
                ForcedHits = 1,
                Autohits = 2
            },
            Tracked = new TrackedCombatScopeSnapshot
            {
                DamageDealt = new CombatScaledAmount(99_999),
                TotalDefeated = 99,
                Accuracy = new CombatAccuracyScopeSnapshot
                {
                    Attempts = 99,
                    Hits = 99
                }
            }
        };

        var totals = CharacterPerformanceCombatTotals.FromCombatSnapshot(snapshot);

        Assert.Equal(new CombatScaledAmount(12_345), totals.DamageDealt);
        Assert.Equal(11, totals.Attempts);
        Assert.Equal(9, totals.Hits);
        Assert.Equal(8, totals.RolledAttempts);
        Assert.Equal(88_000, totals.DisplayedChanceSumHundredths);
        Assert.Equal(44_000, totals.RollSumHundredths);
        Assert.Equal(1, totals.ForcedHits);
        Assert.Equal(2, totals.Autohits);
        Assert.Equal(7, totals.TotalDefeated);
        Assert.Equal(4, totals.MyDefeats);
    }

    [Fact]
    public void Invalid_baseline_rejects_before_subtraction()
    {
        var result = CharacterPerformanceCombatProjection.Project(
            Totals(attempts: 5, hits: 10),
            Totals(attempts: 20, hits: 15));

        Assert.Equal(CharacterPerformanceCombatProjectionOutcome.InvalidBaseline, result.Outcome);
        Assert.Contains("hits cannot exceed attempts", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Invalid_current_rejects_before_subtraction()
    {
        var result = CharacterPerformanceCombatProjection.Project(
            Totals(attempts: 10, hits: 8),
            Totals(attempts: 5, hits: 10));

        Assert.Equal(CharacterPerformanceCombatProjectionOutcome.InvalidCurrent, result.Outcome);
        Assert.Contains("hits cannot exceed attempts", result.Detail, StringComparison.Ordinal);
    }

    private static CharacterPerformanceCombatDelta AssertSuccess(
        CharacterPerformanceCombatProjectionResult result)
    {
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Delta);
        return result.Delta!;
    }

    private static CharacterPerformanceCombatTotals Totals(
        long damageHundredths = 0,
        long attempts = 0,
        long hits = 0,
        long rolledAttempts = 0,
        long chanceSum = 0,
        long rollSum = 0,
        long forcedHits = 0,
        long autohits = 0,
        long totalDefeated = 0,
        long myDefeats = 0) =>
        new()
        {
            DamageDealt = new CombatScaledAmount(damageHundredths),
            Attempts = attempts,
            Hits = hits,
            RolledAttempts = rolledAttempts,
            DisplayedChanceSumHundredths = chanceSum,
            RollSumHundredths = rollSum,
            ForcedHits = forcedHits,
            Autohits = autohits,
            TotalDefeated = totalDefeated,
            MyDefeats = myDefeats
        };
}
