using System.IO;
using CoHAnalytics.Models;
using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Services;

public sealed class GameplaySessionRetentionTests
{
    [Fact]
    public async Task Retention_overflow_stops_ordinary_retention_and_sets_identity_required()
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

            var options = new GameplaySessionOptions
            {
                MaxRetainedEventCount = 2,
                MaxRetainedPayloadBytes = 4096
            };

            var manager = await GameplaySessionTestInfrastructure.CreateStartedManager(
                monitoring,
                parser,
                repository,
                options);

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify("2026-08-04 06:27:01 line one", contextId, source, 1),
                GameplaySessionTestInfrastructure.Classify("2026-08-04 06:27:02 line two", contextId, source, 2),
                GameplaySessionTestInfrastructure.Classify("2026-08-04 06:27:03 line three", contextId, source, 3)
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(
                () => manager.Current.Sessions.Any(session => session.RetentionOverflowed));

            var session = manager.Current.Sessions[0];
            Assert.Equal(CharacterIdentityResolutionState.IdentityRequired, session.CharacterIdentityResolutionState);
            Assert.Equal(2, session.RetainedEventCount);
            Assert.True(session.DiscardedAfterOverflowCount > 0);
            Assert.True(session.NeedsAttention);
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
    public async Task Welcome_still_detected_after_overflow()
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

            var options = new GameplaySessionOptions { MaxRetainedEventCount = 1 };
            var manager = await GameplaySessionTestInfrastructure.CreateStartedManager(
                monitoring,
                parser,
                repository,
                options);

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify("2026-08-04 06:27:01 line one", contextId, source, 1),
                GameplaySessionTestInfrastructure.Classify("2026-08-04 06:27:02 line two", contextId, source, 2),
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:27:10 Welcome to City of Heroes, Example Hero!",
                    contextId,
                    source,
                    3)
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(
                () => manager.Current.Sessions.Any(session =>
                    session.CharacterIdentityConfidence == CharacterIdentityConfidence.Confirmed));

            Assert.NotNull(repository.TryFindTrustedByDisplayName("acct-1", "Example Hero"));
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
    public async Task Manual_confirmation_after_overflow_preserves_incomplete_flag()
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

            var options = new GameplaySessionOptions { MaxRetainedEventCount = 1 };
            var manager = await GameplaySessionTestInfrastructure.CreateStartedManager(
                monitoring,
                parser,
                repository,
                options);

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify("2026-08-04 06:27:01 line one", contextId, source, 1),
                GameplaySessionTestInfrastructure.Classify("2026-08-04 06:27:02 line two", contextId, source, 2)
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(
                () => manager.Current.Sessions.Any(session => session.RetentionOverflowed));

            var establish = repository.EstablishTrustedFromManualConfirmation("acct-1", "Manual Hero");
            var confirm = manager.ConfirmCharacter(contextId, establish.RecordId!);
            Assert.True(confirm.IsSuccess);

            var session = manager.Current.Sessions[0];
            Assert.True(session.RetentionOverflowed);
            Assert.Equal(CharacterIdentityResolutionState.Resolved, session.CharacterIdentityResolutionState);
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
    public async Task Overflow_followed_by_welcome_resumes_recording_in_new_session()
    {
        var monitoring = new FakeMonitoringSessionManager();
        var parser = new GameplaySessionTestInfrastructure.FakeGameplayParserManager();
        var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dir);
        var committed = new List<GameplaySessionEvent>();

        try
        {
            var contextId = MonitoringContextId.CreateNew();
            var source = GameplaySessionTestInfrastructure.DefaultSource();
            monitoring.SetInitial(ParserTestSnapshots.Snapshot(
                1,
                GameplaySessionTestInfrastructure.ReadyContext(contextId, source)));

            var options = new GameplaySessionOptions { MaxRetainedEventCount = 1 };
            var manager = await GameplaySessionTestInfrastructure.CreateStartedManager(
                monitoring,
                parser,
                repository,
                options);
            manager.CommittedEventsAvailable += (_, args) => committed.AddRange(args.Events);

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify("2026-08-04 06:27:01 line one", contextId, source, 1),
                GameplaySessionTestInfrastructure.Classify("2026-08-04 06:27:02 line two", contextId, source, 2),
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:27:10 Welcome to City of Heroes, Example Hero!",
                    contextId,
                    source,
                    3),
                GameplaySessionTestInfrastructure.Classify("2026-08-04 06:27:11 line four", contextId, source, 4)
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(() => committed.Count >= 2);

            var session = manager.Current.Sessions[0];
            Assert.Equal(CharacterIdentityConfidence.Confirmed, session.CharacterIdentityConfidence);
            Assert.False(session.RetentionOverflowed);
            Assert.Contains(committed, event_ => event_.ParserEvent.Sequence == 4L);
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
