using System.Threading.Channels;
using CoHAnalytics.Models;
using CoHAnalytics.ReferenceData;
using CoHAnalytics.Services;

namespace CoHAnalytics.Observations;

/// <summary>
/// Captures unresolved acquisition observations asynchronously and maintains derived journal state.
/// </summary>
public sealed class AcquisitionObservationService : IAcquisitionObservationService, IDisposable
{
    public const int DefaultQueueCapacity = 512;

    private readonly object _journalSync = new();
    private readonly IItemReferenceCatalog _itemReferenceCatalog;
    private readonly ApplicationActivityLogService? _activityLogService;
    private readonly AcquisitionObservationEvidenceStore _evidenceStore;
    private readonly AcquisitionObservationJournalStore _journalStore;
    private readonly TimeProvider _timeProvider;
    private readonly Channel<AcquisitionObservationRecord> _channel;
    private readonly Task _writerTask;
    private readonly CancellationTokenSource _writerCts = new();

    private AcquisitionObservationJournalDocument _journal;
    private long _acquisitionsSeen;
    private long _acquisitionsResolved;
    private long _acquisitionsPresentedUnresolved;
    private long _acquisitionsUnresolved;
    private long _droppedObservations;
    private DateTimeOffset? _lastCaptureAtUtc;
    private string? _persistenceFailureDetail;
    private bool _disposed;

    public AcquisitionObservationService(
        IItemReferenceCatalog itemReferenceCatalog,
        string? dataDirectory = null,
        ApplicationActivityLogService? activityLogService = null,
        AcquisitionObservationServiceOptions? options = null,
        TimeProvider? timeProvider = null)
    {
        options ??= new AcquisitionObservationServiceOptions();
        var queueCapacity = options.QueueCapacity;
        if (queueCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Queue capacity must be positive.");
        }

        _itemReferenceCatalog = itemReferenceCatalog;
        _activityLogService = activityLogService;
        _timeProvider = timeProvider ?? TimeProvider.System;

        var observationsRoot = ApplicationDataPaths.GetObservationsRoot(dataDirectory);
        _evidenceStore = new AcquisitionObservationEvidenceStore(
            observationsRoot,
            options.BeforeEvidenceAppend);
        _journalStore = new AcquisitionObservationJournalStore(observationsRoot);

        try
        {
            _evidenceStore.EnsureDirectory();
        }
        catch (Exception ex)
        {
            _persistenceFailureDetail = ex.Message;
        }

        _journal = LoadJournalWithRecovery();
        ReconcileCatalogIfNeeded(recordActivity: true);

        _channel = Channel.CreateBounded<AcquisitionObservationRecord>(new BoundedChannelOptions(queueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });

        _writerTask = options.EnableBackgroundWriter
            ? Task.Run(ProcessQueueAsync)
            : Task.CompletedTask;
    }

    public event EventHandler? Changed;

    public AcquisitionObservationStatus GetStatus()
    {
        lock (_journalSync)
        {
            return new AcquisitionObservationStatus
            {
                Health = ResolveHealth(),
                HealthDetail = _persistenceFailureDetail,
                AcquisitionsSeen = _acquisitionsSeen,
                AcquisitionsResolved = _acquisitionsResolved,
                AcquisitionsPresentedUnresolved = _acquisitionsPresentedUnresolved,
                AcquisitionsUnresolved = _acquisitionsUnresolved,
                DroppedObservations = _droppedObservations,
                LastCaptureAtUtc = _lastCaptureAtUtc,
                CatalogVersion = _itemReferenceCatalog.Manifest?.CatalogVersion,
                ObservationsRoot = _evidenceStore.ObservationsRoot
            };
        }
    }

    public void RecordClassificationAttempt(bool resolved)
    {
        Interlocked.Increment(ref _acquisitionsSeen);
        if (resolved)
        {
            Interlocked.Increment(ref _acquisitionsResolved);
        }
        else
        {
            Interlocked.Increment(ref _acquisitionsUnresolved);
        }
    }

    public IReadOnlyList<AcquisitionObservationListItem> GetNeedsClassification(
        AcquisitionObservationFilter? filter = null)
    {
        filter ??= new AcquisitionObservationFilter();
        lock (_journalSync)
        {
            IEnumerable<AcquisitionObservationJournalEntry> entries = _journal.Entries
                .Where(entry => entry.ResolutionState is not AcquisitionIdentityResolutionState.Resolved)
                .Where(entry => entry.Status is not ObservationDispositionStatus.Resolved);

            if (!filter.IncludeInactiveDispositions)
            {
                entries = entries.Where(entry => entry.Status is not (
                    ObservationDispositionStatus.Ignored or ObservationDispositionStatus.Rejected));
            }

            if (!string.IsNullOrWhiteSpace(filter.SearchText))
            {
                var searchText = filter.SearchText.Trim();
                entries = entries.Where(entry =>
                    entry.ObservedText.Contains(searchText, StringComparison.OrdinalIgnoreCase)
                    || entry.NormalizedKey.Contains(searchText, StringComparison.OrdinalIgnoreCase));
            }

            if (filter.Family is not null)
            {
                entries = entries.Where(entry => entry.FamilyHint == filter.Family);
            }

            if (filter.ResolutionState is not null)
            {
                entries = entries.Where(entry => entry.ResolutionState == filter.ResolutionState);
            }

            return entries
                .OrderByDescending(entry => entry.LastSeenUtc)
                .ThenBy(entry => entry.NormalizedKey, StringComparer.Ordinal)
                .Select(ToListItem)
                .ToArray();
        }
    }

    public AcquisitionObservationDetail? GetObservationDetail(string normalizedKey)
    {
        if (string.IsNullOrWhiteSpace(normalizedKey))
        {
            return null;
        }

        lock (_journalSync)
        {
            var entry = _journal.Entries.FirstOrDefault(candidate => string.Equals(
                candidate.NormalizedKey,
                normalizedKey,
                StringComparison.Ordinal));
            if (entry is null)
            {
                return null;
            }

            var evidence = _evidenceStore.ReadEvidenceForKey(normalizedKey)
                .Select(record => new AcquisitionObservationEvidenceSample
                {
                    ObservedText = record.ObservedText,
                    CapturedAtUtc = record.CapturedAtUtc,
                    GrammarSource = record.GrammarSource,
                    FailedCatalogVersion = record.FailedCatalogVersion
                })
                .ToArray();

            return new AcquisitionObservationDetail
            {
                Summary = ToListItem(entry),
                FailedCatalogVersions = [.. entry.FailedCatalogVersions],
                DeveloperNotes = entry.DeveloperNotes,
                Evidence = evidence
            };
        }
    }

    public AcquisitionObservationOperationResult ReconcileObservation(string normalizedKey)
    {
        lock (_journalSync)
        {
            var entry = _journal.Entries.FirstOrDefault(candidate => string.Equals(
                candidate.NormalizedKey,
                normalizedKey,
                StringComparison.Ordinal));
            if (entry is null)
            {
                return ObservationResult(AcquisitionObservationOperationOutcome.NotFound);
            }

            if (!_itemReferenceCatalog.TryResolve(entry.ObservedText, out var resolution))
            {
                return ObservationResult(AcquisitionObservationOperationOutcome.CatalogUnresolved);
            }

            var priorStatus = entry.Status;
            var priorState = entry.ResolutionState;
            var priorFamily = entry.FamilyHint;
            var priorItemId = entry.ResolvedCatalogItemId;
            var priorVersion = entry.ResolvingCatalogVersion;

            entry.Status = ObservationDispositionStatus.Resolved;
            entry.ResolutionState = AcquisitionIdentityResolutionState.Resolved;
            entry.FamilyHint = resolution.Item.Family;
            entry.ResolvedCatalogItemId = resolution.Item.CatalogItemId;
            entry.ResolvingCatalogVersion = resolution.CatalogVersion;

            if (!_journalStore.TrySave(_journal, out var failureReason))
            {
                entry.Status = priorStatus;
                entry.ResolutionState = priorState;
                entry.FamilyHint = priorFamily;
                entry.ResolvedCatalogItemId = priorItemId;
                entry.ResolvingCatalogVersion = priorVersion;
                _persistenceFailureDetail = failureReason;
                return ObservationResult(
                    AcquisitionObservationOperationOutcome.PersistenceFailed,
                    failureReason);
            }

            _persistenceFailureDetail = null;
        }

        RaiseChanged();
        return ObservationResult(AcquisitionObservationOperationOutcome.Success);
    }

    public void RecordClassificationAttempt(AcquisitionIdentityResolutionState resolutionState)
    {
        Interlocked.Increment(ref _acquisitionsSeen);
        switch (resolutionState)
        {
            case AcquisitionIdentityResolutionState.Resolved:
                Interlocked.Increment(ref _acquisitionsResolved);
                break;
            case AcquisitionIdentityResolutionState.PresentedUnresolved:
                Interlocked.Increment(ref _acquisitionsPresentedUnresolved);
                break;
            default:
                Interlocked.Increment(ref _acquisitionsUnresolved);
                break;
        }
    }

    public bool TryRecordUnresolvedAcquisition(AcquisitionObservationRecord observation)
    {
        if (_disposed)
        {
            return false;
        }

        if (_channel.Writer.TryWrite(observation))
        {
            return true;
        }

        Interlocked.Increment(ref _droppedObservations);
        RaiseChanged();
        return false;
    }

    internal IReadOnlyList<AcquisitionObservationJournalEntry> DebugJournalEntries
    {
        get
        {
            lock (_journalSync)
            {
                return _journal.Entries.ToArray();
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _channel.Writer.TryComplete();
        if (_writerTask != Task.CompletedTask)
        {
            try
            {
                if (!_writerTask.Wait(TimeSpan.FromSeconds(5)))
                {
                    _writerCts.Cancel();
                    _writerTask.Wait(TimeSpan.FromSeconds(1));
                }
            }
            catch
            {
            }
        }

        _writerCts.Dispose();
    }

    private AcquisitionObservationJournalDocument LoadJournalWithRecovery()
    {
        var loaded = _journalStore.LoadOrEmpty();
        if (loaded.Entries.Count > 0)
        {
            if (NormalizeJournalIdentities(loaded))
            {
                _journalStore.SaveNoThrow(loaded);
            }

            return loaded;
        }

        try
        {
            var evidence = _evidenceStore.ReadAllEvidence(out _);
            if (evidence.Count == 0)
            {
                return loaded;
            }

            var rebuilt = _journalStore.RebuildFromEvidence(evidence, loaded);
            _journalStore.SaveNoThrow(rebuilt);
            return rebuilt;
        }
        catch (Exception ex)
        {
            _persistenceFailureDetail = ex.Message;
            return new AcquisitionObservationJournalDocument();
        }
    }

    private static bool NormalizeJournalIdentities(AcquisitionObservationJournalDocument journal)
    {
        var changed = false;
        var normalizedEntries = new Dictionary<string, AcquisitionObservationJournalEntry>(StringComparer.Ordinal);

        foreach (var entry in journal.Entries.OrderBy(entry => entry.FirstSeenUtc))
        {
            var sourceText = string.IsNullOrWhiteSpace(entry.ObservedText)
                ? entry.NormalizedKey
                : entry.ObservedText;
            var normalizedKey = ItemReferenceLookup.NormalizeLookupKey(sourceText);
            if (!string.Equals(entry.NormalizedKey, normalizedKey, StringComparison.Ordinal))
            {
                entry.NormalizedKey = normalizedKey;
                changed = true;
            }

            if (!normalizedEntries.TryGetValue(normalizedKey, out var existing))
            {
                normalizedEntries[normalizedKey] = entry;
                continue;
            }

            changed = true;
            existing.OccurrenceCount += entry.OccurrenceCount;
            existing.FirstSeenUtc = existing.FirstSeenUtc <= entry.FirstSeenUtc
                ? existing.FirstSeenUtc
                : entry.FirstSeenUtc;
            existing.LastSeenUtc = existing.LastSeenUtc >= entry.LastSeenUtc
                ? existing.LastSeenUtc
                : entry.LastSeenUtc;
            existing.EvidenceSampleCount = Math.Min(
                AcquisitionObservationConstants.MaxEvidenceSamplesPerKey,
                existing.EvidenceSampleCount + entry.EvidenceSampleCount);

            foreach (var version in entry.FailedCatalogVersions)
            {
                if (!existing.FailedCatalogVersions.Contains(version, StringComparer.Ordinal))
                {
                    existing.FailedCatalogVersions.Add(version);
                }
            }

            if (entry.Status > existing.Status)
            {
                existing.Status = entry.Status;
                existing.DeveloperNotes = entry.DeveloperNotes ?? existing.DeveloperNotes;
                existing.ResolvedCatalogItemId = entry.ResolvedCatalogItemId ?? existing.ResolvedCatalogItemId;
                existing.ResolvingCatalogVersion = entry.ResolvingCatalogVersion ?? existing.ResolvingCatalogVersion;
            }

            if (entry.ResolutionState is AcquisitionIdentityResolutionState.PresentedUnresolved)
            {
                existing.ResolutionState = AcquisitionIdentityResolutionState.PresentedUnresolved;
                existing.FamilyHint = entry.FamilyHint ?? existing.FamilyHint;
            }
        }

        if (changed)
        {
            journal.Entries = normalizedEntries.Values
                .OrderByDescending(entry => entry.LastSeenUtc)
                .ToList();
        }

        return changed;
    }

    private void ReconcileCatalogIfNeeded(bool recordActivity)
    {
        if (!_itemReferenceCatalog.IsLoaded || _itemReferenceCatalog.Manifest is null)
        {
            return;
        }

        var currentVersion = _itemReferenceCatalog.Manifest.CatalogVersion;
        lock (_journalSync)
        {
            if (string.Equals(_journal.LastReconciledCatalogVersion, currentVersion, StringComparison.Ordinal))
            {
                return;
            }

            var resolvedCount = 0;
            foreach (var entry in _journal.Entries)
            {
                if (entry.Status is ObservationDispositionStatus.Resolved)
                {
                    continue;
                }

                if (!_itemReferenceCatalog.TryResolve(entry.ObservedText, out var resolution))
                {
                    continue;
                }

                entry.Status = ObservationDispositionStatus.Resolved;
                entry.ResolutionState = AcquisitionIdentityResolutionState.Resolved;
                entry.FamilyHint = resolution.Item.Family;
                entry.ResolvedCatalogItemId = resolution.Item.CatalogItemId;
                entry.ResolvingCatalogVersion = resolution.CatalogVersion;
                resolvedCount++;
            }

            _journal.LastReconciledCatalogVersion = currentVersion;
            _journalStore.SaveNoThrow(_journal);

            if (recordActivity && resolvedCount > 0 && _activityLogService is not null)
            {
                _activityLogService.Record(
                    "reference.reconciled",
                    $"Reference data updated. {resolvedCount} previously unrecognized item(s) are now identified.");
            }
        }
    }

    private async Task ProcessQueueAsync()
    {
        try
        {
            await foreach (var observation in _channel.Reader.ReadAllAsync(_writerCts.Token))
            {
                ProcessObservation(observation);
            }
        }
        catch (OperationCanceledException) when (_writerCts.IsCancellationRequested)
        {
        }
        catch
        {
            _persistenceFailureDetail = "Observation writer failed.";
            RaiseChanged();
        }
    }

    private void ProcessObservation(AcquisitionObservationRecord observation)
    {
        var shouldWriteEvidence = false;

        lock (_journalSync)
        {
            try
            {
                var entry = _journal.Entries.FirstOrDefault(
                    candidate => string.Equals(candidate.NormalizedKey, observation.NormalizedLookupKey, StringComparison.Ordinal));
                if (entry is null)
                {
                    entry = new AcquisitionObservationJournalEntry
                    {
                        NormalizedKey = observation.NormalizedLookupKey,
                        ObservedText = observation.ObservedText,
                        ProducerId = observation.ProducerId,
                        ObservationKind = observation.ObservationKind,
                        OccurrenceCount = 0,
                        FirstSeenUtc = observation.CapturedAtUtc,
                        LastSeenUtc = observation.CapturedAtUtc,
                        Status = ObservationDispositionStatus.New
                    };
                    _journal.Entries.Add(entry);
                }

                entry.OccurrenceCount++;
                if (observation.ResolutionStateAtCapture is AcquisitionIdentityResolutionState.PresentedUnresolved)
                {
                    entry.ResolutionState = AcquisitionIdentityResolutionState.PresentedUnresolved;
                    entry.FamilyHint = observation.FamilyHint ?? entry.FamilyHint;
                }
                entry.LastSeenUtc = observation.CapturedAtUtc;
                if (observation.CapturedAtUtc < entry.FirstSeenUtc)
                {
                    entry.FirstSeenUtc = observation.CapturedAtUtc;
                }

                if (!string.IsNullOrWhiteSpace(observation.FailedCatalogVersion)
                    && !entry.FailedCatalogVersions.Contains(observation.FailedCatalogVersion, StringComparer.Ordinal))
                {
                    entry.FailedCatalogVersions.Add(observation.FailedCatalogVersion);
                }

                if (entry.EvidenceSampleCount < AcquisitionObservationConstants.MaxEvidenceSamplesPerKey)
                {
                    entry.EvidenceSampleCount++;
                    shouldWriteEvidence = true;
                }

                _journalStore.SaveNoThrow(_journal);
                _lastCaptureAtUtc = observation.CapturedAtUtc;
                _persistenceFailureDetail = null;
            }
            catch (Exception ex)
            {
                _persistenceFailureDetail = ex.Message;
            }

            if (shouldWriteEvidence)
            {
                try
                {
                    _evidenceStore.AppendEvidence(observation);
                }
                catch (Exception ex)
                {
                    _persistenceFailureDetail = ex.Message;
                }
            }
        }

        RaiseChanged();
    }

    private AcquisitionCaptureHealth ResolveHealth()
    {
        if (!_itemReferenceCatalog.IsLoaded)
        {
            return AcquisitionCaptureHealth.Unavailable;
        }

        return string.IsNullOrWhiteSpace(_persistenceFailureDetail)
            ? AcquisitionCaptureHealth.Active
            : AcquisitionCaptureHealth.Degraded;
    }

    private static AcquisitionObservationListItem ToListItem(AcquisitionObservationJournalEntry entry) =>
        new()
        {
            NormalizedKey = entry.NormalizedKey,
            ObservedText = entry.ObservedText,
            FamilyHint = entry.FamilyHint,
            ResolutionState = entry.ResolutionState,
            Disposition = entry.Status,
            OccurrenceCount = entry.OccurrenceCount,
            FirstSeenUtc = entry.FirstSeenUtc,
            LastSeenUtc = entry.LastSeenUtc,
            EvidenceSampleCount = entry.EvidenceSampleCount
        };

    private static AcquisitionObservationOperationResult ObservationResult(
        AcquisitionObservationOperationOutcome outcome,
        string? detail = null) =>
        new() { Outcome = outcome, Detail = detail };

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
