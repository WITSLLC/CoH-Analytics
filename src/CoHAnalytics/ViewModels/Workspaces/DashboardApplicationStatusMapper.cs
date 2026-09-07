using CoHAnalytics.Orchestration.Models;

namespace CoHAnalytics.ViewModels.Workspaces;

internal static class DashboardApplicationStatusMapper
{
    public sealed record Presentation(
        string AppStatusHeadline,
        string AppStatusDetail,
        string AppStatusSecondaryText,
        DashboardStatusKind AppStatusKind,
        string MonitoringBannerHeadline,
        string MonitoringBannerMessage,
        DashboardStatusKind MonitoringBannerKind,
        IReadOnlyList<MonitoringTileViewModel> MonitoringCards);

    public static Presentation Map(ApplicationStateSnapshot snapshot)
    {
        var appHeadline = MapOverallStateHeadline(snapshot.State);
        var appDetail = MapDetailText(snapshot);
        var appSecondaryText = MapSecondaryText(snapshot.State);
        var appKind = MapOverallStateKind(snapshot.State);

        var monitoringCards = BuildMonitoringCards(snapshot, appDetail);
        var (bannerHeadline, bannerMessage, bannerKind) = MapBanner(snapshot, appDetail);

        return new Presentation(
            appHeadline,
            appDetail,
            appSecondaryText,
            appKind,
            bannerHeadline,
            bannerMessage,
            bannerKind,
            monitoringCards);
    }

    public static string MapOverallStateHeadline(OverallApplicationState state) =>
        state switch
        {
            OverallApplicationState.Unknown => "Initializing",
            OverallApplicationState.Ready => "Ready",
            OverallApplicationState.Waiting => "Waiting",
            OverallApplicationState.Monitoring => "Monitoring",
            OverallApplicationState.NeedsAttention => "Needs Attention",
            OverallApplicationState.Degraded => "Limited",
            OverallApplicationState.Error => "Action Required",
            _ => "Initializing"
        };

    public static string MapSecondaryText(OverallApplicationState state) =>
        state switch
        {
            OverallApplicationState.Unknown => "Starting services",
            OverallApplicationState.Ready => "All systems operational",
            OverallApplicationState.Waiting => "Awaiting Homecoming",
            OverallApplicationState.Monitoring => "Monitoring active",
            OverallApplicationState.NeedsAttention => "See Dashboard",
            OverallApplicationState.Degraded => "See Dashboard",
            OverallApplicationState.Error => "See Dashboard",
            _ => "Starting services"
        };

    public static string MapDetailText(ApplicationStateSnapshot snapshot)
    {
        if (!string.IsNullOrWhiteSpace(snapshot.PrimaryIssue?.Summary))
        {
            return snapshot.PrimaryIssue.Summary;
        }

        if (!string.IsNullOrWhiteSpace(snapshot.StateSummary))
        {
            return snapshot.StateSummary;
        }

        return MapOverallStateHeadline(snapshot.State);
    }

    public static DashboardStatusKind MapOverallStateKind(OverallApplicationState state) =>
        state switch
        {
            OverallApplicationState.Error => DashboardStatusKind.Error,
            OverallApplicationState.Degraded => DashboardStatusKind.Warning,
            OverallApplicationState.NeedsAttention => DashboardStatusKind.Warning,
            OverallApplicationState.Ready => DashboardStatusKind.Success,
            OverallApplicationState.Monitoring => DashboardStatusKind.Success,
            OverallApplicationState.Waiting => DashboardStatusKind.Information,
            _ => DashboardStatusKind.Information
        };

    public static DashboardStatusKind MapIssueSeverity(ApplicationIssueSeverity severity) =>
        severity switch
        {
            ApplicationIssueSeverity.Critical => DashboardStatusKind.Error,
            ApplicationIssueSeverity.Error => DashboardStatusKind.Error,
            ApplicationIssueSeverity.Warning => DashboardStatusKind.Warning,
            _ => DashboardStatusKind.Information
        };

    private static (string Headline, string Message, DashboardStatusKind Kind) MapBanner(
        ApplicationStateSnapshot snapshot,
        string calmDetail)
    {
        if (snapshot.PrimaryIssue is { } issue)
        {
            var kind = MapIssueSeverity(issue.Severity);
            var headline = kind is DashboardStatusKind.Information ? "STATUS" : "ATTENTION NEEDED";
            return (headline, issue.Summary, kind);
        }

        return ("STATUS", calmDetail, MapOverallStateKind(snapshot.State));
    }

    private static IReadOnlyList<MonitoringTileViewModel> BuildMonitoringCards(
        ApplicationStateSnapshot snapshot,
        string detail)
    {
        var providers = snapshot.Providers;

        var running = providers.Count(provider => provider.LifecycleState == ApplicationContributorLifecycleState.Running);
        var faulted = providers.Count(provider => provider.LifecycleState == ApplicationContributorLifecycleState.Faulted);
        var starting = providers.Count(provider => provider.LifecycleState == ApplicationContributorLifecycleState.Starting);

        var ready = providers.Count(provider => provider.Health == ContributorHealth.Ready);
        var degraded = providers.Count(provider => provider.Health == ContributorHealth.Degraded);
        var error = providers.Count(provider =>
            provider.Health is ContributorHealth.Error or ContributorHealth.Unavailable);

        var (activityLabel, activityDetail) = MapHighestActivity(providers, snapshot.State);

        return
        [
            new MonitoringTileViewModel(
                "Application State",
                MapMonitoringStateLabel(snapshot.State),
                vectorIconPath: "Assets/Icons/monitoring/data-processing.svg",
                subtitle: detail),
            new MonitoringTileViewModel(
                "Contributors",
                $"{running} Running",
                vectorIconPath: "Assets/Icons/monitoring/parser.svg",
                subtitle: BuildContributorSubtitle(running, faulted, starting)),
            new MonitoringTileViewModel(
                "Health",
                $"{ready} Ready",
                vectorIconPath: "Assets/Icons/monitoring/enhancement-database.svg",
                subtitle: BuildHealthSubtitle(degraded, error)),
            new MonitoringTileViewModel(
                "Activity",
                activityLabel,
                vectorIconPath: "Assets/Icons/monitoring/game-client.svg",
                subtitle: activityDetail)
        ];
    }

    private static string MapMonitoringStateLabel(OverallApplicationState state) =>
        state switch
        {
            OverallApplicationState.Unknown => "INITIALIZING",
            OverallApplicationState.Ready => "READY",
            OverallApplicationState.Waiting => "WAITING",
            OverallApplicationState.Monitoring => "MONITORING",
            OverallApplicationState.NeedsAttention => "ATTENTION",
            OverallApplicationState.Degraded => "DEGRADED",
            OverallApplicationState.Error => "ERROR",
            _ => "INITIALIZING"
        };

    private static string BuildContributorSubtitle(int running, int faulted, int starting)
    {
        if (starting > 0)
        {
            return $"{starting} Starting, {faulted} Faulted";
        }

        return $"{faulted} Faulted";
    }

    private static string BuildHealthSubtitle(int degraded, int error)
    {
        if (degraded == 0 && error == 0)
        {
            return "0 Degraded, 0 Error";
        }

        return $"{degraded} Degraded, {error} Error";
    }

    internal static (string Label, string Detail) MapHighestActivity(
        IReadOnlyCollection<ProviderSummary> providers,
        OverallApplicationState overallState)
    {
        if (providers.Any(provider => provider.Activity == ContributorActivity.ReceivingData))
        {
            return ("Receiving Data", "Live data source active");
        }

        if (providers.Any(provider => provider.Activity == ContributorActivity.Active))
        {
            return ("Active", "Game activity detected");
        }

        if (providers.Any(provider => provider.Activity == ContributorActivity.Waiting)
            || overallState == OverallApplicationState.Waiting)
        {
            return ("Waiting", "Waiting on prerequisites");
        }

        if (providers.Any(provider => provider.Activity == ContributorActivity.Paused))
        {
            return ("Paused", "Monitoring paused");
        }

        return ("Inactive", "No live data source yet");
    }

    public static string MapParserStatusLabel(ApplicationStateSnapshot snapshot)
    {
        var parser = snapshot.Providers.FirstOrDefault(provider =>
            string.Equals(provider.ProviderId, ApplicationProviders.Parser, StringComparison.Ordinal));

        if (parser is null)
        {
            return snapshot.Revision <= 0 ? "Parser: starting" : "Parser: unavailable";
        }

        if (parser.LifecycleState is ApplicationContributorLifecycleState.Faulted)
        {
            return "Parser: faulted";
        }

        if (parser.LifecycleState is ApplicationContributorLifecycleState.Starting
            or ApplicationContributorLifecycleState.Registered)
        {
            return "Parser: starting";
        }

        if (parser.Health is ContributorHealth.Error or ContributorHealth.Unavailable)
        {
            return "Parser: error";
        }

        if (parser.Health is ContributorHealth.Degraded)
        {
            return "Parser: degraded";
        }

        return parser.Activity switch
        {
            ContributorActivity.ReceivingData => "Parser: receiving data",
            ContributorActivity.Active => "Parser: active",
            ContributorActivity.Waiting => "Parser: waiting",
            ContributorActivity.Paused => "Parser: paused",
            _ => "Parser: inactive"
        };
    }
}
