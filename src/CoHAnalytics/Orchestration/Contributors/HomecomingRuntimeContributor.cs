using CoHAnalytics.Models;
using CoHAnalytics.Orchestration.Contracts;
using CoHAnalytics.Orchestration.Models;
using CoHAnalytics.Services;

namespace CoHAnalytics.Orchestration.Contributors;

public sealed class HomecomingRuntimeContributor : IApplicationContributor, IDisposable
{
    private readonly IGameRuntimeService _runtimeService;
    private readonly TimeProvider _timeProvider;
    private bool _disposed;

    public HomecomingRuntimeContributor(
        IGameRuntimeService runtimeService,
        TimeProvider? timeProvider = null)
    {
        _runtimeService = runtimeService;
        _timeProvider = timeProvider ?? TimeProvider.System;
        Descriptor = CreateDescriptor();
        _runtimeService.StatusChanged += OnRuntimeStatusChanged;
    }

    public ApplicationContributorDescriptor Descriptor { get; }

    public event EventHandler? ContributionChanged;

    public Task<ApplicationContribution> GetContributionAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(BuildContribution());
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _runtimeService.StatusChanged -= OnRuntimeStatusChanged;
    }

    private void OnRuntimeStatusChanged(object? sender, GameRuntimeStatusChangedEventArgs e) =>
        ContributionChanged?.Invoke(this, EventArgs.Empty);

    private ApplicationContribution BuildContribution()
    {
        var status = _runtimeService.CurrentStatus;
        var clientCount = _runtimeService.RunningClientCount;
        var now = _timeProvider.GetUtcNow();
        var (health, activity) = MapStatus(status, clientCount);

        var facts = new List<ApplicationFact>
        {
            ContributorContributionFactory.IntegerFact(
                ContributorFactKeys.HomecomingRuntime.ClientCount,
                clientCount,
                ApplicationFactScope.Provider,
                now,
                display: new ApplicationFactDisplay("Running clients")),
            ContributorContributionFactory.BooleanFact(
                ContributorFactKeys.HomecomingRuntime.IsRunning,
                status == GameRuntimeStatus.Running,
                ApplicationFactScope.Provider,
                now,
                display: new ApplicationFactDisplay("Client running")),
            ContributorContributionFactory.TextFact(
                ContributorFactKeys.HomecomingRuntime.Status,
                status.ToString(),
                ApplicationFactScope.Provider,
                now,
                display: new ApplicationFactDisplay("Runtime status", IsDiagnosticOnly: true))
        };

        var issues = new List<ApplicationIssue>();
        if (status == GameRuntimeStatus.Error)
        {
            issues.Add(ContributorContributionFactory.Issue(
                ContributorIssueCodes.HomecomingRuntime.DetectionError,
                ApplicationIssueSeverity.Warning,
                "Homecoming runtime detection failed.",
                Descriptor.ProviderId,
                now,
                detail: _runtimeService.LastErrorMessage));
        }

        return ContributorContributionFactory.Create(
            Descriptor,
            health,
            activity,
            facts,
            issues,
            _timeProvider);
    }

    internal static (ContributorHealth Health, ContributorActivity Activity) MapStatus(
        GameRuntimeStatus status,
        int runningClientCount)
    {
        return status switch
        {
            GameRuntimeStatus.Unconfigured => (ContributorHealth.Unavailable, ContributorActivity.Inactive),
            GameRuntimeStatus.Off => (ContributorHealth.Ready, ContributorActivity.Inactive),
            GameRuntimeStatus.Running when runningClientCount > 0 =>
                (ContributorHealth.Ready, ContributorActivity.Active),
            GameRuntimeStatus.Running => (ContributorHealth.Ready, ContributorActivity.Inactive),
            GameRuntimeStatus.Error => (ContributorHealth.Error, ContributorActivity.Inactive),
            _ => (ContributorHealth.Unknown, ContributorActivity.Inactive)
        };
    }

    private static ApplicationContributorDescriptor CreateDescriptor() =>
        new(
            ApplicationProviders.HomecomingRuntime,
            "Homecoming Runtime",
            [ApplicationCapabilities.HomecomingRuntime],
            [ApplicationCapabilities.HomecomingInstallation],
            [],
            ApplicationContributorImportance.Important,
            20)
        {
            Description = "Detects running Homecoming game clients.",
            IconKey = "runtime",
            SchemaVersion = ApplicationContributorDescriptor.ExpectedSchemaVersion
        };
}
