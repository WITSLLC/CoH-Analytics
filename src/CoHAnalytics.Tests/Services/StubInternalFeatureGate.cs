using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Services;

internal sealed class StubInternalFeatureGate : IInternalFeatureGate
{
    public StubInternalFeatureGate(bool isDeveloperMode) => IsDeveloperMode = isDeveloperMode;

    public static StubInternalFeatureGate Enabled { get; } = new(true);

    public static StubInternalFeatureGate Disabled { get; } = new(false);

    public bool IsDeveloperMode { get; }
}
