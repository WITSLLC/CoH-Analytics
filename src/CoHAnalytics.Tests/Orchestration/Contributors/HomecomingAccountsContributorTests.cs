using System.Reflection;
using CoHAnalytics.Models;
using CoHAnalytics.Orchestration.Contributors;
using CoHAnalytics.Orchestration.Models;
using CoHAnalytics.Services;
using CoHAnalytics.Tests.Orchestration;

namespace CoHAnalytics.Tests.Orchestration.Contributors;

public sealed class HomecomingAccountsContributorTests
{
    [Fact]
    public void Descriptor_matches_governing_architecture()
    {
        var contributor = new HomecomingAccountsContributor(
            new HomecomingAccountDiscoveryService(new HomecomingInstallationService(new SettingsService())));

        Assert.Equal(ApplicationProviders.Accounts, contributor.Descriptor.ProviderId);
        Assert.Equal(
            [ApplicationCapabilities.AccountDiscovery, ApplicationCapabilities.AccountLogHistory],
            contributor.Descriptor.Produces);
        Assert.Equal([ApplicationCapabilities.HomecomingInstallation], contributor.Descriptor.Requires);
        Assert.Equal([ApplicationCapabilities.HomecomingRuntime], contributor.Descriptor.Optional);
        Assert.Equal(ApplicationContributorImportance.Important, contributor.Descriptor.Importance);
    }

    [Fact]
    public void BuildSummaryFacts_reports_counts_and_latest_timestamp()
    {
        var now = new DateTimeOffset(2026, 8, 4, 12, 0, 0, TimeSpan.Zero);
        var accounts = new[]
        {
            CreateAccount("alpha", hasLogs: true, hasHistoricalLogs: true, newest: now.AddHours(-2)),
            CreateAccount("beta", hasLogs: true, hasHistoricalLogs: false),
            CreateAccount("gamma", hasLogs: false, hasHistoricalLogs: false)
        };

        var facts = HomecomingAccountsContributor.BuildSummaryFacts(accounts, now);

        Assert.Equal(3L, GetInteger(facts, ContributorFactKeys.Accounts.Count));
        Assert.Equal(2L, GetInteger(facts, ContributorFactKeys.Accounts.WithLogsCount));
        Assert.Equal(1L, GetInteger(facts, ContributorFactKeys.Accounts.WithHistoricalLogsCount));
        Assert.Equal(now.AddHours(-2), GetTimestamp(facts, ContributorFactKeys.Accounts.LatestLogTimestamp));
        Assert.DoesNotContain(facts, fact => fact.Key.Contains("character", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task No_log_accounts_do_not_create_global_warning_issues()
    {
        var service = new HomecomingAccountDiscoveryService(new HomecomingInstallationService(new SettingsService()));
        SetAccounts(service,
        [
            CreateAccount("alpha", hasLogs: false, hasHistoricalLogs: false)
        ]);

        var contributor = new HomecomingAccountsContributor(service, new ManualTimeProvider());
        var contribution = await contributor.GetContributionAsync();

        Assert.Equal(ContributorHealth.Ready, contribution.Health);
        Assert.Equal(ContributorActivity.Inactive, contribution.Activity);
        Assert.DoesNotContain(contribution.Issues, issue => issue.Severity >= ApplicationIssueSeverity.Warning);
    }

    [Fact]
    public async Task Contribution_remains_inactive_even_with_historical_logs()
    {
        var service = new HomecomingAccountDiscoveryService(new HomecomingInstallationService(new SettingsService()));
        SetAccounts(service,
        [
            CreateAccount("alpha", hasLogs: true, hasHistoricalLogs: true, newest: DateTimeOffset.UtcNow)
        ]);

        var contributor = new HomecomingAccountsContributor(service);
        var contribution = await contributor.GetContributionAsync();

        Assert.Equal(ContributorActivity.Inactive, contribution.Activity);
        Assert.DoesNotContain(contribution.Facts, fact => fact.Key.Contains("activity", StringComparison.OrdinalIgnoreCase));
    }

    private static HomecomingAccount CreateAccount(
        string name,
        bool hasLogs,
        bool hasHistoricalLogs,
        DateTimeOffset? newest = null) =>
        new()
        {
            StableId = name,
            DisplayName = name,
            FolderName = name,
            FolderPath = $@"C:\Games\Homecoming\accounts\{name}",
            HasLogsFolder = hasLogs,
            HasHistoricalLogs = hasHistoricalLogs,
            LogFileCount = hasHistoricalLogs ? 1 : 0,
            NewestLogTimestamp = newest,
            Status = hasLogs ? HomecomingAccountStatus.Ready : HomecomingAccountStatus.NoLogs
        };

    private static void SetAccounts(HomecomingAccountDiscoveryService service, IReadOnlyList<HomecomingAccount> accounts) =>
        typeof(HomecomingAccountDiscoveryService)
            .GetProperty(nameof(HomecomingAccountDiscoveryService.Accounts))!
            .SetValue(service, accounts);

    private static long GetInteger(IReadOnlyList<ApplicationFact> facts, string key) =>
        (facts.Single(fact => fact.Key == key).Value as ApplicationFactValue.Integer)!.Value;

    private static DateTimeOffset GetTimestamp(IReadOnlyList<ApplicationFact> facts, string key) =>
        (facts.Single(fact => fact.Key == key).Value as ApplicationFactValue.Timestamp)!.Value;
}
