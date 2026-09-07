namespace CoHAnalytics.Services;

/// <summary>
/// Code-defined options for <see cref="LogActivityService"/>.
/// </summary>
/// <remarks>
/// These are deliberately not user settings. They exist so tests can make observation
/// deterministic, and so the intervals are tunable in code without touching persisted state.
/// </remarks>
public sealed class LogActivityServiceOptions
{
    public static TimeSpan DefaultPollInterval { get; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// How long a source that previously grew may go without further observed growth before it
    /// is reported as inactive. This is an observation threshold only. It is not a chat-logging
    /// timeout and says nothing about whether in-game logging is enabled.
    /// </summary>
    public static TimeSpan DefaultInactivityThreshold { get; } = TimeSpan.FromSeconds(30);

    public TimeSpan PollInterval { get; init; } = DefaultPollInterval;

    public TimeSpan InactivityThreshold { get; init; } = DefaultInactivityThreshold;

    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;

    /// <summary>
    /// Whether the periodic scan timer runs. Tests disable it and drive
    /// <see cref="LogActivityService.ScanAsync"/> directly.
    /// </summary>
    public bool EnablePolling { get; init; } = true;

    /// <summary>Maximum number of recent scan failure messages retained for diagnostics.</summary>
    public int RetainedScanFailureCount { get; init; } = 20;
}
