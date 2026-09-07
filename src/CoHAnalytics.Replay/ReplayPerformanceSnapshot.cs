namespace CoHAnalytics.Replay;

public enum ReplayPerformanceValidity
{
    Valid,
    InvalidCorrectness,
    InvalidDrain,
    InvalidCollector,
    Incomplete
}

public sealed class ReplayQueuePressureSummary
{
    public const int SchemaVersionValue = 1;

    public required long LifecycleEpoch { get; init; }

    public required int Capacity { get; init; }

    public required long AcceptedCount { get; init; }

    public required long CompletedCount { get; init; }

    public required long RejectedCount { get; init; }

    public required long AbandonedCount { get; init; }

    public required int CurrentDepth { get; init; }

    public required int PeakDepth { get; init; }

    public required int InFlightCount { get; init; }

    public required bool Overflowed { get; init; }

    public required bool IsDrained { get; init; }

    public required int PeakAbsoluteDepth { get; init; }

    public double? PeakDepthFraction { get; init; }

    public required bool BacklogObserved { get; init; }

    public required bool BacklogCleared { get; init; }

    public required IReadOnlyList<int> ThresholdsCrossed { get; init; }
}

public sealed class ReplayPendingCommittedEventSummary
{
    public const int SchemaVersionValue = 1;

    public required int Capacity { get; init; }

    public required int CurrentCount { get; init; }

    public required int PeakCount { get; init; }

    public required long DiscardedCount { get; init; }
}

public sealed class ReplayThroughputSummary
{
    public const int SchemaVersionValue = 1;

    public double? SourceLinesPerSecond { get; init; }

    public double? SourceBytesPerSecond { get; init; }

    public double? ParserEventsPerSecond { get; init; }

    public double? GameplayEventsPerSecond { get; init; }
}

public sealed class ReplayContextWriteTiming
{
    public const int SchemaVersionValue = 1;

    public required string Label { get; init; }

    public required long SourceCompleteLines { get; init; }

    public required long SourceBytes { get; init; }

    public long? WriteDurationMilliseconds { get; init; }

    public double? AchievedLinesPerSecond { get; init; }

    public double? AchievedBytesPerSecond { get; init; }
}

public sealed class ReplayConcurrentWriteThroughput
{
    public const int SchemaVersionValue = 1;

    public required long AggregateSourceCompleteLines { get; init; }

    public required long AggregateSourceBytes { get; init; }

    public long? AggregateWriteDurationMilliseconds { get; init; }

    public double? AggregateLinesPerSecond { get; init; }

    public double? AggregateBytesPerSecond { get; init; }

    public required IReadOnlyList<ReplayContextWriteTiming> Contexts { get; init; }

    public static ReplayConcurrentWriteThroughput? Calculate(
        IReadOnlyList<ReplayContextWriteTimingInput> contexts,
        TimeProvider timeProvider)
    {
        if (contexts.Count == 0)
        {
            return null;
        }

        var earliestStart = contexts.Min(context => context.WriteStartTimestamp);
        var latestEnd = contexts.Max(context => context.WriteEndTimestamp);
        if (latestEnd <= earliestStart)
        {
            return new ReplayConcurrentWriteThroughput
            {
                AggregateSourceCompleteLines = contexts.Sum(context => context.SourceCompleteLines),
                AggregateSourceBytes = contexts.Sum(context => context.SourceBytes),
                AggregateWriteDurationMilliseconds = null,
                AggregateLinesPerSecond = null,
                AggregateBytesPerSecond = null,
                Contexts = contexts
                    .Select(
                        context => new ReplayContextWriteTiming
                        {
                            Label = context.Label,
                            SourceCompleteLines = context.SourceCompleteLines,
                            SourceBytes = context.SourceBytes,
                            WriteDurationMilliseconds = null,
                            AchievedLinesPerSecond = null,
                            AchievedBytesPerSecond = null
                        })
                    .ToArray()
            };
        }

        var aggregateDuration = timeProvider.GetElapsedTime(earliestStart, latestEnd);
        var aggregateDurationMilliseconds = ToMilliseconds(aggregateDuration);
        if (aggregateDurationMilliseconds <= 0)
        {
            return new ReplayConcurrentWriteThroughput
            {
                AggregateSourceCompleteLines = contexts.Sum(context => context.SourceCompleteLines),
                AggregateSourceBytes = contexts.Sum(context => context.SourceBytes),
                AggregateWriteDurationMilliseconds = null,
                AggregateLinesPerSecond = null,
                AggregateBytesPerSecond = null,
                Contexts = contexts
                    .Select(
                        context =>
                        {
                            var contextDurationMilliseconds = (long?)ToMilliseconds(
                                timeProvider.GetElapsedTime(context.WriteStartTimestamp, context.WriteEndTimestamp));
                            if (contextDurationMilliseconds <= 0)
                            {
                                contextDurationMilliseconds = null;
                            }

                            return new ReplayContextWriteTiming
                            {
                                Label = context.Label,
                                SourceCompleteLines = context.SourceCompleteLines,
                                SourceBytes = context.SourceBytes,
                                WriteDurationMilliseconds = contextDurationMilliseconds,
                                AchievedLinesPerSecond = CalculateRate(
                                    context.SourceCompleteLines,
                                    contextDurationMilliseconds),
                                AchievedBytesPerSecond = CalculateRate(
                                    context.SourceBytes,
                                    contextDurationMilliseconds)
                            };
                        })
                    .ToArray()
            };
        }

        var aggregateLines = contexts.Sum(context => context.SourceCompleteLines);
        var aggregateBytes = contexts.Sum(context => context.SourceBytes);

        return new ReplayConcurrentWriteThroughput
        {
            AggregateSourceCompleteLines = aggregateLines,
            AggregateSourceBytes = aggregateBytes,
            AggregateWriteDurationMilliseconds = aggregateDurationMilliseconds,
            AggregateLinesPerSecond = CalculateRate(aggregateLines, aggregateDurationMilliseconds),
            AggregateBytesPerSecond = CalculateRate(aggregateBytes, aggregateDurationMilliseconds),
            Contexts = contexts
                .Select(
                    context =>
                    {
                        var contextDurationMilliseconds = ToMilliseconds(
                            timeProvider.GetElapsedTime(context.WriteStartTimestamp, context.WriteEndTimestamp));
                        return new ReplayContextWriteTiming
                        {
                            Label = context.Label,
                            SourceCompleteLines = context.SourceCompleteLines,
                            SourceBytes = context.SourceBytes,
                            WriteDurationMilliseconds = contextDurationMilliseconds,
                            AchievedLinesPerSecond = CalculateRate(
                                context.SourceCompleteLines,
                                contextDurationMilliseconds),
                            AchievedBytesPerSecond = CalculateRate(
                                context.SourceBytes,
                                contextDurationMilliseconds)
                        };
                    })
                .ToArray()
        };
    }

    private static long ToMilliseconds(TimeSpan elapsed) => (long)Math.Round(elapsed.TotalMilliseconds);

    private static double? CalculateRate(long amount, long? durationMilliseconds)
    {
        if (durationMilliseconds is null or <= 0 || amount <= 0)
        {
            return null;
        }

        return amount / (durationMilliseconds.Value / 1000.0);
    }
}

public sealed class ReplayContextWriteTimingInput
{
    public required string Label { get; init; }

    public required long SourceCompleteLines { get; init; }

    public required long SourceBytes { get; init; }

    public required long WriteStartTimestamp { get; init; }

    public required long WriteEndTimestamp { get; init; }
}

public sealed class ReplayResourceMetricSummary
{
    public const int SchemaVersionValue = 1;

    public double? CpuUtilizationPercentFirst { get; init; }

    public double? CpuUtilizationPercentMin { get; init; }

    public double? CpuUtilizationPercentMax { get; init; }

    public double? CpuUtilizationPercentLatest { get; init; }

    public long? WorkingSetBytesFirst { get; init; }

    public long? WorkingSetBytesMin { get; init; }

    public long? WorkingSetBytesMax { get; init; }

    public long? WorkingSetBytesLatest { get; init; }

    public long? PrivateMemoryBytesFirst { get; init; }

    public long? PrivateMemoryBytesMin { get; init; }

    public long? PrivateMemoryBytesMax { get; init; }

    public long? PrivateMemoryBytesLatest { get; init; }

    public long? ManagedHeapBytesFirst { get; init; }

    public long? ManagedHeapBytesMin { get; init; }

    public long? ManagedHeapBytesMax { get; init; }

    public long? ManagedHeapBytesLatest { get; init; }

    public long? TotalAllocatedBytesFirst { get; init; }

    public long? TotalAllocatedBytesMin { get; init; }

    public long? TotalAllocatedBytesMax { get; init; }

    public long? TotalAllocatedBytesLatest { get; init; }

    public int? Gen0CollectionCountFirst { get; init; }

    public int? Gen0CollectionCountLatest { get; init; }

    public int? Gen1CollectionCountFirst { get; init; }

    public int? Gen1CollectionCountLatest { get; init; }

    public int? Gen2CollectionCountFirst { get; init; }

    public int? Gen2CollectionCountLatest { get; init; }

    public int? ThreadCountFirst { get; init; }

    public int? ThreadCountMin { get; init; }

    public int? ThreadCountMax { get; init; }

    public int? ThreadCountLatest { get; init; }

    public int? HandleCountFirst { get; init; }

    public int? HandleCountMin { get; init; }

    public int? HandleCountMax { get; init; }

    public int? HandleCountLatest { get; init; }

    public bool HandleCountSupported { get; init; }
}

public sealed class ReplayCollectorOverheadSummary
{
    public const int SchemaVersionValue = 1;

    public required long SampleCount { get; init; }

    public required long DroppedSampleCount { get; init; }

    public required long SampleIntervalMilliseconds { get; init; }

    public required double TotalSamplingDurationMilliseconds { get; init; }

    public required double MaxSingleSampleDurationMilliseconds { get; init; }

    public required string MeasurementNote { get; init; }
}

public sealed class ReplayPerformanceObservationReport
{
    public const int SchemaVersionValue = 1;

    public required ReplayPerformanceValidity Validity { get; init; }

    public required long RunDurationMilliseconds { get; init; }

    public long? WriteDurationMilliseconds { get; init; }

    public long? DrainDurationMilliseconds { get; init; }

    public long? FinalByteToParserCompleteMilliseconds { get; init; }

    public long? ParserCompleteToGameplayCompleteMilliseconds { get; init; }

    public long? GameplayCompleteToOracleFinalizedMilliseconds { get; init; }

    public required long SourceBytes { get; init; }

    public required long SourceCompleteLines { get; init; }

    public required long ReplayedBytes { get; init; }

    public required long ReplayedCompleteLines { get; init; }

    public required long BootstrapBytes { get; init; }

    public required long BootstrapCompleteLines { get; init; }

    public double? AchievedLinesPerSecond { get; init; }

    public double? AchievedBytesPerSecond { get; init; }

    public required ReplayQueuePressureSummary ParserMonitoringSnapshotQueue { get; init; }

    public required ReplayQueuePressureSummary ParserEventQueue { get; init; }

    public required ReplayQueuePressureSummary GameplayWorkQueue { get; init; }

    public required ReplayPendingCommittedEventSummary PendingCommittedEvents { get; init; }

    public required long ParserTotalLinesProcessed { get; init; }

    public required long ParserRawEventsObserved { get; init; }

    public required long ParserClassifiedEventsObserved { get; init; }

    public required int ParserWorkerCount { get; init; }

    public long? ParserDrainDurationMilliseconds { get; init; }

    public long? ParserCompletionElapsedMilliseconds { get; init; }

    public required long GameplayAcceptedWorkWatermark { get; init; }

    public required long GameplayCompletedWorkWatermark { get; init; }

    public required int GameplayActiveProcessorCallbackCount { get; init; }

    public required long GameplayCommittedEventCount { get; init; }

    public required bool GameplayOverloadState { get; init; }

    public required long GameplayDiscardedCommittedEvents { get; init; }

    public required long GameplayAbandonedWorkCount { get; init; }

    public long? GameplayDrainDurationMilliseconds { get; init; }

    public long? GameplayCompletionElapsedMilliseconds { get; init; }

    public required bool CorrectnessPassed { get; init; }

    public required ReplayDrainOutcome DrainOutcome { get; init; }

    public required ReplayThroughputSummary Throughput { get; init; }

    public ReplayResourceMetricSummary? Resources { get; init; }

    public required ReplayCollectorOverheadSummary CollectorOverhead { get; init; }

    public ReplayProfileObservationReport? Profile { get; init; }

    public IReadOnlyList<ReplayContextSummaryReport>? ContextSummaries { get; init; }

    public ReplayLatencyAggregateReport? Latency { get; init; }

    public double? RequestedLinesPerSecond { get; init; }

    public long? PacingUnderrunCount { get; init; }

    public double? PacingOverrunMilliseconds { get; init; }

    public ReplayConcurrentWriteThroughput? ConcurrentWriteThroughput { get; init; }
}

public sealed record ReplayResourceSample(
    double? CpuUtilizationPercent,
    long? WorkingSetBytes,
    long? PrivateMemoryBytes,
    long? ManagedHeapBytes,
    long? TotalAllocatedBytes,
    int? Gen0CollectionCount,
    int? Gen1CollectionCount,
    int? Gen2CollectionCount,
    int? ThreadCount,
    int? HandleCount,
    bool HandleCountSupported);

internal sealed record ReplayPerformanceSample(
    long ElapsedMilliseconds,
    int ParserEventQueueDepth,
    int ParserMonitoringQueueDepth,
    int GameplayWorkQueueDepth,
    ReplayResourceSample? Resource);
