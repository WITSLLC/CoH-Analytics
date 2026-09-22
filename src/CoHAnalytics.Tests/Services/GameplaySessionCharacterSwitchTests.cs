using System.Collections.Concurrent;
using System.Text;
using CoHAnalytics.Models;
using CoHAnalytics.Services;
using CoHAnalytics.Services.Diagnostics;
using CoHAnalytics.Tests.Services.Diagnostics;

namespace CoHAnalytics.Tests.Services;

public sealed class GameplaySessionCharacterSwitchTests
{
    private static HomecomingProcessInstance SameProcess() => new()
    {
        ProcessId = 4084,
        ProcessStartTime = new DateTimeOffset(2026, 8, 4, 11, 0, 0, TimeSpan.Zero),
        ExecutablePath = @"C:\Homecoming\cityofheroes.exe"
    };

    [Theory]
    [InlineData("Hero B")]
    [InlineData("Hero A")]
    public async Task Live_welcomes_always_replace_session_on_same_process_and_context(string secondName)
    {
        var monitoring = new FakeMonitoringSessionManager();
        var parser = new GameplaySessionTestInfrastructure.FakeGameplayParserManager();
        var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dataDir);
        var contextId = MonitoringContextId.CreateNew();
        var source = GameplaySessionTestInfrastructure.DefaultSource();
        var segment = ParserSourceSegmentId.CreateNew();
        monitoring.SetInitial(ParserTestSnapshots.Snapshot(1,
            GameplaySessionTestInfrastructure.ReadyContext(contextId, source, processInstance: SameProcess())));
        var log = new RecordingDiagnosticLog();
        using var manager = new GameplaySessionManager(monitoring, parser, repository, diagnosticLog: log);
        using var identity = new GameplaySessionIdentityReadService(manager, monitoring, repository);
        try
        {
            await manager.StartAsync();
            var sessionIds = new List<GameplaySessionId>();
            var names = new[] { "Hero A", secondName, "Hero A" };
            for (var index = 0; index < names.Length; index++)
            {
                // Deliberately identical timestamps/text for A -> A; the later byte range is
                // a new live line even when process, context, account and character all match.
                var welcome = GameplaySessionTestInfrastructure.Classify(
                    $"2026-08-04 12:00:00 Welcome to City of Heroes, {names[index]}!",
                    contextId, source, sequence: index + 1) with
                {
                    SourceSegmentId = segment,
                    SourceByteStart = index * 100,
                    SourceByteEnd = index * 100 + 70
                };
                parser.PublishClassified([welcome]);
                await GameplaySessionTestInfrastructure.WaitForWorkQueueToDrainAsync(manager);
                var active = Assert.Single(manager.Current.Sessions);
                sessionIds.Add(active.SessionId);
                Assert.Equal(GameplaySessionLifecycleState.Active, active.LifecycleState);
                Assert.Equal(CharacterIdentityConfidence.Confirmed, active.CharacterIdentityConfidence);
                Assert.Equal(names[index], active.CharacterDisplayName);
                var read = Assert.Single(identity.Current.Contexts);
                Assert.Equal(active.CharacterRecordId, read.CharacterRecordId);
                Assert.Equal(names[index], read.CharacterDisplayName);
                Assert.Equal(CharacterTrustState.TrustedFromWelcome,
                    repository.TryFindTrustedByDisplayName("acct-1", names[index])!.TrustState);
            }

            Assert.Equal(3, sessionIds.Distinct().Count());
            var processed = log.Events.OfType<GameplaySessionWelcomeProcessedDiagnosticEvent>().ToArray();
            Assert.Equal(3, processed.Length);
            Assert.All(processed, item =>
            {
                Assert.Equal(GameplayWelcomeProcessingResult.ReplacedSession, item.Result);
                Assert.Equal("WelcomeBoundary", item.Reason);
                Assert.NotEqual(item.ExistingSessionId, item.ResultingSessionId);
            });
            Assert.Equal(sessionIds[0].ToString(), processed[1].ExistingSessionId);
            Assert.Equal(sessionIds[1].ToString(), processed[2].ExistingSessionId);
            Assert.Equal(3, manager.GetDiagnostics().RecentOperations.Count(operation =>
                operation.Contains("Session finalized", StringComparison.Ordinal)
                && operation.Contains("Welcome boundary", StringComparison.Ordinal)));
        }
        finally
        {
            await manager.StopAsync();
            Directory.Delete(dataDir, recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Same_physical_welcome_replayed_or_recovered_preserves_current_session(bool recovered)
    {
        var monitoring = new FakeMonitoringSessionManager();
        var parser = new GameplaySessionTestInfrastructure.FakeGameplayParserManager();
        var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dataDir);
        var contextId = MonitoringContextId.CreateNew();
        var source = GameplaySessionTestInfrastructure.DefaultSource();
        monitoring.SetInitial(ParserTestSnapshots.Snapshot(1,
            GameplaySessionTestInfrastructure.ReadyContext(contextId, source, processInstance: SameProcess())));
        using var manager = new GameplaySessionManager(monitoring, parser, repository);
        try
        {
            await manager.StartAsync();
            var welcome = GameplaySessionTestInfrastructure.Classify(
                "2026-08-04 12:00:00 Welcome to City of Heroes, Hero A!", contextId, source);
            parser.PublishClassified([welcome]);
            await GameplaySessionTestInfrastructure.WaitForWorkQueueToDrainAsync(manager);
            var original = Assert.Single(manager.Current.Sessions);
            var eventCount = manager.GetDiagnostics().TotalCommittedEvents;
            parser.PublishClassified([recovered
                ? welcome with { IsRecoveredWelcome = true, SourceSegmentId = ParserSourceSegmentId.CreateNew() }
                : welcome]);
            await GameplaySessionTestInfrastructure.WaitForWorkQueueToDrainAsync(manager);
            Assert.Equal(original.SessionId, Assert.Single(manager.Current.Sessions).SessionId);
            Assert.Equal(eventCount, manager.GetDiagnostics().TotalCommittedEvents);

            // Recovery of a different Welcome for the same character cannot preserve the old session.
            parser.PublishClassified([welcome with
            {
                IsRecoveredWelcome = true,
                SourceByteStart = 100,
                SourceByteEnd = 170
            }]);
            await GameplaySessionTestInfrastructure.WaitForWorkQueueToDrainAsync(manager);
            Assert.NotEqual(original.SessionId, Assert.Single(manager.Current.Sessions).SessionId);
        }
        finally
        {
            await manager.StopAsync();
            Directory.Delete(dataDir, recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Analytics_restart_with_same_game_process_recovers_one_session_without_false_live_boundary(
        bool parserStartsFirst)
    {
        using var directory = new ParserTestDirectory();
        var path = directory.CreateFile(content: Encoding.UTF8.GetBytes(
            "2026-08-04 12:00:00 Welcome to City of Heroes, Hero A!\r\n"));
        var source = ParserTestSnapshots.Source(path);
        var contextId = MonitoringContextId.CreateNew();
        var monitoring = new FakeMonitoringSessionManager();
        monitoring.SetInitial(ParserTestSnapshots.Snapshot(1,
            GameplaySessionTestInfrastructure.ReadyContext(contextId, source, processInstance: SameProcess())));
        var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dataDir);
        try
        {
            for (var appRun = 0; appRun < 2; appRun++)
            {
                var log = new RecordingDiagnosticLog();
                await using var parser = new ParserManager(monitoring, ParserTestSnapshots.FastOptions());
                using var manager = new GameplaySessionManager(monitoring, parser, repository, diagnosticLog: log);
                var classified = new ConcurrentQueue<ParserEvent>();
                parser.ClassifiedEventsAvailable += (_, args) =>
                {
                    foreach (var item in args.Events) classified.Enqueue(item);
                };
                try
                {
                    if (parserStartsFirst) await parser.StartAsync();
                    await manager.StartAsync();
                    if (!parserStartsFirst) await parser.StartAsync();
                    await GameplaySessionTestInfrastructure.WaitUntilAsync(() => classified.Count == 1);
                    await GameplaySessionTestInfrastructure.WaitForWorkQueueToDrainAsync(manager);
                    Assert.True(Assert.Single(classified).IsRecoveredWelcome);
                    var active = Assert.Single(manager.Current.Sessions);
                    Assert.Equal("Hero A", active.CharacterDisplayName);
                    Assert.Equal(CharacterIdentityConfidence.Confirmed, active.CharacterIdentityConfidence);
                    var processed = log.Events.OfType<GameplaySessionWelcomeProcessedDiagnosticEvent>().ToArray();
                    Assert.NotEmpty(processed);
                    Assert.All(processed, item =>
                    {
                        Assert.Equal(GameplayWelcomeProcessingResult.RecoveredSession, item.Result);
                        Assert.Equal(active.SessionId.ToString(), item.ResultingSessionId);
                    });
                    Assert.Equal(1, manager.GetDiagnostics().TotalCommittedEvents);
                    Assert.Equal(new FileInfo(path).Length,
                        Assert.Single(parser.Current.Workers).CurrentSegment!.StartingOffset);
                }
                finally
                {
                    await parser.StopAsync();
                    await manager.StopAsync();
                }
            }
            Assert.Single(repository.Current.Records);
        }
        finally
        {
            Directory.Delete(dataDir, recursive: true);
        }
    }
}
