using CoHAnalytics.Models;
using CoHAnalytics.Services;
using CoHAnalytics.Tests.Orchestration;

namespace CoHAnalytics.Tests.Services;

public sealed class TrackedCombatSessionTests
{
    [Fact]
    public async Task Tracked_combat_lifecycle_on_gameplay_session_manager_matches_domain_rules()
    {
        var monitoring = new FakeMonitoringSessionManager();
        var parser = new GameplaySessionTestInfrastructure.FakeGameplayParserManager();
        var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dir);
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 8, 4, 12, 0, 0, TimeSpan.Zero));

        try
        {
            var contextId = MonitoringContextId.CreateNew();
            var source = GameplaySessionTestInfrastructure.DefaultSource();

            monitoring.SetInitial(ParserTestSnapshots.Snapshot(
                1,
                GameplaySessionTestInfrastructure.ReadyContext(contextId, source)));

            var manager = await GameplaySessionTestInfrastructure.CreateStartedManager(
                monitoring,
                parser,
                repository,
                new GameplaySessionOptions
                {
                    TimeProvider = time,
                    CombatSnapshotPublishInterval = TimeSpan.Zero
                });

            await PublishWelcomeAndDamageAsync(parser, manager, contextId, source, time, sequence: 1, amount: "100.00");

            manager.StartTrackedCombat(contextId);
            time.Advance(TimeSpan.FromSeconds(15));
            await PublishDamageAsync(parser, manager, contextId, source, time, sequence: 2, amount: "50.00", advance: TimeSpan.Zero);

            time.Advance(TimeSpan.FromSeconds(15));
            manager.PauseTrackedCombat(contextId);
            time.Advance(TimeSpan.FromSeconds(15));
            await PublishDamageAsync(parser, manager, contextId, source, time, sequence: 3, amount: "500.00", advance: TimeSpan.Zero);

            time.Advance(TimeSpan.FromSeconds(60));
            manager.ResumeTrackedCombat(contextId);
            await PublishDamageAsync(parser, manager, contextId, source, time, sequence: 4, amount: "25.00", advance: TimeSpan.FromSeconds(15));

            time.Advance(TimeSpan.FromSeconds(15));
            manager.StopTrackedCombat(contextId);
            await GameplaySessionTestInfrastructure.WaitForWorkQueueToDrainAsync(manager);

            var session = manager.Current.Sessions.Single();
            Assert.Equal(new CombatScaledAmount(67500), session.Combat.DamageDealt);
            Assert.Equal(new CombatScaledAmount(7500), session.Combat.Tracked.DamageDealt);
            Assert.Equal(TimeSpan.FromSeconds(60), session.Combat.Tracked.ActiveElapsed);
            Assert.Equal(125, session.Combat.Tracked.DamagePerSecondHundredths);

            await manager.StopAsync();
        }
        finally
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch
            {
            }
        }
    }

    [Fact]
    public async Task Multi_context_tracked_combat_remains_isolated()
    {
        var monitoring = new FakeMonitoringSessionManager();
        var parser = new GameplaySessionTestInfrastructure.FakeGameplayParserManager();
        var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dir);
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 8, 4, 12, 0, 0, TimeSpan.Zero));

        try
        {
            var contextA = MonitoringContextId.CreateNew();
            var contextB = MonitoringContextId.CreateNew();
            var sourceA = GameplaySessionTestInfrastructure.DefaultSource("acct-a");
            var sourceB = GameplaySessionTestInfrastructure.DefaultSource("acct-b");

            monitoring.SetInitial(ParserTestSnapshots.Snapshot(
                1,
                GameplaySessionTestInfrastructure.ReadyContext(contextA, sourceA),
                GameplaySessionTestInfrastructure.ReadyContext(contextB, sourceB)));

            var manager = await GameplaySessionTestInfrastructure.CreateStartedManager(
                monitoring,
                parser,
                repository,
                new GameplaySessionOptions { TimeProvider = time, CombatSnapshotPublishInterval = TimeSpan.Zero });

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 12:00:00 Welcome to City of Heroes, Example Hero!",
                    contextA,
                    sourceA,
                    sequence: 1),
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 12:00:00 Welcome to City of Heroes, Example Hero!",
                    contextB,
                    sourceB,
                    sequence: 2)
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(() => manager.Current.Sessions.Count == 2);

            manager.StartTrackedCombat(contextA);
            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 12:00:01 You hit Lusca with your Hot Feet for 13.88 points of Fire damage.",
                    contextA,
                    sourceA,
                    sequence: 3),
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 12:00:01 You hit Skull with your Fire Cages for 8.21 points of Fire damage.",
                    contextB,
                    sourceB,
                    sequence: 4)
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
                manager.Current.Sessions.Single(session => session.ContextId == contextA).Combat.Tracked.DamageDealt.Hundredths == 1388);

            var sessionA = manager.Current.Sessions.Single(session => session.ContextId == contextA);
            var sessionB = manager.Current.Sessions.Single(session => session.ContextId == contextB);

            Assert.Equal(new CombatScaledAmount(1388), sessionA.Combat.Tracked.DamageDealt);
            Assert.Equal(CombatScaledAmount.Zero, sessionB.Combat.Tracked.DamageDealt);
            Assert.True(sessionA.Combat.Tracked.IsTracking);
            Assert.False(sessionB.Combat.Tracked.IsTracking);

            await manager.StopAsync();
        }
        finally
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch
            {
            }
        }
    }

    [Fact]
    public async Task Different_character_handoff_resets_tracked_combat_state()
    {
        var monitoring = new FakeMonitoringSessionManager();
        var parser = new GameplaySessionTestInfrastructure.FakeGameplayParserManager();
        var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dir);
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 8, 4, 12, 0, 0, TimeSpan.Zero));

        try
        {
            var contextId = MonitoringContextId.CreateNew();
            var source = GameplaySessionTestInfrastructure.DefaultSource();

            monitoring.SetInitial(ParserTestSnapshots.Snapshot(
                1,
                GameplaySessionTestInfrastructure.ReadyContext(contextId, source)));

            var manager = await GameplaySessionTestInfrastructure.CreateStartedManager(
                monitoring,
                parser,
                repository,
                new GameplaySessionOptions { TimeProvider = time, CombatSnapshotPublishInterval = TimeSpan.Zero });

            await PublishWelcomeAndDamageAsync(parser, manager, contextId, source, time, sequence: 1, amount: "10.00");
            manager.StartTrackedCombat(contextId);
            await PublishDamageAsync(parser, manager, contextId, source, time, sequence: 3, amount: "5.00", advance: TimeSpan.Zero);
            await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
            {
                var active = manager.Current.Sessions.SingleOrDefault();
                return active is not null
                    && active.Combat.Tracked.IsTracking
                    && active.Combat.Tracked.DamageDealt == new CombatScaledAmount(500);
            });

            var firstSession = Assert.Single(manager.Current.Sessions);
            time.Advance(TimeSpan.FromMinutes(1));

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 12:01:00 Welcome to City of Heroes, Another Hero!",
                    contextId,
                    source,
                    sequence: 4,
                    observedAt: time.GetUtcNow())
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
            {
                var active = manager.Current.Sessions.SingleOrDefault();
                return active is not null
                    && active.SessionId != firstSession.SessionId
                    && active.CharacterDisplayName == "Another Hero"
                    && !active.Combat.Tracked.IsTracking
                    && active.Combat.Tracked.DamageDealt == CombatScaledAmount.Zero;
            });

            var session = manager.Current.Sessions.Single();
            Assert.NotEqual(firstSession.SessionId, session.SessionId);
            Assert.Equal(time.GetUtcNow(), session.StartedAt);
            Assert.False(session.Combat.Tracked.IsTracking);
            Assert.Equal(CombatScaledAmount.Zero, session.Combat.Tracked.DamageDealt);

            await manager.StopAsync();
        }
        finally
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch
            {
            }
        }
    }

    private static async Task PublishWelcomeAndDamageAsync(
        GameplaySessionTestInfrastructure.FakeGameplayParserManager parser,
        GameplaySessionManager manager,
        MonitoringContextId contextId,
        LogSourceId source,
        ManualTimeProvider time,
        long sequence,
        string amount)
    {
        parser.PublishClassified([
            GameplaySessionTestInfrastructure.Classify(
                "2026-08-04 12:00:00 Welcome to City of Heroes, Example Hero!",
                contextId,
                source,
                sequence: sequence,
                observedAt: time.GetUtcNow())
        ]);

        await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
            manager.Current.Sessions.Any(session =>
                session.ContextId == contextId
                && session.CharacterRecordId is not null
                && session.CharacterDisplayName == "Example Hero"));
        await PublishDamageAsync(parser, manager, contextId, source, time, sequence + 1, amount, TimeSpan.Zero);
    }

    private static async Task PublishDamageAsync(
        GameplaySessionTestInfrastructure.FakeGameplayParserManager parser,
        GameplaySessionManager manager,
        MonitoringContextId contextId,
        LogSourceId source,
        ManualTimeProvider time,
        long sequence,
        string amount,
        TimeSpan advance)
    {
        if (advance > TimeSpan.Zero)
        {
            time.Advance(advance);
        }

        var priorCombatEventCount = manager.Current.Sessions
            .Single(session => session.ContextId == contextId)
            .RetainedCombatEventCount;

        parser.PublishClassified([
            GameplaySessionTestInfrastructure.Classify(
                $"2026-08-04 12:00:00 You hit Lusca with your Hot Feet for {amount} points of Fire damage.",
                contextId,
                source,
                sequence: sequence,
                observedAt: time.GetUtcNow())
        ]);

        await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
            manager.Current.Sessions.Any(session =>
                session.ContextId == contextId
                && session.RetainedCombatEventCount == priorCombatEventCount + 1));
    }
}
