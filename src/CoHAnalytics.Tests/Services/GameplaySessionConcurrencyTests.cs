using System.IO;
using CoHAnalytics.Models;
using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Services;

public sealed class GameplaySessionConcurrencyTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Processor_failure_completes_accepted_command_and_finalizes_once(bool confirmCharacter)
    {
        var monitoring = new FakeMonitoringSessionManager();
        var parser = new GameplaySessionTestInfrastructure.FakeGameplayParserManager();
        var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dir);
        var processorEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseProcessor = new ManualResetEventSlim();
        var commandsAdmitted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finalizationCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failNextItem = 0;
        var commandAdmissionCount = 0;
        var finalizationCount = 0;
        var emptySessionCallbacks = 0;
        var callbackCount = 0;
        var options = new GameplaySessionOptions
        {
            WorkQueueCapacity = 4,
            TestHooks = new GameplaySessionTestHooks
            {
                BeforeProcessWorkItem = () =>
                {
                    if (Interlocked.Exchange(ref failNextItem, 0) == 0)
                    {
                        return;
                    }

                    processorEntered.TrySetResult();
                    releaseProcessor.Wait();
                    throw new InvalidOperationException("controlled processor failure");
                },
                AfterCommandAdmission = () =>
                {
                    if (Interlocked.Increment(ref commandAdmissionCount) == 2)
                    {
                        commandsAdmitted.TrySetResult();
                    }
                },
                AfterEpochFinalization = () =>
                {
                    Interlocked.Increment(ref finalizationCount);
                    finalizationCompleted.TrySetResult();
                }
            }
        };

        try
        {
            var contextId = MonitoringContextId.CreateNew();
            var source = GameplaySessionTestInfrastructure.DefaultSource();
            var record = repository.EstablishTrustedFromWelcome("acct-1", "Known Hero");
            monitoring.SetInitial(ParserTestSnapshots.Snapshot(
                1,
                GameplaySessionTestInfrastructure.ReadyContext(contextId, source)));

            var manager = await GameplaySessionTestInfrastructure.CreateStartedManager(
                monitoring,
                parser,
                repository,
                options);
            manager.StateChanged += (_, args) =>
            {
                Interlocked.Increment(ref callbackCount);
                if (args.Snapshot.Sessions.Count == 0)
                {
                    Interlocked.Increment(ref emptySessionCallbacks);
                }
            };
            manager.CommittedEventsAvailable += (_, _) => Interlocked.Increment(ref callbackCount);

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:27:10 ordinary line",
                    contextId,
                    source,
                    1)
            ]);
            await GameplaySessionTestInfrastructure.WaitForWorkQueueToDrainAsync(manager);
            Assert.Single(manager.Current.Sessions, session => session.ContextId == contextId);

            Volatile.Write(ref failNextItem, 1);
            var failingCommandTask = Task.Run(() => confirmCharacter
                ? manager.ConfirmCharacter(contextId, record.RecordId!)
                : manager.ClearIdentity(contextId));
            await processorEntered.Task;

            var queuedCommandTask = Task.Run(() => confirmCharacter
                ? manager.ClearIdentity(contextId)
                : manager.ConfirmCharacter(contextId, record.RecordId!));
            await commandsAdmitted.Task;
            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:27:11 abandoned parser payload",
                    contextId,
                    source,
                    2)
            ]);

            releaseProcessor.Set();
            var results = await Task.WhenAll(failingCommandTask, queuedCommandTask);
            Assert.All(results, result =>
                Assert.Equal(GameplaySessionOutcome.ProcessingFailed, result.Outcome));
            await finalizationCompleted.Task;

            Assert.False(manager.GetDiagnostics().IsRunning);
            Assert.Equal(1, Volatile.Read(ref finalizationCount));
            Assert.Equal(1, Volatile.Read(ref emptySessionCallbacks));
            Assert.Contains(
                manager.GetDiagnostics().RecentOperations,
                operation => operation == "Abandoned 1 queued non-command work item(s) after processing failure.");
            Assert.DoesNotContain(
                manager.GetDiagnostics().RecentOperations,
                operation => operation.Contains("abandoned parser payload", StringComparison.Ordinal));
            var callbacksAtCompletion = Volatile.Read(ref callbackCount);
            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:27:12 rejected after failure",
                    contextId,
                    source,
                    3)
            ]);
            Assert.Equal(callbacksAtCompletion, Volatile.Read(ref callbackCount));
            Assert.Equal(0, parser.ClassifiedEventsSubscriptionCount);
            Assert.Equal(0, monitoring.StateChangedSubscriptionCount);
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
    public async Task Parser_batch_confirm_parser_batch_preserves_command_event_order()
    {
        var monitoring = new FakeMonitoringSessionManager();
        var parser = new GameplaySessionTestInfrastructure.FakeGameplayParserManager();
        var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dir);
        var observableOrder = new List<string>();
        var committed = new List<GameplaySessionEvent>();
        var confirmationObserved = false;

        try
        {
            var contextId = MonitoringContextId.CreateNew();
            var source = GameplaySessionTestInfrastructure.DefaultSource();
            var record = repository.EstablishTrustedFromWelcome("acct-1", "Known Hero");
            monitoring.SetInitial(ParserTestSnapshots.Snapshot(
                1,
                GameplaySessionTestInfrastructure.ReadyContext(contextId, source)));
            var manager = await GameplaySessionTestInfrastructure.CreateStartedManager(
                monitoring,
                parser,
                repository);

            manager.CommittedEventsAvailable += (_, args) =>
            {
                foreach (var event_ in args.Events)
                {
                    committed.Add(event_);
                    observableOrder.Add($"event-{event_.ParserEvent.Sequence}");
                }
            };
            manager.StateChanged += (_, args) =>
            {
                if (args.Snapshot.Sessions.SingleOrDefault()?.CharacterIdentityConfidence
                    == CharacterIdentityConfidence.Confirmed
                    && !confirmationObserved)
                {
                    confirmationObserved = true;
                    observableOrder.Add("confirmation");
                }
            };

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify("2026-08-04 06:27:10 A", contextId, source, 1),
                GameplaySessionTestInfrastructure.Classify("2026-08-04 06:27:11 B", contextId, source, 2)
            ]);
            var confirmation = manager.ConfirmCharacter(contextId, record.RecordId!);
            Assert.True(confirmation.IsSuccess);

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify("2026-08-04 06:27:12 C", contextId, source, 3),
                GameplaySessionTestInfrastructure.Classify("2026-08-04 06:27:13 D", contextId, source, 4)
            ]);
            await GameplaySessionTestInfrastructure.WaitUntilAsync(() => committed.Count == 4);

            Assert.Equal(
                ["event-1", "event-2", "confirmation", "event-3", "event-4"],
                observableOrder);
            Assert.Equal([1L, 2L, 3L, 4L], committed.Select(event_ => event_.ParserEvent.Sequence));
            Assert.Equal([1L, 2L, 3L, 4L], committed.Select(event_ => event_.SessionSequence));
            Assert.All(committed, event_ => Assert.Equal(record.RecordId, event_.CharacterRecordId));
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
    public async Task Multiple_contexts_remain_isolated()
    {
        var monitoring = new FakeMonitoringSessionManager();
        var parser = new GameplaySessionTestInfrastructure.FakeGameplayParserManager();
        var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dir);

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
                repository);

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:27:10 Welcome to City of Heroes, Hero A!",
                    contextA,
                    sourceA,
                    1),
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:27:10 Welcome to City of Heroes, Hero B!",
                    contextB,
                    sourceB,
                    1)
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
                manager.Current.Sessions.Any(session =>
                    session.ContextId == contextA
                    && session.CharacterRecordId is not null
                    && session.CharacterDisplayName == "Hero A")
                && manager.Current.Sessions.Any(session =>
                    session.ContextId == contextB
                    && session.CharacterRecordId is not null
                    && session.CharacterDisplayName == "Hero B"));

            var sessions = manager.Current.Sessions;
            Assert.Equal("Hero A", sessions.Single(session => session.ContextId == contextA).CharacterDisplayName);
            Assert.Equal("Hero B", sessions.Single(session => session.ContextId == contextB).CharacterDisplayName);
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
    public async Task Same_character_record_in_two_contexts_produces_conflict()
    {
        var monitoring = new FakeMonitoringSessionManager();
        var parser = new GameplaySessionTestInfrastructure.FakeGameplayParserManager();
        var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dir);

        try
        {
            var contextA = MonitoringContextId.CreateNew();
            var contextB = MonitoringContextId.CreateNew();
            var sourceA = GameplaySessionTestInfrastructure.DefaultSource("acct-1");
            var sourceB = LogSourceId.Create(
                "acct-1",
                "acct-1",
                @"C:\fake\chatlog 2026-08-05.txt",
                new DateOnly(2026, 8, 5));
            var trusted = repository.EstablishTrustedFromWelcome("acct-1", "Shared Hero");

            monitoring.SetInitial(ParserTestSnapshots.Snapshot(
                1,
                GameplaySessionTestInfrastructure.ReadyContext(contextA, sourceA),
                GameplaySessionTestInfrastructure.ReadyContext(contextB, sourceB)));

            var manager = await GameplaySessionTestInfrastructure.CreateStartedManager(
                monitoring,
                parser,
                repository);

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:27:10 Welcome to City of Heroes, Shared Hero!",
                    contextA,
                    sourceA,
                    1)
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(
                () => manager.Current.Sessions.Any(session => session.ContextId == contextA
                    && session.CharacterIdentityResolutionState == CharacterIdentityResolutionState.Resolved));

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:27:10 Welcome to City of Heroes, Shared Hero!",
                    contextB,
                    sourceB,
                    1)
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(
                () => manager.Current.Sessions.Any(session => session.ContextId == contextB
                    && session.CharacterIdentityResolutionState == CharacterIdentityResolutionState.Conflicted));

            var sessionB = manager.Current.Sessions.Single(session => session.ContextId == contextB);
            Assert.Equal(CharacterIdentityConfidence.Unknown, sessionB.CharacterIdentityConfidence);
            Assert.True(sessionB.NeedsAttention);
            Assert.Equal(trusted.RecordId, repository.TryGetRecord(trusted.RecordId!)!.RecordId);
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
    public async Task Committed_events_are_ordered_and_exactly_once_after_resolution()
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

            repository.EstablishTrustedFromWelcome("acct-1", "Example Hero");

            var manager = await GameplaySessionTestInfrastructure.CreateStartedManager(
                monitoring,
                parser,
                repository);
            manager.CommittedEventsAvailable += (_, args) => committed.AddRange(args.Events);

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:27:10 Welcome to City of Heroes, Example Hero!",
                    contextId,
                    source,
                    1),
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:27:11 line two",
                    contextId,
                    source,
                    2),
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:27:12 line three",
                    contextId,
                    source,
                    3)
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(() => committed.Count >= 3);
            Assert.Equal([1L, 2L, 3L], committed.Select(event_ => event_.ParserEvent.Sequence).ToArray());
            Assert.Equal(committed.Select(event_ => event_.SessionSequence).Distinct().Count(), committed.Count);
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
    public async Task Subscriber_can_read_current_without_deadlock()
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

            var readCompleted = false;
            manager.CommittedEventsAvailable += (_, _) =>
            {
                _ = manager.Current;
                readCompleted = true;
            };

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:27:10 Welcome to City of Heroes, Example Hero!",
                    contextId,
                    source)
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(() => readCompleted);
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
    public async Task Subscriber_exception_does_not_stop_event_processing()
    {
        var monitoring = new FakeMonitoringSessionManager();
        var parser = new GameplaySessionTestInfrastructure.FakeGameplayParserManager();
        var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dir);
        var committed = 0;

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
            manager.CommittedEventsAvailable += (_, _) => throw new InvalidOperationException("subscriber failed");
            manager.CommittedEventsAvailable += (_, args) => committed += args.Events.Count;

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:27:10 Welcome to City of Heroes, Example Hero!",
                    contextId,
                    source)
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(() => committed >= 1);
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
    public async Task ConfirmCharacter_returns_failure_when_repository_persistence_fails()
    {
        var monitoring = new FakeMonitoringSessionManager();
        var parser = new GameplaySessionTestInfrastructure.FakeGameplayParserManager();
        var dataDirectory = Path.Combine(Path.GetTempPath(), "coh-analytics-gameplay", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dataDirectory);
        var repository = new CharacterRepository(new CharacterRepositoryOptions
        {
            DataDirectory = dataDirectory,
            SimulatePersistenceFailure = true
        });

        try
        {
            var contextId = MonitoringContextId.CreateNew();
            var source = GameplaySessionTestInfrastructure.DefaultSource();
            monitoring.SetInitial(ParserTestSnapshots.Snapshot(
                1,
                GameplaySessionTestInfrastructure.ReadyContext(contextId, source)));

            var establish = repository.EstablishTrustedFromManualConfirmation("acct-1", "Manual Hero");
            Assert.False(establish.IsSuccess);

            var manager = await GameplaySessionTestInfrastructure.CreateStartedManager(
                monitoring,
                parser,
                repository);

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:27:10 structurally timestamped",
                    contextId,
                    source)
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(
                () => manager.Current.Sessions.Count == 1);

            var record = repository.TryFindTrustedByDisplayName("acct-1", "Manual Hero")!;
            var result = manager.ConfirmCharacter(contextId, record.RecordId);
            Assert.False(result.IsSuccess);
        }
        finally
        {
            try
            {
                Directory.Delete(dataDirectory, recursive: true);
            }
            catch
            {
            }
        }
    }

    [Fact]
    public async Task Work_queue_failure_resets_on_restart()
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

            Assert.True(manager.GetDiagnostics().WorkQueueOverflowed);
            await manager.StopAsync();

            await manager.StartAsync();
            Assert.False(manager.GetDiagnostics().WorkQueueOverflowed);
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
    public async Task Overload_rejects_later_work_and_detaches_subscriptions()
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

            Assert.Equal(0, monitoring.StateChangedSubscriptionCount);
            Assert.Equal(0, parser.ClassifiedEventsSubscriptionCount);

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:27:20 detached welcome",
                    contextId,
                    source,
                    99)
            ]);

            await Task.Delay(50);
            Assert.Empty(manager.Current.Sessions);
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
    public async Task Reentrant_ConfirmCharacter_from_StateChanged_is_rejected()
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
            var established = repository.EstablishTrustedFromWelcome("acct-1", "Example Hero");

            var manager = await GameplaySessionTestInfrastructure.CreateStartedManager(
                monitoring,
                parser,
                repository);

            var rejected = false;
            manager.StateChanged += (_, _) =>
            {
                var result = manager.ConfirmCharacter(contextId, established.RecordId!);
                rejected = result.Outcome == GameplaySessionOutcome.ReentrantCommandRejected;
            };

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:27:10 Welcome to City of Heroes, Example Hero!",
                    contextId,
                    source)
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(() => rejected);
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
    public async Task Deferred_ui_style_command_after_callback_is_accepted()
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
            var established = repository.EstablishTrustedFromWelcome("acct-1", "Example Hero");

            var manager = await GameplaySessionTestInfrastructure.CreateStartedManager(
                monitoring,
                parser,
                repository);

            var accepted = false;
            manager.StateChanged += (_, _) =>
            {
                Task.Run(() =>
                {
                    var result = manager.ConfirmCharacter(contextId, established.RecordId!);
                    accepted = result.IsSuccess;
                });
            };

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:27:10 Welcome to City of Heroes, Example Hero!",
                    contextId,
                    source)
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(() => accepted);
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
    public async Task Work_queue_diagnostics_track_capacity_depth_peak_and_overload_accounting()
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

            var afterStart = manager.GetDiagnostics();
            Assert.Equal(1, afterStart.LifecycleEpoch);
            Assert.Equal(1, afterStart.WorkQueue.Capacity);

            await GameplaySessionTestInfrastructure.CauseOverloadAsync(
                manager,
                monitoring,
                parser,
                contextId,
                source);

            var overloaded = manager.GetDiagnostics();
            Assert.True(overloaded.WorkQueueOverflowed);
            Assert.True(overloaded.WorkQueue.Overflowed);
            Assert.True(overloaded.WorkQueue.RejectedCount >= 1);
            Assert.True(overloaded.WorkQueue.PeakDepth >= 1);
            Assert.True(overloaded.WorkQueue.CurrentDepth <= overloaded.WorkQueue.PeakDepth);
            Assert.All(overloaded.RecentOperations, operation => Assert.DoesNotContain(@"chatlog", operation));
            Assert.All(overloaded.RecentOperations, operation => Assert.DoesNotContain("Stall Hero", operation));
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
    public async Task Pending_committed_event_diagnostics_track_peak_and_discards()
    {
        var monitoring = new FakeMonitoringSessionManager();
        var parser = new GameplaySessionTestInfrastructure.FakeGameplayParserManager();
        var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dir);
        var options = new GameplaySessionOptions { MaxPendingCommittedEvents = 1 };

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
                options);

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:27:10 Welcome to City of Heroes, Pending Hero!",
                    contextId,
                    source,
                    1),
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:27:11 second committed line",
                    contextId,
                    source,
                    2),
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:27:12 third committed line",
                    contextId,
                    source,
                    3)
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(
                () => manager.GetDiagnostics().PendingCommittedEvents.DiscardedCount >= 1
                    && manager.GetDiagnostics().PendingCommittedEventCount == 0);

            var diagnostics = manager.GetDiagnostics();
            Assert.Equal(1, diagnostics.PendingCommittedEvents.Capacity);
            Assert.Equal(1, diagnostics.PendingCommittedEvents.PeakCount);
            Assert.True(diagnostics.PendingCommittedEvents.DiscardedCount >= 1);
            Assert.Equal(0, diagnostics.PendingCommittedEventCount);
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
    public async Task Live_work_queue_sync_admission_maintains_accounting_identity()
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
                repository,
                new GameplaySessionOptions { WorkQueueCapacity = 4 });

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:27:10 ordinary line",
                    contextId,
                    source,
                    1)
            ]);
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
    public async Task Work_queue_overload_rejection_preserves_accounting_identity()
    {
        using var directory = new ParserTestDirectory();
        var path = directory.CreateFile();
        var source = ParserTestSnapshots.Source(path);
        var contextId = MonitoringContextId.CreateNew();
        const string accountStableId = "acct-1";
        const string characterName = "Queue Hero";
        const string blockingLine = "2026-08-04 06:27:11 blocking line";
        const string overflowLine = "2026-08-04 06:27:12 overflow line";
        var monitoring = new FakeMonitoringSessionManager();
        monitoring.SetInitial(ParserTestSnapshots.Snapshot(
            1,
            GameplaySessionTestInfrastructure.ReadyContext(contextId, source)));

        var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dir);
        var options = new GameplaySessionOptions { WorkQueueCapacity = 1 };
        await using var parser = new ParserManager(monitoring, ParserTestSnapshots.FastOptions());
        var manager = new GameplaySessionManager(monitoring, parser, repository, options);

        try
        {
            await parser.StartAsync();
            await manager.StartAsync();

            var resolvedSession = new TaskCompletionSource<GameplaySessionSnapshot>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            void ObserveResolvedSession(GameplaySessionManagerSnapshot snapshot)
            {
                var session = snapshot.Sessions.SingleOrDefault(candidate =>
                    candidate.ContextId == contextId
                    && candidate.AccountStableId == accountStableId
                    && candidate.CharacterDisplayName == characterName
                    && candidate.LifecycleState == GameplaySessionLifecycleState.Active
                    && candidate.CharacterIdentityResolutionState == CharacterIdentityResolutionState.Resolved);
                if (session is not null)
                {
                    resolvedSession.TrySetResult(session);
                }
            }

            manager.StateChanged += (_, args) => ObserveResolvedSession(args.Snapshot);
            ObserveResolvedSession(manager.Current);
            directory.Append(path, $"2026-08-04 06:27:10 Welcome to City of Heroes, {characterName}!\r\n");
            var expectedSession = await resolvedSession.Task;
            await GameplaySessionTestInfrastructure.WaitForWorkQueueToDrainAsync(manager);
            var baseline = manager.GetDiagnostics().WorkQueue;
            Assert.True(baseline.IsDrained);

            var processingStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseProcessing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void BlockProcessor()
            {
                processingStarted.TrySetResult();
                releaseProcessing.Task.Wait();
            }

            manager.CommittedEventsAvailable += (_, args) =>
            {
                if (args.Events.Any(event_ =>
                        event_.ParserEvent.ContextId == contextId
                        && event_.ParserEvent.SourceId == source
                        && event_.ParserEvent.Sequence == 2
                        && event_.ParserEvent.RawLine == blockingLine))
                {
                    BlockProcessor();
                }
            };
            directory.Append(path, $"{blockingLine}\r\n");
            await processingStarted.Task;

            monitoring.Publish(ParserTestSnapshots.Snapshot(
                2,
                GameplaySessionTestInfrastructure.ReadyContext(contextId, source)));

            var overflowClassified = new TaskCompletionSource<ParserEvent>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            parser.ClassifiedEventsAvailable += (_, args) =>
            {
                var matchingEvent = args.Events.SingleOrDefault(event_ =>
                    event_.ContextId == contextId
                    && event_.SourceId == source
                    && event_.Sequence == 3
                    && event_.RawLine == overflowLine);
                if (matchingEvent is not null)
                {
                    overflowClassified.TrySetResult(matchingEvent);
                }
            };
            directory.Append(path, $"{overflowLine}\r\n");
            await overflowClassified.Task;

            var workQueue = manager.GetDiagnostics().WorkQueue;
            Assert.True(workQueue.Overflowed);
            Assert.Equal(baseline.RejectedCount + 1, workQueue.RejectedCount);
            Assert.Equal(baseline.AcceptedCount + 2, workQueue.AcceptedCount);
            Assert.Equal(baseline.CompletedCount, workQueue.CompletedCount);
            Assert.Equal(baseline.AbandonedCount, workQueue.AbandonedCount);
            Assert.Equal(1, workQueue.CurrentDepth);
            Assert.Equal(1, workQueue.InFlightCount);
            Assert.NotEqual(QueuePressureInvariantClassification.PhantomDepth, workQueue.ClassifyInvariant());
            Assert.True(workQueue.MatchesAccountingIdentity());
            Assert.Equal(
                workQueue.AcceptedCount,
                workQueue.CompletedCount
                    + workQueue.AbandonedCount
                    + workQueue.CurrentDepth
                    + workQueue.InFlightCount);

            var sessionAfterRejection = Assert.Single(
                manager.Current.Sessions,
                session => session.ContextId == contextId);
            Assert.Equal(expectedSession.SessionId, sessionAfterRejection.SessionId);
            Assert.Equal(accountStableId, sessionAfterRejection.AccountStableId);
            Assert.Equal(characterName, sessionAfterRejection.CharacterDisplayName);
            Assert.Equal(GameplaySessionLifecycleState.Active, sessionAfterRejection.LifecycleState);
            Assert.Equal(
                CharacterIdentityResolutionState.Resolved,
                sessionAfterRejection.CharacterIdentityResolutionState);

            releaseProcessing.TrySetResult();
            await manager.StopAsync();
            await parser.StopAsync();
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
    public async Task Epoch_restart_resets_work_queue_accounting()
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
                repository,
                new GameplaySessionOptions { WorkQueueCapacity = 4 });

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:27:10 ordinary line",
                    contextId,
                    source,
                    1)
            ]);
            await GameplaySessionTestInfrastructure.WaitUntilAsync(() => manager.GetDiagnostics().WorkQueue.IsDrained);
            var firstEpoch = manager.GetDiagnostics().LifecycleEpoch;
            await manager.StopAsync();
            await manager.StartAsync();
            await GameplaySessionTestInfrastructure.WaitUntilAsync(() => manager.GetDiagnostics().WorkQueue.IsDrained);

            var workQueue = manager.GetDiagnostics().WorkQueue;
            Assert.NotEqual(firstEpoch, manager.GetDiagnostics().LifecycleEpoch);
            Assert.Equal(firstEpoch + 1, workQueue.LifecycleEpoch);
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
}
