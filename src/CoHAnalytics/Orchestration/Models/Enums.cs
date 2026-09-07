namespace CoHAnalytics.Orchestration.Models;

public enum ContributorHealth
{
    Unknown,
    Ready,
    Degraded,
    Error,
    Unavailable
}

public enum ContributorActivity
{
    Inactive,
    Waiting,
    Active,
    ReceivingData,
    Paused
}

public enum ApplicationContributorLifecycleState
{
    Registered,
    Starting,
    Running,
    Stopping,
    Stopped,
    Faulted
}

public enum ApplicationContributorImportance
{
    Optional,
    Important,
    Critical
}

public enum ApplicationIssueSeverity
{
    Information,
    Warning,
    Error,
    Critical
}

public enum ApplicationFactScope
{
    Application,
    Provider,
    Account,
    Character,
    Session
}

public enum ApplicationFactConfidence
{
    Unknown,
    Inferred,
    Reported,
    Observed
}

public enum ApplicationActionTarget
{
    Contributor,
    Navigation,
    Command
}

public enum OverallApplicationState
{
    Unknown,
    Ready,
    Waiting,
    Monitoring,
    NeedsAttention,
    Degraded,
    Error
}

public enum CapabilityState
{
    Satisfied,
    SatisfiedDegraded,
    Unhealthy,
    Unknown,
    Stale,
    Missing
}

public enum SnapshotGenerationReason
{
    Initial,
    ContributorInvalidation,
    ExplicitRefresh,
    Startup,
    LifecycleChange,
    PeriodicSweep,
    DependencyEvaluation
}
