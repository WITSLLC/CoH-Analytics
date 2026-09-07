namespace CoHAnalytics.Replay;

public enum ReplayProfileName
{
    Baseline,
    FixedRate,
    Burst,
    Maximum,
    DualClient,
    Rollover,
    ExpectedOverload
}

public enum ReplayProfileOutcome
{
    Passed,
    CorrectnessFailed,
    ExpectedOverloadObserved,
    ExpectedOverloadNotObserved,
    UnexpectedOverload,
    HarnessFailed,
    Cancelled
}

public enum ReplayExpectedOverloadBehavior
{
    None,
    GameplayWorkQueue
}

public enum ReplayExpectedCorrectnessBehavior
{
    MustPass,
    AcceptExpectedOverloadOnly
}

public enum ReplayContextRateMode
{
    Maximum,
    FixedLines
}

public sealed record ReplayContextRate(ReplayContextRateMode Mode, double LinesPerSecond = 0);

public sealed class ReplayBurstSettings
{
    public required int LinesPerBurst { get; init; }

    public int? BytesPerBurst { get; init; }

    public required int BurstCount { get; init; }

    public required TimeSpan QuietInterval { get; init; }

    public TimeSpan? BurstDuration { get; init; }
}

public sealed record ReplayStressWorkloadDefinition
{
    public required int LineCount { get; init; }

    public required int Seed { get; init; }

    public required int FanOut { get; init; }

    public required int ContextId { get; init; }

    public bool IncludeWelcome { get; init; } = true;

    public int SegmentCount { get; init; } = 1;

    public bool EnableLatencyMarkers { get; init; }

    public int LatencyMarkerEveryLines { get; init; } = 100;
}

public sealed record ReplayProfile
{
    public required string Name { get; init; }

    public required int ContextCount { get; init; }

    public required ReplayTimingMode TimingMode { get; init; }

    public required ReplayChunkMode ChunkMode { get; init; }

    public double? LinesPerSecond { get; init; }

    public ReplayBurstSettings? BurstSettings { get; init; }

    public required ReplayStressWorkloadDefinition SourceWorkload { get; init; }

    public required ReplayExpectedOverloadBehavior ExpectedOverloadBehavior { get; init; }

    public required ReplayExpectedCorrectnessBehavior ExpectedCorrectnessBehavior { get; init; }

    public required int DefaultDurationLines { get; init; }

    public required bool LatencyMarkersEnabled { get; init; }

    public required bool PerformanceSamplingEnabled { get; init; }

    public ReplayInputMode InputMode { get; init; } = ReplayInputMode.Bootstrap;

    public bool UseConcurrentContexts { get; init; }

    public IReadOnlyList<ReplayContextRate>? ContextRates { get; init; }

    public int? GameplayWorkQueueCapacity { get; init; }

    public int? GameplayMaxPreStartBufferCapacity { get; init; }

    public int? ExpectedOverloadAdmissionLineCount { get; init; }

    public int MinChunkBytes { get; init; } = 1;

    public int MaxChunkBytes { get; init; } = 4096;

    public int FixedChunkBytes { get; init; } = 1024;

    public int Seed { get; init; } = 12345;
}

public sealed class ReplayProfileOverrides
{
    public double? RateLinesPerSecond { get; init; }

    public int? BurstLines { get; init; }

    public int? BurstCount { get; init; }

    public int? BurstQuietMs { get; init; }

    public int? BurstBytes { get; init; }

    public int? StressLines { get; init; }

    public int? StressSeed { get; init; }

    public int? StressFanOut { get; init; }

    public bool? LatencyMarkers { get; init; }

    public int? LatencyMarkerEveryLines { get; init; }

    public string? ContextARate { get; init; }

    public string? ContextBRate { get; init; }

    public bool? PerformanceSampling { get; init; }
}

public sealed class ReplayProfileResolution
{
    public required ReplayProfile Profile { get; init; }

    public required IReadOnlyDictionary<string, string> EffectiveOptions { get; init; }

    public required IReadOnlyList<ReplayProfileContextPlan> ContextPlans { get; init; }

    public double? RequestedRateLinesPerSecond { get; init; }

    public ReplayExpectedOverloadBehavior ExpectedOverloadTarget { get; init; }
}

public sealed class ReplayProfileContextPlan
{
    public required string Label { get; init; }

    public required ReplayPlan Plan { get; init; }

    public required ReplayStressWorkloadDefinition Workload { get; init; }

    public required IReadOnlyList<string> GeneratedSourcePaths { get; init; }

    public string? StressWorkloadHash { get; init; }

    public double? RequestedLinesPerSecond { get; init; }
}

public sealed class ReplayProfileObservationReport
{
    public const int SchemaVersionValue = 1;

    public required string ProfileName { get; init; }

    public required IReadOnlyDictionary<string, string> EffectiveOptions { get; init; }

    public required int ContextCount { get; init; }

    public double? RequestedRateLinesPerSecond { get; init; }

    public double? AchievedRateLinesPerSecond { get; init; }

    public ReplayBurstSettings? BurstConfiguration { get; init; }

    public int? StressSeed { get; init; }

    public long? GeneratedLineCount { get; init; }

    public ReplayExpectedOverloadBehavior ExpectedOverloadTarget { get; init; }

    public required ReplayProfileOutcome Outcome { get; init; }

    public long? PacingUnderrunCount { get; init; }

    public double? PacingOverrunMilliseconds { get; init; }

    public IReadOnlyList<ReplayContextSummaryReport>? Contexts { get; init; }

    public ReplayLatencyAggregateReport? Latency { get; init; }

    public string? OverloadDiagnosticsSummary { get; init; }
}

public sealed class ReplayContextSummaryReport
{
    public const int SchemaVersionValue = 1;

    public required string Label { get; init; }

    public required long SourceLines { get; init; }

    public required long SourceBytes { get; init; }

    public double? AchievedRateLinesPerSecond { get; init; }

    public long ParserObservations { get; init; }

    public long GameplayObservations { get; init; }

    public int? QueuePeakDepth { get; init; }

    public ReplayLatencyAggregateReport? Latency { get; init; }

    public bool? CorrectnessPassed { get; init; }
}

public sealed class ReplayLatencyAggregateReport
{
    public const int SchemaVersionValue = 1;

    public long Count { get; init; }

    public double? P50Milliseconds { get; init; }

    public double? P95Milliseconds { get; init; }

    public double? P99Milliseconds { get; init; }

    public double? MaxMilliseconds { get; init; }

    public long? ParserCount { get; init; }

    public double? ParserP50Milliseconds { get; init; }

    public double? ParserP95Milliseconds { get; init; }

    public double? ParserP99Milliseconds { get; init; }

    public double? ParserMaxMilliseconds { get; init; }

    public long? GameplayCount { get; init; }

    public double? GameplayP50Milliseconds { get; init; }

    public double? GameplayP95Milliseconds { get; init; }

    public double? GameplayP99Milliseconds { get; init; }

    public double? GameplayMaxMilliseconds { get; init; }
}
