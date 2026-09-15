namespace CoHAnalytics.Models;

/// <summary>Versions Direct/BuildConfirmed/Correlated/Unattributed matching rules.</summary>
public static class AttributionPolicyVersion
{
    /// <summary>
    /// BuildConfirmed requires one frozen slot occurrence of the exact logged proc identity.
    /// Correlated is structurally supported and not emitted. Capture-time stale-build
    /// detection is not implemented.
    /// </summary>
    public const int Current = 1;
}
