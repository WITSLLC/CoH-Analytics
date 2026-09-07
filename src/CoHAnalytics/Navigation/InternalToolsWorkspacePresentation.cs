namespace CoHAnalytics.Navigation;

/// <summary>
/// Display metadata for the gated internal tooling workspace.
/// Availability is controlled by <c>IInternalFeatureGate</c>; this type only owns presentation.
/// Title, sidebar label, and icon may change later without redesigning navigation architecture.
/// </summary>
public static class InternalToolsWorkspacePresentation
{
    public const string SidebarLabel = "DEVELOPER TOOLS";

    public const string Title = "Developer Tools";

    public const string Description =
        "Internal utilities for catalog maintenance, reference-data curation, and developer workflows.";

    public const string IconPath = "Assets/Images/Navigation/settings.png";
}