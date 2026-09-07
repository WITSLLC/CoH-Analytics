using CoHAnalytics.Models;
using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Services;

public sealed class CombatAccuracyInitializationTests
{
    private static readonly DateTimeOffset SessionStart =
        new(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Fresh_session_accuracy_is_unavailable()
    {
        var snapshot = new CombatAggregator().ToSnapshot(
            SessionStart,
            SessionStart.AddMinutes(1),
            timingEndAt: null,
            CombatActivityDefaults.IdleThreshold);

        Assert.Equal(CombatAccuracyScopeSnapshot.Empty, snapshot.Accuracy);
        Assert.Equal("—", CombatAccuracyPresentation.BuildSummaryLabel(CombatAccuracyPresentation.Build(snapshot.Accuracy)));
    }

    [Fact]
    public void First_offensive_hit_displays_one_hundred_percent()
    {
        var aggregator = new CombatAggregator();
        aggregator.Apply(RolledResolution(1, CombatAttackOutcome.Hit));

        var snapshot = aggregator.ToSnapshot(
            SessionStart,
            SessionStart.AddMinutes(1),
            timingEndAt: null,
            CombatActivityDefaults.IdleThreshold);

        Assert.Equal(1, snapshot.Accuracy.Attempts);
        Assert.Equal(1, snapshot.Accuracy.Hits);
        Assert.Equal(0, snapshot.Accuracy.Misses);
        Assert.Equal("100.0%", CombatAccuracyPresentation.Build(snapshot.Accuracy).HitPercentLabel);
    }

    [Fact]
    public void First_offensive_miss_displays_zero_percent()
    {
        var aggregator = new CombatAggregator();
        aggregator.Apply(RolledResolution(1, CombatAttackOutcome.Miss));

        var snapshot = aggregator.ToSnapshot(
            SessionStart,
            SessionStart.AddMinutes(1),
            timingEndAt: null,
            CombatActivityDefaults.IdleThreshold);

        Assert.Equal(1, snapshot.Accuracy.Attempts);
        Assert.Equal(0, snapshot.Accuracy.Hits);
        Assert.Equal(1, snapshot.Accuracy.Misses);
        Assert.Equal("0.0%", CombatAccuracyPresentation.Build(snapshot.Accuracy).HitPercentLabel);
    }

    [Fact]
    public void Mixed_offensive_attempts_display_expected_hit_percent()
    {
        var aggregator = new CombatAggregator();
        for (var sequence = 1; sequence <= 9; sequence++)
        {
            aggregator.Apply(RolledResolution(sequence, CombatAttackOutcome.Hit));
        }

        aggregator.Apply(RolledResolution(10, CombatAttackOutcome.Miss));

        var snapshot = aggregator.ToSnapshot(
            SessionStart,
            SessionStart.AddMinutes(1),
            timingEndAt: null,
            CombatActivityDefaults.IdleThreshold);

        Assert.Equal(10, snapshot.Accuracy.Attempts);
        Assert.Equal(9, snapshot.Accuracy.Hits);
        Assert.Equal(1, snapshot.Accuracy.Misses);
        Assert.Equal("90.0%", CombatAccuracyPresentation.Build(snapshot.Accuracy).HitPercentLabel);
    }

    [Fact]
    public void Support_only_session_leaves_accuracy_unavailable()
    {
        var aggregator = new CombatAggregator();
        var supportLines = new[]
        {
            "2026-08-10 12:00:00 Dawn's Vanguard heals you with their Panacea: Chance for +Hit Points/Endurance for 78.93 health points.",
            "2026-08-10 12:00:01 You heal Dawn's Vanguard with Panacea: Chance for +Hit Points/Endurance for 78.93 health points.",
            "2026-08-10 12:00:02 Dawn's Vanguard hits you with their Panacea: Chance for +Hit Points/Endurance granting you 10.23 points of endurance.",
            "2026-08-10 12:00:03 You hit Dawn's Vanguard with your Panacea: Chance for +Hit Points/Endurance granting them 10.23 points of endurance.",
            "2026-08-10 12:00:04 You activated the Speed Boost power.",
            "2026-08-10 12:00:05 You activate Speed Boost.",
            "2026-08-10 12:00:06 HIT Dawn's Vanguard! Your Panacea: Chance for +Hit Points/Endurance power is autohit.",
            "2026-08-10 12:00:07 HIT Dawn's Vanguard! Your Panacea power is autohit.",
            "2026-08-10 12:00:08 HIT Ally! Your Speed Boost power is autohit."
        };

        var sequence = 1L;
        foreach (var line in supportLines)
        {
            if (CombatEventParserTestSupport.TryParseLine(line, out var combatEvent, sequence))
            {
                aggregator.Apply(combatEvent);
            }

            sequence++;
        }

        var snapshot = aggregator.ToSnapshot(
            SessionStart,
            SessionStart.AddMinutes(1),
            timingEndAt: null,
            CombatActivityDefaults.IdleThreshold);

        Assert.Equal(0, snapshot.Accuracy.Attempts);
        Assert.Equal(0, snapshot.Accuracy.Hits);
        Assert.Equal(0, snapshot.Accuracy.Misses);
        Assert.Equal(3, snapshot.Accuracy.Autohits);
        Assert.Equal("—", CombatAccuracyPresentation.BuildSummaryLabel(CombatAccuracyPresentation.Build(snapshot.Accuracy)));
    }

    [Fact]
    public void Fresh_session_after_prior_offensive_data_resets_accuracy()
    {
        var prior = new CombatAggregator();
        prior.Apply(RolledResolution(1, CombatAttackOutcome.Hit));

        var fresh = new CombatAggregator();
        var snapshot = fresh.ToSnapshot(
            SessionStart,
            SessionStart.AddMinutes(1),
            timingEndAt: null,
            CombatActivityDefaults.IdleThreshold);

        Assert.Equal(CombatAccuracyScopeSnapshot.Empty, snapshot.Accuracy);
    }

    [Fact]
    public void Tracked_accuracy_does_not_affect_session_overview_accuracy()
    {
        var session = new CombatAggregator();
        session.Tracked.Start(SessionStart);
        session.Tracked.Apply(RolledResolution(1, CombatAttackOutcome.Hit));
        session.Apply(RolledResolution(2, CombatAttackOutcome.Miss));

        var snapshot = session.ToSnapshot(
            SessionStart,
            SessionStart.AddMinutes(1),
            timingEndAt: null,
            CombatActivityDefaults.IdleThreshold);

        Assert.Equal(1, snapshot.Accuracy.Attempts);
        Assert.Equal(0, snapshot.Accuracy.Hits);
        Assert.Equal(1, snapshot.Accuracy.Misses);
        Assert.Equal(1, snapshot.Tracked.Accuracy.Attempts);
        Assert.Equal(1, snapshot.Tracked.Accuracy.Hits);
    }

    private static CombatEvent RolledResolution(long sequence, CombatAttackOutcome outcome) =>
        new()
        {
            ContextId = MonitoringContextId.CreateNew(),
            ParserSequence = sequence,
            ObservedAt = SessionStart.AddSeconds(sequence),
            Kind = CombatEventKind.AttackResolution,
            GrammarId = outcome == CombatAttackOutcome.Hit
                ? CombatGrammarId.Acc01RolledHit
                : CombatGrammarId.Acc02RolledMiss,
            AttackOutcome = outcome,
            WasRolled = true,
            DisplayedChanceHundredths = 9_500,
            RollHundredths = outcome == CombatAttackOutcome.Hit ? 5_151 : 9_754
        };
}
