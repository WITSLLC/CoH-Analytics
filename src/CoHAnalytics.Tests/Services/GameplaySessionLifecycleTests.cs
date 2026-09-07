using System.IO;
using CoHAnalytics.Models;
using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Services;

public sealed class GameplaySessionLifecycleTests
{
    [Fact]
    public async Task First_start_capture_straggler_is_rejected_while_ready_baseline_remains_provisional()
    {
        var monitoring = new FakeMonitoringSessionManager();
        var parser = new GameplaySessionTestInfrastructure.FakeGameplayParserManager();
        var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dir);
        using var captureCallbackEntered = new ManualResetEventSlim();
        using var releaseCaptureCallback = new ManualResetEventSlim();
        var blockCapture = 1;
        var options = new GameplaySessionOptions
        {
            TestHooks = new GameplaySessionTestHooks
            {
                BeforeFirstStartCaptureLock = () =>
                {
                    if (Interlocked.Exchange(ref blockCapture, 0) == 0)
                    {
                        return;
                    }

                    captureCallbackEntered.Set();
                    releaseCaptureCallback.Wait();
                }
            }
        };

        try
        {
            var contextId = MonitoringContextId.CreateNew();
            var source = GameplaySessionTestInfrastructure.DefaultSource();
            monitoring.SetInitial(ParserTestSnapshots.Snapshot(
                1,
                GameplaySessionTestInfrastructure.ReadyContext(contextId, source)));
            var manager = new GameplaySessionManager(monitoring, parser, repository, options);

            var racingPublish = Task.Run(() => parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:27:10 Welcome to City of Heroes, Stale Hero!",
                    contextId,
                    source)
            ]));
            Assert.True(captureCallbackEntered.Wait(TimeSpan.FromSeconds(1)));

            await manager.StartAsync().WaitAsync(TimeSpan.FromSeconds(1));
            releaseCaptureCallback.Set();
            await racingPublish.WaitAsync(TimeSpan.FromSeconds(1));
            var initialSession = Assert.Single(manager.Current.Sessions);
            Assert.Null(initialSession.CharacterRecordId);
            Assert.NotEqual("Stale Hero", initialSession.CharacterDisplayName);

            await manager.StopAsync();
            await manager.StartAsync();
            var restartedSession = Assert.Single(manager.Current.Sessions);
            Assert.Null(restartedSession.CharacterRecordId);
            Assert.NotEqual("Stale Hero", restartedSession.CharacterDisplayName);
        }
        finally
        {
            releaseCaptureCallback.Set();
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
    public async Task Capacity_one_startup_transfers_multiple_captured_items_in_order()
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
            var manager = new GameplaySessionManager(
                monitoring,
                parser,
                repository,
                new GameplaySessionOptions
                {
                    WorkQueueCapacity = 1,
                    MaxPreStartBufferCapacity = 4
                });
            manager.CommittedEventsAvailable += (_, args) => committed.AddRange(args.Events);

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:27:10 Welcome to City of Heroes, Prefix Hero!",
                    contextId,
                    source,
                    1)
            ]);
            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:27:11 captured second item",
                    contextId,
                    source,
                    2)
            ]);

            await manager.StartAsync();

            Assert.Equal([1L, 2L], committed.Select(event_ => event_.ParserEvent.Sequence));
            Assert.Equal([1L, 2L], committed.Select(event_ => event_.SessionSequence));
            Assert.False(manager.GetDiagnostics().WorkQueueOverflowed);
            Assert.False(manager.GetDiagnostics().PreStartBufferOverflowed);
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
    public async Task Concurrent_duplicate_stops_share_shutdown_and_drain_one_prefix()
    {
        var monitoring = new FakeMonitoringSessionManager();
        var parser = new GameplaySessionTestInfrastructure.FakeGameplayParserManager();
        var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dir);
        using var processorEntered = new ManualResetEventSlim();
        using var releaseProcessor = new ManualResetEventSlim();
        var blockNextItem = 0;
        var finalizedCallbacks = 0;
        var callbackCount = 0;
        var committedSequences = new List<long>();
        var options = new GameplaySessionOptions
        {
            WorkQueueCapacity = 4,
            TestHooks = new GameplaySessionTestHooks
            {
                BeforeProcessWorkItem = () =>
                {
                    if (Interlocked.Exchange(ref blockNextItem, 0) == 0)
                    {
                        return;
                    }

                    processorEntered.Set();
                    releaseProcessor.Wait();
                }
            }
        };

        try
        {
            var contextId = MonitoringContextId.CreateNew();
            var source = GameplaySessionTestInfrastructure.DefaultSource();
            repository.EstablishTrustedFromWelcome("acct-1", "Stop Hero");
            monitoring.SetInitial(ParserTestSnapshots.Snapshot(
                1,
                GameplaySessionTestInfrastructure.ReadyContext(contextId, source)));
            var manager = await GameplaySessionTestInfrastructure.CreateStartedManager(
                monitoring,
                parser,
                repository,
                options);
            manager.CommittedEventsAvailable += (_, args) =>
            {
                Interlocked.Increment(ref callbackCount);
                committedSequences.AddRange(args.Events.Select(event_ => event_.ParserEvent.Sequence));
            };
            manager.StateChanged += (_, args) =>
            {
                Interlocked.Increment(ref callbackCount);
                if (args.Snapshot.Sessions.Count == 0)
                {
                    Interlocked.Increment(ref finalizedCallbacks);
                }
            };

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:27:10 Welcome to City of Heroes, Stop Hero!",
                    contextId,
                    source,
                    1)
            ]);
            await GameplaySessionTestInfrastructure.WaitUntilAsync(
                () => manager.Current.Sessions.Count == 1);

            Volatile.Write(ref blockNextItem, 1);
            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:27:11 accepted before stop",
                    contextId,
                    source,
                    2)
            ]);
            Assert.True(processorEntered.Wait(TimeSpan.FromSeconds(1)));
            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:27:12 queued accepted prefix",
                    contextId,
                    source,
                    3)
            ]);

            var firstStop = manager.StopAsync();
            var secondStop = manager.StopAsync();
            Assert.False(firstStop.IsCompleted);
            Assert.False(secondStop.IsCompleted);

            using var canceledWait = new CancellationTokenSource();
            canceledWait.Cancel();
            await manager.StopAsync(canceledWait.Token);
            Assert.True(manager.GetDiagnostics().IsRunning);

            releaseProcessor.Set();
            await Task.WhenAll(firstStop, secondStop).WaitAsync(TimeSpan.FromSeconds(1));

            Assert.False(manager.GetDiagnostics().IsRunning);
            Assert.Equal([1L, 2L, 3L], committedSequences);
            Assert.Equal(1, Volatile.Read(ref finalizedCallbacks));
            var callbacksAtStop = Volatile.Read(ref callbackCount);
            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:27:13 rejected after stop",
                    contextId,
                    source,
                    4)
            ]);
            Assert.Equal(callbacksAtStop, Volatile.Read(ref callbackCount));
        }
        finally
        {
            releaseProcessor.Set();
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
    public async Task Ready_resume_replaces_historical_suspended_session_without_parser_wakeup()
    {
        var monitoring = new FakeMonitoringSessionManager();
        var parser = new GameplaySessionTestInfrastructure.FakeGameplayParserManager();
        var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dir);

        try
        {
            var contextId = MonitoringContextId.CreateNew();
            var source = GameplaySessionTestInfrastructure.DefaultSource();
            var ready = GameplaySessionTestInfrastructure.ReadyContext(contextId, source);
            monitoring.SetInitial(ParserTestSnapshots.Snapshot(1, ready));

            var manager = await GameplaySessionTestInfrastructure.CreateStartedManager(
                monitoring,
                parser,
                repository);

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:27:10 Welcome to City of Heroes, Example Hero!",
                    contextId,
                    source)
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(
                () => manager.Current.Sessions.Count == 1);

            var sessionId = manager.Current.Sessions[0].SessionId;
            var suspended = ready with
            {
                State = MonitoringContextState.RuntimeSuspended,
                PreviousStateBeforeSuspension = MonitoringContextState.Ready,
                SuspendedAt = DateTimeOffset.UnixEpoch
            };
            monitoring.Publish(ParserTestSnapshots.Snapshot(2, suspended));

            await GameplaySessionTestInfrastructure.WaitUntilAsync(
                () => manager.Current.Sessions[0].LifecycleState == GameplaySessionLifecycleState.Suspended);

            var resumed = suspended with
            {
                State = MonitoringContextState.Ready,
                PreviousStateBeforeSuspension = null,
                SuspendedAt = null
            };
            monitoring.Publish(ParserTestSnapshots.Snapshot(3, resumed));

            await GameplaySessionTestInfrastructure.WaitUntilAsync(
                () => manager.Current.Sessions.Count == 1
                    && manager.Current.Sessions[0].LifecycleState == GameplaySessionLifecycleState.Active
                    && manager.Current.Sessions[0].SessionId != sessionId);

            var activeSession = manager.Current.Sessions.Single(item =>
                item.LifecycleState == GameplaySessionLifecycleState.Active);
            Assert.NotEqual(sessionId, activeSession.SessionId);
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
    public async Task Context_stopped_finalizes_session()
    {
        var monitoring = new FakeMonitoringSessionManager();
        var parser = new GameplaySessionTestInfrastructure.FakeGameplayParserManager();
        var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dir);

        try
        {
            var contextId = MonitoringContextId.CreateNew();
            var source = GameplaySessionTestInfrastructure.DefaultSource();
            var ready = GameplaySessionTestInfrastructure.ReadyContext(contextId, source);
            monitoring.SetInitial(ParserTestSnapshots.Snapshot(1, ready));

            var manager = await GameplaySessionTestInfrastructure.CreateStartedManager(
                monitoring,
                parser,
                repository);

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify("2026-08-04 06:27:10 line", contextId, source)
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(
                () => manager.Current.Sessions.Count == 1);

            monitoring.Publish(ParserTestSnapshots.Snapshot(
                2,
                ready with { State = MonitoringContextState.Stopped }));

            await GameplaySessionTestInfrastructure.WaitUntilAsync(
                () => manager.Current.Sessions.Count == 0);
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
    public async Task No_session_created_after_stop_completes()
    {
        var monitoring = new FakeMonitoringSessionManager();
        var parser = new GameplaySessionTestInfrastructure.FakeGameplayParserManager();
        var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dir);
        var callbacksAfterStop = 0;

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
            manager.CommittedEventsAvailable += (_, _) => callbacksAfterStop++;

            await manager.StopAsync();

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:27:10 Welcome to City of Heroes, Example Hero!",
                    contextId,
                    source)
            ]);

            await Task.Delay(50);
            Assert.Empty(manager.Current.Sessions);
            Assert.Equal(0, callbacksAfterStop);
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
    public async Task StopAsync_canceled_wait_leaves_authoritative_shutdown_running()
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

            var processingStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseProcessing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void BlockProcessor()
            {
                processingStarted.TrySetResult();
                releaseProcessing.Task.Wait();
            }

            manager.CommittedEventsAvailable += (_, _) => BlockProcessor();
            manager.StateChanged += (_, _) => BlockProcessor();

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:27:10 Welcome to City of Heroes, Example Hero!",
                    contextId,
                    source)
            ]);

            await processingStarted.Task;

            using var cts = new CancellationTokenSource();
            cts.Cancel();
            await manager.StopAsync(cts.Token);
            Assert.True(manager.GetDiagnostics().IsRunning);

            releaseProcessing.TrySetResult();
            await manager.StopAsync();
            Assert.False(manager.GetDiagnostics().IsRunning);
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
    public async Task Restart_installs_subscriptions_exactly_once()
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

            var manager = new GameplaySessionManager(monitoring, parser, repository);
            await manager.StartAsync();
            await manager.StopAsync();
            await manager.StartAsync();

            Assert.Equal(1, monitoring.StateChangedSubscriptionCount);
            Assert.Equal(1, parser.ClassifiedEventsSubscriptionCount);
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
    public async Task Restart_increments_lifecycle_epoch_and_resets_queue_diagnostics()
    {
        var monitoring = new FakeMonitoringSessionManager();
        var parser = new GameplaySessionTestInfrastructure.FakeGameplayParserManager();
        var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dir);
        var options = new GameplaySessionOptions { WorkQueueCapacity = 1 };

        try
        {
            var contextId = MonitoringContextId.CreateNew();
            var source = GameplaySessionTestInfrastructure.DefaultSource();
            monitoring.SetInitial(ParserTestSnapshots.Snapshot(
                1,
                GameplaySessionTestInfrastructure.ReadyContext(contextId, source)));

            var manager = new GameplaySessionManager(monitoring, parser, repository, options);
            await manager.StartAsync();

            await GameplaySessionTestInfrastructure.CauseOverloadAsync(
                manager,
                monitoring,
                parser,
                contextId,
                source);

            var firstEpoch = manager.GetDiagnostics();
            Assert.Equal(1, firstEpoch.LifecycleEpoch);
            Assert.True(firstEpoch.WorkQueueOverflowed);

            await manager.StopAsync();
            await manager.StartAsync();
            await GameplaySessionTestInfrastructure.WaitUntilAsync(
                () => manager.GetDiagnostics().LastAcceptedWorkSequence
                    == manager.GetDiagnostics().LastCompletedWorkSequence);

            var secondEpoch = manager.GetDiagnostics();
            Assert.Equal(2, secondEpoch.LifecycleEpoch);
            Assert.False(secondEpoch.WorkQueueOverflowed);
            Assert.False(secondEpoch.WorkQueue.Overflowed);
            Assert.True(secondEpoch.IsQuiescent);
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
    public async Task Stopped_state_events_are_not_buffered_into_rehydrated_ready_session()
    {
        var monitoring = new FakeMonitoringSessionManager();
        var parser = new GameplaySessionTestInfrastructure.FakeGameplayParserManager();
        var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dir);

        try
        {
            var contextId = MonitoringContextId.CreateNew();
            var source = GameplaySessionTestInfrastructure.DefaultSource();
            var ready = GameplaySessionTestInfrastructure.ReadyContext(contextId, source);
            monitoring.SetInitial(ParserTestSnapshots.Snapshot(1, ready));

            var manager = await GameplaySessionTestInfrastructure.CreateStartedManager(
                monitoring,
                parser,
                repository);

            await manager.StopAsync();

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:27:10 Welcome to City of Heroes, Stopped Hero!",
                    contextId,
                    source)
            ]);

            await manager.StartAsync();
            var session = Assert.Single(manager.Current.Sessions);
            Assert.Null(session.CharacterRecordId);
            Assert.NotEqual("Stopped Hero", session.CharacterDisplayName);
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
    public async Task Startup_prefix_async_admission_maintains_accounting_identity()
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
            var manager = new GameplaySessionManager(
                monitoring,
                parser,
                repository,
                new GameplaySessionOptions { WorkQueueCapacity = 1, MaxPreStartBufferCapacity = 4 });

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:27:10 Welcome to City of Heroes, Startup Hero!",
                    contextId,
                    source,
                    1),
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:27:11 second startup line",
                    contextId,
                    source,
                    2)
            ]);

            await manager.StartAsync();
            await GameplaySessionTestInfrastructure.WaitUntilAsync(() => manager.GetDiagnostics().WorkQueue.IsDrained);

            var workQueue = manager.GetDiagnostics().WorkQueue;
            Assert.True(workQueue.IsDrained);
            Assert.True(workQueue.MatchesAccountingIdentity());
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
    public void Work_queue_abandon_at_shutdown_maintains_accounting_identity()
    {
        var tracker = new QueuePressureTracker(capacity: 2);
        tracker.Reset(lifecycleEpoch: 1);
        tracker.BeginAdmission();
        tracker.BeginAdmission();
        tracker.RecordDequeued();
        tracker.AbandonUnfinishedAtShutdown();

        var snapshot = tracker.Snapshot();
        Assert.True(snapshot.IsDrained);
        Assert.Equal(2, snapshot.AbandonedCount);
        Assert.NotEqual(QueuePressureInvariantClassification.PhantomDepth, snapshot.ClassifyInvariant());
        Assert.True(snapshot.MatchesAccountingIdentity());
    }

    [Fact]
    public async Task ResetForNewRuntimeGeneration_clears_active_sessions_and_contexts()
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
            var manager = new GameplaySessionManager(monitoring, parser, repository);

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:27:10 Welcome to City of Heroes, Reset Hero!",
                    contextId,
                    source)
            ]);

            await manager.StartAsync();
            await GameplaySessionTestInfrastructure.WaitUntilAsync(
                () => manager.Current.Sessions.Any(session =>
                    session.ContextId == contextId
                    && session.CharacterRecordId is not null
                    && session.CharacterDisplayName == "Reset Hero"));
            await GameplaySessionTestInfrastructure.WaitForWorkQueueToDrainAsync(manager);
            var originalSessionId = Assert.Single(manager.Current.Sessions).SessionId;

            manager.ResetForNewRuntimeGeneration();
            await GameplaySessionTestInfrastructure.WaitUntilAsync(
                () => manager.Current.Sessions is [var replacement]
                    && replacement.ContextId == contextId
                    && replacement.SessionId != originalSessionId
                    && replacement.CharacterRecordId is null);
            await GameplaySessionTestInfrastructure.WaitForWorkQueueToDrainAsync(manager);

            var replacement = Assert.Single(manager.Current.Sessions);
            Assert.Equal(contextId, replacement.ContextId);
            Assert.NotEqual(originalSessionId, replacement.SessionId);
            Assert.Null(replacement.CharacterRecordId);
            Assert.Equal(CharacterIdentityResolutionState.Unresolved, replacement.CharacterIdentityResolutionState);
            Assert.Contains(
                manager.GetDiagnostics().RecentOperations,
                operation => operation.Contains("Live runtime generation reset", StringComparison.Ordinal));
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
    public async Task Older_monitoring_revision_cannot_remove_session_created_by_newer_ready_revision()
    {
        var monitoring = new FakeMonitoringSessionManager();
        var parser = new GameplaySessionTestInfrastructure.FakeGameplayParserManager();
        var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dir);

        try
        {
            var contextId = MonitoringContextId.CreateNew();
            var source = GameplaySessionTestInfrastructure.DefaultSource();
            using var manager = new GameplaySessionManager(monitoring, parser, repository);
            await manager.StartAsync();

            monitoring.Publish(ParserTestSnapshots.Snapshot(
                2,
                GameplaySessionTestInfrastructure.ReadyContext(contextId, source)));
            await GameplaySessionTestInfrastructure.WaitUntilAsync(
                () => manager.Current.Sessions.Count == 1);
            var sessionId = Assert.Single(manager.Current.Sessions).SessionId;

            monitoring.Publish(ParserTestSnapshots.Snapshot(1));
            await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
                manager.GetDiagnostics().RecentOperations.Any(operation =>
                    operation.Contains("Ignored stale monitoring snapshot revision 1 after 2", StringComparison.Ordinal)));

            Assert.Equal(sessionId, Assert.Single(manager.Current.Sessions).SessionId);
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
    public async Task Reset_rehydrates_equal_authoritative_revision_and_fences_older_work()
    {
        var monitoring = new FakeMonitoringSessionManager();
        var parser = new GameplaySessionTestInfrastructure.FakeGameplayParserManager();
        var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dir);

        try
        {
            var contextId = MonitoringContextId.CreateNew();
            var source = GameplaySessionTestInfrastructure.DefaultSource();
            monitoring.SetInitial(ParserTestSnapshots.Snapshot(
                5,
                GameplaySessionTestInfrastructure.ReadyContext(contextId, source)));
            using var manager = new GameplaySessionManager(monitoring, parser, repository);
            await manager.StartAsync();
            var originalSessionId = Assert.Single(manager.Current.Sessions).SessionId;

            manager.ResetForNewRuntimeGeneration();
            await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
                manager.Current.Sessions.Count == 1
                && manager.Current.Sessions[0].SessionId != originalSessionId);
            var rehydratedSessionId = manager.Current.Sessions[0].SessionId;

            monitoring.Publish(ParserTestSnapshots.Snapshot(4));
            await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
                manager.GetDiagnostics().RecentOperations.Any(operation =>
                    operation.Contains("Ignored stale monitoring snapshot revision 4 after 5", StringComparison.Ordinal)));

            Assert.Equal(rehydratedSessionId, Assert.Single(manager.Current.Sessions).SessionId);
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
