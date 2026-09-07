namespace CoHAnalytics.Services;

/// <summary>
/// Transient, presentation-only account-name masking for safe screenshots and recordings.
/// This state intentionally is not part of persisted application settings.
/// Enabling masking is a developer-only capability and requires <see cref="IInternalFeatureGate.IsDeveloperMode"/>.
/// </summary>
public sealed class AccountAnonymityService
{
    private const char MaskCharacter = '\u2588';
    private readonly IInternalFeatureGate _featureGate;

    public AccountAnonymityService(IInternalFeatureGate? featureGate = null)
    {
        // Default denies developer capability so accidental construction cannot bypass the gate.
        _featureGate = featureGate ?? DisabledInternalFeatureGate.Instance;
    }

    public event EventHandler? Changed;

    public bool IsEnabled { get; private set; }

    public void SetEnabled(bool enabled)
    {
        if (enabled && !_featureGate.IsDeveloperMode)
        {
            enabled = false;
        }

        if (IsEnabled == enabled)
        {
            return;
        }

        IsEnabled = enabled;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public string MaskForPresentation(string? accountName, string fallback = "Unknown account")
    {
        if (string.IsNullOrWhiteSpace(accountName))
        {
            return fallback;
        }

        return IsEnabled
            ? new string(MaskCharacter, accountName.Length)
            : accountName;
    }

    private sealed class DisabledInternalFeatureGate : IInternalFeatureGate
    {
        public static DisabledInternalFeatureGate Instance { get; } = new();

        public bool IsDeveloperMode => false;
    }
}
