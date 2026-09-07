using CoHAnalytics.Models;
using CoHAnalytics.Orchestration.Contracts;
using CoHAnalytics.Orchestration.Models;
using CoHAnalytics.Services;

namespace CoHAnalytics.Orchestration.Contributors;

public sealed class ParserHealthContributorOptions
{
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;
    public TimeSpan ReceivingDataWindow { get; init; } = TimeSpan.FromSeconds(2);
}

/// <summary>
/// Application-facing aggregate adapter for parser I/O and structural classification. It never
/// publishes raw lines, extracted names, paths, byte payloads, or classified events.
/// </summary>
public sealed class ParserHealthContributor
    : IApplicationContributor, IApplicationContributorLifecycle, IDisposable
{
    private readonly IParserManager _manager;
    private readonly ParserHealthContributorOptions _options;
    private bool _disposed;

    public ParserHealthContributor(
        IParserManager manager,
        ParserHealthContributorOptions? options = null)
    {
        _manager = manager;
        _options = options ?? new ParserHealthContributorOptions();
        if (_options.ReceivingDataWindow < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options));
        }

        Descriptor = CreateDescriptor();
        _manager.StateChanged += OnParserStateChanged;
        _manager.ClassificationChanged += OnClassificationChanged;
    }

    public ApplicationContributorDescriptor Descriptor { get; }

    public event EventHandler? ContributionChanged;

    public Task<ApplicationContribution> GetContributionAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(BuildContribution());
    }

    public Task StartAsync(CancellationToken cancellationToken = default) =>
        _manager.StartAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken = default) =>
        _manager.StopAsync(cancellationToken);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _manager.StateChanged -= OnParserStateChanged;
        _manager.ClassificationChanged -= OnClassificationChanged;
    }

    private void OnParserStateChanged(object? sender, ParserManagerChangedEventArgs e) =>
        ContributionChanged?.Invoke(this, EventArgs.Empty);

    private void OnClassificationChanged(object? sender, ParserClassificationChangedEventArgs e) =>
        ContributionChanged?.Invoke(this, EventArgs.Empty);

    private ApplicationContribution BuildContribution()
    {
        var now = _options.TimeProvider.GetUtcNow();
        var parser = _manager.Current;
        var classification = _manager.ClassificationCurrent;
        var diagnostics = _manager.GetDiagnostics();
        var wholeManagerFailure = diagnostics.MonitoringSnapshotQueueOverflowed;
        var allWorkersFaulted = parser.WorkerCount > 0 && parser.FaultedCount == parser.WorkerCount;
        var hasPartialFailure = parser.FaultedCount > 0
            || classification.ClassifierFailureCount > 0
            || diagnostics.EventQueueOverflowed;

        var health = wholeManagerFailure || allWorkersFaulted
            ? ContributorHealth.Error
            : hasPartialFailure
                ? ContributorHealth.Degraded
                : ContributorHealth.Ready;

        var hasReadableWorker = parser.Workers.Any(worker =>
            worker.State is ParserWorkerState.Reading or ParserWorkerState.WaitingForData);
        var activity = health == ContributorHealth.Error
            ? ContributorActivity.Inactive
            : !hasReadableWorker
                ? ContributorActivity.Waiting
                : classification.LastCompleteEventAt is { } lastComplete
                  && now - lastComplete <= _options.ReceivingDataWindow
                    ? ContributorActivity.ReceivingData
                    : parser.ReadingCount > 0
                        ? ContributorActivity.Active
                        : ContributorActivity.Waiting;

        return ContributorContributionFactory.Create(
            Descriptor,
            health,
            activity,
            BuildFacts(parser, classification, now),
            BuildIssues(parser, classification, diagnostics, now),
            _options.TimeProvider);
    }

    private static IReadOnlyList<ApplicationFact> BuildFacts(
        ParserManagerSnapshot parser,
        ParserClassificationSnapshot classification,
        DateTimeOffset observedAt)
    {
        var facts = new List<ApplicationFact>
        {
            Count(ContributorFactKeys.Parser.WorkerCount, parser.WorkerCount, "Parser workers"),
            Count(ContributorFactKeys.Parser.ReadingCount, parser.ReadingCount, "Workers reading"),
            Count(ContributorFactKeys.Parser.WaitingCount, parser.WaitingCount, "Workers waiting"),
            Count(ContributorFactKeys.Parser.SuspendedCount, parser.SuspendedCount, "Workers suspended"),
            Count(ContributorFactKeys.Parser.FaultedCount, parser.FaultedCount, "Workers faulted"),
            Count(ContributorFactKeys.Parser.TotalBytesRead, parser.TotalBytesRead, "Bytes read"),
            Count(ContributorFactKeys.Parser.TotalLinesProcessed, parser.TotalLinesProcessed, "Lines processed"),
            Count(ContributorFactKeys.Parser.TotalClassifiedLines, classification.TotalClassifiedLines, "Lines classified"),
            Count(ContributorFactKeys.Parser.RecognizedLineCount, classification.RecognizedLineCount, "Recognized structural lines"),
            Count(ContributorFactKeys.Parser.UnknownLineCount, classification.UnknownLineCount, "Unknown lines"),
            Count(ContributorFactKeys.Parser.MalformedLineCount, classification.MalformedLineCount, "Malformed lines"),
            Count(ContributorFactKeys.Parser.PotentialIdentityEvidenceCount, classification.PotentialIdentityEvidenceCount, "Potential identity evidence"),
            Count(
                ContributorFactKeys.Parser.ActiveContextCount,
                parser.Workers.Count(worker => worker.State is not ParserWorkerState.Faulted and not ParserWorkerState.Stopped),
                "Active parser contexts")
        };

        if (parser.LastEventAt is { } lastEventAt)
        {
            facts.Add(Timestamp(ContributorFactKeys.Parser.LastEventAt, lastEventAt, "Last parser event"));
        }

        if (classification.LastClassifiedEventAt is { } lastClassifiedAt)
        {
            facts.Add(Timestamp(ContributorFactKeys.Parser.LastClassifiedEventAt, lastClassifiedAt, "Last classified event"));
        }

        return facts;

        ApplicationFact Count(string key, long value, string label) =>
            ContributorContributionFactory.IntegerFact(
                key,
                value,
                ApplicationFactScope.Provider,
                observedAt,
                display: new ApplicationFactDisplay(label));

        ApplicationFact Timestamp(string key, DateTimeOffset value, string label) =>
            ContributorContributionFactory.TimestampFact(
                key,
                value,
                ApplicationFactScope.Provider,
                observedAt,
                display: new ApplicationFactDisplay(label));
    }

    private static IReadOnlyList<ApplicationIssue> BuildIssues(
        ParserManagerSnapshot parser,
        ParserClassificationSnapshot classification,
        ParserManagerDiagnostics diagnostics,
        DateTimeOffset now)
    {
        var issues = new List<ApplicationIssue>();
        foreach (var worker in parser.Workers.Where(worker => worker.State == ParserWorkerState.Faulted))
        {
            var code = worker.FaultCode switch
            {
                "invalid_utf8" => ContributorIssueCodes.Parser.EncodingError,
                "source_io_failure" or "source_access_denied" => ContributorIssueCodes.Parser.ReadFailed,
                _ => ContributorIssueCodes.Parser.WorkerFault
            };
            issues.Add(ContributorContributionFactory.Issue(
                code,
                ApplicationIssueSeverity.Warning,
                "A parser worker stopped after a context-local failure.",
                ApplicationProviders.Parser,
                now,
                detail: worker.FaultCode,
                relatedEntityId: worker.ContextId.ToString()));
        }

        var unavailable = parser.Workers.Count(worker => worker.State == ParserWorkerState.WaitingForSource);
        if (unavailable > 0)
        {
            issues.Add(ContributorContributionFactory.Issue(
                ContributorIssueCodes.Parser.SourceUnavailable,
                ApplicationIssueSeverity.Information,
                $"{unavailable} parser worker(s) are waiting for their assigned source.",
                ApplicationProviders.Parser,
                now));
        }

        if (classification.TooLargeLineCount > 0)
        {
            issues.Add(ContributorContributionFactory.Issue(
                ContributorIssueCodes.Parser.LineTooLarge,
                ApplicationIssueSeverity.Information,
                "One or more parser lines exceeded the bounded line limit.",
                ApplicationProviders.Parser,
                now));
        }

        if (classification.ClassifierFailureCount > 0)
        {
            issues.Add(ContributorContributionFactory.Issue(
                ContributorIssueCodes.Parser.ClassificationFailed,
                ApplicationIssueSeverity.Warning,
                "Structural parser classification failed for one or more events.",
                ApplicationProviders.Parser,
                now));
        }

        if (diagnostics.MonitoringSnapshotQueueOverflowed)
        {
            issues.Add(ContributorContributionFactory.Issue(
                ContributorIssueCodes.Parser.WorkerFault,
                ApplicationIssueSeverity.Error,
                "Parser transition delivery failed.",
                ApplicationProviders.Parser,
                now));
        }

        return issues;
    }

    private static ApplicationContributorDescriptor CreateDescriptor() =>
        new(
            ApplicationProviders.Parser,
            "Chat Log Parser",
            [ApplicationCapabilities.ParserHealth, ApplicationCapabilities.ParserEvents],
            [ApplicationCapabilities.MonitoringContexts],
            [ApplicationCapabilities.LogActivity],
            ApplicationContributorImportance.Important,
            50)
        {
            Description = "Incrementally reads assigned Homecoming chat logs and emits context-tagged parser events.",
            IconKey = "parser",
            SchemaVersion = ApplicationContributorDescriptor.ExpectedSchemaVersion
        };
}
