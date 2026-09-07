using System.Diagnostics;
using CoHAnalytics.Models;
using CoHAnalytics.Services;

namespace CoHAnalytics.Replay;

public sealed class ReplayPerformanceCollector
{
    private const string MeasurementNote =
        "Measurements include Replay harness and observer overhead.";

    private readonly ReplayPerformanceOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly IReplayResourceMetricSource? _resourceSampler;
    private readonly List<ReplayPerformanceSample> _samples = [];
    private readonly QueueThresholdTracker _parserEventQueueTracker;
    private readonly QueueThresholdTracker _gameplayWorkQueueTracker;
    private readonly ResourceAggregateTracker _resourceAggregates = new();

    private long _collectionStartTimestamp;
    private long _lastSampleTimestamp;
    private long _writeStartTimestamp;
    private long _writeEndTimestamp;
    private long _drainStartTimestamp;
    private long _drainEndTimestamp;
    private long _parserCompleteTimestamp;
    private long _gameplayCompleteTimestamp;
    private long _oracleFinalizedTimestamp;
    private long _sampleCount;
    private long _droppedSampleCount;
    private TimeSpan _totalSamplingDuration;
    private TimeSpan _maxSingleSampleDuration;
    private bool _started;
    private bool _samplingStarted;
    private bool _samplingCompleted;
    private bool _completed;
    private bool _collectorFaulted;
    private string? _collectorFaultMessage;
    private bool _writeCompleted;
    private bool _drainStarted;
    private bool _drainCompleted;
    private ReplayLedger? _ledger;
    private ReplayAggregatedLedger? _aggregatedLedger;
    private ReplayConcurrentWriteThroughput? _concurrentWriteThroughput;
    private int _parserMonitoringPeakDepth;
    private int _parserEventPeakDepth;
    private int _gameplayWorkPeakDepth;
    private int _pendingCommittedPeakCount;

    public ReplayPerformanceCollector(ReplayPerformanceOptions options, IReplayResourceMetricSource? resourceSampler = null)
    {
        _options = options;
        _timeProvider = options.TimeProvider;
        _resourceSampler = options.EnableResourceSampling ? resourceSampler ?? new ReplayResourceSampler() : null;
        _parserEventQueueTracker = new QueueThresholdTracker(
            "parser",
            options.QueueThresholdPercents);
        _gameplayWorkQueueTracker = new QueueThresholdTracker(
            "gameplay",
            options.QueueThresholdPercents);
    }

    public bool IsStarted => _started;

    public bool IsCompleted => _completed;

    public void Start(ReplayTimeline timeline)
    {
        if (_started)
        {
            return;
        }

        _started = true;
        _collectionStartTimestamp = _timeProvider.GetTimestamp();
        _lastSampleTimestamp = _collectionStartTimestamp;
        timeline.Record(
            ReplayTimelineCategory.Analytics,
            "performance.started",
            ReplayTimelineRetentionClass.Milestone);
    }

    public void BeginWrite()
    {
        EnsureStarted();
        _writeStartTimestamp = _timeProvider.GetTimestamp();
    }

    public void EndWrite(
        ReplayLedger ledger,
        ReplayAggregatedLedger? aggregatedLedger = null,
        ReplayConcurrentWriteThroughput? concurrentWriteThroughput = null)
    {
        EnsureStarted();
        _ledger = ledger;
        _aggregatedLedger = aggregatedLedger;
        _concurrentWriteThroughput = concurrentWriteThroughput;
        _writeEndTimestamp = _timeProvider.GetTimestamp();
        _writeCompleted = true;
    }

    public void BeginDrain()
    {
        EnsureStarted();
        _drainStartTimestamp = _timeProvider.GetTimestamp();
        _drainStarted = true;
    }

    public void ObserveDuringDrain(
        Func<(ParserManagerDiagnostics Parser, GameplaySessionDiagnostics Gameplay)> readDiagnostics,
        ReplayTimeline timeline,
        CancellationToken cancellationToken)
    {
        EnsureStarted();
        cancellationToken.ThrowIfCancellationRequested();

        var (parserDiagnostics, gameplayDiagnostics) = readDiagnostics();
        ObserveQueueStates(parserDiagnostics, gameplayDiagnostics, timeline);

        if (!_options.EnableSampling || !_drainStarted || _samplingCompleted)
        {
            return;
        }

        var elapsedSinceLastSample = _timeProvider.GetElapsedTime(_lastSampleTimestamp);
        if (elapsedSinceLastSample < _options.SampleInterval)
        {
            return;
        }

        if (!_samplingStarted)
        {
            timeline.Record(
                ReplayTimelineCategory.Analytics,
                "performance.sampling.started",
                ReplayTimelineRetentionClass.Milestone);
            _samplingStarted = true;
        }

        SampleNow(readDiagnostics, timeline, elapsedSinceLastSample);
    }

    public void EndDrain(ReplayDrainResult drainResult, ReplayTimeline timeline)
    {
        EnsureStarted();
        _drainEndTimestamp = _timeProvider.GetTimestamp();
        _drainCompleted = true;

        if (drainResult.Outcome == ReplayDrainOutcome.Completed)
        {
            _parserCompleteTimestamp = _drainEndTimestamp;
            _gameplayCompleteTimestamp = _drainEndTimestamp;
        }

        if (_options.EnableSampling && !_samplingCompleted)
        {
            if (!_samplingStarted)
            {
                timeline.Record(
                    ReplayTimelineCategory.Analytics,
                    "performance.sampling.started",
                    ReplayTimelineRetentionClass.Milestone);
                _samplingStarted = true;
            }

            timeline.Record(
                ReplayTimelineCategory.Analytics,
                "performance.sampling.completed",
                ReplayTimelineRetentionClass.Milestone);
            _samplingCompleted = true;
        }
    }

    public void RecordOracleFinalized()
    {
        EnsureStarted();
        _oracleFinalizedTimestamp = _timeProvider.GetTimestamp();
    }

    public void RecordCollectorFault(string message)
    {
        _collectorFaulted = true;
        _collectorFaultMessage = message;
    }

    public ReplayPerformanceObservationReport Complete(
        ReplayCorrectnessReport correctness,
        ReplayDrainResult drainResult,
        ReplayLedger ledger,
        ParserManagerDiagnostics parserDiagnostics,
        GameplaySessionDiagnostics gameplayDiagnostics,
        ParserManagerSnapshot parserSnapshot,
        ReplayTimeline timeline,
        ReplayProfileObservationReport? profileReport = null,
        IReadOnlyList<ReplayContextSummaryReport>? contextSummaries = null,
        ReplayLatencyAggregateReport? latencyReport = null)
    {
        EnsureStarted();
        if (_completed)
        {
            throw new InvalidOperationException("Replay performance collector already completed.");
        }

        _completed = true;
        _ledger ??= ledger;
        ObserveQueueStates(parserDiagnostics, gameplayDiagnostics, timeline, finalize: true);

        if (_options.EnableSampling && !_samplingCompleted)
        {
            timeline.Record(
                ReplayTimelineCategory.Analytics,
                "performance.sampling.completed",
                ReplayTimelineRetentionClass.Milestone);
            _samplingCompleted = true;
        }

        timeline.Record(
            ReplayTimelineCategory.Analytics,
            "performance.completed",
            ReplayTimelineRetentionClass.Milestone);

        var runDuration = ToMilliseconds(_timeProvider.GetElapsedTime(_collectionStartTimestamp));
        var writeDuration = _concurrentWriteThroughput?.AggregateWriteDurationMilliseconds
            ?? (_writeCompleted
                ? ToMilliseconds(_timeProvider.GetElapsedTime(_writeStartTimestamp, _writeEndTimestamp))
                : (long?)null);
        var drainDuration = _drainCompleted
            ? ToMilliseconds(_timeProvider.GetElapsedTime(_drainStartTimestamp, _drainEndTimestamp))
            : (long?)null;

        var parserDrainDuration = drainDuration;
        var gameplayDrainDuration = drainDuration;

        var parserCompletionElapsed = _parserCompleteTimestamp > 0
            ? ToMilliseconds(_timeProvider.GetElapsedTime(_collectionStartTimestamp, _parserCompleteTimestamp))
            : (long?)null;
        var gameplayCompletionElapsed = _gameplayCompleteTimestamp > 0
            ? ToMilliseconds(_timeProvider.GetElapsedTime(_collectionStartTimestamp, _gameplayCompleteTimestamp))
            : (long?)null;

        var finalByteToParserComplete = _writeCompleted && _parserCompleteTimestamp > 0
            ? ToMilliseconds(_timeProvider.GetElapsedTime(_writeEndTimestamp, _parserCompleteTimestamp))
            : (long?)null;
        var parserToGameplay = _parserCompleteTimestamp > 0 && _gameplayCompleteTimestamp > 0
            ? ToMilliseconds(_timeProvider.GetElapsedTime(_parserCompleteTimestamp, _gameplayCompleteTimestamp))
            : (long?)null;
        var gameplayToOracle = _gameplayCompleteTimestamp > 0 && _oracleFinalizedTimestamp > 0
            ? ToMilliseconds(_timeProvider.GetElapsedTime(_gameplayCompleteTimestamp, _oracleFinalizedTimestamp))
            : (long?)null;

        var sourceLines = _aggregatedLedger?.OriginalSourceCompleteLines ?? ledger.OriginalSourceCompleteLines;
        var sourceBytes = _aggregatedLedger?.OriginalSourceBytes ?? ledger.OriginalSourceBytes;
        var achievedLinesPerSecond = _concurrentWriteThroughput?.AggregateLinesPerSecond
            ?? CalculateRate(sourceLines, writeDuration);
        var achievedBytesPerSecond = _concurrentWriteThroughput?.AggregateBytesPerSecond
            ?? CalculateRate(sourceBytes, writeDuration);
        var parserEventsPerSecond = CalculateRate(
            correctness.ClassifiedEventsObserved,
            parserDrainDuration);
        var gameplayEventsPerSecond = CalculateRate(
            gameplayDiagnostics.TotalCommittedEvents,
            gameplayDrainDuration);

        var validity = DetermineValidity(correctness, drainResult);
        var parserMonitoringSummary = BuildQueueSummary(
            parserDiagnostics.MonitoringSnapshotQueue,
            _parserMonitoringPeakDepth,
            thresholdsCrossed: []);
        var parserEventSummary = _parserEventQueueTracker.BuildSummary(parserDiagnostics.EventQueue);
        var gameplayWorkSummary = _gameplayWorkQueueTracker.BuildSummary(gameplayDiagnostics.WorkQueue);

        return new ReplayPerformanceObservationReport
        {
            Validity = validity,
            RunDurationMilliseconds = runDuration,
            WriteDurationMilliseconds = writeDuration,
            DrainDurationMilliseconds = drainDuration,
            FinalByteToParserCompleteMilliseconds = finalByteToParserComplete,
            ParserCompleteToGameplayCompleteMilliseconds = parserToGameplay,
            GameplayCompleteToOracleFinalizedMilliseconds = gameplayToOracle,
            SourceBytes = sourceBytes,
            SourceCompleteLines = sourceLines,
            ReplayedBytes = _aggregatedLedger?.CombinedDestinationBytes ?? ledger.CombinedDestinationBytes,
            ReplayedCompleteLines = _aggregatedLedger?.CombinedDestinationCompleteLines ?? ledger.CombinedDestinationCompleteLines,
            BootstrapBytes = _aggregatedLedger?.BootstrapBytes ?? ledger.BootstrapBytes,
            BootstrapCompleteLines = _aggregatedLedger?.BootstrapCompleteLines ?? ledger.BootstrapCompleteLines,
            AchievedLinesPerSecond = achievedLinesPerSecond,
            AchievedBytesPerSecond = achievedBytesPerSecond,
            ParserMonitoringSnapshotQueue = parserMonitoringSummary,
            ParserEventQueue = parserEventSummary,
            GameplayWorkQueue = gameplayWorkSummary,
            PendingCommittedEvents = new ReplayPendingCommittedEventSummary
            {
                Capacity = gameplayDiagnostics.PendingCommittedEvents.Capacity,
                CurrentCount = gameplayDiagnostics.PendingCommittedEvents.CurrentCount,
                PeakCount = Math.Max(
                    gameplayDiagnostics.PendingCommittedEvents.PeakCount,
                    _pendingCommittedPeakCount),
                DiscardedCount = gameplayDiagnostics.PendingCommittedEvents.DiscardedCount
            },
            ParserTotalLinesProcessed = parserSnapshot.TotalLinesProcessed,
            ParserRawEventsObserved = correctness.RawEventsObserved,
            ParserClassifiedEventsObserved = correctness.ClassifiedEventsObserved,
            ParserWorkerCount = parserSnapshot.WorkerCount,
            ParserDrainDurationMilliseconds = parserDrainDuration,
            ParserCompletionElapsedMilliseconds = parserCompletionElapsed,
            GameplayAcceptedWorkWatermark = gameplayDiagnostics.LastAcceptedWorkSequence,
            GameplayCompletedWorkWatermark = gameplayDiagnostics.LastCompletedWorkSequence,
            GameplayActiveProcessorCallbackCount = gameplayDiagnostics.ActiveProcessorCallbackCount,
            GameplayCommittedEventCount = gameplayDiagnostics.TotalCommittedEvents,
            GameplayOverloadState = gameplayDiagnostics.WorkQueueOverflowed,
            GameplayDiscardedCommittedEvents = gameplayDiagnostics.PendingCommittedEvents.DiscardedCount,
            GameplayAbandonedWorkCount = gameplayDiagnostics.WorkQueue.AbandonedCount,
            GameplayDrainDurationMilliseconds = gameplayDrainDuration,
            GameplayCompletionElapsedMilliseconds = gameplayCompletionElapsed,
            CorrectnessPassed = correctness.Passed,
            DrainOutcome = drainResult.Outcome,
            Throughput = new ReplayThroughputSummary
            {
                SourceLinesPerSecond = achievedLinesPerSecond,
                SourceBytesPerSecond = achievedBytesPerSecond,
                ParserEventsPerSecond = parserEventsPerSecond,
                GameplayEventsPerSecond = gameplayEventsPerSecond
            },
            Resources = _resourceAggregates.HasData ? _resourceAggregates.BuildSummary() : null,
            CollectorOverhead = new ReplayCollectorOverheadSummary
            {
                SampleCount = _sampleCount,
                DroppedSampleCount = _droppedSampleCount,
                SampleIntervalMilliseconds = (long)_options.SampleInterval.TotalMilliseconds,
                TotalSamplingDurationMilliseconds = _totalSamplingDuration.TotalMilliseconds,
                MaxSingleSampleDurationMilliseconds = _maxSingleSampleDuration.TotalMilliseconds,
                MeasurementNote = MeasurementNote
            },
            Profile = profileReport,
            ContextSummaries = contextSummaries,
            Latency = latencyReport,
            RequestedLinesPerSecond = profileReport?.RequestedRateLinesPerSecond ?? ledger.Pacing.RequestedLinesPerSecond,
            PacingUnderrunCount = ledger.Pacing.UnderrunCount,
            PacingOverrunMilliseconds = ledger.Pacing.OverrunMilliseconds,
            ConcurrentWriteThroughput = _concurrentWriteThroughput
        };
    }

    private void SampleNow(
        Func<(ParserManagerDiagnostics Parser, GameplaySessionDiagnostics Gameplay)> readDiagnostics,
        ReplayTimeline timeline,
        TimeSpan intervalElapsed)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var (parserDiagnostics, gameplayDiagnostics) = readDiagnostics();
            ObserveQueueStates(parserDiagnostics, gameplayDiagnostics, timeline);

            ReplayResourceSample? resource = null;
            if (_resourceSampler is not null)
            {
                resource = _resourceSampler.Capture(intervalElapsed);
                _resourceAggregates.Observe(resource);
            }

            var sample = new ReplayPerformanceSample(
                ToMilliseconds(_timeProvider.GetElapsedTime(_collectionStartTimestamp)),
                parserDiagnostics.EventQueue.CurrentDepth,
                parserDiagnostics.MonitoringSnapshotQueue.CurrentDepth,
                gameplayDiagnostics.WorkQueue.CurrentDepth,
                resource);
            AddSample(sample);
            _sampleCount++;
            _lastSampleTimestamp = _timeProvider.GetTimestamp();
        }
        catch (Exception exception)
        {
            RecordCollectorFault(exception.GetType().Name);
        }
        finally
        {
            stopwatch.Stop();
            _totalSamplingDuration += stopwatch.Elapsed;
            if (stopwatch.Elapsed > _maxSingleSampleDuration)
            {
                _maxSingleSampleDuration = stopwatch.Elapsed;
            }
        }
    }

    private void ObserveQueueStates(
        ParserManagerDiagnostics parserDiagnostics,
        GameplaySessionDiagnostics gameplayDiagnostics,
        ReplayTimeline timeline,
        bool finalize = false)
    {
        _parserMonitoringPeakDepth = Math.Max(
            _parserMonitoringPeakDepth,
            parserDiagnostics.MonitoringSnapshotQueue.PeakDepth);
        _parserEventPeakDepth = Math.Max(_parserEventPeakDepth, parserDiagnostics.EventQueue.PeakDepth);
        _gameplayWorkPeakDepth = Math.Max(_gameplayWorkPeakDepth, gameplayDiagnostics.WorkQueue.PeakDepth);
        _pendingCommittedPeakCount = Math.Max(
            _pendingCommittedPeakCount,
            gameplayDiagnostics.PendingCommittedEvents.PeakCount);

        _parserEventQueueTracker.Observe(parserDiagnostics.EventQueue, timeline, finalize);
        _gameplayWorkQueueTracker.Observe(gameplayDiagnostics.WorkQueue, timeline, finalize);

        if (finalize)
        {
            if (parserDiagnostics.EventQueue.IsDrained)
            {
                timeline.Record(
                    ReplayTimelineCategory.Drain,
                    "parser.drained",
                    ReplayTimelineRetentionClass.Milestone);
            }

            if (gameplayDiagnostics.WorkQueue.IsDrained)
            {
                timeline.Record(
                    ReplayTimelineCategory.Drain,
                    "gameplay.drained",
                    ReplayTimelineRetentionClass.Milestone);
            }
        }
    }

    private void AddSample(ReplayPerformanceSample sample)
    {
        if (_options.MaxRetainedSamples <= 0)
        {
            return;
        }

        if (_samples.Count >= _options.MaxRetainedSamples)
        {
            _samples.RemoveAt(0);
            _droppedSampleCount++;
        }

        _samples.Add(sample);
    }

    private ReplayPerformanceValidity DetermineValidity(
        ReplayCorrectnessReport correctness,
        ReplayDrainResult drainResult)
    {
        if (_collectorFaulted)
        {
            return ReplayPerformanceValidity.InvalidCollector;
        }

        if (!_started || !_writeCompleted || !_drainCompleted)
        {
            return ReplayPerformanceValidity.Incomplete;
        }

        if (!correctness.Passed)
        {
            return ReplayPerformanceValidity.InvalidCorrectness;
        }

        if (drainResult.Outcome != ReplayDrainOutcome.Completed)
        {
            return ReplayPerformanceValidity.InvalidDrain;
        }

        if (_options.EnableSampling && (!_samplingStarted || !_samplingCompleted))
        {
            return ReplayPerformanceValidity.Incomplete;
        }

        return ReplayPerformanceValidity.Valid;
    }

    private static ReplayQueuePressureSummary BuildQueueSummary(
        QueuePressureDiagnostics queue,
        int observedPeakDepth,
        IReadOnlyList<int> thresholdsCrossed)
    {
        var peakDepth = Math.Max(queue.PeakDepth, observedPeakDepth);
        double? peakFraction = queue.Capacity > 0
            ? peakDepth / (double)queue.Capacity
            : null;

        return new ReplayQueuePressureSummary
        {
            LifecycleEpoch = queue.LifecycleEpoch,
            Capacity = queue.Capacity,
            AcceptedCount = queue.AcceptedCount,
            CompletedCount = queue.CompletedCount,
            RejectedCount = queue.RejectedCount,
            AbandonedCount = queue.AbandonedCount,
            CurrentDepth = queue.CurrentDepth,
            PeakDepth = queue.PeakDepth,
            InFlightCount = queue.InFlightCount,
            Overflowed = queue.Overflowed,
            IsDrained = queue.IsDrained,
            PeakAbsoluteDepth = peakDepth,
            PeakDepthFraction = peakFraction,
            BacklogObserved = peakDepth > 0,
            BacklogCleared = peakDepth > 0 && queue.IsDrained,
            ThresholdsCrossed = thresholdsCrossed
        };
    }

    private static double? CalculateRate(long numerator, long? durationMilliseconds)
    {
        if (durationMilliseconds is null or <= 0 || numerator < 0)
        {
            return null;
        }

        var seconds = durationMilliseconds.Value / 1000.0;
        if (seconds <= 0)
        {
            return null;
        }

        return numerator / seconds;
    }

    private long ToMilliseconds(TimeSpan duration) =>
        duration <= TimeSpan.Zero ? 0 : (long)duration.TotalMilliseconds;

    private void EnsureStarted()
    {
        if (!_started)
        {
            throw new InvalidOperationException("Replay performance collector has not started.");
        }
    }

    private sealed class QueueThresholdTracker
    {
        private readonly string _prefix;
        private readonly IReadOnlyList<int> _thresholdPercents;
        private readonly HashSet<int> _crossedThresholds = [];
        private long _lastEpoch = -1;
        private bool _backlogObserved;
        private bool _backlogCleared;
        private int _peakAbsoluteDepth;

        public QueueThresholdTracker(string prefix, IReadOnlyList<int> thresholdPercents)
        {
            _prefix = prefix;
            _thresholdPercents = thresholdPercents;
        }

        public void Observe(QueuePressureDiagnostics queue, ReplayTimeline timeline, bool finalize)
        {
            if (_lastEpoch != queue.LifecycleEpoch)
            {
                _lastEpoch = queue.LifecycleEpoch;
                _crossedThresholds.Clear();
                _backlogObserved = false;
                _backlogCleared = false;
                _peakAbsoluteDepth = 0;
            }

            _peakAbsoluteDepth = Math.Max(_peakAbsoluteDepth, queue.PeakDepth);
            if (queue.CurrentDepth > 0 || queue.InFlightCount > 0 || queue.PeakDepth > 0)
            {
                _backlogObserved = true;
            }

            if (queue.Capacity > 0)
            {
                var depth = Math.Max(queue.CurrentDepth, queue.PeakDepth);
                var percent = (int)Math.Floor(depth * 100.0 / queue.Capacity);
                foreach (var threshold in _thresholdPercents)
                {
                    if (percent >= threshold && _crossedThresholds.Add(threshold))
                    {
                        var code = threshold >= 100
                            ? $"{_prefix}.queue.full"
                            : $"{_prefix}.queue.{threshold}pct";
                        timeline.Record(
                            ReplayTimelineCategory.Pressure,
                            code,
                            ReplayTimelineRetentionClass.Milestone,
                            value: threshold,
                            unit: "percent");
                    }
                }
            }

            if (_backlogObserved && queue.CurrentDepth == 0 && queue.InFlightCount == 0 && !_backlogCleared)
            {
                _backlogCleared = true;
                timeline.Record(
                    ReplayTimelineCategory.Pressure,
                    $"{_prefix}.backlog.cleared",
                    ReplayTimelineRetentionClass.Milestone);
            }

            if (finalize && queue.IsDrained && _backlogObserved && !_backlogCleared)
            {
                _backlogCleared = true;
                timeline.Record(
                    ReplayTimelineCategory.Pressure,
                    $"{_prefix}.backlog.cleared",
                    ReplayTimelineRetentionClass.Milestone);
            }
        }

        public ReplayQueuePressureSummary BuildSummary(QueuePressureDiagnostics queue)
        {
            var peakDepth = Math.Max(queue.PeakDepth, _peakAbsoluteDepth);
            double? peakFraction = queue.Capacity > 0
                ? peakDepth / (double)queue.Capacity
                : null;

            return new ReplayQueuePressureSummary
            {
                LifecycleEpoch = queue.LifecycleEpoch,
                Capacity = queue.Capacity,
                AcceptedCount = queue.AcceptedCount,
                CompletedCount = queue.CompletedCount,
                RejectedCount = queue.RejectedCount,
                AbandonedCount = queue.AbandonedCount,
                CurrentDepth = queue.CurrentDepth,
                PeakDepth = queue.PeakDepth,
                InFlightCount = queue.InFlightCount,
                Overflowed = queue.Overflowed,
                IsDrained = queue.IsDrained,
                PeakAbsoluteDepth = peakDepth,
                PeakDepthFraction = peakFraction,
                BacklogObserved = _backlogObserved,
                BacklogCleared = _backlogCleared,
                ThresholdsCrossed = _crossedThresholds.OrderBy(value => value).ToArray()
            };
        }
    }

    private sealed class ResourceAggregateTracker
    {
        private bool _hasData;
        private double? _cpuFirst;
        private double? _cpuMin;
        private double? _cpuMax;
        private double? _cpuLatest;
        private long? _workingSetFirst;
        private long? _workingSetMin;
        private long? _workingSetMax;
        private long? _workingSetLatest;
        private long? _privateMemoryFirst;
        private long? _privateMemoryMin;
        private long? _privateMemoryMax;
        private long? _privateMemoryLatest;
        private long? _managedHeapFirst;
        private long? _managedHeapMin;
        private long? _managedHeapMax;
        private long? _managedHeapLatest;
        private long? _allocatedFirst;
        private long? _allocatedMin;
        private long? _allocatedMax;
        private long? _allocatedLatest;
        private int? _gen0First;
        private int? _gen0Latest;
        private int? _gen1First;
        private int? _gen1Latest;
        private int? _gen2First;
        private int? _gen2Latest;
        private int? _threadFirst;
        private int? _threadMin;
        private int? _threadMax;
        private int? _threadLatest;
        private int? _handleFirst;
        private int? _handleMin;
        private int? _handleMax;
        private int? _handleLatest;
        private bool _handleCountSupported;

        public bool HasData => _hasData;

        public void Observe(ReplayResourceSample sample)
        {
            _hasData = true;
            ObserveMetric(ref _cpuFirst, ref _cpuLatest, ref _cpuMin, ref _cpuMax, sample.CpuUtilizationPercent);
            ObserveMetric(ref _workingSetFirst, ref _workingSetLatest, ref _workingSetMin, ref _workingSetMax, sample.WorkingSetBytes);
            ObserveMetric(ref _privateMemoryFirst, ref _privateMemoryLatest, ref _privateMemoryMin, ref _privateMemoryMax, sample.PrivateMemoryBytes);
            ObserveMetric(ref _managedHeapFirst, ref _managedHeapLatest, ref _managedHeapMin, ref _managedHeapMax, sample.ManagedHeapBytes);
            ObserveMetric(ref _allocatedFirst, ref _allocatedLatest, ref _allocatedMin, ref _allocatedMax, sample.TotalAllocatedBytes);
            ObserveCounter(ref _gen0First, ref _gen0Latest, sample.Gen0CollectionCount);
            ObserveCounter(ref _gen1First, ref _gen1Latest, sample.Gen1CollectionCount);
            ObserveCounter(ref _gen2First, ref _gen2Latest, sample.Gen2CollectionCount);
            ObserveMetric(ref _threadFirst, ref _threadLatest, ref _threadMin, ref _threadMax, sample.ThreadCount);
            ObserveMetric(ref _handleFirst, ref _handleLatest, ref _handleMin, ref _handleMax, sample.HandleCount);
            _handleCountSupported = sample.HandleCountSupported;
        }

        public ReplayResourceMetricSummary BuildSummary() =>
            new()
            {
                CpuUtilizationPercentFirst = _cpuFirst,
                CpuUtilizationPercentMin = _cpuMin,
                CpuUtilizationPercentMax = _cpuMax,
                CpuUtilizationPercentLatest = _cpuLatest,
                WorkingSetBytesFirst = _workingSetFirst,
                WorkingSetBytesMin = _workingSetMin,
                WorkingSetBytesMax = _workingSetMax,
                WorkingSetBytesLatest = _workingSetLatest,
                PrivateMemoryBytesFirst = _privateMemoryFirst,
                PrivateMemoryBytesMin = _privateMemoryMin,
                PrivateMemoryBytesMax = _privateMemoryMax,
                PrivateMemoryBytesLatest = _privateMemoryLatest,
                ManagedHeapBytesFirst = _managedHeapFirst,
                ManagedHeapBytesMin = _managedHeapMin,
                ManagedHeapBytesMax = _managedHeapMax,
                ManagedHeapBytesLatest = _managedHeapLatest,
                TotalAllocatedBytesFirst = _allocatedFirst,
                TotalAllocatedBytesMin = _allocatedMin,
                TotalAllocatedBytesMax = _allocatedMax,
                TotalAllocatedBytesLatest = _allocatedLatest,
                Gen0CollectionCountFirst = _gen0First,
                Gen0CollectionCountLatest = _gen0Latest,
                Gen1CollectionCountFirst = _gen1First,
                Gen1CollectionCountLatest = _gen1Latest,
                Gen2CollectionCountFirst = _gen2First,
                Gen2CollectionCountLatest = _gen2Latest,
                ThreadCountFirst = _threadFirst,
                ThreadCountMin = _threadMin,
                ThreadCountMax = _threadMax,
                ThreadCountLatest = _threadLatest,
                HandleCountFirst = _handleFirst,
                HandleCountMin = _handleMin,
                HandleCountMax = _handleMax,
                HandleCountLatest = _handleLatest,
                HandleCountSupported = _handleCountSupported
            };

        private static void ObserveMetric<T>(
            ref T? first,
            ref T? latest,
            ref T? min,
            ref T? max,
            T? value)
            where T : struct, IComparable<T>
        {
            if (value is null)
            {
                return;
            }

            first ??= value;
            latest = value;
            min = min is null || value.Value.CompareTo(min.Value) < 0 ? value : min;
            max = max is null || value.Value.CompareTo(max.Value) > 0 ? value : max;
        }

        private static void ObserveCounter(ref int? first, ref int? latest, int? value)
        {
            if (value is null)
            {
                return;
            }

            first ??= value;
            latest = value;
        }
    }
}
