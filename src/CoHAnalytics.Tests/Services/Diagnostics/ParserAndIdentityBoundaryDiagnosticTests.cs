using System.Collections.Concurrent;
using System.Text.Json;
using CoHAnalytics.Models;
using CoHAnalytics.Services;
using CoHAnalytics.Services.Diagnostics;

namespace CoHAnalytics.Tests.Services.Diagnostics;

public sealed class ParserAndIdentityBoundaryDiagnosticTests
{
    [Fact]
    public async Task Worker_creation_binding_and_state_emit_in_order_with_enriched_snapshot()
    {
        using var directory = new ParserTestDirectory();
        var path = directory.CreateFile();
        var source = ParserTestSnapshots.Source(path);
        var contextId = MonitoringContextId.CreateNew();
        var context = ParserTestSnapshots.Context(
            contextId,
            MonitoringContextState.Ready,
            source,
            1,
            MonitoringSourceTransitionKind.SourceAssigned);
        var monitoring = new FakeMonitoringSessionManager();
        monitoring.SetInitial(ParserTestSnapshots.Snapshot(1, context));
        var log = new RecordingDiagnosticLog();
        await using var parser = CreateParser(monitoring, log);

        await parser.StartAsync();

        var createdIndex = IndexOf<ParserWorkerCreatedDiagnosticEvent>(log.Events);
        var bindingIndex = IndexOf<ParserWorkerBindingAppliedDiagnosticEvent>(log.Events);
        var stateIndex = IndexOf<ParserWorkerStateChangedDiagnosticEvent>(log.Events);
        var snapshotIndex = IndexOf<DiagnosticsStateSnapshotCapturedDiagnosticEvent>(log.Events);
        Assert.True(createdIndex < bindingIndex);
        Assert.True(bindingIndex < snapshotIndex);
        Assert.True(stateIndex < snapshotIndex);
        var binding = Assert.Single(log.Events.OfType<ParserWorkerBindingAppliedDiagnosticEvent>());
        Assert.Equal(contextId.ToString(), binding.ContextId);
        Assert.Equal(source.Value, binding.SourceId);
        Assert.Equal(1, binding.BindingGeneration);
        Assert.Equal(MonitoringSourceTransitionKind.SourceAssigned, binding.TransitionKind);
        var snapshot = log.Events.OfType<DiagnosticsStateSnapshotCapturedDiagnosticEvent>().Last();
        var worker = Assert.Single(snapshot.ParserWorkers);
        Assert.Equal(binding.WorkerId, worker.WorkerId);
        Assert.Equal(contextId.ToString(), worker.ContextId);
        Assert.Equal(source.Value, worker.SourceId);
        Assert.Equal(source.FileName, worker.SourceFileName);
        Assert.Equal(1, worker.BindingGeneration);
    }

    [Fact]
    public async Task Worker_state_change_emits_once_and_unchanged_state_does_not_repeat()
    {
        using var directory = new ParserTestDirectory();
        var source = ParserTestSnapshots.Source(directory.CreateFile());
        var contextId = MonitoringContextId.CreateNew();
        var ready = ParserTestSnapshots.Context(
            contextId,
            MonitoringContextState.Ready,
            source,
            1,
            MonitoringSourceTransitionKind.SourceAssigned);
        var monitoring = new FakeMonitoringSessionManager();
        monitoring.SetInitial(ParserTestSnapshots.Snapshot(1, ready));
        var log = new RecordingDiagnosticLog();
        await using var parser = CreateParser(monitoring, log);
        await parser.StartAsync();
        log.Clear();

        monitoring.Publish(ParserTestSnapshots.Snapshot(
            2,
            ready with
            {
                State = MonitoringContextState.RuntimeSuspended,
                PreviousStateBeforeSuspension = MonitoringContextState.Ready
            }));
        await ParserTestSnapshots.WaitUntilAsync(() =>
            Assert.Single(parser.Current.Workers).State == ParserWorkerState.Suspended);

        var changed = Assert.Single(log.Events.OfType<ParserWorkerStateChangedDiagnosticEvent>());
        Assert.Equal(ParserWorkerState.WaitingForData, changed.PreviousState);
        Assert.Equal(ParserWorkerState.Suspended, changed.NextState);
        log.Clear();
        monitoring.Publish(ParserTestSnapshots.Snapshot(
            3,
            ready with
            {
                State = MonitoringContextState.RuntimeSuspended,
                PreviousStateBeforeSuspension = MonitoringContextState.Ready
            }));
        await Task.Delay(50);

        Assert.Empty(log.Events.OfType<ParserWorkerStateChangedDiagnosticEvent>());
    }

    [Fact]
    public async Task Worker_rebinding_emits_one_new_binding_event()
    {
        using var directory = new ParserTestDirectory();
        var first = ParserTestSnapshots.Source(directory.CreateFile("first.txt"));
        var second = ParserTestSnapshots.Source(directory.CreateFile("second.txt"), identityGeneration: 1);
        var contextId = MonitoringContextId.CreateNew();
        var initial = ParserTestSnapshots.Context(
            contextId,
            MonitoringContextState.Ready,
            first,
            1,
            MonitoringSourceTransitionKind.SourceAssigned);
        var monitoring = new FakeMonitoringSessionManager();
        monitoring.SetInitial(ParserTestSnapshots.Snapshot(1, initial));
        var log = new RecordingDiagnosticLog();
        await using var parser = CreateParser(monitoring, log);
        await parser.StartAsync();
        log.Clear();

        monitoring.Publish(ParserTestSnapshots.Snapshot(
            2,
            initial with
            {
                CurrentSourceId = second,
                PreviousSourceId = first,
                SourceBindingGeneration = 2,
                LastSourceBindingTransitionKind = MonitoringSourceTransitionKind.SourceReplaced
            }));
        await ParserTestSnapshots.WaitUntilAsync(() =>
            Assert.Single(parser.Current.Workers).AppliedSourceBindingGeneration == 2);

        var binding = Assert.Single(log.Events.OfType<ParserWorkerBindingAppliedDiagnosticEvent>());
        Assert.Equal(second.Value, binding.SourceId);
        Assert.Equal(2, binding.BindingGeneration);
        Assert.Equal(MonitoringSourceTransitionKind.SourceReplaced, binding.TransitionKind);
    }

    [Fact]
    public async Task Worker_removal_emits_removed_event_and_empty_snapshot()
    {
        using var directory = new ParserTestDirectory();
        var source = ParserTestSnapshots.Source(directory.CreateFile());
        var contextId = MonitoringContextId.CreateNew();
        var ready = ParserTestSnapshots.Context(
            contextId,
            MonitoringContextState.Ready,
            source,
            1,
            MonitoringSourceTransitionKind.SourceAssigned);
        var monitoring = new FakeMonitoringSessionManager();
        monitoring.SetInitial(ParserTestSnapshots.Snapshot(1, ready));
        var log = new RecordingDiagnosticLog();
        await using var parser = CreateParser(monitoring, log);
        await parser.StartAsync();
        log.Clear();

        monitoring.Publish(ParserTestSnapshots.Snapshot(
            2,
            ready with
            {
                State = MonitoringContextState.Stopped,
                CurrentSourceId = null,
                PreviousSourceId = source,
                SourceBindingGeneration = 2,
                LastSourceBindingTransitionKind = MonitoringSourceTransitionKind.ContextRemoved
            }));
        await ParserTestSnapshots.WaitUntilAsync(() => parser.Current.Workers.Count == 0);

        var removed = Assert.Single(log.Events.OfType<ParserWorkerRemovedDiagnosticEvent>());
        Assert.Equal(contextId.ToString(), removed.ContextId);
        Assert.Equal("MonitoringContextRemoved", removed.Reason);
        var snapshot = log.Events.OfType<DiagnosticsStateSnapshotCapturedDiagnosticEvent>().Last();
        Assert.Equal("ParserWorkerRemoved", snapshot.Reason);
        Assert.Empty(snapshot.ParserWorkers);
    }

    [Fact]
    public async Task Worker_io_fault_emits_sanitized_fault_evidence()
    {
        using var directory = new ParserTestDirectory();
        var log = new RecordingDiagnosticLog();
        var contextId = MonitoringContextId.CreateNew();
        var missingPath = Path.Combine(directory.Root, "missing.txt");
        var source = ParserTestSnapshots.Source(missingPath);
        var monitoring = new FakeMonitoringSessionManager();
        monitoring.SetInitial(ParserTestSnapshots.Snapshot(
            1,
            ParserTestSnapshots.Context(
                contextId,
                MonitoringContextState.Ready,
                source,
                1,
                MonitoringSourceTransitionKind.SourceAssigned)));
        await using var parser = CreateParser(monitoring, log);

        await parser.StartAsync();

        var fault = Assert.Single(log.Events.OfType<ParserWorkerFaultedDiagnosticEvent>());
        Assert.Equal(contextId.ToString(), fault.ContextId);
        Assert.Equal("FileNotFoundException", fault.ExceptionType);
        Assert.Equal("source_io_failure", fault.FaultCode);
        var snapshot = log.Events.OfType<DiagnosticsStateSnapshotCapturedDiagnosticEvent>().Last();
        Assert.Equal("ParserWorkerFaulted", snapshot.Reason);
        Assert.Equal(ParserWorkerState.Faulted, Assert.Single(snapshot.ParserWorkers).State);
        var serialized = JsonSerializer.Serialize(fault);
        Assert.DoesNotContain(missingPath, serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stack", serialized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Welcome_classification_emits_rare_identity_evidence_but_ordinary_line_does_not()
    {
        using var directory = new ParserTestDirectory();
        var path = directory.CreateFile();
        var source = ParserTestSnapshots.Source(path);
        var contextId = MonitoringContextId.CreateNew();
        var monitoring = new FakeMonitoringSessionManager();
        monitoring.SetInitial(ParserTestSnapshots.Snapshot(
            1,
            ParserTestSnapshots.Context(
                contextId,
                MonitoringContextState.Ready,
                source,
                1,
                MonitoringSourceTransitionKind.SourceAssigned)));
        var log = new RecordingDiagnosticLog();
        await using var parser = CreateParser(monitoring, log);
        await parser.StartAsync();
        log.Clear();

        directory.Append(
            path,
            "2026-08-04 06:27:09 ordinary filler\r\n" +
            "2026-08-04 06:27:10 Welcome to City of Heroes, Example Hero!\r\n");
        await ParserTestSnapshots.WaitUntilAsync(() =>
            log.Events.OfType<ParserIdentityEvidenceClassifiedDiagnosticEvent>().Any());

        var identity = Assert.Single(log.Events.OfType<ParserIdentityEvidenceClassifiedDiagnosticEvent>());
        Assert.Equal(contextId.ToString(), identity.ContextId);
        Assert.False(string.IsNullOrWhiteSpace(identity.WorkerId));
        Assert.Equal(2, identity.ParserSequence);
        Assert.Equal(ParserStructuralEvidenceKind.WelcomeAttribution, identity.EvidenceKind);
        Assert.Equal("Example Hero", identity.CandidateCharacterName);
        Assert.Equal(source.Value, identity.SourceId);
        Assert.Equal(1, identity.BindingGeneration);
        var serialized = JsonSerializer.Serialize(identity);
        Assert.DoesNotContain("ordinary filler", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("Welcome to City", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("rawLine", serialized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Two_contexts_keep_identity_evidence_on_the_originating_worker()
    {
        using var directory = new ParserTestDirectory();
        var pathA = directory.CreateFile("a.txt");
        var pathB = directory.CreateFile("b.txt");
        var sourceA = ParserTestSnapshots.Source(pathA, "acct-a");
        var sourceB = ParserTestSnapshots.Source(pathB, "acct-b");
        var contextA = MonitoringContextId.CreateNew();
        var contextB = MonitoringContextId.CreateNew();
        var monitoring = new FakeMonitoringSessionManager();
        monitoring.SetInitial(ParserTestSnapshots.Snapshot(
            1,
            ParserTestSnapshots.Context(contextA, MonitoringContextState.Ready, sourceA, 1, MonitoringSourceTransitionKind.SourceAssigned),
            ParserTestSnapshots.Context(contextB, MonitoringContextState.Ready, sourceB, 1, MonitoringSourceTransitionKind.SourceAssigned)));
        var log = new RecordingDiagnosticLog();
        await using var parser = CreateParser(monitoring, log);
        await parser.StartAsync();
        var workerA = log.Events.OfType<ParserWorkerCreatedDiagnosticEvent>()
            .Single(item => item.ContextId == contextA.ToString()).WorkerId;
        log.Clear();

        directory.Append(pathA, "2026-08-04 06:27:10 Welcome to City of Heroes, Hero A!\r\n");
        directory.Append(pathB, "2026-08-04 06:27:11 ordinary filler\r\n");
        await ParserTestSnapshots.WaitUntilAsync(() =>
            log.Events.OfType<ParserIdentityEvidenceClassifiedDiagnosticEvent>().Any());

        var identity = Assert.Single(log.Events.OfType<ParserIdentityEvidenceClassifiedDiagnosticEvent>());
        Assert.Equal(contextA.ToString(), identity.ContextId);
        Assert.Equal(workerA, identity.WorkerId);
        Assert.NotEqual(contextB.ToString(), identity.ContextId);
    }

    [Fact]
    public async Task Welcome_processing_records_replacement_and_unbound_outcomes()
    {
        var log = new RecordingDiagnosticLog();
        await RunWelcomeScenario(log, accountBound: true, stopped: false);
        var accepted = Assert.Single(log.Events.OfType<GameplaySessionWelcomeProcessedDiagnosticEvent>());
        Assert.Equal(GameplayWelcomeProcessingResult.ReplacedSession, accepted.Result);
        Assert.Equal("acct-1", accepted.AccountStableId);
        Assert.NotNull(accepted.ExistingSessionId);
        Assert.NotNull(accepted.ResultingSessionId);
        log.Clear();

        await RunWelcomeScenario(log, accountBound: false, stopped: false);
        var unbound = Assert.Single(log.Events.OfType<GameplaySessionWelcomeProcessedDiagnosticEvent>());
        Assert.Equal(GameplayWelcomeProcessingResult.Unbound, unbound.Result);
        Assert.Null(unbound.AccountStableId);
        Assert.Equal("AccountBindingUnavailable", unbound.Reason);
    }

    [Fact]
    public async Task Stopped_context_welcome_records_current_ignored_outcome()
    {
        var log = new RecordingDiagnosticLog();

        await RunWelcomeScenario(log, accountBound: true, stopped: true);

        var ignored = Assert.Single(log.Events.OfType<GameplaySessionWelcomeProcessedDiagnosticEvent>());
        Assert.Equal(GameplayWelcomeProcessingResult.IgnoredStoppedContext, ignored.Result);
        Assert.Equal("MonitoringContextStopped", ignored.Reason);
    }

    [Fact]
    public async Task Throwing_logger_does_not_change_worker_routing_or_welcome_processing()
    {
        using var directory = new ParserTestDirectory();
        var path = directory.CreateFile();
        var source = ParserTestSnapshots.Source(path);
        var contextId = MonitoringContextId.CreateNew();
        var monitoring = new FakeMonitoringSessionManager();
        monitoring.SetInitial(ParserTestSnapshots.Snapshot(
            1,
            ParserTestSnapshots.Context(contextId, MonitoringContextState.Ready, source, 1, MonitoringSourceTransitionKind.SourceAssigned)));
        await using var parser = CreateParser(monitoring, new ThrowingDiagnosticLog());
        var classified = new ConcurrentQueue<ParserEvent>();
        parser.ClassifiedEventsAvailable += (_, args) =>
        {
            foreach (var parserEvent in args.Events)
            {
                classified.Enqueue(parserEvent);
            }
        };

        var startException = await Record.ExceptionAsync(() => parser.StartAsync());
        directory.Append(path, "2026-08-04 06:27:10 Welcome to City of Heroes, Example Hero!\r\n");
        await ParserTestSnapshots.WaitUntilAsync(() => classified.Any());

        Assert.Null(startException);
        Assert.Single(parser.Current.Workers);
        Assert.Single(classified);
        Assert.Equal(contextId, classified.Single().ContextId);
        await RunWelcomeScenario(new ThrowingDiagnosticLog(), accountBound: true, stopped: false);
    }

    private static ParserManager CreateParser(
        FakeMonitoringSessionManager monitoring,
        IDiagnosticLog log) =>
        new(
            monitoring,
            ParserTestSnapshots.FastOptions(),
            diagnosticLog: log,
            runningClientsProvider: () => [],
            logActivitySnapshotProvider: () => LogActivitySnapshot.Empty);

    private static async Task RunWelcomeScenario(
        IDiagnosticLog log,
        bool accountBound,
        bool stopped)
    {
        var monitoring = new FakeMonitoringSessionManager();
        var parser = new GameplaySessionTestInfrastructure.FakeGameplayParserManager();
        var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dataDirectory);
        try
        {
            var contextId = MonitoringContextId.CreateNew();
            var source = GameplaySessionTestInfrastructure.DefaultSource();
            var context = GameplaySessionTestInfrastructure.ReadyContext(contextId, source) with
            {
                State = stopped ? MonitoringContextState.Stopped : MonitoringContextState.Ready,
                AccountStableId = accountBound ? "acct-1" : null,
                AccountDisplayName = accountBound ? "Private Account" : null
            };
            monitoring.SetInitial(ParserTestSnapshots.Snapshot(1, context));
            using var manager = new GameplaySessionManager(
                monitoring,
                parser,
                repository,
                diagnosticLog: log);
            await manager.StartAsync();

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 06:27:10 Welcome to City of Heroes, Example Hero!",
                    contextId,
                    source,
                    sequence: 7)
            ]);
            await GameplaySessionTestInfrastructure.WaitForWorkQueueToDrainAsync(manager);
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

    private static int IndexOf<TEvent>(IReadOnlyList<DiagnosticEvent> events)
        where TEvent : DiagnosticEvent
    {
        for (var index = 0; index < events.Count; index++)
        {
            if (events[index] is TEvent)
            {
                return index;
            }
        }

        return -1;
    }
}
