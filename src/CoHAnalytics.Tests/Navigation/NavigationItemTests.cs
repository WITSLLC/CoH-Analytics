using CoHAnalytics.Navigation;
using CoHAnalytics.Themes;

namespace CoHAnalytics.Tests.Navigation;

public sealed class NavigationItemTests
{
    [Fact]
    public void Default_navigation_matches_approved_destination_order_when_developer_mode_is_off()
    {
        var items = NavigationItem.CreateDefaultNavigation(ThemeId.Hero);

        Assert.Collection(
            items,
            item => AssertDestination(item, WorkspaceId.Dashboard, "DASHBOARD"),
            item => AssertDestination(item, WorkspaceId.LiveSession, "LIVE SESSION"),
            item => AssertDestination(item, WorkspaceId.Accounts, "ACCOUNTS"),
            item => AssertDestination(item, WorkspaceId.Analytics, "ANALYTICS"),
            item => AssertDestination(item, WorkspaceId.Reference, "REFERENCE"));

        Assert.DoesNotContain(items, item => item.WorkspaceId == WorkspaceId.InternalTools);
        Assert.DoesNotContain(items, item => item.WorkspaceId == WorkspaceId.Diagnostics);
        Assert.DoesNotContain(items, item => item.WorkspaceId == WorkspaceId.Settings);
    }

    [Fact]
    public void Developer_mode_appends_internal_tools_using_presentation_metadata()
    {
        var items = NavigationItem.CreateDefaultNavigation(ThemeId.Hero, includeInternalTools: true);

        Assert.Collection(
            items,
            item => AssertDestination(item, WorkspaceId.Dashboard, "DASHBOARD"),
            item => AssertDestination(item, WorkspaceId.LiveSession, "LIVE SESSION"),
            item => AssertDestination(item, WorkspaceId.Accounts, "ACCOUNTS"),
            item => AssertDestination(item, WorkspaceId.Analytics, "ANALYTICS"),
            item => AssertDestination(item, WorkspaceId.Reference, "REFERENCE"),
            item => AssertDestination(
                item,
                WorkspaceId.InternalTools,
                InternalToolsWorkspacePresentation.SidebarLabel));

        var internalTools = items.Single(item => item.WorkspaceId == WorkspaceId.InternalTools);
        Assert.Equal(InternalToolsWorkspacePresentation.IconPath, internalTools.IconPath);
        Assert.NotEqual("InternalTools", internalTools.Label);
        Assert.DoesNotContain(items, item => item.WorkspaceId == WorkspaceId.Diagnostics);
        Assert.DoesNotContain(items, item => item.WorkspaceId == WorkspaceId.Settings);
    }

    private static void AssertDestination(
        NavigationItem item,
        WorkspaceId workspaceId,
        string label)
    {
        Assert.False(item.IsDivider);
        Assert.Equal(workspaceId, item.WorkspaceId);
        Assert.Equal(label, item.Label);
        Assert.False(string.IsNullOrWhiteSpace(item.IconPath));
    }
}
