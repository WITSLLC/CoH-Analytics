using System.Reflection;
using CoHAnalytics.Tests.Orchestration;
using CoHAnalytics.Models;
using CoHAnalytics.Orchestration;
using CoHAnalytics.Orchestration.Contributors;
using CoHAnalytics.Orchestration.Models;
using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Orchestration.Contributors;

public sealed class HomecomingInstallationContributorTests
{
    [Fact]
    public void Descriptor_matches_governing_architecture()
    {
        var contributor = new HomecomingInstallationContributor(new HomecomingInstallationService(new SettingsService()));

        Assert.Equal(ApplicationProviders.HomecomingInstallation, contributor.Descriptor.ProviderId);
        Assert.Equal("Homecoming Installation", contributor.Descriptor.DisplayName);
        Assert.Equal([ApplicationCapabilities.HomecomingInstallation], contributor.Descriptor.Produces);
        Assert.Empty(contributor.Descriptor.Requires);
        Assert.Empty(contributor.Descriptor.Optional);
        Assert.Equal(ApplicationContributorImportance.Critical, contributor.Descriptor.Importance);
        Assert.Equal(ApplicationContributorDescriptor.ExpectedSchemaVersion, contributor.Descriptor.SchemaVersion);
    }

    [Fact]
    public async Task Missing_installation_reports_unavailable_with_not_found_issue()
    {
        var service = new HomecomingInstallationService(new SettingsService());
        var contributor = new HomecomingInstallationContributor(service);

        var contribution = await contributor.GetContributionAsync();

        Assert.Equal(ApplicationProviders.HomecomingInstallation, contribution.ProviderId);
        Assert.Equal(ContributorHealth.Unavailable, contribution.Health);
        Assert.Equal(ContributorActivity.Inactive, contribution.Activity);
        Assert.Contains(contribution.Facts, fact => fact.Key == ContributorFactKeys.HomecomingInstallation.Configured
            && fact.Value is ApplicationFactValue.Boolean { Value: false });
        var issue = Assert.Single(contribution.Issues);
        Assert.Equal(ContributorIssueCodes.HomecomingInstallation.NotFound, issue.Code);
        Assert.Equal(ApplicationIssueSeverity.Error, issue.Severity);
        Assert.True(issue.RequiresUserAction);
    }

    [Fact]
    public async Task Valid_installation_reports_ready_with_summary_facts()
    {
        var service = new HomecomingInstallationService(new SettingsService());
        SetCurrentInstallation(service, new HomecomingInstallation
        {
            InstallRoot = @"C:\Games\Homecoming",
            LauncherPath = @"C:\Games\Homecoming\Homecoming Launcher.exe"
        });
        SetLastDiscoverySelection(service, @"C:\Games\Homecoming", HomecomingDiscoverySource.Persisted);

        var contributor = new HomecomingInstallationContributor(service, new ManualTimeProvider());
        var contribution = await contributor.GetContributionAsync();

        Assert.Equal(ContributorHealth.Ready, contribution.Health);
        Assert.Empty(contribution.Issues);
        Assert.Contains(contribution.Facts, fact => fact.Key == ContributorFactKeys.HomecomingInstallation.Configured
            && fact.Value is ApplicationFactValue.Boolean { Value: true });
        Assert.Contains(contribution.Facts, fact => fact.Key == ContributorFactKeys.HomecomingInstallation.Root);
        Assert.Contains(contribution.Facts, fact => fact.Key == ContributorFactKeys.HomecomingInstallation.Launcher);
        Assert.Contains(contribution.Facts, fact => fact.Key == ContributorFactKeys.HomecomingInstallation.DiscoverySource);
    }

    private static void SetCurrentInstallation(HomecomingInstallationService service, HomecomingInstallation installation) =>
        typeof(HomecomingInstallationService)
            .GetProperty(nameof(HomecomingInstallationService.CurrentInstallation))!
            .SetValue(service, installation);

    private static void SetLastDiscoverySelection(
        HomecomingInstallationService service,
        string installRoot,
        HomecomingDiscoverySource source)
    {
        var diagnostics = new HomecomingInstallDiscoveryDiagnostics();
        diagnostics.RecordSelection(installRoot, source, installRoot + "\\Homecoming Launcher.exe");
        typeof(HomecomingInstallationService)
            .GetProperty(nameof(HomecomingInstallationService.LastDiscovery))!
            .SetValue(service, diagnostics);
    }
}
