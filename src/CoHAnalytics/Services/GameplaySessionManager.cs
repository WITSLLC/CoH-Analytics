using System.IO;
using System.Threading.Channels;
using CoHAnalytics.Models;
using CoHAnalytics.Services.Diagnostics;

namespace CoHAnalytics.Services;

/// <summary>
/// Per-context gameplay sessions, identity workflow, bounded pre-identity retention, and ordered
/// committed session events (Revision 9 §3.6.22).
/// </summary>
public sealed class GameplaySessionManager : IGameplaySessionManager, IDisposable
{
    [ThreadStatic]
    private static int t_processorCallbackDepth;

    private readonly IMonitoringSessionManager _monitoringSessionManager;
    private readonly IParserManager _parserManager;
    private readonly ICharacterRepository _characterRepository;
    private readonly IGameplayTelemetryParser _gameplayTelemetryParser;
    private readonly ICombatEventParser _combatEventParser;
    private readonly IGameplayReceivedItemClassifier _receivedItemClassifier;
    private readonly IBadgeAcquisitionResolver? _badgeAcquisitionResolver;
    private readonly ICharacterBadgeAcquisitionRepository _badgeAcquisitionRepository;
    private readonly ICharacterPerformanceObservationRepository? _historicalObservationRepository;
    private readonly GameplaySessionOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly object _ingressLock = new();
    private readonly object _lifecycleLock = new();
    private readonly object _stateLock = new();
    private readonly Dictionary<MonitoringContextId, MutableContextState> _contexts = [];
    private readonly List<string> _recentOperations = [];
    private readonly List<GameplaySessionEvent> _pendingCommittedEvents = [];
    private readonly List<CharacterPerformanceObservation> _pendingHistoricalObservations = [];
    private readonly List<WorkItem> _preStartBuffer = [];
    private readonly HashSet<MonitoringContextId> _failedContextIds = [];
    private readonly EventHandler<MonitoringSessionManagerChangedEventArgs> _firstStartMonitoringHandler;
    private readonly EventHandler<ParserEventsClassifiedEventArgs> _firstStartParserHandler;
    private readonly EventHandler<MonitoringSessionManagerChangedEventArgs> _liveMonitoringHandler;
    private readonly EventHandler<ParserEventsClassifiedEventArgs> _liveParserHandler;

    private Channel<WorkItem>? _workChannel;
    private CancellationTokenSource? _epochCts;
    private Task? _processingTask;
    private TaskCompletionSource? _startCompletion;
    private TaskCompletionSource? _stopCompletion;
    private MonitoringSessionManagerSnapshot _monitoringSnapshot = MonitoringSessionManagerSnapshot.Empty;
    private GameplaySessionManagerSnapshot _snapshot = GameplaySessionManagerSnapshot.Empty;
    private long _revision;
    private long _lastMonitoringSnapshotRevision;
    private long _totalCommittedEvents;
    private DateTimeOffset? _lastCommittedEventAt;
    private bool _acceptingWork;
    private bool _isRunning;
    private bool _startInProgress;
    private bool _stopInitiated;
    private bool _epochFinalized;
    private bool _callbacksEnabled;
    private bool _preStartBufferOverflowed;
    private bool _overloadLatched;
    private bool _liveSubscribed;
    private bool _firstStartHandlersSubscribed;
    private bool _firstStartCaptureClosed;
    private bool _firstStartCaptureConsumed;
    private bool _processingFailureLatched;
    private bool _disposed;
    private long _lifecycleEpoch;
    private bool _nonCombatSnapshotDirty;
    private bool _combatSnapshotDirty;
    private bool _trackedLifecycleSnapshotDirty;
    private DateTimeOffset _lastCombatSnapshotPublishedAt = DateTimeOffset.MinValue;
    private long _snapshotPublicationCount;
    private MonitoringContextId? _activeTrackedCombatContextId;
    private readonly QueuePressureTracker _workQueueTracker;
    private readonly ParserClassifier _parserClassifier = new();
    private readonly IDiagnosticLog? _diagnosticLog;
    private int _peakPendingCommittedEventCount;
    private long _pendingCommittedEventsDiscardedCount;
    private long _acceptedWorkSequence;
    private long _completedWorkSequence;
    private int _activeProcessorCallbackCount;

    public GameplaySessionManager(
        IMonitoringSessionManager monitoringSessionManager,
        IParserManager parserManager,
        ICharacterRepository characterRepository,
        GameplaySessionOptions? options = null,
        IGameplayTelemetryParser? gameplayTelemetryParser = null,
        ICombatEventParser? combatEventParser = null,
        IGameplayReceivedItemClassifier? receivedItemClassifier = null,
        IBadgeAcquisitionResolver? badgeAcquisitionResolver = null,
        ICharacterBadgeAcquisitionRepository? badgeAcquisitionRepository = null,
        ICharacterPerformanceObservationRepository? historicalObservationRepository = null,
        IDiagnosticLog? diagnosticLog = null)
    {
        _monitoringSessionManager = monitoringSessionManager;
        _parserManager = parserManager;
        _characterRepository = characterRepository;
        _gameplayTelemetryParser = gameplayTelemetryParser ?? new GameplayTelemetryParser();
        _combatEventParser = combatEventParser ?? new CombatEventParser();
        _receivedItemClassifier = receivedItemClassifier
            ?? new GameplayReceivedItemClassifier(NullReceivedItemTaxonomyCatalog.Instance);
        _badgeAcquisitionResolver = badgeAcquisitionResolver;
        _badgeAcquisitionRepository = badgeAcquisitionRepository
            ?? NullCharacterBadgeAcquisitionRepository.Instance;
        _historicalObservationRepository = historicalObservationRepository;
        _diagnosticLog = diagnosticLog;
        _options = options ?? new GameplaySessionOptions();
        _timeProvider = _options.TimeProvider;
        _firstStartMonitoringHandler = (_, e) => BufferFirstStartWork(WorkItem.MonitoringSnapshot(e.Snapshot));
        _firstStartParserHandler = (_, e) => BufferFirstStartWork(WorkItem.ClassifiedEvents(e.Events));
        _liveMonitoringHandler = (_, e) => TryAdmitWork(WorkItem.MonitoringSnapshot(e.Snapshot));
        _liveParserHandler = (_, e) => TryAdmitWork(WorkItem.ClassifiedEvents(e.Events));
        _workQueueTracker = new QueuePressureTracker(_options.WorkQueueCapacity);
        _monitoringSessionManager.StateChanged += _firstStartMonitoringHandler;
        _parserManager.ClassifiedEventsAvailable += _firstStartParserHandler;
        _firstStartHandlersSubscribed = true;
    }

    public event EventHandler<GameplaySessionManagerChangedEventArgs>? StateChanged;

    public event EventHandler<GameplaySessionEventsAvailableEventArgs>? CommittedEventsAvailable;

    internal long SnapshotPublicationCount => _snapshotPublicationCount;

    public GameplaySessionManagerSnapshot Current
    {
        get
        {
            lock (_stateLock)
            {
                return _snapshot;
            }
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        Task? joinStart = null;
        Task? priorStop = null;
        lock (_lifecycleLock)
        {
            if (_isRunning)
            {
                return;
            }

            if (_startInProgress)
            {
                joinStart = _startCompletion?.Task;
            }
            else
            {
                _startInProgress = true;
                _startCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                priorStop = _stopCompletion?.Task;
            }
        }

        if (joinStart is not null)
        {
            await joinStart.WaitAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        try
        {
            if (priorStop is not null)
            {
                await priorStop.WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            List<WorkItem> prefix;
            MonitoringSessionManagerSnapshot initialBaseline;
            Channel<WorkItem> epochChannel;
            CancellationTokenSource epochCts;
            TaskCompletionSource epochStopCompletion;
            long lifecycleEpoch;
            lock (_lifecycleLock)
            {
                lock (_ingressLock)
                {
                    _firstStartCaptureClosed = true;
                    UnsubscribeFirstStartHandlers();

                    if (_preStartBufferOverflowed)
                    {
                        throw new InvalidOperationException("Gameplay session pre-start buffer overflowed.");
                    }

                    epochChannel = Channel.CreateBounded<WorkItem>(new BoundedChannelOptions(_options.WorkQueueCapacity)
                    {
                        FullMode = BoundedChannelFullMode.Wait,
                        SingleReader = true,
                        SingleWriter = false
                    });
                    epochCts = new CancellationTokenSource();
                    _workChannel = epochChannel;
                    _epochCts = epochCts;
                    _lifecycleEpoch++;
                    lifecycleEpoch = _lifecycleEpoch;
                    _workQueueTracker.Reset(lifecycleEpoch);
                    _peakPendingCommittedEventCount = 0;
                    _pendingCommittedEventsDiscardedCount = 0;
                    _overloadLatched = false;
                    _processingFailureLatched = false;
                    _acceptedWorkSequence = 0;
                    _completedWorkSequence = 0;
                    _activeProcessorCallbackCount = 0;
                    _stopInitiated = false;
                    _epochFinalized = false;
                    _callbacksEnabled = true;
                    _acceptingWork = false;
                    prefix = _firstStartCaptureConsumed ? [] : DrainPreStartBufferLocked();
                    _firstStartCaptureConsumed = true;
                    _preStartBuffer.Clear();
                    initialBaseline = _monitoringSessionManager.Current;
                }

                epochStopCompletion = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                _stopCompletion = epochStopCompletion;
                _processingTask = ProcessWorkQueueAsync(
                    epochChannel,
                    epochCts,
                    epochStopCompletion,
                    lifecycleEpoch);
            }

            var catchUpCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            await WriteStartupItemAsync(
                    epochChannel,
                    WorkItem.MonitoringSnapshot(initialBaseline),
                    epochCts.Token)
                .ConfigureAwait(false);

            foreach (var item in prefix)
            {
                await WriteStartupItemAsync(epochChannel, item, epochCts.Token).ConfigureAwait(false);
            }

            var catchUpBaseline = _monitoringSessionManager.Current;
            await WriteStartupItemAsync(
                    epochChannel,
                    WorkItem.MonitoringSnapshot(catchUpBaseline, catchUpCompletion),
                    epochCts.Token)
                .ConfigureAwait(false);

            await catchUpCompletion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);

            lock (_ingressLock)
            {
                SubscribeLiveHandlers();
                _acceptingWork = true;
                _isRunning = true;
                RecordOperationLocked("Started.");
            }
        }
        finally
        {
            lock (_lifecycleLock)
            {
                _startInProgress = false;
                _startCompletion?.TrySetResult();
                _startCompletion = null;
            }
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return;
        }

        Task stopTask;
        lock (_lifecycleLock)
        {
            if (!_isRunning && (_stopCompletion is null || _stopCompletion.Task.IsCompleted))
            {
                return;
            }

            stopTask = _stopCompletion!.Task;
            if (!_stopInitiated)
            {
                _stopInitiated = true;
                BeginIngressShutdown();
            }
        }

        try
        {
            await stopTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    public GameplaySessionOperationResult ConfirmCharacter(
        MonitoringContextId contextId,
        CharacterRecordId characterRecordId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (IsOnProcessorCallbackStack())
        {
            return GameplaySessionOperationResult.Failure(GameplaySessionOutcome.ReentrantCommandRejected);
        }

        if (!_isRunning)
        {
            return GameplaySessionOperationResult.Failure(GameplaySessionOutcome.ServiceStopped);
        }

        var completion = new TaskCompletionSource<GameplaySessionOperationResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        if (TryAdmitWork(WorkItem.ConfirmCharacter(contextId, characterRecordId, completion)))
        {
            _options.TestHooks?.AfterCommandAdmission?.Invoke();
        }

        return completion.Task.GetAwaiter().GetResult();
    }

    public void ResetForNewRuntimeGeneration()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_isRunning)
        {
            return;
        }

        TryAdmitWork(WorkItem.ResetRuntimeGeneration());
    }

    public GameplaySessionOperationResult ClearIdentity(MonitoringContextId contextId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (IsOnProcessorCallbackStack())
        {
            return GameplaySessionOperationResult.Failure(GameplaySessionOutcome.ReentrantCommandRejected);
        }

        if (!_isRunning)
        {
            return GameplaySessionOperationResult.Failure(GameplaySessionOutcome.ServiceStopped);
        }

        var completion = new TaskCompletionSource<GameplaySessionOperationResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        if (TryAdmitWork(WorkItem.ClearIdentity(contextId, completion)))
        {
            _options.TestHooks?.AfterCommandAdmission?.Invoke();
        }

        return completion.Task.GetAwaiter().GetResult();
    }

    public GameplaySessionOperationResult CaptureHistoricalPerformanceBoundary(
        MonitoringContextId contextId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (IsOnProcessorCallbackStack())
        {
            return GameplaySessionOperationResult.Failure(GameplaySessionOutcome.ReentrantCommandRejected);
        }

        if (!_isRunning)
        {
            return GameplaySessionOperationResult.Failure(GameplaySessionOutcome.ServiceStopped);
        }

        var completion = new TaskCompletionSource<GameplaySessionOperationResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        if (TryAdmitWork(WorkItem.HistoricalPerformanceBoundary(contextId, completion)))
        {
            _options.TestHooks?.AfterCommandAdmission?.Invoke();
        }

        return completion.Task.GetAwaiter().GetResult();
    }

    public GameplaySessionOperationResult StartTrackedCombat(MonitoringContextId contextId) =>
        EnqueueTrackedCombatLifecycle(TrackedCombatLifecycleAction.Start, contextId);

    public GameplaySessionOperationResult PauseTrackedCombat(MonitoringContextId contextId) =>
        EnqueueTrackedCombatLifecycle(TrackedCombatLifecycleAction.Pause, contextId);

    public GameplaySessionOperationResult ResumeTrackedCombat(MonitoringContextId contextId) =>
        EnqueueTrackedCombatLifecycle(TrackedCombatLifecycleAction.Resume, contextId);

    public GameplaySessionOperationResult StopTrackedCombat(MonitoringContextId contextId) =>
        EnqueueTrackedCombatLifecycle(TrackedCombatLifecycleAction.Stop, contextId);

    public GameplaySessionOperationResult ResetTrackedCombat(MonitoringContextId contextId) =>
        EnqueueTrackedCombatLifecycle(TrackedCombatLifecycleAction.Reset, contextId);

    private GameplaySessionOperationResult EnqueueTrackedCombatLifecycle(
        TrackedCombatLifecycleAction action,
        MonitoringContextId contextId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (IsOnProcessorCallbackStack())
        {
            return GameplaySessionOperationResult.Failure(GameplaySessionOutcome.ReentrantCommandRejected);
        }

        if (!_isRunning)
        {
            return GameplaySessionOperationResult.Failure(GameplaySessionOutcome.ServiceStopped);
        }

        var completion = new TaskCompletionSource<GameplaySessionOperationResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        if (TryAdmitWork(WorkItem.TrackedCombatLifecycle(action, contextId, completion)))
        {
            _options.TestHooks?.AfterCommandAdmission?.Invoke();
        }

        return completion.Task.GetAwaiter().GetResult();
    }

    public GameplaySessionDiagnostics GetDiagnostics()
    {
        lock (_stateLock)
        {
            var sessions = _contexts.Values
                .Select(context => context.ActiveSession)
                .Where(session => session is not null)
                .Select(session => session!)
                .ToArray();

            return new GameplaySessionDiagnostics
            {
                IsRunning = _isRunning,
                SnapshotRevision = _revision,
                LifecycleEpoch = _lifecycleEpoch,
                ActiveSessionCount = sessions.Count(session => session.LifecycleState == GameplaySessionLifecycleState.Active),
                SuspendedSessionCount = sessions.Count(session => session.LifecycleState == GameplaySessionLifecycleState.Suspended),
                NeedsAttentionSessionCount = sessions.Count(session => session.NeedsAttention),
                OverflowedSessionCount = sessions.Count(session => session.RetentionOverflowed),
                TotalCommittedEvents = _totalCommittedEvents,
                LastCommittedEventAt = _lastCommittedEventAt,
                FailedContextCount = _failedContextIds.Count,
                PendingCommittedEventCount = _pendingCommittedEvents.Count,
                WorkQueue = _workQueueTracker.Snapshot(),
                PendingCommittedEvents = new PendingCommittedEventDiagnostics
                {
                    Capacity = _options.MaxPendingCommittedEvents,
                    CurrentCount = _pendingCommittedEvents.Count,
                    PeakCount = _peakPendingCommittedEventCount,
                    DiscardedCount = _pendingCommittedEventsDiscardedCount
                },
                WorkQueueOverflowed = _workQueueTracker.Snapshot().Overflowed,
                PreStartBufferOverflowed = _preStartBufferOverflowed,
                LastAcceptedWorkSequence = _acceptedWorkSequence,
                LastCompletedWorkSequence = _completedWorkSequence,
                ActiveProcessorCallbackCount = _activeProcessorCallbackCount,
                RecentOperations = _recentOperations.ToArray()
            };
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        lock (_ingressLock)
        {
            _firstStartCaptureClosed = true;
            _firstStartCaptureConsumed = true;
            _preStartBuffer.Clear();
            UnsubscribeFirstStartHandlers();
        }
        UnsubscribeLiveHandlers();

        try
        {
            StopAsync().ConfigureAwait(false).GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            lock (_stateLock)
            {
                RecordOperationLocked($"Dispose shutdown failed ({exception.GetType().Name}).");
            }
        }

        _disposed = true;
    }

    private void BufferFirstStartWork(WorkItem item)
    {
        _options.TestHooks?.BeforeFirstStartCaptureLock?.Invoke();

        lock (_ingressLock)
        {
            if (_disposed || _firstStartCaptureClosed || _firstStartCaptureConsumed)
            {
                return;
            }

            if (!TryBufferPreStartLocked(item))
            {
                return;
            }
        }
    }

    private bool TryAdmitWork(WorkItem item)
    {
        lock (_ingressLock)
        {
            if (!_acceptingWork || _stopInitiated || _overloadLatched)
            {
                _workQueueTracker.RecordRejected();
                RejectWorkItem(item, _overloadLatched
                    ? GameplaySessionOutcome.Overloaded
                    : GameplaySessionOutcome.ServiceStopped);
                return false;
            }

            var channel = _workChannel;
            if (channel is null
                || !QueuePressureAdmission.TryPublish(channel.Writer, item, _workQueueTracker))
            {
                LatchOverloadAndReject(item);
                return false;
            }

            lock (_stateLock)
            {
                _acceptedWorkSequence++;
            }

            return true;
        }
    }

    private async Task WriteStartupItemAsync(
        Channel<WorkItem> channel,
        WorkItem item,
        CancellationToken cancellationToken)
    {
        try
        {
            await QueuePressureAdmission.PublishAsync(channel.Writer, item, _workQueueTracker, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (ChannelClosedException exception)
        {
            throw new InvalidOperationException("Gameplay session startup queue closed unexpectedly.", exception);
        }

        lock (_stateLock)
        {
            _acceptedWorkSequence++;
        }
    }

    private void LatchOverloadAndReject(WorkItem rejectedItem)
    {
        _workQueueTracker.RecordRejected();
        if (!_overloadLatched)
        {
            _overloadLatched = true;
            _workQueueTracker.LatchOverflow();
            _acceptingWork = false;
            RecordOperationLocked("Work queue overflow while enqueueing.");
            DetachAllIngressSubscriptions();
            _workChannel?.Writer.TryComplete();
        }

        RejectWorkItem(rejectedItem, GameplaySessionOutcome.Overloaded);
    }

    private void RejectWorkItem(WorkItem item, GameplaySessionOutcome outcome)
    {
        if (item.Completion is not null)
        {
            CompleteCommand(item.Completion, GameplaySessionOperationResult.Failure(outcome));
        }
    }

    private void BeginIngressShutdown()
    {
        lock (_ingressLock)
        {
            _acceptingWork = false;
            DetachAllIngressSubscriptions();
            _workChannel?.Writer.TryComplete();
        }
    }

    private void SubscribeLiveHandlers()
    {
        if (_liveSubscribed)
        {
            return;
        }

        _monitoringSessionManager.StateChanged += _liveMonitoringHandler;
        _parserManager.ClassifiedEventsAvailable += _liveParserHandler;
        _liveSubscribed = true;
    }

    private void UnsubscribeLiveHandlers()
    {
        if (!_liveSubscribed)
        {
            return;
        }

        _monitoringSessionManager.StateChanged -= _liveMonitoringHandler;
        _parserManager.ClassifiedEventsAvailable -= _liveParserHandler;
        _liveSubscribed = false;
    }

    private void UnsubscribeFirstStartHandlers()
    {
        if (!_firstStartHandlersSubscribed)
        {
            return;
        }

        _monitoringSessionManager.StateChanged -= _firstStartMonitoringHandler;
        _parserManager.ClassifiedEventsAvailable -= _firstStartParserHandler;
        _firstStartHandlersSubscribed = false;
    }

    private void DetachAllIngressSubscriptions()
    {
        UnsubscribeLiveHandlers();
        UnsubscribeFirstStartHandlers();
    }

    private static bool IsOnProcessorCallbackStack() => t_processorCallbackDepth > 0;

    private static void EnterProcessorCallback() => t_processorCallbackDepth++;

    private static void ExitProcessorCallback()
    {
        if (t_processorCallbackDepth > 0)
        {
            t_processorCallbackDepth--;
        }
    }

    private bool TryBufferPreStartLocked(WorkItem item)
    {
        if (_preStartBuffer.Count >= _options.MaxPreStartBufferCapacity)
        {
            _preStartBufferOverflowed = true;
            RecordOperationLocked("Pre-start buffer overflow while buffering work.");
            return false;
        }

        _preStartBuffer.Add(item);
        return true;
    }

    private List<WorkItem> DrainPreStartBufferLocked()
    {
        var buffered = _preStartBuffer.ToList();
        _preStartBuffer.Clear();
        return buffered;
    }

    private static void CompleteCommand(
        TaskCompletionSource<GameplaySessionOperationResult> completion,
        GameplaySessionOperationResult result)
    {
        completion.TrySetResult(result);
    }

    private async Task ProcessWorkQueueAsync(
        Channel<WorkItem> channel,
        CancellationTokenSource epochCts,
        TaskCompletionSource epochStopCompletion,
        long lifecycleEpoch)
    {
        var cancellationToken = epochCts.Token;

        try
        {
            await foreach (var item in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                _workQueueTracker.RecordDequeued();
                Interlocked.Increment(ref _activeProcessorCallbackCount);
                EnterProcessorCallback();
                try
                {
                    _options.TestHooks?.BeforeProcessWorkItem?.Invoke();
                    ProcessWorkItem(item);
                }
                catch (Exception exception)
                {
                    HandleProcessorFailure(channel, item, exception);
                    break;
                }
                finally
                {
                    ExitProcessorCallback();
                    Interlocked.Decrement(ref _activeProcessorCallbackCount);
                    _workQueueTracker.RecordCompleted();
                    lock (_stateLock)
                    {
                        _completedWorkSequence++;
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            HandleProcessorFailure(channel, null, exception);
        }
        finally
        {
            CompleteEpochShutdown(
                _processingFailureLatched
                    ? "Gameplay session processing failed."
                    : _overloadLatched
                        ? "Work queue overflow."
                        : "Application shutdown.",
                channel,
                epochCts,
                epochStopCompletion,
                lifecycleEpoch);
        }
    }

    private void HandleProcessorFailure(
        Channel<WorkItem> channel,
        WorkItem? failingItem,
        Exception exception)
    {
        CompleteWorkItemAfterProcessorFailure(failingItem, exception);

        lock (_stateLock)
        {
            RecordOperationLocked($"Work item failed ({exception.GetType().Name}).");
        }

        lock (_ingressLock)
        {
            _processingFailureLatched = true;
            _overloadLatched = true;
            _workQueueTracker.LatchOverflow();
            _acceptingWork = false;
            DetachAllIngressSubscriptions();
            channel.Writer.TryComplete();
        }

        var abandonedNonCommandWork = 0;
        while (channel.Reader.TryRead(out var abandoned))
        {
            _workQueueTracker.RecordDequeued();
            if (abandoned.Completion is not null || abandoned.SnapshotCompletion is not null)
            {
                CompleteWorkItemAfterProcessorFailure(abandoned, exception);
                _workQueueTracker.RecordAbandoned();
            }
            else
            {
                _workQueueTracker.RecordAbandoned();
                abandonedNonCommandWork++;
            }
        }

        lock (_stateLock)
        {
            RecordOperationLocked("Gameplay session processing failed.");
            if (abandonedNonCommandWork > 0)
            {
                RecordOperationLocked(
                    $"Abandoned {abandonedNonCommandWork} queued non-command work item(s) after processing failure.");
            }
        }
    }

    private static void CompleteWorkItemAfterProcessorFailure(WorkItem? item, Exception exception)
    {
        if (item is null)
        {
            return;
        }

        if (item.Completion is not null)
        {
            CompleteCommand(
                item.Completion,
                GameplaySessionOperationResult.Failure(
                    GameplaySessionOutcome.ProcessingFailed,
                    exception.GetType().Name));
        }

        item.SnapshotCompletion?.TrySetException(exception);
    }

    private void CompleteEpochShutdown(
        string reason,
        Channel<WorkItem> epochChannel,
        CancellationTokenSource epochCts,
        TaskCompletionSource epochStopCompletion,
        long lifecycleEpoch)
    {
        if (_epochFinalized)
        {
            return;
        }

        _epochFinalized = true;

        lock (_stateLock)
        {
            if (_overloadLatched)
            {
                MarkOverloadContextsLocked();
            }

            FinalizeAllSessionsLocked(reason);
            _isRunning = false;
            RecordOperationLocked(_overloadLatched ? "Overloaded." : "Stopped.");
        }

        FlushCommittedEvents();
        PublishSnapshotIfChanged(forceCombatPublish: true);
        _callbacksEnabled = false;

        _options.TestHooks?.BeforeEpochResourceRelease?.Invoke();
        _workQueueTracker.AbandonUnfinishedAtShutdown();
        epochCts.Cancel();
        epochCts.Dispose();
        lock (_ingressLock)
        {
            if (ReferenceEquals(_epochCts, epochCts))
            {
                _epochCts = null;
            }

            if (ReferenceEquals(_workChannel, epochChannel))
            {
                _workChannel = null;
            }
        }

        lock (_lifecycleLock)
        {
            if (_lifecycleEpoch == lifecycleEpoch)
            {
                _processingTask = null;
            }
        }

        epochStopCompletion.TrySetResult();
        _options.TestHooks?.AfterEpochFinalization?.Invoke();
    }

    private void MarkOverloadContextsLocked()
    {
        foreach (var context in _contexts.Values)
        {
            if (context.ActiveSession is not null)
            {
                context.ActiveSession.NeedsAttention = true;
            }

            _failedContextIds.Add(context.ContextId);
        }
    }

    private void ProcessWorkItem(WorkItem item)
    {
        switch (item.Kind)
        {
            case WorkItemKind.MonitoringSnapshot:
                ProcessMonitoringSnapshot(item);
                break;
            case WorkItemKind.ClassifiedEvents:
                ProcessClassifiedEvents(item.Events!);
                break;
            case WorkItemKind.ConfirmCharacter:
                ProcessConfirmCharacter(item);
                break;
            case WorkItemKind.ClearIdentity:
                ProcessClearIdentity(item);
                break;
            case WorkItemKind.HistoricalPerformanceBoundary:
                ProcessHistoricalPerformanceBoundary(item);
                break;
            case WorkItemKind.TrackedCombatLifecycle:
                ProcessTrackedCombatLifecycle(item);
                break;
            case WorkItemKind.ResetRuntimeGeneration:
                ProcessResetRuntimeGeneration();
                break;
        }
    }

    private void ProcessResetRuntimeGeneration()
    {
        lock (_stateLock)
        {
            foreach (var context in _contexts.Values.ToArray())
            {
                if (context.ActiveSession is not null)
                {
                    FinalizeSessionLocked(
                        context,
                        context.ActiveSession,
                        "Runtime generation reset.");
                }
            }

            _contexts.Clear();
            _failedContextIds.Clear();
            _activeTrackedCombatContextId = null;
            RecordOperationLocked("Live runtime generation reset.");
        }

        // Reconcile against the current monitoring snapshot so Ready contexts regain a
        // provisional session after generation reset. Without this, a monitoring snapshot
        // work item queued before reset can establish a session that reset then clears.
        ProcessMonitoringSnapshot(_monitoringSessionManager.Current);

        MarkCombatSnapshotDirty();
        MarkNonCombatSnapshotDirty();
        MarkTrackedLifecycleSnapshotDirty();
        PublishSnapshotIfChanged(forceCombatPublish: true);
    }

    private void ProcessTrackedCombatLifecycle(WorkItem item)
    {
        try
        {
            var result = ExecuteTrackedCombatLifecycleLocked(item.TrackedCombatAction!.Value, item.ContextId!);
            CompleteCommand(item.Completion!, result);
        }
        catch (Exception exception)
        {
            CompleteCommand(
                item.Completion!,
                GameplaySessionOperationResult.Failure(
                    GameplaySessionOutcome.InvalidContext,
                    exception.GetType().Name));
        }

        PublishSnapshotIfChanged(forceCombatPublish: true);
    }

    private GameplaySessionOperationResult ExecuteTrackedCombatLifecycleLocked(
        TrackedCombatLifecycleAction action,
        MonitoringContextId contextId)
    {
        lock (_stateLock)
        {
            var now = _timeProvider.GetUtcNow();
            var session = GetActiveSessionForContextLocked(contextId);
            if (session is null)
            {
                return GameplaySessionOperationResult.Failure(GameplaySessionOutcome.NoActiveSession);
            }

            switch (action)
            {
                case TrackedCombatLifecycleAction.Start:
                    if (_activeTrackedCombatContextId is MonitoringContextId other && other != contextId)
                    {
                        StopTrackedCombatOnContextLocked(other, now);
                    }

                    session.CombatAggregator.Tracked.Start(now);
                    session.TrackedEarnings.Start(now);
                    _activeTrackedCombatContextId = contextId;
                    break;
                case TrackedCombatLifecycleAction.Pause:
                    if (_activeTrackedCombatContextId != contextId)
                    {
                        return GameplaySessionOperationResult.Failure(GameplaySessionOutcome.InvalidContext);
                    }

                    session.CombatAggregator.Tracked.Pause(now);
                    session.TrackedEarnings.Pause(now);
                    break;
                case TrackedCombatLifecycleAction.Resume:
                    if (_activeTrackedCombatContextId != contextId)
                    {
                        return GameplaySessionOperationResult.Failure(GameplaySessionOutcome.InvalidContext);
                    }

                    session.CombatAggregator.Tracked.Resume(now);
                    session.TrackedEarnings.Resume(now);
                    break;
                case TrackedCombatLifecycleAction.Stop:
                    if (_activeTrackedCombatContextId != contextId)
                    {
                        return GameplaySessionOperationResult.Failure(GameplaySessionOutcome.InvalidContext);
                    }

                    StopTrackedCombatOnContextLocked(contextId, now);
                    break;
                case TrackedCombatLifecycleAction.Reset:
                    session.CombatAggregator.Tracked.Reset();
                    session.TrackedEarnings.Reset();
                    if (_activeTrackedCombatContextId == contextId)
                    {
                        _activeTrackedCombatContextId = null;
                    }

                    break;
            }

            MarkTrackedLifecycleSnapshotDirty();
            return GameplaySessionOperationResult.Success();
        }
    }

    private void StopTrackedCombatOnContextLocked(MonitoringContextId contextId, DateTimeOffset stoppedAt)
    {
        var session = GetActiveSessionForContextLocked(contextId);
        if (session is null)
        {
            return;
        }

        session.CombatAggregator.Tracked.Stop(stoppedAt);
        session.TrackedEarnings.Stop(stoppedAt);
        if (_activeTrackedCombatContextId == contextId)
        {
            _activeTrackedCombatContextId = null;
        }
    }

    private MutableSession? GetActiveSessionForContextLocked(MonitoringContextId contextId) =>
        _contexts.GetValueOrDefault(contextId)?.ActiveSession;

    private void ProcessConfirmCharacter(WorkItem item)
    {
        try
        {
            var result = ExecuteConfirmCharacter(item.ContextId!, item.CharacterRecordId!);
            CompleteCommand(item.Completion!, result);
        }
        catch (Exception exception)
        {
            CompleteCommand(
                item.Completion!,
                GameplaySessionOperationResult.Failure(
                    GameplaySessionOutcome.InvalidContext,
                    exception.GetType().Name));
        }
    }

    private void ProcessClearIdentity(WorkItem item)
    {
        try
        {
            var result = ExecuteClearIdentity(item.ContextId!);
            CompleteCommand(item.Completion!, result);
        }
        catch (Exception exception)
        {
            CompleteCommand(
                item.Completion!,
                GameplaySessionOperationResult.Failure(
                    GameplaySessionOutcome.InvalidContext,
                    exception.GetType().Name));
        }
    }

    private void ProcessHistoricalPerformanceBoundary(WorkItem item)
    {
        try
        {
            var result = ExecuteHistoricalPerformanceBoundary(item.ContextId!);
            CompleteCommand(item.Completion!, result);
        }
        catch (Exception exception)
        {
            CompleteCommand(
                item.Completion!,
                GameplaySessionOperationResult.Failure(
                    GameplaySessionOutcome.ProcessingFailed,
                    exception.GetType().Name));
        }
    }

    private void ProcessMonitoringSnapshot(WorkItem item)
    {
        ProcessMonitoringSnapshot(item.Snapshot!);
        item.SnapshotCompletion?.TrySetResult();
    }

    private void ProcessMonitoringSnapshot(MonitoringSessionManagerSnapshot snapshot)
    {
        lock (_stateLock)
        {
            if (_lastMonitoringSnapshotRevision != 0
                && snapshot.Revision < _lastMonitoringSnapshotRevision)
            {
                RecordOperationLocked(
                    $"Ignored stale monitoring snapshot revision {snapshot.Revision} after {_lastMonitoringSnapshotRevision}.");
                return;
            }

            _lastMonitoringSnapshotRevision = Math.Max(_lastMonitoringSnapshotRevision, snapshot.Revision);
            _monitoringSnapshot = snapshot;
            foreach (var context in snapshot.Contexts)
            {
                if (context.State is MonitoringContextState.Stopped or MonitoringContextState.Error)
                {
                    if (_contexts.TryGetValue(context.ContextId, out var stoppedContext)
                        && stoppedContext.ActiveSession is not null)
                    {
                        FinalizeSessionLocked(
                            stoppedContext,
                            stoppedContext.ActiveSession,
                            $"Context {context.State}.");
                    }

                    continue;
                }

                var mutableContext = GetOrCreateContextLocked(context);
                mutableContext.AccountStableId = context.AccountStableId;
                TryEstablishReadyMonitoringSessionLocked(mutableContext, context);
                ApplyMonitoringContextLocked(mutableContext, context);
            }

            var liveIds = snapshot.Contexts.Select(context => context.ContextId).ToHashSet();
            foreach (var staleId in _contexts.Keys.Where(id => !liveIds.Contains(id)).ToArray())
            {
                var stale = _contexts[staleId];
                if (stale.ActiveSession is not null)
                {
                    FinalizeSessionLocked(stale, stale.ActiveSession, "Context removed.");
                }

                _contexts.Remove(staleId);
                _failedContextIds.Remove(staleId);
            }
        }

        MarkNonCombatSnapshotDirty();
        PublishSnapshotIfChanged();
    }

    private void ProcessClassifiedEvents(IReadOnlyList<ParserEvent> events)
    {
        foreach (var parserEvent in events)
        {
            try
            {
                lock (_stateLock)
                {
                    var contextSnapshot = FindMonitoringContextLocked(parserEvent.ContextId);
                    if (contextSnapshot is null)
                    {
                        WriteIgnoredWelcome(parserEvent, "MonitoringContextUnavailable");
                        continue;
                    }

                    var mutableContext = GetOrCreateContextLocked(contextSnapshot);
                    mutableContext.AccountStableId = contextSnapshot.AccountStableId;
                    ApplyMonitoringContextLocked(mutableContext, contextSnapshot);
                    if (!CharacterIdentityResolver.IsWelcomeEvidence(parserEvent))
                    {
                        TryProcessActivityTriggeredWelcomeRecoveryLocked(mutableContext, contextSnapshot);
                    }
                    ProcessParserEventLocked(mutableContext, contextSnapshot, parserEvent);
                    _failedContextIds.Remove(parserEvent.ContextId);
                }
            }
            catch (Exception exception)
            {
                lock (_stateLock)
                {
                    _failedContextIds.Add(parserEvent.ContextId);
                    RecordOperationLocked(
                        $"Parser event failed for context {parserEvent.ContextId} ({exception.GetType().Name}).");
                }
            }
        }

        FlushCommittedEvents();
        PublishSnapshotIfChanged();
    }

    private void MarkNonCombatSnapshotDirty() => _nonCombatSnapshotDirty = true;

    private void MarkCombatSnapshotDirty() => _combatSnapshotDirty = true;

    private void MarkTrackedLifecycleSnapshotDirty() => _trackedLifecycleSnapshotDirty = true;

    private GameplaySessionOperationResult ExecuteConfirmCharacter(
        MonitoringContextId contextId,
        CharacterRecordId characterRecordId)
    {
        lock (_stateLock)
        {
            var contextSnapshot = FindMonitoringContextLocked(contextId);
            if (contextSnapshot is null)
            {
                return GameplaySessionOperationResult.Failure(GameplaySessionOutcome.InvalidContext);
            }

            var mutableContext = _contexts.GetValueOrDefault(contextId);
            if (mutableContext?.ActiveSession is null)
            {
                return GameplaySessionOperationResult.Failure(GameplaySessionOutcome.NoActiveSession);
            }

            var record = _characterRepository.TryGetRecord(characterRecordId);
            if (record is null)
            {
                return GameplaySessionOperationResult.Failure(GameplaySessionOutcome.RecordNotFound);
            }

            if (contextSnapshot.AccountStableId is null
                || !string.Equals(record.AccountStableId, contextSnapshot.AccountStableId, StringComparison.Ordinal))
            {
                return GameplaySessionOperationResult.Failure(GameplaySessionOutcome.CharacterNotInAccount);
            }

            var session = mutableContext.ActiveSession;
            var observedDisplayName = ResolveObservedDisplayNameForConfirmationLocked(session, record);
            var update = _characterRepository.RecordTrustedObservedDisplayName(
                characterRecordId,
                observedDisplayName,
                CharacterTrustState.TrustedFromManualConfirmation);
            if (!update.IsSuccess)
            {
                return GameplaySessionOperationResult.Failure(
                    GameplaySessionOutcome.RecordNotFound,
                    update.Detail);
            }

            record = _characterRepository.TryGetRecord(characterRecordId)!;

            if (TryPerformCharacterHandoffFromConfirmationLocked(
                    mutableContext,
                    contextSnapshot,
                    session,
                    characterRecordId,
                    record.CurrentDisplayName))
            {
                RecordOperationLocked($"Manual character handoff for context {contextId}.");
            }
            else
            {
                AssignIdentityLocked(
                    mutableContext,
                    session,
                    characterRecordId,
                    record.CurrentDisplayName,
                    CharacterIdentityConfidence.Confirmed,
                    CharacterIdentityResolutionState.Resolved,
                    "Manual confirmation.");

                CommitRetainedEventsLocked(mutableContext, session);
                RecordOperationLocked($"Manual confirmation for context {contextId}.");
            }
        }

        FlushCommittedEvents();
        MarkNonCombatSnapshotDirty();
        PublishSnapshotIfChanged();
        return GameplaySessionOperationResult.Success();
    }

    private GameplaySessionOperationResult ExecuteClearIdentity(MonitoringContextId contextId)
    {
        lock (_stateLock)
        {
            var contextSnapshot = FindMonitoringContextLocked(contextId);
            if (contextSnapshot is null)
            {
                return GameplaySessionOperationResult.Failure(GameplaySessionOutcome.InvalidContext);
            }

            var mutableContext = _contexts.GetValueOrDefault(contextId);
            if (mutableContext?.ActiveSession is null)
            {
                return GameplaySessionOperationResult.Failure(GameplaySessionOutcome.NoActiveSession);
            }

            var session = mutableContext.ActiveSession;
            if (session.IdentityResolution == CharacterIdentityResolutionState.Resolved
                && session.CharacterRecordId is not null)
            {
                FinalizeHistoricalIntervalLocked(
                    session,
                    _timeProvider.GetUtcNow(),
                    advanceCursor: true,
                    advanceWithoutObservation: true);
            }

            session.CharacterRecordId = null;
            session.CharacterDisplayName = null;
            session.IdentityConfidence = CharacterIdentityConfidence.Unknown;
            session.IdentityResolution = CharacterIdentityResolutionState.Unresolved;
            session.Candidates.Clear();
            session.CandidateEvidence.Clear();
            session.NeedsAttention = session.RetentionOverflowed;

            RecordOperationLocked($"Identity cleared for context {contextId}.");
        }

        MarkNonCombatSnapshotDirty();
        PublishSnapshotIfChanged();
        return GameplaySessionOperationResult.Success();
    }

    private GameplaySessionOperationResult ExecuteHistoricalPerformanceBoundary(
        MonitoringContextId contextId)
    {
        lock (_stateLock)
        {
            var session = _contexts.GetValueOrDefault(contextId)?.ActiveSession;
            if (session is null)
            {
                return GameplaySessionOperationResult.Failure(GameplaySessionOutcome.NoActiveSession);
            }

            if (session.IdentityResolution != CharacterIdentityResolutionState.Resolved
                || session.CharacterRecordId is null
                || session.RetentionOverflowed)
            {
                return GameplaySessionOperationResult.Success();
            }

            FinalizeHistoricalIntervalLocked(
                session,
                _timeProvider.GetUtcNow(),
                advanceCursor: true);
            RecordOperationLocked(
                $"Historical performance boundary processed for context {contextId}.");
            return GameplaySessionOperationResult.Success();
        }
    }

    private void ApplyMonitoringContextLocked(
        MutableContextState mutableContext,
        MonitoringContextSnapshot contextSnapshot)
    {
        mutableContext.AccountStableId = contextSnapshot.AccountStableId;

        if (mutableContext.ActiveSession is null)
        {
            return;
        }

        var session = mutableContext.ActiveSession;
        if (contextSnapshot.State == MonitoringContextState.Stopped
            || contextSnapshot.State == MonitoringContextState.Error)
        {
            FinalizeSessionLocked(mutableContext, session, $"Context {contextSnapshot.State}.");
            return;
        }

        var shouldSuspend = contextSnapshot.State == MonitoringContextState.RuntimeSuspended
            || contextSnapshot.SourceLostAt is not null;

        if (shouldSuspend && session.LifecycleState == GameplaySessionLifecycleState.Active)
        {
            session.LifecycleState = GameplaySessionLifecycleState.Suspended;
            session.SuspendedAt = ResolveHistoricalSuspensionBoundaryLocked(
                contextSnapshot,
                session);
            RecordOperationLocked($"Session suspended for context {contextSnapshot.ContextId}.");
        }
    }

    private DateTimeOffset ResolveHistoricalSuspensionBoundaryLocked(
        MonitoringContextSnapshot contextSnapshot,
        MutableSession session)
    {
        var authoritative = contextSnapshot.State == MonitoringContextState.RuntimeSuspended
            ? contextSnapshot.SuspendedAt ?? contextSnapshot.SourceLostAt
            : contextSnapshot.SourceLostAt ?? contextSnapshot.SuspendedAt;
        var boundary = (authoritative ?? _timeProvider.GetUtcNow()).ToUniversalTime();
        var baseline = session.HistoricalPerformanceCursor.BaselineStartedAtUtc;
        if (boundary >= baseline)
        {
            return boundary;
        }

        RecordOperationLocked(
            $"Historical suspension boundary {boundary:O} preceded baseline {baseline:O} for context {contextSnapshot.ContextId}; clamped.");
        return baseline;
    }

    private void TryEstablishReadyMonitoringSessionLocked(
        MutableContextState mutableContext,
        MonitoringContextSnapshot contextSnapshot)
    {
        if (contextSnapshot.State != MonitoringContextState.Ready)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(contextSnapshot.AccountStableId))
        {
            return;
        }

        if (contextSnapshot.CurrentSourceId is null)
        {
            return;
        }

        if (mutableContext.ActiveSession is
            {
                LifecycleState: GameplaySessionLifecycleState.Suspended
            } suspendedSession)
        {
            FinalizeSessionLocked(
                mutableContext,
                suspendedSession,
                $"Ready monitoring established a new live lifecycle for context {mutableContext.ContextId}.");
        }

        if (mutableContext.ActiveSession is
            {
                LifecycleState: GameplaySessionLifecycleState.Active,
                CharacterRecordId: not null,
                IdentityResolution: CharacterIdentityResolutionState.Resolved
            })
        {
            return;
        }

        EnsureProvisionalSessionForReadyContextLocked(mutableContext, contextSnapshot);
        TryProcessStartupWelcomeRecoveryLocked(mutableContext, contextSnapshot);
    }

    private void EnsureProvisionalSessionForReadyContextLocked(
        MutableContextState mutableContext,
        MonitoringContextSnapshot contextSnapshot)
    {
        if (mutableContext.ActiveSession is { LifecycleState: not GameplaySessionLifecycleState.Finalized })
        {
            return;
        }

        var now = _timeProvider.GetUtcNow();
        mutableContext.StartupRecoveryBindingGeneration = null;
        mutableContext.ActivityRecoveryBindingGeneration = null;
        mutableContext.ActiveSession = new MutableSession
        {
            SessionId = GameplaySessionId.CreateNew(),
            LifecycleState = GameplaySessionLifecycleState.Active,
            StartedAt = now,
            CurrentSourceBindingGeneration = contextSnapshot.SourceBindingGeneration,
            CurrentSourceTransitionKind = contextSnapshot.LastSourceBindingTransitionKind,
            IdentityConfidence = CharacterIdentityConfidence.Unknown,
            IdentityResolution = CharacterIdentityResolutionState.Unresolved,
            CombatAggregator = new CombatAggregator(),
            HistoricalPerformanceCursor = CreateInitialHistoricalCursor(now)
        };
        MarkNonCombatSnapshotDirty();
        RecordOperationLocked($"Provisional session started for ready context {mutableContext.ContextId}.");
    }

    private void TryProcessStartupWelcomeRecoveryLocked(
        MutableContextState mutableContext,
        MonitoringContextSnapshot contextSnapshot)
    {
        if (contextSnapshot.CurrentSourceId is null)
        {
            return;
        }

        if (UsesZeroStartRecoveryPolicy(contextSnapshot.LastSourceBindingTransitionKind))
        {
            return;
        }

        if (mutableContext.ActiveSession is null)
        {
            return;
        }

        if (mutableContext.ActiveSession.CharacterRecordId is not null
            && mutableContext.ActiveSession.IdentityResolution == CharacterIdentityResolutionState.Resolved)
        {
            return;
        }

        if (mutableContext.StartupRecoveryBindingGeneration == contextSnapshot.SourceBindingGeneration)
        {
            return;
        }

        mutableContext.StartupRecoveryBindingGeneration = contextSnapshot.SourceBindingGeneration;

        TryRecoverMostRecentWelcomeLocked(mutableContext, contextSnapshot, "Startup");
    }

    private void TryProcessActivityTriggeredWelcomeRecoveryLocked(
        MutableContextState mutableContext,
        MonitoringContextSnapshot contextSnapshot)
    {
        if (contextSnapshot.CurrentSourceId is null
            || mutableContext.ActiveSession is null
            || mutableContext.ActiveSession.IdentityResolution == CharacterIdentityResolutionState.Resolved
            || mutableContext.StartupRecoveryBindingGeneration != contextSnapshot.SourceBindingGeneration
            || mutableContext.ActivityRecoveryBindingGeneration == contextSnapshot.SourceBindingGeneration)
        {
            return;
        }

        mutableContext.ActivityRecoveryBindingGeneration = contextSnapshot.SourceBindingGeneration;
        TryRecoverMostRecentWelcomeLocked(mutableContext, contextSnapshot, "Activity-triggered");
    }

    private void TryRecoverMostRecentWelcomeLocked(
        MutableContextState mutableContext,
        MonitoringContextSnapshot contextSnapshot,
        string recoveryKind)
    {
        var currentSource = contextSnapshot.CurrentSourceId!;
        try
        {
            if (!TryFindMostRecentWelcome(
                    currentSource,
                    contextSnapshot,
                    out var raw))
            {
                return;
            }

            if (raw is not null)
            {
                if (raw.SourceByteStart < contextSnapshot.StartupRecoveryStartOffset)
                {
                    return;
                }

                ProcessRecoveredWelcomeLocked(
                    mutableContext,
                    contextSnapshot,
                    raw,
                    recoveryKind);
                return;
            }

            var predecessor = contextSnapshot.StartupRecoveryPredecessorSourceId;
            if (predecessor is null
                || !TryFindMostRecentWelcome(predecessor, contextSnapshot, out raw)
                || raw is null
                || raw.SourceByteStart < contextSnapshot.StartupRecoveryPredecessorStartOffset
                || !ParserStartupIdentityRecovery.IsWelcomeWithinCurrentRuntime(
                    raw,
                    contextSnapshot.ProcessInstance))
            {
                return;
            }

            ProcessRecoveredWelcomeLocked(
                mutableContext,
                contextSnapshot,
                raw,
                $"{recoveryKind} predecessor");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            RecordOperationLocked(
                $"{recoveryKind} welcome recovery failed for context {contextSnapshot.ContextId} ({exception.GetType().Name}).");
        }
    }

    private static bool UsesZeroStartRecoveryPolicy(MonitoringSourceTransitionKind transition) =>
        transition is MonitoringSourceTransitionKind.AutomaticRollover
            or MonitoringSourceTransitionKind.TruncationReset
            or MonitoringSourceTransitionKind.SourceReplaced;

    private const int StartupRecoveryReadBufferSize = 16 * 1024;

    private const int StartupRecoveryMaximumLineBytes = 256 * 1024;

    private void ProcessParserEventLocked(
        MutableContextState mutableContext,
        MonitoringContextSnapshot contextSnapshot,
        ParserEvent parserEvent,
        string? welcomeRecoveryKind = null)
    {
        if (contextSnapshot.State is MonitoringContextState.Stopped or MonitoringContextState.Error)
        {
            if (mutableContext.ActiveSession is not null)
            {
                FinalizeSessionLocked(mutableContext, mutableContext.ActiveSession, $"Context {contextSnapshot.State}.");
            }

            WriteIgnoredWelcome(parserEvent, $"MonitoringContext{contextSnapshot.State}");

            return;
        }

        EnsureActiveSessionLocked(mutableContext, contextSnapshot, parserEvent);

        var session = mutableContext.ActiveSession!;
        session.LastEventAt = parserEvent.ObservedAt;
        session.CurrentSourceBindingGeneration = parserEvent.BindingGeneration;
        session.CurrentSourceTransitionKind = parserEvent.SourceTransitionKind;

        if (CharacterIdentityResolver.IsWelcomeEvidence(parserEvent))
        {
            HandleWelcomeLocked(
                mutableContext,
                contextSnapshot,
                session,
                parserEvent,
                welcomeRecoveryKind);
            return;
        }

        if (session.IdentityResolution == CharacterIdentityResolutionState.Resolved
            && session.CharacterRecordId is not null)
        {
            HandleEstablishedIdentityEventLocked(mutableContext, session, parserEvent);
            return;
        }

        if (CharacterIdentityResolver.IsStrongAttributedEvidence(parserEvent))
        {
            HandleStrongAttributedEvidenceLocked(mutableContext, contextSnapshot, session, parserEvent);
            return;
        }

        HandleOrdinaryEventLocked(mutableContext, session, parserEvent);
    }

    private void HandleWelcomeLocked(
        MutableContextState mutableContext,
        MonitoringContextSnapshot contextSnapshot,
        MutableSession session,
        ParserEvent welcomeEvent,
        string? recoveryKind)
    {
        var welcomeName = CharacterIdentityResolver.GetStrongCandidateName(welcomeEvent)!;
        var existingSessionId = session.SessionId.ToString();
        if (contextSnapshot.AccountStableId is not { } accountStableId)
        {
            WriteWelcomeProcessed(
                contextSnapshot,
                welcomeEvent,
                welcomeName,
                existingSessionId,
                GameplayWelcomeProcessingResult.Unbound,
                "AccountBindingUnavailable",
                existingSessionId);
            throw new InvalidOperationException("Welcome observed without account binding.");
        }

        var establish = _characterRepository.EstablishTrustedFromWelcome(
            accountStableId,
            welcomeName,
            welcomeEvent.ObservedAt);
        CharacterRecordId? recordId = establish.RecordId;
        CharacterRecord? record = recordId is not null ? _characterRepository.TryGetRecord(recordId) : null;

        if (!establish.IsSuccess || record is null)
        {
            var inMemory = _characterRepository.TryFindTrustedByDisplayName(accountStableId, welcomeName);
            if (inMemory is not null)
            {
                record = inMemory;
                recordId = inMemory.RecordId;
            }
        }

        if (recordId is not null
            && IsSameResolvedCharacter(session, recordId))
        {
            if (session.LifecycleState != GameplaySessionLifecycleState.Suspended)
            {
                CommitEventLocked(mutableContext, session, welcomeEvent);
            }

            RecordOperationLocked(
                $"Repeated welcome for current character on context {contextSnapshot.ContextId}; session preserved.");
            WriteWelcomeProcessed(
                contextSnapshot,
                welcomeEvent,
                welcomeName,
                existingSessionId,
                recoveryKind is null
                    ? GameplayWelcomeProcessingResult.Accepted
                    : GameplayWelcomeProcessingResult.RecoveredSession,
                recoveryKind is null ? "CurrentSessionPreserved" : $"{recoveryKind}WelcomeRecovery",
                session.SessionId.ToString());
            return;
        }

        if (session.LifecycleState != GameplaySessionLifecycleState.Finalized)
        {
            FinalizeSessionLocked(mutableContext, session, "Welcome boundary.");
        }

        var newSession = CreateSessionLocked(mutableContext, contextSnapshot, welcomeEvent);
        mutableContext.ActiveSession = newSession;

        if (!establish.IsSuccess || record is null)
        {
            if (record is null)
            {
                record = _characterRepository.TryFindTrustedByDisplayName(accountStableId, welcomeName);
                recordId = record?.RecordId;
            }

            if (record is not null && recordId is not null)
            {
                newSession.NeedsAttention = !establish.IsSuccess;
                if (newSession.NeedsAttention)
                {
                    RecordOperationLocked(
                        $"Welcome trust persisted unsaved for context {contextSnapshot.ContextId}.");
                }
            }
            else
            {
                newSession.NeedsAttention = true;
                RecordOperationLocked($"Welcome trust establishment failed for context {contextSnapshot.ContextId}.");
                CommitEventLocked(mutableContext, newSession, welcomeEvent);
                WriteWelcomeProcessed(
                    contextSnapshot,
                    welcomeEvent,
                    welcomeName,
                    existingSessionId,
                    GameplayWelcomeProcessingResult.Rejected,
                    "TrustedIdentityUnavailable",
                    newSession.SessionId.ToString());
                return;
            }
        }

        AssignIdentityLocked(
            mutableContext,
            newSession,
            recordId!,
            record!.CurrentDisplayName,
            CharacterIdentityConfidence.Confirmed,
            CharacterIdentityResolutionState.Resolved,
            "Welcome boundary.");

        CommitEventLocked(mutableContext, newSession, welcomeEvent);
        RecordOperationLocked($"Welcome session started for context {contextSnapshot.ContextId}.");
        WriteWelcomeProcessed(
            contextSnapshot,
            welcomeEvent,
            welcomeName,
            existingSessionId,
            recoveryKind is null
                ? GameplayWelcomeProcessingResult.ReplacedSession
                : GameplayWelcomeProcessingResult.RecoveredSession,
            recoveryKind is null
                ? "WelcomeBoundary"
                : $"{recoveryKind}WelcomeRecovery",
            newSession.SessionId.ToString());
    }

    private void HandleEstablishedIdentityEventLocked(
        MutableContextState mutableContext,
        MutableSession session,
        ParserEvent parserEvent)
    {
        if (session.LifecycleState == GameplaySessionLifecycleState.Suspended)
        {
            return;
        }

        CommitEventLocked(mutableContext, session, parserEvent);
    }

    private void HandleStrongAttributedEvidenceLocked(
        MutableContextState mutableContext,
        MonitoringContextSnapshot contextSnapshot,
        MutableSession session,
        ParserEvent parserEvent)
    {
        var candidateName = CharacterIdentityResolver.GetStrongCandidateName(parserEvent)!;
        var accountStableId = contextSnapshot.AccountStableId;
        if (accountStableId is null)
        {
            RetainOrOverflowLocked(mutableContext, session, parserEvent, isIdentityEvidence: true);
            return;
        }

        var trustedMatch = CharacterIdentityResolver.TryInferKnownCharacter(
            _characterRepository,
            accountStableId,
            candidateName);

        if (trustedMatch is not null)
        {
            if (IsRecordActiveInOtherContextLocked(mutableContext.ContextId, trustedMatch.RecordId))
            {
                session.NeedsAttention = true;
                session.IdentityResolution = CharacterIdentityResolutionState.Conflicted;
                session.IdentityConfidence = CharacterIdentityConfidence.Unknown;
                RecordOperationLocked(
                    $"Cross-context character conflict for context {mutableContext.ContextId}.");
            }
            else
            {
                AssignIdentityLocked(
                    mutableContext,
                    session,
                    trustedMatch.RecordId,
                    trustedMatch.CurrentDisplayName,
                    CharacterIdentityConfidence.Inferred,
                    CharacterIdentityResolutionState.Resolved,
                    "Known-character inference.");

                _characterRepository.RecordInferredObservation(
                    trustedMatch.RecordId,
                    parserEvent.ObservedAt);
                CommitRetainedEventsLocked(mutableContext, session);
                CommitEventLocked(mutableContext, session, parserEvent);
            }

            return;
        }

        AddCandidateLocked(session, candidateName, parserEvent);
        session.IdentityConfidence = CharacterIdentityConfidence.Unknown;
        session.IdentityResolution = session.Candidates.Count > 1
            && session.Candidates.Select(candidate => candidate.NormalizedName).Distinct().Count() > 1
            ? CharacterIdentityResolutionState.Conflicted
            : CharacterIdentityResolutionState.Candidate;

        RetainOrOverflowLocked(mutableContext, session, parserEvent, isIdentityEvidence: true);
    }

    private void HandleOrdinaryEventLocked(
        MutableContextState mutableContext,
        MutableSession session,
        ParserEvent parserEvent)
    {
        if (session.IdentityResolution == CharacterIdentityResolutionState.Resolved)
        {
            if (session.LifecycleState == GameplaySessionLifecycleState.Suspended)
            {
                return;
            }

            CommitEventLocked(mutableContext, session, parserEvent);
            return;
        }

        RetainOrOverflowLocked(mutableContext, session, parserEvent, isIdentityEvidence: false);
    }

    private void RetainOrOverflowLocked(
        MutableContextState mutableContext,
        MutableSession session,
        ParserEvent parserEvent,
        bool isIdentityEvidence)
    {
        if (session.RetentionOverflowed)
        {
            if (!isIdentityEvidence)
            {
                session.DiscardedAfterOverflowCount++;
            }

            return;
        }

        if (session.IdentityResolution == CharacterIdentityResolutionState.Resolved)
        {
            CommitEventLocked(mutableContext, session, parserEvent);
            return;
        }

        var payloadBytes = parserEvent.RawLine.Length;
        var wouldExceedCount = session.RetainedEvents.Count >= _options.MaxRetainedEventCount;
        var wouldExceedBytes = session.RetainedPayloadBytes + payloadBytes > _options.MaxRetainedPayloadBytes;
        if (!isIdentityEvidence && (wouldExceedCount || wouldExceedBytes))
        {
            session.RetentionOverflowed = true;
            session.IdentityResolution = CharacterIdentityResolutionState.IdentityRequired;
            session.NeedsAttention = true;
            if (!session.OverflowWarningEmitted)
            {
                session.OverflowWarningEmitted = true;
                RecordOperationLocked($"Retention overflow for context {mutableContext.ContextId}.");
            }

            session.DiscardedAfterOverflowCount++;
            return;
        }

        if (isIdentityEvidence)
        {
            if (session.CandidateEvidence.Count >= _options.MaxIdentityEvidenceCountPerContext)
            {
                if (!session.IdentityEvidenceOverflowEmitted)
                {
                    session.IdentityEvidenceOverflowEmitted = true;
                    RecordOperationLocked(
                        $"Identity evidence overflow for context {mutableContext.ContextId}.");
                }

                return;
            }
        }

        session.RetainedEvents.Add(parserEvent);
        session.RetainedPayloadBytes += payloadBytes;
        MarkNonCombatSnapshotDirty();
    }

    private void AddCandidateLocked(MutableSession session, string displayName, ParserEvent parserEvent)
    {
        var normalized = CharacterIdentityResolver.NormalizeName(displayName);
        var now = parserEvent.ObservedAt;
        var existing = session.Candidates.FirstOrDefault(candidate =>
            CharacterNameNormalizer.NamesMatch(candidate.NormalizedName, normalized));

        if (existing is null)
        {
            if (session.Candidates.Count >= _options.MaxCandidateCountPerContext)
            {
                if (!session.IdentityEvidenceOverflowEmitted)
                {
                    session.IdentityEvidenceOverflowEmitted = true;
                    RecordOperationLocked("Candidate count overflow for one gameplay session.");
                }

                return;
            }

            session.Candidates.Add(new MutableCandidate
            {
                DisplayName = displayName,
                NormalizedName = normalized,
                FirstObservedAt = now,
                LastObservedAt = now,
                ObservationCount = 1
            });
        }
        else
        {
            existing.LastObservedAt = now;
            existing.ObservationCount++;
        }

        if (session.CandidateEvidence.Count < _options.MaxIdentityEvidenceCountPerContext)
        {
            session.CandidateEvidence.Add(new CharacterIdentityEvidence
            {
                EvidenceKind = parserEvent.StructuralEvidence!.EvidenceKind,
                CandidateName = displayName,
                ObservedAt = now,
                ParserSequence = parserEvent.Sequence
            });
        }
    }

    private void AssignIdentityLocked(
        MutableContextState mutableContext,
        MutableSession session,
        CharacterRecordId recordId,
        string displayName,
        CharacterIdentityConfidence confidence,
        CharacterIdentityResolutionState resolution,
        string reason)
    {
        if (IsRecordActiveInOtherContextLocked(mutableContext.ContextId, recordId)
            && resolution == CharacterIdentityResolutionState.Resolved)
        {
            session.NeedsAttention = true;
            session.IdentityResolution = CharacterIdentityResolutionState.Conflicted;
            session.IdentityConfidence = CharacterIdentityConfidence.Unknown;
            RecordOperationLocked($"Cross-context character conflict for context {mutableContext.ContextId}.");
            return;
        }

        session.CharacterRecordId = recordId;
        session.CharacterDisplayName = displayName;
        session.IdentityConfidence = confidence;
        session.IdentityResolution = resolution;
        session.Candidates.Clear();
        session.CandidateEvidence.Clear();
        RecordOperationLocked($"{reason} assigned identity for context {mutableContext.ContextId}.");
        MarkNonCombatSnapshotDirty();
    }

    private static bool IsSameResolvedCharacter(MutableSession session, CharacterRecordId recordId) =>
        session.IdentityResolution == CharacterIdentityResolutionState.Resolved
        && session.CharacterRecordId == recordId;

    private string ResolveObservedDisplayNameForConfirmationLocked(
        MutableSession session,
        CharacterRecord record)
    {
        var recordNormalized = CharacterNameNormalizer.Normalize(record.CurrentDisplayName);

        if (NamesMatchDisplayName(session.CharacterDisplayName, recordNormalized))
        {
            return record.CurrentDisplayName;
        }

        if (!string.IsNullOrWhiteSpace(session.CharacterDisplayName))
        {
            var sessionNormalized = CharacterNameNormalizer.Normalize(session.CharacterDisplayName);
            if (RecordClaimsNormalizedNameAsAlias(record, sessionNormalized))
            {
                return session.CharacterDisplayName;
            }

            if (AnotherTrustedRecordClaimsCurrentName(
                    record.AccountStableId,
                    record.RecordId,
                    sessionNormalized)
                && record.TrustState == CharacterTrustState.TrustedFromManualConfirmation)
            {
                return record.CurrentDisplayName;
            }

            return session.CharacterDisplayName;
        }

        var candidate = session.Candidates
            .OrderByDescending(candidate => candidate.LastObservedAt)
            .ThenByDescending(candidate => candidate.ObservationCount)
            .FirstOrDefault();

        if (candidate is not null)
        {
            if (NamesMatchDisplayName(candidate.DisplayName, recordNormalized))
            {
                return record.CurrentDisplayName;
            }

            if (RecordClaimsNormalizedNameAsAlias(record, candidate.NormalizedName))
            {
                return candidate.DisplayName;
            }

            if (AnotherTrustedRecordClaimsCurrentName(
                    record.AccountStableId,
                    record.RecordId,
                    candidate.NormalizedName)
                && record.TrustState == CharacterTrustState.TrustedFromManualConfirmation)
            {
                return record.CurrentDisplayName;
            }

            return candidate.DisplayName;
        }

        return record.CurrentDisplayName;
    }

    private bool AnotherTrustedRecordClaimsCurrentName(
        string accountStableId,
        CharacterRecordId selectedRecordId,
        string normalizedName)
    {
        foreach (var other in _characterRepository.Current.Records)
        {
            if (other.RecordId == selectedRecordId
                || !string.Equals(other.AccountStableId, accountStableId, StringComparison.Ordinal))
            {
                continue;
            }

            if (CharacterNameNormalizer.NamesMatch(other.NormalizedCharacterName, normalizedName))
            {
                return true;
            }
        }

        return false;
    }

    private static bool NamesMatchDisplayName(string? displayName, string normalizedTarget) =>
        !string.IsNullOrWhiteSpace(displayName)
        && CharacterNameNormalizer.NamesMatch(
            CharacterNameNormalizer.Normalize(displayName),
            normalizedTarget);

    private static bool RecordClaimsNormalizedNameAsAlias(CharacterRecord record, string normalizedName) =>
        record.Aliases.Any(alias =>
            CharacterNameNormalizer.NamesMatch(
                CharacterNameNormalizer.Normalize(alias.DisplayName),
                normalizedName));

    private bool TryPerformCharacterHandoffFromConfirmationLocked(
        MutableContextState mutableContext,
        MonitoringContextSnapshot contextSnapshot,
        MutableSession session,
        CharacterRecordId recordId,
        string displayName)
    {
        if (!IsResolvedCharacterHandoff(session, recordId))
        {
            return false;
        }

        FinalizeSessionLocked(mutableContext, session, "Character handoff.");
        var boundaryEvent = CreateCharacterHandoffBoundaryEvent(contextSnapshot, session);
        var newSession = CreateSessionLocked(mutableContext, contextSnapshot, boundaryEvent);
        mutableContext.ActiveSession = newSession;
        AssignIdentityLocked(
            mutableContext,
            newSession,
            recordId,
            displayName,
            CharacterIdentityConfidence.Confirmed,
            CharacterIdentityResolutionState.Resolved,
            "Manual character handoff.");
        return true;
    }

    private static bool IsResolvedCharacterHandoff(MutableSession session, CharacterRecordId nextRecordId) =>
        session.IdentityResolution == CharacterIdentityResolutionState.Resolved
        && session.CharacterRecordId is not null
        && session.CharacterRecordId != nextRecordId;

    private ParserEvent CreateCharacterHandoffBoundaryEvent(
        MonitoringContextSnapshot contextSnapshot,
        MutableSession previousSession)
    {
        var sourceId = contextSnapshot.CurrentSourceId
                       ?? throw new InvalidOperationException("Character handoff requires a claimed source.");

        return new ParserEvent
        {
            ContextId = contextSnapshot.ContextId,
            SourceId = sourceId,
            SourceSegmentId = ParserSourceSegmentId.CreateNew(),
            BindingGeneration = previousSession.CurrentSourceBindingGeneration,
            SourceTransitionKind = previousSession.CurrentSourceTransitionKind,
            Sequence = previousSession.CommittedSequence + 1,
            ObservedAt = _timeProvider.GetUtcNow(),
            RawLine = string.Empty,
            SourceByteStart = 0,
            SourceByteEnd = 0,
            LineStatus = ParserLineStatus.Complete,
            EventKind = ParserEventKind.Unknown,
            ClassificationStatus = ParserClassificationStatus.Unknown,
            ClassificationRuleId = "character-handoff"
        };
    }

    private void CommitRetainedEventsLocked(MutableContextState mutableContext, MutableSession session)
    {
        foreach (var retained in session.RetainedEvents)
        {
            CommitEventLocked(mutableContext, session, retained);
        }

        session.RetainedEvents.Clear();
        session.RetainedPayloadBytes = 0;
    }

    private void CommitEventLocked(
        MutableContextState mutableContext,
        MutableSession session,
        ParserEvent parserEvent)
    {
        if (session.LifecycleState == GameplaySessionLifecycleState.Finalized)
        {
            return;
        }

        session.CommittedSequence++;
        ApplyTelemetryLocked(mutableContext, session, parserEvent);
        var committed = new GameplaySessionEvent
        {
            SessionId = session.SessionId,
            ContextId = mutableContext.ContextId,
            CharacterRecordId = session.CharacterRecordId,
            ParserEvent = parserEvent,
            CommittedAt = _timeProvider.GetUtcNow(),
            SessionSequence = session.CommittedSequence
        };

        if (_pendingCommittedEvents.Count >= _options.MaxPendingCommittedEvents)
        {
            session.NeedsAttention = true;
            _pendingCommittedEventsDiscardedCount++;
            RecordOperationLocked($"Committed-event buffer overflow for context {mutableContext.ContextId}.");
            return;
        }

        _pendingCommittedEvents.Add(committed);
        if (_pendingCommittedEvents.Count > _peakPendingCommittedEventCount)
        {
            _peakPendingCommittedEventCount = _pendingCommittedEvents.Count;
        }
        _totalCommittedEvents++;
        _lastCommittedEventAt = committed.CommittedAt;
    }

    private void ApplyTelemetryLocked(
        MutableContextState mutableContext,
        MutableSession session,
        ParserEvent parserEvent)
    {
        ApplyCombatTelemetryLocked(session, parserEvent);

        if (!_gameplayTelemetryParser.TryParse(parserEvent, out var telemetry))
        {
            return;
        }

        if (telemetry.GrammarId == GameplayTelemetryGrammarId.Chr01CharacterLevelImprovement)
        {
            ApplyCharacterLevelTelemetryLocked(session, parserEvent, telemetry);
            return;
        }

        session.SessionExperienceGained += telemetry.ExperienceGained;
        session.SessionGameplayInfluenceGained += telemetry.GameplayInfluenceGained;
        session.RollingEarnings.Apply(parserEvent, telemetry.ExperienceGained, telemetry.GameplayInfluenceGained);
        session.TrackedEarnings.Apply(telemetry.ExperienceGained, telemetry.GameplayInfluenceGained);

        if (telemetry.RewardCategory == GameplaySessionRewardCategory.None)
        {
            if (telemetry.ExperienceGained > 0 || telemetry.GameplayInfluenceGained > 0)
            {
                MarkNonCombatSnapshotDirty();
            }

            return;
        }

        MarkNonCombatSnapshotDirty();

        var rawItemText = telemetry.ReceivedItemText;
        var rewardCategory = telemetry.RewardCategory;
        ReceivedItemClassificationMetadata? classificationMetadata = null;
        BadgeAcquisitionMetadata? badgeAcquisitionMetadata = null;
        BadgeAcquiredEvent? badgeAcquisition = null;

        if (rewardCategory == GameplaySessionRewardCategory.Badge
            && !string.IsNullOrWhiteSpace(telemetry.RewardDisplayName)
            && _badgeAcquisitionResolver is not null)
        {
            badgeAcquisition = _badgeAcquisitionResolver.Resolve(telemetry.RewardDisplayName);
            if (badgeAcquisition.ResolutionState == AcquisitionIdentityResolutionState.Resolved)
            {
                badgeAcquisitionMetadata = new BadgeAcquisitionMetadata
                {
                    CatalogItemId = badgeAcquisition.ResolvedCatalogItemId,
                    CatalogVersion = badgeAcquisition.CatalogVersion,
                    MatchedAliasText = badgeAcquisition.MatchedAliasText
                };
            }
        }

        if (rewardCategory == GameplaySessionRewardCategory.ReceivedItem
            && rawItemText is not null
            && _receivedItemClassifier.TryClassify(rawItemText, out var classifiedCategory, out var metadata))
        {
            rewardCategory = classifiedCategory;
            classificationMetadata = metadata;
        }

        var displayName = telemetry.RewardDisplayName ?? rawItemText ?? "Unknown";
        var rewardEntry = new GameplaySessionRecentRewardEntry
        {
            ObservedAt = parserEvent.ObservedAt,
            Category = rewardCategory,
            DisplayName = displayName,
            Quantity = telemetry.RewardQuantity > 0 ? telemetry.RewardQuantity : 1,
            RawReceivedItemText = rawItemText ?? displayName,
            ClassificationMetadata = classificationMetadata,
            BadgeAcquisitionMetadata = badgeAcquisitionMetadata
        };

        session.RecentRewards.Insert(0, rewardEntry);
        while (session.RecentRewards.Count > _options.MaxRecentSessionRewards)
        {
            session.RecentRewards.RemoveAt(session.RecentRewards.Count - 1);
        }

        if (GameplaySessionRewardCategoryPresentation.CountsTowardCategoryDropTotals(rewardCategory))
        {
            IncrementCategoryDropCount(session, rewardCategory, rewardEntry.Quantity);
        }

        if (rewardCategory == GameplaySessionRewardCategory.RewardCurrency
            && !string.IsNullOrWhiteSpace(telemetry.RewardDisplayName))
        {
            session.RewardCurrencyTotals[telemetry.RewardDisplayName] =
                session.RewardCurrencyTotals.GetValueOrDefault(telemetry.RewardDisplayName) + rewardEntry.Quantity;
        }

        IncrementItemTotal(session, rewardCategory, displayName, rewardEntry.Quantity);

        if (badgeAcquisition is not null)
        {
            ApplyBadgePersistenceLocked(mutableContext, session, parserEvent, badgeAcquisition);
        }
    }

    private void ApplyBadgePersistenceLocked(
        MutableContextState mutableContext,
        MutableSession session,
        ParserEvent parserEvent,
        BadgeAcquiredEvent badgeAcquisition)
    {
        if (session.IdentityResolution != CharacterIdentityResolutionState.Resolved
            || session.CharacterRecordId is not { } recordId
            || badgeAcquisition.ResolutionState != AcquisitionIdentityResolutionState.Resolved
            || string.IsNullOrWhiteSpace(badgeAcquisition.ResolvedCatalogItemId)
            || mutableContext.AccountStableId is not { } accountStableId)
        {
            return;
        }

        _badgeAcquisitionRepository.RecordAcquisition(
            recordId,
            accountStableId,
            badgeAcquisition.ResolvedCatalogItemId,
            badgeAcquisition.ObservedBadgeTitle,
            parserEvent.ObservedAt,
            CharacterBadgeAcquisitionProvenance.LogReceipt);
        _characterRepository.RecordTrustedActivity(recordId, parserEvent.ObservedAt);
    }

    private void ApplyCharacterLevelTelemetryLocked(
        MutableSession session,
        ParserEvent parserEvent,
        GameplayTelemetryObservation telemetry)
    {
        if (session.IdentityResolution != CharacterIdentityResolutionState.Resolved
            || session.CharacterRecordId is not { } recordId
            || telemetry.CharacterObservedLevel is not int level)
        {
            return;
        }

        _characterRepository.RecordObservedLevel(recordId, level, parserEvent.ObservedAt);
    }

    private void ApplyCombatTelemetryLocked(MutableSession session, ParserEvent parserEvent)
    {
        if (!_combatEventParser.TryParse(parserEvent, out var combatEvent))
        {
            return;
        }

        session.CombatEvents.Add(combatEvent with { SessionId = session.SessionId });
        while (session.CombatEvents.Count > _options.MaxRetainedCombatEvents)
        {
            session.CombatEvents.RemoveAt(0);
        }

        session.CombatAggregator.Apply(combatEvent);
        session.CombatAggregator.Tracked.Apply(combatEvent);
        MarkCombatSnapshotDirty();
    }

    private static void IncrementItemTotal(
        MutableSession session,
        GameplaySessionRewardCategory category,
        string displayName,
        long quantity)
    {
        switch (category)
        {
            case GameplaySessionRewardCategory.Salvage:
                session.SalvageTotals[displayName] =
                    session.SalvageTotals.GetValueOrDefault(displayName) + quantity;
                break;
            case GameplaySessionRewardCategory.Enhancement:
                session.EnhancementTotals[displayName] =
                    session.EnhancementTotals.GetValueOrDefault(displayName) + quantity;
                break;
            case GameplaySessionRewardCategory.Recipe:
                session.RecipeTotals[displayName] =
                    session.RecipeTotals.GetValueOrDefault(displayName) + quantity;
                break;
            case GameplaySessionRewardCategory.Inspiration:
                session.InspirationTotals[displayName] =
                    session.InspirationTotals.GetValueOrDefault(displayName) + quantity;
                break;
        }
    }

    private static void IncrementCategoryDropCount(
        MutableSession session,
        GameplaySessionRewardCategory category,
        long quantity)
    {
        switch (category)
        {
            case GameplaySessionRewardCategory.Salvage:
                session.RewardCategoryCounts = session.RewardCategoryCounts with
                {
                    SalvageDropCount = session.RewardCategoryCounts.SalvageDropCount + quantity
                };
                break;
            case GameplaySessionRewardCategory.Recipe:
                session.RewardCategoryCounts = session.RewardCategoryCounts with
                {
                    RecipeDropCount = session.RewardCategoryCounts.RecipeDropCount + quantity
                };
                break;
            case GameplaySessionRewardCategory.Enhancement:
                session.RewardCategoryCounts = session.RewardCategoryCounts with
                {
                    EnhancementDropCount = session.RewardCategoryCounts.EnhancementDropCount + quantity
                };
                break;
            case GameplaySessionRewardCategory.Inspiration:
                session.RewardCategoryCounts = session.RewardCategoryCounts with
                {
                    InspirationDropCount = session.RewardCategoryCounts.InspirationDropCount + quantity
                };
                break;
        }
    }

    private void EnsureActiveSessionLocked(
        MutableContextState mutableContext,
        MonitoringContextSnapshot contextSnapshot,
        ParserEvent parserEvent)
    {
        if (mutableContext.ActiveSession is not null)
        {
            if (mutableContext.ActiveSession.LifecycleState == GameplaySessionLifecycleState.Suspended
                && contextSnapshot.State is not MonitoringContextState.RuntimeSuspended
                    and not MonitoringContextState.Stopped
                    and not MonitoringContextState.Error)
            {
                FinalizeSessionLocked(
                    mutableContext,
                    mutableContext.ActiveSession,
                    $"New gameplay session started after offline for context {mutableContext.ContextId}.");
            }
            else if (mutableContext.ActiveSession.LifecycleState != GameplaySessionLifecycleState.Finalized)
            {
                return;
            }
        }

        mutableContext.ActiveSession = CreateSessionLocked(mutableContext, contextSnapshot, parserEvent);
        MarkNonCombatSnapshotDirty();
        RecordOperationLocked($"Provisional session started for context {mutableContext.ContextId}.");
    }

    private MutableSession CreateSessionLocked(
        MutableContextState mutableContext,
        MonitoringContextSnapshot contextSnapshot,
        ParserEvent parserEvent)
    {
        var now = parserEvent.ObservedAt;
        var lifecycle = contextSnapshot.State == MonitoringContextState.RuntimeSuspended
            || contextSnapshot.SourceLostAt is not null
            ? GameplaySessionLifecycleState.Suspended
            : GameplaySessionLifecycleState.Active;

        return new MutableSession
        {
            SessionId = GameplaySessionId.CreateNew(),
            LifecycleState = lifecycle,
            StartedAt = now,
            SuspendedAt = lifecycle == GameplaySessionLifecycleState.Suspended ? now : null,
            CurrentSourceBindingGeneration = parserEvent.BindingGeneration,
            CurrentSourceTransitionKind = parserEvent.SourceTransitionKind,
            IdentityConfidence = CharacterIdentityConfidence.Unknown,
            IdentityResolution = CharacterIdentityResolutionState.Unresolved,
            CombatAggregator = new CombatAggregator(),
            HistoricalPerformanceCursor = CreateInitialHistoricalCursor(now)
        };
    }

    private void FinalizeSessionLocked(
        MutableContextState mutableContext,
        MutableSession session,
        string reason)
    {
        if (session.LifecycleState == GameplaySessionLifecycleState.Finalized)
        {
            return;
        }

        var finalizedAt = _timeProvider.GetUtcNow();
        var historicalEndedAt = session.SuspendedAt ?? finalizedAt;
        FinalizeHistoricalIntervalLocked(session, historicalEndedAt, advanceCursor: false);

        session.LifecycleState = GameplaySessionLifecycleState.Finalized;
        session.FinalizedAt = finalizedAt;
        RecordCharacterActivityFromSessionLocked(session);
        session.CombatAggregator.Freeze(session.FinalizedAt.Value);
        session.RollingEarnings.Freeze();
        if (_activeTrackedCombatContextId == mutableContext.ContextId)
        {
            session.CombatAggregator.Tracked.Stop(session.FinalizedAt.Value);
            session.TrackedEarnings.Stop(session.FinalizedAt.Value);
            _activeTrackedCombatContextId = null;
        }

        MarkCombatSnapshotDirty();
        MarkNonCombatSnapshotDirty();
        mutableContext.ActiveSession = null;
        RecordOperationLocked($"Session finalized for context {mutableContext.ContextId}: {reason}.");
    }

    private void FinalizeHistoricalIntervalLocked(
        MutableSession session,
        DateTimeOffset endedAt,
        bool advanceCursor,
        bool advanceWithoutObservation = false)
    {
        RetryPendingHistoricalObservationsLocked();

        var cursor = session.HistoricalPerformanceCursor;
        var normalizedEnd = endedAt.ToUniversalTime();
        var currentCombat = CaptureHistoricalCombatTotals(session, normalizedEnd);
        var currentExperience = session.SessionExperienceGained;
        var currentInfluence = session.SessionGameplayInfluenceGained;
        var capturedObservation = false;

        if (session.IdentityResolution == CharacterIdentityResolutionState.Resolved
            && session.CharacterRecordId is { } characterRecordId
            && !session.RetentionOverflowed
            && normalizedEnd > cursor.BaselineStartedAtUtc)
        {
            var combatProjection = CharacterPerformanceCombatProjection.Project(
                cursor.BaselineCombatTotals,
                currentCombat);
            var earningsProjection = CharacterPerformanceEarningsProjection.Project(
                new CharacterPerformanceEarningsTotals
                {
                    StartedAtUtc = cursor.BaselineStartedAtUtc,
                    ExperienceGained = cursor.BaselineExperienceGained,
                    GameplayInfluenceGained = cursor.BaselineGameplayInfluenceGained
                },
                new CharacterPerformanceEarningsTotals
                {
                    ExperienceGained = currentExperience,
                    GameplayInfluenceGained = currentInfluence
                },
                normalizedEnd);

            if (combatProjection.IsSuccess
                && combatProjection.Delta is { } combatDelta
                && earningsProjection.IsSuccess
                && earningsProjection.Delta is { } earningsDelta)
            {
                var observation = new CharacterPerformanceObservation
                {
                    GameplaySessionId = session.SessionId,
                    SegmentOrdinal = cursor.NextSegmentOrdinal,
                    CharacterRecordId = characterRecordId,
                    StartedAtUtc = earningsDelta.StartedAtUtc,
                    EndedAtUtc = earningsDelta.EndedAtUtc,
                    DamageDealt = combatDelta.DamageDealt,
                    Attempts = combatDelta.Attempts,
                    Hits = combatDelta.Hits,
                    RolledAttempts = combatDelta.RolledAttempts,
                    DisplayedChanceSumHundredths = combatDelta.DisplayedChanceSumHundredths,
                    RollSumHundredths = combatDelta.RollSumHundredths,
                    ForcedHits = combatDelta.ForcedHits,
                    Autohits = combatDelta.Autohits,
                    TotalDefeated = combatDelta.TotalDefeated,
                    MyDefeats = combatDelta.MyDefeats,
                    ExperienceGained = earningsDelta.ExperienceGained,
                    GameplayInfluenceGained = earningsDelta.GameplayInfluenceGained
                };

                PersistHistoricalObservationLocked(observation);
                capturedObservation = true;
            }
            else
            {
                var detail = combatProjection.IsSuccess
                    ? earningsProjection.Detail
                    : combatProjection.Detail;
                RecordOperationLocked(
                    $"Historical performance projection skipped for session {session.SessionId}: {detail}.");
            }
        }
        else if (session.RetentionOverflowed
                 && session.IdentityResolution == CharacterIdentityResolutionState.Resolved)
        {
            RecordOperationLocked(
                $"Historical performance skipped for overflowed session {session.SessionId}.");
        }

        if (!advanceCursor || (!capturedObservation && !advanceWithoutObservation))
        {
            return;
        }

        if (capturedObservation)
        {
            cursor.NextSegmentOrdinal++;
        }

        cursor.BaselineStartedAtUtc = normalizedEnd;
        cursor.BaselineCombatTotals = currentCombat;
        cursor.BaselineExperienceGained = currentExperience;
        cursor.BaselineGameplayInfluenceGained = currentInfluence;
    }

    private CharacterPerformanceCombatTotals CaptureHistoricalCombatTotals(
        MutableSession session,
        DateTimeOffset referenceAt) =>
        CharacterPerformanceCombatTotals.FromCombatSnapshot(
            session.CombatAggregator.ToSnapshot(
                session.StartedAt,
                referenceAt,
                referenceAt,
                _options.CombatIdleThreshold));

    private void PersistHistoricalObservationLocked(CharacterPerformanceObservation observation)
    {
        if (_historicalObservationRepository is null)
        {
            return;
        }

        if (TryPersistHistoricalObservationLocked(observation))
        {
            return;
        }

        var sameKey = _pendingHistoricalObservations.FirstOrDefault(candidate =>
            candidate.GameplaySessionId == observation.GameplaySessionId
            && candidate.SegmentOrdinal == observation.SegmentOrdinal);
        if (sameKey is null)
        {
            _pendingHistoricalObservations.Add(observation);
        }
        else if (sameKey != observation)
        {
            RecordOperationLocked(
                $"Historical performance pending-key conflict for session {observation.GameplaySessionId} segment {observation.SegmentOrdinal}.");
        }
    }

    private bool TryFindMostRecentWelcome(
        LogSourceId source,
        MonitoringContextSnapshot contextSnapshot,
        out ParserRawEvent? raw)
    {
        raw = null;
        var path = source.FilePath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return false;
        }

        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length <= 0)
        {
            return true;
        }

        var sequence = 0L;
        raw = ParserStartupIdentityRecovery.FindMostRecentWelcomeBeforeOffset(
            stream,
            stream.Length,
            StartupRecoveryReadBufferSize,
            contextSnapshot.ContextId,
            source,
            ParserSourceSegmentId.CreateNew(),
            contextSnapshot.SourceBindingGeneration,
            contextSnapshot.LastSourceBindingTransitionKind,
            _timeProvider.GetUtcNow(),
            StartupRecoveryMaximumLineBytes,
            ref sequence);
        return true;
    }

    private void ProcessRecoveredWelcomeLocked(
        MutableContextState mutableContext,
        MonitoringContextSnapshot contextSnapshot,
        ParserRawEvent raw,
        string recoveryKind)
    {
        var classified = _parserClassifier.Classify(raw);
        if (!CharacterIdentityResolver.IsWelcomeEvidence(classified))
        {
            return;
        }

        _failedContextIds.Remove(contextSnapshot.ContextId);
        ProcessParserEventLocked(mutableContext, contextSnapshot, classified, recoveryKind);
        RecordOperationLocked($"{recoveryKind} welcome recovery for context {contextSnapshot.ContextId}.");
    }

    private void WriteIgnoredWelcome(ParserEvent parserEvent, string reason)
    {
        if (!CharacterIdentityResolver.IsWelcomeEvidence(parserEvent))
        {
            return;
        }

        WriteDiagnostic(new GameplaySessionWelcomeProcessedDiagnosticEvent
        {
            ContextId = parserEvent.ContextId.ToString(),
            ParserSequence = parserEvent.Sequence,
            CandidateCharacterName = CharacterIdentityResolver.GetStrongCandidateName(parserEvent)!,
            Result = GameplayWelcomeProcessingResult.IgnoredStoppedContext,
            Reason = reason
        });
    }

    private void WriteWelcomeProcessed(
        MonitoringContextSnapshot context,
        ParserEvent parserEvent,
        string candidateCharacterName,
        string? existingSessionId,
        GameplayWelcomeProcessingResult result,
        string reason,
        string? resultingSessionId)
    {
        WriteDiagnostic(new GameplaySessionWelcomeProcessedDiagnosticEvent
        {
            ContextId = context.ContextId.ToString(),
            ParserSequence = parserEvent.Sequence,
            AccountStableId = context.AccountStableId,
            CandidateCharacterName = candidateCharacterName,
            ExistingSessionId = existingSessionId,
            Result = result,
            Reason = reason,
            ResultingSessionId = resultingSessionId
        });
    }

    private void WriteDiagnostic(DiagnosticEvent diagnosticEvent)
    {
        try
        {
            _diagnosticLog?.Write(diagnosticEvent);
        }
        catch
        {
            // Diagnostics are side-effect-only and must never influence gameplay behavior.
        }
    }

    private void RetryPendingHistoricalObservationsLocked()
    {
        if (_historicalObservationRepository is null || _pendingHistoricalObservations.Count == 0)
        {
            return;
        }

        for (var index = _pendingHistoricalObservations.Count - 1; index >= 0; index--)
        {
            if (TryPersistHistoricalObservationLocked(_pendingHistoricalObservations[index]))
            {
                _pendingHistoricalObservations.RemoveAt(index);
            }
        }
    }

    private bool TryPersistHistoricalObservationLocked(CharacterPerformanceObservation observation)
    {
        try
        {
            var result = _historicalObservationRepository!.Persist(observation);
            switch (result.Outcome)
            {
                case CharacterPerformanceObservationWriteOutcome.Persisted:
                case CharacterPerformanceObservationWriteOutcome.Duplicate:
                    return true;
                case CharacterPerformanceObservationWriteOutcome.PersistenceFailed:
                    RecordOperationLocked(
                        $"Historical performance persistence deferred for session {observation.GameplaySessionId} segment {observation.SegmentOrdinal}: {result.Detail}.");
                    return false;
                case CharacterPerformanceObservationWriteOutcome.Conflict:
                    RecordOperationLocked(
                        $"Historical performance conflict for session {observation.GameplaySessionId} segment {observation.SegmentOrdinal}: {result.Detail}.");
                    return true;
                default:
                    RecordOperationLocked(
                        $"Historical performance rejected for session {observation.GameplaySessionId} segment {observation.SegmentOrdinal}: {result.Detail}.");
                    return true;
            }
        }
        catch (Exception exception)
        {
            RecordOperationLocked(
                $"Historical performance persistence deferred for session {observation.GameplaySessionId} segment {observation.SegmentOrdinal} ({exception.GetType().Name}).");
            return false;
        }
    }

    private static MutableHistoricalPerformanceCursor CreateInitialHistoricalCursor(
        DateTimeOffset startedAt) =>
        new()
        {
            BaselineStartedAtUtc = startedAt.ToUniversalTime(),
            BaselineCombatTotals = new CharacterPerformanceCombatTotals()
        };

    private void RecordCharacterActivityFromSessionLocked(MutableSession session)
    {
        if (session.IdentityResolution != CharacterIdentityResolutionState.Resolved
            || session.CharacterRecordId is not { } recordId
            || session.LastEventAt is not DateTimeOffset observedAt)
        {
            return;
        }

        _characterRepository.RecordTrustedActivity(recordId, observedAt);
    }

    private void FinalizeAllSessionsLocked(string reason)
    {
        foreach (var mutableContext in _contexts.Values)
        {
            if (mutableContext.ActiveSession is not null)
            {
                FinalizeSessionLocked(mutableContext, mutableContext.ActiveSession, reason);
            }
        }

        RetryPendingHistoricalObservationsLocked();
    }

    private bool IsRecordActiveInOtherContextLocked(
        MonitoringContextId contextId,
        CharacterRecordId recordId)
    {
        foreach (var pair in _contexts)
        {
            if (pair.Key == contextId)
            {
                continue;
            }

            var otherSession = pair.Value.ActiveSession;
            if (otherSession is null
                || otherSession.LifecycleState == GameplaySessionLifecycleState.Finalized)
            {
                continue;
            }

            if (otherSession.IdentityResolution == CharacterIdentityResolutionState.Resolved
                && otherSession.CharacterRecordId == recordId)
            {
                return true;
            }
        }

        return false;
    }

    private MutableContextState GetOrCreateContextLocked(MonitoringContextSnapshot contextSnapshot)
    {
        if (!_contexts.TryGetValue(contextSnapshot.ContextId, out var mutableContext))
        {
            mutableContext = new MutableContextState { ContextId = contextSnapshot.ContextId };
            _contexts[contextSnapshot.ContextId] = mutableContext;
        }

        return mutableContext;
    }

    private MonitoringContextSnapshot? FindMonitoringContextLocked(MonitoringContextId contextId) =>
        _monitoringSnapshot.Contexts.FirstOrDefault(context => context.ContextId == contextId);

    private void PublishSnapshotIfChanged(bool forceCombatPublish = false)
    {
        if (!_callbacksEnabled)
        {
            return;
        }

        GameplaySessionManagerSnapshot? published = null;
        lock (_stateLock)
        {
            var now = _timeProvider.GetUtcNow();
            var shouldPublishCombat = forceCombatPublish
                || (_combatSnapshotDirty
                    && now - _lastCombatSnapshotPublishedAt >= _options.CombatSnapshotPublishInterval);

            if (!_nonCombatSnapshotDirty
                && !_trackedLifecycleSnapshotDirty
                && !shouldPublishCombat)
            {
                return;
            }

            published = PublishSnapshotLocked(now);
            if (published is null)
            {
                return;
            }

            _nonCombatSnapshotDirty = false;
            _combatSnapshotDirty = false;
            _trackedLifecycleSnapshotDirty = false;
            _lastCombatSnapshotPublishedAt = now;
            _snapshotPublicationCount++;
            _options.TestHooks?.OnSnapshotPublished?.Invoke();
        }

        if (published is not null)
        {
            RaiseStateChanged(published);
        }
    }

    private void RaiseStateChanged(GameplaySessionManagerSnapshot published)
    {
        EnterProcessorCallback();
        try
        {
            var handlers = StateChanged?.GetInvocationList() ?? [];
            var args = new GameplaySessionManagerChangedEventArgs(published);
            foreach (EventHandler<GameplaySessionManagerChangedEventArgs> handler in handlers)
            {
                try
                {
                    handler(this, args);
                }
                catch
                {
                }
            }
        }
        finally
        {
            ExitProcessorCallback();
        }
    }

    private GameplaySessionManagerSnapshot? PublishSnapshotLocked(DateTimeOffset referenceAt)
    {
        var sessions = _contexts.Values
            .Where(context => context.ActiveSession is not null)
            .Select(context => ToSnapshotLocked(context, context.ActiveSession!, referenceAt))
            .OrderBy(snapshot => snapshot.ContextId.Value)
            .ToArray();

        var candidate = GameplaySessionManagerSnapshot.Create(sessions, referenceAt, _revision + 1);
        if (SemanticEqualsLocked(candidate))
        {
            return null;
        }

        _revision = candidate.Revision;
        _snapshot = candidate;
        return candidate;
    }

    private bool SemanticEqualsLocked(GameplaySessionManagerSnapshot candidate)
    {
        if (_snapshot.Revision == 0 && candidate.Sessions.Count == 0)
        {
            return true;
        }

        if (_snapshot.Sessions.Count != candidate.Sessions.Count)
        {
            return false;
        }

        for (var index = 0; index < candidate.Sessions.Count; index++)
        {
            var left = _snapshot.Sessions[index];
            var right = candidate.Sessions[index];
            if (left.SessionId != right.SessionId
                || left.LifecycleState != right.LifecycleState
                || left.CharacterRecordId != right.CharacterRecordId
                || left.CharacterIdentityConfidence != right.CharacterIdentityConfidence
                || left.CharacterIdentityResolutionState != right.CharacterIdentityResolutionState
                || left.RetainedEventCount != right.RetainedEventCount
                || left.RetainedPayloadBytes != right.RetainedPayloadBytes
                || left.DiscardedAfterOverflowCount != right.DiscardedAfterOverflowCount
                || left.RetentionOverflowed != right.RetentionOverflowed
                || left.NeedsAttention != right.NeedsAttention
                || left.CandidateCount != right.CandidateCount
                || !CandidatesEquivalent(left.IdentityCandidates, right.IdentityCandidates)
                || left.CurrentSourceBindingGeneration != right.CurrentSourceBindingGeneration
                || left.CurrentSourceTransitionKind != right.CurrentSourceTransitionKind
                || left.SuspendedAt != right.SuspendedAt
                || left.FinalizedAt != right.FinalizedAt
                || left.LastEventAt != right.LastEventAt
                || left.SessionExperienceGained != right.SessionExperienceGained
                || left.SessionGameplayInfluenceGained != right.SessionGameplayInfluenceGained
                || !RecentRewardsEquivalent(left.RecentRewards, right.RecentRewards)
                || !RewardCurrencyTotalsEquivalent(left.RewardCurrencyTotals, right.RewardCurrencyTotals)
                || !ItemTotalsEquivalent(left.SalvageTotals, right.SalvageTotals)
                || !ItemTotalsEquivalent(left.EnhancementTotals, right.EnhancementTotals)
                || !ItemTotalsEquivalent(left.RecipeTotals, right.RecipeTotals)
                || !ItemTotalsEquivalent(left.InspirationTotals, right.InspirationTotals)
                || !RewardCategoryCountsEquivalent(left.RewardCategoryCounts, right.RewardCategoryCounts)
                || !CombatSnapshotsEquivalent(left.Combat, right.Combat)
                || !RollingEarningsSnapshotsEquivalent(left.RollingEarnings, right.RollingEarnings)
                || !TrackedEarningsSnapshotsEquivalent(left.TrackedEarnings, right.TrackedEarnings))
            {
                return false;
            }
        }

        return true;
    }

    private static bool CandidatesEquivalent(
        IReadOnlyList<CharacterIdentityCandidate> left,
        IReadOnlyList<CharacterIdentityCandidate> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            var leftCandidate = left[index];
            var rightCandidate = right[index];
            if (!string.Equals(leftCandidate.DisplayName, rightCandidate.DisplayName, StringComparison.Ordinal)
                || !string.Equals(leftCandidate.NormalizedName, rightCandidate.NormalizedName, StringComparison.Ordinal)
                || leftCandidate.FirstObservedAt != rightCandidate.FirstObservedAt
                || leftCandidate.LastObservedAt != rightCandidate.LastObservedAt
                || leftCandidate.ObservationCount != rightCandidate.ObservationCount)
            {
                return false;
            }
        }

        return true;
    }

    private static IReadOnlyList<CharacterIdentityCandidate> ToCandidateSnapshots(IEnumerable<MutableCandidate> candidates) =>
        candidates
            .Select(candidate => new CharacterIdentityCandidate
            {
                DisplayName = candidate.DisplayName,
                NormalizedName = candidate.NormalizedName,
                FirstObservedAt = candidate.FirstObservedAt,
                LastObservedAt = candidate.LastObservedAt,
                ObservationCount = candidate.ObservationCount
            })
            .OrderBy(candidate => candidate.NormalizedName, StringComparer.Ordinal)
            .ToArray();

    private GameplaySessionSnapshot ToSnapshotLocked(
        MutableContextState context,
        MutableSession session,
        DateTimeOffset referenceAt) =>
        new()
        {
            SessionId = session.SessionId,
            ContextId = context.ContextId,
            AccountStableId = context.AccountStableId,
            LifecycleState = session.LifecycleState,
            CharacterRecordId = session.CharacterRecordId,
            CharacterDisplayName = session.CharacterDisplayName,
            CharacterIdentityConfidence = session.IdentityConfidence,
            CharacterIdentityResolutionState = session.IdentityResolution,
            StartedAt = session.StartedAt,
            SuspendedAt = session.SuspendedAt,
            FinalizedAt = session.FinalizedAt,
            CurrentSourceBindingGeneration = session.CurrentSourceBindingGeneration,
            CurrentSourceTransitionKind = session.CurrentSourceTransitionKind,
            RetainedEventCount = session.RetainedEvents.Count,
            RetainedPayloadBytes = session.RetainedPayloadBytes,
            DiscardedAfterOverflowCount = session.DiscardedAfterOverflowCount,
            RetentionOverflowed = session.RetentionOverflowed,
            NeedsAttention = session.NeedsAttention,
            CandidateCount = session.Candidates.Count,
            IdentityCandidates = ToCandidateSnapshots(session.Candidates),
            LastEventAt = session.LastEventAt,
            SessionExperienceGained = session.SessionExperienceGained,
            SessionGameplayInfluenceGained = session.SessionGameplayInfluenceGained,
            RecentRewards = session.RecentRewards.ToArray(),
            RewardCurrencyTotals = session.RewardCurrencyTotals
                .Select(pair => new GameplaySessionRewardCurrencyTotal
                {
                    CurrencyDisplayName = pair.Key,
                    Quantity = pair.Value
                })
                .OrderBy(total => total.CurrencyDisplayName, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            SalvageTotals = ToItemTotals(session.SalvageTotals),
            EnhancementTotals = ToItemTotals(session.EnhancementTotals),
            RecipeTotals = ToItemTotals(session.RecipeTotals),
            InspirationTotals = ToItemTotals(session.InspirationTotals),
            RewardCategoryCounts = session.RewardCategoryCounts,
            RetainedCombatEventCount = session.CombatEvents.Count,
            Combat = session.CombatAggregator.ToSnapshot(
                session.StartedAt,
                referenceAt,
                session.FinalizedAt,
                _options.CombatIdleThreshold),
            RollingEarnings = session.RollingEarnings.ToSnapshot(
                session.StartedAt,
                referenceAt,
                session.FinalizedAt),
            TrackedEarnings = session.TrackedEarnings.ToSnapshot(referenceAt)
        };

    private static bool CombatSnapshotsEquivalent(CombatSnapshot left, CombatSnapshot right) =>
        left.DamageDealt == right.DamageDealt
        && left.DamageReceived == right.DamageReceived
        && left.HealingDealt == right.HealingDealt
        && left.HealingReceived == right.HealingReceived
        && left.TotalDefeated == right.TotalDefeated
        && left.MyDefeats == right.MyDefeats
        && left.PowerActivations == right.PowerActivations
        && left.IsInCombat == right.IsInCombat
        && left.LastCombatAt == right.LastCombatAt
        && left.CurrentEngagementDuration == right.CurrentEngagementDuration
        && left.SessionDamagePerSecondHundredths == right.SessionDamagePerSecondHundredths
        && TrackedCombatSnapshotsEquivalent(left.Tracked, right.Tracked)
        && RollingCombatSnapshotsEquivalent(left.Rolling, right.Rolling)
        && AccuracySnapshotsEquivalent(left.Accuracy, right.Accuracy);

    private static bool AccuracySnapshotsEquivalent(
        CombatAccuracyScopeSnapshot left,
        CombatAccuracyScopeSnapshot right) =>
        left.Attempts == right.Attempts
        && left.Hits == right.Hits
        && left.Misses == right.Misses
        && left.RolledAttempts == right.RolledAttempts
        && left.DisplayedChanceSumHundredths == right.DisplayedChanceSumHundredths
        && left.RollSumHundredths == right.RollSumHundredths
        && left.ForcedHits == right.ForcedHits
        && left.Autohits == right.Autohits;

    private static bool TrackedCombatSnapshotsEquivalent(
        TrackedCombatScopeSnapshot left,
        TrackedCombatScopeSnapshot right) =>
        left.IsTracking == right.IsTracking
        && left.IsPaused == right.IsPaused
        && left.StartedAt == right.StartedAt
        && left.ActiveElapsed == right.ActiveElapsed
        && left.DamageDealt == right.DamageDealt
        && left.DamageReceived == right.DamageReceived
        && left.HealingDealt == right.HealingDealt
        && left.HealingReceived == right.HealingReceived
        && left.TotalDefeated == right.TotalDefeated
        && left.MyDefeats == right.MyDefeats
        && left.PowerActivations == right.PowerActivations
        && left.DamagePerSecondHundredths == right.DamagePerSecondHundredths
        && AccuracySnapshotsEquivalent(left.Accuracy, right.Accuracy);

    private static bool RollingCombatSnapshotsEquivalent(
        RollingCombatScopeSnapshot left,
        RollingCombatScopeSnapshot right) =>
        left.TimestampPrecisionUnavailable == right.TimestampPrecisionUnavailable
        && RollingWindowSnapshotsEquivalent(left.OneMinute, right.OneMinute)
        && RollingWindowSnapshotsEquivalent(left.TwoMinutes, right.TwoMinutes)
        && RollingWindowSnapshotsEquivalent(left.FiveMinutes, right.FiveMinutes)
        && RollingWindowSnapshotsEquivalent(left.TenMinutes, right.TenMinutes)
        && RollingWindowSnapshotsEquivalent(left.FifteenMinutes, right.FifteenMinutes);

    private static bool RollingWindowSnapshotsEquivalent(
        RollingCombatWindowSnapshot left,
        RollingCombatWindowSnapshot right) =>
        left.Availability == right.Availability
        && left.WindowMinutes == right.WindowMinutes
        && left.DamageDealt == right.DamageDealt
        && left.DamagePerSecondHundredths == right.DamagePerSecondHundredths
        && left.WindowDuration == right.WindowDuration
        && left.EffectiveDenominator == right.EffectiveDenominator
        && left.TotalDefeated == right.TotalDefeated
        && left.MyDefeats == right.MyDefeats
        && AccuracySnapshotsEquivalent(left.Accuracy, right.Accuracy);

    private static bool RollingEarningsSnapshotsEquivalent(
        RollingEarningsScopeSnapshot left,
        RollingEarningsScopeSnapshot right) =>
        left.TimestampPrecisionUnavailable == right.TimestampPrecisionUnavailable
        && RollingEarningsWindowSnapshotsEquivalent(left.OneMinute, right.OneMinute)
        && RollingEarningsWindowSnapshotsEquivalent(left.TwoMinutes, right.TwoMinutes)
        && RollingEarningsWindowSnapshotsEquivalent(left.FiveMinutes, right.FiveMinutes)
        && RollingEarningsWindowSnapshotsEquivalent(left.TenMinutes, right.TenMinutes)
        && RollingEarningsWindowSnapshotsEquivalent(left.FifteenMinutes, right.FifteenMinutes);

    private static bool RollingEarningsWindowSnapshotsEquivalent(
        RollingEarningsWindowSnapshot left,
        RollingEarningsWindowSnapshot right) =>
        left.Availability == right.Availability
        && left.WindowMinutes == right.WindowMinutes
        && left.ExperienceGained == right.ExperienceGained
        && left.InfluenceGained == right.InfluenceGained
        && left.WindowDuration == right.WindowDuration
        && left.EffectiveDenominator == right.EffectiveDenominator;

    private static bool TrackedEarningsSnapshotsEquivalent(
        TrackedEarningsScopeSnapshot left,
        TrackedEarningsScopeSnapshot right) =>
        left.IsTracking == right.IsTracking
        && left.IsPaused == right.IsPaused
        && left.StartedAt == right.StartedAt
        && left.ActiveElapsed == right.ActiveElapsed
        && left.ExperienceGained == right.ExperienceGained
        && left.InfluenceGained == right.InfluenceGained;

    private static GameplaySessionItemTotal[] ToItemTotals(Dictionary<string, long> totals) =>
        totals
            .Select(pair => new GameplaySessionItemTotal
            {
                DisplayName = pair.Key,
                Quantity = pair.Value
            })
            .OrderBy(total => total.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static bool RecentRewardsEquivalent(
        IReadOnlyList<GameplaySessionRecentRewardEntry> left,
        IReadOnlyList<GameplaySessionRecentRewardEntry> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            var leftEntry = left[index];
            var rightEntry = right[index];
            if (leftEntry.ObservedAt != rightEntry.ObservedAt
                || leftEntry.Category != rightEntry.Category
                || !string.Equals(leftEntry.DisplayName, rightEntry.DisplayName, StringComparison.Ordinal)
                || leftEntry.Quantity != rightEntry.Quantity
                || !string.Equals(leftEntry.RawReceivedItemText, rightEntry.RawReceivedItemText, StringComparison.Ordinal)
                || !ClassificationMetadataEquivalent(leftEntry.ClassificationMetadata, rightEntry.ClassificationMetadata)
                || !BadgeAcquisitionMetadataEquivalent(leftEntry.BadgeAcquisitionMetadata, rightEntry.BadgeAcquisitionMetadata))
            {
                return false;
            }
        }

        return true;
    }

    private static bool RewardCurrencyTotalsEquivalent(
        IReadOnlyList<GameplaySessionRewardCurrencyTotal> left,
        IReadOnlyList<GameplaySessionRewardCurrencyTotal> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            var leftTotal = left[index];
            var rightTotal = right[index];
            if (!string.Equals(leftTotal.CurrencyDisplayName, rightTotal.CurrencyDisplayName, StringComparison.OrdinalIgnoreCase)
                || leftTotal.Quantity != rightTotal.Quantity)
            {
                return false;
            }
        }

        return true;
    }

    private static bool ItemTotalsEquivalent(
        IReadOnlyList<GameplaySessionItemTotal> left,
        IReadOnlyList<GameplaySessionItemTotal> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            var leftTotal = left[index];
            var rightTotal = right[index];
            if (!string.Equals(leftTotal.DisplayName, rightTotal.DisplayName, StringComparison.OrdinalIgnoreCase)
                || leftTotal.Quantity != rightTotal.Quantity)
            {
                return false;
            }
        }

        return true;
    }

    private static bool ClassificationMetadataEquivalent(
        ReceivedItemClassificationMetadata? left,
        ReceivedItemClassificationMetadata? right)
    {
        if (left is null && right is null)
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        return string.Equals(left.EnhancementSetName, right.EnhancementSetName, StringComparison.Ordinal)
            && left.EnhancementLevelMin == right.EnhancementLevelMin
            && left.EnhancementLevelMax == right.EnhancementLevelMax
            && string.Equals(left.EnhancementRarity, right.EnhancementRarity, StringComparison.Ordinal)
            && string.Equals(left.EnhancementTypeLabel, right.EnhancementTypeLabel, StringComparison.Ordinal)
            && string.Equals(left.SalvageRarity, right.SalvageRarity, StringComparison.Ordinal)
            && left.SalvageLevelMin == right.SalvageLevelMin
            && left.SalvageLevelMax == right.SalvageLevelMax
            && string.Equals(left.InspirationForm, right.InspirationForm, StringComparison.Ordinal);
    }

    private static bool BadgeAcquisitionMetadataEquivalent(
        BadgeAcquisitionMetadata? left,
        BadgeAcquisitionMetadata? right)
    {
        if (left is null && right is null)
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        return string.Equals(left.CatalogItemId, right.CatalogItemId, StringComparison.Ordinal)
            && string.Equals(left.CatalogVersion, right.CatalogVersion, StringComparison.Ordinal)
            && string.Equals(left.MatchedAliasText, right.MatchedAliasText, StringComparison.Ordinal);
    }

    private static bool RewardCategoryCountsEquivalent(
        GameplaySessionRewardCategoryCounts left,
        GameplaySessionRewardCategoryCounts right) =>
        left.SalvageDropCount == right.SalvageDropCount
        && left.RecipeDropCount == right.RecipeDropCount
        && left.EnhancementDropCount == right.EnhancementDropCount
        && left.InspirationDropCount == right.InspirationDropCount;

    private void FlushCommittedEvents()
    {
        if (!_callbacksEnabled)
        {
            lock (_stateLock)
            {
                _pendingCommittedEvents.Clear();
            }

            return;
        }

        List<List<GameplaySessionEvent>> batches;
        lock (_stateLock)
        {
            batches = DrainPendingCommittedEventsLocked();
        }

        foreach (var batch in batches)
        {
            RaiseCommittedEvents(batch);
        }
    }

    private List<List<GameplaySessionEvent>> DrainPendingCommittedEventsLocked()
    {
        if (_pendingCommittedEvents.Count == 0)
        {
            return [];
        }

        var batches = new List<List<GameplaySessionEvent>>();
        while (_pendingCommittedEvents.Count > 0)
        {
            var batchSize = Math.Min(_pendingCommittedEvents.Count, _options.CommittedEventBatchSize);
            var batch = _pendingCommittedEvents.Take(batchSize).ToList();
            _pendingCommittedEvents.RemoveRange(0, batchSize);
            batches.Add(batch);
        }

        return batches;
    }

    private void RaiseCommittedEvents(IReadOnlyList<GameplaySessionEvent> batch)
    {
        EnterProcessorCallback();
        try
        {
            var handlers = CommittedEventsAvailable?.GetInvocationList() ?? [];
            var args = new GameplaySessionEventsAvailableEventArgs(batch);
            foreach (EventHandler<GameplaySessionEventsAvailableEventArgs> handler in handlers)
            {
                try
                {
                    handler(this, args);
                }
                catch
                {
                }
            }
        }
        finally
        {
            ExitProcessorCallback();
        }
    }

    private void RecordOperationLocked(string message)
    {
        _recentOperations.Add(message);
        while (_recentOperations.Count > _options.MaxRecentDiagnosticsEntries)
        {
            _recentOperations.RemoveAt(0);
        }
    }

    private enum WorkItemKind
    {
        MonitoringSnapshot,
        ClassifiedEvents,
        ConfirmCharacter,
        ClearIdentity,
        HistoricalPerformanceBoundary,
        TrackedCombatLifecycle,
        ResetRuntimeGeneration
    }

    private enum TrackedCombatLifecycleAction
    {
        Start,
        Pause,
        Resume,
        Stop,
        Reset
    }

    private sealed class WorkItem
    {
        public required WorkItemKind Kind { get; init; }

        public MonitoringSessionManagerSnapshot? Snapshot { get; init; }

        public IReadOnlyList<ParserEvent>? Events { get; init; }

        public MonitoringContextId? ContextId { get; init; }

        public CharacterRecordId? CharacterRecordId { get; init; }

        public TrackedCombatLifecycleAction? TrackedCombatAction { get; init; }

        public TaskCompletionSource<GameplaySessionOperationResult>? Completion { get; init; }

        public TaskCompletionSource? SnapshotCompletion { get; init; }

        public static WorkItem MonitoringSnapshot(
            MonitoringSessionManagerSnapshot snapshot,
            TaskCompletionSource? completion = null) =>
            new() { Kind = WorkItemKind.MonitoringSnapshot, Snapshot = snapshot, SnapshotCompletion = completion };

        public static WorkItem ClassifiedEvents(IReadOnlyList<ParserEvent> events) =>
            new() { Kind = WorkItemKind.ClassifiedEvents, Events = events };

        public static WorkItem ConfirmCharacter(
            MonitoringContextId contextId,
            CharacterRecordId characterRecordId,
            TaskCompletionSource<GameplaySessionOperationResult> completion) =>
            new()
            {
                Kind = WorkItemKind.ConfirmCharacter,
                ContextId = contextId,
                CharacterRecordId = characterRecordId,
                Completion = completion
            };

        public static WorkItem ClearIdentity(
            MonitoringContextId contextId,
            TaskCompletionSource<GameplaySessionOperationResult> completion) =>
            new()
            {
                Kind = WorkItemKind.ClearIdentity,
                ContextId = contextId,
                Completion = completion
            };

        public static WorkItem HistoricalPerformanceBoundary(
            MonitoringContextId contextId,
            TaskCompletionSource<GameplaySessionOperationResult> completion) =>
            new()
            {
                Kind = WorkItemKind.HistoricalPerformanceBoundary,
                ContextId = contextId,
                Completion = completion
            };

        public static WorkItem TrackedCombatLifecycle(
            TrackedCombatLifecycleAction action,
            MonitoringContextId contextId,
            TaskCompletionSource<GameplaySessionOperationResult> completion) =>
            new()
            {
                Kind = WorkItemKind.TrackedCombatLifecycle,
                TrackedCombatAction = action,
                ContextId = contextId,
                Completion = completion
            };

        public static WorkItem ResetRuntimeGeneration() =>
            new() { Kind = WorkItemKind.ResetRuntimeGeneration };
    }

    private sealed class MutableContextState
    {
        public required MonitoringContextId ContextId { get; init; }

        public string? AccountStableId { get; set; }

        public MutableSession? ActiveSession { get; set; }

        public long? StartupRecoveryBindingGeneration { get; set; }

        public long? ActivityRecoveryBindingGeneration { get; set; }
    }

    private sealed class MutableSession
    {
        public required GameplaySessionId SessionId { get; init; }

        public required GameplaySessionLifecycleState LifecycleState { get; set; }

        public CharacterRecordId? CharacterRecordId { get; set; }

        public string? CharacterDisplayName { get; set; }

        public CharacterIdentityConfidence IdentityConfidence { get; set; }

        public CharacterIdentityResolutionState IdentityResolution { get; set; }

        public required DateTimeOffset StartedAt { get; init; }

        public DateTimeOffset? SuspendedAt { get; set; }

        public DateTimeOffset? FinalizedAt { get; set; }

        public long CurrentSourceBindingGeneration { get; set; }

        public MonitoringSourceTransitionKind CurrentSourceTransitionKind { get; set; }

        public List<ParserEvent> RetainedEvents { get; } = [];

        public long RetainedPayloadBytes { get; set; }

        public long DiscardedAfterOverflowCount { get; set; }

        public bool RetentionOverflowed { get; set; }

        public bool OverflowWarningEmitted { get; set; }

        public bool IdentityEvidenceOverflowEmitted { get; set; }

        public bool NeedsAttention { get; set; }

        public DateTimeOffset? LastEventAt { get; set; }

        public long CommittedSequence { get; set; }

        public long SessionExperienceGained { get; set; }

        public long SessionGameplayInfluenceGained { get; set; }

        public List<GameplaySessionRecentRewardEntry> RecentRewards { get; } = [];

        public List<CombatEvent> CombatEvents { get; } = [];

        public CombatAggregator CombatAggregator { get; init; } = new();

        public required MutableHistoricalPerformanceCursor HistoricalPerformanceCursor { get; init; }

        public RollingEarningsAccumulator RollingEarnings { get; init; } = new();

        public TrackedEarningsAccumulator TrackedEarnings { get; init; } = new();

        public Dictionary<string, long> RewardCurrencyTotals { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, long> SalvageTotals { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, long> EnhancementTotals { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, long> RecipeTotals { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, long> InspirationTotals { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        public GameplaySessionRewardCategoryCounts RewardCategoryCounts { get; set; } =
            new GameplaySessionRewardCategoryCounts();

        public List<MutableCandidate> Candidates { get; } = [];

        public List<CharacterIdentityEvidence> CandidateEvidence { get; } = [];
    }

    private sealed class MutableHistoricalPerformanceCursor
    {
        public int NextSegmentOrdinal { get; set; }

        public required DateTimeOffset BaselineStartedAtUtc { get; set; }

        public required CharacterPerformanceCombatTotals BaselineCombatTotals { get; set; }

        public long BaselineExperienceGained { get; set; }

        public long BaselineGameplayInfluenceGained { get; set; }
    }

    private sealed class MutableCandidate
    {
        public required string DisplayName { get; init; }

        public required string NormalizedName { get; init; }

        public required DateTimeOffset FirstObservedAt { get; init; }

        public DateTimeOffset LastObservedAt { get; set; }

        public int ObservationCount { get; set; }
    }
}
