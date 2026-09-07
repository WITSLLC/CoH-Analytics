using CoHAnalytics.Models;
using CoHAnalytics.Replay;
using CoHAnalytics.Services;
using CoHAnalytics.Tests.Services;

namespace CoHAnalytics.Tests.Replay;

public sealed class ReplayPerformanceCollectorTests
{
    [Fact]
    public void Collector_starts_and_stops_once()
    {
        var time = new ManualReplayTimeProvider();
        var timeline = new ReplayTimeline(time);
        var collector = CreateCollector(time, maxSamples: 4, sampleIntervalMs: 10, enableSampling: true);

        collector.Start(timeline);
        Assert.True(collector.IsStarted);
        Assert.False(collector.IsCompleted);

        var report = CompleteCollector(collector, timeline, time, correctnessPassed: true);
        Assert.True(collector.IsCompleted);
        Assert.Throws<InvalidOperationException>(() => CompleteCollector(collector, timeline, time));
        Assert.Contains(timeline.Events, entry => entry.Code == "performance.started");
        Assert.Contains(timeline.Events, entry => entry.Code == "performance.completed");
        Assert.Equal(ReplayPerformanceValidity.Valid, report.Validity);
    }

    [Fact]
    public void Samples_are_monotonic_and_bounded_with_dropped_sample_accounting()
    {
        var time = new ManualReplayTimeProvider();
        var timeline = new ReplayTimeline(time);
        var collector = CreateCollector(time, maxSamples: 2, sampleIntervalMs: 5, enableSampling: true);
        collector.Start(timeline);
        collector.BeginWrite();
        collector.EndWrite(CreateLedger());
        collector.BeginDrain();

        var depth = 0;
        for (var index = 0; index < 5; index++)
        {
            depth = index + 1;
            time.Advance(TimeSpan.FromMilliseconds(6));
            collector.ObserveDuringDrain(() => CreateDiagnostics(eventDepth: depth), timeline, CancellationToken.None);
        }

        collector.EndDrain(
            new ReplayDrainResult(ReplayDrainOutcome.Completed, CreateDrainDiagnostics()),
            timeline);
        var report = CompleteCollector(collector, timeline, time);

        Assert.True(report.CollectorOverhead.SampleCount >= 3);
        Assert.True(report.CollectorOverhead.DroppedSampleCount >= 1);
        Assert.Equal(2, report.CollectorOverhead.SampleCount - report.CollectorOverhead.DroppedSampleCount);
    }

    [Fact]
    public void Queue_peaks_preserved_after_historical_samples_are_discarded()
    {
        var time = new ManualReplayTimeProvider();
        var timeline = new ReplayTimeline(time);
        var collector = CreateCollector(time, maxSamples: 1, sampleIntervalMs: 1, enableSampling: true);
        collector.Start(timeline);
        collector.BeginWrite();
        collector.EndWrite(CreateLedger());
        collector.BeginDrain();

        time.Advance(TimeSpan.FromMilliseconds(2));
        collector.ObserveDuringDrain(() => CreateDiagnostics(eventDepth: 9), timeline, CancellationToken.None);
        time.Advance(TimeSpan.FromMilliseconds(2));
        collector.ObserveDuringDrain(() => CreateDiagnostics(eventDepth: 1), timeline, CancellationToken.None);

        collector.EndDrain(
            new ReplayDrainResult(ReplayDrainOutcome.Completed, CreateDrainDiagnostics()),
            timeline);
        var report = CompleteCollector(collector, timeline, time);

        Assert.Equal(9, report.ParserEventQueue.PeakAbsoluteDepth);
        Assert.True(report.CollectorOverhead.DroppedSampleCount >= 1);
    }

    [Fact]
    public void Threshold_timeline_events_emit_once_per_epoch()
    {
        var time = new ManualReplayTimeProvider();
        var timeline = new ReplayTimeline(time);
        var collector = CreateCollector(time, maxSamples: 16, sampleIntervalMs: 1, enableSampling: true);
        collector.Start(timeline);
        collector.BeginDrain();

        for (var index = 0; index < 3; index++)
        {
            time.Advance(TimeSpan.FromMilliseconds(2));
            collector.ObserveDuringDrain(() => CreateDiagnostics(eventDepth: 256), timeline, CancellationToken.None);
        }

        Assert.Equal(1, timeline.Events.Count(entry => entry.Code == "parser.queue.25pct"));
        Assert.Equal(1, timeline.Events.Count(entry => entry.Code == "parser.queue.full"));
    }

    [Fact]
    public void Backlog_cleared_event_emits_after_nonzero_depth_returns_to_zero()
    {
        var time = new ManualReplayTimeProvider();
        var timeline = new ReplayTimeline(time);
        var collector = CreateCollector(time, maxSamples: 8, sampleIntervalMs: 1, enableSampling: true);
        collector.Start(timeline);
        collector.BeginDrain();

        time.Advance(TimeSpan.FromMilliseconds(2));
        collector.ObserveDuringDrain(() => CreateDiagnostics(eventDepth: 4), timeline, CancellationToken.None);
        time.Advance(TimeSpan.FromMilliseconds(2));
        collector.ObserveDuringDrain(() => CreateDiagnostics(eventDepth: 0), timeline, CancellationToken.None);

        collector.EndDrain(
            new ReplayDrainResult(ReplayDrainOutcome.Completed, CreateDrainDiagnostics()),
            timeline);
        CompleteCollector(collector, timeline, time);

        Assert.Contains(timeline.Events, entry => entry.Code == "parser.backlog.cleared");
    }

    [Fact]
    public void Bootstrap_is_excluded_from_source_throughput()
    {
        var time = new ManualReplayTimeProvider();
        var timeline = new ReplayTimeline(time);
        var collector = CreateCollector(time);
        collector.Start(timeline);
        collector.BeginWrite();
        time.Advance(TimeSpan.FromMilliseconds(100));
        var ledger = new ReplayLedger();
        ledger.RecordSourceAnalysis(1, 1000, 10, false, 0);
        ledger.RecordBootstrap(1, 500, 1);
        collector.EndWrite(ledger);
        collector.BeginDrain();
        collector.EndDrain(
            new ReplayDrainResult(ReplayDrainOutcome.Completed, CreateDrainDiagnostics()),
            timeline);

        var report = CompleteCollector(
            collector,
            timeline,
            time,
            ledger: ledger,
            classifiedEvents: 10,
            committedEvents: 10,
            lifecycleAlreadyConfigured: true);
        Assert.Equal(100.0, report.AchievedLinesPerSecond);
        Assert.Equal(10000.0, report.AchievedBytesPerSecond);
        Assert.Equal(500, report.BootstrapBytes);
        Assert.Equal(1000, report.SourceBytes);
    }

    [Fact]
    public void Zero_duration_guards_return_null_throughput()
    {
        var time = new ManualReplayTimeProvider();
        var timeline = new ReplayTimeline(time);
        var collector = CreateCollector(time);
        collector.Start(timeline);
        collector.BeginWrite();
        collector.EndWrite(CreateLedger());
        collector.BeginDrain();
        collector.EndDrain(
            new ReplayDrainResult(ReplayDrainOutcome.Completed, CreateDrainDiagnostics()),
            timeline);

        var report = CompleteCollector(collector, timeline, time);
        Assert.Null(report.Throughput.SourceLinesPerSecond);
        Assert.Null(report.Throughput.ParserEventsPerSecond);
    }

    [Fact]
    public void Correctness_failure_marks_performance_invalid()
    {
        var time = new ManualReplayTimeProvider();
        var timeline = new ReplayTimeline(time);
        var collector = CreateCollector(time);
        collector.Start(timeline);
        collector.BeginWrite();
        collector.EndWrite(CreateLedger());
        collector.BeginDrain();
        collector.EndDrain(
            new ReplayDrainResult(ReplayDrainOutcome.Completed, CreateDrainDiagnostics()),
            timeline);

        var report = CompleteCollector(collector, timeline, time, correctnessPassed: false);
        Assert.Equal(ReplayPerformanceValidity.InvalidCorrectness, report.Validity);
    }

    [Fact]
    public void Drain_timeout_marks_performance_invalid()
    {
        var time = new ManualReplayTimeProvider();
        var timeline = new ReplayTimeline(time);
        var collector = CreateCollector(time);
        collector.Start(timeline);
        collector.BeginWrite();
        collector.EndWrite(CreateLedger());
        collector.BeginDrain();
        collector.EndDrain(
            new ReplayDrainResult(ReplayDrainOutcome.TimedOut, CreateDrainDiagnostics()),
            timeline);

        var report = CompleteCollector(
            collector,
            timeline,
            time,
            drainOutcome: ReplayDrainOutcome.TimedOut,
            lifecycleAlreadyConfigured: true);
        Assert.Equal(ReplayPerformanceValidity.InvalidDrain, report.Validity);
    }

    [Fact]
    public void Collector_fault_marks_performance_invalid()
    {
        var time = new ManualReplayTimeProvider();
        var timeline = new ReplayTimeline(time);
        var collector = CreateCollector(time);
        collector.Start(timeline);
        collector.RecordCollectorFault("sample-fault");
        collector.BeginWrite();
        collector.EndWrite(CreateLedger());
        collector.BeginDrain();
        collector.EndDrain(
            new ReplayDrainResult(ReplayDrainOutcome.Completed, CreateDrainDiagnostics()),
            timeline);

        var report = CompleteCollector(collector, timeline, time);
        Assert.Equal(ReplayPerformanceValidity.InvalidCollector, report.Validity);
    }

    private static ReplayPerformanceCollector CreateCollector(
        ManualReplayTimeProvider time,
        int maxSamples = 8,
        int sampleIntervalMs = 10,
        IReplayResourceMetricSource? resourceSampler = null,
        bool enableSampling = false) =>
        new(
            new ReplayPerformanceOptions
            {
                TimeProvider = time,
                MaxRetainedSamples = maxSamples,
                SampleInterval = TimeSpan.FromMilliseconds(sampleIntervalMs),
                EnableSampling = enableSampling,
                EnableResourceSampling = resourceSampler is not null
            },
            resourceSampler);

    private static ReplayPerformanceObservationReport CompleteCollector(
        ReplayPerformanceCollector collector,
        ReplayTimeline timeline,
        ManualReplayTimeProvider time,
        bool correctnessPassed = true,
        long classifiedEvents = 4,
        long committedEvents = 4,
        ReplayLedger? ledger = null,
        ReplayDrainOutcome drainOutcome = ReplayDrainOutcome.Completed,
        bool lifecycleAlreadyConfigured = false)
    {
        if (!lifecycleAlreadyConfigured)
        {
            collector.BeginWrite();
            collector.EndWrite(ledger ?? CreateLedger());
            collector.BeginDrain();
            collector.EndDrain(
                new ReplayDrainResult(drainOutcome, CreateDrainDiagnostics()),
                timeline);
        }

        time.Advance(TimeSpan.FromMilliseconds(1));
        collector.RecordOracleFinalized();
        ledger ??= CreateLedger();
        return collector.Complete(
            new ReplayCorrectnessReport
            {
                Passed = correctnessPassed,
                RawEventsObserved = classifiedEvents,
                ClassifiedEventsObserved = classifiedEvents,
                CommittedGameplayEventsObserved = committedEvents,
                ContextCount = 1,
                WelcomeBoundariesObserved = 1
            },
            new ReplayDrainResult(drainOutcome, CreateDrainDiagnostics()),
            ledger,
            CreateDiagnostics().Parser,
            CreateDiagnostics().Gameplay,
            ParserManagerSnapshot.Empty,
            timeline);
    }

    private static ReplayLedger CreateLedger()
    {
        var ledger = new ReplayLedger();
        ledger.RecordSourceAnalysis(1, 100, 4, false, 0);
        return ledger;
    }

    private static ReplayDrainDiagnostics CreateDrainDiagnostics() =>
        new(
            MonitoringReady: true,
            ProcessedLines: 4,
            ExpectedLines: 4,
            ParserQueuedEvents: 0,
            ParserWorkersReady: true,
            RawObserved: 4,
            ClassifiedObserved: 4,
            CommittedObserved: 4,
            GameplayQuiescent: true,
            PendingCommittedEvents: 0,
            FailedContextCount: 0,
            ParserOverflowed: false,
            GameplayOverloaded: false,
            UnsatisfiedStages: []);

    private static (ParserManagerDiagnostics Parser, GameplaySessionDiagnostics Gameplay) CreateDiagnostics(
        int eventDepth = 0,
        int gameplayDepth = 0)
    {
        var eventQueue = QueuePressureDiagnostics.Idle(256, 1) with
        {
            CurrentDepth = eventDepth,
            PeakDepth = eventDepth,
            AcceptedCount = eventDepth
        };
        var parser = GameplaySessionTestInfrastructure.IdleParserDiagnostics() with
        {
            MonitoringSnapshotQueue = QueuePressureDiagnostics.Idle(64, 1),
            EventQueue = eventQueue
        };
        var gameplay = GameplaySessionTestInfrastructure.IdleGameplayDiagnostics(
            lastAcceptedWorkSequence: 4,
            lastCompletedWorkSequence: 4);
        gameplay = new GameplaySessionDiagnostics
        {
            IsRunning = gameplay.IsRunning,
            SnapshotRevision = gameplay.SnapshotRevision,
            LifecycleEpoch = gameplay.LifecycleEpoch,
            ActiveSessionCount = gameplay.ActiveSessionCount,
            SuspendedSessionCount = gameplay.SuspendedSessionCount,
            NeedsAttentionSessionCount = gameplay.NeedsAttentionSessionCount,
            OverflowedSessionCount = gameplay.OverflowedSessionCount,
            TotalCommittedEvents = gameplay.TotalCommittedEvents,
            LastCommittedEventAt = gameplay.LastCommittedEventAt,
            FailedContextCount = gameplay.FailedContextCount,
            PendingCommittedEventCount = gameplay.PendingCommittedEventCount,
            WorkQueue = QueuePressureDiagnostics.Idle(512, 1) with
            {
                CurrentDepth = gameplayDepth,
                PeakDepth = gameplayDepth
            },
            PendingCommittedEvents = gameplay.PendingCommittedEvents,
            WorkQueueOverflowed = gameplay.WorkQueueOverflowed,
            PreStartBufferOverflowed = gameplay.PreStartBufferOverflowed,
            LastAcceptedWorkSequence = gameplay.LastAcceptedWorkSequence,
            LastCompletedWorkSequence = gameplay.LastCompletedWorkSequence,
            ActiveProcessorCallbackCount = gameplay.ActiveProcessorCallbackCount,
            RecentOperations = gameplay.RecentOperations
        };
        return (parser, gameplay);
    }
}
