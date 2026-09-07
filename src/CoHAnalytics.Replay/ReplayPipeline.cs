using System.Text;
using CoHAnalytics.Models;
using CoHAnalytics.Services;

namespace CoHAnalytics.Replay;

public sealed class ReplayPipeline : IAsyncDisposable
{
    private readonly TimeProvider _timeProvider;
    private readonly TimeProvider _lifecycleTimeProvider;
    private readonly ReplayLifecycleWaiter _lifecycleWaiter;
    private readonly ReplayLifecycleTimeouts _lifecycleTimeouts;
    private readonly ReplayPerformanceOptions _performanceOptions;
    private readonly string _characterDataDirectory;
    private ReplayServiceHost? _host;

    public ReplayPipeline(TimeProvider timeProvider, string? characterDataDirectory = null)
        : this(
            timeProvider,
            characterDataDirectory,
            TimeProvider.System,
            new ReplayLifecycleTimeouts(),
            new ReplayPerformanceOptions { TimeProvider = timeProvider })
    {
    }

    public ReplayPipeline(
        TimeProvider timeProvider,
        string? characterDataDirectory,
        ReplayPerformanceOptions performanceOptions)
        : this(
            timeProvider,
            characterDataDirectory,
            TimeProvider.System,
            new ReplayLifecycleTimeouts(),
            performanceOptions)
    {
    }

    internal ReplayPipeline(
        TimeProvider timeProvider,
        string? characterDataDirectory,
        TimeProvider lifecycleTimeProvider,
        ReplayLifecycleTimeouts lifecycleTimeouts,
        ReplayPerformanceOptions? performanceOptions = null)
    {
        _timeProvider = timeProvider;
        _lifecycleTimeProvider = lifecycleTimeProvider;
        _lifecycleWaiter = new ReplayLifecycleWaiter(lifecycleTimeProvider);
        _lifecycleTimeouts = lifecycleTimeouts;
        _performanceOptions = CreateEffectivePerformanceOptions(timeProvider, performanceOptions);
        _characterDataDirectory = characterDataDirectory
            ?? Path.Combine(Path.GetTempPath(), "coh-analytics-replay-characters", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_characterDataDirectory);
    }

    public async Task<ReplayPipelineResult> ExecuteAsync(
        ReplayPipelineRequest request,
        ReplayLedger ledger,
        ReplayTimeline timeline,
        CancellationToken cancellationToken = default)
    {
        var oracle = new ReplayCorrectnessOracle();
        var lifecycleSignal = new ReplayLifecycleSignal();
        var performanceCollector = new ReplayPerformanceCollector(_performanceOptions);
        performanceCollector.Start(timeline);
        _host = CreateServiceHost(
            request.Contexts.Select(binding => binding.Workspace).ToArray(),
            ResolveGameplayOptions(request),
            ResolveParserOptions(request));

        var latencyTracker = request.LatencyTracker;
        var profileResolution = request.ProfileResolution;
        if (profileResolution is not null)
        {
            timeline.Record(
                ReplayTimelineCategory.Analytics,
                "profile.started",
                ReplayTimelineRetentionClass.Milestone,
                value: 0,
                unit: profileResolution.Profile.Name);
            if (profileResolution.ExpectedOverloadTarget != ReplayExpectedOverloadBehavior.None)
            {
                timeline.Record(
                    ReplayTimelineCategory.Analytics,
                    "overload.expected",
                    ReplayTimelineRetentionClass.Milestone);
            }
        }

        void OnLogActivityChanged(object? _, LogActivityChangedEventArgs __) => lifecycleSignal.Pulse();
        void OnMonitoringChanged(object? _, MonitoringSessionManagerChangedEventArgs __) => lifecycleSignal.Pulse();
        void OnParserChanged(object? _, ParserManagerChangedEventArgs __) => lifecycleSignal.Pulse();
        void OnGameplayChanged(object? _, GameplaySessionManagerChangedEventArgs __) => lifecycleSignal.Pulse();
        void OnRaw(object? _, ParserEventsAvailableEventArgs args)
        {
            oracle.ObserveRawEvents(args.Events);
            if (latencyTracker is not null)
            {
                foreach (var parserEvent in args.Events)
                {
                    latencyTracker.ObserveRawLine(parserEvent.RawLine);
                }
            }

            lifecycleSignal.Pulse();
        }

        void OnClassified(object? _, ParserEventsClassifiedEventArgs args)
        {
            oracle.ObserveClassifiedEvents(args.Events);
            lifecycleSignal.Pulse();
        }

        void OnCommitted(object? _, GameplaySessionEventsAvailableEventArgs args)
        {
            oracle.ObserveCommittedEvents(args.Events);
            if (latencyTracker is not null)
            {
                foreach (var gameplayEvent in args.Events)
                {
                    latencyTracker.ObserveCommittedLine(gameplayEvent.ParserEvent.RawLine);
                }
            }

            lifecycleSignal.Pulse();
        }

        _host.LogActivity.ActivityChanged += OnLogActivityChanged;
        _host.Monitoring.StateChanged += OnMonitoringChanged;
        _host.Parser.StateChanged += OnParserChanged;
        _host.Gameplay.StateChanged += OnGameplayChanged;
        _host.Parser.EventsAvailable += OnRaw;
        _host.Parser.ClassifiedEventsAvailable += OnClassified;
        _host.Gameplay.CommittedEventsAvailable += OnCommitted;

        var hostStopped = false;
        try
        {
            timeline.Record(
                ReplayTimelineCategory.Pipeline,
                "monitoring.started",
                ReplayTimelineRetentionClass.Milestone);

            await _host.StartAsync(cancellationToken).ConfigureAwait(false);

            timeline.Record(
                ReplayTimelineCategory.Pipeline,
                "parser.started",
                ReplayTimelineRetentionClass.Milestone);
            timeline.Record(
                ReplayTimelineCategory.Pipeline,
                "gameplay.started",
                ReplayTimelineRetentionClass.Milestone);

            await _host.LogActivity.ScanAsync(cancellationToken).ConfigureAwait(false);

            var expectedWelcome = 0;
            var seededBindings = new List<(ReplayContextBinding Binding, ReplayLedger ContextLedger)>();
            foreach (var binding in request.Contexts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                EnsureEmptyDestinationLogs(binding);
                await _host.LogActivity.ScanAsync(cancellationToken).ConfigureAwait(false);
                var seededContext = SeedMonitoringContext(_host.Monitoring, binding.Workspace, binding.Plan);
                await WaitForSeededContextReadyAsync(seededContext, lifecycleSignal, cancellationToken)
                    .ConfigureAwait(false);

                var contextLedger = request.Contexts.Count == 1 ? ledger : new ReplayLedger();
                seededBindings.Add((binding, contextLedger));
                expectedWelcome += CountExpectedWelcomeBoundaries(binding.Plan);
                await RefreshLifecycleStateAsync(oracle, cancellationToken).ConfigureAwait(false);
            }

            if (latencyTracker is { Enabled: true })
            {
                timeline.Record(
                    ReplayTimelineCategory.Analytics,
                    "latency.sampling.started",
                    ReplayTimelineRetentionClass.Milestone);
            }

            ReplayAggregatedLedger? aggregatedLedger = null;
            if (profileResolution?.Profile.UseConcurrentContexts == true)
            {
                var coordinator = new ReplayLoadCoordinator(_timeProvider, timeline);
                var jobs = seededBindings
                    .Select(
                        (entry, index) => new ReplayLoadContextJob
                        {
                            ContextIndex = index,
                            Label = profileResolution.ContextPlans.ElementAtOrDefault(index)?.Label
                                ?? $"context-{index + 1}",
                            Workspace = entry.Binding.Workspace,
                            Plan = entry.Binding.Plan,
                            Ledger = entry.ContextLedger,
                            LatencyTracker = latencyTracker,
                            BetweenSegmentsAsync = CreateBetweenSegmentsHandler(
                                entry.Binding,
                                oracle,
                                lifecycleSignal,
                                timeline,
                                cancellationToken)
                        })
                    .ToArray();
                var coordinatorResult = await coordinator.ExecuteAsync(
                        jobs,
                        performanceCollector,
                        cancellationToken)
                    .ConfigureAwait(false);
                aggregatedLedger = coordinatorResult.AggregatedLedger;
                foreach (var contextResult in coordinatorResult.ContextResults)
                {
                    var plan = seededBindings[contextResult.ContextIndex].Binding.Plan;
                    contextResult.Ledger.Validate(plan);
                }
            }
            else
            {
                foreach (var (binding, contextLedger) in seededBindings)
                {
                    performanceCollector.BeginWrite();
                    var writeStartTimestamp = _timeProvider.GetTimestamp();
                    if (string.Equals(
                            profileResolution?.Profile.Name,
                            "expected-overload",
                            StringComparison.Ordinal)
                        && profileResolution is not null)
                    {
                        var overloadProfile = profileResolution.Profile;
                        var writer = new ReplayFileWriter(_timeProvider, contextLedger, timeline);
                        await ReplayExpectedOverloadAdmission.ExecuteAsync(
                                writer,
                                binding.Plan,
                                binding.Workspace,
                                timeline,
                                overloadProfile.ExpectedOverloadAdmissionLineCount
                                ?? ReplayExpectedOverloadAdmission.DefaultAdmissionLineCount,
                                cancellationToken,
                                latencyTracker)
                            .ConfigureAwait(false);
                        ReplayExpectedOverloadAdmission.ReconcileSegmentAccounting(contextLedger, 1);
                        contextLedger.Validate(binding.Plan);
                    }
                    else
                    {
                        var writer = new ReplayFileWriter(_timeProvider, contextLedger, timeline);
                        await writer.ExecuteAsync(
                                binding.Plan,
                                binding.Workspace,
                                cancellationToken,
                                betweenSegmentsAsync: CreateBetweenSegmentsHandler(
                                    binding,
                                    oracle,
                                    lifecycleSignal,
                                    timeline,
                                    cancellationToken),
                                latencyTracker: latencyTracker)
                            .ConfigureAwait(false);
                    }

                    var writeEndTimestamp = _timeProvider.GetTimestamp();
                    performanceCollector.EndWrite(contextLedger);

                    if (!ReferenceEquals(contextLedger, ledger))
                    {
                        contextLedger.Validate(binding.Plan);
                    }

                    await RefreshLifecycleStateAsync(oracle, cancellationToken).ConfigureAwait(false);
                    _ = writeStartTimestamp;
                    _ = writeEndTimestamp;
                }
            }

            if (latencyTracker is not null)
            {
                timeline.Record(
                    ReplayTimelineCategory.Analytics,
                    "latency.sampling.completed",
                    ReplayTimelineRetentionClass.Milestone);
            }

            var expectedLines = request.Contexts.Count == 1
                ? ledger.OriginalSourceCompleteLines + ledger.BootstrapCompleteLines
                : request.Contexts.Sum(binding =>
                    binding.Plan.Segments.Sum(segment =>
                        ReplayLineAccounting.Analyze(File.ReadAllBytes(segment.SourcePath)).CompleteLines)
                    + (binding.Plan.InputMode == ReplayInputMode.Bootstrap ? 1L : 0L));
            performanceCollector.BeginDrain();
            var drainResult = await WaitForDrainAsync(
                    oracle,
                    expectedLines,
                    expectedWelcome,
                    timeline,
                    lifecycleSignal,
                    performanceCollector,
                    cancellationToken)
                .ConfigureAwait(false);
            performanceCollector.EndDrain(drainResult, timeline);

            var parserSnapshot = _host.Parser.Current;
            var parserDiagnostics = _host.Parser.GetDiagnostics();
            var gameplayDiagnostics = _host.Gameplay.GetDiagnostics();
            var gameplaySnapshot = _host.Gameplay.Current;
            var monitoringSnapshot = _host.Monitoring.Current;

            await _host.StopAsync().ConfigureAwait(false);
            hostStopped = true;

            _host.LogActivity.ActivityChanged -= OnLogActivityChanged;
            _host.Monitoring.StateChanged -= OnMonitoringChanged;
            _host.Parser.StateChanged -= OnParserChanged;
            _host.Gameplay.StateChanged -= OnGameplayChanged;
            _host.Parser.EventsAvailable -= OnRaw;
            _host.Parser.ClassifiedEventsAvailable -= OnClassified;
            _host.Gameplay.CommittedEventsAvailable -= OnCommitted;

            if (drainResult.Outcome == ReplayDrainOutcome.TimedOut)
            {
                await oracle.RecordExternalFailureAsync(
                        "drain.timeout",
                        BuildTimeoutMessage(drainResult.Diagnostics))
                    .ConfigureAwait(false);
            }
            else if (drainResult.Outcome == ReplayDrainOutcome.Faulted)
            {
                await oracle.RecordExternalFailureAsync(
                        "drain.fault",
                        BuildFaultMessage(drainResult.Diagnostics))
                    .ConfigureAwait(false);
            }

            if (drainResult.Outcome == ReplayDrainOutcome.Cancelled)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            timeline.Record(
                ReplayTimelineCategory.Pipeline,
                "parser.completed",
                ReplayTimelineRetentionClass.Milestone);
            timeline.Record(
                ReplayTimelineCategory.Pipeline,
                "gameplay.completed",
                ReplayTimelineRetentionClass.Milestone);

            var beginsMidSession = seededBindings.Any(entry => entry.ContextLedger.BeginsMidSession);
            var correctness = await oracle.CompleteAsync(
                beginsMidSession,
                expectedWelcome,
                request.Contexts.Count).ConfigureAwait(false);
            performanceCollector.RecordOracleFinalized();
            timeline.Record(
                ReplayTimelineCategory.Pipeline,
                "correctness.completed",
                ReplayTimelineRetentionClass.Milestone,
                value: correctness.Passed ? 1 : 0,
                unit: "passed");

            var effectiveLedger = aggregatedLedger?.PrimaryLedger ?? ledger;
            var latencyReport = latencyTracker?.BuildReport();
            var concurrentWriteThroughput = aggregatedLedger?.ConcurrentWriteThroughput;
            var contextSummaries = BuildContextSummaries(
                seededBindings,
                profileResolution,
                oracle,
                gameplayDiagnostics,
                concurrentWriteThroughput);
            var profileReport = BuildProfileReport(
                profileResolution,
                correctness,
                drainResult,
                gameplayDiagnostics,
                parserDiagnostics,
                parserSnapshot,
                concurrentWriteThroughput,
                effectiveLedger,
                latencyReport,
                harnessFailed: false);

            if (profileResolution is not null)
            {
                timeline.Record(
                    ReplayTimelineCategory.Analytics,
                    profileReport?.Outcome == ReplayProfileOutcome.ExpectedOverloadObserved
                        ? "overload.observed"
                        : profileReport?.Outcome == ReplayProfileOutcome.ExpectedOverloadNotObserved
                            ? "overload.missing"
                            : "profile.completed",
                    ReplayTimelineRetentionClass.Milestone,
                    unit: profileReport?.Outcome.ToString());
            }

            var performance = performanceCollector.Complete(
                correctness,
                drainResult,
                effectiveLedger,
                parserDiagnostics,
                gameplayDiagnostics,
                parserSnapshot,
                timeline,
                profileReport,
                contextSummaries,
                latencyReport);

            return new ReplayPipelineResult
            {
                Correctness = correctness,
                Parser = new ReplayParserObservationReport
                {
                    RawEventsObserved = oracle.RawEventsObserved,
                    ClassifiedEventsObserved = oracle.ClassifiedEventsObserved,
                    TotalLinesProcessed = parserSnapshot.TotalLinesProcessed,
                    ExpectedCompleteLines = expectedLines,
                    WorkerCount = parserSnapshot.WorkerCount,
                    ParserDrained = parserDiagnostics.QueuedEventCount == 0
                        && parserSnapshot.TotalLinesProcessed >= expectedLines
                },
                Gameplay = new ReplayGameplayObservationReport
                {
                    CommittedEventsObserved = oracle.CommittedGameplayEventsObserved,
                    ActiveSessionCount = gameplaySnapshot.Sessions.Count,
                    GameplayDrained = gameplayDiagnostics.PendingCommittedEventCount == 0,
                    FailedContextCount = gameplayDiagnostics.FailedContextCount,
                    TotalCommittedEvents = gameplayDiagnostics.TotalCommittedEvents,
                    MonitoringContextCount = monitoringSnapshot.ContextCount
                },
                Performance = performance,
                Profile = profileReport
            };
        }
        finally
        {
            if (_host is not null)
            {
                _host.LogActivity.ActivityChanged -= OnLogActivityChanged;
                _host.Monitoring.StateChanged -= OnMonitoringChanged;
                _host.Parser.StateChanged -= OnParserChanged;
                _host.Gameplay.StateChanged -= OnGameplayChanged;
                _host.Parser.EventsAvailable -= OnRaw;
                _host.Parser.ClassifiedEventsAvailable -= OnClassified;
                _host.Gameplay.CommittedEventsAvailable -= OnCommitted;

                if (!hostStopped)
                {
                    await _host.StopAsync().ConfigureAwait(false);
                }
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_host is not null)
        {
            await _host.DisposeAsync().ConfigureAwait(false);
            _host = null;
        }

        try
        {
            if (Directory.Exists(_characterDataDirectory))
            {
                Directory.Delete(_characterDataDirectory, recursive: true);
            }
        }
        catch
        {
        }
    }

    private ReplayServiceHost CreateServiceHost(
        IReadOnlyList<ReplayWorkspace> workspaces,
        GameplaySessionOptions? gameplaySessionOptions,
        ParserManagerOptions parserOptions)
    {
        var runtime = new ReplayStandInRuntimeService();
        var accounts = workspaces.Select(workspace => workspace.ToHomecomingAccount()).ToArray();
        var logActivity = new LogActivityService(
            () => accounts,
            new LogActivityServiceOptions
            {
                TimeProvider = _lifecycleTimeProvider,
                EnablePolling = true,
                PollInterval = TimeSpan.FromMilliseconds(25),
                InactivityThreshold = TimeSpan.FromMilliseconds(1)
            });
        var monitoring = new MonitoringSessionManager(
            runtime,
            logActivity,
            new MonitoringSessionManagerOptions { TimeProvider = _timeProvider });
        var parser = new ParserManager(
            monitoring,
            parserOptions);
        var characterRepository = new CharacterRepository(
            new CharacterRepositoryOptions { DataDirectory = _characterDataDirectory });
        var gameplay = new GameplaySessionManager(
            monitoring,
            parser,
            characterRepository,
            gameplaySessionOptions ?? new GameplaySessionOptions { TimeProvider = _timeProvider });

        return new ReplayServiceHost(
            runtime,
            logActivity,
            monitoring,
            parser,
            characterRepository,
            gameplay);
    }

    private GameplaySessionOptions? ResolveGameplayOptions(ReplayPipelineRequest request)
    {
        if (request.GameplaySessionOptions is not null)
        {
            return request.GameplaySessionOptions;
        }

        var capacity = request.ProfileResolution?.Profile.GameplayWorkQueueCapacity;
        var preStartCapacity = request.ProfileResolution?.Profile.GameplayMaxPreStartBufferCapacity;
        if (capacity is null && preStartCapacity is null)
        {
            return null;
        }

        return new GameplaySessionOptions
        {
            TimeProvider = _timeProvider,
            WorkQueueCapacity = capacity ?? 512,
            MaxPreStartBufferCapacity = preStartCapacity ?? 256
        };
    }

    private Func<int, Task>? CreateBetweenSegmentsHandler(
        ReplayContextBinding binding,
        ReplayCorrectnessOracle oracle,
        ReplayLifecycleSignal lifecycleSignal,
        ReplayTimeline timeline,
        CancellationToken cancellationToken)
    {
        if (binding.Plan.Segments.Count <= 1)
        {
            return null;
        }

        var cumulativeSegmentLines = 0L;
        return async completedOrdinal =>
        {
            timeline.Record(
                ReplayTimelineCategory.Analytics,
                "rollover.segment.completed",
                ReplayTimelineRetentionClass.Milestone,
                completedOrdinal);
            var completedSegment = binding.Plan.Segments
                .First(segment => segment.Ordinal == completedOrdinal);
            cumulativeSegmentLines += ReplayLineAccounting
                .Analyze(File.ReadAllBytes(completedSegment.SourcePath))
                .CompleteLines;
            await WaitForParserProgressAsync(
                    cumulativeSegmentLines,
                    lifecycleSignal,
                    cancellationToken)
                .ConfigureAwait(false);
            await RefreshLifecycleStateAsync(oracle, cancellationToken).ConfigureAwait(false);
            await AwaitCompletedSegmentInactiveAsync(
                    binding.Workspace.ResolveDestinationLogPath(completedSegment.DestinationDate),
                    lifecycleSignal,
                    cancellationToken)
                .ConfigureAwait(false);
            if (completedOrdinal < binding.Plan.Segments.Count)
            {
                timeline.Record(
                    ReplayTimelineCategory.Analytics,
                    "rollover.segment.started",
                    ReplayTimelineRetentionClass.Milestone,
                    completedOrdinal + 1);
            }
        };
    }

    private static IReadOnlyList<ReplayContextSummaryReport>? BuildContextSummaries(
        IReadOnlyList<(ReplayContextBinding Binding, ReplayLedger ContextLedger)> seededBindings,
        ReplayProfileResolution? profileResolution,
        ReplayCorrectnessOracle oracle,
        GameplaySessionDiagnostics gameplayDiagnostics,
        ReplayConcurrentWriteThroughput? concurrentWriteThroughput)
    {
        if (seededBindings.Count <= 1 && profileResolution is null)
        {
            return null;
        }

        if (concurrentWriteThroughput is not null)
        {
            return concurrentWriteThroughput.Contexts
                .Select(
                    context => new ReplayContextSummaryReport
                    {
                        Label = context.Label,
                        SourceLines = context.SourceCompleteLines,
                        SourceBytes = context.SourceBytes,
                        AchievedRateLinesPerSecond = context.AchievedLinesPerSecond,
                        ParserObservations = oracle.RawEventsObserved / Math.Max(seededBindings.Count, 1),
                        GameplayObservations = oracle.CommittedGameplayEventsObserved / Math.Max(seededBindings.Count, 1),
                        QueuePeakDepth = gameplayDiagnostics.WorkQueue.PeakDepth,
                        CorrectnessPassed = oracle.Failures.Count == 0
                    })
                .ToArray();
        }

        return seededBindings
            .Select(
                (entry, index) => new ReplayContextSummaryReport
                {
                    Label = profileResolution?.ContextPlans.ElementAtOrDefault(index)?.Label
                        ?? $"context-{index + 1}",
                    SourceLines = entry.ContextLedger.OriginalSourceCompleteLines,
                    SourceBytes = entry.ContextLedger.OriginalSourceBytes,
                    AchievedRateLinesPerSecond = entry.ContextLedger.Pacing.AchievedLinesPerSecond,
                    ParserObservations = oracle.RawEventsObserved / Math.Max(seededBindings.Count, 1),
                    GameplayObservations = oracle.CommittedGameplayEventsObserved / Math.Max(seededBindings.Count, 1),
                    QueuePeakDepth = gameplayDiagnostics.WorkQueue.PeakDepth,
                    CorrectnessPassed = oracle.Failures.Count == 0
                })
            .ToArray();
    }

    private static ReplayProfileObservationReport? BuildProfileReport(
        ReplayProfileResolution? profileResolution,
        ReplayCorrectnessReport correctness,
        ReplayDrainResult drainResult,
        GameplaySessionDiagnostics gameplayDiagnostics,
        ParserManagerDiagnostics parserDiagnostics,
        ParserManagerSnapshot parserSnapshot,
        ReplayConcurrentWriteThroughput? concurrentWriteThroughput,
        ReplayLedger ledger,
        ReplayLatencyAggregateReport? latencyReport,
        bool harnessFailed)
    {
        if (profileResolution is null)
        {
            return null;
        }

        var outcome = ReplayProfileCatalog.DetermineOutcome(
            profileResolution.Profile,
            correctness,
            drainResult,
            gameplayDiagnostics,
            parserDiagnostics,
            parserSnapshot,
            harnessFailed);

        var achievedRate = concurrentWriteThroughput?.AggregateLinesPerSecond
            ?? ledger.Pacing.AchievedLinesPerSecond;

        return new ReplayProfileObservationReport
        {
            ProfileName = profileResolution.Profile.Name,
            EffectiveOptions = profileResolution.EffectiveOptions,
            ContextCount = profileResolution.Profile.ContextCount,
            RequestedRateLinesPerSecond = profileResolution.RequestedRateLinesPerSecond
                ?? ledger.Pacing.RequestedLinesPerSecond,
            AchievedRateLinesPerSecond = achievedRate,
            BurstConfiguration = profileResolution.Profile.BurstSettings,
            StressSeed = profileResolution.Profile.SourceWorkload.Seed,
            GeneratedLineCount = profileResolution.Profile.SourceWorkload.LineCount,
            ExpectedOverloadTarget = profileResolution.ExpectedOverloadTarget,
            Outcome = outcome,
            PacingUnderrunCount = ledger.Pacing.UnderrunCount,
            PacingOverrunMilliseconds = ledger.Pacing.OverrunMilliseconds,
            Latency = latencyReport,
            OverloadDiagnosticsSummary = outcome == ReplayProfileOutcome.ExpectedOverloadNotObserved
                ? ReplayProfileCatalog.BuildOverloadDiagnosticsSummary(
                    profileResolution.ExpectedOverloadTarget,
                    gameplayDiagnostics,
                    parserDiagnostics)
                : null
        };
    }

    private ReplayServiceHost CreateServiceHost(IReadOnlyList<ReplayWorkspace> workspaces)
        => CreateServiceHost(workspaces, null, CreateParserOptions());

    private static ParserManagerOptions ResolveParserOptions(ReplayPipelineRequest request)
    {
        if (request.ProfileResolution?.Profile.ExpectedOverloadBehavior
            == ReplayExpectedOverloadBehavior.GameplayWorkQueue)
        {
            return new ParserManagerOptions
            {
                PollInterval = TimeSpan.FromMilliseconds(1),
                ReadBufferSize = 256,
                MaximumLineBytes = 4096,
                EventQueueCapacity = 512,
                EventBatchSize = 1,
                MonitoringSnapshotQueueCapacity = 64,
                MaximumRecentSegments = 8
            };
        }

        return CreateParserOptions();
    }

    private async Task RefreshLifecycleStateAsync(
        ReplayCorrectnessOracle oracle,
        CancellationToken cancellationToken)
    {
        var host = GetRequiredHost();
        await host.LogActivity.ScanAsync(cancellationToken).ConfigureAwait(false);
        AcceptPendingOffers(host.Monitoring);
        BindMonitoringContexts(host.Monitoring, oracle);
    }

    private Task WaitForSeededContextReadyAsync(
        SeededMonitoringContext seededContext,
        ReplayLifecycleSignal lifecycleSignal,
        CancellationToken cancellationToken)
    {
        var host = GetRequiredHost();
        return _lifecycleWaiter.WaitAsync(
            "seeded monitoring context readiness",
            () => ReplayLifecycleConditions.IsSeededContextReady(
                host.Monitoring.Current,
                host.Parser.Current,
                seededContext.ContextId,
                seededContext.AccountStableId,
                seededContext.SourceId),
            host.LogActivity.ScanAsync,
            () => DescribeSeededContext(
                host.Monitoring.Current,
                host.Parser.Current,
                seededContext),
            lifecycleSignal,
            _lifecycleTimeouts.SeededContextReady,
            cancellationToken);
    }

    private Task WaitForParserProgressAsync(
        long minimumLines,
        ReplayLifecycleSignal lifecycleSignal,
        CancellationToken cancellationToken)
    {
        var host = GetRequiredHost();
        return _lifecycleWaiter.WaitAsync(
            $"parser progress to {minimumLines} complete lines",
            () => host.Parser.Current.TotalLinesProcessed >= minimumLines,
            host.LogActivity.ScanAsync,
            () => $"processed={host.Parser.Current.TotalLinesProcessed}, expected-at-least={minimumLines}, "
                + $"queued={host.Parser.GetDiagnostics().QueuedEventCount}, "
                + $"workers={host.Parser.Current.WorkerCount}",
            lifecycleSignal,
            _lifecycleTimeouts.ParserProgress,
            cancellationToken);
    }

    private Task AwaitCompletedSegmentInactiveAsync(
        string completedDestinationPath,
        ReplayLifecycleSignal lifecycleSignal,
        CancellationToken cancellationToken)
    {
        var host = GetRequiredHost();
        return _lifecycleWaiter.WaitAsync(
            "completed segment source inactivity",
            () => IsCompletedSegmentInactive(host, completedDestinationPath),
            async ct =>
            {
                await host.LogActivity.ScanAsync(ct).ConfigureAwait(false);
                await host.LogActivity.ScanAsync(ct).ConfigureAwait(false);
            },
            () => DescribeCompletedSegmentState(host, completedDestinationPath),
            lifecycleSignal,
            _lifecycleTimeouts.InactiveSource,
            cancellationToken);
    }

    private static bool IsCompletedSegmentInactive(ReplayServiceHost host, string completedDestinationPath)
    {
        var candidate = host.LogActivity.Current.Candidates.FirstOrDefault(
            segment => string.Equals(
                segment.SourceId.FilePath,
                completedDestinationPath,
                StringComparison.OrdinalIgnoreCase));
        return candidate?.ActivityState == LogSourceActivityState.Inactive;
    }

    private static string DescribeCompletedSegmentState(ReplayServiceHost host, string completedDestinationPath)
    {
        var candidate = host.LogActivity.Current.Candidates.FirstOrDefault(
            segment => string.Equals(
                segment.SourceId.FilePath,
                completedDestinationPath,
                StringComparison.OrdinalIgnoreCase));
        return candidate is null
            ? $"path={completedDestinationPath}, candidate=missing, candidates={host.LogActivity.Current.Candidates.Count}"
            : $"path={completedDestinationPath}, state={candidate.ActivityState}, length={candidate.Length}, "
                + $"last-growth={candidate.LastGrowthAt:O}";
    }

    private async Task<ReplayDrainResult> WaitForDrainAsync(
        ReplayCorrectnessOracle oracle,
        long expectedCompleteLines,
        int expectedWelcomeBoundaries,
        ReplayTimeline timeline,
        ReplayLifecycleSignal lifecycleSignal,
        ReplayPerformanceCollector performanceCollector,
        CancellationToken cancellationToken)
    {
        var host = GetRequiredHost();
        var deadline = _lifecycleTimeProvider.GetUtcNow() + _lifecycleTimeouts.Drain;
        var firstRawMilestoneRecorded = false;
        var firstCommitMilestoneRecorded = false;

        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var progress = oracle.Progress;
                if (!firstRawMilestoneRecorded && progress.RawEventsObserved > 0)
                {
                    firstRawMilestoneRecorded = true;
                    timeline.Record(
                        ReplayTimelineCategory.Pipeline,
                        "parser.first-event",
                        ReplayTimelineRetentionClass.Milestone);
                }

                if (!firstCommitMilestoneRecorded && progress.CommittedGameplayEventsObserved > 0)
                {
                    firstCommitMilestoneRecorded = true;
                    timeline.Record(
                        ReplayTimelineCategory.Pipeline,
                        "gameplay.first-commit",
                        ReplayTimelineRetentionClass.Milestone);
                }

                var diagnostics = BuildDrainDiagnostics(host, oracle, expectedCompleteLines);
                performanceCollector.ObserveDuringDrain(
                    () => (host.Parser.GetDiagnostics(), host.Gameplay.GetDiagnostics()),
                    timeline,
                    cancellationToken);
                var faultOutcome = DetectDrainFault(host, oracle, diagnostics);
                if (faultOutcome is not null)
                {
                    return new ReplayDrainResult(faultOutcome.Value, diagnostics);
                }

                if (IsDrainComplete(host, oracle, expectedCompleteLines, diagnostics))
                {
                    return new ReplayDrainResult(ReplayDrainOutcome.Completed, diagnostics);
                }

                if (_lifecycleTimeProvider.GetUtcNow() >= deadline)
                {
                    return new ReplayDrainResult(ReplayDrainOutcome.TimedOut, diagnostics);
                }

                var change = lifecycleSignal.WaitForChangeAsync();
                await RefreshLifecycleStateAsync(oracle, cancellationToken).ConfigureAwait(false);

                var refreshedDiagnostics = BuildDrainDiagnostics(host, oracle, expectedCompleteLines);
                performanceCollector.ObserveDuringDrain(
                    () => (host.Parser.GetDiagnostics(), host.Gameplay.GetDiagnostics()),
                    timeline,
                    cancellationToken);
                var refreshedFault = DetectDrainFault(host, oracle, refreshedDiagnostics);
                if (refreshedFault is not null)
                {
                    return new ReplayDrainResult(refreshedFault.Value, refreshedDiagnostics);
                }

                if (IsDrainComplete(host, oracle, expectedCompleteLines, refreshedDiagnostics))
                {
                    return new ReplayDrainResult(ReplayDrainOutcome.Completed, refreshedDiagnostics);
                }

                if (_lifecycleTimeProvider.GetUtcNow() >= deadline)
                {
                    return new ReplayDrainResult(ReplayDrainOutcome.TimedOut, refreshedDiagnostics);
                }

                var remaining = deadline - _lifecycleTimeProvider.GetUtcNow();
                if (remaining <= TimeSpan.Zero)
                {
                    return new ReplayDrainResult(ReplayDrainOutcome.TimedOut, refreshedDiagnostics);
                }

                try
                {
                    await change.WaitAsync(remaining, _lifecycleTimeProvider, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    var timeoutDiagnostics = BuildDrainDiagnostics(host, oracle, expectedCompleteLines);
                    if (IsDrainComplete(host, oracle, expectedCompleteLines, timeoutDiagnostics))
                    {
                        return new ReplayDrainResult(ReplayDrainOutcome.Completed, timeoutDiagnostics);
                    }

                    return new ReplayDrainResult(ReplayDrainOutcome.TimedOut, timeoutDiagnostics);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new ReplayDrainResult(
                ReplayDrainOutcome.Cancelled,
                BuildDrainDiagnostics(host, oracle, expectedCompleteLines));
        }
    }

    private static ReplayDrainOutcome? DetectDrainFault(
        ReplayServiceHost host,
        ReplayCorrectnessOracle oracle,
        ReplayDrainDiagnostics diagnostics)
    {
        if (diagnostics.ParserOverflowed
            || diagnostics.GameplayOverloaded
            || diagnostics.FailedContextCount > 0
            || oracle.HasConsumerFault)
        {
            return ReplayDrainOutcome.Faulted;
        }

        if (host.Parser.Current.Workers.Any(
                worker => worker.State == ParserWorkerState.Faulted && worker.FaultCode is not null))
        {
            return ReplayDrainOutcome.Faulted;
        }

        return null;
    }

    private static bool IsDrainComplete(
        ReplayServiceHost host,
        ReplayCorrectnessOracle oracle,
        long expectedCompleteLines,
        ReplayDrainDiagnostics diagnostics)
    {
        var progress = oracle.Progress;
        var parserComplete = diagnostics.ProcessedLines >= diagnostics.ExpectedLines
            && diagnostics.ParserQueuedEvents == 0
            && diagnostics.ParserWorkersReady;
        var classifiedCaughtUp = expectedCompleteLines == 0
            || progress.ClassifiedEventsObserved >= expectedCompleteLines;
        var gameplayQuiescent = !parserComplete
            || (diagnostics.GameplayQuiescent && classifiedCaughtUp);
        var oracleObserved = progress.CommittedGameplayEventsObserved >= diagnostics.CommittedObserved;

        return parserComplete && classifiedCaughtUp && gameplayQuiescent && oracleObserved;
    }

    private static ReplayDrainDiagnostics BuildDrainDiagnostics(
        ReplayServiceHost host,
        ReplayCorrectnessOracle oracle,
        long expectedCompleteLines)
    {
        var parserDiagnostics = host.Parser.GetDiagnostics();
        var gameplayDiagnostics = host.Gameplay.GetDiagnostics();
        var progress = oracle.Progress;
        var workersReady = host.Parser.Current.WorkerCount == 0
            || host.Parser.Current.Workers.All(
                worker => worker.State is ParserWorkerState.WaitingForData or ParserWorkerState.Faulted);
        var parserComplete = host.Parser.Current.TotalLinesProcessed >= expectedCompleteLines
            && parserDiagnostics.QueuedEventCount == 0
            && workersReady;
        var classifiedCaughtUp = expectedCompleteLines == 0
            || progress.ClassifiedEventsObserved >= expectedCompleteLines;
        var gameplayIsQuiescent = gameplayDiagnostics.IsQuiescent;
        var gameplayQuiescent = parserComplete
            && gameplayIsQuiescent
            && classifiedCaughtUp;
        var oracleCaughtUp = progress.CommittedGameplayEventsObserved >= gameplayDiagnostics.TotalCommittedEvents;
        var overallDrainComplete = parserComplete && classifiedCaughtUp && gameplayQuiescent && oracleCaughtUp;

        var unsatisfied = new List<string>();
        if (!parserComplete)
        {
            unsatisfied.Add(
                $"parser(processed={host.Parser.Current.TotalLinesProcessed}/expected={expectedCompleteLines},queued={parserDiagnostics.QueuedEventCount})");
        }

        if (!classifiedCaughtUp)
        {
            unsatisfied.Add(
                $"classified(observed={progress.ClassifiedEventsObserved}/expected={expectedCompleteLines})");
        }

        if (parserComplete && !gameplayIsQuiescent)
        {
            unsatisfied.Add(
                $"gameplay(accepted={gameplayDiagnostics.LastAcceptedWorkSequence},completed={gameplayDiagnostics.LastCompletedWorkSequence},active={gameplayDiagnostics.ActiveProcessorCallbackCount},pending={gameplayDiagnostics.PendingCommittedEventCount})");
        }

        if (!oracleCaughtUp)
        {
            unsatisfied.Add(
                $"oracle(committed={progress.CommittedGameplayEventsObserved}/expected={gameplayDiagnostics.TotalCommittedEvents})");
        }

        var timeoutDetail = new ReplayDrainTimeoutDiagnostics(
            ParserEventQueue: parserDiagnostics.EventQueue,
            ParserMonitoringQueue: parserDiagnostics.MonitoringSnapshotQueue,
            ParserEventQueueInvariant: parserDiagnostics.EventQueue.ClassifyInvariant(),
            ParserComplete: parserComplete,
            GameplayIsQuiescent: gameplayIsQuiescent,
            ClassifiedCaughtUp: classifiedCaughtUp,
            OracleCaughtUp: oracleCaughtUp,
            OverallDrainComplete: overallDrainComplete,
            GameplayLastAcceptedWorkSequence: gameplayDiagnostics.LastAcceptedWorkSequence,
            GameplayLastCompletedWorkSequence: gameplayDiagnostics.LastCompletedWorkSequence,
            GameplayActiveProcessorCallbackCount: gameplayDiagnostics.ActiveProcessorCallbackCount,
            GameplayPendingCommittedEventCount: gameplayDiagnostics.PendingCommittedEventCount,
            GameplayWorkQueue: gameplayDiagnostics.WorkQueue,
            OracleRawObserved: progress.RawEventsObserved,
            OracleClassifiedObserved: progress.ClassifiedEventsObserved,
            OracleCommittedObserved: progress.CommittedGameplayEventsObserved,
            OracleLateObservationCount: oracle.LateObservationCount,
            OracleConsumerFault: oracle.HasConsumerFault);

        return new ReplayDrainDiagnostics(
            MonitoringReady: host.Monitoring.Current.ContextCount > 0,
            ProcessedLines: host.Parser.Current.TotalLinesProcessed,
            ExpectedLines: expectedCompleteLines,
            ParserQueuedEvents: parserDiagnostics.QueuedEventCount,
            ParserWorkersReady: workersReady,
            RawObserved: progress.RawEventsObserved,
            ClassifiedObserved: progress.ClassifiedEventsObserved,
            CommittedObserved: gameplayDiagnostics.TotalCommittedEvents,
            GameplayQuiescent: gameplayQuiescent,
            PendingCommittedEvents: gameplayDiagnostics.PendingCommittedEventCount,
            FailedContextCount: gameplayDiagnostics.FailedContextCount,
            ParserOverflowed: parserDiagnostics.MonitoringSnapshotQueueOverflowed
                || parserDiagnostics.EventQueueOverflowed,
            GameplayOverloaded: gameplayDiagnostics.WorkQueueOverflowed,
            UnsatisfiedStages: unsatisfied,
            TimeoutDetail: timeoutDetail);
    }

    private static string BuildTimeoutMessage(ReplayDrainDiagnostics diagnostics)
    {
        var stages = diagnostics.UnsatisfiedStages.Count == 0
            ? "unknown"
            : string.Join(", ", diagnostics.UnsatisfiedStages.Take(3));
        var message =
            $"Drain deadline exceeded. Unsatisfied: {stages}. "
            + $"processed={diagnostics.ProcessedLines}/{diagnostics.ExpectedLines}. "
            + (diagnostics.TimeoutDetail?.ToCompactDiagnosticString()
                ?? $"gameplay-quiescent={diagnostics.GameplayQuiescent}");
        const int maxLength = 1200;
        return message.Length <= maxLength ? message : message[..maxLength];
    }

    private static string BuildFaultMessage(ReplayDrainDiagnostics diagnostics)
    {
        var stage = diagnostics.ParserOverflowed
            ? "parser-overflow"
            : diagnostics.GameplayOverloaded
                ? "gameplay-overload"
                : diagnostics.FailedContextCount > 0
                    ? "gameplay-context-failure"
                    : "pipeline-fault";
        var message =
            $"Drain fault at {stage}. failed-contexts={diagnostics.FailedContextCount}, "
            + $"parser-overflowed={diagnostics.ParserOverflowed}, "
            + $"gameplay-overloaded={diagnostics.GameplayOverloaded}";
        return message.Length <= 200 ? message : message[..200];
    }

    private static void AcceptPendingOffers(MonitoringSessionManager monitoring)
    {
        if (monitoring.Current.ContextCount > 0)
        {
            return;
        }

        foreach (var offer in monitoring.Current.PendingOffers)
        {
            if (offer.State == MonitoringSourceOfferState.Pending)
            {
                monitoring.AcceptOffer(offer.OfferId);
            }
        }
    }

    private static void BindMonitoringContexts(
        MonitoringSessionManager monitoring,
        ReplayCorrectnessOracle oracle)
    {
        foreach (var context in monitoring.Current.Contexts)
        {
            if (context.AccountStableId is not null)
            {
                oracle.BindAccount(context.AccountStableId, context.ContextId);
            }
        }
    }

    private static SeededMonitoringContext SeedMonitoringContext(
        MonitoringSessionManager monitoring,
        ReplayWorkspace workspace,
        ReplayPlan plan)
    {
        var firstSegment = plan.Segments[0];
        var destinationPath = workspace.ResolveDestinationLogPath(firstSegment.DestinationDate);
        var sourceId = LogSourceId.Create(
            workspace.AccountStableId,
            workspace.AccountFolderName,
            destinationPath,
            firstSegment.DestinationDate);
        var contextId = monitoring.AddContext(
            workspace.AccountStableId,
            workspace.AccountFolderName,
            sourceId);
        return new SeededMonitoringContext(contextId, workspace.AccountStableId, sourceId);
    }

    private static string DescribeSeededContext(
        MonitoringSessionManagerSnapshot snapshot,
        ParserManagerSnapshot parserSnapshot,
        SeededMonitoringContext seededContext)
    {
        var context = snapshot.Contexts.SingleOrDefault(
            candidate => candidate.ContextId == seededContext.ContextId);
        var worker = parserSnapshot.Workers.SingleOrDefault(
            candidate => candidate.ContextId == seededContext.ContextId);
        var contextState = context is null
            ? $"context={seededContext.ContextId} missing, total-contexts={snapshot.ContextCount}"
            : $"context={context.ContextId}, state={context.State}, "
                + $"account={context.AccountStableId ?? "<null>"}, "
                + $"expected-account={seededContext.AccountStableId}, "
                + $"source={context.CurrentSourceId?.ToString() ?? "<null>"}, "
                + $"expected-source={seededContext.SourceId}";
        var workerState = worker is null
            ? "parser-worker=missing"
            : $"parser-worker={worker.WorkerId}, state={worker.State}, "
                + $"source={worker.CurrentSourceId?.ToString() ?? "<null>"}, "
                + $"binding-generation={worker.AppliedSourceBindingGeneration}";
        return $"{contextState}; {workerState}";
    }

    private ReplayServiceHost GetRequiredHost() =>
        _host ?? throw new InvalidOperationException("Replay service host has not been created.");

    private static void EnsureEmptyDestinationLogs(ReplayContextBinding binding)
    {
        foreach (var segment in binding.Plan.Segments)
        {
            var destinationPath = binding.Workspace.ResolveDestinationLogPath(segment.DestinationDate);
            if (File.Exists(destinationPath))
            {
                continue;
            }

            Directory.CreateDirectory(binding.Workspace.LogsDirectory);
            File.WriteAllBytes(destinationPath, []);
        }
    }

    private static readonly ParserClassifier WelcomeBoundaryClassifier = new();

    internal static int AdditionalExpectedWelcomeBoundariesForTests { get; set; }

    internal static int CountExpectedWelcomeBoundaries(ReplayPlan plan)
    {
        var expected = plan.InputMode == ReplayInputMode.Bootstrap ? 1 : 0;
        foreach (var segment in plan.Segments)
        {
            expected += CountWelcomeBoundariesInBytes(File.ReadAllBytes(segment.SourcePath));
        }

        return expected + AdditionalExpectedWelcomeBoundariesForTests;
    }

    private static int CountWelcomeBoundariesInBytes(byte[] sourceBytes)
    {
        var expected = 0;
        var lineStart = 0;
        for (var index = 0; index < sourceBytes.Length; index++)
        {
            if (sourceBytes[index] != (byte)'\n')
            {
                continue;
            }

            if (IsClassifiedWelcomeBoundary(sourceBytes, lineStart, index - lineStart + 1))
            {
                expected++;
            }

            lineStart = index + 1;
        }

        return expected;
    }

    private static bool IsClassifiedWelcomeBoundary(byte[] sourceBytes, int lineStart, int lineLength)
    {
        var lineText = Encoding.UTF8.GetString(sourceBytes, lineStart, lineLength).TrimEnd('\r', '\n');
        if (lineText.Length == 0)
        {
            return false;
        }

        var classified = WelcomeBoundaryClassifier.Classify(new ParserRawEvent
        {
            ContextId = MonitoringContextId.CreateNew(),
            SourceId = LogSourceId.Create(
                "replay-welcome-count",
                "replay-welcome-count",
                "replay-welcome-count",
                new DateOnly(2026, 1, 1)),
            SourceSegmentId = ParserSourceSegmentId.CreateNew(),
            BindingGeneration = 1,
            Sequence = 1,
            ObservedAt = DateTimeOffset.UnixEpoch,
            RawLine = lineText,
            SourceByteStart = lineStart,
            SourceByteEnd = lineStart + lineLength,
            LineStatus = ParserLineStatus.Complete
        });

        return ReplayCorrectnessOracle.IsWelcomeBoundary(classified);
    }

    private static ParserManagerOptions CreateParserOptions() =>
        new()
        {
            PollInterval = TimeSpan.FromMilliseconds(10),
            ReadBufferSize = 256,
            MaximumLineBytes = 4096,
            EventQueueCapacity = 256,
            EventBatchSize = 32,
            MonitoringSnapshotQueueCapacity = 64,
            MaximumRecentSegments = 8
        };

    private static ReplayPerformanceOptions CreateEffectivePerformanceOptions(
        TimeProvider timeProvider,
        ReplayPerformanceOptions? performanceOptions)
    {
        if (performanceOptions is null)
        {
            return new ReplayPerformanceOptions { TimeProvider = timeProvider };
        }

        return new ReplayPerformanceOptions
        {
            TimeProvider = timeProvider,
            SampleInterval = performanceOptions.SampleInterval,
            MaxRetainedSamples = performanceOptions.MaxRetainedSamples,
            QueueThresholdPercents = performanceOptions.QueueThresholdPercents,
            EnableSampling = performanceOptions.EnableSampling,
            EnableResourceSampling = performanceOptions.EnableResourceSampling
        };
    }

    private sealed record SeededMonitoringContext(
        MonitoringContextId ContextId,
        string AccountStableId,
        LogSourceId SourceId);

    private sealed class ReplayServiceHost : IAsyncDisposable
    {
        public ReplayServiceHost(
            ReplayStandInRuntimeService runtime,
            LogActivityService logActivity,
            MonitoringSessionManager monitoring,
            ParserManager parser,
            CharacterRepository characterRepository,
            GameplaySessionManager gameplay)
        {
            Runtime = runtime;
            LogActivity = logActivity;
            Monitoring = monitoring;
            Parser = parser;
            CharacterRepository = characterRepository;
            Gameplay = gameplay;
        }

        public ReplayStandInRuntimeService Runtime { get; }

        public LogActivityService LogActivity { get; }

        public MonitoringSessionManager Monitoring { get; }

        public ParserManager Parser { get; }

        public CharacterRepository CharacterRepository { get; }

        public GameplaySessionManager Gameplay { get; }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await LogActivity.StartAsync(cancellationToken).ConfigureAwait(false);
            await Monitoring.StartAsync(cancellationToken).ConfigureAwait(false);
            await Parser.StartAsync(cancellationToken).ConfigureAwait(false);
            await Gameplay.StartAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task StopAsync()
        {
            List<Exception>? failures = null;
            foreach (var stopAsync in new Func<Task>[]
            {
                () => Gameplay.StopAsync(CancellationToken.None),
                () => Parser.StopAsync(CancellationToken.None),
                () => Monitoring.StopAsync(CancellationToken.None),
                () => LogActivity.StopAsync(CancellationToken.None)
            })
            {
                try
                {
                    await stopAsync().ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    failures ??= [];
                    failures.Add(exception);
                }
            }

            if (failures is not null)
            {
                throw new AggregateException("One or more replay services failed to stop.", failures);
            }
        }

        public async ValueTask DisposeAsync()
        {
            await StopAsync().ConfigureAwait(false);
            Parser.Dispose();
            Gameplay.Dispose();
            Monitoring.Dispose();
            LogActivity.Dispose();
            Runtime.Dispose();
        }
    }
}

internal sealed class ReplayStandInRuntimeService : IGameRuntimeService
{
    public GameRuntimeStatus CurrentStatus { get; private set; } = GameRuntimeStatus.Running;

    public int RunningClientCount { get; private set; } = 1;

    public IReadOnlyList<HomecomingProcessInstance> RunningClients { get; private set; } =
        Array.Empty<HomecomingProcessInstance>();

    public string? LastErrorMessage { get; private set; }

#pragma warning disable CS0067
    public event EventHandler<GameRuntimeStatusChangedEventArgs>? StatusChanged;
#pragma warning restore CS0067

    public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task LaunchAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public void Start()
    {
    }

    public void Stop()
    {
    }

    public void Dispose()
    {
    }
}
