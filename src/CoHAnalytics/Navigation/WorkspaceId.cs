namespace CoHAnalytics.Navigation;

public enum WorkspaceId
{
    Dashboard,
    LiveSession,
    Accounts,
    Analytics,
    Reference,
    Diagnostics,

    /// <summary>
    /// Stable identity for the gated internal tooling workspace.
    /// Display title/label/icon are presentation metadata and may change without renaming this id.
    /// </summary>
    InternalTools,

    Settings
}
