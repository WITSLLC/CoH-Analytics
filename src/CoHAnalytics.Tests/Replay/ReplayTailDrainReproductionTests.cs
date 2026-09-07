using System.Text;
using System.Threading.Channels;
using CoHAnalytics.Models;
using CoHAnalytics.Replay;
using CoHAnalytics.Services;
using CoHAnalytics.Tests.Services;

namespace CoHAnalytics.Tests.Replay;

/// <summary>
/// Deterministic reproduction for Replay 1C.5 high-volume 2× drain.timeout where
/// parser(processed=N/expected=N,queued=1) and composite gameplay-quiescent=false.
/// </summary>
[Collection(nameof(ReplayPipelineCollection))]
public sealed class ReplayTailDrainReproductionTests
{
    private const int Replay15CProcessedLines = 8081;
    private const int Replay15CQueuedAtTimeout = 1;

    [Fact]
    public async Task Parser_tail_raw_event_in_dispatch_queue_matches_replay_1c5_failure_signature()
    {
        var evidence = await CaptureTailQueueEvidenceAsync(totalLines: 4);

        Assert.Equal(1, evidence.Parser.QueuedEventCount);
        Assert.Equal(1, evidence.Parser.EventQueue.CurrentDepth);
        Assert.Equal(
            evidence.Parser.EventQueue.AcceptedCount,
            evidence.Parser.EventQueue.CompletedCount
            + evidence.Parser.EventQueue.InFlightCount
            + evidence.Parser.EventQueue.CurrentDepth);
        Assert.Equal(1, evidence.Parser.EventQueue.InFlightCount);
        Assert.True(evidence.Parser.EventQueue.AcceptedCount > evidence.Parser.EventQueue.CompletedCount);
        Assert.Equal(
            QueuePressureInvariantClassification.ConsistentInFlight,
            evidence.Parser.EventQueue.ClassifyInvariant());
        Assert.True(evidence.Parser.TotalLinesProcessed >= 4);
        Assert.False(evidence.WouldReplayDrainComplete);
        Assert.False(evidence.CompositeGameplayQuiescent);
        Assert.Contains("parser(", evidence.UnsatisfiedStages, StringComparison.Ordinal);
        Assert.Contains("queued=1", evidence.UnsatisfiedStages, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Gameplay_trails_parser_when_final_raw_event_remains_queued()
    {
        var evidence = await CaptureTailQueueEvidenceAsync(
            totalLines: 6,
            includeGameplay: true);

        Assert.Equal(1, evidence.Parser.QueuedEventCount);
        Assert.True(evidence.Parser.TotalLinesProcessed >= 6);
        Assert.NotNull(evidence.Gameplay);
        Assert.True(evidence.Parser.ClassifiedObserved < evidence.Parser.TotalLinesProcessed);
        Assert.False(evidence.WouldReplayDrainComplete);
    }

    [Fact]
    public void Replay_1c5_timeout_message_queued_one_is_parser_event_queue_current_depth()
    {
        // ReplayPipeline.BuildDrainDiagnostics reports:
        //   queued={parserDiagnostics.QueuedEventCount}
        // ParserManager.GetDiagnostics sets:
        //   QueuedEventCount = eventQueue.CurrentDepth
        // where eventQueue is the bounded ParserRawEvent dispatch channel tracker.
        var idle = QueuePressureDiagnostics.Idle(256, lifecycleEpoch: 1);
        var tailQueued = idle with
        {
            AcceptedCount = Replay15CProcessedLines,
            CompletedCount = Replay15CProcessedLines - Replay15CQueuedAtTimeout,
            CurrentDepth = Replay15CQueuedAtTimeout,
            PeakDepth = 87
        };

        Assert.Equal(Replay15CQueuedAtTimeout, tailQueued.CurrentDepth);
        Assert.False(tailQueued.IsDrained);
        Assert.NotEqual(
            tailQueued.AcceptedCount,
            tailQueued.CompletedCount + tailQueued.AbandonedCount);
    }

    [Fact]
    public void Phantom_backlog_accounting_matches_dequeue_before_accept_interleaving()
    {
        var tracker = new QueuePressureTracker(capacity: 4);
        tracker.Reset(lifecycleEpoch: 1);
        tracker.RecordDequeued();
        tracker.RecordCompleted();
        tracker.RecordAccepted();

        var snapshot = tracker.Snapshot();
        Assert.Equal(1, snapshot.CurrentDepth);
        Assert.Equal(0, snapshot.InFlightCount);
        Assert.Equal(snapshot.AcceptedCount, snapshot.CompletedCount);
        Assert.Equal(QueuePressureInvariantClassification.PhantomDepth, snapshot.ClassifyInvariant());
        Assert.False(snapshot.IsDrained);
    }

    [Fact]
    public void Admission_before_publish_prevents_phantom_depth_for_vulnerable_interleaving()
    {
        var channel = Channel.CreateBounded<int>(new BoundedChannelOptions(1)
        {
            SingleReader = true,
            SingleWriter = true
        });
        var tracker = new QueuePressureTracker(capacity: 1);
        tracker.Reset(lifecycleEpoch: 1);

        Assert.True(QueuePressureAdmission.TryPublish(channel.Writer, 42, tracker));
        Assert.True(channel.Reader.TryRead(out _));
        tracker.RecordDequeued();
        tracker.RecordCompleted();

        var snapshot = tracker.Snapshot();
        Assert.Equal(0, snapshot.CurrentDepth);
        Assert.Equal(0, snapshot.InFlightCount);
        Assert.Equal(snapshot.AcceptedCount, snapshot.CompletedCount);
        Assert.Equal(QueuePressureInvariantClassification.ConsistentDrained, snapshot.ClassifyInvariant());
        Assert.True(snapshot.IsDrained);
        Assert.True(snapshot.MatchesAccountingIdentity());
    }

    [Fact]
    public void Admission_before_publish_is_stable_across_200_iterations()
    {
        for (var iteration = 0; iteration < 200; iteration++)
        {
            var channel = Channel.CreateBounded<int>(new BoundedChannelOptions(1)
            {
                SingleReader = true,
                SingleWriter = true
            });
            var tracker = new QueuePressureTracker(capacity: 1);
            tracker.Reset(lifecycleEpoch: 1);

            Assert.True(QueuePressureAdmission.TryPublish(channel.Writer, iteration, tracker));
            Assert.True(channel.Reader.TryRead(out _));
            tracker.RecordDequeued();
            tracker.RecordCompleted();

            var snapshot = tracker.Snapshot();
            Assert.Equal(QueuePressureInvariantClassification.ConsistentDrained, snapshot.ClassifyInvariant());
            Assert.True(snapshot.IsDrained);
            Assert.True(snapshot.MatchesAccountingIdentity());
        }
    }

    [Fact]
    public async Task Blocked_subscriber_release_drains_to_consistent_state()
    {
        var blockedEvidence = await CaptureBlockedTailEvidenceAsync(totalLines: 4);
        Assert.Equal(1, blockedEvidence.Parser.EventQueue.CurrentDepth);
        Assert.Equal(1, blockedEvidence.Parser.EventQueue.InFlightCount);
        Assert.True(blockedEvidence.Parser.EventQueue.AcceptedCount > blockedEvidence.Parser.EventQueue.CompletedCount);
        Assert.Equal(
            QueuePressureInvariantClassification.ConsistentInFlight,
            blockedEvidence.Parser.EventQueue.ClassifyInvariant());
        Assert.False(blockedEvidence.WouldReplayDrainComplete);

        var postRelease = await CapturePostReleaseTailEvidenceAsync(totalLines: 4);
        Assert.Equal(0, postRelease.EventQueue.CurrentDepth);
        Assert.Equal(0, postRelease.EventQueue.InFlightCount);
        Assert.Equal(
            postRelease.EventQueue.AcceptedCount,
            postRelease.EventQueue.CompletedCount + postRelease.EventQueue.AbandonedCount);
        Assert.Equal(
            QueuePressureInvariantClassification.ConsistentDrained,
            postRelease.EventQueue.ClassifyInvariant());
        Assert.True(postRelease.EventQueue.IsDrained);
        Assert.True(postRelease.EventQueue.MatchesAccountingIdentity());
    }

    [Fact]
    public async Task Real_backlog_reproduction_is_stable_across_100_iterations()
    {
        for (var iteration = 0; iteration < 100; iteration++)
        {
            var evidence = await CaptureTailQueueEvidenceAsync(totalLines: 4);
            Assert.Equal(1, evidence.Parser.EventQueue.CurrentDepth);
            Assert.Equal(1, evidence.Parser.EventQueue.InFlightCount);
            Assert.True(evidence.Parser.EventQueue.AcceptedCount > evidence.Parser.EventQueue.CompletedCount);
            Assert.Equal(
                QueuePressureInvariantClassification.ConsistentInFlight,
                evidence.Parser.EventQueue.ClassifyInvariant());
        }
    }

    [Fact]
    public void Legacy_manual_accounting_order_remains_phantom_across_100_iterations()
    {
        for (var iteration = 0; iteration < 100; iteration++)
        {
            var tracker = new QueuePressureTracker(capacity: 4);
            tracker.Reset(lifecycleEpoch: 1);
            tracker.RecordDequeued();
            tracker.RecordCompleted();
            tracker.RecordAccepted();

            var snapshot = tracker.Snapshot();
            Assert.Equal(QueuePressureInvariantClassification.PhantomDepth, snapshot.ClassifyInvariant());
        }
    }

    [Fact]
    public async Task Tail_queue_reproduction_is_stable_across_100_iterations()
    {
        for (var iteration = 0; iteration < 100; iteration++)
        {
            var evidence = await CaptureTailQueueEvidenceAsync(totalLines: 4);
            Assert.Equal(1, evidence.Parser.QueuedEventCount);
            Assert.False(evidence.WouldReplayDrainComplete);
        }
    }

    [Fact]
    public async Task Existing_drain_lifecycle_tests_remain_stable_across_50_iterations()
    {
        for (var iteration = 0; iteration < 50; iteration++)
        {
            await RunDrainTimeoutHarnessAsync();
            await RunLifecycleTimeoutHarnessAsync();
        }
    }

    private static async Task<TailDrainEvidence> CaptureTailQueueEvidenceAsync(
        int totalLines,
        bool includeGameplay = false)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(totalLines, 2);

        using var directory = new ParserTestDirectory();
        var path = directory.CreateFile();
        var contextId = MonitoringContextId.CreateNew();
        var source = ParserTestSnapshots.Source(path);
        var monitoring = new FakeMonitoringSessionManager();
        monitoring.SetInitial(ParserTestSnapshots.Snapshot(
            1,
            ParserTestSnapshots.Context(
                contextId,
                MonitoringContextState.Ready,
                source,
                1,
                MonitoringSourceTransitionKind.SourceAssigned)));

        var options = ParserTestSnapshots.FastOptions(eventBatchSize: 1, eventQueueCapacity: 128);
        await using var parser = new ParserManager(monitoring, options);

        GameplaySessionManager? gameplay = null;
        CharacterRepository? repository = null;
        string? repositoryDirectory = null;
        if (includeGameplay)
        {
            repository = GameplaySessionTestInfrastructure.CreateRepository(out repositoryDirectory!);
            gameplay = new GameplaySessionManager(monitoring, parser, repository);
            await gameplay.StartAsync();
        }

        using var penultimateHandlerEntered = new ManualResetEventSlim(false);
        using var releasePenultimateHandler = new ManualResetEventSlim(false);
        var handlerInvocations = 0;

        parser.EventsAvailable += (_, _) =>
        {
            var invocation = Interlocked.Increment(ref handlerInvocations);
            if (invocation == totalLines - 1)
            {
                penultimateHandlerEntered.Set();
                releasePenultimateHandler.Wait(TimeSpan.FromSeconds(2));
            }
        };

        await parser.StartAsync();

        var payload = new StringBuilder();
        for (var line = 0; line < totalLines; line++)
        {
            payload.Append("2026-08-04 18:20:07 synthetic line ");
            payload.Append(line);
            payload.Append("\r\n");
        }

        directory.Append(path, payload.ToString());

        Assert.True(penultimateHandlerEntered.Wait(TimeSpan.FromSeconds(2)));
        await ParserTestSnapshots.WaitUntilAsync(() => parser.GetDiagnostics().QueuedEventCount == 1);
        await ParserTestSnapshots.WaitUntilAsync(
            () => parser.Current.TotalLinesProcessed >= totalLines);

        var parserDiagnostics = parser.GetDiagnostics();
        var workerSnapshot = Assert.Single(parser.Current.Workers);
        GameplaySessionDiagnostics? gameplayDiagnostics = gameplay?.GetDiagnostics();

        var classifiedObserved = parserDiagnostics.Classification.TotalClassifiedLines;
        var gameplayQuiescent = gameplayDiagnostics?.IsQuiescent ?? false;
        var committedObserved = gameplayDiagnostics?.TotalCommittedEvents ?? 0;
        var wouldDrainComplete = EvaluateReplayDrainComplete(
            processedLines: parser.Current.TotalLinesProcessed,
            expectedLines: totalLines,
            queuedEventCount: parserDiagnostics.QueuedEventCount,
            workersReady: parser.Current.Workers.All(
                worker => worker.State is ParserWorkerState.WaitingForData or ParserWorkerState.Reading),
            classifiedObserved: classifiedObserved,
            gameplayIsQuiescent: gameplayQuiescent,
            committedObserved: committedObserved,
            totalCommitted: committedObserved);

        var compositeGameplayQuiescent = EvaluateCompositeGameplayQuiescent(
            processedLines: parser.Current.TotalLinesProcessed,
            expectedLines: totalLines,
            queuedEventCount: parserDiagnostics.QueuedEventCount,
            workersReady: true,
            classifiedObserved: classifiedObserved,
            gameplayIsQuiescent: gameplayQuiescent);

        var unsatisfied = BuildUnsatisfiedStages(
            processedLines: parser.Current.TotalLinesProcessed,
            expectedLines: totalLines,
            queuedEventCount: parserDiagnostics.QueuedEventCount,
            classifiedObserved: classifiedObserved,
            expectedClassified: totalLines,
            gameplayIsQuiescent: gameplayQuiescent,
            parserComplete: parser.Current.TotalLinesProcessed >= totalLines
                && parserDiagnostics.QueuedEventCount == 0,
            committedObserved: committedObserved,
            totalCommitted: committedObserved);

        var evidence = new TailDrainEvidence(
            Parser: new ParserTailEvidence(
                QueuedEventCount: parserDiagnostics.QueuedEventCount,
                EventQueue: parserDiagnostics.EventQueue,
                MonitoringSnapshotQueue: parserDiagnostics.MonitoringSnapshotQueue,
                WorkerLinesEmitted: workerSnapshot.TotalLinesEmitted,
                WorkerState: workerSnapshot.State,
                TotalLinesProcessed: parser.Current.TotalLinesProcessed,
                ClassifiedObserved: classifiedObserved,
                RawObserved: parserDiagnostics.Classification.TotalClassifiedLines),
            Gameplay: gameplayDiagnostics is null
                ? null
                : new GameplayTailEvidence(
                    LastAcceptedWorkSequence: gameplayDiagnostics.LastAcceptedWorkSequence,
                    LastCompletedWorkSequence: gameplayDiagnostics.LastCompletedWorkSequence,
                    ActiveProcessorCallbackCount: gameplayDiagnostics.ActiveProcessorCallbackCount,
                    PendingCommittedEventCount: gameplayDiagnostics.PendingCommittedEventCount,
                    WorkQueue: gameplayDiagnostics.WorkQueue,
                    PendingCommittedEvents: gameplayDiagnostics.PendingCommittedEvents,
                    TotalCommittedEvents: gameplayDiagnostics.TotalCommittedEvents,
                    IsQuiescent: gameplayDiagnostics.IsQuiescent,
                    ClassifiedObserved: classifiedObserved),
            WouldReplayDrainComplete: wouldDrainComplete,
            CompositeGameplayQuiescent: compositeGameplayQuiescent,
            UnsatisfiedStages: unsatisfied);

        releasePenultimateHandler.Set();
        await ParserTestSnapshots.WaitUntilAsync(() => parser.GetDiagnostics().EventQueue.IsDrained);

        if (gameplay is not null)
        {
            await gameplay.StopAsync();
            gameplay.Dispose();
        }

        if (repositoryDirectory is not null && Directory.Exists(repositoryDirectory))
        {
            Directory.Delete(repositoryDirectory, recursive: true);
        }

        return evidence;
    }

    private static async Task<(ParserTailEvidence Parser, bool WouldReplayDrainComplete)> CaptureBlockedTailEvidenceAsync(
        int totalLines)
    {
        using var directory = new ParserTestDirectory();
        var path = directory.CreateFile();
        var contextId = MonitoringContextId.CreateNew();
        var source = ParserTestSnapshots.Source(path);
        var monitoring = new FakeMonitoringSessionManager();
        monitoring.SetInitial(ParserTestSnapshots.Snapshot(
            1,
            ParserTestSnapshots.Context(
                contextId,
                MonitoringContextState.Ready,
                source,
                1,
                MonitoringSourceTransitionKind.SourceAssigned)));

        var options = ParserTestSnapshots.FastOptions(eventBatchSize: 1, eventQueueCapacity: 128);
        await using var parser = new ParserManager(monitoring, options);

        using var penultimateHandlerEntered = new ManualResetEventSlim(false);
        using var releasePenultimateHandler = new ManualResetEventSlim(false);
        var handlerInvocations = 0;

        parser.EventsAvailable += (_, _) =>
        {
            var invocation = Interlocked.Increment(ref handlerInvocations);
            if (invocation == totalLines - 1)
            {
                penultimateHandlerEntered.Set();
                releasePenultimateHandler.Wait(TimeSpan.FromSeconds(2));
            }
        };

        await parser.StartAsync();

        var payload = new StringBuilder();
        for (var line = 0; line < totalLines; line++)
        {
            payload.Append("2026-08-04 18:20:07 synthetic line ");
            payload.Append(line);
            payload.Append("\r\n");
        }

        directory.Append(path, payload.ToString());

        Assert.True(penultimateHandlerEntered.Wait(TimeSpan.FromSeconds(2)));
        await ParserTestSnapshots.WaitUntilAsync(() => parser.GetDiagnostics().QueuedEventCount == 1);

        var parserDiagnostics = parser.GetDiagnostics();
        var workerSnapshot = Assert.Single(parser.Current.Workers);
        var wouldDrainComplete = EvaluateReplayDrainComplete(
            processedLines: parser.Current.TotalLinesProcessed,
            expectedLines: totalLines,
            queuedEventCount: parserDiagnostics.QueuedEventCount,
            workersReady: parser.Current.Workers.All(
                worker => worker.State is ParserWorkerState.WaitingForData or ParserWorkerState.Reading),
            classifiedObserved: parserDiagnostics.Classification.TotalClassifiedLines,
            gameplayIsQuiescent: false,
            committedObserved: 0,
            totalCommitted: 0);

        var evidence = new ParserTailEvidence(
            QueuedEventCount: parserDiagnostics.QueuedEventCount,
            EventQueue: parserDiagnostics.EventQueue,
            MonitoringSnapshotQueue: parserDiagnostics.MonitoringSnapshotQueue,
            WorkerLinesEmitted: workerSnapshot.TotalLinesEmitted,
            WorkerState: workerSnapshot.State,
            TotalLinesProcessed: parser.Current.TotalLinesProcessed,
            ClassifiedObserved: parserDiagnostics.Classification.TotalClassifiedLines,
            RawObserved: parserDiagnostics.Classification.TotalClassifiedLines);

        releasePenultimateHandler.Set();
        await ParserTestSnapshots.WaitUntilAsync(() => parser.GetDiagnostics().EventQueue.IsDrained);
        await parser.StopAsync();

        return (evidence, wouldDrainComplete);
    }

    private static async Task<ParserTailEvidence> CapturePostReleaseTailEvidenceAsync(int totalLines)
    {
        using var directory = new ParserTestDirectory();
        var path = directory.CreateFile();
        var contextId = MonitoringContextId.CreateNew();
        var source = ParserTestSnapshots.Source(path);
        var monitoring = new FakeMonitoringSessionManager();
        monitoring.SetInitial(ParserTestSnapshots.Snapshot(
            1,
            ParserTestSnapshots.Context(
                contextId,
                MonitoringContextState.Ready,
                source,
                1,
                MonitoringSourceTransitionKind.SourceAssigned)));

        var options = ParserTestSnapshots.FastOptions(eventBatchSize: 1, eventQueueCapacity: 128);
        await using var parser = new ParserManager(monitoring, options);

        using var penultimateHandlerEntered = new ManualResetEventSlim(false);
        using var releasePenultimateHandler = new ManualResetEventSlim(false);
        var handlerInvocations = 0;

        parser.EventsAvailable += (_, _) =>
        {
            var invocation = Interlocked.Increment(ref handlerInvocations);
            if (invocation == totalLines - 1)
            {
                penultimateHandlerEntered.Set();
                releasePenultimateHandler.Wait(TimeSpan.FromSeconds(2));
            }
        };

        await parser.StartAsync();

        var payload = new StringBuilder();
        for (var line = 0; line < totalLines; line++)
        {
            payload.Append("2026-08-04 18:20:07 synthetic line ");
            payload.Append(line);
            payload.Append("\r\n");
        }

        directory.Append(path, payload.ToString());

        Assert.True(penultimateHandlerEntered.Wait(TimeSpan.FromSeconds(2)));
        await ParserTestSnapshots.WaitUntilAsync(() => parser.GetDiagnostics().QueuedEventCount == 1);
        releasePenultimateHandler.Set();
        await ParserTestSnapshots.WaitUntilAsync(() => parser.GetDiagnostics().EventQueue.IsDrained);

        var parserDiagnostics = parser.GetDiagnostics();
        var workerSnapshot = Assert.Single(parser.Current.Workers);
        await parser.StopAsync();

        return new ParserTailEvidence(
            QueuedEventCount: parserDiagnostics.QueuedEventCount,
            EventQueue: parserDiagnostics.EventQueue,
            MonitoringSnapshotQueue: parserDiagnostics.MonitoringSnapshotQueue,
            WorkerLinesEmitted: workerSnapshot.TotalLinesEmitted,
            WorkerState: workerSnapshot.State,
            TotalLinesProcessed: parser.Current.TotalLinesProcessed,
            ClassifiedObserved: parserDiagnostics.Classification.TotalClassifiedLines,
            RawObserved: parserDiagnostics.Classification.TotalClassifiedLines);
    }

    private static bool EvaluateReplayDrainComplete(
        long processedLines,
        long expectedLines,
        int queuedEventCount,
        bool workersReady,
        long classifiedObserved,
        bool gameplayIsQuiescent,
        long committedObserved,
        long totalCommitted)
    {
        var parserComplete = processedLines >= expectedLines
            && queuedEventCount == 0
            && workersReady;
        var classifiedCaughtUp = classifiedObserved >= expectedLines;
        var gameplayQuiescent = EvaluateCompositeGameplayQuiescent(
            processedLines,
            expectedLines,
            queuedEventCount,
            workersReady,
            classifiedObserved,
            gameplayIsQuiescent);
        var oracleObserved = committedObserved >= totalCommitted;

        return parserComplete && classifiedCaughtUp && gameplayQuiescent && oracleObserved;
    }

    private static bool EvaluateCompositeGameplayQuiescent(
        long processedLines,
        long expectedLines,
        int queuedEventCount,
        bool workersReady,
        long classifiedObserved,
        bool gameplayIsQuiescent)
    {
        var parserComplete = processedLines >= expectedLines
            && queuedEventCount == 0
            && workersReady;
        var classifiedCaughtUp = classifiedObserved >= expectedLines;

        return parserComplete && gameplayIsQuiescent && classifiedCaughtUp;
    }

    private static string BuildUnsatisfiedStages(
        long processedLines,
        long expectedLines,
        int queuedEventCount,
        long classifiedObserved,
        long expectedClassified,
        bool gameplayIsQuiescent,
        bool parserComplete,
        long committedObserved,
        long totalCommitted)
    {
        var unsatisfied = new List<string>();
        if (!parserComplete)
        {
            unsatisfied.Add(
                $"parser(processed={processedLines}/expected={expectedLines},queued={queuedEventCount})");
        }

        if (classifiedObserved < expectedClassified)
        {
            unsatisfied.Add(
                $"classified(observed={classifiedObserved}/expected={expectedClassified})");
        }

        if (parserComplete && !gameplayIsQuiescent)
        {
            unsatisfied.Add("gameplay(pending)");
        }

        if (committedObserved < totalCommitted)
        {
            unsatisfied.Add(
                $"oracle(committed={committedObserved}/expected={totalCommitted})");
        }

        return string.Join(", ", unsatisfied);
    }

    private static async Task RunDrainTimeoutHarnessAsync()
    {
        var replayTime = new ManualReplayTimeProvider();
        var sourcePath = ReplayTestPaths.Fixture("core-session.log");
        var plan = ReplayTestPlanFactory.CreatePlan(
            sourcePath,
            new DateOnly(2026, 1, 15),
            ReplayInputMode.Exact,
            ReplayChunkMode.WholeLine);
        var workspace = await ReplayWorkspace.CreateAsync(keepWorkspace: true);
        var ledger = new ReplayLedger();
        var timeline = new ReplayTimeline(replayTime);
        await using var pipeline = new ReplayPipeline(
            replayTime,
            characterDataDirectory: Path.Combine(Path.GetTempPath(), $"replay-timeout-{Guid.NewGuid():n}"),
            lifecycleTimeProvider: TimeProvider.System,
            lifecycleTimeouts: new ReplayLifecycleTimeouts { Drain = TimeSpan.FromTicks(-1) });

        var result = await pipeline.ExecuteAsync(
            new ReplayPipelineRequest { Contexts = [new ReplayContextBinding(workspace, plan)] },
            ledger,
            timeline);

        try
        {
            Assert.False(result.Success);
            Assert.Contains(result.Correctness.Failures, failure => failure.Code == "drain.timeout");
        }
        finally
        {
            Directory.Delete(workspace.RootPath, recursive: true);
        }
    }

    private static async Task RunLifecycleTimeoutHarnessAsync()
    {
        var time = new ManualReplayTimeProvider();
        var signal = new ReplayLifecycleSignal();
        var waiter = new ReplayLifecycleWaiter(time);
        var exception = await Assert.ThrowsAsync<ReplayLifecycleTimeoutException>(() =>
            waiter.WaitAsync(
                "parser drain",
                () => false,
                _ => Task.CompletedTask,
                () => "processed=2/3, queued=1",
                signal,
                TimeSpan.FromMilliseconds(5),
                CancellationToken.None));

        Assert.Equal("parser drain", exception.ConditionName);
        Assert.Contains("processed=2/3, queued=1", exception.Message, StringComparison.Ordinal);
    }

    private sealed record ParserTailEvidence(
        int QueuedEventCount,
        QueuePressureDiagnostics EventQueue,
        QueuePressureDiagnostics MonitoringSnapshotQueue,
        long WorkerLinesEmitted,
        ParserWorkerState WorkerState,
        long TotalLinesProcessed,
        long ClassifiedObserved,
        long RawObserved);

    private sealed record GameplayTailEvidence(
        long LastAcceptedWorkSequence,
        long LastCompletedWorkSequence,
        int ActiveProcessorCallbackCount,
        int PendingCommittedEventCount,
        QueuePressureDiagnostics WorkQueue,
        PendingCommittedEventDiagnostics PendingCommittedEvents,
        long TotalCommittedEvents,
        bool IsQuiescent,
        long ClassifiedObserved);

    private sealed record TailDrainEvidence(
        ParserTailEvidence Parser,
        GameplayTailEvidence? Gameplay,
        bool WouldReplayDrainComplete,
        bool CompositeGameplayQuiescent,
        string UnsatisfiedStages);
}
