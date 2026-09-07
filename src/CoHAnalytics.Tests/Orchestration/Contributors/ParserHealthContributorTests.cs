using CoHAnalytics.Models;
using CoHAnalytics.Orchestration.Contributors;
using CoHAnalytics.Orchestration.Models;
using CoHAnalytics.Services;
using CoHAnalytics.Tests.Orchestration;
using CoHAnalytics.Tests.Services;

namespace CoHAnalytics.Tests.Orchestration.Contributors;

public sealed class ParserHealthContributorTests
{
    [Fact]
    public void Descriptor_matches_parser_architecture()
    {
        var manager = new FakeParserManager();
        using var contributor = new ParserHealthContributor(manager);
        var descriptor = contributor.Descriptor;

        Assert.Equal(ApplicationProviders.Parser, descriptor.ProviderId);
        Assert.Equal("Chat Log Parser", descriptor.DisplayName);
        Assert.Equal([ApplicationCapabilities.ParserHealth, ApplicationCapabilities.ParserEvents], descriptor.Produces);
        Assert.Equal([ApplicationCapabilities.MonitoringContexts], descriptor.Requires);
        Assert.Equal([ApplicationCapabilities.LogActivity], descriptor.Optional);
        Assert.Equal(ApplicationContributorImportance.Important, descriptor.Importance);
        Assert.Equal("parser", descriptor.IconKey);
    }

    [Theory]
    [InlineData(ParserWorkerState.WaitingForSource)]
    [InlineData(ParserWorkerState.WaitingForData)]
    [InlineData(ParserWorkerState.Suspended)]
    public async Task Zero_or_waiting_workers_are_ready_and_waiting(ParserWorkerState state)
    {
        var manager = new FakeParserManager
        {
            Current = Snapshot(Worker(state))
        };
        using var contributor = new ParserHealthContributor(manager);

        var contribution = await contributor.GetContributionAsync();

        Assert.Equal(ContributorHealth.Ready, contribution.Health);
        Assert.Equal(ContributorActivity.Waiting, contribution.Activity);
    }

    [Fact]
    public async Task No_workers_is_ready_and_waiting()
    {
        using var contributor = new ParserHealthContributor(new FakeParserManager());
        var contribution = await contributor.GetContributionAsync();
        Assert.Equal(ContributorHealth.Ready, contribution.Health);
        Assert.Equal(ContributorActivity.Waiting, contribution.Activity);
    }

    [Fact]
    public async Task Reading_worker_is_active_and_recent_complete_event_is_receiving_data()
    {
        var time = new ManualTimeProvider();
        var manager = new FakeParserManager { Current = Snapshot(Worker(ParserWorkerState.Reading)) };
        using var contributor = new ParserHealthContributor(manager, new ParserHealthContributorOptions
        {
            TimeProvider = time,
            ReceivingDataWindow = TimeSpan.FromSeconds(2)
        });

        Assert.Equal(ContributorActivity.Active, (await contributor.GetContributionAsync()).Activity);

        manager.ClassificationCurrent = new ParserClassificationSnapshot
        {
            TotalClassifiedLines = 1,
            RecognizedLineCount = 1,
            LastClassifiedEventAt = time.GetUtcNow(),
            LastCompleteEventAt = time.GetUtcNow(),
            Revision = 1
        };
        Assert.Equal(ContributorActivity.ReceivingData, (await contributor.GetContributionAsync()).Activity);

        time.Advance(TimeSpan.FromSeconds(3));
        Assert.Equal(ContributorActivity.Active, (await contributor.GetContributionAsync()).Activity);
    }

    [Fact]
    public async Task Partial_worker_fault_degrades_but_all_worker_failure_is_error()
    {
        var manager = new FakeParserManager
        {
            Current = Snapshot(Worker(ParserWorkerState.Faulted, "invalid_utf8"), Worker(ParserWorkerState.WaitingForData))
        };
        using var contributor = new ParserHealthContributor(manager);

        var partial = await contributor.GetContributionAsync();
        Assert.Equal(ContributorHealth.Degraded, partial.Health);
        Assert.Equal(ContributorActivity.Waiting, partial.Activity);
        Assert.Contains(partial.Issues, issue => issue.Code == ContributorIssueCodes.Parser.EncodingError);

        manager.Current = Snapshot(Worker(ParserWorkerState.Faulted, "source_io_failure"));
        var whole = await contributor.GetContributionAsync();
        Assert.Equal(ContributorHealth.Error, whole.Health);
        Assert.Equal(ContributorActivity.Inactive, whole.Activity);
        Assert.Contains(whole.Issues, issue => issue.Code == ContributorIssueCodes.Parser.ReadFailed);
    }

    [Fact]
    public async Task Unknown_lines_are_facts_not_health_failures_or_issues()
    {
        var manager = new FakeParserManager
        {
            Current = Snapshot(Worker(ParserWorkerState.WaitingForData)),
            ClassificationCurrent = new ParserClassificationSnapshot
            {
                TotalClassifiedLines = 10,
                UnknownLineCount = 10,
                LastClassifiedEventAt = DateTimeOffset.UnixEpoch,
                Revision = 1
            }
        };
        using var contributor = new ParserHealthContributor(manager);

        var contribution = await contributor.GetContributionAsync();

        Assert.Equal(ContributorHealth.Ready, contribution.Health);
        Assert.Equal(10, IntegerFact(contribution, ContributorFactKeys.Parser.UnknownLineCount));
        Assert.DoesNotContain(contribution.Issues, issue => issue.Code.Contains("unknown", StringComparison.Ordinal));
        Assert.DoesNotContain(contribution.Facts, fact => fact.Value.ToString()!.Contains("chat", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Classification_failure_degrades_and_uses_one_aggregate_safe_issue()
    {
        var manager = new FakeParserManager
        {
            Current = Snapshot(Worker(ParserWorkerState.WaitingForData)),
            ClassificationCurrent = new ParserClassificationSnapshot
            {
                TotalClassifiedLines = 2,
                MalformedLineCount = 1,
                ClassifierFailureCount = 1,
                Revision = 1
            }
        };
        using var contributor = new ParserHealthContributor(manager);

        var contribution = await contributor.GetContributionAsync();

        Assert.Equal(ContributorHealth.Degraded, contribution.Health);
        var issue = Assert.Single(contribution.Issues, issue => issue.Code == ContributorIssueCodes.Parser.ClassificationFailed);
        Assert.DoesNotContain("name", issue.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Whole_manager_transition_delivery_failure_is_error_and_inactive()
    {
        var manager = new FakeParserManager
        {
            Current = Snapshot(Worker(ParserWorkerState.WaitingForData)),
            MonitoringOverflow = true
        };
        using var contributor = new ParserHealthContributor(manager);

        var contribution = await contributor.GetContributionAsync();

        Assert.Equal(ContributorHealth.Error, contribution.Health);
        Assert.Equal(ContributorActivity.Inactive, contribution.Activity);
        Assert.Contains(contribution.Issues, issue =>
            issue.Code == ContributorIssueCodes.Parser.WorkerFault
            && issue.Severity == ApplicationIssueSeverity.Error);
    }

    [Fact]
    public async Task Facts_are_complete_provider_scoped_and_contain_no_raw_payloads()
    {
        var now = DateTimeOffset.UnixEpoch.AddHours(1);
        var manager = new FakeParserManager
        {
            Current = ParserManagerSnapshot.Create([Worker(ParserWorkerState.WaitingForData)], now, now, 1),
            ClassificationCurrent = new ParserClassificationSnapshot
            {
                TotalClassifiedLines = 4,
                RecognizedLineCount = 2,
                UnknownLineCount = 1,
                MalformedLineCount = 1,
                PotentialIdentityEvidenceCount = 1,
                LastClassifiedEventAt = now,
                Revision = 1
            }
        };
        using var contributor = new ParserHealthContributor(manager);

        var contribution = await contributor.GetContributionAsync();

        Assert.Equal(4, IntegerFact(contribution, ContributorFactKeys.Parser.TotalClassifiedLines));
        Assert.Equal(2, IntegerFact(contribution, ContributorFactKeys.Parser.RecognizedLineCount));
        Assert.Equal(1, IntegerFact(contribution, ContributorFactKeys.Parser.MalformedLineCount));
        Assert.Equal(1, IntegerFact(contribution, ContributorFactKeys.Parser.PotentialIdentityEvidenceCount));
        Assert.All(contribution.Facts, fact => Assert.Equal(ApplicationFactScope.Provider, fact.Scope));
        Assert.Empty(contribution.Actions);
    }

    [Fact]
    public async Task Lifecycle_is_owned_once_and_disposal_unsubscribes_both_invalidations()
    {
        var manager = new FakeParserManager();
        var contributor = new ParserHealthContributor(manager);
        var invalidations = 0;
        contributor.ContributionChanged += (_, _) => invalidations++;

        await contributor.StartAsync();
        await contributor.StopAsync();
        manager.RaiseStateChanged();
        manager.RaiseClassificationChanged();
        Assert.Equal(2, invalidations);
        Assert.Equal(1, manager.StartCount);
        Assert.Equal(1, manager.StopCount);

        contributor.Dispose();
        manager.RaiseStateChanged();
        manager.RaiseClassificationChanged();
        Assert.Equal(2, invalidations);
    }

    private static ParserManagerSnapshot Snapshot(params ParserWorkerSnapshot[] workers) =>
        ParserManagerSnapshot.Create(workers, workers.Select(worker => worker.LastEventAt).Max(), DateTimeOffset.UnixEpoch, 1);

    private static ParserWorkerSnapshot Worker(ParserWorkerState state, string? faultCode = null) =>
        new()
        {
            WorkerId = ParserWorkerId.CreateNew(),
            ContextId = MonitoringContextId.CreateNew(),
            State = state,
            AppliedSourceBindingGeneration = 1,
            LastAppliedTransitionKind = MonitoringSourceTransitionKind.SourceAssigned,
            RecentSegments = [],
            TotalBytesRead = 100,
            TotalLinesEmitted = 2,
            LastEventSequence = 2,
            FaultCode = faultCode,
            FaultMessage = faultCode is null ? null : "Safe fault"
        };

    private static long IntegerFact(ApplicationContribution contribution, string key) =>
        Assert.IsType<ApplicationFactValue.Integer>(contribution.Facts.Single(fact => fact.Key == key).Value).Value;

    private sealed class FakeParserManager : IParserManager
    {
        public ParserManagerSnapshot Current { get; set; } = ParserManagerSnapshot.Empty;
        public ParserClassificationSnapshot ClassificationCurrent { get; set; } = ParserClassificationSnapshot.Empty;
        public bool MonitoringOverflow { get; set; }
        public bool EventOverflow { get; set; }
        public int StartCount { get; private set; }
        public int StopCount { get; private set; }

        public event EventHandler<ParserManagerChangedEventArgs>? StateChanged;
        public event EventHandler<ParserEventsAvailableEventArgs>? EventsAvailable
        {
            add { }
            remove { }
        }
        public event EventHandler<ParserClassificationChangedEventArgs>? ClassificationChanged;
        public event EventHandler<ParserEventsClassifiedEventArgs>? ClassifiedEventsAvailable
        {
            add { }
            remove { }
        }

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            StartCount++;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            StopCount++;
            return Task.CompletedTask;
        }

        public ParserManagerDiagnostics GetDiagnostics() =>
            GameplaySessionTestInfrastructure.IdleParserDiagnostics(
                monitoringSnapshotQueueOverflowed: MonitoringOverflow,
                eventQueueOverflowed: EventOverflow);

        public ParserClassificationDiagnostics GetClassificationDiagnostics() => new()
        {
            SnapshotRevision = ClassificationCurrent.Revision,
            TotalClassifiedLines = ClassificationCurrent.TotalClassifiedLines,
            RecognizedLineCount = ClassificationCurrent.RecognizedLineCount,
            UnknownLineCount = ClassificationCurrent.UnknownLineCount,
            MalformedLineCount = ClassificationCurrent.MalformedLineCount,
            PotentialIdentityEvidenceCount = ClassificationCurrent.PotentialIdentityEvidenceCount,
            ClassifierFailureCount = ClassificationCurrent.ClassifierFailureCount,
            LastClassifiedEventAt = ClassificationCurrent.LastClassifiedEventAt,
            RuleMatchCounts = new Dictionary<string, long>(),
            RecentClassifierFailures = []
        };

        public void RaiseStateChanged() => StateChanged?.Invoke(this, new ParserManagerChangedEventArgs(Current));
        public void RaiseClassificationChanged() => ClassificationChanged?.Invoke(this, new ParserClassificationChangedEventArgs(ClassificationCurrent));
    }
}
