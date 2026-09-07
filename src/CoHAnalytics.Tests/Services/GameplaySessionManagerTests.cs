using System.IO;
using CoHAnalytics.Models;
using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Services;

public sealed class GameplaySessionManagerTests
{
    [Fact]
    public async Task Waiting_context_has_no_session_and_ready_context_creates_provisional_session()
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
                ParserTestSnapshots.Context(
                    contextId,
                    MonitoringContextState.WaitingForSource,
                    source: null,
                    bindingGeneration: 0,
                    transition: MonitoringSourceTransitionKind.None)));

            var manager = await GameplaySessionTestInfrastructure.CreateStartedManager(
                monitoring,
                parser,
                repository);

            Assert.Empty(manager.Current.Sessions);

            monitoring.Publish(ParserTestSnapshots.Snapshot(
                2,
                GameplaySessionTestInfrastructure.ReadyContext(contextId, source)));

            await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
                manager.Current.Sessions.Any(session =>
                    session.ContextId == contextId
                    && session.LifecycleState == GameplaySessionLifecycleState.Active
                    && session.CharacterIdentityConfidence == CharacterIdentityConfidence.Unknown
                    && session.CharacterIdentityResolutionState == CharacterIdentityResolutionState.Unresolved));

            var session = Assert.Single(manager.Current.Sessions);
            Assert.Null(session.CharacterRecordId);
            Assert.Null(session.CharacterDisplayName);
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
    public async Task First_complete_classified_event_is_retained_by_ready_time_provisional_session()
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

            var event_ = GameplaySessionTestInfrastructure.Classify(
                "2026-08-04 06:28:14 structurally timestamped",
                contextId,
                source);
            parser.PublishClassified([event_]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(
                () => manager.Current.Sessions.Any(session =>
                    session.ContextId == contextId
                    && session.RetainedEventCount == 1));

            var session = manager.Current.Sessions[0];
            Assert.Equal(CharacterIdentityConfidence.Unknown, session.CharacterIdentityConfidence);
            Assert.Equal(CharacterIdentityResolutionState.Unresolved, session.CharacterIdentityResolutionState);
            Assert.Equal(1, session.RetainedEventCount);
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
