using System.IO;
using CoHAnalytics.Models;
using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Services;

public sealed class GameplaySessionRolloverTests
{
    [Fact]
    public async Task Rollover_preserves_active_session_and_identity()
    {
        var monitoring = new FakeMonitoringSessionManager();
        var parser = new GameplaySessionTestInfrastructure.FakeGameplayParserManager();
        var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dir);

        try
        {
            var contextId = MonitoringContextId.CreateNew();
            var source = GameplaySessionTestInfrastructure.DefaultSource();
            var context = GameplaySessionTestInfrastructure.ReadyContext(
                contextId,
                source,
                MonitoringSourceTransitionKind.SourceAssigned);
            monitoring.SetInitial(ParserTestSnapshots.Snapshot(1, context));

            var manager = await GameplaySessionTestInfrastructure.CreateStartedManager(
                monitoring,
                parser,
                repository);

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:27:10 Welcome to City of Heroes, Example Hero!",
                    contextId,
                    source,
                    sequence: 1)
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(
                () => manager.Current.Sessions.Any(session => session.CharacterRecordId is not null));

            var sessionIdBefore = manager.Current.Sessions[0].SessionId;
            var rolledSource = GameplaySessionTestInfrastructure.DefaultSource();
            var rolledContext = ParserTestSnapshots.Context(
                contextId,
                MonitoringContextState.Ready,
                rolledSource,
                2,
                MonitoringSourceTransitionKind.AutomaticRollover,
                source);

            monitoring.Publish(ParserTestSnapshots.Snapshot(2, rolledContext));

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:27:11 structurally timestamped",
                    contextId,
                    rolledSource,
                    sequence: 2,
                    transition: MonitoringSourceTransitionKind.AutomaticRollover,
                    bindingGeneration: 2)
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(
                () => manager.Current.Sessions[0].CurrentSourceBindingGeneration == 2);

            var session = manager.Current.Sessions[0];
            Assert.Equal(sessionIdBefore, session.SessionId);
            Assert.Equal(CharacterIdentityConfidence.Confirmed, session.CharacterIdentityConfidence);
            Assert.Equal(MonitoringSourceTransitionKind.AutomaticRollover, session.CurrentSourceTransitionKind);
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
