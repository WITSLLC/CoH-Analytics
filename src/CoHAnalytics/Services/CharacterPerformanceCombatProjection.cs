using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

/// <summary>
/// Pure historical projection from session-scope combat baselines to combat deltas.
/// </summary>
public static class CharacterPerformanceCombatProjection
{
    public static CharacterPerformanceCombatProjectionResult Project(
        CharacterPerformanceCombatTotals baseline,
        CharacterPerformanceCombatTotals current)
    {
        if (!TryValidateCumulativeTotals(baseline, out var baselineError))
        {
            return Failure(
                CharacterPerformanceCombatProjectionOutcome.InvalidBaseline,
                baselineError);
        }

        if (!TryValidateCumulativeTotals(current, out var currentError))
        {
            return Failure(
                CharacterPerformanceCombatProjectionOutcome.InvalidCurrent,
                currentError);
        }

        if (!TrySubtractNonNegative(
                baseline.DamageDealt.Hundredths,
                current.DamageDealt.Hundredths,
                "damageDealt",
                out var damageDeltaHundredths,
                out var regressionError))
        {
            return Failure(
                CharacterPerformanceCombatProjectionOutcome.CounterRegression,
                regressionError);
        }

        if (!TrySubtractCounter(baseline.Attempts, current.Attempts, "attempts", out var attemptsDelta, out regressionError)
            || !TrySubtractCounter(baseline.Hits, current.Hits, "hits", out var hitsDelta, out regressionError)
            || !TrySubtractCounter(
                baseline.RolledAttempts,
                current.RolledAttempts,
                "rolledAttempts",
                out var rolledAttemptsDelta,
                out regressionError)
            || !TrySubtractCounter(
                baseline.DisplayedChanceSumHundredths,
                current.DisplayedChanceSumHundredths,
                "displayedChanceSumHundredths",
                out var chanceSumDelta,
                out regressionError)
            || !TrySubtractCounter(
                baseline.RollSumHundredths,
                current.RollSumHundredths,
                "rollSumHundredths",
                out var rollSumDelta,
                out regressionError)
            || !TrySubtractCounter(
                baseline.ForcedHits,
                current.ForcedHits,
                "forcedHits",
                out var forcedHitsDelta,
                out regressionError)
            || !TrySubtractCounter(
                baseline.Autohits,
                current.Autohits,
                "autohits",
                out var autohitsDelta,
                out regressionError)
            || !TrySubtractCounter(
                baseline.TotalDefeated,
                current.TotalDefeated,
                "totalDefeated",
                out var totalDefeatedDelta,
                out regressionError)
            || !TrySubtractCounter(
                baseline.MyDefeats,
                current.MyDefeats,
                "myDefeats",
                out var myDefeatsDelta,
                out regressionError))
        {
            return Failure(
                CharacterPerformanceCombatProjectionOutcome.CounterRegression,
                regressionError);
        }

        var delta = new CharacterPerformanceCombatDelta
        {
            DamageDealt = new CombatScaledAmount(damageDeltaHundredths),
            Attempts = attemptsDelta,
            Hits = hitsDelta,
            RolledAttempts = rolledAttemptsDelta,
            DisplayedChanceSumHundredths = chanceSumDelta,
            RollSumHundredths = rollSumDelta,
            ForcedHits = forcedHitsDelta,
            Autohits = autohitsDelta,
            TotalDefeated = totalDefeatedDelta,
            MyDefeats = myDefeatsDelta
        };

        if (!TryValidateDelta(delta, out var deltaError))
        {
            return Failure(
                CharacterPerformanceCombatProjectionOutcome.InvalidDelta,
                deltaError);
        }

        return new CharacterPerformanceCombatProjectionResult
        {
            Outcome = CharacterPerformanceCombatProjectionOutcome.Success,
            Delta = delta
        };
    }

    internal static bool TryValidateCumulativeTotals(
        CharacterPerformanceCombatTotals totals,
        out string error)
    {
        if (totals.DamageDealt.Hundredths < 0)
        {
            error = "damageDealt must be non-negative";
            return false;
        }

        if (totals.Attempts < 0
            || totals.Hits < 0
            || totals.RolledAttempts < 0
            || totals.DisplayedChanceSumHundredths < 0
            || totals.RollSumHundredths < 0
            || totals.ForcedHits < 0
            || totals.Autohits < 0)
        {
            error = "accuracy counters and sums must be non-negative";
            return false;
        }

        if (totals.Hits > totals.Attempts)
        {
            error = "hits cannot exceed attempts";
            return false;
        }

        if (totals.ForcedHits > totals.Hits)
        {
            error = "forcedHits cannot exceed hits";
            return false;
        }

        if (totals.RolledAttempts > totals.Attempts)
        {
            error = "rolledAttempts cannot exceed attempts";
            return false;
        }

        if (totals.TotalDefeated < 0 || totals.MyDefeats < 0)
        {
            error = "enemy totals must be non-negative";
            return false;
        }

        if (totals.MyDefeats > totals.TotalDefeated)
        {
            error = "myDefeats cannot exceed totalDefeated";
            return false;
        }

        error = string.Empty;
        return true;
    }

    internal static bool TryValidateDelta(CharacterPerformanceCombatDelta delta, out string error)
    {
        if (delta.DamageDealt.Hundredths < 0)
        {
            error = "damageDealt delta must be non-negative";
            return false;
        }

        if (delta.Attempts < 0
            || delta.Hits < 0
            || delta.RolledAttempts < 0
            || delta.DisplayedChanceSumHundredths < 0
            || delta.RollSumHundredths < 0
            || delta.ForcedHits < 0
            || delta.Autohits < 0)
        {
            error = "accuracy counter deltas and sum deltas must be non-negative";
            return false;
        }

        if (delta.Hits > delta.Attempts)
        {
            error = "hits delta cannot exceed attempts delta";
            return false;
        }

        if (delta.ForcedHits > delta.Hits)
        {
            error = "forcedHits delta cannot exceed hits delta";
            return false;
        }

        if (delta.RolledAttempts > delta.Attempts)
        {
            error = "rolledAttempts delta cannot exceed attempts delta";
            return false;
        }

        if (delta.TotalDefeated < 0 || delta.MyDefeats < 0)
        {
            error = "enemy total deltas must be non-negative";
            return false;
        }

        if (delta.MyDefeats > delta.TotalDefeated)
        {
            error = "myDefeats delta cannot exceed totalDefeated delta";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static bool TrySubtractCounter(
        long baseline,
        long current,
        string fieldName,
        out long delta,
        out string error)
    {
        if (current < baseline)
        {
            delta = 0;
            error = $"{fieldName} regressed from {baseline} to {current}";
            return false;
        }

        delta = current - baseline;
        error = string.Empty;
        return true;
    }

    private static bool TrySubtractNonNegative(
        long baselineHundredths,
        long currentHundredths,
        string fieldName,
        out long deltaHundredths,
        out string error)
    {
        if (currentHundredths < baselineHundredths)
        {
            deltaHundredths = 0;
            error = $"{fieldName} regressed from {baselineHundredths} to {currentHundredths}";
            return false;
        }

        deltaHundredths = currentHundredths - baselineHundredths;
        error = string.Empty;
        return true;
    }

    private static CharacterPerformanceCombatProjectionResult Failure(
        CharacterPerformanceCombatProjectionOutcome outcome,
        string detail) =>
        new()
        {
            Outcome = outcome,
            Detail = detail
        };
}
