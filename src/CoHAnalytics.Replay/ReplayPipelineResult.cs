using CoHAnalytics.Models;
using CoHAnalytics.Services;

namespace CoHAnalytics.Replay;

public sealed record ReplayCorrectnessFailure(string Code, string Message);

public sealed class ReplayCorrectnessReport
{
    public const int SchemaVersionValue = 1;

    public bool Passed { get; init; }

    public long RawEventsObserved { get; init; }

    public long ClassifiedEventsObserved { get; init; }

    public long CommittedGameplayEventsObserved { get; init; }

    public int ContextCount { get; init; }

    public int WelcomeBoundariesObserved { get; init; }

    public bool BeginsMidSession { get; init; }

    public IReadOnlyList<string> ClassificationRuleIds { get; init; } = [];

    public IReadOnlyList<ReplayCorrectnessFailure> Failures { get; init; } = [];

    public int LateObservationCount { get; init; }
}

public sealed class ReplayParserObservationReport
{
    public const int SchemaVersionValue = 1;

    public long RawEventsObserved { get; init; }

    public long ClassifiedEventsObserved { get; init; }

    public long TotalLinesProcessed { get; init; }

    public long ExpectedCompleteLines { get; init; }

    public int WorkerCount { get; init; }

    public bool ParserDrained { get; init; }
}

public sealed class ReplayGameplayObservationReport
{
    public const int SchemaVersionValue = 1;

    public long CommittedEventsObserved { get; init; }

    public int ActiveSessionCount { get; init; }

    public bool GameplayDrained { get; init; }

    public int FailedContextCount { get; init; }

    public long TotalCommittedEvents { get; init; }

    public int MonitoringContextCount { get; init; }
}

public sealed class ReplayPipelineResult
{
    public bool Success => Correctness.Passed;

    public required ReplayCorrectnessReport Correctness { get; init; }

    public required ReplayParserObservationReport Parser { get; init; }

    public required ReplayGameplayObservationReport Gameplay { get; init; }

    public ReplayPerformanceObservationReport? Performance { get; init; }

    public ReplayProfileObservationReport? Profile { get; init; }
}

public sealed record ReplayContextBinding(ReplayWorkspace Workspace, ReplayPlan Plan);

public sealed class ReplayPipelineRequest
{
    public required IReadOnlyList<ReplayContextBinding> Contexts { get; init; }

    public ReplayProfileResolution? ProfileResolution { get; init; }

    public GameplaySessionOptions? GameplaySessionOptions { get; init; }

    public ReplayLatencyTracker? LatencyTracker { get; init; }
}
