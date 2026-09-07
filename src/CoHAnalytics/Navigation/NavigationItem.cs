using CoHAnalytics.Themes;

namespace CoHAnalytics.Navigation;

public sealed class NavigationItem
{
    public required WorkspaceId WorkspaceId { get; init; }

    public required string Label { get; init; }

    public required string IconPath { get; init; }

    public bool IsDivider { get; init; }

    /// <summary>
    /// Builds the left-navigation destinations.
    /// When <paramref name="includeInternalTools"/> is true, the gated internal tools
    /// workspace is appended using presentation metadata from
    /// <see cref="InternalToolsWorkspacePresentation"/>.
    /// </summary>
    public static IReadOnlyList<NavigationItem> CreateDefaultNavigation(
        ThemeId themeId,
        bool includeInternalTools = false)
    {
        _ = themeId;

        var items = new List<NavigationItem>
        {
            new NavigationItem
            {
                WorkspaceId = WorkspaceId.Dashboard,
                Label = "DASHBOARD",
                IconPath = "Assets/Images/Navigation/dashboard.png"
            },
            new NavigationItem
            {
                WorkspaceId = WorkspaceId.LiveSession,
                Label = "LIVE SESSION",
                IconPath = "Assets/Images/Navigation/live-session.png"
            },
            new NavigationItem
            {
                WorkspaceId = WorkspaceId.Accounts,
                Label = "ACCOUNTS",
                IconPath = "Assets/Images/Panels/accounts.png"
            },
            new NavigationItem
            {
                WorkspaceId = WorkspaceId.Analytics,
                Label = "ANALYTICS",
                IconPath = "Assets/Images/Navigation/combat.png"
            },
            new NavigationItem
            {
                WorkspaceId = WorkspaceId.Reference,
                Label = "REFERENCE",
                IconPath = "Assets/Images/Navigation/reference.png"
            }
        };

        if (includeInternalTools)
        {
            items.Add(
                new NavigationItem
                {
                    WorkspaceId = WorkspaceId.InternalTools,
                    Label = InternalToolsWorkspacePresentation.SidebarLabel,
                    IconPath = InternalToolsWorkspacePresentation.IconPath
                });
        }

        return items;
    }
}
