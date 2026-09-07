namespace CoHAnalytics.Services;

/// <summary>
/// Single consumer-facing gate for internal/developer capabilities.
/// Only this service understands the hidden activation setting.
/// </summary>
public interface IInternalFeatureGate
{
    /// <summary>
    /// True when internal developer tooling may be exposed and developer-only
    /// service capabilities may run. Default is false.
    /// </summary>
    bool IsDeveloperMode { get; }
}
