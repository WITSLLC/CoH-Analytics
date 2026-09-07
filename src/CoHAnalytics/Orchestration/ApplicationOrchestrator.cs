using CoHAnalytics.Orchestration.Contracts;
using CoHAnalytics.Orchestration.Diagnostics;
using CoHAnalytics.Orchestration.Internal;
using CoHAnalytics.Orchestration.Models;

namespace CoHAnalytics.Orchestration;

public sealed class ApplicationOrchestrator : IApplicationOrchestrator, IAsyncDisposable
{
    private readonly ApplicationOrchestratorOptions _options;
    private readonly IApplicationActionRouter _actionRouter;
    private readonly TimeProvider _timeProvider;
    private readonly object _stateSync = new();
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly CapabilityGraph _graph = new();
    private readonly Dictionary<string, ContributorEntry> _entries = new(StringComparer.Ordinal);
    private readonly EventHistoryBuffer _eventHistory;
    private readonly List<string> _validationMessages = [];
    private readonly List<string> _schemaMismatches = [];
    private readonly Dictionary<string, int> _failureCounts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _timeoutCounts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TimeSpan> _lastPullDurations = new(StringComparer.Ordinal);

    private CancellationTokenSource _lifetimeCts = new();
    private CancellationTokenSource? _debounceCts;
    private ApplicationStateSnapshot _current;
    private bool _started;
    private bool _disposed;
    private bool _refreshDirty;
    private bool _fullRefreshRequested;
    private long _noOpSuppressionCount;
    private string? _latestSnapshotRule;
    private string? _primaryIssueRule;
    private string? _overallStateRule;
    private Exception? _lastLifecycleException;
    private long _revision;

    public ApplicationOrchestrator(
        ApplicationOrchestratorOptions? options = null,
        IApplicationActionRouter? actionRouter = null)
    {
        _options = options ?? new ApplicationOrchestratorOptions();
        _actionRouter = actionRouter ?? NullApplicationActionRouter.Instance;
        _timeProvider = _options.TimeProvider;
        _eventHistory = new EventHistoryBuffer(_options.EventHistoryCapacity);
        _current = ApplicationStateSnapshot.Empty(_timeProvider.GetUtcNow());
    }

    public ApplicationStateSnapshot Current => Volatile.Read(ref _current!);

    public event EventHandler<ApplicationStateChangedEventArgs>? SnapshotChanged;

    public void Register(IApplicationContributor contributor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(contributor);

        lock (_stateSync)
        {
            if (_started)
            {
                throw new InvalidOperationException("Contributors cannot be registered after startup.");
            }

            // Defensive copy: a caller-owned mutable list backing Produces/Requires/Optional
            // must not be able to change graph/evaluation behavior after registration.
            var descriptor = contributor.Descriptor with
            {
                Produces = contributor.Descriptor.Produces.ToArray(),
                Requires = contributor.Descriptor.Requires.ToArray(),
                Optional = contributor.Descriptor.Optional.ToArray()
            };
            var errors = DescriptorValidator.Validate(descriptor);
            if (errors.Count > 0)
            {
                throw new ArgumentException(
                    $"Invalid descriptor for '{descriptor.ProviderId}': {string.Join("; ", errors)}",
                    nameof(contributor));
            }

            if (_entries.ContainsKey(descriptor.ProviderId))
            {
                throw new InvalidOperationException($"Duplicate provider ID '{descriptor.ProviderId}'.");
            }

            foreach (var capability in descriptor.Produces)
            {
                if (_graph.TryGetProducer(capability, out var existingProducer))
                {
                    var message =
                        $"Capability '{capability}' is already produced by '{existingProducer}'; rejecting '{descriptor.ProviderId}' so the first registration wins.";
                    _validationMessages.Add(message);
                    AppendEvent(
                        ApplicationOrchestrationEventKind.DependencyUnresolved,
                        message,
                        descriptor.ProviderId,
                        relatedCapabilityId: capability,
                        detail: "orchestrator.duplicate_capability");

                    throw new InvalidOperationException(message);
                }
            }

            if (descriptor.SchemaVersion != ApplicationContributorDescriptor.ExpectedSchemaVersion)
            {
                var message =
                    $"Provider '{descriptor.ProviderId}' reported SchemaVersion {descriptor.SchemaVersion}; expected {ApplicationContributorDescriptor.ExpectedSchemaVersion}.";
                _schemaMismatches.Add(message);
                _validationMessages.Add(message);
            }

            if (contributor is not IApplicationActionHandler)
            {
                foreach (var action in GetDeclaredContributorActions(contributor))
                {
                    // Deferred until first pull; registration-time check for handler presence when target known later.
                }
            }

            var entry = new ContributorEntry(contributor, descriptor)
            {
                LifecycleState = ApplicationContributorLifecycleState.Registered
            };
            _entries[descriptor.ProviderId] = entry;
            _graph.Add(descriptor);

            AppendEvent(
                ApplicationOrchestrationEventKind.ContributorRegistered,
                $"Contributor registered: {descriptor.DisplayName}",
                descriptor.ProviderId);
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_stateSync)
        {
            if (_started)
            {
                return;
            }
        }

        ValidateGraphOrThrow();

        List<ContributorEntry> ordered;
        lock (_stateSync)
        {
            ordered = _graph.TopologicalOrder
                .Select(id => _entries[id])
                .ToList();
        }

        foreach (var entry in ordered)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await StartContributorAsync(entry, cancellationToken).ConfigureAwait(false);
        }

        lock (_stateSync)
        {
            foreach (var entry in _entries.Values)
            {
                entry.Contributor.ContributionChanged += OnContributionChanged;
            }

            _started = true;
        }

        await RefreshCoreAsync(full: true, reason: SnapshotGenerationReason.Startup, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return;
        }

        List<ContributorEntry> reverseOrdered;
        lock (_stateSync)
        {
            foreach (var entry in _entries.Values)
            {
                entry.Contributor.ContributionChanged -= OnContributionChanged;
            }

            CancelDebounce_NoLock();
            try
            {
                _lifetimeCts.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }

            reverseOrdered = _graph.ReverseTopologicalOrder
                .Where(id => _entries.ContainsKey(id))
                .Select(id => _entries[id])
                .ToList();
        }

        try
        {
            using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            waitCts.CancelAfter(_options.ShutdownWaitTimeout);
            await _refreshGate.WaitAsync(waitCts.Token).ConfigureAwait(false);
            _refreshGate.Release();
        }
        catch (OperationCanceledException)
        {
        }

        foreach (var entry in reverseOrdered)
        {
            await StopContributorAsync(entry, cancellationToken).ConfigureAwait(false);
        }

        lock (_stateSync)
        {
            _started = false;
        }
    }

    public async Task RefreshAsync(string? providerId = null, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (providerId is null)
        {
            await RefreshCoreAsync(full: true, SnapshotGenerationReason.ExplicitRefresh, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        lock (_stateSync)
        {
            if (_entries.TryGetValue(providerId, out var entry))
            {
                entry.IsDirty = true;
            }
        }

        await RefreshCoreAsync(full: false, SnapshotGenerationReason.ExplicitRefresh, cancellationToken)
            .ConfigureAwait(false);
    }

    public ApplicationOrchestratorDiagnostics GetDiagnostics()
    {
        lock (_stateSync)
        {
            return new ApplicationOrchestratorDiagnostics
            {
                RegisteredDescriptors = _entries.Values.Select(e => e.Descriptor).ToArray(),
                LifecycleStates = _entries.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value.LifecycleState,
                    StringComparer.Ordinal),
                TopologicalOrder = _graph.TopologicalOrder.ToArray(),
                CapabilityProducers = new Dictionary<string, string>(_graph.ProducerByCapability, StringComparer.Ordinal),
                ValidationMessages = _validationMessages.ToArray(),
                UnresolvedDependencies = CollectUnresolvedDependencies_NoLock(),
                ContributionTimestamps = _entries.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value.Contribution?.ObservedAt,
                    StringComparer.Ordinal),
                StaleProviders = _entries.Values
                    .Where(e => IsStale_NoLock(e))
                    .Select(e => e.Descriptor.ProviderId)
                    .OrderBy(id => id, StringComparer.Ordinal)
                    .ToArray(),
                LastPullDurations = new Dictionary<string, TimeSpan>(_lastPullDurations, StringComparer.Ordinal),
                ContributorFailureCounts = new Dictionary<string, int>(_failureCounts, StringComparer.Ordinal),
                ContributorTimeoutCounts = new Dictionary<string, int>(_timeoutCounts, StringComparer.Ordinal),
                SchemaVersionMismatches = _schemaMismatches.ToArray(),
                NoOpSuppressionCount = _noOpSuppressionCount,
                LatestSnapshotGenerationRule = _latestSnapshotRule,
                PrimaryIssueSelectionRule = _primaryIssueRule,
                OverallStateSelectionRule = _overallStateRule,
                EventHistory = _eventHistory.Snapshot(),
                LastLifecycleException = _lastLifecycleException
            };
        }
    }

    public IReadOnlyList<ApplicationOrchestrationEvent> GetEventHistory()
    {
        lock (_stateSync)
        {
            return _eventHistory.Snapshot();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            await StopAsync().ConfigureAwait(false);
        }
        catch
        {
        }

        _refreshGate.Dispose();
        _lifetimeCts.Dispose();
        _debounceCts?.Dispose();
    }

    private void ValidateGraphOrThrow()
    {
        lock (_stateSync)
        {
            if (_graph.RebuildTopologicalOrder(out var cycleError))
            {
                return;
            }

            var message = cycleError ?? "Required-dependency cycle detected.";
            _validationMessages.Add(message);

            AppendEvent(
                ApplicationOrchestrationEventKind.DependencyUnresolved,
                "Required-dependency cycle detected",
                detail: message);

            if (_options.CycleValidationMode == CycleValidationMode.Throw)
            {
                throw new InvalidOperationException(message);
            }

            foreach (var participant in _graph.CycleParticipants.ToArray())
            {
                if (_entries.Remove(participant, out var entry))
                {
                    _graph.Remove(participant);
                    _validationMessages.Add($"Rejected cyclic contributor '{participant}'.");
                    entry.LifecycleState = ApplicationContributorLifecycleState.Faulted;
                }
            }

            _graph.RebuildTopologicalOrder(out _);
        }
    }

    private async Task StartContributorAsync(ContributorEntry entry, CancellationToken cancellationToken)
    {
        if (entry.Contributor is IApplicationContributorLifecycle lifecycle)
        {
            SetLifecycle(entry, ApplicationContributorLifecycleState.Starting);
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetimeCts.Token);
                cts.CancelAfter(entry.Descriptor.PullTimeout ?? _options.DefaultPullTimeout);
                await lifecycle.StartAsync(cts.Token).ConfigureAwait(false);
                SetLifecycle(entry, ApplicationContributorLifecycleState.Running);
            }
            catch (Exception ex)
            {
                FaultContributor(entry, ex, startup: true);
            }
        }
        else
        {
            SetLifecycle(entry, ApplicationContributorLifecycleState.Running);
        }
    }

    private async Task StopContributorAsync(ContributorEntry entry, CancellationToken cancellationToken)
    {
        if (entry.LifecycleState is ApplicationContributorLifecycleState.Stopped)
        {
            return;
        }

        if (entry.Contributor is IApplicationContributorLifecycle lifecycle)
        {
            SetLifecycle(entry, ApplicationContributorLifecycleState.Stopping);
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(entry.Descriptor.PullTimeout ?? _options.DefaultPullTimeout);
                await lifecycle.StopAsync(cts.Token).ConfigureAwait(false);
                SetLifecycle(entry, ApplicationContributorLifecycleState.Stopped);
            }
            catch (Exception ex)
            {
                FaultContributor(entry, ex, startup: false);
            }
        }
        else
        {
            SetLifecycle(entry, ApplicationContributorLifecycleState.Stopped);
        }

        if (entry.Contributor is IDisposable disposable)
        {
            try
            {
                disposable.Dispose();
            }
            catch
            {
            }
        }
    }

    private void FaultContributor(ContributorEntry entry, Exception exception, bool startup)
    {
        lock (_stateSync)
        {
            _lastLifecycleException = exception;
            var from = entry.LifecycleState;
            entry.LifecycleState = ApplicationContributorLifecycleState.Faulted;
            entry.LifecycleFaulted = true;

            AppendEvent(
                ApplicationOrchestrationEventKind.ContributorLifecycleChanged,
                $"Lifecycle {from} → Faulted",
                entry.Descriptor.ProviderId,
                detail: $"{from} → Faulted");

            AppendEvent(
                ApplicationOrchestrationEventKind.ContributorFaulted,
                startup
                    ? $"Contributor startup failed: {entry.Descriptor.DisplayName}"
                    : $"Contributor shutdown failed: {entry.Descriptor.DisplayName}",
                entry.Descriptor.ProviderId,
                detail: exception.GetType().Name);
        }
    }

    private void SetLifecycle(ContributorEntry entry, ApplicationContributorLifecycleState next)
    {
        lock (_stateSync)
        {
            var from = entry.LifecycleState;
            if (from == next)
            {
                return;
            }

            entry.LifecycleState = next;
            AppendEvent(
                ApplicationOrchestrationEventKind.ContributorLifecycleChanged,
                $"Lifecycle {from} → {next}",
                entry.Descriptor.ProviderId,
                detail: $"{from} → {next}");
        }
    }

    private void OnContributionChanged(object? sender, EventArgs e)
    {
        if (sender is not IApplicationContributor contributor)
        {
            return;
        }

        lock (_stateSync)
        {
            if (!_entries.TryGetValue(contributor.Descriptor.ProviderId, out var entry))
            {
                return;
            }

            if (entry.LifecycleState is not ApplicationContributorLifecycleState.Running)
            {
                return;
            }

            entry.IsDirty = true;
            AppendEvent(
                ApplicationOrchestrationEventKind.ContributionInvalidated,
                "Contribution invalidated",
                entry.Descriptor.ProviderId);
            ScheduleDebouncedRefresh_NoLock();
        }
    }

    private void ScheduleDebouncedRefresh_NoLock()
    {
        CancelDebounce_NoLock();
        var cts = new CancellationTokenSource();
        _debounceCts = cts;
        _ = DebounceAndRefreshAsync(cts);
    }

    private async Task DebounceAndRefreshAsync(CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(_options.DebounceInterval, cts.Token).ConfigureAwait(false);
            await RefreshCoreAsync(full: false, SnapshotGenerationReason.ContributorInvalidation, _lifetimeCts.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void CancelDebounce_NoLock()
    {
        try
        {
            _debounceCts?.Cancel();
            _debounceCts?.Dispose();
        }
        catch
        {
        }

        _debounceCts = null;
    }

    private async Task RefreshCoreAsync(
        bool full,
        SnapshotGenerationReason reason,
        CancellationToken cancellationToken)
    {
        if (!await _refreshGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            lock (_stateSync)
            {
                _refreshDirty = true;
                if (full)
                {
                    _fullRefreshRequested = true;
                }
            }

            return;
        }

        try
        {
            do
            {
                bool runFull;
                lock (_stateSync)
                {
                    runFull = full || _fullRefreshRequested;
                    _refreshDirty = false;
                    _fullRefreshRequested = false;
                }

                await ExecuteRefreshPassAsync(runFull, reason, cancellationToken).ConfigureAwait(false);

                lock (_stateSync)
                {
                    if (!_refreshDirty)
                    {
                        break;
                    }

                    full = _fullRefreshRequested;
                }
            }
            while (true);
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private async Task ExecuteRefreshPassAsync(
        bool full,
        SnapshotGenerationReason reason,
        CancellationToken cancellationToken)
    {
        AppendEvent(ApplicationOrchestrationEventKind.RefreshStarted, full ? "Full refresh started" : "Targeted refresh started");

        List<ContributorEntry> toPull;
        lock (_stateSync)
        {
            toPull = _entries.Values
                .Where(e => e.LifecycleState == ApplicationContributorLifecycleState.Running)
                .Where(e => full || e.IsDirty || e.Contribution is null)
                .OrderBy(e => _graph.IndexOf(e.Descriptor.ProviderId))
                .ToList();
        }

        var pullTasks = toPull.Select(entry => PullContributorAsync(entry, cancellationToken));
        await Task.WhenAll(pullTasks).ConfigureAwait(false);

        PublishSnapshot(reason);

        AppendEvent(ApplicationOrchestrationEventKind.RefreshCompleted, full ? "Full refresh completed" : "Targeted refresh completed");
    }

    private async Task PullContributorAsync(ContributorEntry entry, CancellationToken cancellationToken)
    {
        if (entry.LifecycleState is not ApplicationContributorLifecycleState.Running)
        {
            return;
        }

        var timeout = entry.Descriptor.PullTimeout ?? _options.DefaultPullTimeout;
        var started = _timeProvider.GetTimestamp();

        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetimeCts.Token);
            linked.CancelAfter(timeout);

            var contribution = await entry.Contributor.GetContributionAsync(linked.Token).ConfigureAwait(false);
            var elapsed = _timeProvider.GetElapsedTime(started);

            lock (_stateSync)
            {
                _lastPullDurations[entry.Descriptor.ProviderId] = elapsed;
                entry.IsDirty = false;

                if (contribution is null
                    || !string.Equals(contribution.ProviderId, entry.Descriptor.ProviderId, StringComparison.Ordinal))
                {
                    ApplyFailedContribution_NoLock(entry, "Malformed contribution.");
                    return;
                }

                var immutable = contribution.WithImmutableCollections();
                ValidateFactsDebug(entry.Descriptor, immutable);
                RejectUnhandledContributorActions_NoLock(entry, immutable);

                var previousIssues = entry.IssueCreatedAtByKey;
                var merged = MergeIssues(immutable, previousIssues, _timeProvider.GetUtcNow());
                entry.Contribution = merged;
                entry.IssueCreatedAtByKey = merged.Issues.ToDictionary(
                    issue => issue.DedupeKey,
                    issue => issue.CreatedAt,
                    StringComparer.Ordinal);

                AppendEvent(
                    ApplicationOrchestrationEventKind.ContributionPulled,
                    "Contribution pulled",
                    entry.Descriptor.ProviderId);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && !_lifetimeCts.IsCancellationRequested)
        {
            lock (_stateSync)
            {
                _timeoutCounts[entry.Descriptor.ProviderId] =
                    _timeoutCounts.GetValueOrDefault(entry.Descriptor.ProviderId) + 1;
                entry.IsDirty = false;

                if (entry.Contribution is null)
                {
                    entry.Contribution = CreateSynthetic(
                        entry.Descriptor.ProviderId,
                        ContributorHealth.Unknown,
                        ApplicationIssueCodes.ContributorTimeout,
                        "Contributor pull timed out.");
                }
                else
                {
                    entry.ForceStale = true;
                }

                AppendEvent(
                    ApplicationOrchestrationEventKind.ContributionTimedOut,
                    "Contribution timed out",
                    entry.Descriptor.ProviderId);
                AppendEvent(
                    ApplicationOrchestrationEventKind.ContributionMarkedStale,
                    "Contribution marked stale",
                    entry.Descriptor.ProviderId);
            }
        }
        catch (Exception ex)
        {
            lock (_stateSync)
            {
                _lastPullDurations[entry.Descriptor.ProviderId] = _timeProvider.GetElapsedTime(started);
                ApplyFailedContribution_NoLock(entry, ex.GetType().Name);
            }
        }
    }

    private void ApplyFailedContribution_NoLock(ContributorEntry entry, string detail)
    {
        _failureCounts[entry.Descriptor.ProviderId] =
            _failureCounts.GetValueOrDefault(entry.Descriptor.ProviderId) + 1;
        entry.IsDirty = false;
        entry.Contribution = CreateSynthetic(
            entry.Descriptor.ProviderId,
            ContributorHealth.Error,
            ApplicationIssueCodes.ContributorFailed,
            "Contributor failed while producing a contribution.",
            detail);

        AppendEvent(
            ApplicationOrchestrationEventKind.ContributionFailed,
            "Contribution failed",
            entry.Descriptor.ProviderId,
            detail: detail);
    }

    private ApplicationContribution CreateSynthetic(
        string providerId,
        ContributorHealth health,
        string issueCode,
        string summary,
        string? detail = null)
    {
        var now = _timeProvider.GetUtcNow();
        var severity = health == ContributorHealth.Error
            ? ApplicationIssueSeverity.Error
            : ApplicationIssueSeverity.Warning;

        return new ApplicationContribution(
            providerId,
            health,
            ContributorActivity.Inactive,
            Array.Empty<ApplicationFact>(),
            [
                new ApplicationIssue(issueCode, severity, summary, providerId, now)
                {
                    Detail = detail,
                    UpdatedAt = now
                }
            ],
            Array.Empty<ApplicationAction>(),
            now);
    }

    private ApplicationContribution MergeIssues(
        ApplicationContribution contribution,
        Dictionary<string, DateTimeOffset> previousCreatedAt,
        DateTimeOffset now)
    {
        var normalized = SnapshotEvaluator.NormalizeIssues(contribution.Issues, contribution.ProviderId, now);
        var merged = new List<ApplicationIssue>();
        foreach (var issue in normalized)
        {
            if (previousCreatedAt.TryGetValue(issue.DedupeKey, out var createdAt))
            {
                merged.Add(issue with { CreatedAt = createdAt, UpdatedAt = now });
            }
            else
            {
                merged.Add(issue with { CreatedAt = issue.CreatedAt == default ? now : issue.CreatedAt, UpdatedAt = now });
            }
        }

        return contribution with { Issues = merged.ToArray() };
    }

    private void PublishSnapshot(SnapshotGenerationReason reason)
    {
        SnapshotEvaluator.EvaluationResult evaluation;
        ApplicationStateSnapshot candidate;
        ApplicationStateSnapshot previous;

        lock (_stateSync)
        {
            var inputs = BuildEvaluationInputs_NoLock();
            evaluation = SnapshotEvaluator.Evaluate(inputs, _timeProvider.GetUtcNow());
            previous = _current;

            candidate = new ApplicationStateSnapshot(
                evaluation.State,
                evaluation.OrderedProviders.ToArray(),
                evaluation.OrderedIssues.ToArray(),
                evaluation.PrimaryIssue,
                evaluation.OrderedActions.ToArray(),
                _timeProvider.GetUtcNow())
            {
                Facts = evaluation.Facts.ToArray(),
                StateSummary = evaluation.StateSummary,
                Reason = reason,
                Revision = previous.Revision
            };

            _latestSnapshotRule = evaluation.OverallStateRule;
            _primaryIssueRule = evaluation.PrimaryIssueRule;
            _overallStateRule = evaluation.OverallStateRule;

            if (evaluation.PrimaryIssue is not null)
            {
                AppendEvent(
                    ApplicationOrchestrationEventKind.PrimaryIssueSelected,
                    $"Primary issue: {evaluation.PrimaryIssue.Code}",
                    evaluation.PrimaryIssue.ProviderId,
                    detail: evaluation.PrimaryIssueRule);
            }

            AppendEvent(
                ApplicationOrchestrationEventKind.OverallStateSelected,
                $"Overall state: {evaluation.State}",
                detail: evaluation.OverallStateRule);

            if (SnapshotEvaluator.AreSemanticallyEqual(previous, candidate))
            {
                _noOpSuppressionCount++;
                AppendEvent(
                    ApplicationOrchestrationEventKind.SnapshotSuppressed,
                    "Snapshot suppressed as no-op",
                    snapshotRevision: previous.Revision);
                return;
            }

            _revision++;
            candidate = candidate with { Revision = _revision };
            Volatile.Write(ref _current, candidate);

            AppendEvent(
                ApplicationOrchestrationEventKind.SnapshotPublished,
                $"Snapshot {_revision} published",
                snapshotRevision: _revision);
        }

        try
        {
            SnapshotChanged?.Invoke(this, new ApplicationStateChangedEventArgs(candidate));
        }
        catch
        {
            // Subscriber isolation.
        }
    }

    private List<SnapshotEvaluator.ProviderEvaluationInput> BuildEvaluationInputs_NoLock()
    {
        var inputs = new List<SnapshotEvaluator.ProviderEvaluationInput>();
        var capabilityHealth = new Dictionary<string, CapabilityState>(StringComparer.Ordinal);

        // Seed every capability from its producer's own reported state, then refine below in
        // topological order so a producer that is itself blocked cannot satisfy what it
        // produces (§9.3 transitive propagation).
        foreach (var capability in _graph.ProducerByCapability.Keys.OrderBy(id => id, StringComparer.Ordinal))
        {
            capabilityHealth[capability] = _graph.EvaluateCapability(
                capability,
                ProducerHealth_NoLock,
                providerId => _entries.TryGetValue(providerId, out var entry) && IsStale_NoLock(entry));
        }

        foreach (var entry in OrderedEntriesForEvaluation_NoLock())
        {
            var contribution = GetEffectiveContribution_NoLock(entry);
            var unmet = new List<string>();
            var pending = new List<string>();
            var effectiveHealth = contribution.Health;
            var effectiveActivity = contribution.Activity;
            var syntheticIssues = new List<ApplicationIssue>(contribution.Issues);
            var syntheticFacts = new List<ApplicationFact>(contribution.Facts);
            string? rootCauseCapability = null;

            foreach (var required in entry.Descriptor.Requires)
            {
                var state = capabilityHealth.GetValueOrDefault(required, CapabilityState.Missing);
                switch (state)
                {
                    case CapabilityState.Unknown:
                        // Registered producer, not observed yet: pending, not broken (§9.2.1).
                        pending.Add(required);
                        if (effectiveHealth is ContributorHealth.Ready or ContributorHealth.Unknown)
                        {
                            effectiveHealth = ContributorHealth.Unavailable;
                        }

                        AppendEvent(
                            ApplicationOrchestrationEventKind.DependencyUnresolved,
                            $"{entry.Descriptor.DisplayName} unavailable — waiting for {required}.",
                            entry.Descriptor.ProviderId,
                            relatedCapabilityId: required,
                            detail: "pending: producer not yet observed");
                        break;
                    case CapabilityState.Missing:
                    case CapabilityState.Unhealthy:
                        unmet.Add(required);
                        effectiveHealth = ContributorHealth.Unavailable;
                        rootCauseCapability ??= required;
                        AppendEvent(
                            ApplicationOrchestrationEventKind.DependencyUnresolved,
                            $"{entry.Descriptor.DisplayName} unavailable — requires {required}, which is unresolved.",
                            entry.Descriptor.ProviderId,
                            relatedCapabilityId: required,
                            detail: state == CapabilityState.Missing
                                ? "unresolved: no registered producer"
                                : "unhealthy: producer reported failure");
                        break;
                    case CapabilityState.SatisfiedDegraded:
                    case CapabilityState.Stale:
                        unmet.Add(required);
                        if (effectiveHealth is ContributorHealth.Ready or ContributorHealth.Unknown)
                        {
                            effectiveHealth = ContributorHealth.Degraded;
                        }

                        var degradedNow = _timeProvider.GetUtcNow();
                        var degradedReason = state == CapabilityState.Stale ? "stale" : "degraded";
                        syntheticIssues.Add(new ApplicationIssue(
                            ApplicationIssueCodes.DependencyDegraded,
                            ApplicationIssueSeverity.Warning,
                            $"{entry.Descriptor.DisplayName} degraded — requires {required}, which is {degradedReason}.",
                            entry.Descriptor.ProviderId,
                            degradedNow)
                        {
                            UpdatedAt = degradedNow,
                            RelatedEntityId = required
                        });

                        AppendEvent(
                            ApplicationOrchestrationEventKind.DependencyUnresolved,
                            $"{entry.Descriptor.DisplayName} degraded — requires {required}, which is {degradedReason}.",
                            entry.Descriptor.ProviderId,
                            relatedCapabilityId: required,
                            detail: $"{degradedReason}: producer satisfied with reduced confidence");

                        break;
                }
            }

            foreach (var optional in entry.Descriptor.Optional)
            {
                var state = capabilityHealth.GetValueOrDefault(optional, CapabilityState.Missing);
                if (state is CapabilityState.Missing or CapabilityState.Unhealthy or CapabilityState.Unknown)
                {
                    syntheticFacts.Add(new ApplicationFact(
                        $"{entry.Descriptor.ProviderId}.optional_capability_absent",
                        new ApplicationFactValue.Text(optional),
                        ApplicationFactScope.Provider,
                        _timeProvider.GetUtcNow())
                    {
                        Confidence = ApplicationFactConfidence.Observed,
                        Display = new ApplicationFactDisplay("Optional capability absent", IsDiagnosticOnly: true)
                    });
                }
            }

            if (entry.LifecycleState == ApplicationContributorLifecycleState.Faulted)
            {
                effectiveHealth = contribution.Health; // do not rewrite health
                var severity = entry.Descriptor.Importance == ApplicationContributorImportance.Critical
                    ? ApplicationIssueSeverity.Critical
                    : ApplicationIssueSeverity.Error;
                var now = _timeProvider.GetUtcNow();
                syntheticIssues.Add(new ApplicationIssue(
                    ApplicationIssueCodes.ContributorLifecycleFailed,
                    severity,
                    $"{entry.Descriptor.DisplayName} lifecycle failed.",
                    entry.Descriptor.ProviderId,
                    now)
                {
                    UpdatedAt = now,
                    RequiresUserAction = false
                });
            }

            // A provider blocked only by unobserved dependencies is waiting, not broken (§9.2.1).
            // Its own reported problems, if any, take precedence over the pending narrative.
            var blockedByPendingOnly = pending.Count > 0
                && unmet.Count == 0
                && contribution.Health is ContributorHealth.Ready or ContributorHealth.Unknown
                && entry.LifecycleState is not (ApplicationContributorLifecycleState.Faulted
                    or ApplicationContributorLifecycleState.Stopping
                    or ApplicationContributorLifecycleState.Stopped);

            if (blockedByPendingOnly)
            {
                effectiveActivity = ContributorActivity.Waiting;
            }

            foreach (var pendingCapability in pending)
            {
                var pendingNow = _timeProvider.GetUtcNow();
                syntheticIssues.Add(new ApplicationIssue(
                    ApplicationIssueCodes.DependencyPending,
                    ApplicationIssueSeverity.Information,
                    $"{entry.Descriptor.DisplayName} unavailable — waiting for {pendingCapability}.",
                    entry.Descriptor.ProviderId,
                    pendingNow)
                {
                    UpdatedAt = pendingNow,
                    RequiresUserAction = false,
                    RelatedEntityId = pendingCapability
                });
            }

            if (rootCauseCapability is not null)
            {
                var now = _timeProvider.GetUtcNow();
                var isRoot = IsRootDependencyFailure_NoLock(entry.Descriptor.ProviderId, rootCauseCapability, capabilityHealth);
                syntheticIssues.Add(new ApplicationIssue(
                    ApplicationIssueCodes.DependencyUnresolved,
                    isRoot
                        ? (entry.Descriptor.Importance == ApplicationContributorImportance.Critical
                            ? ApplicationIssueSeverity.Critical
                            : ApplicationIssueSeverity.Error)
                        : ApplicationIssueSeverity.Information,
                    $"{entry.Descriptor.DisplayName} unavailable — requires {rootCauseCapability}, which is unresolved.",
                    entry.Descriptor.ProviderId,
                    now)
                {
                    UpdatedAt = now,
                    Detail = rootCauseCapability
                });
            }

            if (IsStale_NoLock(entry)
                && entry.Descriptor.Importance == ApplicationContributorImportance.Critical
                && entry.LifecycleState == ApplicationContributorLifecycleState.Running)
            {
                var now = _timeProvider.GetUtcNow();
                syntheticIssues.Add(new ApplicationIssue(
                    ApplicationIssueCodes.ContributorStale,
                    ApplicationIssueSeverity.Warning,
                    $"{entry.Descriptor.DisplayName} contribution is stale.",
                    entry.Descriptor.ProviderId,
                    now)
                {
                    UpdatedAt = now
                });
            }

            var adjusted = contribution with
            {
                Health = effectiveHealth,
                Activity = effectiveActivity,
                Issues = SnapshotEvaluator.NormalizeIssues(syntheticIssues, entry.Descriptor.ProviderId, _timeProvider.GetUtcNow()),
                Facts = syntheticFacts.ToArray()
            };

            inputs.Add(new SnapshotEvaluator.ProviderEvaluationInput(
                entry.Descriptor,
                entry.LifecycleState,
                adjusted,
                IsStale_NoLock(entry),
                unmet,
                effectiveHealth));

            // Publish this provider's produced capabilities from its dependency-adjusted state.
            // A pending-blocked provider yields Unknown rather than Unhealthy, so the cascade
            // stays informational instead of turning startup into an error one level down (§9.3).
            CapabilityState producedState;
            if (blockedByPendingOnly)
            {
                producedState = CapabilityState.Unknown;
            }
            else if (entry.LifecycleState is ApplicationContributorLifecycleState.Faulted
                or ApplicationContributorLifecycleState.Stopping
                or ApplicationContributorLifecycleState.Stopped)
            {
                producedState = CapabilityState.Unhealthy;
            }
            else
            {
                producedState = CapabilityGraph.EvaluateState(effectiveHealth, IsStale_NoLock(entry));
            }

            foreach (var produced in entry.Descriptor.Produces)
            {
                capabilityHealth[produced] = producedState;
            }
        }

        return inputs;
    }

    /// <summary>
    /// Evaluation order: capability-graph topological order first so producers are resolved
    /// before their consumers, then any entry the graph does not yet order (registration
    /// before <see cref="StartAsync"/>), by provider ID for determinism.
    /// </summary>
    private List<ContributorEntry> OrderedEntriesForEvaluation_NoLock()
    {
        var ordered = new List<ContributorEntry>(_entries.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var providerId in _graph.TopologicalOrder)
        {
            if (_entries.TryGetValue(providerId, out var entry) && seen.Add(providerId))
            {
                ordered.Add(entry);
            }
        }

        foreach (var pair in _entries.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (seen.Add(pair.Key))
            {
                ordered.Add(pair.Value);
            }
        }

        return ordered;
    }

    private ContributorHealth? ProducerHealth_NoLock(string providerId)
    {
        if (!_entries.TryGetValue(providerId, out var entry))
        {
            return null;
        }

        if (entry.LifecycleState is ApplicationContributorLifecycleState.Faulted
            or ApplicationContributorLifecycleState.Stopped
            or ApplicationContributorLifecycleState.Stopping)
        {
            return ContributorHealth.Unavailable;
        }

        return GetEffectiveContribution_NoLock(entry).Health;
    }

    private bool IsRootDependencyFailure_NoLock(
        string providerId,
        string capabilityId,
        IReadOnlyDictionary<string, CapabilityState> capabilityHealth)
    {
        if (!_graph.TryGetProducer(capabilityId, out var producerId) || producerId is null)
        {
            return true;
        }

        if (!_entries.TryGetValue(producerId, out var producer))
        {
            return true;
        }

        return producer.Descriptor.Requires.All(required =>
            capabilityHealth.GetValueOrDefault(required, CapabilityState.Missing) is CapabilityState.Satisfied);
    }

    private ApplicationContribution GetEffectiveContribution_NoLock(ContributorEntry entry)
    {
        if (entry.Contribution is not null)
        {
            return entry.Contribution;
        }

        return ApplicationContribution.Empty(entry.Descriptor.ProviderId, _timeProvider.GetUtcNow());
    }

    private bool IsStale_NoLock(ContributorEntry entry)
    {
        if (entry.ForceStale)
        {
            return true;
        }

        var contribution = entry.Contribution;
        if (contribution is null)
        {
            return false;
        }

        var now = _timeProvider.GetUtcNow();
        if (contribution.ExpiresAt is { } expiresAt && now > expiresAt)
        {
            return true;
        }

        if (entry.Descriptor.MaxAge is { } maxAge && now - contribution.ObservedAt > maxAge)
        {
            return true;
        }

        return false;
    }

    private List<string> CollectUnresolvedDependencies_NoLock()
    {
        var unresolved = new List<string>();
        foreach (var entry in _entries.Values)
        {
            foreach (var required in entry.Descriptor.Requires)
            {
                if (!_graph.TryGetProducer(required, out _))
                {
                    unresolved.Add($"{entry.Descriptor.ProviderId} requires {required}");
                }
            }
        }

        return unresolved.OrderBy(item => item, StringComparer.Ordinal).ToList();
    }

    private void ValidateFactsDebug(ApplicationContributorDescriptor descriptor, ApplicationContribution contribution)
    {
#if DEBUG
        foreach (var fact in contribution.Facts)
        {
            if (!DescriptorValidator.FactKeyBelongsToContributor(fact.Key, descriptor))
            {
                System.Diagnostics.Debug.Fail(
                    $"Fact key '{fact.Key}' is not in a namespace owned by '{descriptor.ProviderId}'.");
            }
        }
#endif
    }

    private void RejectUnhandledContributorActions_NoLock(ContributorEntry entry, ApplicationContribution contribution)
    {
        if (entry.Contributor is IApplicationActionHandler)
        {
            return;
        }

        foreach (var action in contribution.Actions.Where(a => a.Target == ApplicationActionTarget.Contributor))
        {
            var message =
                $"Contributor '{entry.Descriptor.ProviderId}' offered action '{action.ActionId}' without IApplicationActionHandler.";
            _validationMessages.Add(message);
            AppendEvent(
                ApplicationOrchestrationEventKind.ActionInvocationFailed,
                "Contributor action rejected: no handler",
                entry.Descriptor.ProviderId,
                detail: action.ActionId);
        }
    }

    private static IEnumerable<ApplicationAction> GetDeclaredContributorActions(IApplicationContributor contributor) =>
        Array.Empty<ApplicationAction>();

    private void AppendEvent(
        ApplicationOrchestrationEventKind kind,
        string summary,
        string? providerId = null,
        string? relatedCapabilityId = null,
        long? snapshotRevision = null,
        string? detail = null)
    {
        // Callers may already hold _stateSync; Monitor is reentrant.
        lock (_stateSync)
        {
            _eventHistory.Append(
                _timeProvider.GetUtcNow(),
                kind,
                summary,
                providerId,
                relatedCapabilityId,
                snapshotRevision,
                detail);
        }
    }

    private sealed class ContributorEntry(IApplicationContributor contributor, ApplicationContributorDescriptor descriptor)
    {
        public IApplicationContributor Contributor { get; } = contributor;

        public ApplicationContributorDescriptor Descriptor { get; } = descriptor;

        public ApplicationContributorLifecycleState LifecycleState { get; set; }

        public ApplicationContribution? Contribution { get; set; }

        public bool IsDirty { get; set; } = true;

        public bool ForceStale { get; set; }

        public bool LifecycleFaulted { get; set; }

        public Dictionary<string, DateTimeOffset> IssueCreatedAtByKey { get; set; } = new(StringComparer.Ordinal);
    }
}
