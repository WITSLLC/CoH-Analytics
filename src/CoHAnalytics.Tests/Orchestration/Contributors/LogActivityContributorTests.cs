using System.IO;
using CoHAnalytics.Models;
using CoHAnalytics.Orchestration.Contributors;
using CoHAnalytics.Orchestration.Models;
using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Orchestration.Contributors;

public sealed class LogActivityContributorTests
{
    private static readonly DateOnly Today = new(2026, 8, 4);

    [Fact]
    public void Descriptor_declares_the_expected_identity_capabilities_and_dependencies()
    {
        using var contributor = CreateContributor(out _);
        var descriptor = contributor.Descriptor;

        Assert.Equal(ApplicationProviders.LogActivity, descriptor.ProviderId);
        Assert.Equal("log.activity", descriptor.ProviderId);
        Assert.Equal("Chat Log Activity", descriptor.DisplayName);
        Assert.Equal(
            "Observes Homecoming account chat-log files and reports candidate activity.",
            descriptor.Description);
        Assert.Equal("monitoring", descriptor.IconKey);
        Assert.Equal(ApplicationContributorDescriptor.ExpectedSchemaVersion, descriptor.SchemaVersion);
        Assert.Equal(ApplicationContributorImportance.Important, descriptor.Importance);

        Assert.Equal(
            [ApplicationCapabilities.LogDiscovery, ApplicationCapabilities.LogActivity],
            descriptor.Produces);
        Assert.Equal([ApplicationCapabilities.AccountDiscovery], descriptor.Requires);
        Assert.Equal([ApplicationCapabilities.HomecomingRuntime], descriptor.Optional);
        Assert.Equal(
            [ApplicationCapabilities.AccountDiscovery, ApplicationCapabilities.HomecomingRuntime],
            descriptor.Consumes);
    }

    [Fact]
    public async Task No_discovered_accounts_reports_ready_and_waiting_without_issues()
    {
        using var contributor = CreateContributor(out var service);
        service.Snapshot = Snapshot([], accountCount: 0, logsFolderCount: 0);

        var contribution = await contributor.GetContributionAsync();

        Assert.Equal(ContributorHealth.Ready, contribution.Health);
        Assert.Equal(ContributorActivity.Waiting, contribution.Activity);
        Assert.Empty(contribution.Issues);
        Assert.Equal(0, IntegerFact(contribution, ContributorFactKeys.LogActivity.AccountCount));
    }

    [Fact]
    public async Task Accounts_without_logs_folders_report_ready_and_waiting_without_errors()
    {
        using var contributor = CreateContributor(out var service);
        service.Snapshot = Snapshot([], accountCount: 3, logsFolderCount: 0);

        var contribution = await contributor.GetContributionAsync();

        Assert.Equal(ContributorHealth.Ready, contribution.Health);
        Assert.Equal(ContributorActivity.Waiting, contribution.Activity);
        Assert.Empty(contribution.Issues);
        Assert.Equal(3, IntegerFact(contribution, ContributorFactKeys.LogActivity.AccountCount));
        Assert.Equal(0, IntegerFact(contribution, ContributorFactKeys.LogActivity.LogsFolderCount));
    }

    [Fact]
    public async Task Historical_files_alone_never_report_receiving_data()
    {
        using var contributor = CreateContributor(out var service);
        service.Snapshot = Snapshot(
            [
                Candidate("Alpha", Today.AddDays(-1), LogSourceActivityState.Historical),
                Candidate("Alpha", Today, LogSourceActivityState.Waiting)
            ],
            accountCount: 1,
            logsFolderCount: 1);

        var contribution = await contributor.GetContributionAsync();

        Assert.Equal(ContributorHealth.Ready, contribution.Health);
        Assert.Equal(ContributorActivity.Waiting, contribution.Activity);
        Assert.Equal(1, IntegerFact(contribution, ContributorFactKeys.LogActivity.HistoricalCount));
        Assert.Equal(0, IntegerFact(contribution, ContributorFactKeys.LogActivity.GrowingCount));
    }

    [Fact]
    public async Task One_observed_growing_source_reports_receiving_data()
    {
        using var contributor = CreateContributor(out var service);
        var growth = new DateTimeOffset(2026, 8, 4, 12, 0, 0, TimeSpan.Zero);
        service.Snapshot = Snapshot(
            [Candidate("Alpha", Today, LogSourceActivityState.Growing, lastGrowthAt: growth)],
            accountCount: 1,
            logsFolderCount: 1);

        var contribution = await contributor.GetContributionAsync();

        Assert.Equal(ContributorHealth.Ready, contribution.Health);
        Assert.Equal(ContributorActivity.ReceivingData, contribution.Activity);
        Assert.Equal(1, IntegerFact(contribution, ContributorFactKeys.LogActivity.GrowingCount));
        Assert.Equal(
            growth,
            Assert.IsType<ApplicationFactValue.Timestamp>(
                Fact(contribution, ContributorFactKeys.LogActivity.LastGrowthAt).Value).Value);
    }

    [Fact]
    public async Task Multiple_growing_sources_are_counted_without_selecting_a_primary()
    {
        using var contributor = CreateContributor(out var service);
        service.Snapshot = Snapshot(
            [
                Candidate("Alpha", Today, LogSourceActivityState.Growing, lastGrowthAt: DateTimeOffset.UnixEpoch),
                Candidate("Beta", Today, LogSourceActivityState.Growing, lastGrowthAt: DateTimeOffset.UnixEpoch)
            ],
            accountCount: 2,
            logsFolderCount: 2);

        var contribution = await contributor.GetContributionAsync();

        Assert.Equal(ContributorActivity.ReceivingData, contribution.Activity);
        Assert.Equal(2, IntegerFact(contribution, ContributorFactKeys.LogActivity.GrowingCount));
        Assert.Equal(2, IntegerFact(contribution, ContributorFactKeys.LogActivity.CandidateCount));
    }

    [Fact]
    public async Task Inaccessible_source_alongside_a_healthy_one_reports_degraded_with_the_highest_activity()
    {
        using var contributor = CreateContributor(out var service);
        var inaccessible = Candidate(
            "Alpha",
            Today.AddDays(-1),
            LogSourceActivityState.Unavailable,
            changeKind: LogSourceChangeKind.Inaccessible,
            unavailableReason: "Access to the path is denied.");

        service.Snapshot = Snapshot(
            [
                inaccessible,
                Candidate("Beta", Today, LogSourceActivityState.Growing, lastGrowthAt: DateTimeOffset.UnixEpoch)
            ],
            accountCount: 2,
            logsFolderCount: 2);

        var contribution = await contributor.GetContributionAsync();

        Assert.Equal(ContributorHealth.Degraded, contribution.Health);
        Assert.Equal(ContributorActivity.ReceivingData, contribution.Activity);

        var issue = Assert.Single(contribution.Issues);
        Assert.Equal(ContributorIssueCodes.LogActivity.SourceUnavailable, issue.Code);
        Assert.Equal(ApplicationIssueSeverity.Warning, issue.Severity);
        Assert.Equal(inaccessible.SourceId.Value, issue.RelatedEntityId);
        Assert.Equal("Access to the path is denied.", issue.Detail);
        Assert.Equal($"log.activity/log.source_unavailable/{inaccessible.SourceId.Value}", issue.DedupeKey);
    }

    [Fact]
    public async Task Deleted_historical_source_that_never_grew_produces_no_issue()
    {
        using var contributor = CreateContributor(out var service);
        service.Snapshot = Snapshot(
            [
                Candidate(
                    "Alpha",
                    Today.AddDays(-5),
                    LogSourceActivityState.Unavailable,
                    changeKind: LogSourceChangeKind.Disappeared,
                    unavailableReason: "File no longer exists at its observed path.")
            ],
            accountCount: 1,
            logsFolderCount: 1);

        var contribution = await contributor.GetContributionAsync();

        Assert.Equal(ContributorHealth.Ready, contribution.Health);
        Assert.Empty(contribution.Issues);
        Assert.Equal(1, IntegerFact(contribution, ContributorFactKeys.LogActivity.UnavailableCount));
    }

    [Fact]
    public async Task Bulk_source_loss_is_summarized_as_one_issue()
    {
        using var contributor = CreateContributor(out var service);
        service.Snapshot = Snapshot(
            [
                .. Enumerable.Range(0, 4).Select(index => Candidate(
                    "Alpha",
                    Today.AddDays(-index),
                    LogSourceActivityState.Unavailable,
                    changeKind: LogSourceChangeKind.Inaccessible,
                    unavailableReason: "Access to the path is denied."))
            ],
            accountCount: 1,
            logsFolderCount: 1);

        var contribution = await contributor.GetContributionAsync();

        var issue = Assert.Single(contribution.Issues);
        Assert.Equal(ContributorIssueCodes.LogActivity.SourceUnavailable, issue.Code);
        Assert.Null(issue.RelatedEntityId);
        Assert.Contains("4", issue.Summary);
    }

    [Fact]
    public async Task Service_failure_reports_error_and_a_normalized_issue()
    {
        using var contributor = CreateContributor(out var service);
        service.Snapshot = Snapshot([], accountCount: 1, logsFolderCount: 1);
        service.LastScanFailureMessage = "Scan failed: the device is not ready.";

        var contribution = await contributor.GetContributionAsync();

        Assert.Equal(ContributorHealth.Error, contribution.Health);
        Assert.Equal(ContributorActivity.Inactive, contribution.Activity);

        var issue = Assert.Single(contribution.Issues);
        Assert.Equal(ContributorIssueCodes.LogActivity.ObservationFailed, issue.Code);
        Assert.Equal(ApplicationIssueSeverity.Error, issue.Severity);
        Assert.Equal("Scan failed: the device is not ready.", issue.Detail);
        Assert.False(issue.RequiresUserAction);
    }

    [Fact]
    public async Task Aggregate_facts_use_stable_keys_integer_types_and_provider_scope()
    {
        using var contributor = CreateContributor(out var service);
        service.Snapshot = Snapshot(
            [Candidate("Alpha", Today, LogSourceActivityState.Growing, lastGrowthAt: DateTimeOffset.UnixEpoch)],
            accountCount: 1,
            logsFolderCount: 1);

        var contribution = await contributor.GetContributionAsync();

        string[] expectedCountKeys =
        [
            "log.account_count",
            "log.logs_folder_count",
            "log.candidate_count",
            "log.growing_count",
            "log.inactive_count",
            "log.historical_count",
            "log.unavailable_count",
            "log.rollover_candidate_count"
        ];

        Assert.Equal(
            [.. expectedCountKeys, "log.last_growth_at"],
            contribution.Facts.Select(fact => fact.Key).ToArray());

        Assert.All(expectedCountKeys, key =>
            Assert.IsType<ApplicationFactValue.Integer>(Fact(contribution, key).Value));
        Assert.All(contribution.Facts, fact =>
        {
            Assert.Equal(ApplicationFactScope.Provider, fact.Scope);
            Assert.Equal(ApplicationFactConfidence.Observed, fact.Confidence);
        });
    }

    [Fact]
    public async Task Contribution_makes_no_logging_enabled_character_or_session_claim()
    {
        using var contributor = CreateContributor(out var service);
        service.Snapshot = Snapshot(
            [Candidate("Alpha", Today, LogSourceActivityState.Growing, lastGrowthAt: DateTimeOffset.UnixEpoch)],
            accountCount: 1,
            logsFolderCount: 1);

        var contribution = await contributor.GetContributionAsync();

        string[] forbiddenTerms =
        [
            "logchat", "log chat", "enabled", "disabled", "character", "session", "context",
            "parser", "selected", "assigned"
        ];

        var text = string.Join(
            " ",
            contribution.Facts.Select(fact => $"{fact.Key} {fact.Display?.Label}")
                .Concat(contribution.Issues.Select(issue => $"{issue.Code} {issue.Summary} {issue.Detail}")));

        Assert.All(forbiddenTerms, term =>
            Assert.DoesNotContain(term, text, StringComparison.OrdinalIgnoreCase));

        // No per-source detail is published through orchestration facts.
        Assert.All(contribution.Facts, fact => Assert.Null(fact.RelatedEntityId));
        Assert.Empty(contribution.Actions);
    }

    [Fact]
    public void Service_change_event_is_forwarded_as_a_data_free_hint()
    {
        using var contributor = CreateContributor(out var service);
        var hints = 0;
        object? payload = null;

        contributor.ContributionChanged += (_, args) =>
        {
            hints++;
            payload = args;
        };

        service.RaiseActivityChanged();

        Assert.Equal(1, hints);
        Assert.Same(EventArgs.Empty, payload);

        contributor.Dispose();
        service.RaiseActivityChanged();
        Assert.Equal(1, hints);
    }

    [Fact]
    public async Task Lifecycle_starts_and_stops_the_wrapped_service_exactly_once()
    {
        using var contributor = CreateContributor(out var service);

        await contributor.StartAsync();
        await contributor.StopAsync();

        Assert.Equal(1, service.StartCount);
        Assert.Equal(1, service.StopCount);
    }

    [Fact]
    public void Dispose_is_idempotent_and_does_not_dispose_the_service()
    {
        var contributor = CreateContributor(out var service);

        contributor.Dispose();
        contributor.Dispose();

        Assert.False(service.IsDisposed);
    }

    private static LogActivityContributor CreateContributor(out FakeLogActivityService service)
    {
        service = new FakeLogActivityService();
        return new LogActivityContributor(service, new ManualTimeProvider());
    }

    private static LogActivitySnapshot Snapshot(
        IEnumerable<LogSourceCandidate> candidates,
        int accountCount,
        int logsFolderCount) =>
        LogActivitySnapshot.Create(
            candidates,
            accountCount,
            logsFolderCount,
            new DateTimeOffset(2026, 8, 4, 12, 0, 0, TimeSpan.Zero),
            1);

    private static LogSourceCandidate Candidate(
        string accountDisplayName,
        DateOnly logDate,
        LogSourceActivityState state,
        LogSourceChangeKind changeKind = LogSourceChangeKind.Unchanged,
        DateTimeOffset? lastGrowthAt = null,
        string? unavailableReason = null)
    {
        var observedAt = new DateTimeOffset(2026, 8, 4, 12, 0, 0, TimeSpan.Zero);
        var filePath = Path.Combine(
            Path.GetTempPath(),
            "accounts",
            accountDisplayName,
            "Logs",
            $"chatlog {logDate:yyyy-MM-dd}.txt");
        var accountStableId = $"stable-{accountDisplayName}";
        var sourceId = LogSourceId.Create(accountStableId, accountDisplayName, filePath, logDate);

        return new LogSourceCandidate
        {
            SourceId = sourceId,
            AccountStableId = accountStableId,
            AccountDisplayName = accountDisplayName,
            FilePath = sourceId.FilePath,
            FileName = sourceId.FileName,
            LogDate = logDate,
            Exists = state != LogSourceActivityState.Unavailable,
            Length = 100,
            PreviousLength = 100,
            FirstObservedAt = observedAt,
            LastObservedAt = observedAt,
            FirstGrowthAt = lastGrowthAt,
            LastGrowthAt = lastGrowthAt,
            ActivityState = state,
            LastChangeKind = changeKind,
            IsCurrentDailyFile = logDate == Today,
            UnavailableReason = unavailableReason
        };
    }

    private static ApplicationFact Fact(ApplicationContribution contribution, string key) =>
        Assert.Single(contribution.Facts, fact => fact.Key == key);

    private static long IntegerFact(ApplicationContribution contribution, string key) =>
        Assert.IsType<ApplicationFactValue.Integer>(Fact(contribution, key).Value).Value;

    private sealed class FakeLogActivityService : ILogActivityService, IDisposable
    {
        public LogActivitySnapshot Snapshot { get; set; } = LogActivitySnapshot.Empty;

        public LogActivitySnapshot Current => Snapshot;

        public bool IsRunning { get; private set; }

        public string? LastScanFailureMessage { get; set; }

        public int StartCount { get; private set; }

        public int StopCount { get; private set; }

        public bool IsDisposed { get; private set; }

        public event EventHandler<LogActivityChangedEventArgs>? ActivityChanged;

        public void RaiseActivityChanged() =>
            ActivityChanged?.Invoke(this, new LogActivityChangedEventArgs(Snapshot));

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            StartCount++;
            IsRunning = true;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            StopCount++;
            IsRunning = false;
            return Task.CompletedTask;
        }

        public Task<LogActivitySnapshot> ScanAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Snapshot);

        public LogActivityDiagnostics GetDiagnostics() => new()
        {
            PollInterval = TimeSpan.FromSeconds(1),
            InactivityThreshold = TimeSpan.FromSeconds(30),
            IsPollingEnabled = false,
            IsRunning = IsRunning,
            ScanCount = 0,
            LastSnapshotRevision = Snapshot.Revision,
            SuppressedNoOpScanCount = 0,
            Accounts = [],
            Sources = [],
            RecentScanFailures = []
        };

        public void Dispose() => IsDisposed = true;
    }
}
