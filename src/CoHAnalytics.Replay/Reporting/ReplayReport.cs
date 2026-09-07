using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CoHAnalytics.Replay.Reporting;

public sealed class ReplayReportDocument
{
    public const int SchemaVersionValue = 1;

    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; } = SchemaVersionValue;

    [JsonPropertyName("run")]
    public ReplayRunSection Run { get; init; } = new();

    [JsonPropertyName("input")]
    public ReplayInputSection Input { get; init; } = new();

    [JsonPropertyName("replay")]
    public ReplayExecutionSection Replay { get; init; } = new();

    [JsonPropertyName("timeline")]
    public ReplayTimelineSection Timeline { get; init; } = new();

    [JsonPropertyName("parser")]
    public ReplayParserSection? Parser { get; init; }

    [JsonPropertyName("gameplay")]
    public ReplayGameplaySection? Gameplay { get; init; }

    [JsonPropertyName("correctness")]
    public ReplayCorrectnessSection? Correctness { get; init; }

    [JsonPropertyName("performance")]
    public ReplayPerformanceSection? Performance { get; init; }

    [JsonPropertyName("throughput")]
    public ReplayThroughputSection? Throughput { get; init; }

    [JsonPropertyName("queues")]
    public ReplayQueuesSection? Queues { get; init; }

    [JsonPropertyName("resources")]
    public ReplayResourcesSection? Resources { get; init; }

    [JsonPropertyName("profile")]
    public ReplayProfileSection? Profile { get; init; }

    [JsonPropertyName("latency")]
    public ReplayLatencySection? Latency { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class ReplayRunSection
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; } = 1;

    [JsonPropertyName("status")]
    public string Status { get; init; } = "Completed";

    [JsonPropertyName("startedAtUtc")]
    public DateTimeOffset StartedAtUtc { get; init; }

    [JsonPropertyName("completedAtUtc")]
    public DateTimeOffset CompletedAtUtc { get; init; }

    [JsonPropertyName("elapsedMilliseconds")]
    public long ElapsedMilliseconds { get; init; }

    [JsonPropertyName("workspaceRetained")]
    public bool WorkspaceRetained { get; init; }

    [JsonPropertyName("workspaceCleanupCompleted")]
    public bool WorkspaceCleanupCompleted { get; init; }
}

public sealed class ReplayInputSection
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; } = 1;

    [JsonPropertyName("mode")]
    public string Mode { get; init; } = "Exact";

    [JsonPropertyName("beginsMidSession")]
    public bool BeginsMidSession { get; init; }

    [JsonPropertyName("sourceBytes")]
    public long SourceBytes { get; init; }

    [JsonPropertyName("sourceCompleteLines")]
    public long SourceCompleteLines { get; init; }

    [JsonPropertyName("bootstrapBytes")]
    public long BootstrapBytes { get; init; }

    [JsonPropertyName("bootstrapCompleteLines")]
    public long BootstrapCompleteLines { get; init; }

    [JsonPropertyName("combinedDestinationBytes")]
    public long CombinedDestinationBytes { get; init; }

    [JsonPropertyName("combinedDestinationCompleteLines")]
    public long CombinedDestinationCompleteLines { get; init; }

    [JsonPropertyName("hasIncompleteFinalFragment")]
    public bool HasIncompleteFinalFragment { get; init; }

    [JsonPropertyName("incompleteFinalFragmentBytes")]
    public long IncompleteFinalFragmentBytes { get; init; }

    [JsonPropertyName("segmentCount")]
    public int SegmentCount { get; init; }
}

public sealed class ReplayExecutionSection
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; } = 1;

    [JsonPropertyName("chunkMode")]
    public string ChunkMode { get; init; } = string.Empty;

    [JsonPropertyName("timingMode")]
    public string TimingMode { get; init; } = string.Empty;

    [JsonPropertyName("seed")]
    public int Seed { get; init; }

    [JsonPropertyName("plannedChunks")]
    public long PlannedChunks { get; init; }

    [JsonPropertyName("appendedChunks")]
    public long AppendedChunks { get; init; }

    [JsonPropertyName("flushes")]
    public long Flushes { get; init; }

    [JsonPropertyName("burstCount")]
    public long BurstCount { get; init; }

    [JsonPropertyName("rolloverCount")]
    public long RolloverCount { get; init; }

    [JsonPropertyName("segments")]
    public IReadOnlyList<ReplaySegmentReport> Segments { get; init; } = [];
}

public sealed class ReplaySegmentReport
{
    [JsonPropertyName("ordinal")]
    public int Ordinal { get; init; }

    [JsonPropertyName("destinationDate")]
    public string DestinationDate { get; init; } = string.Empty;

    [JsonPropertyName("sourceBytes")]
    public long SourceBytes { get; init; }

    [JsonPropertyName("destinationBytes")]
    public long DestinationBytes { get; init; }

    [JsonPropertyName("plannedChunks")]
    public long PlannedChunks { get; init; }

    [JsonPropertyName("appendedChunks")]
    public long AppendedChunks { get; init; }
}

public sealed class ReplayTimelineSection
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; } = 1;

    [JsonPropertyName("capacity")]
    public int Capacity { get; init; } = ReplayTimeline.Capacity;

    [JsonPropertyName("droppedEventCount")]
    public int DroppedEventCount { get; init; }

    [JsonPropertyName("truncated")]
    public bool Truncated { get; init; }

    [JsonPropertyName("events")]
    public IReadOnlyList<ReplayTimelineEventReport> Events { get; init; } = [];
}

public sealed class ReplayTimelineEventReport
{
    [JsonPropertyName("sequence")]
    public long Sequence { get; init; }

    [JsonPropertyName("elapsedMilliseconds")]
    public long ElapsedMilliseconds { get; init; }

    [JsonPropertyName("category")]
    public string Category { get; init; } = string.Empty;

    [JsonPropertyName("code")]
    public string Code { get; init; } = string.Empty;

    [JsonPropertyName("contextOrdinal")]
    public int? ContextOrdinal { get; init; }

    [JsonPropertyName("value")]
    public long? Value { get; init; }

    [JsonPropertyName("unit")]
    public string? Unit { get; init; }

    [JsonPropertyName("retentionClass")]
    public string RetentionClass { get; init; } = string.Empty;
}

public sealed class ReplayParserSection
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; } = ReplayParserObservationReport.SchemaVersionValue;

    [JsonPropertyName("rawEventsObserved")]
    public long RawEventsObserved { get; init; }

    [JsonPropertyName("classifiedEventsObserved")]
    public long ClassifiedEventsObserved { get; init; }

    [JsonPropertyName("totalLinesProcessed")]
    public long TotalLinesProcessed { get; init; }

    [JsonPropertyName("expectedCompleteLines")]
    public long ExpectedCompleteLines { get; init; }

    [JsonPropertyName("workerCount")]
    public int WorkerCount { get; init; }

    [JsonPropertyName("parserDrained")]
    public bool ParserDrained { get; init; }
}

public sealed class ReplayGameplaySection
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; } = ReplayGameplayObservationReport.SchemaVersionValue;

    [JsonPropertyName("committedEventsObserved")]
    public long CommittedEventsObserved { get; init; }

    [JsonPropertyName("activeSessionCount")]
    public int ActiveSessionCount { get; init; }

    [JsonPropertyName("gameplayDrained")]
    public bool GameplayDrained { get; init; }
}

public sealed class ReplayCorrectnessSection
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; } = ReplayCorrectnessReport.SchemaVersionValue;

    [JsonPropertyName("passed")]
    public bool Passed { get; init; }

    [JsonPropertyName("contextCount")]
    public int ContextCount { get; init; }

    [JsonPropertyName("welcomeBoundariesObserved")]
    public int WelcomeBoundariesObserved { get; init; }

    [JsonPropertyName("beginsMidSession")]
    public bool BeginsMidSession { get; init; }

    [JsonPropertyName("failureCount")]
    public int FailureCount { get; init; }

    [JsonPropertyName("failures")]
    public IReadOnlyList<ReplayCorrectnessFailureReport> Failures { get; init; } = [];
}

public sealed class ReplayCorrectnessFailureReport
{
    [JsonPropertyName("code")]
    public string Code { get; init; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;
}

public sealed class ReplayPerformanceSection
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; } = ReplayPerformanceObservationReport.SchemaVersionValue;

    [JsonPropertyName("validity")]
    public string Validity { get; init; } = string.Empty;

    [JsonPropertyName("runDurationMs")]
    public long RunDurationMs { get; init; }

    [JsonPropertyName("writeDurationMs")]
    public long? WriteDurationMs { get; init; }

    [JsonPropertyName("drainDurationMs")]
    public long? DrainDurationMs { get; init; }

    [JsonPropertyName("correctnessPassed")]
    public bool CorrectnessPassed { get; init; }

    [JsonPropertyName("drainOutcome")]
    public string DrainOutcome { get; init; } = string.Empty;

    [JsonPropertyName("gameplayOverloadState")]
    public bool GameplayOverloadState { get; init; }

    [JsonPropertyName("measurementNote")]
    public string MeasurementNote { get; init; } = string.Empty;
}

public sealed class ReplayThroughputSection
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; } = ReplayThroughputSummary.SchemaVersionValue;

    [JsonPropertyName("sourceLinesPerSecond")]
    public double? SourceLinesPerSecond { get; init; }

    [JsonPropertyName("sourceBytesPerSecond")]
    public double? SourceBytesPerSecond { get; init; }

    [JsonPropertyName("parserEventsPerSecond")]
    public double? ParserEventsPerSecond { get; init; }

    [JsonPropertyName("gameplayEventsPerSecond")]
    public double? GameplayEventsPerSecond { get; init; }

    [JsonPropertyName("sourceBytes")]
    public long SourceBytes { get; init; }

    [JsonPropertyName("sourceCompleteLines")]
    public long SourceCompleteLines { get; init; }

    [JsonPropertyName("bootstrapBytes")]
    public long BootstrapBytes { get; init; }

    [JsonPropertyName("bootstrapCompleteLines")]
    public long BootstrapCompleteLines { get; init; }

    [JsonPropertyName("aggregateWriteDurationMs")]
    public long? AggregateWriteDurationMs { get; init; }

    [JsonPropertyName("concurrentContexts")]
    public IReadOnlyList<ReplayConcurrentContextThroughputSection>? ConcurrentContexts { get; init; }
}

public sealed class ReplayConcurrentContextThroughputSection
{
    [JsonPropertyName("label")]
    public string Label { get; init; } = string.Empty;

    [JsonPropertyName("sourceLines")]
    public long SourceLines { get; init; }

    [JsonPropertyName("sourceBytes")]
    public long SourceBytes { get; init; }

    [JsonPropertyName("writeDurationMs")]
    public long? WriteDurationMs { get; init; }

    [JsonPropertyName("achievedLinesPerSecond")]
    public double? AchievedLinesPerSecond { get; init; }

    [JsonPropertyName("achievedBytesPerSecond")]
    public double? AchievedBytesPerSecond { get; init; }
}

public sealed class ReplayQueuePressureSection
{
    [JsonPropertyName("lifecycleEpoch")]
    public long LifecycleEpoch { get; init; }

    [JsonPropertyName("capacity")]
    public int Capacity { get; init; }

    [JsonPropertyName("peakAbsoluteDepth")]
    public int PeakAbsoluteDepth { get; init; }

    [JsonPropertyName("peakDepthFraction")]
    public double? PeakDepthFraction { get; init; }

    [JsonPropertyName("overflowed")]
    public bool Overflowed { get; init; }

    [JsonPropertyName("isDrained")]
    public bool IsDrained { get; init; }

    [JsonPropertyName("backlogObserved")]
    public bool BacklogObserved { get; init; }

    [JsonPropertyName("backlogCleared")]
    public bool BacklogCleared { get; init; }

    [JsonPropertyName("thresholdsCrossed")]
    public IReadOnlyList<int> ThresholdsCrossed { get; init; } = [];
}

public sealed class ReplayQueuesSection
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; } = 1;

    [JsonPropertyName("parserMonitoringSnapshotQueue")]
    public ReplayQueuePressureSection ParserMonitoringSnapshotQueue { get; init; } = new();

    [JsonPropertyName("parserEventQueue")]
    public ReplayQueuePressureSection ParserEventQueue { get; init; } = new();

    [JsonPropertyName("gameplayWorkQueue")]
    public ReplayQueuePressureSection GameplayWorkQueue { get; init; } = new();

    [JsonPropertyName("pendingCommittedEvents")]
    public ReplayPendingCommittedSection PendingCommittedEvents { get; init; } = new();
}

public sealed class ReplayPendingCommittedSection
{
    [JsonPropertyName("capacity")]
    public int Capacity { get; init; }

    [JsonPropertyName("peakCount")]
    public int PeakCount { get; init; }

    [JsonPropertyName("discardedCount")]
    public long DiscardedCount { get; init; }
}

public sealed class ReplayResourcesSection
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; } = ReplayResourceMetricSummary.SchemaVersionValue;

    [JsonPropertyName("cpuUtilizationPercentMax")]
    public double? CpuUtilizationPercentMax { get; init; }

    [JsonPropertyName("cpuUtilizationPercentLatest")]
    public double? CpuUtilizationPercentLatest { get; init; }

    [JsonPropertyName("workingSetBytesMax")]
    public long? WorkingSetBytesMax { get; init; }

    [JsonPropertyName("managedHeapBytesMax")]
    public long? ManagedHeapBytesMax { get; init; }

    [JsonPropertyName("handleCountSupported")]
    public bool HandleCountSupported { get; init; }

    [JsonPropertyName("sampleCount")]
    public long SampleCount { get; init; }

    [JsonPropertyName("droppedSampleCount")]
    public long DroppedSampleCount { get; init; }
}

public sealed class ReplayProfileSection
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; } = ReplayProfileObservationReport.SchemaVersionValue;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("outcome")]
    public string Outcome { get; init; } = string.Empty;

    [JsonPropertyName("contextCount")]
    public int ContextCount { get; init; }

    [JsonPropertyName("requestedRateLinesPerSecond")]
    public double? RequestedRateLinesPerSecond { get; init; }

    [JsonPropertyName("achievedRateLinesPerSecond")]
    public double? AchievedRateLinesPerSecond { get; init; }

    [JsonPropertyName("stressSeed")]
    public int? StressSeed { get; init; }

    [JsonPropertyName("generatedLineCount")]
    public long? GeneratedLineCount { get; init; }

    [JsonPropertyName("expectedOverloadTarget")]
    public string? ExpectedOverloadTarget { get; init; }

    [JsonPropertyName("effectiveOptions")]
    public IReadOnlyDictionary<string, string>? EffectiveOptions { get; init; }

    [JsonPropertyName("contexts")]
    public IReadOnlyList<ReplayContextReportSection>? Contexts { get; init; }

    [JsonPropertyName("overloadDiagnosticsSummary")]
    public string? OverloadDiagnosticsSummary { get; init; }
}

public sealed class ReplayContextReportSection
{
    [JsonPropertyName("label")]
    public string Label { get; init; } = string.Empty;

    [JsonPropertyName("sourceLines")]
    public long SourceLines { get; init; }

    [JsonPropertyName("sourceBytes")]
    public long SourceBytes { get; init; }

    [JsonPropertyName("achievedRateLinesPerSecond")]
    public double? AchievedRateLinesPerSecond { get; init; }
}

public sealed class ReplayLatencySection
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; } = ReplayLatencyAggregateReport.SchemaVersionValue;

    [JsonPropertyName("count")]
    public long Count { get; init; }

    [JsonPropertyName("p50Milliseconds")]
    public double? P50Milliseconds { get; init; }

    [JsonPropertyName("p95Milliseconds")]
    public double? P95Milliseconds { get; init; }

    [JsonPropertyName("p99Milliseconds")]
    public double? P99Milliseconds { get; init; }

    [JsonPropertyName("maxMilliseconds")]
    public double? MaxMilliseconds { get; init; }
}

public static class ReplayReportBuilder
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    public static ReplayReportDocument Build(
        ReplayPlan plan,
        ReplayLedger ledger,
        ReplayTimeline timeline,
        ReplayWorkspace workspace,
        DateTimeOffset startedAtUtc,
        DateTimeOffset completedAtUtc,
        long elapsedMilliseconds,
        string status,
        ReplayPipelineResult? pipelineResult = null)
    {
        var performance = pipelineResult?.Performance;
        var concurrentThroughput = performance?.ConcurrentWriteThroughput;
        return new ReplayReportDocument
        {
            Run = new ReplayRunSection
            {
                Status = status,
                StartedAtUtc = startedAtUtc,
                CompletedAtUtc = completedAtUtc,
                ElapsedMilliseconds = elapsedMilliseconds,
                WorkspaceRetained = plan.KeepWorkspace,
                WorkspaceCleanupCompleted = workspace.CleanupCompleted
            },
            Input = new ReplayInputSection
            {
                Mode = plan.InputMode.ToString(),
                BeginsMidSession = ledger.BeginsMidSession,
                SourceBytes = performance?.SourceBytes ?? ledger.OriginalSourceBytes,
                SourceCompleteLines = performance?.SourceCompleteLines ?? ledger.OriginalSourceCompleteLines,
                BootstrapBytes = performance?.BootstrapBytes ?? ledger.BootstrapBytes,
                BootstrapCompleteLines = performance?.BootstrapCompleteLines ?? ledger.BootstrapCompleteLines,
                CombinedDestinationBytes = performance?.ReplayedBytes ?? ledger.CombinedDestinationBytes,
                CombinedDestinationCompleteLines = performance?.ReplayedCompleteLines ?? ledger.CombinedDestinationCompleteLines,
                HasIncompleteFinalFragment = ledger.HasIncompleteFinalFragment,
                IncompleteFinalFragmentBytes = ledger.IncompleteFinalFragmentBytes,
                SegmentCount = plan.Segments.Count
            },
            Replay = new ReplayExecutionSection
            {
                ChunkMode = plan.ChunkMode.ToString(),
                TimingMode = plan.TimingMode.ToString(),
                Seed = plan.Seed,
                PlannedChunks = ledger.PlannedChunks,
                AppendedChunks = ledger.AppendedChunks,
                Flushes = ledger.Flushes,
                BurstCount = ledger.BurstCount,
                RolloverCount = ledger.RolloverCount,
                Segments = plan.Segments.Select(segment =>
                {
                    ledger.Segments.TryGetValue(segment.Ordinal, out var segmentLedger);
                    segmentLedger ??= new ReplaySegmentLedger { Ordinal = segment.Ordinal };
                    return new ReplaySegmentReport
                    {
                        Ordinal = segment.Ordinal,
                        DestinationDate = segment.DestinationDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        SourceBytes = segmentLedger.SourceBytes,
                        DestinationBytes = segmentLedger.DestinationBytes,
                        PlannedChunks = segmentLedger.PlannedChunks,
                        AppendedChunks = segmentLedger.AppendedChunks
                    };
                }).ToArray()
            },
            Parser = pipelineResult is null
                ? null
                : new ReplayParserSection
                {
                    RawEventsObserved = pipelineResult.Parser.RawEventsObserved,
                    ClassifiedEventsObserved = pipelineResult.Parser.ClassifiedEventsObserved,
                    TotalLinesProcessed = pipelineResult.Parser.TotalLinesProcessed,
                    ExpectedCompleteLines = pipelineResult.Parser.ExpectedCompleteLines,
                    WorkerCount = pipelineResult.Parser.WorkerCount,
                    ParserDrained = pipelineResult.Parser.ParserDrained
                },
            Gameplay = pipelineResult is null
                ? null
                : new ReplayGameplaySection
                {
                    CommittedEventsObserved = pipelineResult.Gameplay.CommittedEventsObserved,
                    ActiveSessionCount = pipelineResult.Gameplay.ActiveSessionCount,
                    GameplayDrained = pipelineResult.Gameplay.GameplayDrained
                },
            Correctness = pipelineResult is null
                ? null
                : new ReplayCorrectnessSection
                {
                    Passed = pipelineResult.Correctness.Passed,
                    ContextCount = pipelineResult.Correctness.ContextCount,
                    WelcomeBoundariesObserved = pipelineResult.Correctness.WelcomeBoundariesObserved,
                    BeginsMidSession = pipelineResult.Correctness.BeginsMidSession,
                    FailureCount = pipelineResult.Correctness.Failures.Count,
                    Failures = pipelineResult.Correctness.Failures
                        .Select(failure => new ReplayCorrectnessFailureReport
                        {
                            Code = failure.Code,
                            Message = failure.Message
                        })
                        .ToArray()
                },
            Timeline = new ReplayTimelineSection
            {
                DroppedEventCount = timeline.DroppedEventCount,
                Truncated = timeline.Truncated,
                Events = timeline.Events.Select(entry => new ReplayTimelineEventReport
                {
                    Sequence = entry.Sequence,
                    ElapsedMilliseconds = entry.ElapsedMilliseconds,
                    Category = entry.Category.ToString(),
                    Code = entry.Code,
                    ContextOrdinal = entry.ContextOrdinal,
                    Value = entry.Value,
                    Unit = entry.Unit,
                    RetentionClass = entry.RetentionClass.ToString()
                }).ToArray()
            },
            Performance = performance is null
                ? null
                : new ReplayPerformanceSection
                {
                    Validity = performance.Validity.ToString(),
                    RunDurationMs = performance.RunDurationMilliseconds,
                    WriteDurationMs = performance.WriteDurationMilliseconds,
                    DrainDurationMs = performance.DrainDurationMilliseconds,
                    CorrectnessPassed = performance.CorrectnessPassed,
                    DrainOutcome = performance.DrainOutcome.ToString(),
                    GameplayOverloadState = performance.GameplayOverloadState,
                    MeasurementNote = performance.CollectorOverhead.MeasurementNote
                },
            Throughput = performance is null
                ? null
                : new ReplayThroughputSection
                {
                    SourceLinesPerSecond = performance.Throughput.SourceLinesPerSecond,
                    SourceBytesPerSecond = performance.Throughput.SourceBytesPerSecond,
                    ParserEventsPerSecond = performance.Throughput.ParserEventsPerSecond,
                    GameplayEventsPerSecond = performance.Throughput.GameplayEventsPerSecond,
                    SourceBytes = performance.SourceBytes,
                    SourceCompleteLines = performance.SourceCompleteLines,
                    BootstrapBytes = performance.BootstrapBytes,
                    BootstrapCompleteLines = performance.BootstrapCompleteLines,
                    AggregateWriteDurationMs = concurrentThroughput?.AggregateWriteDurationMilliseconds
                        ?? performance.WriteDurationMilliseconds,
                    ConcurrentContexts = concurrentThroughput?.Contexts
                        .Select(
                            context => new ReplayConcurrentContextThroughputSection
                            {
                                Label = context.Label,
                                SourceLines = context.SourceCompleteLines,
                                SourceBytes = context.SourceBytes,
                                WriteDurationMs = context.WriteDurationMilliseconds,
                                AchievedLinesPerSecond = context.AchievedLinesPerSecond,
                                AchievedBytesPerSecond = context.AchievedBytesPerSecond
                            })
                        .ToArray()
                },
            Queues = performance is null
                ? null
                : new ReplayQueuesSection
                {
                    ParserMonitoringSnapshotQueue = ToQueueSection(performance.ParserMonitoringSnapshotQueue),
                    ParserEventQueue = ToQueueSection(performance.ParserEventQueue),
                    GameplayWorkQueue = ToQueueSection(performance.GameplayWorkQueue),
                    PendingCommittedEvents = new ReplayPendingCommittedSection
                    {
                        Capacity = performance.PendingCommittedEvents.Capacity,
                        PeakCount = performance.PendingCommittedEvents.PeakCount,
                        DiscardedCount = performance.PendingCommittedEvents.DiscardedCount
                    }
                },
            Resources = performance?.Resources is null
                ? null
                : new ReplayResourcesSection
                {
                    CpuUtilizationPercentMax = performance.Resources.CpuUtilizationPercentMax,
                    CpuUtilizationPercentLatest = performance.Resources.CpuUtilizationPercentLatest,
                    WorkingSetBytesMax = performance.Resources.WorkingSetBytesMax,
                    ManagedHeapBytesMax = performance.Resources.ManagedHeapBytesMax,
                    HandleCountSupported = performance.Resources.HandleCountSupported,
                    SampleCount = performance.CollectorOverhead.SampleCount,
                    DroppedSampleCount = performance.CollectorOverhead.DroppedSampleCount
                },
            Profile = pipelineResult?.Profile is null
                ? null
                : new ReplayProfileSection
                {
                    Name = pipelineResult.Profile.ProfileName,
                    Outcome = pipelineResult.Profile.Outcome.ToString(),
                    ContextCount = pipelineResult.Profile.ContextCount,
                    RequestedRateLinesPerSecond = pipelineResult.Profile.RequestedRateLinesPerSecond,
                    AchievedRateLinesPerSecond = pipelineResult.Profile.AchievedRateLinesPerSecond,
                    StressSeed = pipelineResult.Profile.StressSeed,
                    GeneratedLineCount = pipelineResult.Profile.GeneratedLineCount,
                    ExpectedOverloadTarget = pipelineResult.Profile.ExpectedOverloadTarget.ToString(),
                    EffectiveOptions = pipelineResult.Profile.EffectiveOptions,
                    Contexts = pipelineResult.Performance?.ContextSummaries?
                        .Select(context => new ReplayContextReportSection
                        {
                            Label = context.Label,
                            SourceLines = context.SourceLines,
                            SourceBytes = context.SourceBytes,
                            AchievedRateLinesPerSecond = context.AchievedRateLinesPerSecond
                        })
                        .ToArray(),
                    OverloadDiagnosticsSummary = pipelineResult.Profile.OverloadDiagnosticsSummary
                },
            Latency = (pipelineResult?.Profile?.Latency ?? pipelineResult?.Performance?.Latency) is not { } latency
                ? null
                : new ReplayLatencySection
                {
                    Count = latency.Count,
                    P50Milliseconds = latency.P50Milliseconds,
                    P95Milliseconds = latency.P95Milliseconds,
                    P99Milliseconds = latency.P99Milliseconds,
                    MaxMilliseconds = latency.MaxMilliseconds
                }
        };
    }

    private static ReplayQueuePressureSection ToQueueSection(ReplayQueuePressureSummary summary) =>
        new()
        {
            LifecycleEpoch = summary.LifecycleEpoch,
            Capacity = summary.Capacity,
            PeakAbsoluteDepth = summary.PeakAbsoluteDepth,
            PeakDepthFraction = summary.PeakDepthFraction,
            Overflowed = summary.Overflowed,
            IsDrained = summary.IsDrained,
            BacklogObserved = summary.BacklogObserved,
            BacklogCleared = summary.BacklogCleared,
            ThresholdsCrossed = summary.ThresholdsCrossed
        };

    public static string ToJson(ReplayReportDocument document) =>
        JsonSerializer.Serialize(document, SerializerOptions);

    public static string ToText(ReplayReportDocument document)
    {
        var builder = new StringBuilder();
        builder.AppendLine("CoH Analytics Replay Report");
        builder.AppendLine($"Schema Version: {document.SchemaVersion}");
        builder.AppendLine();
        builder.AppendLine("Run");
        builder.AppendLine($"  Status: {document.Run.Status}");
        builder.AppendLine($"  Started (UTC): {document.Run.StartedAtUtc:O}");
        builder.AppendLine($"  Completed (UTC): {document.Run.CompletedAtUtc:O}");
        builder.AppendLine($"  Elapsed: {document.Run.ElapsedMilliseconds} ms");
        if (document.Run.WorkspaceRetained)
        {
            builder.AppendLine("  Workspace: retained (not deleted)");
        }
        else
        {
            builder.AppendLine($"  Workspace deleted: {document.Run.WorkspaceCleanupCompleted}");
        }
        builder.AppendLine();
        builder.AppendLine("Input");
        builder.AppendLine($"  Mode: {document.Input.Mode}");
        if (string.Equals(document.Input.Mode, nameof(ReplayInputMode.Bootstrap), StringComparison.Ordinal))
        {
            builder.AppendLine("  Note: Bootstrap input was injected before the first source segment.");
        }

        builder.AppendLine($"  Begins mid-session: {document.Input.BeginsMidSession}");
        builder.AppendLine($"  Source bytes: {document.Input.SourceBytes}");
        builder.AppendLine($"  Source complete lines: {document.Input.SourceCompleteLines}");
        builder.AppendLine($"  Bootstrap bytes: {document.Input.BootstrapBytes}");
        builder.AppendLine($"  Bootstrap complete lines: {document.Input.BootstrapCompleteLines}");
        builder.AppendLine($"  Combined destination bytes: {document.Input.CombinedDestinationBytes}");
        builder.AppendLine($"  Combined destination complete lines: {document.Input.CombinedDestinationCompleteLines}");
        builder.AppendLine($"  Incomplete final fragment: {document.Input.HasIncompleteFinalFragment}");
        builder.AppendLine($"  Incomplete final fragment bytes: {document.Input.IncompleteFinalFragmentBytes}");
        builder.AppendLine($"  Segment count: {document.Input.SegmentCount}");
        builder.AppendLine();
        builder.AppendLine("Replay");
        builder.AppendLine($"  Chunk mode: {document.Replay.ChunkMode}");
        builder.AppendLine($"  Timing mode: {document.Replay.TimingMode}");
        builder.AppendLine($"  Seed: {document.Replay.Seed}");
        builder.AppendLine($"  Planned chunks: {document.Replay.PlannedChunks}");
        builder.AppendLine($"  Appended chunks: {document.Replay.AppendedChunks}");
        builder.AppendLine($"  Flushes: {document.Replay.Flushes}");
        builder.AppendLine($"  Burst count: {document.Replay.BurstCount}");
        builder.AppendLine($"  Rollover count: {document.Replay.RolloverCount}");
        if (document.Parser is not null)
        {
            builder.AppendLine();
            builder.AppendLine("Parser");
            builder.AppendLine($"  Raw events observed: {document.Parser.RawEventsObserved}");
            builder.AppendLine($"  Classified events observed: {document.Parser.ClassifiedEventsObserved}");
            builder.AppendLine($"  Total lines processed: {document.Parser.TotalLinesProcessed}");
            builder.AppendLine($"  Expected complete lines: {document.Parser.ExpectedCompleteLines}");
            builder.AppendLine($"  Worker count: {document.Parser.WorkerCount}");
            builder.AppendLine($"  Parser drained: {document.Parser.ParserDrained}");
        }

        if (document.Gameplay is not null)
        {
            builder.AppendLine();
            builder.AppendLine("Gameplay");
            builder.AppendLine($"  Committed events observed: {document.Gameplay.CommittedEventsObserved}");
            builder.AppendLine($"  Active session count: {document.Gameplay.ActiveSessionCount}");
            builder.AppendLine($"  Gameplay drained: {document.Gameplay.GameplayDrained}");
        }

        if (document.Correctness is not null)
        {
            builder.AppendLine();
            builder.AppendLine("Correctness");
            builder.AppendLine($"  Passed: {document.Correctness.Passed}");
            builder.AppendLine($"  Context count: {document.Correctness.ContextCount}");
            builder.AppendLine($"  Welcome boundaries observed: {document.Correctness.WelcomeBoundariesObserved}");
            builder.AppendLine($"  Begins mid-session: {document.Correctness.BeginsMidSession}");
            builder.AppendLine($"  Failure count: {document.Correctness.FailureCount}");
            foreach (var failure in document.Correctness.Failures)
            {
                builder.AppendLine($"  - {failure.Code}: {failure.Message}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("Timeline");
        builder.AppendLine($"  Capacity: {document.Timeline.Capacity}");
        builder.AppendLine($"  Dropped events: {document.Timeline.DroppedEventCount}");
        builder.AppendLine($"  Truncated: {document.Timeline.Truncated}");
        foreach (var entry in document.Timeline.Events)
        {
            builder.AppendLine(
                $"  [{entry.Sequence}] {entry.ElapsedMilliseconds} ms {entry.Category}/{entry.Code}");
        }

        if (document.Performance is not null)
        {
            builder.AppendLine();
            builder.AppendLine("Performance");
            builder.AppendLine($"  Validity: {document.Performance.Validity}");
            builder.AppendLine($"  Run duration: {document.Performance.RunDurationMs} ms");
            if (document.Performance.WriteDurationMs is not null)
            {
                builder.AppendLine($"  Write duration: {document.Performance.WriteDurationMs} ms");
            }

            if (document.Performance.DrainDurationMs is not null)
            {
                builder.AppendLine($"  Drain duration: {document.Performance.DrainDurationMs} ms");
            }

            builder.AppendLine($"  Correctness passed: {document.Performance.CorrectnessPassed}");
            builder.AppendLine($"  Drain outcome: {document.Performance.DrainOutcome}");
            builder.AppendLine($"  Gameplay overload: {document.Performance.GameplayOverloadState}");
            builder.AppendLine($"  Note: {document.Performance.MeasurementNote}");
        }

        if (document.Throughput is not null)
        {
            builder.AppendLine();
            builder.AppendLine("Throughput");
            builder.AppendLine($"  Source lines: {document.Throughput.SourceCompleteLines}");
            builder.AppendLine($"  Source bytes: {document.Throughput.SourceBytes}");
            if (document.Throughput.AggregateWriteDurationMs is not null)
            {
                builder.AppendLine($"  Aggregate write duration: {document.Throughput.AggregateWriteDurationMs} ms");
            }

            if (document.Throughput.SourceLinesPerSecond is not null)
            {
                builder.AppendLine(
                    $"  Achieved source rate: {document.Throughput.SourceLinesPerSecond:F2} lines/s, "
                    + $"{document.Throughput.SourceBytesPerSecond:F2} bytes/s");
            }

            if (document.Throughput.ConcurrentContexts is { Count: > 0 } contexts)
            {
                foreach (var context in contexts)
                {
                    builder.AppendLine(
                        $"  Context {context.Label}: {context.SourceLines} lines, "
                        + $"{context.AchievedLinesPerSecond:F2} lines/s");
                }
            }

            builder.AppendLine($"  Bootstrap bytes: {document.Throughput.BootstrapBytes}");
        }

        if (document.Queues is not null)
        {
            builder.AppendLine();
            builder.AppendLine("Queues");
            builder.AppendLine(
                $"  Parser event queue peak: {document.Queues.ParserEventQueue.PeakAbsoluteDepth}"
                + $"/{document.Queues.ParserEventQueue.Capacity}");
            builder.AppendLine(
                $"  Gameplay work queue peak: {document.Queues.GameplayWorkQueue.PeakAbsoluteDepth}"
                + $"/{document.Queues.GameplayWorkQueue.Capacity}");
        }

        if (document.Resources is not null)
        {
            builder.AppendLine();
            builder.AppendLine("Resources");
            if (document.Resources.CpuUtilizationPercentMax is not null)
            {
                builder.AppendLine(
                    $"  CPU estimate max: {document.Resources.CpuUtilizationPercentMax:F2}%");
            }

            if (document.Resources.WorkingSetBytesMax is not null)
            {
                builder.AppendLine($"  Peak working set: {document.Resources.WorkingSetBytesMax} bytes");
            }

            if (document.Resources.ManagedHeapBytesMax is not null)
            {
                builder.AppendLine($"  Peak managed heap: {document.Resources.ManagedHeapBytesMax} bytes");
            }

            builder.AppendLine($"  Performance samples: {document.Resources.SampleCount}");
            builder.AppendLine($"  Dropped samples: {document.Resources.DroppedSampleCount}");
        }

        return builder.ToString();
    }

    public static async Task WriteJsonAsync(
        ReplayReportDocument document,
        string path,
        CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(path, ToJson(document), cancellationToken).ConfigureAwait(false);
    }

    public static async Task WriteTextAsync(
        ReplayReportDocument document,
        string path,
        CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(path, ToText(document), cancellationToken).ConfigureAwait(false);
    }
}
