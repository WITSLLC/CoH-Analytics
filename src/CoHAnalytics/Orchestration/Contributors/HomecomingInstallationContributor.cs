using CoHAnalytics.Models;
using CoHAnalytics.Orchestration.Contracts;
using CoHAnalytics.Orchestration.Models;
using CoHAnalytics.Services;

namespace CoHAnalytics.Orchestration.Contributors;

public sealed class HomecomingInstallationContributor : IApplicationContributor
{
    private readonly HomecomingInstallationService _installationService;
    private readonly TimeProvider _timeProvider;

    public HomecomingInstallationContributor(
        HomecomingInstallationService installationService,
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
        var installation = _installationService.CurrentInstallation;
        var discovery = _installationService.LastDiscovery;
        var now = _timeProvider.GetUtcNow();

        if (discovery.MultipleInstallAmbiguity)
        {
            return ContributorContributionFactory.Create(
                Descriptor,
                ContributorHealth.Degraded,
                ContributorActivity.Inactive,
                BuildConfiguredFacts(installation, discovery, now),
                [
                    ContributorContributionFactory.Issue(
                        ContributorIssueCodes.HomecomingInstallation.MultipleFound,
                        ApplicationIssueSeverity.Warning,
                        "Multiple Homecoming installations were detected.",
                        Descriptor.ProviderId,
                        now,
                        detail: string.Join(", ", discovery.AmbiguousInstallRoots))
                ],
                _timeProvider,
                sourceDescription: discovery.FinalSelectedSource?.ToString());
        }

        if (installation is null)
        {
            return ContributorContributionFactory.Create(
                Descriptor,
                ContributorHealth.Unavailable,
                ContributorActivity.Inactive,
                [
                    ContributorContributionFactory.BooleanFact(
                        ContributorFactKeys.HomecomingInstallation.Configured,
                        false,
                        ApplicationFactScope.Provider,
                        now)
                ],
                [
                    ContributorContributionFactory.Issue(
                        ContributorIssueCodes.HomecomingInstallation.NotFound,
                        ApplicationIssueSeverity.Error,
                        "Homecoming installation was not discovered.",
                        Descriptor.ProviderId,
                        now,
                        requiresUserAction: true,
                        detail: "Configure a Homecoming installation in Settings.")
                ],
                _timeProvider);
        }

        return ContributorContributionFactory.Create(
            Descriptor,
            ContributorHealth.Ready,
            ContributorActivity.Inactive,
            BuildConfiguredFacts(installation, discovery, now),
            [],
            _timeProvider,
            sourceDescription: discovery.FinalSelectedSource?.ToString());
    }

    private static IReadOnlyList<ApplicationFact> BuildConfiguredFacts(
        HomecomingInstallation? installation,
        HomecomingInstallDiscoveryDiagnostics discovery,
        DateTimeOffset observedAt)
    {
        var facts = new List<ApplicationFact>
        {
            ContributorContributionFactory.BooleanFact(
                ContributorFactKeys.HomecomingInstallation.Configured,
                installation is not null,
                ApplicationFactScope.Provider,
                observedAt)
        };

        if (installation is null)
        {
            return facts;
        }

        facts.Add(ContributorContributionFactory.TextFact(
            ContributorFactKeys.HomecomingInstallation.Root,
            installation.InstallRoot,
            ApplicationFactScope.Provider,
            observedAt,
            display: new ApplicationFactDisplay("Install root", IsDiagnosticOnly: true)));

        facts.Add(ContributorContributionFactory.TextFact(
            ContributorFactKeys.HomecomingInstallation.Launcher,
            installation.LauncherPath,
            ApplicationFactScope.Provider,
            observedAt,
            display: new ApplicationFactDisplay("Launcher path", IsDiagnosticOnly: true)));

        if (discovery.FinalSelectedSource is { } source)
        {
            facts.Add(ContributorContributionFactory.TextFact(
                ContributorFactKeys.HomecomingInstallation.DiscoverySource,
                source.ToString(),
                ApplicationFactScope.Provider,
                observedAt,
                display: new ApplicationFactDisplay("Discovery source", IsDiagnosticOnly: true)));
        }

        return facts;
    }

    private static ApplicationContributorDescriptor CreateDescriptor() =>
        new(
            ApplicationProviders.HomecomingInstallation,
            "Homecoming Installation",
            [ApplicationCapabilities.HomecomingInstallation],
            [],
            [],
            ApplicationContributorImportance.Critical,
            10)
        {
            Description = "Validated Homecoming installation available to CoH Analytics.",
            IconKey = "homecoming",
            SchemaVersion = ApplicationContributorDescriptor.ExpectedSchemaVersion
        };
}
