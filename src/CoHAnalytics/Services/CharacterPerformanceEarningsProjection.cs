using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

/// <summary>
/// Pure historical projection from session-scope earnings baselines to earnings/timing deltas.
/// </summary>
public static class CharacterPerformanceEarningsProjection
{
    public static CharacterPerformanceEarningsProjectionResult Project(
        CharacterPerformanceEarningsTotals baseline,
        CharacterPerformanceEarningsTotals current,
        DateTimeOffset endedAtUtc)
    {
        if (!TryValidateBaseline(baseline, out var baselineError))
        {
            return Failure(
                CharacterPerformanceEarningsProjectionOutcome.InvalidBaseline,
                baselineError);
        }

        if (!TryValidateCumulativeTotals(current, out var currentError))
        {
            return Failure(
                CharacterPerformanceEarningsProjectionOutcome.InvalidCurrent,
                currentError);
        }

        var startedAtUtc = baseline.StartedAtUtc!.Value.ToUniversalTime();
        var endedAtUtcNormalized = endedAtUtc.ToUniversalTime();

        if (endedAtUtcNormalized <= startedAtUtc)
        {
            return Failure(
                CharacterPerformanceEarningsProjectionOutcome.InvalidBoundary,
                "endedAtUtc must be later than startedAtUtc");
        }

        if (!TrySubtractCounter(
                baseline.ExperienceGained,
                current.ExperienceGained,
                "experienceGained",
                out var experienceDelta,
                out var regressionError)
            || !TrySubtractCounter(
                baseline.GameplayInfluenceGained,
                current.GameplayInfluenceGained,
                "gameplayInfluenceGained",
                out var influenceDelta,
                out regressionError))
        {
            return Failure(
                CharacterPerformanceEarningsProjectionOutcome.CounterRegression,
                regressionError);
        }

        return new CharacterPerformanceEarningsProjectionResult
        {
            Outcome = CharacterPerformanceEarningsProjectionOutcome.Success,
            Delta = new CharacterPerformanceEarningsDelta
            {
                StartedAtUtc = startedAtUtc,
                EndedAtUtc = endedAtUtcNormalized,
                ExperienceGained = experienceDelta,
                GameplayInfluenceGained = influenceDelta
            }
        };
    }

    internal static bool TryValidateBaseline(
        CharacterPerformanceEarningsTotals baseline,
        out string error)
    {
        if (baseline.StartedAtUtc is null)
        {
            error = "startedAtUtc is required on the baseline";
            return false;
        }

        return TryValidateCumulativeTotals(baseline, out error);
    }

    internal static bool TryValidateCumulativeTotals(
        CharacterPerformanceEarningsTotals totals,
        out string error)
    {
        if (totals.ExperienceGained < 0 || totals.GameplayInfluenceGained < 0)
        {
            error = "earnings totals must be non-negative";
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

    private static CharacterPerformanceEarningsProjectionResult Failure(
        CharacterPerformanceEarningsProjectionOutcome outcome,
        string detail) =>
        new()
        {
            Outcome = outcome,
            Detail = detail
        };
}
