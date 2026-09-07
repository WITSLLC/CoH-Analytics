using CoHAnalytics.Models;
using CoHAnalytics.Services.Diagnostics;

namespace CoHAnalytics.Services;

/// <summary>
/// Owns the collection of monitoring contexts: client/account/source assignment, one-source-per-
/// context enforcement, automatic enrollment of every unambiguous active source, ambiguity offers,
/// automatic same-account rollover, and runtime-aware suspension/resumption (Revision 6 §3.6,
/// Revision 7 §3.6.20).
/// </summary>
/// <remarks>
/// <para>
/// The manager consumes <see cref="ILogActivityService"/> observations for source discovery and
/// activity only. It never parses chat content, reads chat lines, identifies characters, creates
/// gameplay sessions, or mutates/suppresses/reinterprets Log Activity's own state. Log Activity
/// remains fully observational and independent (§3.6.12).
/// </para>
/// <para>
/// It subscribes directly to <see cref="IGameRuntimeService.StatusChanged"/> so suspension is
/// immediate and event-driven, never waiting for Log Activity's polling interval.
/// </para>
/// <para>
/// All mutation of the context/offer/suppression collections happens under one lock
/// (<see cref="_sync"/>), so exactly one reconciliation pass runs at a time and no source can be
/// double-claimed under races. Subscriber callbacks are always invoked outside the lock.
/// </para>
/// </remarks>
public sealed class MonitoringSessionManager : IMonitoringSessionManager, IDisposable
{
    private const int MaxRecentDecisions = 50;

    private readonly IGameRuntimeService _runtimeService;
    private readonly ILogActivityService _logActivityService;
    private readonly MonitoringSessionManagerOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly IDiagnosticLog? _diagnosticLog;
    private readonly object _sync = new();
    private readonly Dictionary<MonitoringContextId, MutableContext> _contexts = [];
    private readonly Dictionary<string, MutableOffer> _offers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DeclineSuppression> _declineSuppressions = new(StringComparer.Ordinal);
    private readonly HashSet<string> _exitedProcessSourceSuppressions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _runtimeExitSourceOffsets = new(StringComparer.Ordinal);
    private readonly List<string> _recentDecisions = [];
    private readonly Dictionary<MonitoringContextId, MonitoringProcessBindingUnavailableReason>
        _diagnosticUnavailableProcessBindings = [];

    private MonitoringSessionManagerSnapshot _snapshot = MonitoringSessionManagerSnapshot.Empty;
    private LogActivitySnapshot _lastLogActivitySnapshot = LogActivitySnapshot.Empty;
    private long _revision;
    private bool _running;
    private bool _lastKnownRuntimeAvailable;
    private bool _disposed;
    private DateTimeOffset? _lastReconciliationAt;
    private TimeSpan? _lastReconciliationDuration;
    private string? _lastReconciliationReason;
    private DateTimeOffset? _logActivityEnrollmentCutoffAt;

    public MonitoringSessionManager(
        IGameRuntimeService runtimeService,
        ILogActivityService logActivityService,
        MonitoringSessionManagerOptions? options = null,
        IDiagnosticLog? diagnosticLog = null)
    {
        _runtimeService = runtimeService;
        _logActivityService = logActivityService;
        _options = options ?? new MonitoringSessionManagerOptions();
        _timeProvider = _options.TimeProvider;
        _diagnosticLog = diagnosticLog;
    }

    public event EventHandler<MonitoringSessionManagerChangedEventArgs>? StateChanged;

    public MonitoringSessionManagerSnapshot Current
    {
        get
        {
            lock (_sync)
            {
                return _snapshot;
            }
        }
    }

    public bool IsRunning => _running;

    public bool IsRuntimeAvailable
    {
        get
        {
            lock (_sync)
            {
                return _lastKnownRuntimeAvailable;
            }
        }
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        // Read outside the lock: a plain in-memory property read on another service, not a
        // filesystem call, but kept outside regardless so no service call ever happens while
        // holding the manager's own state lock.
        var initialLogSnapshot = _logActivityService.Current;

        bool changed;
        MonitoringSessionManagerSnapshot snapshot;

        lock (_sync)
        {
            if (_running)
            {
                return Task.CompletedTask;
            }

            _running = true;

            // Read current runtime status immediately; do not wait for a later status event, so
            // contexts created while the client is already offline start suspended.
            _lastKnownRuntimeAvailable = IsRuntimeStatusAvailable(_runtimeService.CurrentStatus);
            _runtimeService.StatusChanged += OnRuntimeStatusChanged;
            _logActivityService.ActivityChanged += OnLogActivityChanged;

            var now = _timeProvider.GetUtcNow();
            ReconcileLocked(initialLogSnapshot, now, "Startup");
            changed = TryPublishLocked(now, out snapshot);
        }

        if (changed)
        {
            StateChanged?.Invoke(this, new MonitoringSessionManagerChangedEventArgs(snapshot));
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (!_running)
            {
                return Task.CompletedTask;
            }

            _running = false;
            _runtimeService.StatusChanged -= OnRuntimeStatusChanged;
            _logActivityService.ActivityChanged -= OnLogActivityChanged;
        }

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        lock (_sync)
        {
            if (_running)
            {
                _running = false;
                _runtimeService.StatusChanged -= OnRuntimeStatusChanged;
                _logActivityService.ActivityChanged -= OnLogActivityChanged;
            }
        }
    }

    // ---- Explicit source-claim domain APIs (§3.6.8) ----------------------------------------

    public MonitoringSourceClaimResult ClaimSource(MonitoringContextId contextId, LogSourceId sourceId)
    {
        bool changed;
        MonitoringSessionManagerSnapshot snapshot;
        MonitoringSourceClaimResult result;

        lock (_sync)
        {
            if (!_running)
            {
                return MonitoringSourceClaimResult.Failure(MonitoringSourceClaimOutcome.ManagerNotRunning);
            }

            if (!_contexts.TryGetValue(contextId, out var context) || context.State == MonitoringContextState.Stopped)
            {
                return MonitoringSourceClaimResult.Failure(MonitoringSourceClaimOutcome.ContextNotFound);
            }

            if (context.CurrentSourceId is not null)
            {
                return MonitoringSourceClaimResult.Failure(MonitoringSourceClaimOutcome.ContextAlreadyHasSource);
            }

            var candidate = _lastLogActivitySnapshot.Candidates.FirstOrDefault(c => c.SourceId == sourceId);
            if (candidate is null)
            {
                return MonitoringSourceClaimResult.Failure(MonitoringSourceClaimOutcome.SourceNotFound);
            }

            if (candidate.ActivityState == LogSourceActivityState.Unavailable)
            {
                return MonitoringSourceClaimResult.Failure(MonitoringSourceClaimOutcome.SourceUnavailable);
            }

            if (IsSourceOwnedLocked(sourceId))
            {
                return MonitoringSourceClaimResult.Failure(MonitoringSourceClaimOutcome.SourceAlreadyClaimed);
            }

            if (context.AccountStableId is not null
                && !string.Equals(context.AccountStableId, sourceId.AccountStableId, StringComparison.Ordinal))
            {
                return MonitoringSourceClaimResult.Failure(MonitoringSourceClaimOutcome.InvalidAccountBinding);
            }

            var now = _timeProvider.GetUtcNow();
            context.AccountStableId ??= sourceId.AccountStableId;
            context.AccountDisplayName ??= sourceId.AccountDisplayName;
            var transitionKind = context.PreviousSourceId == sourceId
                ? MonitoringSourceTransitionKind.SourceReclaimed
                : MonitoringSourceTransitionKind.SourceAssigned;
            context.CurrentSourceId = sourceId;
            context.SourceAssignedAt = now;
            context.StartupRecoveryStartOffset = GetStartupRecoveryStartOffsetLocked(sourceId);
            ApplyStartupRecoveryPredecessorLocked(context, candidate);
            context.LastSourceTransitionAt = now;
            context.LastSourceTransitionReason = MonitoringContextChangeReason.ManualClaim;
            context.SourceLostAt = null;
            context.LastHandledTruncationSourceId = candidate.LastChangeKind == LogSourceChangeKind.Truncated
                ? sourceId.Value
                : null;
            context.LastHandledTruncationObservedAt = candidate.LastChangeKind == LogSourceChangeKind.Truncated
                ? candidate.LastObservedAt
                : null;
            AdvanceSourceBindingLocked(context, transitionKind);

            if (context.State == MonitoringContextState.WaitingForSource)
            {
                if (_lastKnownRuntimeAvailable)
                {
                    context.State = MonitoringContextState.Ready;
                }
                else
                {
                    context.State = MonitoringContextState.RuntimeSuspended;
                    context.PreviousStateBeforeSuspension = MonitoringContextState.Ready;
                    context.SuspendedAt = now;
                }

                context.LastStateChangedAt = now;
            }

            InvalidateOfferIfPendingLocked(sourceId, MonitoringSourceOfferState.ClaimedElsewhere);
            RecordDecisionLocked($"Context {contextId} claimed {DescribeSource(sourceId)}.");
            changed = TryPublishLocked(now, out snapshot);
            result = MonitoringSourceClaimResult.Success();
        }

        if (changed)
        {
            StateChanged?.Invoke(this, new MonitoringSessionManagerChangedEventArgs(snapshot));
        }

        return result;
    }

    public MonitoringSourceClaimResult ReleaseSource(MonitoringContextId contextId)
    {
        bool changed;
        MonitoringSessionManagerSnapshot snapshot;

        lock (_sync)
        {
            if (!_running)
            {
                return MonitoringSourceClaimResult.Failure(MonitoringSourceClaimOutcome.ManagerNotRunning);
            }

            if (!_contexts.TryGetValue(contextId, out var context))
            {
                return MonitoringSourceClaimResult.Failure(MonitoringSourceClaimOutcome.ContextNotFound);
            }

            if (context.CurrentSourceId is null)
            {
                return MonitoringSourceClaimResult.Failure(MonitoringSourceClaimOutcome.SourceNotFound);
            }

            var now = _timeProvider.GetUtcNow();
            context.PreviousSourceId = context.CurrentSourceId;
            context.CurrentSourceId = null;
            ClearStartupRecoveryPredecessorLocked(context);
            context.LastSourceTransitionAt = now;
            context.LastSourceTransitionReason = MonitoringContextChangeReason.SourceReleased;
            context.SourceLostAt = null;
            AdvanceSourceBindingLocked(context, MonitoringSourceTransitionKind.SourceReleased);

            if (context.State == MonitoringContextState.Ready)
            {
                context.State = MonitoringContextState.WaitingForSource;
                context.LastStateChangedAt = now;
            }
            else if (context.State == MonitoringContextState.RuntimeSuspended)
            {
                context.PreviousStateBeforeSuspension = MonitoringContextState.WaitingForSource;
            }

            RecordDecisionLocked($"Context {contextId} released its source.");
            changed = TryPublishLocked(now, out snapshot);
        }

        if (changed)
        {
            StateChanged?.Invoke(this, new MonitoringSessionManagerChangedEventArgs(snapshot));
        }

        return MonitoringSourceClaimResult.Success();
    }

    public MonitoringSourceClaimResult RemoveContext(MonitoringContextId contextId)
    {
        bool changed;
        MonitoringSessionManagerSnapshot snapshot;

        lock (_sync)
        {
            if (!_contexts.TryGetValue(contextId, out _))
            {
                return MonitoringSourceClaimResult.Failure(MonitoringSourceClaimOutcome.ContextNotFound);
            }

            var now = _timeProvider.GetUtcNow();
            RemoveContextLocked(contextId, now);
            changed = TryPublishLocked(now, out snapshot);
        }

        if (changed)
        {
            StateChanged?.Invoke(this, new MonitoringSessionManagerChangedEventArgs(snapshot));
        }

        return MonitoringSourceClaimResult.Success();
    }

    public void ResetForNewRuntimeGeneration(bool observedZeroClientCountAfterNonZero = true)
    {
        bool changed;
        MonitoringSessionManagerSnapshot snapshot;

        lock (_sync)
        {
            if (!_running)
            {
                return;
            }

            var now = _timeProvider.GetUtcNow();
            if (observedZeroClientCountAfterNonZero)
            {
                // Full exit/relaunch boundary: stale logs from the prior process instance must not
                // resurrect on the new client.
                _logActivityEnrollmentCutoffAt = now;
            }

            foreach (var contextId in _contexts.Keys.ToArray())
            {
                RemoveContextLocked(contextId, now);
                _contexts.Remove(contextId);
            }

            _offers.Clear();
            _declineSuppressions.Clear();
            _exitedProcessSourceSuppressions.Clear();
            ReconcileLocked(_lastLogActivitySnapshot, now, "RuntimeGenerationReset");
            changed = TryPublishLocked(now, out snapshot);
        }

        if (changed)
        {
            StateChanged?.Invoke(this, new MonitoringSessionManagerChangedEventArgs(snapshot));
        }
    }

    private void RemoveContextLocked(MonitoringContextId contextId, DateTimeOffset now)
    {
        if (!_contexts.TryGetValue(contextId, out var context))
        {
            return;
        }

        if (context.CurrentSourceId is not null)
        {
            context.PreviousSourceId = context.CurrentSourceId;
            context.CurrentSourceId = null;
            ClearStartupRecoveryPredecessorLocked(context);
            context.LastSourceTransitionAt = now;
            context.LastSourceTransitionReason = MonitoringContextChangeReason.ContextRemoved;
            context.SourceLostAt = null;
            AdvanceSourceBindingLocked(context, MonitoringSourceTransitionKind.ContextRemoved);
        }

        context.State = MonitoringContextState.Stopped;
        context.LastStateChangedAt = now;
        context.SuspendedAt = null;
        context.PreviousStateBeforeSuspension = null;
        RecordDecisionLocked($"Context {contextId} removed.");
    }

    // ---- Additional-session offer decisions (§3.6.6) ----------------------------------------

    public MonitoringSourceDecision AcceptOffer(MonitoringSourceOfferId offerId)
    {
        bool changed;
        MonitoringSessionManagerSnapshot snapshot;
        MonitoringSourceDecision result;

        lock (_sync)
        {
            if (!_running)
            {
                return MonitoringSourceDecision.Failure(MonitoringSourceDecisionOutcome.ManagerNotRunning);
            }

            var offer = _offers.Values.FirstOrDefault(o => o.OfferId == offerId);
            if (offer is null || offer.State != MonitoringSourceOfferState.Pending)
            {
                return MonitoringSourceDecision.Failure(MonitoringSourceDecisionOutcome.OfferNotFound);
            }

            var now = _timeProvider.GetUtcNow();
            var candidate = _lastLogActivitySnapshot.Candidates.FirstOrDefault(c => c.SourceId == offer.SourceId);

            if (candidate is null || candidate.ActivityState == LogSourceActivityState.Unavailable)
            {
                offer.State = MonitoringSourceOfferState.SourceUnavailable;
                _offers.Remove(offer.SourceId.Value);
                RecordDecisionLocked($"Offer for {DescribeSource(offer.SourceId)} could not be accepted: source unavailable.");
                changed = TryPublishLocked(now, out snapshot);
                result = MonitoringSourceDecision.Failure(MonitoringSourceDecisionOutcome.SourceUnavailable);
            }
            else if (IsSourceOwnedLocked(offer.SourceId))
            {
                offer.State = MonitoringSourceOfferState.ClaimedElsewhere;
                _offers.Remove(offer.SourceId.Value);
                RecordDecisionLocked($"Offer for {DescribeSource(offer.SourceId)} could not be accepted: already claimed.");
                changed = TryPublishLocked(now, out snapshot);
                result = MonitoringSourceDecision.Failure(MonitoringSourceDecisionOutcome.SourceAlreadyClaimed);
            }
            else
            {
                var contextId = MonitoringContextId.CreateNew();
                var context = new MutableContext
                {
                    ContextId = contextId,
                    AccountStableId = offer.AccountStableId,
                    AccountDisplayName = offer.AccountDisplayName,
                    CurrentSourceId = offer.SourceId,
                    CreatedAt = now,
                    LastStateChangedAt = now,
                    SourceAssignedAt = now,
                    StartupRecoveryStartOffset = GetStartupRecoveryStartOffsetLocked(offer.SourceId),
                    LastSourceTransitionAt = now,
                    LastSourceTransitionReason = MonitoringContextChangeReason.AcceptedAdditionalSessionOffer,
                    SourceBindingGeneration = 1,
                    LastSourceBindingTransitionKind = MonitoringSourceTransitionKind.SourceAssigned
                };

                ApplyStartupRecoveryPredecessorLocked(context, candidate);

                // Accepting while offline is allowed: it creates a suspended context with Ready
                // as the prior state, never fabricated active state (§7/§11).
                if (_lastKnownRuntimeAvailable)
                {
                    context.State = MonitoringContextState.Ready;
                }
                else
                {
                    context.State = MonitoringContextState.RuntimeSuspended;
                    context.PreviousStateBeforeSuspension = MonitoringContextState.Ready;
                    context.SuspendedAt = now;
                }

                _contexts[contextId] = context;
                TryBindProcessInstanceLocked(context);
                offer.State = MonitoringSourceOfferState.Accepted;
                _offers.Remove(offer.SourceId.Value);
                RecordDecisionLocked($"Accepted offer for {DescribeSource(offer.SourceId)}; created context {contextId}.");
                changed = TryPublishLocked(now, out snapshot);
                result = MonitoringSourceDecision.Success(contextId);
            }
        }

        if (changed)
        {
            StateChanged?.Invoke(this, new MonitoringSessionManagerChangedEventArgs(snapshot));
        }

        return result;
    }

    public MonitoringSourceDecision DeclineOffer(MonitoringSourceOfferId offerId)
    {
        bool changed;
        MonitoringSessionManagerSnapshot snapshot;

        lock (_sync)
        {
            if (!_running)
            {
                return MonitoringSourceDecision.Failure(MonitoringSourceDecisionOutcome.ManagerNotRunning);
            }

            var offer = _offers.Values.FirstOrDefault(o => o.OfferId == offerId);
            if (offer is null || offer.State != MonitoringSourceOfferState.Pending)
            {
                return MonitoringSourceDecision.Failure(MonitoringSourceDecisionOutcome.OfferNotFound);
            }

            var now = _timeProvider.GetUtcNow();
            offer.State = MonitoringSourceOfferState.Declined;
            _offers.Remove(offer.SourceId.Value);

            // Version 1 decline-suppression policy: suppressed until the source is observed
            // Inactive/Unavailable and later returns to Growing, i.e. until its current growth
            // episode ends and a distinct later one begins.
            _declineSuppressions[offer.SourceId.Value] = new DeclineSuppression();

            RecordDecisionLocked($"Declined offer for {DescribeSource(offer.SourceId)}.");
            changed = TryPublishLocked(now, out snapshot);
        }

        if (changed)
        {
            StateChanged?.Invoke(this, new MonitoringSessionManagerChangedEventArgs(snapshot));
        }

        return MonitoringSourceDecision.Success();
    }

    public MonitoringSourceDecision ReconsiderSource(LogSourceId sourceId)
    {
        bool changed;
        MonitoringSessionManagerSnapshot snapshot;

        lock (_sync)
        {
            if (!_running)
            {
                return MonitoringSourceDecision.Failure(MonitoringSourceDecisionOutcome.ManagerNotRunning);
            }

            _declineSuppressions.Remove(sourceId.Value);
            var now = _timeProvider.GetUtcNow();
            RecordDecisionLocked($"Manual reconsideration requested for {DescribeSource(sourceId)}.");
            ReconcileLocked(_lastLogActivitySnapshot, now, "ManualReconsideration");
            changed = TryPublishLocked(now, out snapshot);
        }

        if (changed)
        {
            StateChanged?.Invoke(this, new MonitoringSessionManagerChangedEventArgs(snapshot));
        }

        return MonitoringSourceDecision.Success();
    }

    // ---- Controlled context-creation seam (Slice 5A, retained) ------------------------------

    /// <summary>
    /// Controlled, non-user-facing seam for creating a monitoring context directly, without
    /// going through automatic first-context creation or offer acceptance. Intended for tests
    /// and developer diagnostics.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="sourceId"/> is already owned by another non-stopped context,
    /// so ownership is never silently reassigned.
    /// </exception>
    internal MonitoringContextId AddContext(
        string? accountStableId = null,
        string? accountDisplayName = null,
        LogSourceId? sourceId = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        bool changed;
        MonitoringSessionManagerSnapshot snapshot;
        MonitoringContextId contextId;

        lock (_sync)
        {
            if (sourceId is not null && IsSourceOwnedLocked(sourceId))
            {
                throw new InvalidOperationException(
                    $"Log source '{sourceId.Value}' is already owned by another monitoring context.");
            }

            var now = _timeProvider.GetUtcNow();
            contextId = MonitoringContextId.CreateNew();
            var intendedState = sourceId is not null
                ? MonitoringContextState.Ready
                : MonitoringContextState.WaitingForSource;

            var context = new MutableContext
            {
                ContextId = contextId,
                AccountStableId = accountStableId,
                AccountDisplayName = accountDisplayName,
                CurrentSourceId = sourceId,
                CreatedAt = now,
                LastStateChangedAt = now,
                SourceAssignedAt = sourceId is not null ? now : null,
                StartupRecoveryStartOffset = sourceId is not null
                    ? GetStartupRecoveryStartOffsetLocked(sourceId)
                    : 0,
                LastSourceTransitionAt = sourceId is not null ? now : null,
                LastSourceTransitionReason = sourceId is not null ? MonitoringContextChangeReason.ManualSeed : null,
                SourceBindingGeneration = sourceId is not null ? 1 : 0,
                LastSourceBindingTransitionKind = sourceId is not null
                    ? MonitoringSourceTransitionKind.SourceAssigned
                    : MonitoringSourceTransitionKind.None
            };

            if (sourceId is not null)
            {
                var candidate = _lastLogActivitySnapshot.Candidates
                    .FirstOrDefault(item => item.SourceId == sourceId);
                if (candidate is not null)
                {
                    ApplyStartupRecoveryPredecessorLocked(context, candidate);
                }
            }

            if (_lastKnownRuntimeAvailable)
            {
                context.State = intendedState;
            }
            else
            {
                context.State = MonitoringContextState.RuntimeSuspended;
                context.PreviousStateBeforeSuspension = intendedState;
                context.SuspendedAt = now;
            }

            _contexts[contextId] = context;
            TryBindProcessInstanceLocked(context);
            changed = TryPublishLocked(now, out snapshot);
        }

        if (changed)
        {
            StateChanged?.Invoke(this, new MonitoringSessionManagerChangedEventArgs(snapshot));
        }

        return contextId;
    }

    /// <summary>
    /// Test-only seam that forces one context into <see cref="MonitoringContextState.Error"/>.
    /// </summary>
    internal void MarkContextErrorForTests(MonitoringContextId contextId)
    {
        bool changed;
        MonitoringSessionManagerSnapshot snapshot;

        lock (_sync)
        {
            if (!_contexts.TryGetValue(contextId, out var context))
            {
                return;
            }

            var now = _timeProvider.GetUtcNow();
            context.State = MonitoringContextState.Error;
            context.PreviousStateBeforeSuspension = null;
            context.SuspendedAt = null;
            context.LastStateChangedAt = now;
            changed = TryPublishLocked(now, out snapshot);
        }

        if (changed)
        {
            StateChanged?.Invoke(this, new MonitoringSessionManagerChangedEventArgs(snapshot));
        }
    }

    public MonitoringSessionManagerDiagnostics GetDiagnostics()
    {
        lock (_sync)
        {
            return new MonitoringSessionManagerDiagnostics
            {
                IsRunning = _running,
                IsRuntimeAvailable = _lastKnownRuntimeAvailable,
                LastSnapshotRevision = _revision,
                ContextCount = _contexts.Count,
                Contexts = [.. _contexts.Values.Select(context => context.ToSnapshot())],
                PendingOffers =
                [
                    .. _offers.Values
                        .Where(offer => offer.State == MonitoringSourceOfferState.Pending)
                        .Select(offer => offer.ToSnapshot())
                ],
                DeclineSuppressedSourceIds = [.. _declineSuppressions.Keys],
                RecentDecisions = [.. _recentDecisions],
                LastReconciliationAt = _lastReconciliationAt,
                LastReconciliationDuration = _lastReconciliationDuration,
                LastReconciliationReason = _lastReconciliationReason
            };
        }
    }

    // ---- Runtime transitions -----------------------------------------------------------------

    private void OnRuntimeStatusChanged(object? sender, GameRuntimeStatusChangedEventArgs e)
    {
        bool changed;
        MonitoringSessionManagerSnapshot snapshot;

        lock (_sync)
        {
            if (!_running)
            {
                return;
            }

            var now = _timeProvider.GetUtcNow();
            var wasAvailable = _lastKnownRuntimeAvailable;
            var nowAvailable = IsRuntimeStatusAvailable(e.NewStatus);
            _lastKnownRuntimeAvailable = nowAvailable;

            var availabilityChanged = wasAvailable != nowAvailable;
            var processSnapshotChanged = e.PreviousRunningClientCount != e.RunningClientCount
                || !HomecomingProcessInstance.SequenceEqualByIdentity(
                    e.PreviousRunningClients,
                    e.RunningClients);

            if (!availabilityChanged && !processSnapshotChanged)
            {
                // No availability transition (e.g. Off -> Error, or Error -> Unconfigured):
                // treating every non-Running status the same way is the conservative choice.
                return;
            }

            if (availabilityChanged)
            {
                if (!nowAvailable)
                {
                    if (e.PreviousRunningClientCount > 0 && e.RunningClientCount == 0)
                    {
                        CaptureRuntimeExitSourceOffsetsLocked();
                    }
                    SuspendAllLocked(now);
                }
                else
                {
                    ResumeAllLocked(now);
                }
            }

            var isMultiClientCollapse = e.PreviousRunningClients.Count > 1
                && e.RunningClients.Count == 1;
            if (isMultiClientCollapse)
            {
                ReconcileSurvivingProcessContextLocked(e.RunningClients[0], now);
            }

            // Re-evaluate claims/offers under the new runtime availability using the same,
            // already-cached Log Activity snapshot: this does not depend on a new scan, and it
            // lets a runtime return unblock auto-first-context creation if it is now eligible.
            ReconcileLocked(
                _lastLogActivitySnapshot,
                now,
                "RuntimeTransition",
                allowLogBasedSoleProcessHandoff: !isMultiClientCollapse);

            changed = TryPublishLocked(now, out snapshot);
        }

        if (changed)
        {
            StateChanged?.Invoke(this, new MonitoringSessionManagerChangedEventArgs(snapshot));
        }
    }

    private void OnLogActivityChanged(object? sender, LogActivityChangedEventArgs e)
    {
        // Pulled outside the lock, per the invalidation-hint contract: the event itself carries
        // no authority, only the current immutable snapshot does.
        var latest = _logActivityService.Current;

        bool changed;
        MonitoringSessionManagerSnapshot snapshot;

        lock (_sync)
        {
            if (!_running)
            {
                return;
            }

            var now = _timeProvider.GetUtcNow();
            ReconcileLocked(latest, now, "LogActivityChanged");
            changed = TryPublishLocked(now, out snapshot);
        }

        if (changed)
        {
            StateChanged?.Invoke(this, new MonitoringSessionManagerChangedEventArgs(snapshot));
        }
    }

    private void SuspendAllLocked(DateTimeOffset now)
    {
        foreach (var context in _contexts.Values)
        {
            if (context.State != MonitoringContextState.WaitingForSource
                && context.State != MonitoringContextState.Ready)
            {
                continue;
            }

            context.PreviousStateBeforeSuspension = context.State;
            context.State = MonitoringContextState.RuntimeSuspended;
            context.SuspendedAt = now;
            context.LastStateChangedAt = now;
        }
    }

    private void ResumeAllLocked(DateTimeOffset now)
    {
        foreach (var context in _contexts.Values)
        {
            if (context.State != MonitoringContextState.RuntimeSuspended)
            {
                continue;
            }

            context.State = context.PreviousStateBeforeSuspension ?? MonitoringContextState.WaitingForSource;
            if (context.SourceLostAt is not null)
            {
                context.State = MonitoringContextState.WaitingForSource;
                RecordDecisionLocked(
                    $"Context {context.ContextId} resumed waiting because its claimed source remains unavailable.");
            }

            context.PreviousStateBeforeSuspension = null;
            context.SuspendedAt = null;
            context.LastStateChangedAt = now;
        }
    }

    // ---- Reconciliation: claims, rollover, offers, source loss (§3.6.6-3.6.9) ----------------

    private void ReconcileLocked(
        LogActivitySnapshot logSnapshot,
        DateTimeOffset now,
        string reason,
        bool allowLogBasedSoleProcessHandoff = true)
    {
        var startedAt = _timeProvider.GetUtcNow();

        _lastLogActivitySnapshot = logSnapshot;
        var candidatesById = logSnapshot.Candidates
            .GroupBy(candidate => candidate.SourceId.Value, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        UpdateDeclineSuppressionsLocked(logSnapshot);
        UpdateExitedProcessSourceSuppressionsLocked(logSnapshot);
        TryApplyReplacementsLocked(logSnapshot.Candidates, now);
        UpdateSourceLossAndRecoveryLocked(candidatesById, now);
        TryApproveTruncationResetsLocked(candidatesById, now);
        TryApplyRolloversLocked(candidatesById, now);
        InvalidateStaleOffersLocked(candidatesById);
        ReconcileProcessInstanceBindingsLocked();
        if (allowLogBasedSoleProcessHandoff)
        {
            TryApplySameProcessAccountHandoffsLocked(logSnapshot, now);
        }
        CreateOrClaimForUnclaimedGrowingSourcesLocked(logSnapshot, now);
        if (allowLogBasedSoleProcessHandoff)
        {
            EnforceSingleProcessAccountOwnershipLocked(logSnapshot, now);
        }
        ReconcileProcessInstanceBindingsLocked();

        _lastReconciliationAt = now;
        _lastReconciliationReason = reason;
        _lastReconciliationDuration = _timeProvider.GetUtcNow() - startedAt;
    }

    /// <summary>
    /// Resolves a two-client to one-client transition from proven process ownership before log
    /// activity can be mistaken for survivor identity.
    /// </summary>
    private void ReconcileSurvivingProcessContextLocked(
        HomecomingProcessInstance survivingProcess,
        DateTimeOffset now)
    {
        var activeContexts = _contexts.Values
            .Where(context => context.State != MonitoringContextState.Stopped)
            .ToList();
        var provenSurvivor = activeContexts.FirstOrDefault(context =>
            context.ProcessInstance is { } bound
            && (survivingProcess.Equals(bound)
                || survivingProcess.IsMetadataRefinementOf(bound)));

        foreach (var context in activeContexts)
        {
            var isBoundToExitedProcess = context.ProcessInstance is { } bound
                && !survivingProcess.Equals(bound)
                && !survivingProcess.IsMetadataRefinementOf(bound);
            var isDisplacedByProvenSurvivor = provenSurvivor is not null
                && context.ContextId != provenSurvivor.ContextId;

            if (!isBoundToExitedProcess && !isDisplacedByProvenSurvivor)
            {
                continue;
            }

            var exitedSourceId = context.CurrentSourceId;
            RemoveContextLocked(context.ContextId, now);
            _contexts.Remove(context.ContextId);
            if (exitedSourceId is not null)
            {
                _exitedProcessSourceSuppressions.Add(exitedSourceId.Value);
            }
            RecordDecisionLocked(
                $"Retired context {context.ContextId} after process {context.ProcessInstance?.ProcessId ?? 0} exited; " +
                $"process {survivingProcess.ProcessId} survived.");
        }

        var remaining = _contexts.Values
            .Where(context => context.State != MonitoringContextState.Stopped)
            .ToList();
        if (remaining.Count == 1)
        {
            remaining[0].ProcessInstance = survivingProcess;
        }
    }

    private void UpdateExitedProcessSourceSuppressionsLocked(LogActivitySnapshot logSnapshot)
    {
        var activeSourceIds = logSnapshot.Candidates
            .Where(candidate => candidate.ActivityState is not (
                LogSourceActivityState.Inactive or LogSourceActivityState.Unavailable))
            .Select(candidate => candidate.SourceId.Value)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var sourceId in _exitedProcessSourceSuppressions.ToArray())
        {
            if (!activeSourceIds.Contains(sourceId))
            {
                _exitedProcessSourceSuppressions.Remove(sourceId);
            }
        }
    }

    private void UpdateDeclineSuppressionsLocked(LogActivitySnapshot logSnapshot)
    {
        foreach (var candidate in logSnapshot.Candidates)
        {
            if (!_declineSuppressions.TryGetValue(candidate.SourceId.Value, out var suppression))
            {
                continue;
            }

            if (candidate.ActivityState is LogSourceActivityState.Inactive or LogSourceActivityState.Unavailable)
            {
                suppression.SeenNonGrowingSinceDecline = true;
            }
            else if (candidate.ActivityState == LogSourceActivityState.Growing && suppression.SeenNonGrowingSinceDecline)
            {
                // A distinct later growth episode began: the suppression lifts and the source
                // becomes eligible for a fresh offer again this same pass.
                _declineSuppressions.Remove(candidate.SourceId.Value);
            }
        }
    }

    private void UpdateSourceLossAndRecoveryLocked(Dictionary<string, LogSourceCandidate> candidatesById, DateTimeOffset now)
    {
        foreach (var context in _contexts.Values)
        {
            if (context.CurrentSourceId is null || context.State == MonitoringContextState.Stopped)
            {
                continue;
            }

            candidatesById.TryGetValue(context.CurrentSourceId.Value, out var candidate);
            var isUnavailable = candidate is null || candidate.ActivityState == LogSourceActivityState.Unavailable;

            if (isUnavailable && context.SourceLostAt is null)
            {
                context.SourceLostAt = now;
                context.LastSourceTransitionAt = now;
                context.LastSourceTransitionReason = MonitoringContextChangeReason.SourceLost;

                if (context.State == MonitoringContextState.Ready)
                {
                    context.State = MonitoringContextState.WaitingForSource;
                    context.LastStateChangedAt = now;
                }

                // RuntimeSuspended contexts stay suspended; their prior state remains Ready so
                // the context itself remains paused, but it must resume into WaitingForSource
                // while the loss persists.
                if (context.State == MonitoringContextState.RuntimeSuspended)
                {
                    context.PreviousStateBeforeSuspension = MonitoringContextState.WaitingForSource;
                    RecordDecisionLocked(
                        $"Context {context.ContextId} lost its source while suspended; resume target changed to WaitingForSource.");
                }
            }
            else if (!isUnavailable && context.SourceLostAt is not null)
            {
                context.SourceLostAt = null;
                context.LastSourceTransitionAt = now;
                context.LastSourceTransitionReason = MonitoringContextChangeReason.SourceRecovered;

                if (context.State == MonitoringContextState.WaitingForSource)
                {
                    context.State = MonitoringContextState.Ready;
                    context.LastStateChangedAt = now;
                }
            }
        }
    }

    private void TryApplyReplacementsLocked(IReadOnlyList<LogSourceCandidate> candidates, DateTimeOffset now)
    {
        var replacementSignals = candidates
            .Where(candidate => candidate.IsReplaced || candidate.LastChangeKind == LogSourceChangeKind.Replaced)
            .ToList();

        if (replacementSignals.Count == 0)
        {
            return;
        }

        foreach (var context in _contexts.Values)
        {
            var currentSource = context.CurrentSourceId;
            if (currentSource is null || context.State == MonitoringContextState.Stopped)
            {
                continue;
            }

            var laterGenerationsAtSamePath = replacementSignals
                .Where(candidate =>
                    string.Equals(candidate.FilePath, currentSource.FilePath, StringComparison.OrdinalIgnoreCase)
                    && candidate.SourceId.IdentityGeneration > currentSource.IdentityGeneration)
                .ToList();

            if (laterGenerationsAtSamePath.Count == 0)
            {
                continue;
            }

            var sameAccount = laterGenerationsAtSamePath
                .Where(candidate => string.Equals(
                    candidate.AccountStableId,
                    context.AccountStableId,
                    StringComparison.Ordinal))
                .ToList();

            if (sameAccount.Count == 0)
            {
                RecordDecisionLocked(
                    $"Replacement rejected for context {context.ContextId}: account mismatch at the claimed path.");
                continue;
            }

            var expectedGeneration = currentSource.IdentityGeneration + 1;
            var expectedSuccessors = sameAccount
                .Where(candidate => candidate.SourceId.IdentityGeneration == expectedGeneration)
                .ToList();

            if (expectedSuccessors.Count == 0)
            {
                RecordDecisionLocked(
                    $"Replacement rejected for context {context.ContextId}: expected identity generation {expectedGeneration}.");
                continue;
            }

            var evidencedSuccessors = expectedSuccessors
                .Where(candidate => candidate.IsReplaced && !string.IsNullOrWhiteSpace(candidate.ReplacementEvidence))
                .ToList();

            if (evidencedSuccessors.Count == 0)
            {
                RecordDecisionLocked(
                    $"Replacement rejected for context {context.ContextId}: replacement evidence is missing.");
                continue;
            }

            if (evidencedSuccessors.Count != 1)
            {
                RecordDecisionLocked(
                    $"Replacement rejected for context {context.ContextId}: multiple successor candidates are ambiguous.");
                continue;
            }

            var replacement = evidencedSuccessors[0];
            if (!replacement.Exists || replacement.ActivityState == LogSourceActivityState.Unavailable)
            {
                RecordDecisionLocked(
                    $"Replacement rejected for context {context.ContextId}: successor source is unavailable.");
                continue;
            }

            if (IsSourceOwnedLocked(replacement.SourceId))
            {
                RecordDecisionLocked(
                    $"Replacement rejected for context {context.ContextId}: successor source is already claimed.");
                continue;
            }

            context.PreviousSourceId = currentSource;
            context.CurrentSourceId = replacement.SourceId;
            context.AccountDisplayName = replacement.AccountDisplayName;
            context.SourceAssignedAt = now;
            context.StartupRecoveryStartOffset = 0;
            ClearStartupRecoveryPredecessorLocked(context);
            context.LastSourceTransitionAt = now;
            context.LastSourceTransitionReason = MonitoringContextChangeReason.SourceReplaced;
            context.SourceLostAt = null;
            context.LastHandledTruncationSourceId = null;
            context.LastHandledTruncationObservedAt = null;
            AdvanceSourceBindingLocked(context, MonitoringSourceTransitionKind.SourceReplaced);

            if (context.State == MonitoringContextState.RuntimeSuspended)
            {
                context.PreviousStateBeforeSuspension = MonitoringContextState.Ready;
            }
            else
            {
                context.State = MonitoringContextState.Ready;
                context.LastStateChangedAt = now;
            }

            InvalidateOfferIfPendingLocked(replacement.SourceId, MonitoringSourceOfferState.ClaimedElsewhere);
            RecordDecisionLocked(
                $"Context {context.ContextId} accepted verified source replacement generation {expectedGeneration}.");
        }
    }

    private void TryApproveTruncationResetsLocked(
        Dictionary<string, LogSourceCandidate> candidatesById,
        DateTimeOffset now)
    {
        foreach (var context in _contexts.Values)
        {
            if (context.CurrentSourceId is not { } sourceId || context.State == MonitoringContextState.Stopped)
            {
                continue;
            }

            if (!candidatesById.TryGetValue(sourceId.Value, out var candidate)
                || !candidate.IsTruncated
                || candidate.LastChangeKind != LogSourceChangeKind.Truncated)
            {
                continue;
            }

            if (string.Equals(context.LastHandledTruncationSourceId, sourceId.Value, StringComparison.Ordinal)
                && context.LastHandledTruncationObservedAt == candidate.LastObservedAt)
            {
                continue;
            }

            context.LastHandledTruncationSourceId = sourceId.Value;
            context.LastHandledTruncationObservedAt = candidate.LastObservedAt;
            context.LastSourceTransitionAt = now;
            context.LastSourceTransitionReason = MonitoringContextChangeReason.TruncationReset;
            context.SourceLostAt = null;
            AdvanceSourceBindingLocked(context, MonitoringSourceTransitionKind.TruncationReset);

            if (context.State == MonitoringContextState.RuntimeSuspended)
            {
                context.PreviousStateBeforeSuspension = MonitoringContextState.Ready;
            }
            else if (context.State == MonitoringContextState.WaitingForSource)
            {
                context.State = MonitoringContextState.Ready;
                context.LastStateChangedAt = now;
            }

            RecordDecisionLocked(
                $"Context {context.ContextId} approved one parser reset for a newly observed truncation.");
        }
    }

    private void TryApplyRolloversLocked(Dictionary<string, LogSourceCandidate> candidatesById, DateTimeOffset now)
    {
        var rolloverCandidates = candidatesById.Values
            .Where(c => c.IsRolloverCandidate
                && c.RolloverPredecessorSourceId is not null
                && c.ActivityState == LogSourceActivityState.Growing
                && !IsSourceOwnedLocked(c.SourceId))
            .ToList();

        foreach (var group in rolloverCandidates.GroupBy(c => c.RolloverPredecessorSourceId!, StringComparer.Ordinal))
        {
            if (group.Count() != 1)
            {
                // Multiple new sources claim the same predecessor: ambiguous, do not guess.
                RecordDecisionLocked($"Rollover skipped: multiple candidates claim the same predecessor ({group.Key[..Math.Min(8, group.Key.Length)]}).");
                continue;
            }

            var candidate = group.First();
            var context = _contexts.Values.FirstOrDefault(c =>
                c.State != MonitoringContextState.Stopped
                && c.CurrentSourceId is not null
                && string.Equals(c.CurrentSourceId.Value, group.Key, StringComparison.Ordinal));

            if (context is null)
            {
                // The predecessor is not any context's current source; not this manager's
                // rollover to make.
                continue;
            }

            if (!string.Equals(context.AccountStableId, candidate.AccountStableId, StringComparison.Ordinal))
            {
                RecordDecisionLocked($"Rollover rejected for context {context.ContextId}: account mismatch.");
                continue;
            }

            if (!candidatesById.TryGetValue(group.Key, out var predecessorCandidate)
                || predecessorCandidate.ActivityState is not (LogSourceActivityState.Inactive or LogSourceActivityState.Unavailable))
            {
                // Old source still growing, or evidence is missing/incomplete: premature, do not
                // guess.
                RecordDecisionLocked($"Rollover rejected for context {context.ContextId}: predecessor still active or evidence incomplete.");
                continue;
            }

            context.PreviousSourceId = context.CurrentSourceId;
            context.CurrentSourceId = candidate.SourceId;
            context.AccountDisplayName = candidate.AccountDisplayName;
            context.SourceAssignedAt = now;
            context.StartupRecoveryStartOffset = 0;
            ClearStartupRecoveryPredecessorLocked(context);
            context.LastSourceTransitionAt = now;
            context.LastSourceTransitionReason = MonitoringContextChangeReason.AutomaticRollover;
            context.SourceLostAt = null;
            context.LastHandledTruncationSourceId = null;
            context.LastHandledTruncationObservedAt = null;
            AdvanceSourceBindingLocked(context, MonitoringSourceTransitionKind.AutomaticRollover);

            if (context.State == MonitoringContextState.RuntimeSuspended)
            {
                context.PreviousStateBeforeSuspension = MonitoringContextState.Ready;
            }
            else
            {
                context.State = MonitoringContextState.Ready;
                context.LastStateChangedAt = now;
            }

            RecordDecisionLocked($"Context {context.ContextId} silently rolled over to a new same-account source.");
        }
    }

    private void InvalidateStaleOffersLocked(Dictionary<string, LogSourceCandidate> candidatesById)
    {
        foreach (var sourceKey in _offers.Keys.ToList())
        {
            var offer = _offers[sourceKey];
            if (offer.State != MonitoringSourceOfferState.Pending)
            {
                continue;
            }

            if (IsSourceOwnedLocked(offer.SourceId))
            {
                offer.State = MonitoringSourceOfferState.ClaimedElsewhere;
                RecordDecisionLocked($"Offer for {DescribeSource(offer.SourceId)} invalidated: claimed elsewhere.");
                _offers.Remove(sourceKey);
                continue;
            }

            if (!candidatesById.TryGetValue(sourceKey, out var candidate)
                || candidate.ActivityState == LogSourceActivityState.Unavailable)
            {
                offer.State = MonitoringSourceOfferState.SourceUnavailable;
                RecordDecisionLocked($"Offer for {DescribeSource(offer.SourceId)} invalidated: source unavailable.");
                _offers.Remove(sourceKey);
            }
        }
    }

    /// <summary>
    /// Retires the current account context when the sole running Homecoming process begins writing
    /// to a different account's log with strictly newer growth evidence.
    /// </summary>
    private void TryApplySameProcessAccountHandoffsLocked(LogActivitySnapshot logSnapshot, DateTimeOffset now)
    {
        if (!_lastKnownRuntimeAvailable
            || !TryGetUnambiguousProcessInstanceLocked(out var processInstance))
        {
            return;
        }

        var boundContexts = _contexts.Values
            .Where(context =>
                context.State != MonitoringContextState.Stopped
                && context.ProcessInstance?.Equals(processInstance) == true)
            .ToList();

        if (boundContexts.Count != 1
            || string.IsNullOrWhiteSpace(boundContexts[0].AccountStableId))
        {
            return;
        }

        var existingContext = boundContexts[0];
        var handoffCandidates = logSnapshot.Candidates
            .Where(candidate =>
                candidate.ActivityState == LogSourceActivityState.Growing
                && !IsSourceOwnedLocked(candidate.SourceId)
                && !string.Equals(
                    candidate.AccountStableId,
                    existingContext.AccountStableId,
                    StringComparison.Ordinal))
            .GroupBy(candidate => candidate.AccountStableId, StringComparer.Ordinal)
            .Where(group => group.Count() == 1)
            .Select(group => group.First())
            .Where(candidate => IsQualifyingSameProcessHandoffTargetLocked(candidate, existingContext))
            .ToList();

        if (handoffCandidates.Count != 1)
        {
            return;
        }

        ApplySameProcessAccountHandoffLocked(existingContext, handoffCandidates[0], now);
    }

    private bool IsQualifyingSameProcessHandoffTargetLocked(
        LogSourceCandidate candidate,
        MutableContext existingContext)
    {
        if (existingContext.CurrentSourceId is null
            || candidate.LastGrowthAt is not { } targetGrowth)
        {
            return false;
        }

        var existingSource = _lastLogActivitySnapshot.Candidates
            .FirstOrDefault(item => item.SourceId == existingContext.CurrentSourceId);

        if (existingSource?.LastGrowthAt is { } existingGrowth
            && targetGrowth <= existingGrowth)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// When exactly one Homecoming process is running, at most one account context may remain
    /// current. This catches the live login-switch case where Account B was auto-enrolled before
    /// the unclaimed-source handoff could retire Account A, leaving two contexts on one process.
    /// </summary>
    private void EnforceSingleProcessAccountOwnershipLocked(LogActivitySnapshot logSnapshot, DateTimeOffset now)
    {
        if (!_lastKnownRuntimeAvailable || !TryGetUnambiguousProcessInstanceLocked(out _))
        {
            return;
        }

        var activeContexts = _contexts.Values
            .Where(context =>
                context.State != MonitoringContextState.Stopped
                && !string.IsNullOrWhiteSpace(context.AccountStableId))
            .ToList();

        if (activeContexts.Count <= 1)
        {
            return;
        }

        var distinctAccounts = activeContexts
            .Select(context => context.AccountStableId!)
            .Distinct(StringComparer.Ordinal)
            .Count();
        if (distinctAccounts <= 1)
        {
            return;
        }

        if (!TrySelectDominantAccountContextLocked(activeContexts, logSnapshot, out var dominantContext))
        {
            return;
        }

        var dominantGrowth = GetAccountActivityEvidence(dominantContext, logSnapshot).LastGrowthAt ?? now;

        foreach (var context in activeContexts)
        {
            if (context.ContextId == dominantContext.ContextId)
            {
                continue;
            }

            ApplySameProcessAccountHandoffLocked(
                context,
                dominantGrowth,
                dominantContext.AccountStableId!,
                now);
        }

        TryBindProcessInstanceLocked(dominantContext);
    }

    private bool TrySelectDominantAccountContextLocked(
        IReadOnlyList<MutableContext> activeContexts,
        LogActivitySnapshot logSnapshot,
        out MutableContext dominantContext)
    {
        var ranked = activeContexts
            .Select(context => (Context: context, Evidence: GetAccountActivityEvidence(context, logSnapshot)))
            .OrderByDescending(entry => entry.Evidence.IsGrowing)
            .ThenByDescending(entry => entry.Evidence.LastGrowthAt ?? DateTimeOffset.MinValue)
            .ThenByDescending(entry => entry.Context.LastStateChangedAt)
            .ToList();

        var best = ranked[0];
        if (!best.Evidence.IsGrowing || best.Evidence.LastGrowthAt is not { } bestGrowth)
        {
            dominantContext = null!;
            return false;
        }

        foreach (var challenger in ranked.Skip(1))
        {
            if (challenger.Evidence.IsGrowing
                && challenger.Evidence.LastGrowthAt is { } challengerGrowth
                && challengerGrowth >= bestGrowth)
            {
                dominantContext = null!;
                return false;
            }
        }

        var hasRetirableAccount = ranked.Skip(1).Any(entry =>
            !entry.Evidence.IsGrowing
            || entry.Evidence.LastGrowthAt is not { } growth
            || bestGrowth > growth);

        if (!hasRetirableAccount)
        {
            dominantContext = null!;
            return false;
        }

        dominantContext = best.Context;
        return true;
    }

    private AccountActivityEvidence GetAccountActivityEvidence(
        MutableContext context,
        LogActivitySnapshot logSnapshot)
    {
        if (context.CurrentSourceId is null)
        {
            return AccountActivityEvidence.None;
        }

        var source = logSnapshot.Candidates.FirstOrDefault(candidate => candidate.SourceId == context.CurrentSourceId);
        if (source is null)
        {
            return AccountActivityEvidence.None;
        }

        return new AccountActivityEvidence(
            source.ActivityState == LogSourceActivityState.Growing,
            source.LastGrowthAt);
    }

    private readonly record struct AccountActivityEvidence(bool IsGrowing, DateTimeOffset? LastGrowthAt)
    {
        public static AccountActivityEvidence None => new(false, null);
    }

    private void ApplySameProcessAccountHandoffLocked(
        MutableContext existingContext,
        LogSourceCandidate targetCandidate,
        DateTimeOffset now)
    {
        ApplySameProcessAccountHandoffLocked(
            existingContext,
            targetCandidate.LastGrowthAt ?? now,
            targetCandidate.AccountStableId,
            now);
    }

    private void ApplySameProcessAccountHandoffLocked(
        MutableContext existingContext,
        DateTimeOffset enrollmentCutoff,
        string successorAccountStableId,
        DateTimeOffset now)
    {
        _logActivityEnrollmentCutoffAt = enrollmentCutoff;

        RetireContextForSameProcessAccountHandoffLocked(existingContext.ContextId, now);
        _contexts.Remove(existingContext.ContextId);

        RecordDecisionLocked(
            $"Same-process account handoff: retired {existingContext.AccountStableId} " +
            $"for {successorAccountStableId} on process {existingContext.ProcessInstance?.ProcessId ?? 0}.");
    }

    private void RetireContextForSameProcessAccountHandoffLocked(
        MonitoringContextId contextId,
        DateTimeOffset now)
    {
        if (!_contexts.TryGetValue(contextId, out var context))
        {
            return;
        }

        if (context.CurrentSourceId is not null)
        {
            context.PreviousSourceId = context.CurrentSourceId;
            context.CurrentSourceId = null;
            ClearStartupRecoveryPredecessorLocked(context);
            context.LastSourceTransitionAt = now;
            context.LastSourceTransitionReason = MonitoringContextChangeReason.SameProcessAccountHandoffRetired;
            context.SourceLostAt = null;
            AdvanceSourceBindingLocked(context, MonitoringSourceTransitionKind.ContextRemoved);
        }

        context.State = MonitoringContextState.Stopped;
        context.LastStateChangedAt = now;
        context.SuspendedAt = null;
        context.PreviousStateBeforeSuspension = null;
    }

    /// <summary>
    /// Enrolls every unambiguous active source automatically, one context per account. Monitoring
    /// is not gated on user acceptance: a second Homecoming client writing to its own account log
    /// becomes its own monitoring context as soon as that log is observed growing (§3.6.6).
    /// </summary>
    /// <remarks>
    /// Offers remain only where attribution is genuinely ambiguous, so no source is ever guessed
    /// onto the wrong context.
    /// </remarks>
    private void CreateOrClaimForUnclaimedGrowingSourcesLocked(LogActivitySnapshot logSnapshot, DateTimeOffset now)
    {
        var accountGroups = logSnapshot.Candidates
            .Where(c =>
                c.ActivityState == LogSourceActivityState.Growing
                && !IsSourceOwnedLocked(c.SourceId)
                && !_exitedProcessSourceSuppressions.Contains(c.SourceId.Value)
                && HasObservedGrowthSinceRuntimeGenerationLocked(c))
            .GroupBy(c => c.AccountStableId, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal);

        foreach (var accountGroup in accountGroups)
        {
            var candidates = accountGroup
                .OrderBy(candidate => candidate.SourceId.Value, StringComparer.Ordinal)
                .ToList();

            // A further growing source under an already-monitored account cannot be attributed
            // without guessing: it may be an in-progress rollover or a second client sharing one
            // account, and the source-ownership invariant forbids both (§3.6.8).
            var accountAlreadyMonitored = _contexts.Values.Any(context =>
                context.State != MonitoringContextState.Stopped
                && string.Equals(context.AccountStableId, accountGroup.Key, StringComparison.Ordinal));

            if (!accountAlreadyMonitored && candidates.Count == 1 && _lastKnownRuntimeAvailable)
            {
                AutoCreateContextLocked(candidates[0], now);
                continue;
            }

            var offerReason = accountAlreadyMonitored
                ? MonitoringSourceOfferReason.AmbiguousAccountSource
                : MonitoringSourceOfferReason.AmbiguousInitialSelection;

            foreach (var candidate in candidates)
            {
                if (!HasObservedGrowthSinceRuntimeGenerationLocked(candidate)
                    || _offers.ContainsKey(candidate.SourceId.Value)
                    || _declineSuppressions.ContainsKey(candidate.SourceId.Value))
                {
                    continue;
                }

                CreateOfferLocked(candidate, now, offerReason);
            }
        }
    }

    /// <summary>
    /// Automatic enrollment requires growth observed after the current process-instance cutoff.
    /// Known accounts with stale log activity from a prior process instance must not become active sessions.
    /// </summary>
    private bool HasObservedGrowthSinceRuntimeGenerationLocked(LogSourceCandidate candidate)
    {
        if (_logActivityEnrollmentCutoffAt is null)
        {
            return true;
        }

        return candidate.LastGrowthAt is { } lastGrowth
            && lastGrowth >= _logActivityEnrollmentCutoffAt;
    }

    private void AutoCreateContextLocked(LogSourceCandidate candidate, DateTimeOffset now)
    {
        var contextId = MonitoringContextId.CreateNew();
        var context = new MutableContext
        {
            ContextId = contextId,
            AccountStableId = candidate.AccountStableId,
            AccountDisplayName = candidate.AccountDisplayName,
            CurrentSourceId = candidate.SourceId,
            CreatedAt = now,
            LastStateChangedAt = now,
            SourceAssignedAt = now,
            StartupRecoveryStartOffset = GetStartupRecoveryStartOffsetLocked(candidate.SourceId),
            LastSourceTransitionAt = now,
            LastSourceTransitionReason = MonitoringContextChangeReason.AutomaticUnambiguousSelection,
            SourceBindingGeneration = 1,
            LastSourceBindingTransitionKind = MonitoringSourceTransitionKind.SourceAssigned,
            State = MonitoringContextState.Ready
        };

        ApplyStartupRecoveryPredecessorLocked(context, candidate);

        _contexts[contextId] = context;
        TryBindProcessInstanceLocked(context);
        RecordDecisionLocked($"Automatically created context {contextId} for unambiguous growing source {DescribeSource(candidate.SourceId)}.");
    }

    private void ReconcileProcessInstanceBindingsLocked()
    {
        foreach (var context in _contexts.Values)
        {
            if (context.State == MonitoringContextState.Stopped)
            {
                continue;
            }

            if (context.ProcessInstance is { } previous)
            {
                var refinement = _runtimeService.RunningClients.SingleOrDefault(
                    current => current.IsMetadataRefinementOf(previous));
                if (refinement is not null)
                {
                    context.ProcessInstance = refinement;
                }
            }

            TryBindProcessInstanceLocked(context);
        }
    }

    /// <summary>
    /// Binds <paramref name="context"/> to the sole running Homecoming process when attribution
    /// is unambiguous. Existing proven bindings are preserved and never reassigned.
    /// </summary>
    private void TryBindProcessInstanceLocked(MutableContext context)
    {
        if (!TryGetUnambiguousProcessInstanceLocked(out var processInstance))
        {
            return;
        }

        context.ProcessInstance ??= processInstance;
    }

    private bool TryGetUnambiguousProcessInstanceLocked(out HomecomingProcessInstance processInstance)
    {
        var runningClients = _runtimeService.RunningClients;
        if (runningClients.Count == 1)
        {
            processInstance = runningClients[0];
            return true;
        }

        processInstance = default!;
        return false;
    }

    private void CreateOfferLocked(LogSourceCandidate candidate, DateTimeOffset now, MonitoringSourceOfferReason reason)
    {
        var offer = new MutableOffer
        {
            OfferId = MonitoringSourceOfferId.CreateNew(),
            SourceId = candidate.SourceId,
            AccountStableId = candidate.AccountStableId,
            AccountDisplayName = candidate.AccountDisplayName,
            DetectedAt = now,
            State = MonitoringSourceOfferState.Pending,
            Reason = reason
        };

        _offers[candidate.SourceId.Value] = offer;
        RecordDecisionLocked($"Created {reason} offer for {DescribeSource(candidate.SourceId)}.");
    }

    private void InvalidateOfferIfPendingLocked(LogSourceId sourceId, MonitoringSourceOfferState state)
    {
        if (_offers.TryGetValue(sourceId.Value, out var offer) && offer.State == MonitoringSourceOfferState.Pending)
        {
            offer.State = state;
            _offers.Remove(sourceId.Value);
        }
    }

    private void RecordDecisionLocked(string message)
    {
        _recentDecisions.Add(message);
        if (_recentDecisions.Count > MaxRecentDecisions)
        {
            _recentDecisions.RemoveAt(0);
        }
    }

    private static string DescribeSource(LogSourceId sourceId) =>
        $"{sourceId.AccountDisplayName}/{sourceId.Value[..Math.Min(8, sourceId.Value.Length)]}";

    private bool TryPublishLocked(DateTimeOffset now, out MonitoringSessionManagerSnapshot snapshot)
    {
        var unclaimedGrowingSourceCount = _lastLogActivitySnapshot.Candidates.Count(c =>
            c.ActivityState == LogSourceActivityState.Growing && !IsSourceOwnedLocked(c.SourceId));

        var candidate = MonitoringSessionManagerSnapshot.Create(
            _contexts.Values.Select(context => context.ToSnapshot()),
            _offers.Values
                .Where(offer => offer.State == MonitoringSourceOfferState.Pending)
                .Select(offer => offer.ToSnapshot()),
            unclaimedGrowingSourceCount,
            _declineSuppressions.Count,
            now,
            _revision + 1);

        WriteContextDiagnosticsLocked(_snapshot, candidate);

        if (candidate.IsSemanticallyEquivalentTo(_snapshot))
        {
            snapshot = _snapshot;
            return false;
        }

        _revision++;
        _snapshot = candidate;
        snapshot = candidate;
        return true;
    }

    private void WriteContextDiagnosticsLocked(
        MonitoringSessionManagerSnapshot previous,
        MonitoringSessionManagerSnapshot next)
    {
        if (_diagnosticLog is null)
        {
            return;
        }

        var events = new List<DiagnosticEvent>();
        var captureSnapshot = false;
        var snapshotReason = string.Empty;
        var previousById = previous.Contexts.ToDictionary(context => context.ContextId);
        var nextById = next.Contexts.ToDictionary(context => context.ContextId);

        foreach (var context in next.Contexts.OrderBy(item => item.ContextId.ToString(), StringComparer.Ordinal))
        {
            if (!previousById.TryGetValue(context.ContextId, out var prior))
            {
                events.Add(new MonitoringContextCreatedDiagnosticEvent
                {
                    ContextId = context.ContextId.ToString(),
                    AccountStableId = context.AccountStableId,
                    SourceId = context.CurrentSourceId?.Value,
                    SourceFileName = context.CurrentSourceId?.FileName,
                    State = context.State,
                    Reason = ContextCreationReason(context)
                });

                if (context.AccountStableId is not null)
                {
                    events.Add(CreateAccountBindingEvent(null, context));
                }

                if (context.CurrentSourceId is not null)
                {
                    events.Add(CreateSourceBindingEvent(null, context));
                }

                if (context.ProcessInstance is not null)
                {
                    events.Add(CreateProcessBindingEvent(null, context));
                }

                captureSnapshot = true;
                snapshotReason = "MonitoringContextCreated";
                continue;
            }

            if (prior.State != context.State)
            {
                events.Add(new MonitoringContextStateChangedDiagnosticEvent
                {
                    ContextId = context.ContextId.ToString(),
                    PreviousState = prior.State,
                    NextState = context.State,
                    Reason = ContextStateChangeReason(prior, context)
                });

                if (context.State == MonitoringContextState.Stopped)
                {
                    captureSnapshot = true;
                    snapshotReason = "MonitoringContextRetired";
                }
            }

            if (!string.Equals(prior.AccountStableId, context.AccountStableId, StringComparison.Ordinal))
            {
                events.Add(CreateAccountBindingEvent(prior, context));
            }

            if (prior.CurrentSourceId != context.CurrentSourceId)
            {
                events.Add(CreateSourceBindingEvent(prior, context));
            }

            if (!Equals(prior.ProcessInstance, context.ProcessInstance))
            {
                events.Add(CreateProcessBindingEvent(prior, context));
                captureSnapshot = true;
                snapshotReason = "MonitoringProcessAssociationChanged";
            }
        }

        foreach (var context in previous.Contexts.OrderBy(item => item.ContextId.ToString(), StringComparer.Ordinal))
        {
            if (nextById.ContainsKey(context.ContextId))
            {
                continue;
            }

            events.Add(new MonitoringContextRetiredDiagnosticEvent
            {
                ContextId = context.ContextId.ToString(),
                AccountStableId = context.AccountStableId,
                SourceId = context.CurrentSourceId?.Value,
                Reason = "ContextRemoved"
            });
            _diagnosticUnavailableProcessBindings.Remove(context.ContextId);
            captureSnapshot = true;
            snapshotReason = "MonitoringContextRetired";
        }

        foreach (var context in next.Contexts.OrderBy(item => item.ContextId.ToString(), StringComparer.Ordinal))
        {
            var unavailableReason = GetProcessBindingUnavailableReason(context);
            if (unavailableReason is null)
            {
                _diagnosticUnavailableProcessBindings.Remove(context.ContextId);
                continue;
            }

            if (_diagnosticUnavailableProcessBindings.TryGetValue(context.ContextId, out var previousReason)
                && previousReason == unavailableReason)
            {
                continue;
            }

            _diagnosticUnavailableProcessBindings[context.ContextId] = unavailableReason.Value;
            events.Add(new MonitoringContextProcessBindingUnavailableDiagnosticEvent
            {
                ContextId = context.ContextId.ToString(),
                Reason = unavailableReason.Value
            });
        }

        if (captureSnapshot)
        {
            events.Add(CreateStateSnapshot(snapshotReason, next));
        }

        foreach (var diagnosticEvent in events)
        {
            TryWriteDiagnostic(diagnosticEvent);
        }
    }

    private MonitoringProcessBindingUnavailableReason? GetProcessBindingUnavailableReason(
        MonitoringContextSnapshot context)
    {
        if (context.State == MonitoringContextState.Stopped || context.ProcessInstance is not null)
        {
            return null;
        }

        if (_runtimeService.RunningClients.Count > 1)
        {
            return MonitoringProcessBindingUnavailableReason.MultipleRuntimeClients;
        }

        if (!_lastKnownRuntimeAvailable || _runtimeService.RunningClients.Count == 0)
        {
            return MonitoringProcessBindingUnavailableReason.RuntimeUnavailable;
        }

        return MonitoringProcessBindingUnavailableReason.NoProvenAssociation;
    }

    private DiagnosticsStateSnapshotCapturedDiagnosticEvent CreateStateSnapshot(
        string reason,
        MonitoringSessionManagerSnapshot monitoringSnapshot) =>
        new()
        {
            Reason = reason,
            RunningClients = _runtimeService.RunningClients
                .Select(ToDiagnosticRuntimeClient)
                .ToArray(),
            LogSources = _lastLogActivitySnapshot.Candidates
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
            MonitoringContexts = monitoringSnapshot.Contexts
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
                .ToArray()
        };

    private static MonitoringContextAccountBoundDiagnosticEvent CreateAccountBindingEvent(
        MonitoringContextSnapshot? previous,
        MonitoringContextSnapshot next) =>
        new()
        {
            ContextId = next.ContextId.ToString(),
            PreviousAccountStableId = previous?.AccountStableId,
            NextAccountStableId = next.AccountStableId,
            Reason = next.LastSourceTransitionReason?.ToString() ?? "ContextAccountEstablished"
        };

    private static MonitoringContextSourceBindingChangedDiagnosticEvent CreateSourceBindingEvent(
        MonitoringContextSnapshot? previous,
        MonitoringContextSnapshot next) =>
        new()
        {
            ContextId = next.ContextId.ToString(),
            PreviousSourceId = previous?.CurrentSourceId?.Value,
            NextSourceId = next.CurrentSourceId?.Value,
            PreviousSourceFileName = previous?.CurrentSourceId?.FileName,
            NextSourceFileName = next.CurrentSourceId?.FileName,
            BindingGeneration = next.SourceBindingGeneration,
            Reason = next.LastSourceTransitionReason?.ToString()
                ?? next.LastSourceBindingTransitionKind.ToString()
        };

    private static MonitoringContextProcessBindingChangedDiagnosticEvent CreateProcessBindingEvent(
        MonitoringContextSnapshot? previous,
        MonitoringContextSnapshot next) =>
        new()
        {
            ContextId = next.ContextId.ToString(),
            PreviousProcess = previous?.ProcessInstance is null
                ? null
                : ToDiagnosticRuntimeClient(previous.ProcessInstance),
            NextProcess = next.ProcessInstance is null
                ? null
                : ToDiagnosticRuntimeClient(next.ProcessInstance),
            Reason = previous?.ProcessInstance is not null && next.ProcessInstance is not null
                ? "RuntimeProcessIdentityRefined"
                : "UnambiguousRuntimeClient"
        };

    private static DiagnosticRuntimeClient ToDiagnosticRuntimeClient(HomecomingProcessInstance process) =>
        new()
        {
            ProcessId = process.ProcessId,
            ProcessStartTime = process.ProcessStartTime.ToUniversalTime()
        };

    private static string ContextCreationReason(MonitoringContextSnapshot context) =>
        context.LastSourceTransitionReason?.ToString() ?? "ManualContextCreation";

    private static string ContextStateChangeReason(
        MonitoringContextSnapshot previous,
        MonitoringContextSnapshot next)
    {
        if (next.State == MonitoringContextState.RuntimeSuspended)
        {
            return "RuntimeUnavailable";
        }

        if (previous.State == MonitoringContextState.RuntimeSuspended)
        {
            return "RuntimeAvailable";
        }

        return next.LastSourceTransitionReason?.ToString()
            ?? (next.State == MonitoringContextState.Stopped ? "ContextRemoved" : "StateReconciled");
    }

    private void TryWriteDiagnostic(DiagnosticEvent diagnosticEvent)
    {
        try
        {
            _diagnosticLog?.Write(diagnosticEvent);
        }
        catch
        {
            // Diagnostics are side-effect-only and must never influence monitoring decisions.
        }
    }

    private static void AdvanceSourceBindingLocked(
        MutableContext context,
        MonitoringSourceTransitionKind transitionKind)
    {
        context.SourceBindingGeneration++;
        context.LastSourceBindingTransitionKind = transitionKind;
    }

    private bool IsSourceOwnedLocked(LogSourceId sourceId) =>
        _contexts.Values.Any(context =>
            context.State != MonitoringContextState.Stopped
            && context.CurrentSourceId == sourceId);

    private void CaptureRuntimeExitSourceOffsetsLocked()
    {
        foreach (var context in _contexts.Values)
        {
            if (context.CurrentSourceId is not { } sourceId)
            {
                continue;
            }

            var candidate = _lastLogActivitySnapshot.Candidates
                .FirstOrDefault(item => item.SourceId == sourceId);
            if (candidate is null)
            {
                continue;
            }

            var exitOffset = candidate.Length;
            try
            {
                if (File.Exists(sourceId.FilePath))
                {
                    exitOffset = new FileInfo(sourceId.FilePath).Length;
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // The last authoritative Log Activity observation remains the best available
                // fallback when metadata cannot be refreshed at the process-exit boundary.
            }

            _runtimeExitSourceOffsets[sourceId.Value] = Math.Max(0, exitOffset);
        }
    }

    private long GetStartupRecoveryStartOffsetLocked(LogSourceId sourceId) =>
        _runtimeExitSourceOffsets.TryGetValue(sourceId.Value, out var offset)
            ? offset
            : 0;

    private void ApplyStartupRecoveryPredecessorLocked(
        MutableContext context,
        LogSourceCandidate currentCandidate)
    {
        ClearStartupRecoveryPredecessorLocked(context);

        if (!currentCandidate.IsCurrentDailyFile
            || !TryGetUnambiguousProcessInstanceLocked(out var processInstance))
        {
            return;
        }

        var processStartDate = DateOnly.FromDateTime(processInstance.ProcessStartTime.DateTime);
        if (processStartDate.AddDays(1) != currentCandidate.LogDate)
        {
            return;
        }

        var priorDate = currentCandidate.LogDate.AddDays(-1);
        var predecessors = _lastLogActivitySnapshot.Candidates
            .Where(candidate =>
                candidate.Exists
                && candidate.SourceId != currentCandidate.SourceId
                && string.Equals(
                    candidate.AccountStableId,
                    currentCandidate.AccountStableId,
                    StringComparison.Ordinal)
                && candidate.LogDate == priorDate)
            .ToList();

        if (currentCandidate.RolloverPredecessorSourceId is { } explicitPredecessorId)
        {
            predecessors = predecessors
                .Where(candidate => string.Equals(
                    candidate.SourceId.Value,
                    explicitPredecessorId,
                    StringComparison.Ordinal))
                .ToList();
        }

        if (predecessors.Count != 1)
        {
            return;
        }

        var predecessor = predecessors[0];
        context.StartupRecoveryPredecessorSourceId = predecessor.SourceId;
        context.StartupRecoveryPredecessorStartOffset =
            GetStartupRecoveryStartOffsetLocked(predecessor.SourceId);
    }

    private static void ClearStartupRecoveryPredecessorLocked(MutableContext context)
    {
        context.StartupRecoveryPredecessorSourceId = null;
        context.StartupRecoveryPredecessorStartOffset = 0;
    }

    private static bool IsRuntimeStatusAvailable(GameRuntimeStatus status) =>
        status == GameRuntimeStatus.Running;

    private sealed class DeclineSuppression
    {
        public bool SeenNonGrowingSinceDecline { get; set; }
    }

    private sealed class MutableOffer
    {
        public required MonitoringSourceOfferId OfferId { get; init; }

        public required LogSourceId SourceId { get; init; }

        public required string AccountStableId { get; init; }

        public required string AccountDisplayName { get; init; }

        public required DateTimeOffset DetectedAt { get; init; }

        public MonitoringSourceOfferState State { get; set; }

        public required MonitoringSourceOfferReason Reason { get; init; }

        public MonitoringSourceOffer ToSnapshot() => new()
        {
            OfferId = OfferId,
            SourceId = SourceId,
            AccountStableId = AccountStableId,
            AccountDisplayName = AccountDisplayName,
            DetectedAt = DetectedAt,
            State = State,
            Reason = Reason
        };
    }

    private sealed class MutableContext
    {
        public required MonitoringContextId ContextId { get; init; }

        public MonitoringContextState State { get; set; }

        public string? AccountStableId { get; set; }

        public string? AccountDisplayName { get; set; }

        public LogSourceId? CurrentSourceId { get; set; }

        public LogSourceId? PreviousSourceId { get; set; }

        public long SourceBindingGeneration { get; set; }

        public MonitoringSourceTransitionKind LastSourceBindingTransitionKind { get; set; }

        public required DateTimeOffset CreatedAt { get; init; }

        public DateTimeOffset LastStateChangedAt { get; set; }

        public DateTimeOffset? SuspendedAt { get; set; }

        public MonitoringContextState? PreviousStateBeforeSuspension { get; set; }

        public DateTimeOffset? SourceAssignedAt { get; set; }

        public long StartupRecoveryStartOffset { get; set; }

        public LogSourceId? StartupRecoveryPredecessorSourceId { get; set; }

        public long StartupRecoveryPredecessorStartOffset { get; set; }

        public DateTimeOffset? LastSourceTransitionAt { get; set; }

        public MonitoringContextChangeReason? LastSourceTransitionReason { get; set; }

        public DateTimeOffset? SourceLostAt { get; set; }

        public string? LastHandledTruncationSourceId { get; set; }

        public DateTimeOffset? LastHandledTruncationObservedAt { get; set; }

        public HomecomingProcessInstance? ProcessInstance { get; set; }

        public MonitoringContextSnapshot ToSnapshot() => new()
        {
            ContextId = ContextId,
            State = State,
            AccountStableId = AccountStableId,
            AccountDisplayName = AccountDisplayName,
            CurrentSourceId = CurrentSourceId,
            PreviousSourceId = PreviousSourceId,
            SourceBindingGeneration = SourceBindingGeneration,
            LastSourceBindingTransitionKind = LastSourceBindingTransitionKind,
            CreatedAt = CreatedAt,
            LastStateChangedAt = LastStateChangedAt,
            SuspendedAt = SuspendedAt,
            PreviousStateBeforeSuspension = PreviousStateBeforeSuspension,
            SourceAssignedAt = SourceAssignedAt,
            StartupRecoveryStartOffset = StartupRecoveryStartOffset,
            StartupRecoveryPredecessorSourceId = StartupRecoveryPredecessorSourceId,
            StartupRecoveryPredecessorStartOffset = StartupRecoveryPredecessorStartOffset,
            LastSourceTransitionAt = LastSourceTransitionAt,
            LastSourceTransitionReason = LastSourceTransitionReason,
            SourceLostAt = SourceLostAt,
            ProcessInstance = ProcessInstance
        };
    }
}
