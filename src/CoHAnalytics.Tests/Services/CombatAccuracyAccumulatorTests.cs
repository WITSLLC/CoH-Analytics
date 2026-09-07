using CoHAnalytics.Models;
using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Services;

public sealed class CombatAccuracyAccumulatorTests
{
    private static readonly DateTimeOffset SessionStart =
        new(2026, 8, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Session_aggregation_counts_attempts_hits_and_misses()
    {
        var aggregator = new CombatAggregator();
        ApplyRolledHit(aggregator, 1);
        ApplyRolledMiss(aggregator, 2);

        var snapshot = aggregator.ToSnapshot(SessionStart, SessionStart.AddMinutes(1), null, CombatActivityDefaults.IdleThreshold);

        Assert.Equal(2, snapshot.Accuracy.Attempts);
        Assert.Equal(1, snapshot.Accuracy.Hits);
        Assert.Equal(1, snapshot.Accuracy.Misses);
        Assert.Equal(2, snapshot.Accuracy.RolledAttempts);
        Assert.Equal(19_000, snapshot.Accuracy.DisplayedChanceSumHundredths);
        Assert.Equal(14_905, snapshot.Accuracy.RollSumHundredths);
    }

    [Fact]
    public void Forced_hit_counts_as_attempt_but_autohit_does_not()
    {
        var aggregator = new CombatAggregator();
        ApplyForcedHit(aggregator, 1);
        ApplyAutohit(aggregator, 2);

        var snapshot = aggregator.ToSnapshot(SessionStart, SessionStart.AddMinutes(1), null, CombatActivityDefaults.IdleThreshold);

        Assert.Equal(1, snapshot.Accuracy.Attempts);
        Assert.Equal(1, snapshot.Accuracy.Hits);
        Assert.Equal(0, snapshot.Accuracy.Misses);
        Assert.Equal(0, snapshot.Accuracy.RolledAttempts);
        Assert.Equal(1, snapshot.Accuracy.ForcedHits);
        Assert.Equal(1, snapshot.Accuracy.Autohits);
    }

    [Fact]
    public void Tracked_pause_excludes_resolution_events()
    {
        var tracked = new TrackedCombatAccumulator();
        tracked.Start(SessionStart);
        tracked.Pause(SessionStart.AddSeconds(30));

        tracked.Apply(ResolutionEvent(1, CombatAttackOutcome.Hit, wasRolled: true, 9500, 5151));
        tracked.Apply(ResolutionEvent(2, CombatAttackOutcome.Miss, wasRolled: true, 9500, 9754));

        var snapshot = tracked.ToSnapshot(SessionStart.AddMinutes(1));

        Assert.Equal(TrackedCombatScopeSnapshot.Empty.Accuracy, snapshot.Accuracy);
    }

    [Fact]
    public void Multi_context_accuracy_remains_isolated()
    {
        var aggregatorA = new CombatAggregator();
        var aggregatorB = new CombatAggregator();
        ApplyRolledHit(aggregatorA, 1);
        ApplyRolledMiss(aggregatorB, 1);

        var snapshotA = aggregatorA.ToSnapshot(SessionStart, SessionStart.AddMinutes(1), null, CombatActivityDefaults.IdleThreshold);
        var snapshotB = aggregatorB.ToSnapshot(SessionStart, SessionStart.AddMinutes(1), null, CombatActivityDefaults.IdleThreshold);

        Assert.Equal(1, snapshotA.Accuracy.Attempts);
        Assert.Equal(1, snapshotA.Accuracy.Hits);
        Assert.Equal(1, snapshotB.Accuracy.Attempts);
        Assert.Equal(1, snapshotB.Accuracy.Misses);
    }

    [Fact]
    public void New_gameplay_session_gets_empty_accuracy_counters()
    {
        var aggregator = new CombatAggregator();
        ApplyRolledHit(aggregator, 1);

        var fresh = new CombatAggregator();
        var snapshot = fresh.ToSnapshot(SessionStart, SessionStart.AddMinutes(1), null, CombatActivityDefaults.IdleThreshold);

        Assert.Equal(CombatAccuracyScopeSnapshot.Empty, snapshot.Accuracy);
    }

    [Fact]
    public void Accuracy_unavailable_state_does_not_render_false_zeros()
    {
        var presentation = CombatAccuracyPresentation.Build(CombatAccuracyScopeSnapshot.Empty);

        Assert.Equal(CombatAccuracyAvailability.NotObserved, presentation.Availability);
        Assert.False(presentation.ShowMetrics);
        Assert.Equal("—", presentation.AttemptsLabel);
        Assert.Contains("Detailed hit-roll data has not been observed", presentation.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Aoe_activation_with_multiple_target_resolutions_counts_each_attempt()
    {
        var aggregator = new CombatAggregator();
        aggregator.Apply(ResolutionEvent(1, CombatAttackOutcome.Hit, wasRolled: true, 7550, 3086));
        aggregator.Apply(ResolutionEvent(2, CombatAttackOutcome.Hit, wasRolled: true, 7550, 4122));
        aggregator.Apply(ResolutionEvent(3, CombatAttackOutcome.Miss, wasRolled: true, 7550, 9754));

        var snapshot = aggregator.ToSnapshot(SessionStart, SessionStart.AddMinutes(1), null, CombatActivityDefaults.IdleThreshold);

        Assert.Equal(3, snapshot.Accuracy.Attempts);
        Assert.Equal(2, snapshot.Accuracy.Hits);
        Assert.Equal(1, snapshot.Accuracy.Misses);
        Assert.Equal(3, snapshot.Accuracy.RolledAttempts);
        Assert.Equal(22_650, snapshot.Accuracy.DisplayedChanceSumHundredths);
    }

    [Fact]
    public void Formatting_renders_percentages_and_averages_correctly()
    {
        var accuracy = new CombatAccuracyScopeSnapshot
        {
            Attempts = 9,
            Hits = 8,
            Misses = 1,
            RolledAttempts = 7,
            DisplayedChanceSumHundredths = 59_090,
            RollSumHundredths = 34_337,
            ForcedHits = 1,
            Autohits = 1
        };

        var presentation = CombatAccuracyPresentation.Build(accuracy);

        Assert.Equal("9", presentation.AttemptsLabel);
        Assert.Equal("8", presentation.HitsLabel);
        Assert.Equal("1", presentation.MissesLabel);
        Assert.Equal("88.9%", presentation.HitPercentLabel);
        Assert.Equal("84.4%", presentation.AverageChanceLabel);
        Assert.Equal("49.1", presentation.AverageRollLabel);
        Assert.Equal("1", presentation.ForcedHitsLabel);
        Assert.Equal("1", presentation.AutohitsLabel);
    }

    private static void ApplyRolledHit(CombatAggregator aggregator, long sequence) =>
        aggregator.Apply(ResolutionEvent(sequence, CombatAttackOutcome.Hit, wasRolled: true, 9500, 5151));

    private static void ApplyRolledMiss(CombatAggregator aggregator, long sequence) =>
        aggregator.Apply(ResolutionEvent(sequence, CombatAttackOutcome.Miss, wasRolled: true, 9500, 9754));

    private static void ApplyForcedHit(CombatAggregator aggregator, long sequence) =>
        aggregator.Apply(ResolutionEvent(sequence, CombatAttackOutcome.Hit, wasForced: true));

    private static void ApplyAutohit(CombatAggregator aggregator, long sequence) =>
        aggregator.Apply(ResolutionEvent(sequence, CombatAttackOutcome.Hit, isAutohit: true));

    private static CombatEvent ResolutionEvent(
        long sequence,
        CombatAttackOutcome outcome,
        bool wasRolled = false,
        long? displayedChanceHundredths = null,
        long? rollHundredths = null,
        bool wasForced = false,
        bool isAutohit = false) =>
        new()
        {
            ContextId = MonitoringContextId.CreateNew(),
            ParserSequence = sequence,
            ObservedAt = SessionStart.AddSeconds(sequence),
            Kind = CombatEventKind.AttackResolution,
            GrammarId = isAutohit
                ? CombatGrammarId.Acc04Autohit
                : wasForced
                    ? CombatGrammarId.Acc03ForcedHit
                    : outcome == CombatAttackOutcome.Miss
                        ? CombatGrammarId.Acc02RolledMiss
                        : CombatGrammarId.Acc01RolledHit,
            AttackOutcome = outcome,
            DisplayedChanceHundredths = displayedChanceHundredths,
            RollHundredths = rollHundredths,
            WasRolled = wasRolled,
            WasForced = wasForced,
            IsAutohit = isAutohit
        };
}
