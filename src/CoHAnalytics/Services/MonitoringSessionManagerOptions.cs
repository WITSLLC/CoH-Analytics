namespace CoHAnalytics.Services;

/// <summary>
/// Code-defined options for <see cref="MonitoringSessionManager"/>.
/// </summary>
/// <remarks>
/// The manager is event-driven rather than polling, so there is no interval to configure here.
/// This exists so tests can inject a deterministic <see cref="TimeProvider"/>.
/// </remarks>
public sealed class MonitoringSessionManagerOptions
{
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;
}
