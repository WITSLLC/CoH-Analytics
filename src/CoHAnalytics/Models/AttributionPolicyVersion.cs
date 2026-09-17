namespace CoHAnalytics.Models;

/// <summary>Versions Direct/BuildConfirmed/Correlated/Unattributed matching rules.</summary>
public static class AttributionPolicyVersion
{
    /// <summary>
    /// BuildConfirmed requires one frozen slot occurrence of the exact logged proc identity.
    /// Correlated is structurally supported and not emitted. Capture-time stale-build
    /// detection is not implemented.
    /// </summary>
    /// <remarks>
    /// Version 2 recognises every catalogued damage-proc identity. Version 1 admitted a single
    /// hardcoded identity, so its BuildConfirmed/Unattributed split is not comparable with 2.
    /// </remarks>
    public const int Current = 2;
}
