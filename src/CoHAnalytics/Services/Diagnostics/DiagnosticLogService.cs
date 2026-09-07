using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;

namespace CoHAnalytics.Services.Diagnostics;

/// <summary>
/// Persistent Standard diagnostic stream. Producers only enqueue typed events; one background
/// reader owns serialization, rolling-file writes, flushing, and sink degradation.
/// </summary>
public sealed class DiagnosticLogService : IDiagnosticLog, IDisposable
{
    public const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly object _statusSync = new();
    private readonly object _dropSync = new();
    private readonly DiagnosticLogOptions _options;
    private readonly Channel<PendingDiagnosticRecord> _channel;
    private readonly QueuePressureTracker _queuePressure;
    private readonly CancellationTokenSource _writerCancellation = new();
    private readonly RollingJsonlFileSink? _sink;
    private readonly Task _writerTask;
    private readonly string _activePath;

    private DiagnosticLogStreamState _streamState = DiagnosticLogStreamState.Initializing;
    private long _writtenCount;
    private long _droppedCount;
    private DateTimeOffset? _lastSuccessfulWriteAtUtc;
    private string? _lastFailureCode;
    private string? _lastFailureType;
    private int? _lastFailureHResult;
    private DateTimeOffset? _lastFailureAtUtc;
    private long _pendingDroppedCount;
    private DateTimeOffset? _firstPendingDropAtUtc;
    private DateTimeOffset? _lastPendingDropAtUtc;
    private long _sequence;
    private int _accepting = 1;
    private int _disposed;

    public DiagnosticLogService(
        string? dataDirectory = null,
        DiagnosticLogOptions? options = null)
    {
        _options = options ?? new DiagnosticLogOptions();
        _options.Validate();

        ApplicationRunId = Guid.NewGuid();
        var logsDirectory = ApplicationDataPaths.GetLogsRoot(dataDirectory);
        _activePath = Path.Combine(logsDirectory, RollingJsonlFileSink.ActiveFileName);
        _queuePressure = new QueuePressureTracker(_options.QueueCapacity);
        _channel = Channel.CreateBounded<PendingDiagnosticRecord>(new BoundedChannelOptions(_options.QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });

        RollingJsonlFileSink? sink = null;
        try
        {
            sink = new RollingJsonlFileSink(logsDirectory, _options);
            SetStreamState(DiagnosticLogStreamState.Active);
        }
        catch (Exception exception)
        {
            RecordFailure(FailureCode(exception, "sink_initialization_failed"), exception);
            SetStreamState(DiagnosticLogStreamState.Degraded);
            Volatile.Write(ref _accepting, 0);
            _channel.Writer.TryComplete();
        }

        _sink = sink;
        _writerTask = sink is null
            ? Task.CompletedTask
            : Task.Run(() => ProcessQueueAsync(_writerCancellation.Token));
    }

    public Guid ApplicationRunId { get; }

    public bool IsEnabled(DiagnosticChannel channel, DiagnosticCategory category) =>
        channel == DiagnosticChannel.Standard
        && Enum.IsDefined(category)
        && Volatile.Read(ref _accepting) == 1
        && GetStreamState() == DiagnosticLogStreamState.Active;

    public void Write(DiagnosticEvent diagnosticEvent)
    {
        try
        {
            var observedAt = _options.TimeProvider.GetUtcNow().ToUniversalTime();
            if (diagnosticEvent is null
                || diagnosticEvent.Channel != DiagnosticChannel.Standard
                || string.IsNullOrWhiteSpace(diagnosticEvent.EventName)
                || !IsEnabled(diagnosticEvent.Channel, diagnosticEvent.Category))
            {
                RecordDropped(observedAt);
                if (diagnosticEvent is null || string.IsNullOrWhiteSpace(diagnosticEvent?.EventName))
                {
                    RecordFailure("malformed_event", new ArgumentException("Diagnostic event is invalid."));
                }

                return;
            }

            var pending = new PendingDiagnosticRecord(diagnosticEvent, observedAt);
            if (!QueuePressureAdmission.TryPublish(_channel.Writer, pending, _queuePressure))
            {
                _queuePressure.RecordRejected();
                _queuePressure.LatchOverflow();
                RecordDropped(observedAt);
            }
        }
        catch (Exception exception)
        {
            RecordDroppedNoThrow();
            RecordFailureNoThrow("event_admission_failed", exception);
        }
    }

    public DiagnosticLogStatus GetStatus()
    {
        var queue = _queuePressure.Snapshot();
        lock (_statusSync)
        {
            return new DiagnosticLogStatus
            {
                StreamState = _streamState,
                ActivePath = _activePath,
                ApplicationRunId = ApplicationRunId,
                QueueDepth = queue.CurrentDepth,
                PeakQueueDepth = queue.PeakDepth,
                WrittenCount = _writtenCount,
                DroppedCount = Interlocked.Read(ref _droppedCount),
                LastSuccessfulWriteAtUtc = _lastSuccessfulWriteAtUtc,
                LastFailureCode = _lastFailureCode,
                LastFailureType = _lastFailureType,
                LastFailureHResult = _lastFailureHResult,
                LastFailureAtUtc = _lastFailureAtUtc
            };
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Volatile.Write(ref _accepting, 0);
        if (GetStreamState() == DiagnosticLogStreamState.Active)
        {
            SetStreamState(DiagnosticLogStreamState.Stopping);
        }

        _channel.Writer.TryComplete();
        if (!_writerTask.IsCompleted)
        {
            try
            {
                if (!_writerTask.Wait(_options.ShutdownTimeout))
                {
                    RecordFailure("shutdown_timeout", new TimeoutException("Diagnostic log shutdown timed out."));
                    SetStreamState(DiagnosticLogStreamState.Degraded);
                    _queuePressure.AbandonUnfinishedAtShutdown();
                    _writerCancellation.Cancel();
                    return;
                }
            }
            catch (Exception exception)
            {
                RecordFailureNoThrow("shutdown_failed", exception);
                SetStreamState(DiagnosticLogStreamState.Degraded);
                _writerCancellation.Cancel();
                return;
            }
        }

        _writerCancellation.Dispose();
        if (GetStreamState() != DiagnosticLogStreamState.Degraded)
        {
            SetStreamState(DiagnosticLogStreamState.Stopped);
        }
    }

    private async Task ProcessQueueAsync(CancellationToken cancellationToken)
    {
        var stoppedNormally = false;
        try
        {
            while (await _channel.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var batch = ReadBatch();
                if (batch.Count == 0)
                {
                    continue;
                }

                if (!TryPersistBatch(batch))
                {
                    return;
                }

                TryEnqueueDroppedMarker();
            }

            if (!TryPersistPendingDroppedMarkerDirect())
            {
                return;
            }

            stoppedNormally = true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            DisableSink("writer_task_failed", exception);
        }
        finally
        {
            _sink?.Dispose();
            if (stoppedNormally && GetStreamState() != DiagnosticLogStreamState.Degraded)
            {
                SetStreamState(DiagnosticLogStreamState.Stopped);
            }
        }
    }

    private List<PendingDiagnosticRecord> ReadBatch()
    {
        var batch = new List<PendingDiagnosticRecord>(_options.MaximumBatchSize);
        while (batch.Count < _options.MaximumBatchSize && _channel.Reader.TryRead(out var pending))
        {
            _queuePressure.RecordDequeued();
            batch.Add(pending);
        }

        return batch;
    }

    private bool TryPersistBatch(IReadOnlyList<PendingDiagnosticRecord> batch)
    {
        var serialized = new List<string>(batch.Count);
        var persistedCandidates = new List<PendingDiagnosticRecord>(batch.Count);

        foreach (var pending in batch)
        {
            try
            {
                _options.BeforeSerialize?.Invoke(pending.Event);
                serialized.Add(Serialize(pending));
                persistedCandidates.Add(pending);
            }
            catch (Exception exception)
            {
                _queuePressure.RecordAbandoned();
                RecordDropped(
                    pending.ObservedAtUtc,
                    includeInDroppedMarker: pending.Event is not DiagnosticRecordsDroppedDiagnosticEvent);
                RecordFailure("serialization_failed", exception);
            }
        }

        if (serialized.Count == 0)
        {
            return true;
        }

        var writtenAt = _options.TimeProvider.GetUtcNow().ToUniversalTime();
        try
        {
            _sink!.WriteBatch(serialized, writtenAt);
            foreach (var _ in persistedCandidates)
            {
                _queuePressure.RecordCompleted();
            }

            lock (_statusSync)
            {
                _writtenCount += serialized.Count;
                _lastSuccessfulWriteAtUtc = writtenAt;
            }

            return true;
        }
        catch (Exception exception)
        {
            foreach (var pending in persistedCandidates)
            {
                _queuePressure.RecordAbandoned();
                RecordDropped(pending.ObservedAtUtc);
            }

            DisableSink(FailureCode(exception, "sink_write_failed"), exception);
            return false;
        }
    }

    private string Serialize(PendingDiagnosticRecord pending)
    {
        var payload = JsonSerializer.SerializeToElement(
            pending.Event,
            pending.Event.GetType(),
            SerializerOptions);
        var envelope = new DiagnosticLogEnvelope
        {
            SchemaVersion = CurrentSchemaVersion,
            Timestamp = pending.ObservedAtUtc,
            Sequence = Interlocked.Increment(ref _sequence),
            ApplicationRunId = ApplicationRunId,
            Severity = pending.Event.Severity,
            Channel = pending.Event.Channel,
            Category = pending.Event.Category,
            Event = pending.Event.EventName,
            Data = payload
        };
        return JsonSerializer.Serialize(envelope, SerializerOptions);
    }

    private void TryEnqueueDroppedMarker()
    {
        lock (_dropSync)
        {
            if (_pendingDroppedCount == 0
                || _firstPendingDropAtUtc is not { } first
                || _lastPendingDropAtUtc is not { } last)
            {
                return;
            }

            var marker = new PendingDiagnosticRecord(
                new DiagnosticRecordsDroppedDiagnosticEvent
                {
                    DroppedLogCount = _pendingDroppedCount,
                    FirstDroppedAt = first,
                    LastDroppedAt = last
                },
                _options.TimeProvider.GetUtcNow().ToUniversalTime());

            if (!QueuePressureAdmission.TryPublish(_channel.Writer, marker, _queuePressure))
            {
                return;
            }

            _pendingDroppedCount = 0;
            _firstPendingDropAtUtc = null;
            _lastPendingDropAtUtc = null;
        }
    }

    private bool TryPersistPendingDroppedMarkerDirect()
    {
        PendingDiagnosticRecord? marker = null;
        lock (_dropSync)
        {
            if (_pendingDroppedCount > 0
                && _firstPendingDropAtUtc is { } first
                && _lastPendingDropAtUtc is { } last)
            {
                marker = new PendingDiagnosticRecord(
                    new DiagnosticRecordsDroppedDiagnosticEvent
                    {
                        DroppedLogCount = _pendingDroppedCount,
                        FirstDroppedAt = first,
                        LastDroppedAt = last
                    },
                    _options.TimeProvider.GetUtcNow().ToUniversalTime());
                _pendingDroppedCount = 0;
                _firstPendingDropAtUtc = null;
                _lastPendingDropAtUtc = null;
            }
        }

        return marker is null || TryPersistDirect(marker);
    }

    private bool TryPersistDirect(PendingDiagnosticRecord pending)
    {
        try
        {
            _options.BeforeSerialize?.Invoke(pending.Event);
            var serialized = Serialize(pending);
            var writtenAt = _options.TimeProvider.GetUtcNow().ToUniversalTime();
            _sink!.WriteBatch([serialized], writtenAt);
            lock (_statusSync)
            {
                _writtenCount++;
                _lastSuccessfulWriteAtUtc = writtenAt;
            }

            return true;
        }
        catch (Exception exception)
        {
            DisableSink(FailureCode(exception, "sink_write_failed"), exception);
            return false;
        }
    }

    private void DisableSink(string failureCode, Exception exception)
    {
        RecordFailure(failureCode, exception);
        SetStreamState(DiagnosticLogStreamState.Degraded);
        Volatile.Write(ref _accepting, 0);
        _channel.Writer.TryComplete(exception);

        while (_channel.Reader.TryRead(out var abandoned))
        {
            _queuePressure.RecordDequeued();
            _queuePressure.RecordAbandoned();
            RecordDropped(abandoned.ObservedAtUtc);
        }
    }

    private void RecordDropped(DateTimeOffset observedAt, bool includeInDroppedMarker = true)
    {
        lock (_dropSync)
        {
            Interlocked.Increment(ref _droppedCount);
            if (!includeInDroppedMarker)
            {
                return;
            }

            _pendingDroppedCount++;
            _firstPendingDropAtUtc ??= observedAt;
            _lastPendingDropAtUtc = observedAt;
        }
    }

    private void RecordDroppedNoThrow()
    {
        try
        {
            RecordDropped(_options.TimeProvider.GetUtcNow().ToUniversalTime());
        }
        catch
        {
        }
    }

    private void RecordFailure(string failureCode, Exception exception)
    {
        var failure = UnwrapSinkException(exception);
        lock (_statusSync)
        {
            _lastFailureCode = failureCode;
            _lastFailureType = failure.GetType().FullName;
            _lastFailureHResult = failure.HResult;
            _lastFailureAtUtc = _options.TimeProvider.GetUtcNow().ToUniversalTime();
        }
    }

    private void RecordFailureNoThrow(string failureCode, Exception exception)
    {
        try
        {
            RecordFailure(failureCode, exception);
        }
        catch
        {
        }
    }

    private DiagnosticLogStreamState GetStreamState()
    {
        lock (_statusSync)
        {
            return _streamState;
        }
    }

    private void SetStreamState(DiagnosticLogStreamState state)
    {
        lock (_statusSync)
        {
            _streamState = state;
        }
    }

    private static string FailureCode(Exception exception, string fallback) =>
        exception is DiagnosticLogSinkException sinkException
            ? sinkException.FailureCode
            : fallback;

    private static Exception UnwrapSinkException(Exception exception) =>
        exception is DiagnosticLogSinkException { InnerException: not null } sinkException
            ? sinkException.InnerException!
            : exception;

    private sealed record PendingDiagnosticRecord(
        DiagnosticEvent Event,
        DateTimeOffset ObservedAtUtc);

    private sealed record DiagnosticLogEnvelope
    {
        public required int SchemaVersion { get; init; }

        [JsonPropertyName("ts")]
        public required DateTimeOffset Timestamp { get; init; }

        public required long Sequence { get; init; }

        public required Guid ApplicationRunId { get; init; }

        public required DiagnosticSeverity Severity { get; init; }

        public required DiagnosticChannel Channel { get; init; }

        public required DiagnosticCategory Category { get; init; }

        public required string Event { get; init; }

        public required JsonElement Data { get; init; }
    }
}
