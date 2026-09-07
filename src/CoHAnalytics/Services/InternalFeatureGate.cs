namespace CoHAnalytics.Services;

/// <summary>
/// Resolves the hidden internal feature gate from persisted settings.
/// Activation value and setting name are intentionally opaque and not exposed in UI.
/// Restart is required after changing the underlying setting.
/// </summary>
public sealed class InternalFeatureGate : IInternalFeatureGate
{
    private const string DeveloperModeToken = "Ellie";

    public InternalFeatureGate(SettingsService settingsService)
    {
        ArgumentNullException.ThrowIfNull(settingsService);
        var settings = settingsService.Load();
        IsDeveloperMode = string.Equals(settings.AngiesView, DeveloperModeToken, StringComparison.Ordinal);
    }

    public bool IsDeveloperMode { get; }
}
