using System.Collections.Concurrent;
using System.Threading.Channels;
using CoHAnalytics.Models;
using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Services;

public sealed class ParserManagerTests
{
    [Fact]
    public async Task Creates_one_worker_per_bound_context_and_none_for_unbound_context()
    {
        using var directory = new ParserTestDirectory();
        var firstPath = directory.CreateFile("first.txt");
        var secondPath = directory.CreateFile("second.txt");
        var firstId = MonitoringContextId.CreateNew();
        var secondId = MonitoringContextId.CreateNew();
        var unboundId = MonitoringContextId.CreateNew();
        var monitoring = new FakeMonitoringSessionManager();
        monitoring.SetInitial(ParserTestSnapshots.Snapshot(
            1,
            ParserTestSnapshots.Context(firstId, MonitoringContextState.Ready, ParserTestSnapshots.Source(firstPath), 1, MonitoringSourceTransitionKind.SourceAssigned),
            ParserTestSnapshots.Context(secondId, MonitoringContextState.Ready, ParserTestSnapshots.Source(secondPath, "acct-2"), 1, MonitoringSourceTransitionKind.SourceAssigned),
            ParserTestSnapshots.Context(unboundId, MonitoringContextState.WaitingForSource, null, 0, MonitoringSourceTransitionKind.None)));
        await using var parser = new ParserManager(monitoring, ParserTestSnapshots.FastOptions());

        await parser.StartAsync();

        Assert.Equal(2, parser.Current.WorkerCount);
        Assert.Equal(2, parser.Current.WaitingCount);
        Assert.Equal(
            parser.Current.Workers.OrderBy(worker => worker.ContextId.ToString()).Select(worker => worker.ContextId),
            parser.Current.Workers.Select(worker => worker.ContextId));
        Assert.DoesNotContain(parser.Current.Workers, worker => worker.ContextId == unboundId);
        var diagnostics = parser.GetDiagnostics();
        Assert.All(diagnostics.Workers, worker => Assert.DoesNotContain(directory.Root, worker.SourceFileName ?? string.Empty));
        Assert.All(diagnostics.RecentDecisions, decision => Assert.DoesNotContain(directory.Root, decision));
    }

    [Fact]
    public async Task Two_contexts_read_independently_and_events_remain_context_tagged()
    {
        using var directory = new ParserTestDirectory();
        var firstPath = directory.CreateFile("first.txt");
        var secondPath = directory.CreateFile("second.txt");
        var firstSource = ParserTestSnapshots.Source(firstPath);
        var secondSource = ParserTestSnapshots.Source(secondPath, "acct-2");
        var firstId = MonitoringContextId.CreateNew();
        var secondId = MonitoringContextId.CreateNew();
        var monitoring = new FakeMonitoringSessionManager();
        monitoring.SetInitial(ParserTestSnapshots.Snapshot(
            1,
            ParserTestSnapshots.Context(firstId, MonitoringContextState.Ready, firstSource, 1, MonitoringSourceTransitionKind.SourceAssigned),
            ParserTestSnapshots.Context(secondId, MonitoringContextState.Ready, secondSource, 1, MonitoringSourceTransitionKind.SourceAssigned)));
        await using var parser = new ParserManager(monitoring, ParserTestSnapshots.FastOptions());
        var events = new ConcurrentQueue<ParserRawEvent>();
        var firstEventObserved = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var secondEventObserved = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        parser.EventsAvailable += (_, args) =>
        {
            foreach (var parserEvent in args.Events)
            {
                events.Enqueue(parserEvent);
                if (parserEvent.ContextId == firstId
                    && parserEvent.SourceId == firstSource
                    && parserEvent.RawLine == "alpha")
                {
                    firstEventObserved.TrySetResult();
                }

                if (parserEvent.ContextId == secondId
                    && parserEvent.SourceId == secondSource
                    && parserEvent.RawLine == "beta")
                {
                    secondEventObserved.TrySetResult();
                }
            }
        };

        await parser.StartAsync();
        directory.Append(firstPath, "alpha\r\n");
        directory.Append(secondPath, "beta\r\n");
        await Task.WhenAll(firstEventObserved.Task, secondEventObserved.Task);

        Assert.Contains(events, item => item.ContextId == firstId && item.SourceId == firstSource && item.RawLine == "alpha");
        Assert.Contains(events, item => item.ContextId == secondId && item.SourceId == secondSource && item.RawLine == "beta");
        Assert.All(events, item => Assert.Equal(1, item.Sequence));
    }

    [Fact]
    public async Task One_worker_fault_does_not_stop_another()
    {
        using var directory = new ParserTestDirectory();
        var badPath = directory.CreateFile("bad.txt");
        var goodPath = directory.CreateFile("good.txt");
        var badId = MonitoringContextId.CreateNew();
        var goodId = MonitoringContextId.CreateNew();
        var monitoring = new FakeMonitoringSessionManager();
        monitoring.SetInitial(ParserTestSnapshots.Snapshot(
            1,
            ParserTestSnapshots.Context(badId, MonitoringContextState.Ready, ParserTestSnapshots.Source(badPath), 1, MonitoringSourceTransitionKind.SourceAssigned),
            ParserTestSnapshots.Context(goodId, MonitoringContextState.Ready, ParserTestSnapshots.Source(goodPath, "acct-2"), 1, MonitoringSourceTransitionKind.SourceAssigned)));
        await using var parser = new ParserManager(monitoring, ParserTestSnapshots.FastOptions());

        await parser.StartAsync();
        directory.Append(badPath, [0xC3, 0x28, 0x0A]);
        directory.Append(goodPath, "healthy\r\n");
        await ParserTestSnapshots.WaitUntilAsync(() =>
            parser.Current.FaultedCount == 1 && parser.Current.TotalLinesProcessed == 1);
        await ParserTestSnapshots.WaitUntilAsync(() =>
            parser.Current.Workers.Single(worker => worker.ContextId == goodId).State == ParserWorkerState.WaitingForData);

        Assert.Equal(ParserWorkerState.Faulted, parser.Current.Workers.Single(worker => worker.ContextId == badId).State);
        Assert.Equal(ParserWorkerState.WaitingForData, parser.Current.Workers.Single(worker => worker.ContextId == goodId).State);
    }

    [Fact]
    public async Task Bounded_delivery_overflow_faults_visibly_and_stop_prevents_later_callbacks()
    {
        using var directory = new ParserTestDirectory();
        var path = directory.CreateFile();
        var contextId = MonitoringContextId.CreateNew();
        var monitoring = new FakeMonitoringSessionManager();
        monitoring.SetInitial(ParserTestSnapshots.Snapshot(
            1,
            ParserTestSnapshots.Context(contextId, MonitoringContextState.Ready, ParserTestSnapshots.Source(path), 1, MonitoringSourceTransitionKind.SourceAssigned)));
        var options = ParserTestSnapshots.FastOptions(eventQueueCapacity: 1, eventBatchSize: 1);
        await using var parser = new ParserManager(monitoring, options);
        using var subscriberEntered = new ManualResetEventSlim();
        using var releaseSubscriber = new ManualResetEventSlim();
        var callbackCount = 0;
        parser.EventsAvailable += (_, _) =>
        {
            Interlocked.Increment(ref callbackCount);
            subscriberEntered.Set();
            releaseSubscriber.Wait(TimeSpan.FromSeconds(2));
        };

        await parser.StartAsync();
        directory.Append(path, "one\ntwo\nthree\nfour\n");
        Assert.True(subscriberEntered.Wait(TimeSpan.FromSeconds(2)));
        await ParserTestSnapshots.WaitUntilAsync(() => parser.Current.FaultedCount == 1);
        Assert.True(parser.GetDiagnostics().EventQueueOverflowed);

        releaseSubscriber.Set();
        await parser.StopAsync();
        var afterStop = Volatile.Read(ref callbackCount);
        directory.Append(path, "after stop\n");
        await Task.Delay(50);
        Assert.Equal(afterStop, Volatile.Read(ref callbackCount));
    }

    [Fact]
    public async Task Queue_diagnostics_report_capacities_epoch_depth_and_drain_semantics()
    {
        using var directory = new ParserTestDirectory();
        var path = directory.CreateFile();
        var contextId = MonitoringContextId.CreateNew();
        var monitoring = new FakeMonitoringSessionManager();
        monitoring.SetInitial(ParserTestSnapshots.Snapshot(
            1,
            ParserTestSnapshots.Context(
                contextId,
                MonitoringContextState.Ready,
                ParserTestSnapshots.Source(path),
                1,
                MonitoringSourceTransitionKind.SourceAssigned)));
        var options = ParserTestSnapshots.FastOptions(eventQueueCapacity: 4, eventBatchSize: 1);
        await using var parser = new ParserManager(monitoring, options);

        await parser.StartAsync();

        var afterStart = parser.GetDiagnostics();
        Assert.Equal(1, afterStart.LifecycleEpoch);
        Assert.Equal(options.MonitoringSnapshotQueueCapacity, afterStart.MonitoringSnapshotQueue.Capacity);
        Assert.Equal(options.EventQueueCapacity, afterStart.EventQueue.Capacity);
        Assert.Equal(1, afterStart.MonitoringSnapshotQueue.AcceptedCount);
        Assert.True(afterStart.MonitoringSnapshotQueue.IsDrained);
        Assert.Equal(0, afterStart.QueuedEventCount);
        Assert.Equal(0, afterStart.EventQueue.CurrentDepth);

        directory.Append(path, "line one\nline two\n");
        await ParserTestSnapshots.WaitUntilAsync(() => parser.GetDiagnostics().EventQueue.CompletedCount >= 2);

        var duringWork = parser.GetDiagnostics();
        Assert.True(duringWork.EventQueue.PeakDepth >= 1);
        Assert.True(duringWork.EventQueue.AcceptedCount >= 2);
        Assert.Equal(duringWork.EventQueue.AcceptedCount, duringWork.EventQueue.CompletedCount);
        Assert.Equal(0, duringWork.QueuedEventCount);
        Assert.True(duringWork.EventQueue.IsDrained);
        Assert.All(duringWork.RecentDecisions, decision => Assert.DoesNotContain(directory.Root, decision));

        await parser.StopAsync();

        var afterStop = parser.GetDiagnostics();
        Assert.False(afterStop.IsRunning);
        Assert.True(afterStop.EventQueue.IsDrained);
        Assert.True(afterStop.MonitoringSnapshotQueue.IsDrained);

        await parser.StartAsync();
        var afterRestart = parser.GetDiagnostics();
        Assert.Equal(2, afterRestart.LifecycleEpoch);
        Assert.Equal(1, afterRestart.MonitoringSnapshotQueue.AcceptedCount);
        Assert.Equal(0, afterRestart.EventQueue.AcceptedCount);
        Assert.False(afterRestart.EventQueueOverflowed);
        Assert.False(afterRestart.MonitoringSnapshotQueueOverflowed);
    }

    [Fact]
    public async Task Event_queue_overflow_records_rejected_count_and_latches_overflow()
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
        var options = ParserTestSnapshots.FastOptions(eventQueueCapacity: 1, eventBatchSize: 1);
        await using var parser = new ParserManager(monitoring, options);
        var subscriberEntered = new TaskCompletionSource<ParserRawEvent>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSubscriber = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        parser.EventsAvailable += (_, args) =>
        {
            var matchingEvent = args.Events.SingleOrDefault(parserEvent =>
                parserEvent.ContextId == contextId
                && parserEvent.SourceId == source
                && parserEvent.Sequence == 1
                && parserEvent.RawLine == "one");
            if (matchingEvent is null)
            {
                return;
            }

            subscriberEntered.TrySetResult(matchingEvent);
            releaseSubscriber.Task.GetAwaiter().GetResult();
        };

        try
        {
            await parser.StartAsync();
            directory.Append(path, "one\n");
            var firstEvent = await subscriberEntered.Task;
            Assert.Equal(contextId, firstEvent.ContextId);
            Assert.Equal(source, firstEvent.SourceId);
            Assert.Equal(1, firstEvent.Sequence);
            Assert.Equal("one", firstEvent.RawLine);

            var baseline = parser.GetDiagnostics();
            Assert.Equal(1, baseline.EventQueue.AcceptedCount);
            Assert.Equal(0, baseline.EventQueue.RejectedCount);
            Assert.Equal(0, baseline.EventQueue.CompletedCount);
            Assert.Equal(0, baseline.EventQueue.AbandonedCount);
            Assert.Equal(0, baseline.EventQueue.CurrentDepth);
            Assert.Equal(1, baseline.EventQueue.InFlightCount);
            Assert.False(baseline.EventQueue.Overflowed);
            Assert.True(baseline.EventQueue.MatchesAccountingIdentity());

            var overflowObserved = new TaskCompletionSource<ParserManagerSnapshot>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            void ObserveOverflow(ParserManagerSnapshot snapshot)
            {
                if (snapshot.Workers.Any(worker =>
                        worker.ContextId == contextId
                        && worker.CurrentSourceId == source
                        && worker.LastEventSequence == 3
                        && worker.State == ParserWorkerState.Faulted
                        && worker.FaultCode == "event_queue_overflow"))
                {
                    overflowObserved.TrySetResult(snapshot);
                }
            }

            parser.StateChanged += (_, args) => ObserveOverflow(args.Snapshot);
            ObserveOverflow(parser.Current);
            directory.Append(path, "two\nthree\n");
            var overflowSnapshot = await overflowObserved.Task;

            var worker = Assert.Single(
                overflowSnapshot.Workers,
                candidate => candidate.ContextId == contextId);
            Assert.Equal(source, worker.CurrentSourceId);
            Assert.Equal(3, worker.LastEventSequence);
            Assert.Equal(ParserWorkerState.Faulted, worker.State);
            Assert.Equal("event_queue_overflow", worker.FaultCode);

            var diagnostics = parser.GetDiagnostics();
            Assert.True(diagnostics.EventQueue.Overflowed);
            Assert.True(diagnostics.EventQueueOverflowed);
            Assert.Equal(baseline.EventQueue.AcceptedCount + 1, diagnostics.EventQueue.AcceptedCount);
            Assert.Equal(baseline.EventQueue.RejectedCount + 1, diagnostics.EventQueue.RejectedCount);
            Assert.Equal(baseline.EventQueue.CompletedCount, diagnostics.EventQueue.CompletedCount);
            Assert.Equal(baseline.EventQueue.AbandonedCount, diagnostics.EventQueue.AbandonedCount);
            Assert.Equal(1, diagnostics.EventQueue.CurrentDepth);
            Assert.Equal(1, diagnostics.EventQueue.InFlightCount);
            Assert.True(diagnostics.EventQueue.MatchesAccountingIdentity());
            Assert.Equal(
                diagnostics.EventQueue.AcceptedCount,
                diagnostics.EventQueue.CompletedCount
                    + diagnostics.EventQueue.AbandonedCount
                    + diagnostics.EventQueue.CurrentDepth
                    + diagnostics.EventQueue.InFlightCount);
        }
        finally
        {
            releaseSubscriber.TrySetResult();
            await parser.StopAsync();
        }
    }

    [Fact]
    public void Queue_pressure_tracker_phantom_depth_when_accept_follows_dequeue_and_completion()
    {
        var tracker = new QueuePressureTracker(capacity: 4);
        tracker.Reset(lifecycleEpoch: 1);

        tracker.RecordDequeued();
        tracker.RecordCompleted();
        tracker.RecordAccepted();

        var snapshot = tracker.Snapshot();
        Assert.Equal(1, snapshot.CurrentDepth);
        Assert.Equal(0, snapshot.InFlightCount);
        Assert.Equal(1, snapshot.AcceptedCount);
        Assert.Equal(1, snapshot.CompletedCount);
        Assert.False(snapshot.IsDrained);
        Assert.Equal(QueuePressureInvariantClassification.PhantomDepth, snapshot.ClassifyInvariant());
    }

    [Fact]
    public void Queue_pressure_invariant_classification_covers_drained_queued_inflight_and_overflow()
    {
        var drained = QueuePressureDiagnostics.Idle(8, 1) with
        {
            AcceptedCount = 3,
            CompletedCount = 3
        };
        Assert.Equal(QueuePressureInvariantClassification.ConsistentDrained, drained.ClassifyInvariant());

        var queued = drained with
        {
            CurrentDepth = 2,
            AcceptedCount = 5,
            CompletedCount = 3
        };
        Assert.Equal(QueuePressureInvariantClassification.ConsistentQueued, queued.ClassifyInvariant());

        var inFlight = drained with
        {
            InFlightCount = 1,
            AcceptedCount = 4,
            CompletedCount = 3
        };
        Assert.Equal(QueuePressureInvariantClassification.ConsistentInFlight, inFlight.ClassifyInvariant());

        var overflowed = drained with { Overflowed = true, CurrentDepth = 1 };
        Assert.Equal(QueuePressureInvariantClassification.Overflowed, overflowed.ClassifyInvariant());
    }

    [Fact]
    public void TryPublish_rejected_write_cancels_admission_without_phantom_depth()
    {
        var channel = Channel.CreateBounded<int>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = true
        });
        var tracker = new QueuePressureTracker(capacity: 1);
        tracker.Reset(lifecycleEpoch: 1);

        Assert.True(QueuePressureAdmission.TryPublish(channel.Writer, 1, tracker));
        Assert.False(QueuePressureAdmission.TryPublish(channel.Writer, 2, tracker));

        var snapshot = tracker.Snapshot();
        Assert.Equal(1, snapshot.CurrentDepth);
        Assert.Equal(1, snapshot.AcceptedCount);
        Assert.NotEqual(QueuePressureInvariantClassification.PhantomDepth, snapshot.ClassifyInvariant());
        Assert.True(snapshot.MatchesAccountingIdentity());
    }

    [Fact]
    public async Task PublishAsync_cancelled_write_cancels_admission()
    {
        var channel = Channel.CreateBounded<int>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = true
        });
        var tracker = new QueuePressureTracker(capacity: 1);
        tracker.Reset(lifecycleEpoch: 1);
        Assert.True(QueuePressureAdmission.TryPublish(channel.Writer, 1, tracker));

        using var cancellation = new CancellationTokenSource();
        var publishTask = QueuePressureAdmission.PublishAsync(channel.Writer, 2, tracker, cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => publishTask);

        var snapshot = tracker.Snapshot();
        Assert.Equal(1, snapshot.CurrentDepth);
        Assert.Equal(1, snapshot.AcceptedCount);
        Assert.True(snapshot.MatchesAccountingIdentity());
    }

    [Fact]
    public async Task Parser_raw_event_queue_maintains_accounting_identity_with_immediate_subscriber()
    {
        using var directory = new ParserTestDirectory();
        var path = directory.CreateFile();
        var contextId = MonitoringContextId.CreateNew();
        var monitoring = new FakeMonitoringSessionManager();
        monitoring.SetInitial(ParserTestSnapshots.Snapshot(
            1,
            ParserTestSnapshots.Context(
                contextId,
                MonitoringContextState.Ready,
                ParserTestSnapshots.Source(path),
                1,
                MonitoringSourceTransitionKind.SourceAssigned)));
        await using var parser = new ParserManager(
            monitoring,
            ParserTestSnapshots.FastOptions(eventBatchSize: 1, eventQueueCapacity: 8));

        await parser.StartAsync();
        directory.Append(path, "one\ntwo\nthree\nfour\n");
        await ParserTestSnapshots.WaitUntilAsync(() => parser.GetDiagnostics().EventQueue.IsDrained);

        var eventQueue = parser.GetDiagnostics().EventQueue;
        Assert.True(eventQueue.IsDrained);
        Assert.Equal(QueuePressureInvariantClassification.ConsistentDrained, eventQueue.ClassifyInvariant());
        Assert.True(eventQueue.MatchesAccountingIdentity());
        await parser.StopAsync();
    }

    [Fact]
    public async Task Parser_monitoring_snapshot_queue_maintains_accounting_identity_after_transitions()
    {
        using var directory = new ParserTestDirectory();
        var path = directory.CreateFile();
        var contextId = MonitoringContextId.CreateNew();
        var source = ParserTestSnapshots.Source(path);
        var monitoring = new FakeMonitoringSessionManager();
        monitoring.SetInitial(ParserTestSnapshots.Snapshot(
            1,
            ParserTestSnapshots.Context(contextId, MonitoringContextState.Ready, source, 1, MonitoringSourceTransitionKind.SourceAssigned)));
        await using var parser = new ParserManager(monitoring, ParserTestSnapshots.FastOptions());

        await parser.StartAsync();
        monitoring.Publish(ParserTestSnapshots.Snapshot(
            2,
            ParserTestSnapshots.Context(contextId, MonitoringContextState.WaitingForSource, null, 2, MonitoringSourceTransitionKind.SourceReleased, source)));
        monitoring.Publish(ParserTestSnapshots.Snapshot(
            3,
            ParserTestSnapshots.Context(contextId, MonitoringContextState.Ready, source, 3, MonitoringSourceTransitionKind.SourceReclaimed, source)));
        await ParserTestSnapshots.WaitUntilAsync(() => parser.GetDiagnostics().MonitoringSnapshotQueue.IsDrained);

        var monitoringQueue = parser.GetDiagnostics().MonitoringSnapshotQueue;
        Assert.True(monitoringQueue.IsDrained);
        Assert.True(monitoringQueue.MatchesAccountingIdentity());
        await parser.StopAsync();
    }

    [Fact]
    public async Task Parser_shutdown_abandons_queued_raw_events_without_phantom_depth()
    {
        using var directory = new ParserTestDirectory();
        var path = directory.CreateFile();
        var contextId = MonitoringContextId.CreateNew();
        var monitoring = new FakeMonitoringSessionManager();
        monitoring.SetInitial(ParserTestSnapshots.Snapshot(
            1,
            ParserTestSnapshots.Context(
                contextId,
                MonitoringContextState.Ready,
                ParserTestSnapshots.Source(path),
                1,
                MonitoringSourceTransitionKind.SourceAssigned)));
        await using var parser = new ParserManager(
            monitoring,
            ParserTestSnapshots.FastOptions(eventBatchSize: 1, eventQueueCapacity: 1));
        using var subscriberEntered = new ManualResetEventSlim();
        using var releaseSubscriber = new ManualResetEventSlim();
        parser.EventsAvailable += (_, _) =>
        {
            subscriberEntered.Set();
            releaseSubscriber.Wait(TimeSpan.FromSeconds(2));
        };

        await parser.StartAsync();
        directory.Append(path, "one\ntwo\nthree\n");
        Assert.True(subscriberEntered.Wait(TimeSpan.FromSeconds(2)));
        await ParserTestSnapshots.WaitUntilAsync(
            () => !parser.GetDiagnostics().EventQueue.IsDrained);

        var stopTask = parser.StopAsync();
        releaseSubscriber.Set();
        await stopTask;

        var eventQueue = parser.GetDiagnostics().EventQueue;
        Assert.True(eventQueue.IsDrained);
        Assert.NotEqual(QueuePressureInvariantClassification.PhantomDepth, eventQueue.ClassifyInvariant());
        Assert.True(eventQueue.MatchesAccountingIdentity());
    }
}
