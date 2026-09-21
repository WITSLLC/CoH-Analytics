using System.Runtime.InteropServices;
using System.Text;
using CoHAnalytics.Models;
using CoHAnalytics.Services.Diagnostics;

namespace CoHAnalytics.Services;

/// <summary>
/// Incrementally tails one manager-assigned source at a time for a single monitoring context.
/// Source selection remains entirely owned by <see cref="IMonitoringSessionManager"/>.
/// </summary>
public sealed class ParserWorker : IParserWorker
{
    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private readonly ParserManagerOptions _options;
    private readonly IDiagnosticLog? _diagnosticLog;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _snapshotLock = new();
    private readonly List<byte> _lineBuffer = [];
    private readonly List<(byte Value, long Offset)> _bomProbe = [];
    private readonly List<ParserSourceSegmentSnapshot> _recentSegments = [];
    private CancellationTokenSource? _lifetimeCancellation;
    private Task? _readLoop;
    private FileStream? _stream;
    private LogSourceId? _boundSource;
    private MutableSegment? _activeSegment;
    private ParserWorkerState _state = ParserWorkerState.Created;
    private MonitoringSourceTransitionKind _lastTransition;
    private long _appliedGeneration;
    private long _cursor;
    private long _startupRecoveryStartOffset;
    private LogSourceId? _startupRecoveryPredecessorSource;
    private long _startupRecoveryPredecessorStartOffset;
    private HomecomingProcessInstance? _processInstance;
    private long _lineStartOffset;
    private long _totalBytesRead;
    private long _totalLinesEmitted;
    private long _lastEventSequence;
    private DateTimeOffset? _lastEventAt;
    private bool _lineTooLarge;
    private bool _bomResolved = true;
    private StartPositionPolicy _pendingStartPolicy = StartPositionPolicy.AttachmentEnd;
    private string? _faultCode;
    private string? _faultMessage;
    private bool _started;
    private bool _disposed;
    private ParserWorkerSnapshot _current;

    public ParserWorker(
        MonitoringContextId contextId,
        ParserManagerOptions options,
        IDiagnosticLog? diagnosticLog = null)
    {
        ArgumentNullException.ThrowIfNull(contextId);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        ContextId = contextId;
        WorkerId = ParserWorkerId.CreateNew();
        _options = options;
        _diagnosticLog = diagnosticLog;
        _current = CreateSnapshotLocked();
    }

    public ParserWorkerId WorkerId { get; }

    public MonitoringContextId ContextId { get; }

    public ParserWorkerSnapshot Current
    {
        get
        {
            lock (_snapshotLock)
            {
                return _current;
            }
        }
    }

    public event EventHandler<ParserWorkerChangedEventArgs>? StateChanged;

    public event EventHandler<ParserRawEventAvailableEventArgs>? RawEventAvailable;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ParserWorkerSnapshot? changed = null;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_started)
            {
                return;
            }

            _started = true;
            _lifetimeCancellation = new CancellationTokenSource();
            _readLoop = RunReadLoopAsync(_lifetimeCancellation.Token);
            changed = UpdateSnapshotLocked();
        }
        finally
        {
            _gate.Release();
        }

        PublishState(changed);
    }

    public async Task ApplyContextAsync(
        MonitoringContextSnapshot context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.ContextId != ContextId)
        {
            throw new ArgumentException("The context does not belong to this parser worker.", nameof(context));
        }

        ParserWorkerSnapshot? changed = null;
        List<ParserRawEvent> startupEvents = [];
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_started || _state == ParserWorkerState.Stopped)
            {
                return;
            }

            if (_state == ParserWorkerState.Faulted)
            {
                return;
            }

            startupEvents = await ApplyContextLockedAsync(context, cancellationToken).ConfigureAwait(false);
            changed = UpdateSnapshotLocked();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            FaultLocked(FaultCode(exception), SafeMessage(exception), exception);
            changed = UpdateSnapshotLocked();
        }
        finally
        {
            _gate.Release();
        }

        PublishEvents(startupEvents);
        PublishState(changed);
    }

    public async Task FaultAsync(
        string code,
        string message,
        CancellationToken cancellationToken = default)
    {
        ParserWorkerSnapshot? changed = null;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_state is not ParserWorkerState.Stopped and not ParserWorkerState.Faulted)
            {
                FaultLocked(code, message);
                changed = UpdateSnapshotLocked();
            }
        }
        finally
        {
            _gate.Release();
        }

        PublishState(changed);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        CancellationTokenSource? lifetimeCancellation;
        Task? readLoop;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_state == ParserWorkerState.Stopped && !_started)
            {
                return;
            }

            lifetimeCancellation = _lifetimeCancellation;
            readLoop = _readLoop;
            lifetimeCancellation?.Cancel();
        }
        finally
        {
            _gate.Release();
        }

        if (readLoop is not null)
        {
            try
            {
                await readLoop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        ParserWorkerSnapshot? changed = null;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_state != ParserWorkerState.Stopped)
            {
                FinalizeActiveSegmentLocked(ParserSourceSegmentState.Completed);
                CloseStreamLocked();
                _state = ParserWorkerState.Stopped;
                changed = UpdateSnapshotLocked();
            }

            _started = false;
        }
        finally
        {
            _gate.Release();
        }

        PublishState(changed);
        lifetimeCancellation?.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await StopAsync().ConfigureAwait(false);
        _disposed = true;
        _gate.Dispose();
    }

    private async Task RunReadLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_options.PollInterval, _options.TimeProvider, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            List<ParserRawEvent> events = [];
            ParserWorkerSnapshot? changed = null;

            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_state is ParserWorkerState.WaitingForData or ParserWorkerState.Reading
                    && _stream is not null)
                {
                    await ReadAvailableLockedAsync(events, cancellationToken).ConfigureAwait(false);
                    changed = UpdateSnapshotLocked();
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DecoderFallbackException)
            {
                FaultLocked(FaultCode(exception), SafeMessage(exception), exception);
                changed = UpdateSnapshotLocked();
            }
            finally
            {
                _gate.Release();
            }

            PublishEvents(events);
            PublishState(changed);
        }
    }

    private async Task<List<ParserRawEvent>> ApplyContextLockedAsync(
        MonitoringContextSnapshot context,
        CancellationToken cancellationToken)
    {
        var startupEvents = new List<ParserRawEvent>();
        if (context.State == MonitoringContextState.Stopped
            || context.LastSourceBindingTransitionKind == MonitoringSourceTransitionKind.ContextRemoved)
        {
            if (context.SourceBindingGeneration > _appliedGeneration + 1 && _appliedGeneration != 0)
            {
                FaultLocked("binding_generation_jump", GenerationJumpMessage(context));
                return startupEvents;
            }

            if (context.SourceBindingGeneration > _appliedGeneration)
            {
                _appliedGeneration = context.SourceBindingGeneration;
                _lastTransition = MonitoringSourceTransitionKind.ContextRemoved;
            }

            FinalizeActiveSegmentLocked(ParserSourceSegmentState.Removed);
            CloseStreamLocked();
            _boundSource = null;
            _state = ParserWorkerState.Stopped;
            return startupEvents;
        }

        if (context.SourceBindingGeneration < _appliedGeneration)
        {
            FaultLocked(
                "binding_generation_regression",
                $"Context generation {context.SourceBindingGeneration} is behind applied generation {_appliedGeneration}.");
            return startupEvents;
        }

        if (context.SourceBindingGeneration > _appliedGeneration)
        {
            if (_appliedGeneration != 0 && context.SourceBindingGeneration != _appliedGeneration + 1)
            {
                FaultLocked("binding_generation_jump", GenerationJumpMessage(context));
                return startupEvents;
            }

            if (!ApplyBindingTransitionLocked(context))
            {
                return startupEvents;
            }
        }
        else if (_appliedGeneration != 0 && context.CurrentSourceId != _boundSource)
        {
            FaultLocked(
                "source_changed_without_generation",
                "The assigned source changed without a binding-generation transition.");
            return startupEvents;
        }

        switch (context.State)
        {
            case MonitoringContextState.RuntimeSuspended:
                CloseStreamLocked();
                _state = ParserWorkerState.Suspended;
                break;

            case MonitoringContextState.WaitingForSource:
                CloseStreamLocked();
                _state = ParserWorkerState.WaitingForSource;
                break;

            case MonitoringContextState.Ready when _boundSource is not null:
                startupEvents = await EnsureAttachedLockedAsync(cancellationToken).ConfigureAwait(false);
                _state = ParserWorkerState.WaitingForData;
                break;

            case MonitoringContextState.Error:
                FaultLocked("monitoring_context_error", "The owning monitoring context entered Error.");
                break;

            default:
                CloseStreamLocked();
                _state = ParserWorkerState.WaitingForSource;
                break;
        }

        return startupEvents;
    }

    private bool ApplyBindingTransitionLocked(MonitoringContextSnapshot context)
    {
        var transition = context.LastSourceBindingTransitionKind;
        var source = context.CurrentSourceId;

        if (transition == MonitoringSourceTransitionKind.None)
        {
            FaultLocked("missing_binding_transition", "A binding generation advanced without a transition kind.");
            return false;
        }

        var requiresSource = transition is
            MonitoringSourceTransitionKind.SourceAssigned
            or MonitoringSourceTransitionKind.SourceReclaimed
            or MonitoringSourceTransitionKind.AutomaticRollover
            or MonitoringSourceTransitionKind.TruncationReset
            or MonitoringSourceTransitionKind.SourceReplaced;

        if (requiresSource != (source is not null))
        {
            FaultLocked("invalid_binding_transition", "The transition kind and current source are inconsistent.");
            return false;
        }

        if (transition == MonitoringSourceTransitionKind.TruncationReset
            && _boundSource is not null
            && source != _boundSource)
        {
            FaultLocked("invalid_truncation_source", "A truncation reset changed the logical source identity.");
            return false;
        }

        var endState = transition switch
        {
            MonitoringSourceTransitionKind.AutomaticRollover => ParserSourceSegmentState.RolledOver,
            MonitoringSourceTransitionKind.TruncationReset => ParserSourceSegmentState.TruncationReset,
            MonitoringSourceTransitionKind.SourceReplaced => ParserSourceSegmentState.Replaced,
            MonitoringSourceTransitionKind.SourceReleased => ParserSourceSegmentState.Released,
            MonitoringSourceTransitionKind.ContextRemoved => ParserSourceSegmentState.Removed,
            _ => ParserSourceSegmentState.Completed
        };

        FinalizeActiveSegmentLocked(endState);
        CloseStreamLocked();
        ResetFramingLocked();

        _boundSource = source;
        _appliedGeneration = context.SourceBindingGeneration;
        _lastTransition = transition;
        _startupRecoveryStartOffset = Math.Max(0, context.StartupRecoveryStartOffset);
        _startupRecoveryPredecessorSource = context.StartupRecoveryPredecessorSourceId;
        _startupRecoveryPredecessorStartOffset =
            Math.Max(0, context.StartupRecoveryPredecessorStartOffset);
        _processInstance = context.ProcessInstance;

        // Reclaims intentionally follow initial-attachment policy: a non-empty existing file
        // begins at attachment-time EOF. Only manager-approved rollover/reset/replacement starts 0.
        _pendingStartPolicy = transition is
            MonitoringSourceTransitionKind.AutomaticRollover
            or MonitoringSourceTransitionKind.TruncationReset
            or MonitoringSourceTransitionKind.SourceReplaced
            ? StartPositionPolicy.Zero
            : StartPositionPolicy.AttachmentEnd;

        return true;
    }

    private async Task<List<ParserRawEvent>> EnsureAttachedLockedAsync(CancellationToken cancellationToken)
    {
        if (_stream is not null)
        {
            return [];
        }

        if (_boundSource is null)
        {
            throw new InvalidOperationException("Cannot attach without a manager-assigned source.");
        }

        var stream = new FileStream(
            _boundSource.FilePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            _options.ReadBufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        try
        {
            if (_activeSegment is null)
            {
                var startingOffset = _pendingStartPolicy == StartPositionPolicy.Zero ? 0 : stream.Length;
                var segmentId = ParserSourceSegmentId.CreateNew();
                var startupEvents = RecoverStartupIdentityEventsLocked(stream, startingOffset, segmentId);
                stream.Position = startingOffset;
                _cursor = startingOffset;
                _lineStartOffset = startingOffset;
                _bomResolved = startingOffset != 0;
                _activeSegment = new MutableSegment
                {
                    SourceSegmentId = segmentId,
                    ContextId = ContextId,
                    SourceId = _boundSource,
                    BindingGeneration = _appliedGeneration,
                    TransitionKind = _lastTransition,
                    StartedAt = _options.TimeProvider.GetUtcNow(),
                    StartingOffset = startingOffset,
                    EndingOffset = startingOffset
                };

                _stream = stream;
                if (startupEvents.Count > 0)
                {
                    _totalLinesEmitted += startupEvents.Count;
                }

                await Task.CompletedTask.ConfigureAwait(false);
                return startupEvents;
            }

            if (stream.Length < _cursor)
            {
                throw new IOException(
                    "The source length is below the preserved parser cursor without an approved reset transition.");
            }

            stream.Position = _cursor;
            _stream = stream;
            await Task.CompletedTask.ConfigureAwait(false);
            return [];
        }
        catch
        {
            await stream.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Scans backward from the attach offset for the welcome that opened the session already
    /// in progress.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The scan ends at the attach offset. A welcome opens a gameplay session, so the first
    /// welcome encountered when scanning backward from end-of-file is the most recent welcome in
    /// the file. The runtime identity boundary then determines whether that welcome is valid for
    /// this client lifetime.
    /// </para>
    /// <para>
    /// When no welcome exists anywhere before the attach offset, currency cannot be established,
    /// so nothing is emitted and identity stays unresolved.
    /// </para>
    /// </remarks>
    private List<ParserRawEvent> RecoverStartupIdentityEventsLocked(
        FileStream stream,
        long tailOffset,
        ParserSourceSegmentId sourceSegmentId)
    {
        if (_pendingStartPolicy != StartPositionPolicy.AttachmentEnd)
        {
            return [];
        }

        var observedAt = _options.TimeProvider.GetUtcNow();
        var welcome = tailOffset <= 0
            ? null
            : ParserStartupIdentityRecovery.FindMostRecentWelcomeBeforeOffset(
                stream,
                tailOffset,
                _options.ReadBufferSize,
                ContextId,
                _boundSource!,
                sourceSegmentId,
                _appliedGeneration,
                _lastTransition,
                observedAt,
                _options.MaximumLineBytes,
                ref _lastEventSequence);

        if (welcome is not null)
        {
            return welcome.SourceByteStart < _startupRecoveryStartOffset
                ? []
                : [welcome];
        }

        var predecessor = _startupRecoveryPredecessorSource;
        if (predecessor is null
            || string.IsNullOrWhiteSpace(predecessor.FilePath)
            || !File.Exists(predecessor.FilePath))
        {
            return [];
        }

        try
        {
            using var predecessorStream = new FileStream(
                predecessor.FilePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            if (predecessorStream.Length <= 0)
            {
                return [];
            }

            welcome = ParserStartupIdentityRecovery.FindMostRecentWelcomeBeforeOffset(
                predecessorStream,
                predecessorStream.Length,
                _options.ReadBufferSize,
                ContextId,
                predecessor,
                ParserSourceSegmentId.CreateNew(),
                _appliedGeneration,
                _lastTransition,
                observedAt,
                _options.MaximumLineBytes,
                ref _lastEventSequence);

            return welcome is null
                || welcome.SourceByteStart < _startupRecoveryPredecessorStartOffset
                || !ParserStartupIdentityRecovery.IsWelcomeWithinCurrentRuntime(
                    welcome,
                    _processInstance)
                ? []
                : [welcome];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The predecessor is optional recovery evidence. Failure to inspect it must not
            // prevent the worker from tailing the authoritative current source.
            return [];
        }
    }

    private async Task ReadAvailableLockedAsync(
        List<ParserRawEvent> events,
        CancellationToken cancellationToken)
    {
        if (_stream is null || _activeSegment is null)
        {
            return;
        }

        if (_stream.Length < _cursor)
        {
            throw new IOException(
                "The source length is below the parser cursor without an approved truncation reset.");
        }

        var buffer = new byte[_options.ReadBufferSize];
        var readAny = false;
        while (true)
        {
            _state = ParserWorkerState.Reading;
            var read = await _stream.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                _state = readAny ? ParserWorkerState.Reading : ParserWorkerState.WaitingForData;
                return;
            }

            readAny = true;

            for (var index = 0; index < read; index++)
            {
                var absoluteOffset = _cursor;
                ProcessByteLocked(buffer[index], absoluteOffset, events);
                _cursor++;
                _totalBytesRead++;
                _activeSegment.BytesRead++;
                _activeSegment.EndingOffset = _cursor;
            }
        }
    }

    private void ProcessByteLocked(byte value, long absoluteOffset, List<ParserRawEvent> events)
    {
        if (!_bomResolved)
        {
            _bomProbe.Add((value, absoluteOffset));
            var probeMatches = _bomProbe
                .Select((item, index) => item.Value == Utf8Bom[index])
                .All(match => match);

            if (probeMatches && _bomProbe.Count < Utf8Bom.Length)
            {
                return;
            }

            if (probeMatches)
            {
                _bomResolved = true;
                _lineStartOffset = absoluteOffset + 1;
                _bomProbe.Clear();
                return;
            }

            _bomResolved = true;
            foreach (var item in _bomProbe)
            {
                ProcessFramingByteLocked(item.Value, item.Offset, events);
            }

            _bomProbe.Clear();
            return;
        }

        ProcessFramingByteLocked(value, absoluteOffset, events);
    }

    private void ProcessFramingByteLocked(byte value, long absoluteOffset, List<ParserRawEvent> events)
    {
        if (value != (byte)'\n')
        {
            if (!_lineTooLarge)
            {
                if (_lineBuffer.Count < _options.MaximumLineBytes)
                {
                    _lineBuffer.Add(value);
                }
                else
                {
                    _lineBuffer.Clear();
                    _lineTooLarge = true;
                }
            }

            return;
        }

        string rawLine;
        ParserLineStatus status;
        if (_lineTooLarge)
        {
            rawLine = string.Empty;
            status = ParserLineStatus.TooLarge;
        }
        else
        {
            if (_lineBuffer.Count > 0 && _lineBuffer[^1] == (byte)'\r')
            {
                _lineBuffer.RemoveAt(_lineBuffer.Count - 1);
            }

            rawLine = StrictUtf8.GetString(CollectionsMarshal.AsSpan(_lineBuffer));
            status = ParserLineStatus.Complete;
        }

        _lastEventSequence++;
        _totalLinesEmitted++;
        _lastEventAt = _options.TimeProvider.GetUtcNow();
        // ObservedAt is application observation time; optional SourceTimestamp comes from classification.
        _activeSegment!.LinesEmitted++;
        events.Add(new ParserRawEvent
        {
            ContextId = ContextId,
            SourceId = _activeSegment.SourceId,
            SourceSegmentId = _activeSegment.SourceSegmentId,
            BindingGeneration = _activeSegment.BindingGeneration,
            SourceTransitionKind = _activeSegment.TransitionKind,
            Sequence = _lastEventSequence,
            ObservedAt = _lastEventAt.Value,
            RawLine = rawLine,
            SourceByteStart = _lineStartOffset,
            SourceByteEnd = absoluteOffset + 1,
            LineStatus = status
        });

        _lineBuffer.Clear();
        _lineTooLarge = false;
        _lineStartOffset = absoluteOffset + 1;
    }

    private void FinalizeActiveSegmentLocked(ParserSourceSegmentState state)
    {
        if (_activeSegment is null)
        {
            return;
        }

        var incomplete = PartialBufferByteCountLocked() > 0;
        _recentSegments.Add(_activeSegment.ToSnapshot(
            _options.TimeProvider.GetUtcNow(),
            state,
            incomplete));
        while (_recentSegments.Count > _options.MaximumRecentSegments)
        {
            _recentSegments.RemoveAt(0);
        }

        _activeSegment = null;
        ResetFramingLocked();
    }

    private void ResetFramingLocked()
    {
        _lineBuffer.Clear();
        _bomProbe.Clear();
        _lineTooLarge = false;
        _bomResolved = true;
        _lineStartOffset = 0;
    }

    private void FaultLocked(string code, string message, Exception? exception = null)
    {
        FinalizeActiveSegmentLocked(ParserSourceSegmentState.Faulted);
        CloseStreamLocked();
        _faultCode = code;
        _faultMessage = message;
        _state = ParserWorkerState.Faulted;
        TryWriteDiagnostic(new ParserWorkerFaultedDiagnosticEvent
        {
            WorkerId = WorkerId.ToString(),
            ContextId = ContextId.ToString(),
            ExceptionType = exception?.GetType().Name ?? "ParserWorkerFault",
            HResult = exception?.HResult ?? 0,
            FaultCode = code,
            Reason = code
        });
    }

    private void CloseStreamLocked()
    {
        _stream?.Dispose();
        _stream = null;
    }

    private ParserWorkerSnapshot? UpdateSnapshotLocked()
    {
        var candidate = CreateSnapshotLocked();
        lock (_snapshotLock)
        {
            if (SemanticallyEquivalent(_current, candidate))
            {
                return null;
            }

            _current = candidate;
            return candidate;
        }
    }

    private ParserWorkerSnapshot CreateSnapshotLocked()
    {
        var currentSegment = _activeSegment?.ToSnapshot(null, ParserSourceSegmentState.Active, false);
        return new ParserWorkerSnapshot
        {
            WorkerId = WorkerId,
            ContextId = ContextId,
            State = _state,
            CurrentSourceId = _boundSource,
            AppliedSourceBindingGeneration = _appliedGeneration,
            LastAppliedTransitionKind = _lastTransition,
            CurrentSegment = currentSegment,
            RecentSegments = [.. _recentSegments],
            Checkpoint = currentSegment is null
                ? null
                : new ParserReadCheckpoint
                {
                    SourceSegmentId = currentSegment.SourceSegmentId,
                    ByteOffset = _cursor,
                    PartialBufferByteCount = PartialBufferByteCountLocked(),
                    HasPendingBomProbe = !_bomResolved
                },
            TotalBytesRead = _totalBytesRead,
            TotalLinesEmitted = _totalLinesEmitted,
            LastEventSequence = _lastEventSequence,
            LastEventAt = _lastEventAt,
            FaultCode = _faultCode,
            FaultMessage = _faultMessage
        };
    }

    private int PartialBufferByteCountLocked() =>
        _lineTooLarge ? _options.MaximumLineBytes + 1 : _lineBuffer.Count + _bomProbe.Count;

    private static bool SemanticallyEquivalent(ParserWorkerSnapshot left, ParserWorkerSnapshot right) =>
        left with { RecentSegments = [] } == right with { RecentSegments = [] }
        && left.RecentSegments.SequenceEqual(right.RecentSegments);

    private void PublishState(ParserWorkerSnapshot? snapshot)
    {
        if (snapshot is not null)
        {
            StateChanged?.Invoke(this, new ParserWorkerChangedEventArgs(snapshot));
        }
    }

    private void PublishEvents(IEnumerable<ParserRawEvent> events)
    {
        foreach (var parserEvent in events)
        {
            RawEventAvailable?.Invoke(this, new ParserRawEventAvailableEventArgs(parserEvent));
        }
    }

    private string GenerationJumpMessage(MonitoringContextSnapshot context) =>
        $"Context generation jumped from {_appliedGeneration} to {context.SourceBindingGeneration}.";

    private static string FaultCode(Exception exception) => exception switch
    {
        DecoderFallbackException => "invalid_utf8",
        UnauthorizedAccessException => "source_access_denied",
        _ => "source_io_failure"
    };

    private static string SafeMessage(Exception exception) => exception switch
    {
        DecoderFallbackException => "The source contained invalid UTF-8.",
        UnauthorizedAccessException => "The assigned source could not be opened for reading.",
        _ => "The assigned source could not be read safely."
    };

    private void TryWriteDiagnostic(DiagnosticEvent diagnosticEvent)
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

    private enum StartPositionPolicy
    {
        AttachmentEnd,
        Zero
    }

    private sealed class MutableSegment
    {
        public required ParserSourceSegmentId SourceSegmentId { get; init; }

        public required MonitoringContextId ContextId { get; init; }

        public required LogSourceId SourceId { get; init; }

        public required long BindingGeneration { get; init; }

        public required MonitoringSourceTransitionKind TransitionKind { get; init; }

        public required DateTimeOffset StartedAt { get; init; }

        public required long StartingOffset { get; init; }

        public required long EndingOffset { get; set; }

        public long BytesRead { get; set; }

        public long LinesEmitted { get; set; }

        public ParserSourceSegmentSnapshot ToSnapshot(
            DateTimeOffset? endedAt,
            ParserSourceSegmentState state,
            bool incompleteFragmentAtEnd) =>
            new()
            {
                SourceSegmentId = SourceSegmentId,
                ContextId = ContextId,
                SourceId = SourceId,
                BindingGeneration = BindingGeneration,
                TransitionKind = TransitionKind,
                StartedAt = StartedAt,
                EndedAt = endedAt,
                StartingOffset = StartingOffset,
                EndingOffset = EndingOffset,
                BytesRead = BytesRead,
                LinesEmitted = LinesEmitted,
                IncompleteFragmentAtEnd = incompleteFragmentAtEnd,
                State = state
            };
    }
}
