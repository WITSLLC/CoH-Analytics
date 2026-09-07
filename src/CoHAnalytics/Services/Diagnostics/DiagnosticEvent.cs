using CoHAnalytics.Models;

namespace CoHAnalytics.Services.Diagnostics;

public enum DiagnosticChannel
{
    Standard
}

public enum DiagnosticCategory
{
    Application,
    RuntimeProcess,
    LogSource,
    Monitoring,
    Parser,
    GameplaySession,
    Diagnostics
}

public enum DiagnosticSeverity
{
    Information,
    Warning,
    Error,
    Critical
}

/// <summary>
/// Closed application diagnostic-event family. Common persistence-envelope fields are supplied
/// by <see cref="DiagnosticLogService"/> rather than by event producers.
/// </summary>
public abstract record DiagnosticEvent
{
    private protected DiagnosticEvent()
    {
    }

    internal abstract DiagnosticChannel Channel { get; }

    internal abstract DiagnosticCategory Category { get; }

    internal abstract DiagnosticSeverity Severity { get; }

    internal abstract string EventName { get; }
}

public sealed record ApplicationRunStartedDiagnosticEvent : DiagnosticEvent
{
    public required string ApplicationVersion { get; init; }

    public required int ProcessId { get; init; }

    internal override DiagnosticChannel Channel => DiagnosticChannel.Standard;

    internal override DiagnosticCategory Category => DiagnosticCategory.Application;

    internal override DiagnosticSeverity Severity => DiagnosticSeverity.Information;

    internal override string EventName => "ApplicationRunStarted";
}

public sealed record ApplicationStartupCompletedDiagnosticEvent : DiagnosticEvent
{
    internal override DiagnosticChannel Channel => DiagnosticChannel.Standard;

    internal override DiagnosticCategory Category => DiagnosticCategory.Application;

    internal override DiagnosticSeverity Severity => DiagnosticSeverity.Information;

    internal override string EventName => "ApplicationStartupCompleted";
}

public sealed record ApplicationStartupFailedDiagnosticEvent : DiagnosticEvent
{
    public required string ExceptionType { get; init; }

    public required int HResult { get; init; }

    public string? FailureCode { get; init; }

    internal override DiagnosticChannel Channel => DiagnosticChannel.Standard;

    internal override DiagnosticCategory Category => DiagnosticCategory.Application;

    internal override DiagnosticSeverity Severity => DiagnosticSeverity.Error;

    internal override string EventName => "ApplicationStartupFailed";
}

public sealed record ApplicationShutdownStartedDiagnosticEvent : DiagnosticEvent
{
    public int? ExitCode { get; init; }

    internal override DiagnosticChannel Channel => DiagnosticChannel.Standard;

    internal override DiagnosticCategory Category => DiagnosticCategory.Application;

    internal override DiagnosticSeverity Severity => DiagnosticSeverity.Information;

    internal override string EventName => "ApplicationShutdownStarted";
}

public sealed record ApplicationShutdownCompletedDiagnosticEvent : DiagnosticEvent
{
    internal override DiagnosticChannel Channel => DiagnosticChannel.Standard;

    internal override DiagnosticCategory Category => DiagnosticCategory.Application;

    internal override DiagnosticSeverity Severity => DiagnosticSeverity.Information;

    internal override string EventName => "ApplicationShutdownCompleted";
}

/// <summary>Logger-generated evidence that records were rejected before persistence.</summary>
public sealed record DiagnosticRecordsDroppedDiagnosticEvent : DiagnosticEvent
{
    public required long DroppedLogCount { get; init; }

    public required DateTimeOffset FirstDroppedAt { get; init; }

    public required DateTimeOffset LastDroppedAt { get; init; }

    internal override DiagnosticChannel Channel => DiagnosticChannel.Standard;

    internal override DiagnosticCategory Category => DiagnosticCategory.Diagnostics;

    internal override DiagnosticSeverity Severity => DiagnosticSeverity.Warning;

    internal override string EventName => "DiagnosticRecordsDropped";
}

public sealed record DiagnosticRuntimeClient
{
    public required int ProcessId { get; init; }

    public required DateTimeOffset ProcessStartTime { get; init; }
}

public sealed record DiagnosticLogSource
{
    public required string SourceId { get; init; }

    public required string AccountStableId { get; init; }

    public required string SourceFileName { get; init; }

    public required LogSourceActivityState ActivityState { get; init; }

    public required long Length { get; init; }

    public DateTimeOffset? LastGrowthAt { get; init; }
}

public sealed record DiagnosticMonitoringContext
{
    public required string ContextId { get; init; }

    public required MonitoringContextState State { get; init; }

    public string? AccountStableId { get; init; }

    public string? SourceId { get; init; }

    public string? SourceFileName { get; init; }

    public DiagnosticRuntimeClient? ProcessInstance { get; init; }

    public required long BindingGeneration { get; init; }
}

public enum MonitoringProcessBindingUnavailableReason
{
    MultipleRuntimeClients,
    NoProvenAssociation,
    RuntimeUnavailable
}

public sealed record DiagnosticParserWorker
{
    public required string WorkerId { get; init; }

    public required string ContextId { get; init; }

    public required ParserWorkerState State { get; init; }

    public string? SourceId { get; init; }

    public string? SourceFileName { get; init; }

    public string? SourceSegmentId { get; init; }

    public required long BindingGeneration { get; init; }
}

public sealed record ParserWorkerCreatedDiagnosticEvent : DiagnosticEvent
{
    public required string WorkerId { get; init; }

    public required string ContextId { get; init; }

    public string? SourceId { get; init; }

    public long? BindingGeneration { get; init; }

    public required ParserWorkerState State { get; init; }

    internal override DiagnosticChannel Channel => DiagnosticChannel.Standard;

    internal override DiagnosticCategory Category => DiagnosticCategory.Parser;

    internal override DiagnosticSeverity Severity => DiagnosticSeverity.Information;

    internal override string EventName => "Parser.WorkerCreated";
}

public sealed record ParserWorkerStateChangedDiagnosticEvent : DiagnosticEvent
{
    public required string WorkerId { get; init; }

    public required string ContextId { get; init; }

    public required ParserWorkerState PreviousState { get; init; }

    public required ParserWorkerState NextState { get; init; }

    public required string Reason { get; init; }

    internal override DiagnosticChannel Channel => DiagnosticChannel.Standard;

    internal override DiagnosticCategory Category => DiagnosticCategory.Parser;

    internal override DiagnosticSeverity Severity => DiagnosticSeverity.Information;

    internal override string EventName => "Parser.WorkerStateChanged";
}

public sealed record ParserWorkerBindingAppliedDiagnosticEvent : DiagnosticEvent
{
    public required string WorkerId { get; init; }

    public required string ContextId { get; init; }

    public string? SourceId { get; init; }

    public string? SourceSegmentId { get; init; }

    public required long BindingGeneration { get; init; }

    public required MonitoringSourceTransitionKind TransitionKind { get; init; }

    public long? StartingOffset { get; init; }

    internal override DiagnosticChannel Channel => DiagnosticChannel.Standard;

    internal override DiagnosticCategory Category => DiagnosticCategory.Parser;

    internal override DiagnosticSeverity Severity => DiagnosticSeverity.Information;

    internal override string EventName => "Parser.WorkerBindingApplied";
}

public sealed record ParserWorkerRemovedDiagnosticEvent : DiagnosticEvent
{
    public required string WorkerId { get; init; }

    public required string ContextId { get; init; }

    public required string Reason { get; init; }

    internal override DiagnosticChannel Channel => DiagnosticChannel.Standard;

    internal override DiagnosticCategory Category => DiagnosticCategory.Parser;

    internal override DiagnosticSeverity Severity => DiagnosticSeverity.Information;

    internal override string EventName => "Parser.WorkerRemoved";
}

public sealed record ParserWorkerFaultedDiagnosticEvent : DiagnosticEvent
{
    public required string WorkerId { get; init; }

    public required string ContextId { get; init; }

    public required string ExceptionType { get; init; }

    public required int HResult { get; init; }

    public required string FaultCode { get; init; }

    public required string Reason { get; init; }

    internal override DiagnosticChannel Channel => DiagnosticChannel.Standard;

    internal override DiagnosticCategory Category => DiagnosticCategory.Parser;

    internal override DiagnosticSeverity Severity => DiagnosticSeverity.Error;

    internal override string EventName => "Parser.WorkerFaulted";
}

public sealed record ParserIdentityEvidenceClassifiedDiagnosticEvent : DiagnosticEvent
{
    public required string WorkerId { get; init; }

    public required string ContextId { get; init; }

    public required long ParserSequence { get; init; }

    public required ParserEventKind ParserEventKind { get; init; }

    public required ParserStructuralEvidenceKind EvidenceKind { get; init; }

    public required string CandidateCharacterName { get; init; }

    public string? AccountStableId { get; init; }

    public required string SourceId { get; init; }

    public required string SourceSegmentId { get; init; }

    public required long BindingGeneration { get; init; }

    internal override DiagnosticChannel Channel => DiagnosticChannel.Standard;

    internal override DiagnosticCategory Category => DiagnosticCategory.Parser;

    internal override DiagnosticSeverity Severity => DiagnosticSeverity.Information;

    internal override string EventName => "Parser.IdentityEvidenceClassified";
}

public enum GameplayWelcomeProcessingResult
{
    Accepted,
    ReplacedSession,
    RecoveredSession,
    Unbound,
    IgnoredStoppedContext,
    Rejected
}

public sealed record GameplaySessionWelcomeProcessedDiagnosticEvent : DiagnosticEvent
{
    public required string ContextId { get; init; }

    public required long ParserSequence { get; init; }

    public string? AccountStableId { get; init; }

    public required string CandidateCharacterName { get; init; }

    public string? ExistingSessionId { get; init; }

    public required GameplayWelcomeProcessingResult Result { get; init; }

    public required string Reason { get; init; }

    public string? ResultingSessionId { get; init; }

    internal override DiagnosticChannel Channel => DiagnosticChannel.Standard;

    internal override DiagnosticCategory Category => DiagnosticCategory.GameplaySession;

    internal override DiagnosticSeverity Severity =>
        Result is GameplayWelcomeProcessingResult.Unbound or GameplayWelcomeProcessingResult.Rejected
            ? DiagnosticSeverity.Warning
            : DiagnosticSeverity.Information;

    internal override string EventName => "GameplaySession.WelcomeProcessed";
}

public sealed record MonitoringContextCreatedDiagnosticEvent : DiagnosticEvent
{
    public required string ContextId { get; init; }

    public string? AccountStableId { get; init; }

    public string? SourceId { get; init; }

    public string? SourceFileName { get; init; }

    public required MonitoringContextState State { get; init; }

    public required string Reason { get; init; }

    internal override DiagnosticChannel Channel => DiagnosticChannel.Standard;

    internal override DiagnosticCategory Category => DiagnosticCategory.Monitoring;

    internal override DiagnosticSeverity Severity => DiagnosticSeverity.Information;

    internal override string EventName => "Monitoring.ContextCreated";
}

public sealed record MonitoringContextStateChangedDiagnosticEvent : DiagnosticEvent
{
    public required string ContextId { get; init; }

    public required MonitoringContextState PreviousState { get; init; }

    public required MonitoringContextState NextState { get; init; }

    public required string Reason { get; init; }

    internal override DiagnosticChannel Channel => DiagnosticChannel.Standard;

    internal override DiagnosticCategory Category => DiagnosticCategory.Monitoring;

    internal override DiagnosticSeverity Severity => DiagnosticSeverity.Information;

    internal override string EventName => "Monitoring.ContextStateChanged";
}

public sealed record MonitoringContextRetiredDiagnosticEvent : DiagnosticEvent
{
    public required string ContextId { get; init; }

    public string? AccountStableId { get; init; }

    public string? SourceId { get; init; }

    public required string Reason { get; init; }

    internal override DiagnosticChannel Channel => DiagnosticChannel.Standard;

    internal override DiagnosticCategory Category => DiagnosticCategory.Monitoring;

    internal override DiagnosticSeverity Severity => DiagnosticSeverity.Information;

    internal override string EventName => "Monitoring.ContextRetired";
}

public sealed record MonitoringContextSourceBindingChangedDiagnosticEvent : DiagnosticEvent
{
    public required string ContextId { get; init; }

    public string? PreviousSourceId { get; init; }

    public string? NextSourceId { get; init; }

    public string? PreviousSourceFileName { get; init; }

    public string? NextSourceFileName { get; init; }

    public required long BindingGeneration { get; init; }

    public required string Reason { get; init; }

    internal override DiagnosticChannel Channel => DiagnosticChannel.Standard;

    internal override DiagnosticCategory Category => DiagnosticCategory.Monitoring;

    internal override DiagnosticSeverity Severity => DiagnosticSeverity.Information;

    internal override string EventName => "Monitoring.ContextSourceBindingChanged";
}

public sealed record MonitoringContextProcessBindingChangedDiagnosticEvent : DiagnosticEvent
{
    public required string ContextId { get; init; }

    public DiagnosticRuntimeClient? PreviousProcess { get; init; }

    public DiagnosticRuntimeClient? NextProcess { get; init; }

    public required string Reason { get; init; }

    internal override DiagnosticChannel Channel => DiagnosticChannel.Standard;

    internal override DiagnosticCategory Category => DiagnosticCategory.Monitoring;

    internal override DiagnosticSeverity Severity => DiagnosticSeverity.Information;

    internal override string EventName => "Monitoring.ContextProcessBindingChanged";
}

public sealed record MonitoringContextProcessBindingUnavailableDiagnosticEvent : DiagnosticEvent
{
    public required string ContextId { get; init; }

    public required MonitoringProcessBindingUnavailableReason Reason { get; init; }

    internal override DiagnosticChannel Channel => DiagnosticChannel.Standard;

    internal override DiagnosticCategory Category => DiagnosticCategory.Monitoring;

    internal override DiagnosticSeverity Severity => DiagnosticSeverity.Warning;

    internal override string EventName => "Monitoring.ContextProcessBindingUnavailable";
}

public sealed record MonitoringContextAccountBoundDiagnosticEvent : DiagnosticEvent
{
    public required string ContextId { get; init; }

    public string? PreviousAccountStableId { get; init; }

    public string? NextAccountStableId { get; init; }

    public required string Reason { get; init; }

    internal override DiagnosticChannel Channel => DiagnosticChannel.Standard;

    internal override DiagnosticCategory Category => DiagnosticCategory.Monitoring;

    internal override DiagnosticSeverity Severity => DiagnosticSeverity.Information;

    internal override string EventName => "Monitoring.ContextAccountBound";
}

public sealed record RuntimeClientSetChangedDiagnosticEvent : DiagnosticEvent
{
    public required GameRuntimeStatus PreviousStatus { get; init; }

    public required GameRuntimeStatus NextStatus { get; init; }

    public required IReadOnlyList<DiagnosticRuntimeClient> PreviousClients { get; init; }

    public required IReadOnlyList<DiagnosticRuntimeClient> NextClients { get; init; }

    internal override DiagnosticChannel Channel => DiagnosticChannel.Standard;

    internal override DiagnosticCategory Category => DiagnosticCategory.RuntimeProcess;

    internal override DiagnosticSeverity Severity => DiagnosticSeverity.Information;

    internal override string EventName => "Runtime.ClientSetChanged";
}

public sealed record RuntimeStatusChangedDiagnosticEvent : DiagnosticEvent
{
    public required GameRuntimeStatus PreviousStatus { get; init; }

    public required GameRuntimeStatus NextStatus { get; init; }

    internal override DiagnosticChannel Channel => DiagnosticChannel.Standard;

    internal override DiagnosticCategory Category => DiagnosticCategory.RuntimeProcess;

    internal override DiagnosticSeverity Severity => DiagnosticSeverity.Information;

    internal override string EventName => "Runtime.RuntimeStatusChanged";
}

public sealed record LogActivitySourceDiscoveredDiagnosticEvent : DiagnosticEvent
{
    public required string SourceId { get; init; }

    public required string AccountStableId { get; init; }

    public required string SourceFileName { get; init; }

    public required LogSourceActivityState ActivityState { get; init; }

    public required long Length { get; init; }

    public DateTimeOffset? LastGrowthAt { get; init; }

    internal override DiagnosticChannel Channel => DiagnosticChannel.Standard;

    internal override DiagnosticCategory Category => DiagnosticCategory.LogSource;

    internal override DiagnosticSeverity Severity => DiagnosticSeverity.Information;

    internal override string EventName => "LogActivity.SourceDiscovered";
}

public sealed record LogActivitySourceStateChangedDiagnosticEvent : DiagnosticEvent
{
    public required string SourceId { get; init; }

    public required string AccountStableId { get; init; }

    public required string SourceFileName { get; init; }

    public required LogSourceActivityState PreviousActivityState { get; init; }

    public required LogSourceActivityState NextActivityState { get; init; }

    public required long PreviousLength { get; init; }

    public required long Length { get; init; }

    public DateTimeOffset? LastGrowthAt { get; init; }

    public required string Reason { get; init; }

    internal override DiagnosticChannel Channel => DiagnosticChannel.Standard;

    internal override DiagnosticCategory Category => DiagnosticCategory.LogSource;

    internal override DiagnosticSeverity Severity => DiagnosticSeverity.Information;

    internal override string EventName => "LogActivity.SourceStateChanged";
}

public sealed record LogActivitySourceReplacedDiagnosticEvent : DiagnosticEvent
{
    public required string PreviousSourceId { get; init; }

    public required string SourceId { get; init; }

    public required string AccountStableId { get; init; }

    public required string SourceFileName { get; init; }

    public required LogSourceActivityState PreviousActivityState { get; init; }

    public required LogSourceActivityState NextActivityState { get; init; }

    public required long PreviousLength { get; init; }

    public required long Length { get; init; }

    public required string Reason { get; init; }

    internal override DiagnosticChannel Channel => DiagnosticChannel.Standard;

    internal override DiagnosticCategory Category => DiagnosticCategory.LogSource;

    internal override DiagnosticSeverity Severity => DiagnosticSeverity.Information;

    internal override string EventName => "LogActivity.SourceReplaced";
}

public sealed record LogActivitySourceRolloverDiagnosticEvent : DiagnosticEvent
{
    public required string SourceId { get; init; }

    public required string PredecessorSourceId { get; init; }

    public required string AccountStableId { get; init; }

    public required string SourceFileName { get; init; }

    public required LogSourceActivityState ActivityState { get; init; }

    public required long Length { get; init; }

    public DateTimeOffset? LastGrowthAt { get; init; }

    public required string Reason { get; init; }

    internal override DiagnosticChannel Channel => DiagnosticChannel.Standard;

    internal override DiagnosticCategory Category => DiagnosticCategory.LogSource;

    internal override DiagnosticSeverity Severity => DiagnosticSeverity.Information;

    internal override string EventName => "LogActivity.SourceRollover";
}

public sealed record LogActivitySourceLostDiagnosticEvent : DiagnosticEvent
{
    public required string SourceId { get; init; }

    public required string AccountStableId { get; init; }

    public required string SourceFileName { get; init; }

    public required LogSourceActivityState PreviousActivityState { get; init; }

    public required LogSourceActivityState NextActivityState { get; init; }

    public required long Length { get; init; }

    public DateTimeOffset? LastGrowthAt { get; init; }

    public required string Reason { get; init; }

    internal override DiagnosticChannel Channel => DiagnosticChannel.Standard;

    internal override DiagnosticCategory Category => DiagnosticCategory.LogSource;

    internal override DiagnosticSeverity Severity => DiagnosticSeverity.Warning;

    internal override string EventName => "LogActivity.SourceLost";
}

public sealed record DiagnosticsStateSnapshotCapturedDiagnosticEvent : DiagnosticEvent
{
    public string Reason { get; init; } = "RuntimeClientSetChanged";

    public required IReadOnlyList<DiagnosticRuntimeClient> RunningClients { get; init; }

    public required IReadOnlyList<DiagnosticLogSource> LogSources { get; init; }

    public IReadOnlyList<DiagnosticMonitoringContext> MonitoringContexts { get; init; } = [];

    public IReadOnlyList<DiagnosticParserWorker> ParserWorkers { get; init; } = [];

    internal override DiagnosticChannel Channel => DiagnosticChannel.Standard;

    internal override DiagnosticCategory Category => DiagnosticCategory.Diagnostics;

    internal override DiagnosticSeverity Severity => DiagnosticSeverity.Information;

    internal override string EventName => "Diagnostics.StateSnapshotCaptured";
}
