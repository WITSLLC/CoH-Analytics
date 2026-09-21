using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

/// <summary>
/// Mutable attack-resolution counters shared by session, tracked, and rolling combat scopes.
/// </summary>
public sealed class CombatAccuracyAccumulator
{
    private CombatAccuracyCounterState _state;

    public void Apply(CombatEvent combatEvent) => _state.Apply(combatEvent);

    internal static bool CountsTowardOffensiveAccuracy(CombatGrammarId grammarId) =>
        grammarId is CombatGrammarId.Acc01RolledHit
            or CombatGrammarId.Acc02RolledMiss
            or CombatGrammarId.Acc03ForcedHit;

    public void Reset() => _state = default;

    public CombatAccuracyScopeSnapshot ToSnapshot() => _state.ToSnapshot();
}

/// <summary>Mutable attack-resolution counters that can be merged across rolling buckets.</summary>
internal struct CombatAccuracyCounterState
{
    public long Attempts;

    public long Hits;

    public long Misses;

    public long RolledAttempts;

    public long DisplayedChanceSumHundredths;

    public long RollSumHundredths;

    public long ForcedHits;

    public long Autohits;

    public void Apply(CombatEvent combatEvent)
    {
        if (combatEvent.Kind != CombatEventKind.AttackResolution
            || combatEvent.AttackOutcome is not { } outcome)
        {
            return;
        }

        if (combatEvent.GrammarId == CombatGrammarId.Acc04Autohit)
        {
            Autohits++;
            return;
        }

        if (!CombatAccuracyAccumulator.CountsTowardOffensiveAccuracy(combatEvent.GrammarId))
        {
            return;
        }

        Attempts++;
        if (outcome == CombatAttackOutcome.Hit)
        {
            Hits++;
        }
        else
        {
            Misses++;
        }

        if (combatEvent.WasForced)
        {
            ForcedHits++;
        }

        if (combatEvent.WasRolled
            && combatEvent.DisplayedChanceHundredths is { } displayedChanceHundredths
            && combatEvent.RollHundredths is { } rollHundredths)
        {
            RolledAttempts++;
            DisplayedChanceSumHundredths += displayedChanceHundredths;
            RollSumHundredths += rollHundredths;
        }
    }

    public void Add(in CombatAccuracyCounterState other)
    {
        Attempts += other.Attempts;
        Hits += other.Hits;
        Misses += other.Misses;
        RolledAttempts += other.RolledAttempts;
        DisplayedChanceSumHundredths += other.DisplayedChanceSumHundredths;
        RollSumHundredths += other.RollSumHundredths;
        ForcedHits += other.ForcedHits;
        Autohits += other.Autohits;
    }

    public CombatAccuracyScopeSnapshot ToSnapshot() =>
        new()
        {
            Attempts = Attempts,
            Hits = Hits,
            Misses = Misses,
            RolledAttempts = RolledAttempts,
            DisplayedChanceSumHundredths = DisplayedChanceSumHundredths,
            RollSumHundredths = RollSumHundredths,
            ForcedHits = ForcedHits,
            Autohits = Autohits
        };
}
