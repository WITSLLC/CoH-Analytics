using CoHAnalytics.Models;
using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Services;

public sealed class CombatAggregatorTests
{
    private static readonly DateTimeOffset SessionStart = new(2026, 8, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Known_event_sequence_produces_exact_health_flow_totals()
    {
        var aggregator = new CombatAggregator();
        var contextId = MonitoringContextId.CreateNew();

        foreach (var combatEvent in BuildSampleSequence(contextId))
        {
            aggregator.Apply(combatEvent);
        }

        var snapshot = aggregator.ToSnapshot(
            SessionStart,
            new DateTimeOffset(2026, 8, 4, 12, 0, 20, TimeSpan.Zero),
            timingEndAt: null,
            CombatActivityDefaults.IdleThreshold);

        Assert.Equal(new CombatScaledAmount(1388 + 559 + 1036), snapshot.DamageDealt);
        Assert.Equal(new CombatScaledAmount(2215), snapshot.DamageReceived);
        Assert.Equal(new CombatScaledAmount(7850), snapshot.HealingDealt);
        Assert.Equal(new CombatScaledAmount(4225), snapshot.HealingReceived);
        Assert.Equal(1, snapshot.TotalDefeated);
        Assert.Equal(1, snapshot.MyDefeats);
        Assert.Equal(1, snapshot.PowerActivations);
    }

    [Fact]
    public void Dot_contributes_normally_to_damage_dealt()
    {
        var aggregator = new CombatAggregator();
        var contextId = MonitoringContextId.CreateNew();

        aggregator.Apply(Event(contextId, 1, CombatEventKind.DamageDealt, amountHundredths: 1388));
        aggregator.Apply(Event(contextId, 2, CombatEventKind.DamageDealt, amountHundredths: 559, isOverTime: true));

        var snapshot = aggregator.ToSnapshot(
            SessionStart,
            SessionStart.AddSeconds(2),
            null,
            CombatActivityDefaults.IdleThreshold);

        Assert.Equal(new CombatScaledAmount(1947), snapshot.DamageDealt);
    }

    [Fact]
    public void Each_defeat_event_increments_exactly_once()
    {
        var aggregator = new CombatAggregator();
        var contextId = MonitoringContextId.CreateNew();

        aggregator.Apply(Event(contextId, 1, CombatEventKind.Defeat));
        aggregator.Apply(Event(contextId, 2, CombatEventKind.Defeat));

        var snapshot = aggregator.ToSnapshot(SessionStart, SessionStart.AddSeconds(2), null, CombatActivityDefaults.IdleThreshold);
        Assert.Equal(2, snapshot.TotalDefeated);
        Assert.Equal(2, snapshot.MyDefeats);
    }

    [Fact]
    public void Each_activation_increments_exactly_once()
    {
        var aggregator = new CombatAggregator();
        var contextId = MonitoringContextId.CreateNew();

        aggregator.Apply(Event(contextId, 1, CombatEventKind.PowerActivation));
        aggregator.Apply(Event(contextId, 2, CombatEventKind.PowerActivation));

        var snapshot = aggregator.ToSnapshot(SessionStart, SessionStart.AddSeconds(2), null, CombatActivityDefaults.IdleThreshold);
        Assert.Equal(2, snapshot.PowerActivations);
    }

    [Fact]
    public void Qualifying_event_transitions_idle_to_active_and_sets_last_combat_at()
    {
        var aggregator = new CombatAggregator();
        var contextId = MonitoringContextId.CreateNew();
        var observedAt = SessionStart.AddSeconds(5);

        aggregator.Apply(Event(contextId, 1, CombatEventKind.DamageDealt, observedAt: observedAt, amountHundredths: 100));

        var snapshot = aggregator.ToSnapshot(
            SessionStart,
            observedAt,
            null,
            CombatActivityDefaults.IdleThreshold);

        Assert.True(snapshot.IsInCombat);
        Assert.Equal(observedAt, snapshot.LastCombatAt);
    }

    [Fact]
    public void Idle_threshold_transitions_active_to_idle_and_clears_engagement_duration()
    {
        var aggregator = new CombatAggregator();
        var contextId = MonitoringContextId.CreateNew();
        var first = SessionStart;
        var reference = SessionStart.Add(CombatActivityDefaults.IdleThreshold);

        aggregator.Apply(Event(contextId, 1, CombatEventKind.DamageDealt, observedAt: first, amountHundredths: 100));

        var snapshot = aggregator.ToSnapshot(SessionStart, reference, null, CombatActivityDefaults.IdleThreshold);

        Assert.False(snapshot.IsInCombat);
        Assert.Equal(TimeSpan.Zero, snapshot.CurrentEngagementDuration);
        Assert.Equal(first, snapshot.LastCombatAt);
    }

    [Fact]
    public void Engagement_duration_increases_during_active_period_and_is_not_used_for_dps()
    {
        var aggregator = new CombatAggregator();
        var contextId = MonitoringContextId.CreateNew();
        var first = SessionStart;
        var second = SessionStart.AddSeconds(4);
        var reference = SessionStart.AddSeconds(6);

        aggregator.Apply(Event(contextId, 1, CombatEventKind.DamageDealt, observedAt: first, amountHundredths: 1000));
        aggregator.Apply(Event(contextId, 2, CombatEventKind.DamageDealt, observedAt: second, amountHundredths: 500));

        var snapshot = aggregator.ToSnapshot(SessionStart, reference, null, CombatActivityDefaults.IdleThreshold);

        Assert.True(snapshot.IsInCombat);
        Assert.Equal(TimeSpan.FromSeconds(6), snapshot.CurrentEngagementDuration);
        Assert.Equal(
            CombatAggregator.CalculateSessionDamagePerSecondHundredths(
                new CombatScaledAmount(1500),
                TimeSpan.FromSeconds(6)),
            snapshot.SessionDamagePerSecondHundredths);
    }

    [Fact]
    public void Freeze_clears_in_combat_state_for_final_snapshot()
    {
        var aggregator = new CombatAggregator();
        var contextId = MonitoringContextId.CreateNew();
        var observedAt = SessionStart.AddSeconds(2);

        aggregator.Apply(Event(contextId, 1, CombatEventKind.DamageDealt, observedAt: observedAt, amountHundredths: 100));
        aggregator.Freeze(observedAt.AddSeconds(1));

        var snapshot = aggregator.ToSnapshot(
            SessionStart,
            observedAt.AddSeconds(1),
            observedAt.AddSeconds(1),
            CombatActivityDefaults.IdleThreshold);

        Assert.False(snapshot.IsInCombat);
        Assert.Equal(new CombatScaledAmount(100), snapshot.DamageDealt);
    }

    private static IEnumerable<CombatEvent> BuildSampleSequence(MonitoringContextId contextId) =>
    [
        Event(contextId, 1, CombatEventKind.DamageDealt, amountHundredths: 1388),
        Event(contextId, 2, CombatEventKind.DamageDealt, amountHundredths: 559, isOverTime: true),
        Event(contextId, 3, CombatEventKind.DamageDealt, amountHundredths: 1036, isOverTime: true),
        Event(contextId, 4, CombatEventKind.DamageReceived, amountHundredths: 2215),
        Event(contextId, 5, CombatEventKind.HealingDealt, amountHundredths: 7850),
        Event(contextId, 6, CombatEventKind.HealingReceived, amountHundredths: 4225),
        Event(contextId, 7, CombatEventKind.PowerActivation),
        Event(contextId, 8, CombatEventKind.Defeat)
    ];

    private static CombatEvent Event(
        MonitoringContextId contextId,
        long sequence,
        CombatEventKind kind,
        DateTimeOffset? observedAt = null,
        long amountHundredths = 0,
        bool isOverTime = false) =>
        new()
        {
            ContextId = contextId,
            ParserSequence = sequence,
            ObservedAt = observedAt ?? SessionStart.AddSeconds(sequence - 1),
            SourceTimestamp = SessionStart.AddSeconds(sequence - 1).DateTime,
            Kind = kind,
            GrammarId = CombatGrammarId.Dmg01YouHitWithPower,
            ActorRole = CombatActorRole.Self,
            Amount = new CombatScaledAmount(amountHundredths),
            IsOverTime = isOverTime
        };
}
