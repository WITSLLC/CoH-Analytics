using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

/// <summary>
/// Mutable attack-resolution counters shared by session, tracked, and rolling combat scopes.
/// </summary>
public sealed class CombatAccuracyAccumulator
{
    private CombatAccuracyCounterState _state;

    public void Apply(CombatEvent combatEvent) => _state.Apply(combatEvent);

    public void Apply(CanonicalCombatEvent canonicalEvent)
    {
        if (canonicalEvent.Family != CombatEventFamily.AttackResolution
            || canonicalEvent.Outcome is not { } outcome)
        {
            return;
        }

        var wasRolled = !canonicalEvent.Delivery.HasFlag(DeliveryFlags.Forced)
            && !canonicalEvent.Delivery.HasFlag(DeliveryFlags.Autohit);
        _state.Apply(
            canonicalEvent.GrammarId,
            outcome,
            canonicalEvent.Delivery.HasFlag(DeliveryFlags.Forced),
            wasRolled,
            canonicalEvent.DisplayedChanceHundredths,
            canonicalEvent.RollHundredths);
    }

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

        Apply(
            combatEvent.GrammarId,
            outcome,
            combatEvent.WasForced,
            combatEvent.WasRolled,
            combatEvent.DisplayedChanceHundredths,
            combatEvent.RollHundredths);
    }

    public void Apply(
        CombatGrammarId grammarId,
        CombatAttackOutcome outcome,
        bool wasForced,
        bool wasRolled,
        long? displayedChanceHundredths,
        long? rollHundredths)
    {
        if (grammarId == CombatGrammarId.Acc04Autohit)
        {
            Autohits++;
            return;
        }

        if (!CombatAccuracyAccumulator.CountsTowardOffensiveAccuracy(grammarId))
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

        if (wasForced)
        {
            ForcedHits++;
        }

        if (wasRolled
            && displayedChanceHundredths is { } chance
            && rollHundredths is { } roll)
        {
            RolledAttempts++;
            DisplayedChanceSumHundredths += chance;
            RollSumHundredths += roll;
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
