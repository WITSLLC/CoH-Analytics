namespace CoHAnalytics.Orchestration;

public enum CycleValidationMode
{
    /// <summary>Throws on required-dependency cycles (DEBUG default).</summary>
    Throw,

    /// <summary>Rejects participating contributors and records diagnostics (RELEASE default).</summary>
    Degrade
}

public sealed class ApplicationOrchestratorOptions
{
    public TimeSpan DebounceInterval { get; init; } = TimeSpan.FromMilliseconds(250);

    public TimeSpan DefaultPullTimeout { get; init; } = TimeSpan.FromSeconds(2);

    public TimeSpan ShutdownWaitTimeout { get; init; } = TimeSpan.FromSeconds(1);

    public int EventHistoryCapacity { get; init; } = 200;

    public CycleValidationMode CycleValidationMode { get; init; } =
#if DEBUG
        CycleValidationMode.Throw;
#else
        CycleValidationMode.Degrade;
#endif

    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;
}
