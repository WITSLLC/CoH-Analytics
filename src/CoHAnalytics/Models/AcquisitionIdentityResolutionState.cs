namespace CoHAnalytics.Models;

/// <summary>
/// Describes how completely an observed acquisition is understood by the app-owned catalog.
/// Player-facing presentation and canonical identity resolution are intentionally independent.
/// </summary>
public enum AcquisitionIdentityResolutionState
{
    Resolved,
    PresentedUnresolved,
    Unresolved
}
