using CoHAnalytics.Models;
using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Services;

public sealed class RollingAnalyticsProductionParityTests
{
    [Fact]
    public void Live_session_exposes_session_and_rolling_operational_metrics_together()
    {
        var sessionStartedAt = LocalAt(2026, 8, 9, 17, 0, 0);
        var referenceAt = sessionStartedAt.AddMinutes(2);
        var contextId = MonitoringContextId.CreateNew();
        var combatAggregator = new CombatAggregator();
        var earningsAccumulator = new RollingEarningsAccumulator();

        for (var second = 1; second <= 120; second++)
        {
            var observedAt = sessionStartedAt.AddSeconds(second);
            var sourceTimestamp = observedAt.LocalDateTime;

            combatAggregator.Apply(new CombatEvent
            {
                ContextId = contextId,
                ParserSequence = second,
                ObservedAt = observedAt,
                SourceTimestamp = sourceTimestamp,
                Kind = CombatEventKind.DamageDealt,
                GrammarId = CombatGrammarId.Dmg01YouHitWithPower,
                Amount = new CombatScaledAmount(100)
            });

            combatAggregator.Apply(new CombatEvent
            {
                ContextId = contextId,
                ParserSequence = second + 1_000,
                ObservedAt = observedAt,
                SourceTimestamp = sourceTimestamp,
                Kind = CombatEventKind.AttackResolution,
                GrammarId = CombatGrammarId.Acc01RolledHit,
                ActorRole = CombatActorRole.Self,
                TargetName = "Example Target",
                PowerName = "Example Power",
                AttackOutcome = CombatAttackOutcome.Hit,
                WasRolled = true,
                DisplayedChanceHundredths = 9_000,
                RollHundredths = 5_000
            });

            earningsAccumulator.Apply(
                ParserEvent(second, observedAt, sourceTimestamp),
                experienceGained: 50,
                influenceGained: 5);
        }

        var combat = combatAggregator.ToSnapshot(
            sessionStartedAt,
            referenceAt,
            timingEndAt: null,
            CombatActivityDefaults.IdleThreshold);
        var rollingEarnings = earningsAccumulator.ToSnapshot(sessionStartedAt, referenceAt, timingEndAt: null);
        var context = new LiveMonitoringContextIdentityReadModel
        {
            ContextId = contextId,
            ContextState = MonitoringContextState.Ready,
            CharacterIdentityConfidence = CharacterIdentityConfidence.Confirmed,
            CharacterIdentityResolutionState = CharacterIdentityResolutionState.Resolved,
            SessionLifecycleState = GameplaySessionLifecycleState.Active,
            HasActiveSession = true,
            IdentityStatusLabel = "Confirmed",
            IdentityDetail = "Active character: Example Hero",
            SessionStartedAt = sessionStartedAt,
            SessionExperienceGained = 6_000,
            SessionGameplayInfluenceGained = 600,
            RetainedCombatEventCount = 240,
            Combat = combat,
            RollingEarnings = rollingEarnings
        };

        var combatMetrics = CombatAnalyticsPresentation.BuildMetrics(
            context,
            referenceAt,
            RollingCombatPresets.TenMinutes);
        Assert.NotEqual("—", combatMetrics.Session.RateValue);
        Assert.True(combatMetrics.Accuracy.ShowMetrics);
        Assert.NotEqual("—", combatMetrics.Rolling.RateValue);
        Assert.NotEqual("No qualifying combat data yet.", combatMetrics.Rolling.Detail);
        var rollingPresentation = RollingEarningsPresentation.BuildHero(
            context.RollingEarnings,
            RollingCombatPresets.TenMinutes);
        Assert.NotEqual("—", rollingPresentation.ExperienceRateValue);
        Assert.NotEqual("—", rollingPresentation.InfluenceRateValue);
        Assert.NotEqual("No qualifying earnings data yet.", rollingPresentation.Detail);
    }

    private static DateTimeOffset LocalAt(int year, int month, int day, int hour, int minute, int second = 0) =>
        new(new DateTime(year, month, day, hour, minute, second, DateTimeKind.Local));

    private static ParserEvent ParserEvent(long sequence, DateTimeOffset observedAt, DateTime sourceTimestamp) =>
        new()
        {
            ContextId = MonitoringContextId.CreateNew(),
            SourceId = LogSourceId.Create("acct", "acct", @"C:\fake\log.txt", new DateOnly(2026, 8, 9)),
            SourceSegmentId = ParserSourceSegmentId.CreateNew(),
            BindingGeneration = 1,
            Sequence = sequence,
            ObservedAt = observedAt,
            RawLine = "You gain experience.",
            SourceByteStart = 0,
            SourceByteEnd = 1,
            LineStatus = ParserLineStatus.Complete,
            EventKind = ParserEventKind.TimestampedLine,
            ClassificationStatus = ParserClassificationStatus.Recognized,
            ClassificationRuleId = "xp",
            SourceTimestamp = sourceTimestamp
        };
}
