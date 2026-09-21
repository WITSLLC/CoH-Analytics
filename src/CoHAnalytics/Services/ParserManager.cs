using System.Threading.Channels;
using CoHAnalytics.Models;
using CoHAnalytics.Services.Diagnostics;

namespace CoHAnalytics.Services;

/// <summary>
/// Serializes immutable monitoring-manager snapshots into one parser worker per eligible context
/// and dispatches raw events through a bounded queue.
/// </summary>
public sealed class ParserManager : IParserManager, IDisposable, IAsyncDisposable
{
    private readonly IMonitoringSessionManager _monitoringManager;
    private readonly IParserWorkerFactory _workerFactory;
    private readonly IParserClassifier _classifier;
    private readonly ParserManagerOptions _options;
    private readonly IDiagnosticLog? _diagnosticLog;
    private readonly Func<IReadOnlyList<HomecomingProcessInstance>>? _runningClientsProvider;
    private readonly Func<LogActivitySnapshot>? _logActivitySnapshotProvider;
    private readonly object _stateLock = new();
    private readonly object _ingressLock = new();
    private readonly Dictionary<MonitoringContextId, IParserWorker> _workers = [];
    private readonly Dictionary<ParserWorkerId, ParserWorkerSnapshot> _diagnosticWorkerSnapshots = [];
    private readonly Dictionary<ParserEventCorrelationKey, ParserEventOrigin> _eventWorkerCorrelations = [];
    private readonly Queue<string> _recentDecisions = [];
    private readonly Queue<string> _recentClassifierFailures = [];
    private readonly Dictionary<string, long> _classificationRuleCounts = new(StringComparer.Ordinal);
    private Channel<SnapshotWorkItem>? _snapshotChannel;
    private Channel<ParserRawEvent>? _eventChannel;
    private CancellationTokenSource? _lifetimeCancellation;
    private Task? _reconciliationTask;
    private Task? _eventDispatcherTask;
    private ParserManagerSnapshot _current = ParserManagerSnapshot.Empty;
    private ParserClassificationSnapshot _classificationCurrent = ParserClassificationSnapshot.Empty;
    private long _lastMonitoringSnapshotRevision;
    private long _lifecycleEpoch;
    private readonly QueuePressureTracker _monitoringSnapshotQueueTracker;
    private readonly QueuePressureTracker _eventQueueTracker;
    private int _queuedEventCount;
    private bool _running;
    private bool _disposed;

    public ParserManager(
        IMonitoringSessionManager monitoringManager,
        ParserManagerOptions? options = null,
        IParserWorkerFactory? workerFactory = null,
        IParserClassifier? classifier = null,
        IDiagnosticLog? diagnosticLog = null,
        Func<IReadOnlyList<HomecomingProcessInstance>>? runningClientsProvider = null,
        Func<LogActivitySnapshot>? logActivitySnapshotProvider = null)
    {
        ArgumentNullException.ThrowIfNull(monitoringManager);
        _monitoringManager = monitoringManager;
        _options = options ?? new ParserManagerOptions();
        _options.Validate();
        _diagnosticLog = diagnosticLog;
        _runningClientsProvider = runningClientsProvider;
        _logActivitySnapshotProvider = logActivitySnapshotProvider;
        _workerFactory = workerFactory ?? new DiagnosticParserWorkerFactory(_options, diagnosticLog);
        _classifier = classifier ?? new ParserClassifier();
        _monitoringSnapshotQueueTracker = new QueuePressureTracker(_options.MonitoringSnapshotQueueCapacity);
        _eventQueueTracker = new QueuePressureTracker(_options.EventQueueCapacity);
    }

    public ParserManagerSnapshot Current
    {
        get
        {
            lock (_stateLock)
            {
                return _current;
            }
        }
    }

    public ParserClassificationSnapshot ClassificationCurrent
    {
        get
        {
            lock (_stateLock)
            {
                return _classificationCurrent;
            }
        }
    }

    internal bool IsRunning
    {
        get
        {
            lock (_stateLock)
            {
                return _running;
            }
        }
    }

    public event EventHandler<ParserManagerChangedEventArgs>? StateChanged;

    public event EventHandler<ParserEventsAvailableEventArgs>? EventsAvailable;

    public event EventHandler<ParserClassificationChangedEventArgs>? ClassificationChanged;

    public event EventHandler<ParserEventsClassifiedEventArgs>? ClassifiedEventsAvailable;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Task initialReconciliation;

        lock (_ingressLock)
        {
            lock (_stateLock)
            {
                if (_running)
                {
                    return;
                }

                _running = true;
                _lifecycleEpoch++;
                _monitoringSnapshotQueueTracker.Reset(_lifecycleEpoch);
                _eventQueueTracker.Reset(_lifecycleEpoch);
                _queuedEventCount = 0;
                _classificationCurrent = ParserClassificationSnapshot.Empty;
                _classificationRuleCounts.Clear();
                _recentClassifierFailures.Clear();
                _lifetimeCancellation = new CancellationTokenSource();
                _snapshotChannel = Channel.CreateBounded<SnapshotWorkItem>(
                    new BoundedChannelOptions(_options.MonitoringSnapshotQueueCapacity)
                    {
                        SingleReader = true,
                        SingleWriter = false,
                        FullMode = BoundedChannelFullMode.Wait
                    });
                _eventChannel = Channel.CreateBounded<ParserRawEvent>(
                    new BoundedChannelOptions(_options.EventQueueCapacity)
                    {
                        SingleReader = true,
                        SingleWriter = false,
                        FullMode = BoundedChannelFullMode.Wait
                    });
            }

            _monitoringManager.StateChanged += OnMonitoringStateChanged;
            var initialCompletion = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var initial = new SnapshotWorkItem(_monitoringManager.Current, initialCompletion);
            if (!QueuePressureAdmission.TryPublish(_snapshotChannel!.Writer, initial, _monitoringSnapshotQueueTracker))
            {
                throw new InvalidOperationException("The parser snapshot queue rejected its startup baseline.");
            }

            _reconciliationTask = ProcessSnapshotsAsync(
                _snapshotChannel.Reader,
                _lifetimeCancellation!.Token);
            _eventDispatcherTask = DispatchEventsAsync(
                _eventChannel!.Reader,
                _lifetimeCancellation.Token);
            initialReconciliation = initialCompletion.Task;
        }

        try
        {
            await initialReconciliation.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await StopAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        Channel<SnapshotWorkItem>? snapshotChannel;
        Channel<ParserRawEvent>? eventChannel;
        CancellationTokenSource? lifetimeCancellation;
        Task? reconciliationTask;
        Task? dispatcherTask;

        lock (_ingressLock)
        {
            lock (_stateLock)
            {
                if (!_running)
                {
                    return;
                }

                _running = false;
                snapshotChannel = _snapshotChannel;
                eventChannel = _eventChannel;
                lifetimeCancellation = _lifetimeCancellation;
                reconciliationTask = _reconciliationTask;
                dispatcherTask = _eventDispatcherTask;
            }

            _monitoringManager.StateChanged -= OnMonitoringStateChanged;
            snapshotChannel?.Writer.TryComplete();
            eventChannel?.Writer.TryComplete();
            lifetimeCancellation?.Cancel();
        }

        await AwaitCancellationAsync(reconciliationTask).ConfigureAwait(false);
        await AwaitCancellationAsync(dispatcherTask).ConfigureAwait(false);
        _monitoringSnapshotQueueTracker.AbandonUnfinishedAtShutdown();
        _eventQueueTracker.AbandonUnfinishedAtShutdown();
        Interlocked.Exchange(ref _queuedEventCount, 0);

        IParserWorker[] workers;
        lock (_stateLock)
        {
            workers = [.. _workers.Values];
            foreach (var worker in workers)
            {
                worker.StateChanged -= OnWorkerStateChanged;
                worker.RawEventAvailable -= OnRawEventAvailable;
            }

            _workers.Clear();
            _eventWorkerCorrelations.Clear();
        }

        foreach (var worker in workers)
        {
            WriteWorkerRemoved(worker.Current, "ParserManagerStopped");
            lock (_stateLock)
            {
                _diagnosticWorkerSnapshots.Remove(worker.WorkerId);
            }
        }

        foreach (var worker in workers)
        {
            await worker.StopAsync(cancellationToken).ConfigureAwait(false);
            await worker.DisposeAsync().ConfigureAwait(false);
        }

        lifetimeCancellation?.Dispose();
        PublishAggregateSnapshot();
        if (workers.Length > 0)
        {
            WriteParserStateSnapshot("ParserWorkerRemoved");
        }
    }

    public ParserManagerDiagnostics GetDiagnostics()
    {
        lock (_stateLock)
        {
            var monitoringQueue = _monitoringSnapshotQueueTracker.Snapshot();
            var eventQueue = _eventQueueTracker.Snapshot();
            return new ParserManagerDiagnostics
            {
                IsRunning = _running,
                LastMonitoringSnapshotRevision = _lastMonitoringSnapshotRevision,
                LastParserSnapshotRevision = _current.Revision,
                LifecycleEpoch = _lifecycleEpoch,
                QueuedEventCount = eventQueue.CurrentDepth,
                MonitoringSnapshotQueue = monitoringQueue,
                EventQueue = eventQueue,
                MonitoringSnapshotQueueOverflowed = monitoringQueue.Overflowed,
                EventQueueOverflowed = eventQueue.Overflowed,
                Classification = BuildClassificationDiagnosticsLocked(),
                Workers = [.. _workers.Values
                    .Select(worker => worker.Current)
                    .OrderBy(worker => worker.ContextId.ToString(), StringComparer.Ordinal)
                    .Select(ToDiagnostics)],
                RecentDecisions = [.. _recentDecisions]
            };
        }
    }

    public ParserClassificationDiagnostics GetClassificationDiagnostics()
    {
        lock (_stateLock)
        {
            return BuildClassificationDiagnosticsLocked();
        }
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().ConfigureAwait(false).GetAwaiter().GetResult();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await StopAsync().ConfigureAwait(false);
        _disposed = true;
    }

    private void OnMonitoringStateChanged(object? sender, MonitoringSessionManagerChangedEventArgs e)
    {
        lock (_ingressLock)
        {
            if (!IsRunning || _snapshotChannel is null)
            {
                return;
            }

            if (QueuePressureAdmission.TryPublish(
                    _snapshotChannel.Writer,
                    new SnapshotWorkItem(e.Snapshot, null),
                    _monitoringSnapshotQueueTracker))
            {
                return;
            }

            _monitoringSnapshotQueueTracker.RecordRejected();
            _monitoringSnapshotQueueTracker.LatchOverflow();
            lock (_stateLock)
            {
                RecordDecisionLocked("Monitoring snapshot queue overflowed; workers were faulted safely.");
            }

            _ = Task.Run(() => FaultAllWorkersAsync(
                "monitoring_snapshot_queue_overflow",
                "The parser could not retain every monitoring transition snapshot."));
        }
    }

    private async Task ProcessSnapshotsAsync(
        ChannelReader<SnapshotWorkItem> reader,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var item in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                _monitoringSnapshotQueueTracker.RecordDequeued();
                try
                {
                    try
                    {
                        await ReconcileSnapshotAsync(item.Snapshot, cancellationToken).ConfigureAwait(false);
                        item.Completion?.TrySetResult();
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        item.Completion?.TrySetCanceled(cancellationToken);
                        throw;
                    }
                    catch (Exception exception)
                    {
                        item.Completion?.TrySetException(exception);
                        lock (_stateLock)
                        {
                            RecordDecisionLocked($"Reconciliation failed: {exception.GetType().Name}.");
                        }

                        await FaultAllWorkersAsync(
                            "reconciliation_failure",
                            "Parser reconciliation failed safely.").ConfigureAwait(false);
                    }
                }
                finally
                {
                    _monitoringSnapshotQueueTracker.RecordCompleted();
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task ReconcileSnapshotAsync(
        MonitoringSessionManagerSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        lock (_stateLock)
        {
            if (_lastMonitoringSnapshotRevision != 0
                && snapshot.Revision < _lastMonitoringSnapshotRevision)
            {
                RecordDecisionLocked(
                    $"Ignored stale monitoring snapshot revision {snapshot.Revision} after {_lastMonitoringSnapshotRevision}.");
                return;
            }

            _lastMonitoringSnapshotRevision = Math.Max(_lastMonitoringSnapshotRevision, snapshot.Revision);
        }

        var observedContexts = new HashSet<MonitoringContextId>();
        foreach (var context in snapshot.Contexts
                     .OrderBy(context => context.ContextId.ToString(), StringComparer.Ordinal))
        {
            observedContexts.Add(context.ContextId);
            IParserWorker? worker;
            lock (_stateLock)
            {
                _workers.TryGetValue(context.ContextId, out worker);
            }

            if (context.State == MonitoringContextState.Stopped
                || context.LastSourceBindingTransitionKind == MonitoringSourceTransitionKind.ContextRemoved)
            {
                if (worker is not null)
                {
                    await worker.ApplyContextAsync(context, cancellationToken).ConfigureAwait(false);
                    await RemoveWorkerAsync(worker, cancellationToken, "Context removed; parser worker disposed.")
                        .ConfigureAwait(false);
                }

                continue;
            }

            if (worker is null && context.CurrentSourceId is null)
            {
                continue;
            }

            if (worker is null)
            {
                worker = _workerFactory.Create(context.ContextId);
                worker.StateChanged += OnWorkerStateChanged;
                worker.RawEventAvailable += OnRawEventAvailable;
                lock (_stateLock)
                {
                    if (!_workers.TryAdd(context.ContextId, worker))
                    {
                        throw new InvalidOperationException("A duplicate parser worker was created for one context.");
                    }

                    RecordDecisionLocked($"Created parser worker for context {context.ContextId}.");
                    _diagnosticWorkerSnapshots[worker.WorkerId] = worker.Current;
                }

                WriteDiagnostic(new ParserWorkerCreatedDiagnosticEvent
                {
                    WorkerId = worker.WorkerId.ToString(),
                    ContextId = worker.ContextId.ToString(),
                    SourceId = worker.Current.CurrentSourceId?.Value,
                    BindingGeneration = worker.Current.AppliedSourceBindingGeneration == 0
                        ? null
                        : worker.Current.AppliedSourceBindingGeneration,
                    State = worker.Current.State
                });

                await worker.StartAsync(cancellationToken).ConfigureAwait(false);
            }

            var before = worker.Current;
            await worker.ApplyContextAsync(context, cancellationToken).ConfigureAwait(false);
            var after = worker.Current;
            lock (_stateLock)
            {
                if (after.AppliedSourceBindingGeneration != before.AppliedSourceBindingGeneration)
                {
                    RecordDecisionLocked(
                        $"Context {context.ContextId} applied generation {after.AppliedSourceBindingGeneration} ({after.LastAppliedTransitionKind}).");
                }

                if (after.State != before.State)
                {
                    RecordDecisionLocked(
                        $"Context {context.ContextId} parser state changed from {before.State} to {after.State}.");
                }
            }
        }

        IParserWorker[] orphaned;
        lock (_stateLock)
        {
            orphaned = [.. _workers
                .Where(pair => !observedContexts.Contains(pair.Key))
                .Select(pair => pair.Value)];
        }

        foreach (var worker in orphaned)
        {
            await RemoveWorkerAsync(worker, cancellationToken, "Context disappeared; parser worker disposed.")
                .ConfigureAwait(false);
        }

        PublishAggregateSnapshot();
    }

    private async Task RemoveWorkerAsync(
        IParserWorker worker,
        CancellationToken cancellationToken,
        string decision)
    {
        worker.StateChanged -= OnWorkerStateChanged;
        worker.RawEventAvailable -= OnRawEventAvailable;
        lock (_stateLock)
        {
            _workers.Remove(worker.ContextId);
            _diagnosticWorkerSnapshots.Remove(worker.WorkerId);
            RecordDecisionLocked(decision);
        }

        WriteWorkerRemoved(
            worker.Current,
            decision.StartsWith("Context disappeared", StringComparison.Ordinal)
                ? "MonitoringContextDisappeared"
                : "MonitoringContextRemoved");

        await worker.StopAsync(cancellationToken).ConfigureAwait(false);
        await worker.DisposeAsync().ConfigureAwait(false);
        PublishAggregateSnapshot();
        WriteParserStateSnapshot("ParserWorkerRemoved");
    }

    private void OnWorkerStateChanged(object? sender, ParserWorkerChangedEventArgs e)
    {
        ParserWorkerSnapshot? previous;
        lock (_stateLock)
        {
            _diagnosticWorkerSnapshots.TryGetValue(e.Snapshot.WorkerId, out previous);
            _diagnosticWorkerSnapshots[e.Snapshot.WorkerId] = e.Snapshot;
        }

        var bindingChanged = previous is not null
            && (previous.AppliedSourceBindingGeneration != e.Snapshot.AppliedSourceBindingGeneration
                || previous.CurrentSourceId != e.Snapshot.CurrentSourceId);
        var lifecycleChanged = previous is not null
            && previous.State != e.Snapshot.State
            && IsStandardLifecycleTransition(previous.State, e.Snapshot.State);

        if (bindingChanged)
        {
            WriteDiagnostic(new ParserWorkerBindingAppliedDiagnosticEvent
            {
                WorkerId = e.Snapshot.WorkerId.ToString(),
                ContextId = e.Snapshot.ContextId.ToString(),
                SourceId = e.Snapshot.CurrentSourceId?.Value,
                SourceSegmentId = e.Snapshot.CurrentSegment?.SourceSegmentId.ToString(),
                BindingGeneration = e.Snapshot.AppliedSourceBindingGeneration,
                TransitionKind = e.Snapshot.LastAppliedTransitionKind,
                StartingOffset = e.Snapshot.CurrentSegment?.StartingOffset
            });
        }

        if (lifecycleChanged)
        {
            WriteDiagnostic(new ParserWorkerStateChangedDiagnosticEvent
            {
                WorkerId = e.Snapshot.WorkerId.ToString(),
                ContextId = e.Snapshot.ContextId.ToString(),
                PreviousState = previous!.State,
                NextState = e.Snapshot.State,
                Reason = WorkerStateReason(e.Snapshot)
            });
        }

        if (e.Snapshot.State == ParserWorkerState.Faulted)
        {
            lock (_stateLock)
            {
                RecordDecisionLocked(
                    $"Context {e.Snapshot.ContextId} parser faulted ({e.Snapshot.FaultCode ?? "unknown"}).");
            }
        }

        if (IsRunning)
        {
            PublishAggregateSnapshot();
        }

        if (bindingChanged || lifecycleChanged || e.Snapshot.State == ParserWorkerState.Faulted)
        {
            WriteParserStateSnapshot(
                e.Snapshot.State == ParserWorkerState.Faulted
                    ? "ParserWorkerFaulted"
                    : bindingChanged
                        ? "ParserWorkerBindingChanged"
                        : "ParserWorkerStateChanged");
        }
    }

    private void OnRawEventAvailable(object? sender, ParserRawEventAvailableEventArgs e)
    {
        Channel<ParserRawEvent>? channel;
        lock (_stateLock)
        {
            if (!_running)
            {
                return;
            }

            channel = _eventChannel;
        }

        ParserEventCorrelationKey? correlationKey = null;
        if (_diagnosticLog is not null && channel is not null && sender is IParserWorker originatingWorker)
        {
            correlationKey = ParserEventCorrelationKey.From(e.ParserEvent);
            var accountStableId = _monitoringManager.Current.Contexts
                .FirstOrDefault(context => context.ContextId == e.ParserEvent.ContextId)
                ?.AccountStableId;
            lock (_stateLock)
            {
                _eventWorkerCorrelations[correlationKey.Value] = new ParserEventOrigin(
                    originatingWorker.WorkerId,
                    accountStableId);
            }
        }

        if (channel is not null
            && QueuePressureAdmission.TryPublish(channel.Writer, e.ParserEvent, _eventQueueTracker))
        {
            Interlocked.Increment(ref _queuedEventCount);
            return;
        }

        if (correlationKey is not null)
        {
            lock (_stateLock)
            {
                _eventWorkerCorrelations.Remove(correlationKey.Value);
            }
        }

        _eventQueueTracker.RecordRejected();
        _eventQueueTracker.LatchOverflow();
        lock (_stateLock)
        {
            RecordDecisionLocked($"Event queue overflowed for context {e.ParserEvent.ContextId}.");
        }

        if (sender is IParserWorker worker)
        {
            _ = Task.Run(() => worker.FaultAsync(
                "event_queue_overflow",
                "Raw event delivery capacity was exhausted; no silent drop was permitted."));
        }
    }

    private async Task DispatchEventsAsync(
        ChannelReader<ParserRawEvent> reader,
        CancellationToken cancellationToken)
    {
        try
        {
            while (await reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                List<ParserRawEvent> batch = [];
                while (batch.Count < _options.EventBatchSize && reader.TryRead(out var parserEvent))
                {
                    _eventQueueTracker.RecordDequeued();
                    Interlocked.Decrement(ref _queuedEventCount);
                    batch.Add(parserEvent);
                }

                if (batch.Count > 0)
                {
                    try
                    {
                        if (IsRunning)
                        {
                            var classified = ClassifyBatch(batch);
                            PublishClassification(classified);
                            var args = new ParserEventsAvailableEventArgs(batch);
                            var handlers = EventsAvailable?.GetInvocationList() ?? [];
                            foreach (EventHandler<ParserEventsAvailableEventArgs> handler in handlers)
                            {
                                try
                                {
                                    handler(this, args);
                                }
                                catch (Exception exception)
                                {
                                    lock (_stateLock)
                                    {
                                        RecordDecisionLocked(
                                            $"Event subscriber failed ({exception.GetType().Name}).");
                                    }
                                }
                            }

                            var classifiedArgs = new ParserEventsClassifiedEventArgs(classified);
                            var classifiedHandlers = ClassifiedEventsAvailable?.GetInvocationList() ?? [];
                            foreach (EventHandler<ParserEventsClassifiedEventArgs> handler in classifiedHandlers)
                            {
                                try
                                {
                                    handler(this, classifiedArgs);
                                }
                                catch (Exception exception)
                                {
                                    lock (_stateLock)
                                    {
                                        RecordDecisionLocked(
                                            $"Classified-event subscriber failed ({exception.GetType().Name}).");
                                    }
                                }
                            }
                        }
                    }
                    finally
                    {
                        for (var index = 0; index < batch.Count; index++)
                        {
                            _eventQueueTracker.RecordCompleted();
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void PublishAggregateSnapshot()
    {
        ParserManagerSnapshot? published = null;
        lock (_stateLock)
        {
            var workers = _workers.Values.Select(worker => worker.Current).ToArray();
            var lastEventAt = workers
                .Where(worker => worker.LastEventAt.HasValue)
                .Select(worker => worker.LastEventAt)
                .Max();
            var candidate = ParserManagerSnapshot.Create(
                workers,
                lastEventAt,
                _options.TimeProvider.GetUtcNow(),
                _current.Revision + 1);

            if (!_current.IsSemanticallyEquivalentTo(candidate))
            {
                _current = candidate;
                published = candidate;
            }
        }

        if (published is not null && IsRunning)
        {
            var args = new ParserManagerChangedEventArgs(published);
            var handlers = StateChanged?.GetInvocationList() ?? [];
            foreach (EventHandler<ParserManagerChangedEventArgs> handler in handlers)
            {
                try
                {
                    handler(this, args);
                }
                catch (Exception exception)
                {
                    lock (_stateLock)
                    {
                        RecordDecisionLocked(
                            $"State subscriber failed ({exception.GetType().Name}).");
                    }
                }
            }
        }
    }

    private async Task FaultAllWorkersAsync(string code, string message)
    {
        IParserWorker[] workers;
        lock (_stateLock)
        {
            workers = [.. _workers.Values];
        }

        foreach (var worker in workers)
        {
            await worker.FaultAsync(code, message).ConfigureAwait(false);
        }
    }

    private void RecordDecisionLocked(string decision)
    {
        _recentDecisions.Enqueue(decision);
        while (_recentDecisions.Count > _options.MaximumRecentDecisions)
        {
            _recentDecisions.Dequeue();
        }
    }

    private IReadOnlyList<ParserEvent> ClassifyBatch(IReadOnlyList<ParserRawEvent> batch)
    {
        var classified = new List<ParserEvent>(batch.Count);
        foreach (var raw in batch)
        {
            ParserEvent parserEvent;
            try
            {
                parserEvent = _classifier.Classify(raw);
            }
            catch (Exception exception)
            {
                lock (_stateLock)
                {
                    _recentClassifierFailures.Enqueue(exception.GetType().Name);
                    while (_recentClassifierFailures.Count > _options.MaximumRecentDecisions)
                    {
                        _recentClassifierFailures.Dequeue();
                    }
                }

                parserEvent = new ParserEvent
                {
                    ContextId = raw.ContextId,
                    SourceId = raw.SourceId,
                    SourceSegmentId = raw.SourceSegmentId,
                    BindingGeneration = raw.BindingGeneration,
                    SourceTransitionKind = raw.SourceTransitionKind,
                    Sequence = raw.Sequence,
                    ObservedAt = raw.ObservedAt,
                    RawLine = raw.RawLine,
                    SourceByteStart = raw.SourceByteStart,
                    SourceByteEnd = raw.SourceByteEnd,
                    LineStatus = raw.LineStatus,
                    EventKind = ParserEventKind.Malformed,
                    ClassificationStatus = ParserClassificationStatus.ClassifierFailed,
                    ClassificationRuleId = "classifier_failed"
                };
            }

            classified.Add(parserEvent);
            WriteIdentityEvidence(parserEvent);
        }

        return classified;
    }

    private void WriteIdentityEvidence(ParserEvent parserEvent)
    {
        ParserEventOrigin origin;
        bool originFound;
        lock (_stateLock)
        {
            var key = ParserEventCorrelationKey.From(parserEvent);
            originFound = _eventWorkerCorrelations.Remove(key, out origin);
        }

        if (!originFound || parserEvent.StructuralEvidence is not { } evidence)
        {
            return;
        }

        WriteDiagnostic(new ParserIdentityEvidenceClassifiedDiagnosticEvent
        {
            WorkerId = origin.WorkerId.ToString(),
            ContextId = parserEvent.ContextId.ToString(),
            ParserSequence = parserEvent.Sequence,
            ParserEventKind = parserEvent.EventKind,
            EvidenceKind = evidence.EvidenceKind,
            CandidateCharacterName = evidence.CandidateName,
            AccountStableId = origin.AccountStableId,
            SourceId = parserEvent.SourceId.Value,
            SourceSegmentId = parserEvent.SourceSegmentId.ToString(),
            BindingGeneration = parserEvent.BindingGeneration
        });
    }

    private void WriteWorkerRemoved(ParserWorkerSnapshot worker, string reason) =>
        WriteDiagnostic(new ParserWorkerRemovedDiagnosticEvent
        {
            WorkerId = worker.WorkerId.ToString(),
            ContextId = worker.ContextId.ToString(),
            Reason = reason
        });

    private void WriteParserStateSnapshot(string reason)
    {
        try
        {
            ParserWorkerSnapshot[] workers;
            lock (_stateLock)
            {
                workers = _workers.Values.Select(worker => worker.Current).ToArray();
            }

            var monitoring = _monitoringManager.Current;
            var logActivity = _logActivitySnapshotProvider?.Invoke() ?? LogActivitySnapshot.Empty;
            WriteDiagnostic(new DiagnosticsStateSnapshotCapturedDiagnosticEvent
            {
                Reason = reason,
                RunningClients = (_runningClientsProvider?.Invoke() ?? [])
                    .Select(ToDiagnosticRuntimeClient)
                    .ToArray(),
                LogSources = logActivity.Candidates
                    .OrderBy(candidate => candidate.SourceId.Value, StringComparer.Ordinal)
                    .Select(candidate => new DiagnosticLogSource
                    {
                        SourceId = candidate.SourceId.Value,
                        AccountStableId = candidate.AccountStableId,
                        SourceFileName = candidate.SourceId.FileName,
                        ActivityState = candidate.ActivityState,
                        Length = candidate.Length,
                        LastGrowthAt = candidate.LastGrowthAt
                    })
                    .ToArray(),
                MonitoringContexts = monitoring.Contexts
                    .OrderBy(context => context.ContextId.ToString(), StringComparer.Ordinal)
                    .Select(context => new DiagnosticMonitoringContext
                    {
                        ContextId = context.ContextId.ToString(),
                        State = context.State,
                        AccountStableId = context.AccountStableId,
                        SourceId = context.CurrentSourceId?.Value,
                        SourceFileName = context.CurrentSourceId?.FileName,
                        ProcessInstance = context.ProcessInstance is null
                            ? null
                            : ToDiagnosticRuntimeClient(context.ProcessInstance),
                        BindingGeneration = context.SourceBindingGeneration
                    })
                    .ToArray(),
                ParserWorkers = workers
                    .OrderBy(worker => worker.WorkerId.ToString(), StringComparer.Ordinal)
                    .Select(worker => new DiagnosticParserWorker
                    {
                        WorkerId = worker.WorkerId.ToString(),
                        ContextId = worker.ContextId.ToString(),
                        State = worker.State,
                        SourceId = worker.CurrentSourceId?.Value,
                        SourceFileName = worker.CurrentSourceId?.FileName,
                        SourceSegmentId = worker.CurrentSegment?.SourceSegmentId.ToString(),
                        BindingGeneration = worker.AppliedSourceBindingGeneration
                    })
                    .ToArray()
            });
        }
        catch
        {
            // Snapshot diagnostics are observational and never affect parser behavior.
        }
    }

    private static bool IsStandardLifecycleTransition(ParserWorkerState previous, ParserWorkerState next) =>
        (previous, next) is not
            (ParserWorkerState.WaitingForData, ParserWorkerState.Reading)
            and not (ParserWorkerState.Reading, ParserWorkerState.WaitingForData);

    private static string WorkerStateReason(ParserWorkerSnapshot worker) =>
        worker.State == ParserWorkerState.Faulted
            ? worker.FaultCode ?? "WorkerFaulted"
            : worker.LastAppliedTransitionKind == MonitoringSourceTransitionKind.None
                ? "WorkerLifecycle"
                : worker.LastAppliedTransitionKind.ToString();

    private static DiagnosticRuntimeClient ToDiagnosticRuntimeClient(HomecomingProcessInstance process) =>
        new()
        {
            ProcessId = process.ProcessId,
            ProcessStartTime = process.ProcessStartTime.ToUniversalTime()
        };

    private void WriteDiagnostic(DiagnosticEvent diagnosticEvent)
    {
        try
        {
            _diagnosticLog?.Write(diagnosticEvent);
        }
        catch
        {
            // Diagnostics are side-effect-only and must never influence parser behavior.
        }
    }

    private void PublishClassification(IReadOnlyList<ParserEvent> events)
    {
        ParserClassificationSnapshot published;
        lock (_stateLock)
        {
            foreach (var parserEvent in events)
            {
                _classificationRuleCounts.TryGetValue(parserEvent.ClassificationRuleId, out var count);
                _classificationRuleCounts[parserEvent.ClassificationRuleId] = count + 1;
            }

            _classificationCurrent = _classificationCurrent.Apply(events, _options.TimeProvider.GetUtcNow());
            published = _classificationCurrent;
        }

        if (!IsRunning)
        {
            return;
        }

        var args = new ParserClassificationChangedEventArgs(published);
        var handlers = ClassificationChanged?.GetInvocationList() ?? [];
        foreach (EventHandler<ParserClassificationChangedEventArgs> handler in handlers)
        {
            try
            {
                handler(this, args);
            }
            catch (Exception exception)
            {
                lock (_stateLock)
                {
                    RecordDecisionLocked($"Classification-state subscriber failed ({exception.GetType().Name}).");
                }
            }
        }
    }

    private ParserClassificationDiagnostics BuildClassificationDiagnosticsLocked() =>
        new()
        {
            SnapshotRevision = _classificationCurrent.Revision,
            TotalClassifiedLines = _classificationCurrent.TotalClassifiedLines,
            RecognizedLineCount = _classificationCurrent.RecognizedLineCount,
            UnknownLineCount = _classificationCurrent.UnknownLineCount,
            MalformedLineCount = _classificationCurrent.MalformedLineCount,
            PotentialIdentityEvidenceCount = _classificationCurrent.PotentialIdentityEvidenceCount,
            ClassifierFailureCount = _classificationCurrent.ClassifierFailureCount,
            LastClassifiedEventAt = _classificationCurrent.LastClassifiedEventAt,
            RuleMatchCounts = new Dictionary<string, long>(_classificationRuleCounts, StringComparer.Ordinal),
            RecentClassifierFailures = [.. _recentClassifierFailures]
        };

    private static ParserWorkerDiagnostics ToDiagnostics(ParserWorkerSnapshot worker) =>
        new()
        {
            WorkerId = worker.WorkerId.ToString(),
            ContextId = worker.ContextId.ToString(),
            SourceOpaqueId = worker.CurrentSourceId?.Value,
            SourceFileName = worker.CurrentSourceId?.FileName,
            State = worker.State,
            AppliedBindingGeneration = worker.AppliedSourceBindingGeneration,
            TransitionKind = worker.LastAppliedTransitionKind,
            SegmentId = worker.CurrentSegment?.SourceSegmentId.ToString(),
            StartingOffset = worker.CurrentSegment?.StartingOffset,
            CurrentOffset = worker.Checkpoint?.ByteOffset,
            BytesRead = worker.TotalBytesRead,
            LinesEmitted = worker.TotalLinesEmitted,
            PartialBufferByteCount = worker.Checkpoint?.PartialBufferByteCount ?? 0,
            DecoderState = worker.Checkpoint?.HasPendingBomProbe == true
                ? "AwaitingBOMProbe"
                : worker.Checkpoint?.PartialBufferByteCount > 0
                    ? "BufferedIncompleteLine"
                    : "Ready",
            FaultCode = worker.FaultCode
        };

    private static async Task AwaitCancellationAsync(Task? task)
    {
        if (task is null)
        {
            return;
        }

        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private sealed record SnapshotWorkItem(
        MonitoringSessionManagerSnapshot Snapshot,
        TaskCompletionSource? Completion);

    private readonly record struct ParserEventCorrelationKey(
        MonitoringContextId ContextId,
        ParserSourceSegmentId SourceSegmentId,
        long Sequence)
    {
        public static ParserEventCorrelationKey From(ParserRawEvent parserEvent) =>
            new(parserEvent.ContextId, parserEvent.SourceSegmentId, parserEvent.Sequence);

        public static ParserEventCorrelationKey From(ParserEvent parserEvent) =>
            new(parserEvent.ContextId, parserEvent.SourceSegmentId, parserEvent.Sequence);
    }

    private readonly record struct ParserEventOrigin(
        ParserWorkerId WorkerId,
        string? AccountStableId);

    private sealed class DiagnosticParserWorkerFactory(
        ParserManagerOptions options,
        IDiagnosticLog? diagnosticLog) : IParserWorkerFactory
    {
        public IParserWorker Create(MonitoringContextId contextId) =>
            new ParserWorker(contextId, options, diagnosticLog);
    }
}
