using CoHAnalytics.Models;
using CoHAnalytics.Orchestration.Contracts;
using CoHAnalytics.Orchestration.Models;
using CoHAnalytics.Services;

namespace CoHAnalytics.Orchestration.Contributors;

public sealed class HomecomingAccountsContributor : IApplicationContributor
{
    private readonly HomecomingAccountDiscoveryService _accountDiscoveryService;
    private readonly TimeProvider _timeProvider;

    public HomecomingAccountsContributor(
        HomecomingAccountDiscoveryService accountDiscoveryService,
        TimeProvider? timeProvider = null)
    {
        _accountDiscoveryService = accountDiscoveryService;
        _timeProvider = timeProvider ?? TimeProvider.System;
        Descriptor = CreateDescriptor();
    }

    public ApplicationContributorDescriptor Descriptor { get; }

    public event EventHandler? ContributionChanged;

    public Task<ApplicationContribution> GetContributionAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(BuildContribution());
    }

    public void Refresh()
    {
        _accountDiscoveryService.Discover();
        ContributionChanged?.Invoke(this, EventArgs.Empty);
    }

    private ApplicationContribution BuildContribution()
    {
        var accounts = _accountDiscoveryService.Accounts;
        var discovery = _accountDiscoveryService.LastDiscovery;
        var now = _timeProvider.GetUtcNow();

        if (IsInstallationMissing(discovery))
        {
            return ContributorContributionFactory.Create(
                Descriptor,
                ContributorHealth.Unavailable,
                ContributorActivity.Inactive,
                BuildSummaryFacts(accounts, now),
                [
                    ContributorContributionFactory.Issue(
                        ContributorIssueCodes.Accounts.InstallationRequired,
                        ApplicationIssueSeverity.Information,
                        "Homecoming accounts cannot be discovered until installation is configured.",
                        Descriptor.ProviderId,
                        now)
                ],
                _timeProvider);
        }

        if (IsDiscoveryFailure(discovery, accounts))
        {
            return ContributorContributionFactory.Create(
                Descriptor,
                ContributorHealth.Degraded,
                ContributorActivity.Inactive,
                BuildSummaryFacts(accounts, now),
                [
                    ContributorContributionFactory.Issue(
                        ContributorIssueCodes.Accounts.DiscoveryFailed,
                        ApplicationIssueSeverity.Warning,
                        "Homecoming account discovery encountered a problem.",
                        Descriptor.ProviderId,
                        now,
                        detail: GetPrimaryFailureReason(discovery))
                ],
                _timeProvider);
        }

        var issues = new List<ApplicationIssue>();
        if (accounts.Count == 0)
        {
            issues.Add(ContributorContributionFactory.Issue(
                ContributorIssueCodes.Accounts.NoneDiscovered,
                ApplicationIssueSeverity.Information,
                "No Homecoming account folders were discovered.",
                Descriptor.ProviderId,
                now));
        }

        return ContributorContributionFactory.Create(
            Descriptor,
            ContributorHealth.Ready,
            ContributorActivity.Inactive,
            BuildSummaryFacts(accounts, now),
            issues,
            _timeProvider);
    }

    internal static IReadOnlyList<ApplicationFact> BuildSummaryFacts(
        IReadOnlyList<HomecomingAccount> accounts,
        DateTimeOffset observedAt)
    {
        var withLogs = accounts.Count(account => account.HasLogsFolder);
        var withHistoricalLogs = accounts.Count(account => account.HasHistoricalLogs);
        var latestLog = accounts
            .Where(account => account.NewestLogTimestamp is not null)
            .Select(account => account.NewestLogTimestamp!.Value)
            .DefaultIfEmpty()
            .Max();

        var facts = new List<ApplicationFact>
        {
            ContributorContributionFactory.IntegerFact(
                ContributorFactKeys.Accounts.Count,
                accounts.Count,
                ApplicationFactScope.Application,
                observedAt,
                display: new ApplicationFactDisplay("Account count")),
            ContributorContributionFactory.IntegerFact(
                ContributorFactKeys.Accounts.WithLogsCount,
                withLogs,
                ApplicationFactScope.Application,
                observedAt,
                display: new ApplicationFactDisplay("Accounts with Logs folder")),
            ContributorContributionFactory.IntegerFact(
                ContributorFactKeys.Accounts.WithHistoricalLogsCount,
                withHistoricalLogs,
                ApplicationFactScope.Application,
                observedAt,
                display: new ApplicationFactDisplay("Accounts with historical logs"))
        };

        if (latestLog != default)
        {
            facts.Add(ContributorContributionFactory.TimestampFact(
                ContributorFactKeys.Accounts.LatestLogTimestamp,
                latestLog,
                ApplicationFactScope.Application,
                observedAt,
                display: new ApplicationFactDisplay("Newest historical log timestamp")));
        }

        return facts;
    }

    private static bool IsInstallationMissing(HomecomingAccountDiscoveryDiagnostics discovery) =>
        discovery.Attempts.Any(attempt =>
            !attempt.IsValid
            && string.Equals(
                attempt.FailureReason,
                "Homecoming installation is not configured.",
                StringComparison.Ordinal));

    private static bool IsDiscoveryFailure(
        HomecomingAccountDiscoveryDiagnostics discovery,
        IReadOnlyList<HomecomingAccount> accounts)
    {
        if (accounts.Count > 0)
        {
            return false;
        }

        return discovery.Attempts.Any(attempt =>
            !attempt.IsValid
            && !string.Equals(
                attempt.FailureReason,
                "Homecoming installation is not configured.",
                StringComparison.Ordinal)
            && !string.Equals(
                attempt.FailureReason,
                "Ignored shared folder.",
                StringComparison.Ordinal));
    }

    private static string? GetPrimaryFailureReason(HomecomingAccountDiscoveryDiagnostics discovery) =>
        discovery.Attempts
            .Where(attempt => !attempt.IsValid)
            .Select(attempt => attempt.FailureReason)
            .FirstOrDefault(reason => !string.IsNullOrWhiteSpace(reason));

    private static ApplicationContributorDescriptor CreateDescriptor() =>
        new(
            ApplicationProviders.Accounts,
            "Homecoming Accounts",
            [ApplicationCapabilities.AccountDiscovery, ApplicationCapabilities.AccountLogHistory],
            [ApplicationCapabilities.HomecomingInstallation],
            [ApplicationCapabilities.HomecomingRuntime],
            ApplicationContributorImportance.Important,
            30)
        {
            Description = "Homecoming account folders discovered on this machine.",
            IconKey = "accounts",
            SchemaVersion = ApplicationContributorDescriptor.ExpectedSchemaVersion
        };
}
