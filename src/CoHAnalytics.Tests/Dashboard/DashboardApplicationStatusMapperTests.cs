using CoHAnalytics.Orchestration.Models;
using CoHAnalytics.ViewModels.Workspaces;

namespace CoHAnalytics.Tests.Dashboard;

public sealed class DashboardApplicationStatusMapperTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 4, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(OverallApplicationState.Unknown, DashboardStatusKind.Information)]
    [InlineData(OverallApplicationState.Ready, DashboardStatusKind.Success)]
    [InlineData(OverallApplicationState.Waiting, DashboardStatusKind.Information)]
    [InlineData(OverallApplicationState.Monitoring, DashboardStatusKind.Success)]
    [InlineData(OverallApplicationState.NeedsAttention, DashboardStatusKind.Warning)]
    [InlineData(OverallApplicationState.Degraded, DashboardStatusKind.Warning)]
    [InlineData(OverallApplicationState.Error, DashboardStatusKind.Error)]
    public void Maps_overall_state_visual_category(OverallApplicationState state, DashboardStatusKind expectedKind)
    {
        var snapshot = CreateSnapshot(state);
        Assert.Equal(expectedKind, DashboardApplicationStatusMapper.MapOverallStateKind(state));
        Assert.Equal(expectedKind, DashboardApplicationStatusMapper.Map(snapshot).AppStatusKind);
    }

    [Theory]
    [InlineData(OverallApplicationState.Unknown, "Initializing")]
    [InlineData(OverallApplicationState.Ready, "Ready")]
    [InlineData(OverallApplicationState.Waiting, "Waiting")]
    [InlineData(OverallApplicationState.Monitoring, "Monitoring")]
    [InlineData(OverallApplicationState.NeedsAttention, "Needs Attention")]
    [InlineData(OverallApplicationState.Degraded, "Limited")]
    [InlineData(OverallApplicationState.Error, "Action Required")]
    public void Maps_every_overall_state_headline(OverallApplicationState state, string expectedHeadline)
    {
        var snapshot = CreateSnapshot(state);
        Assert.Equal(expectedHeadline, DashboardApplicationStatusMapper.MapOverallStateHeadline(state));
        Assert.Equal(expectedHeadline, DashboardApplicationStatusMapper.Map(snapshot).AppStatusHeadline);
    }

    [Theory]
    [InlineData(OverallApplicationState.Unknown, "Starting services")]
    [InlineData(OverallApplicationState.Ready, "All systems operational")]
    [InlineData(OverallApplicationState.Waiting, "Awaiting Homecoming")]
    [InlineData(OverallApplicationState.Monitoring, "Monitoring active")]
    [InlineData(OverallApplicationState.NeedsAttention, "See Dashboard")]
    [InlineData(OverallApplicationState.Degraded, "See Dashboard")]
    [InlineData(OverallApplicationState.Error, "See Dashboard")]
    public void Maps_every_overall_state_secondary_text(OverallApplicationState state, string expectedText)
    {
        Assert.Equal(expectedText, DashboardApplicationStatusMapper.MapSecondaryText(state));
        Assert.Equal(expectedText, DashboardApplicationStatusMapper.Map(CreateSnapshot(state)).AppStatusSecondaryText);
    }

    [Fact]
    public void Primary_issue_summary_takes_precedence_over_state_summary()
    {
        var snapshot = CreateSnapshot(
            OverallApplicationState.Error,
            stateSummary: "Error",
            primaryIssue: new ApplicationIssue("installation.homecoming.not_found", ApplicationIssueSeverity.Error, "Homecoming installation was not discovered.", "installation.homecoming", Now));

        var presentation = DashboardApplicationStatusMapper.Map(snapshot);

        Assert.Equal("Homecoming installation was not discovered.", presentation.AppStatusDetail);
        Assert.Equal("ATTENTION NEEDED", presentation.MonitoringBannerHeadline);
        Assert.Equal(DashboardStatusKind.Error, presentation.MonitoringBannerKind);
    }

    [Fact]
    public void State_summary_is_used_when_no_primary_issue()
    {
        var snapshot = CreateSnapshot(OverallApplicationState.Waiting, stateSummary: "Waiting for prerequisites.");

        var presentation = DashboardApplicationStatusMapper.Map(snapshot);

        Assert.Equal("Waiting for prerequisites.", presentation.AppStatusDetail);
        Assert.Equal("STATUS", presentation.MonitoringBannerHeadline);
    }

    [Fact]
    public void Falls_back_to_headline_when_summary_blank()
    {
        var snapshot = CreateSnapshot(OverallApplicationState.Ready, stateSummary: " ");

        Assert.Equal("Ready", DashboardApplicationStatusMapper.Map(snapshot).AppStatusDetail);
    }

    [Fact]
    public void Monitoring_cards_report_lifecycle_and_health_counts()
    {
        var snapshot = CreateSnapshot(
            OverallApplicationState.Ready,
            providers:
            [
                Provider("installation.homecoming", ContributorHealth.Ready, ContributorActivity.Inactive, ApplicationContributorLifecycleState.Running),
                Provider("runtime.homecoming", ContributorHealth.Ready, ContributorActivity.Active, ApplicationContributorLifecycleState.Running),
                Provider("accounts", ContributorHealth.Ready, ContributorActivity.Inactive, ApplicationContributorLifecycleState.Running),
                Provider("installation.mids", ContributorHealth.Unknown, ContributorActivity.Inactive, ApplicationContributorLifecycleState.Running)
            ]);

        var cards = DashboardApplicationStatusMapper.Map(snapshot).MonitoringCards;

        Assert.Equal("4 Running", cards[1].Value);
        Assert.Equal("0 Faulted", cards[1].Subtitle);
        Assert.Equal("3 Ready", cards[2].Value);
        Assert.Equal("0 Degraded, 0 Error", cards[2].Subtitle);
    }

    [Fact]
    public void Activity_mapping_does_not_claim_receiving_data_from_runtime_active()
    {
        var providers = new[]
        {
            Provider("runtime.homecoming", ContributorHealth.Ready, ContributorActivity.Active, ApplicationContributorLifecycleState.Running)
        };

        var (label, detail) = DashboardApplicationStatusMapper.MapHighestActivity(providers, OverallApplicationState.Waiting);

        Assert.Equal("Active", label);
        Assert.Equal("Game activity detected", detail);
        Assert.DoesNotContain("Receiving", label, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("log", detail, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(ContributorActivity.ReceivingData, "Parser: receiving data")]
    [InlineData(ContributorActivity.Active, "Parser: active")]
    [InlineData(ContributorActivity.Waiting, "Parser: waiting")]
    [InlineData(ContributorActivity.Inactive, "Parser: inactive")]
    [InlineData(ContributorActivity.Paused, "Parser: paused")]
    public void Maps_parser_activity_to_status_bar_label(
        ContributorActivity activity,
        string expectedLabel)
    {
        var snapshot = CreateSnapshot(
            OverallApplicationState.Monitoring,
            providers:
            [
                Provider(
                    ApplicationProviders.Parser,
                    ContributorHealth.Ready,
                    activity,
                    ApplicationContributorLifecycleState.Running)
            ]);

        Assert.Equal(expectedLabel, DashboardApplicationStatusMapper.MapParserStatusLabel(snapshot));
    }

    [Fact]
    public void Maps_parser_health_and_lifecycle_to_status_bar_label()
    {
        var degraded = CreateSnapshot(
            OverallApplicationState.Degraded,
            providers:
            [
                Provider(
                    ApplicationProviders.Parser,
                    ContributorHealth.Degraded,
                    ContributorActivity.Active,
                    ApplicationContributorLifecycleState.Running)
            ]);
        var faulted = CreateSnapshot(
            OverallApplicationState.Error,
            providers:
            [
                Provider(
                    ApplicationProviders.Parser,
                    ContributorHealth.Ready,
                    ContributorActivity.Inactive,
                    ApplicationContributorLifecycleState.Faulted)
            ]);
        var starting = CreateSnapshot(
            OverallApplicationState.Unknown,
            providers:
            [
                Provider(
                    ApplicationProviders.Parser,
                    ContributorHealth.Ready,
                    ContributorActivity.Inactive,
                    ApplicationContributorLifecycleState.Starting)
            ]);

        Assert.Equal("Parser: degraded", DashboardApplicationStatusMapper.MapParserStatusLabel(degraded));
        Assert.Equal("Parser: faulted", DashboardApplicationStatusMapper.MapParserStatusLabel(faulted));
        Assert.Equal("Parser: starting", DashboardApplicationStatusMapper.MapParserStatusLabel(starting));
    }

    [Fact]
    public void Maps_missing_parser_provider_to_starting_or_unavailable()
    {
        var initial = ApplicationStateSnapshot.Empty(Now);
        var unavailable = CreateSnapshot(OverallApplicationState.Ready);

        Assert.Equal("Parser: starting", DashboardApplicationStatusMapper.MapParserStatusLabel(initial));
        Assert.Equal("Parser: unavailable", DashboardApplicationStatusMapper.MapParserStatusLabel(unavailable));
    }

    [Fact]
    public void Optional_mids_unknown_does_not_force_error_presentation_by_itself()
    {
        var snapshot = CreateSnapshot(
            OverallApplicationState.Ready,
            providers:
            [
                Provider("installation.homecoming", ContributorHealth.Ready, ContributorActivity.Inactive, ApplicationContributorLifecycleState.Running),
                Provider("runtime.homecoming", ContributorHealth.Ready, ContributorActivity.Inactive, ApplicationContributorLifecycleState.Running),
                Provider("accounts", ContributorHealth.Ready, ContributorActivity.Inactive, ApplicationContributorLifecycleState.Running),
                Provider("installation.mids", ContributorHealth.Unknown, ContributorActivity.Inactive, ApplicationContributorLifecycleState.Running)
            ],
            stateSummary: "Ready");

        var presentation = DashboardApplicationStatusMapper.Map(snapshot);

        Assert.Equal(OverallApplicationState.Ready, snapshot.State);
        Assert.Equal("READY", presentation.MonitoringCards[0].Value);
        Assert.Equal(DashboardStatusKind.Success, presentation.AppStatusKind);
    }

    private static ApplicationStateSnapshot CreateSnapshot(
        OverallApplicationState state,
        string? stateSummary = null,
        ApplicationIssue? primaryIssue = null,
        IReadOnlyList<ProviderSummary>? providers = null) =>
        new(
            state,
            providers ?? [],
            primaryIssue is null ? [] : [primaryIssue],
            primaryIssue,
            [],
            Now)
        {
            StateSummary = stateSummary ?? state.ToString(),
            Revision = 1
        };

    private static ProviderSummary Provider(
        string providerId,
        ContributorHealth health,
        ContributorActivity activity,
        ApplicationContributorLifecycleState lifecycle) =>
        new(providerId, providerId, health, activity, lifecycle, Now);
}
