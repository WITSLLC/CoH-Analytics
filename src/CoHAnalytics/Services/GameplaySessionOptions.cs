namespace CoHAnalytics.Services;

/// <summary>Code-defined options for <see cref="GameplaySessionManager"/>.</summary>
public sealed class GameplaySessionOptions
{
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;

    public int MaxRetainedEventCount { get; init; } = 128;

    public long MaxRetainedPayloadBytes { get; init; } = 64 * 1024;

    public int WorkQueueCapacity { get; init; } = 512;

    public int CommittedEventBatchSize { get; init; } = 64;

    public int MaxPendingCommittedEvents { get; init; } = 4096;

    public int MaxRecentDiagnosticsEntries { get; init; } = 50;

    public int MaxPreStartBufferCapacity { get; init; } = 256;

    public int MaxIdentityEvidenceCountPerContext { get; init; } = 32;

    public int MaxCandidateCountPerContext { get; init; } = 16;

    public int MaxRecentSessionRewards { get; init; } = 30;

    public int MaxRetainedCombatEvents { get; init; } = 1024;

  public TimeSpan CombatSnapshotPublishInterval { get; init; } = TimeSpan.FromSeconds(1);

    public TimeSpan CombatIdleThreshold { get; init; } = CombatActivityDefaults.IdleThreshold;

    internal GameplaySessionTestHooks? TestHooks { get; init; }
}

internal sealed class GameplaySessionTestHooks
{
    public Action? BeforeFirstStartCaptureLock { get; init; }

    public Action? BeforeProcessWorkItem { get; init; }

    public Action? AfterCommandAdmission { get; init; }

    public Action? BeforeEpochResourceRelease { get; init; }

    public Action? AfterEpochFinalization { get; init; }

    public Action? OnSnapshotPublished { get; init; }
}
