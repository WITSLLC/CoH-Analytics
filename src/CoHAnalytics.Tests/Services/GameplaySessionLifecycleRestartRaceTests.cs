using System.Collections.Concurrent;
using System.IO;
using CoHAnalytics.Models;
using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Services;

public sealed class GameplaySessionLifecycleRestartRaceTests
{
    [Fact]
    public async Task Overload_finalization_is_joined_before_restart_owns_new_epoch()
    {
        var monitoring = new FakeMonitoringSessionManager();
        var parser = new GameplaySessionTestInfrastructure.FakeGameplayParserManager();
        var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dir);
        var finalizationReachedResourceRelease = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFinalization = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var postRestartEventCommitted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var committedSequences = new ConcurrentQueue<long>();
        var blockFirstFinalization = 1;
        var options = new GameplaySessionOptions
        {
            WorkQueueCapacity = 1,
            TestHooks = new GameplaySessionTestHooks
            {
                BeforeEpochResourceRelease = () =>
                {
                    if (Interlocked.Exchange(ref blockFirstFinalization, 0) == 0)
                    {
                        return;
                    }

                    finalizationReachedResourceRelease.TrySetResult();
                    releaseFinalization.Task.GetAwaiter().GetResult();
                }
            }
        };
        var manager = new GameplaySessionManager(monitoring, parser, repository, options);

        try
        {
            var contextId = MonitoringContextId.CreateNew();
            var source = GameplaySessionTestInfrastructure.DefaultSource();
            monitoring.SetInitial(ParserTestSnapshots.Snapshot(
                1,
                GameplaySessionTestInfrastructure.ReadyContext(contextId, source)));
            manager.CommittedEventsAvailable += (_, args) =>
            {
                foreach (var event_ in args.Events)
                {
                    committedSequences.Enqueue(event_.ParserEvent.Sequence);
                    if (event_.ParserEvent.Sequence == 3)
                    {
                        postRestartEventCommitted.TrySetResult();
                    }
                }
            };

            await manager.StartAsync();
            await GameplaySessionTestInfrastructure.CauseOverloadAsync(
                manager,
                monitoring,
                parser,
                contextId,
                source);
            await finalizationReachedResourceRelease.Task;

            var firstEpoch = manager.GetDiagnostics();
            Assert.Equal(1, firstEpoch.LifecycleEpoch);
            Assert.True(firstEpoch.WorkQueueOverflowed);
            Assert.False(firstEpoch.IsRunning);
            var committedAtFinalizationBoundary = committedSequences.Count;

            var stopTask = manager.StopAsync();
            var restartTask = manager.StartAsync();

            Assert.False(stopTask.IsCompleted);
            Assert.False(restartTask.IsCompleted);

            releaseFinalization.TrySetResult();
            await Task.WhenAll(stopTask, restartTask);
            await GameplaySessionTestInfrastructure.WaitForWorkQueueToDrainAsync(manager);

            var secondEpoch = manager.GetDiagnostics();
            Assert.True(secondEpoch.IsRunning);
            Assert.Equal(2, secondEpoch.LifecycleEpoch);
            Assert.Equal(2, secondEpoch.WorkQueue.LifecycleEpoch);
            Assert.False(secondEpoch.WorkQueueOverflowed);
            Assert.False(secondEpoch.WorkQueue.Overflowed);
            Assert.True(secondEpoch.IsQuiescent);
            Assert.Equal(1, monitoring.StateChangedSubscriptionCount);
            Assert.Equal(1, parser.ClassifiedEventsSubscriptionCount);

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:27:13 Welcome to City of Heroes, Stall Hero!",
                    contextId,
                    source,
                    sequence: 3)
            ]);
            await postRestartEventCommitted.Task;

            Assert.Equal(committedAtFinalizationBoundary + 1, committedSequences.Count);
            Assert.Equal(3, committedSequences.Last());
            Assert.Equal(2, manager.GetDiagnostics().LifecycleEpoch);
        }
        finally
        {
            releaseFinalization.TrySetResult();
            await manager.StopAsync();
            manager.Dispose();
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
