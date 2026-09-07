using CoHAnalytics.Models;
using CoHAnalytics.Orchestration.Contributors;
using CoHAnalytics.Orchestration.Models;
using CoHAnalytics.Services;
using CoHAnalytics.Tests.Orchestration;

namespace CoHAnalytics.Tests.Orchestration.Contributors;

public sealed class MidsInstallationContributorTests
{
    [Fact]
    public void Descriptor_matches_governing_architecture()
    {
        var contributor = new MidsInstallationContributor(new MidsInstallationService(new SettingsService()));

        Assert.Equal(ApplicationProviders.MidsInstallation, contributor.Descriptor.ProviderId);
        Assert.Equal(
            [ApplicationCapabilities.MidsInstallation, ApplicationCapabilities.MidsHomecomingDatabase],
            contributor.Descriptor.Produces);
        Assert.Equal(ApplicationContributorImportance.Optional, contributor.Descriptor.Importance);
    }

    [Fact]
    public void BuildFacts_maps_installation_and_database_capabilities()
    {
        var now = new DateTimeOffset(2026, 8, 4, 12, 0, 0, TimeSpan.Zero);
        var status = MidsInstallationStatus.FromInstallation(new MidsInstallation
        {
            InstallRoot = @"C:\Tools\Mids",
            ExecutablePath = @"C:\Tools\Mids\MidsReborn.exe",
            ApplicationVersion = "3.0.0",
            HomecomingDatabasePath = @"C:\Tools\Mids\Homecoming.mdb",
            HomecomingDatabaseVersion = "2026.01"
        });

        var facts = MidsInstallationContributor.BuildFacts(status, now);

        Assert.True(GetBoolean(facts, ContributorFactKeys.MidsInstallation.Configured));
        Assert.True(GetBoolean(facts, ContributorFactKeys.MidsInstallation.DatabaseAvailable));
        Assert.Equal("3.0.0", GetText(facts, ContributorFactKeys.MidsInstallation.Version));
        Assert.Equal("2026.01", GetText(facts, ContributorFactKeys.MidsInstallation.DatabaseVersion));
    }

    [Fact]
    public async Task Absent_mids_does_not_report_error_severity()
    {
        var service = new MidsInstallationService(new SettingsService());
        var contributor = new MidsInstallationContributor(service, new ManualTimeProvider());

        var contribution = await contributor.GetContributionAsync();

        Assert.Equal(ContributorHealth.Unknown, contribution.Health);
        Assert.DoesNotContain(contribution.Issues, issue => issue.Severity >= ApplicationIssueSeverity.Error);
        Assert.Contains(contribution.Issues, issue => issue.Code == ContributorIssueCodes.MidsInstallation.NotFound);
    }

    private static bool GetBoolean(IReadOnlyList<ApplicationFact> facts, string key) =>
        (facts.Single(fact => fact.Key == key).Value as ApplicationFactValue.Boolean)!.Value;

    private static string GetText(IReadOnlyList<ApplicationFact> facts, string key) =>
        (facts.Single(fact => fact.Key == key).Value as ApplicationFactValue.Text)!.Value;
}
