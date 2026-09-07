using CoHAnalytics.Models;
using CoHAnalytics.Services;
using CoHAnalytics.Tests.Replay;

namespace CoHAnalytics.Tests.Services;

public sealed class CombatAggregationReplayOracleTests
{
    [Fact]
    public void Combat_core_grammar_fixture_produces_exact_session_combat_snapshot()
    {
        var fixturePath = ReplayTestPaths.Fixture("combat-core-grammar.log");
        var lines = File.ReadAllLines(fixturePath);
        var contextId = MonitoringContextId.CreateNew();
        var events = CombatEventParserTestSupport.ParseFixtureLines(
            lines,
            new DateOnly(2026, 8, 4),
            contextId);

        var aggregator = new CombatAggregator();
        foreach (var combatEvent in events)
        {
            aggregator.Apply(combatEvent);
        }

        var referenceAt = new DateTimeOffset(2026, 8, 4, 12, 0, 20, TimeSpan.Zero);
        var snapshot = aggregator.ToSnapshot(
            new DateTimeOffset(2026, 8, 4, 12, 0, 0, TimeSpan.Zero),
            referenceAt,
            timingEndAt: null,
            CombatActivityDefaults.IdleThreshold);

        var expected = BuildExpectedFixtureSnapshot(referenceAt);
        AssertCombatSnapshotEquivalent(expected, snapshot);
    }

    internal static CombatSnapshot BuildExpectedFixtureSnapshot(DateTimeOffset referenceAt)
    {
        var sessionStart = new DateTimeOffset(2026, 8, 4, 12, 0, 0, TimeSpan.Zero);
        var lastCombatAt = new DateTimeOffset(2026, 8, 4, 12, 0, 15, TimeSpan.Zero);
        var elapsed = referenceAt - sessionStart;
        var damageDealt = new CombatScaledAmount(8688);
        var isInCombat = referenceAt - lastCombatAt < CombatActivityDefaults.IdleThreshold;

        return new CombatSnapshot
        {
            DamageDealt = damageDealt,
            DamageReceived = new CombatScaledAmount(9215),
            HealingDealt = new CombatScaledAmount(9350),
            HealingReceived = new CombatScaledAmount(4225),
            TotalDefeated = 1,
            MyDefeats = 1,
            PowerActivations = 2,
            IsInCombat = isInCombat,
            LastCombatAt = lastCombatAt,
            CurrentEngagementDuration = isInCombat ? referenceAt - sessionStart : TimeSpan.Zero,
            SessionDamagePerSecondHundredths =
                CombatAggregator.CalculateSessionDamagePerSecondHundredths(damageDealt, elapsed),
            Accuracy = CombatAccuracyScopeSnapshot.Empty
        };
    }

    internal static void AssertCombatSnapshotEquivalent(CombatSnapshot expected, CombatSnapshot actual)
    {
        Assert.Equal(expected.DamageDealt, actual.DamageDealt);
        Assert.Equal(expected.DamageReceived, actual.DamageReceived);
        Assert.Equal(expected.HealingDealt, actual.HealingDealt);
        Assert.Equal(expected.HealingReceived, actual.HealingReceived);
        Assert.Equal(expected.TotalDefeated, actual.TotalDefeated);
        Assert.Equal(expected.MyDefeats, actual.MyDefeats);
        Assert.Equal(expected.PowerActivations, actual.PowerActivations);
        Assert.Equal(expected.IsInCombat, actual.IsInCombat);
        Assert.Equal(expected.LastCombatAt, actual.LastCombatAt);
        Assert.Equal(expected.CurrentEngagementDuration, actual.CurrentEngagementDuration);
        Assert.Equal(expected.SessionDamagePerSecondHundredths, actual.SessionDamagePerSecondHundredths);
        Assert.Equal(expected.Accuracy, actual.Accuracy);
        Assert.Equal(TrackedCombatScopeSnapshot.Empty, actual.Tracked);
    }
}
