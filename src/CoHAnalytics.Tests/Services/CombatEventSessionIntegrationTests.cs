using CoHAnalytics.Models;
using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Services;

public sealed class CombatEventSessionIntegrationTests
{
    [Fact]
    public async Task Gameplay_session_manager_retains_parsed_combat_events_without_aggregation()
    {
        var monitoring = new FakeMonitoringSessionManager();
        var parser = new GameplaySessionTestInfrastructure.FakeGameplayParserManager();
        var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dir);

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
                repository);

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 12:00:00 Welcome to City of Heroes, Example Hero!",
                    contextId,
                    source,
                    sequence: 1),
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 12:00:01 You hit Lusca with your Hot Feet for 13.88 points of Fire damage.",
                    contextId,
                    source,
                    sequence: 2),
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 12:00:02 You gain 500 experience and 100 influence.",
                    contextId,
                    source,
                    sequence: 3),
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 12:00:03 You activate Fire Cages.",
                    contextId,
                    source,
                    sequence: 4)
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
            {
                var sessions = manager.Current.Sessions;
                return sessions.Count == 1 && sessions[0].RetainedCombatEventCount == 2;
            });

            var session = manager.Current.Sessions[0];
            Assert.Equal(500, session.SessionExperienceGained);
            Assert.Equal(2, session.RetainedCombatEventCount);
            Assert.Equal(new CombatScaledAmount(1388), session.Combat.DamageDealt);
            Assert.Equal(1, session.Combat.PowerActivations);

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
}
