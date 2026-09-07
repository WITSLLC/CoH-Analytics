using System.IO;
using CoHAnalytics.Models;
using CoHAnalytics.Services;
using CoHAnalytics.Tests.Services;

namespace CoHAnalytics.Tests.Integration;

public sealed class GameplaySessionOverloadIntegrationTests
{
    [Fact]
    public async Task Queue_saturation_does_not_block_parser_dispatcher()
    {
        using var directory = new ParserTestDirectory();
        var path = directory.CreateFile();
        var source = ParserTestSnapshots.Source(path);
        var contextId = MonitoringContextId.CreateNew();
        var monitoring = new FakeMonitoringSessionManager();
        monitoring.SetInitial(ParserTestSnapshots.Snapshot(
            1,
            GameplaySessionTestInfrastructure.ReadyContext(contextId, source)));

        var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dataDir);
        var options = new GameplaySessionOptions { WorkQueueCapacity = 1 };
        await using var parser = new ParserManager(monitoring, ParserTestSnapshots.FastOptions());
        var manager = new GameplaySessionManager(monitoring, parser, repository, options);

        try
        {
            await parser.StartAsync();
            await manager.StartAsync();

            var processingStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseProcessing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void BlockProcessor()
            {
                processingStarted.TrySetResult();
                releaseProcessing.Task.Wait();
            }

            manager.CommittedEventsAvailable += (_, _) => BlockProcessor();
            manager.StateChanged += (_, _) => BlockProcessor();

            directory.Append(path, "2026-08-04 06:27:10 Welcome to City of Heroes, Parser Hero!\r\n");
            await processingStarted.Task;

            monitoring.Publish(ParserTestSnapshots.Snapshot(
                2,
                GameplaySessionTestInfrastructure.ReadyContext(contextId, source)));
            directory.Append(path, "2026-08-04 06:27:11 overflow line\r\n");

            await GameplaySessionTestInfrastructure.WaitUntilAsync(
                () => manager.GetDiagnostics().WorkQueueOverflowed);
            var gameplayDiagnostics = manager.GetDiagnostics();
            Assert.True(gameplayDiagnostics.WorkQueue.Overflowed);
            Assert.True(gameplayDiagnostics.WorkQueue.RejectedCount >= 1);
            directory.Append(path, "2026-08-04 06:27:12 parser still runs\r\n");

            await ParserTestSnapshots.WaitUntilAsync(
                () => parser.ClassificationCurrent.TotalClassifiedLines >= 3);

            releaseProcessing.TrySetResult();
            Assert.True(parser.GetDiagnostics().IsRunning);
        }
        finally
        {
            await manager.StopAsync();
            try
            {
                Directory.Delete(dataDir, recursive: true);
            }
            catch
            {
            }
        }
    }
}
