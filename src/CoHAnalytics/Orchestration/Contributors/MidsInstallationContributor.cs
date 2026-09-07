using CoHAnalytics.Models;
using CoHAnalytics.Orchestration.Contracts;
using CoHAnalytics.Orchestration.Models;
using CoHAnalytics.Services;

namespace CoHAnalytics.Orchestration.Contributors;

public sealed class MidsInstallationContributor : IApplicationContributor
{
    private readonly MidsInstallationService _installationService;
    private readonly TimeProvider _timeProvider;

    public MidsInstallationContributor(
        MidsInstallationService installationService,
        TimeProvider? timeProvider = null)
    {
        _installationService = installationService;
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
        _installationService.DiscoverAndPersist();
        ContributionChanged?.Invoke(this, EventArgs.Empty);
    }

    private ApplicationContribution BuildContribution()
    {
        var status = _installationService.GetStatus();
        var now = _timeProvider.GetUtcNow();

        if (status.MultipleInstallAmbiguity)
        {
            return ContributorContributionFactory.Create(
                Descriptor,
                ContributorHealth.Degraded,
                ContributorActivity.Inactive,
                BuildFacts(status, now),
                [
                    ContributorContributionFactory.Issue(
                        ContributorIssueCodes.MidsInstallation.MultipleFound,
                        ApplicationIssueSeverity.Information,
                        "Multiple Mids Reborn installations were detected.",
                        Descriptor.ProviderId,
                        now,
                        detail: "Discovery could not select a single installation.")
                ],
                _timeProvider,
                componentVersion: status.HomecomingDatabaseVersion);
        }

        if (!status.IsValid)
        {
            return ContributorContributionFactory.Create(
                Descriptor,
                ContributorHealth.Unknown,
                ContributorActivity.Inactive,
                BuildFacts(status, now),
                [
                    ContributorContributionFactory.Issue(
                        ContributorIssueCodes.MidsInstallation.NotFound,
                        ApplicationIssueSeverity.Information,
                        "Mids Reborn installation was not detected.",
                        Descriptor.ProviderId,
                        now)
                ],
                _timeProvider);
        }

        return ContributorContributionFactory.Create(
            Descriptor,
            ContributorHealth.Ready,
            ContributorActivity.Inactive,
            BuildFacts(status, now),
            [],
            _timeProvider,
            componentVersion: status.HomecomingDatabaseVersion);
    }

    internal static IReadOnlyList<ApplicationFact> BuildFacts(
        MidsInstallationStatus status,
        DateTimeOffset observedAt)
    {
        var databaseAvailable = status.IsValid
            && !string.IsNullOrWhiteSpace(status.HomecomingDatabasePath);

        var facts = new List<ApplicationFact>
        {
            ContributorContributionFactory.BooleanFact(
                ContributorFactKeys.MidsInstallation.Configured,
                status.IsConfigured,
                ApplicationFactScope.Provider,
                observedAt),
            ContributorContributionFactory.BooleanFact(
                ContributorFactKeys.MidsInstallation.DatabaseAvailable,
                databaseAvailable,
                ApplicationFactScope.Provider,
                observedAt,
                display: new ApplicationFactDisplay("Homecoming database available"))
        };

        if (!string.IsNullOrWhiteSpace(status.InstallPath))
        {
            facts.Add(ContributorContributionFactory.TextFact(
                ContributorFactKeys.MidsInstallation.Root,
                status.InstallPath,
                ApplicationFactScope.Provider,
                observedAt,
                display: new ApplicationFactDisplay("Install root", IsDiagnosticOnly: true)));
        }

        if (!string.IsNullOrWhiteSpace(status.ApplicationVersion))
        {
            facts.Add(ContributorContributionFactory.TextFact(
                ContributorFactKeys.MidsInstallation.Version,
                status.ApplicationVersion,
                ApplicationFactScope.Provider,
                observedAt,
                display: new ApplicationFactDisplay("Application version")));
        }

        if (!string.IsNullOrWhiteSpace(status.HomecomingDatabaseVersion))
        {
            facts.Add(ContributorContributionFactory.TextFact(
                ContributorFactKeys.MidsInstallation.DatabaseVersion,
                status.HomecomingDatabaseVersion,
                ApplicationFactScope.Provider,
                observedAt,
                display: new ApplicationFactDisplay("Homecoming database version")));
        }

        return facts;
    }

    private static ApplicationContributorDescriptor CreateDescriptor() =>
        new(
            ApplicationProviders.MidsInstallation,
            "Mids Reborn",
            [ApplicationCapabilities.MidsInstallation, ApplicationCapabilities.MidsHomecomingDatabase],
            [],
            [],
            ApplicationContributorImportance.Optional,
            40)
        {
            Description = "Validated Mids Reborn installation and bundled Homecoming database.",
            IconKey = "mids",
            SchemaVersion = ApplicationContributorDescriptor.ExpectedSchemaVersion
        };
}
