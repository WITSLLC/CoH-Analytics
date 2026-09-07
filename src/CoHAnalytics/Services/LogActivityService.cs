using System.Text.RegularExpressions;
using CoHAnalytics.Models;
using CoHAnalytics.Services.Diagnostics;

namespace CoHAnalytics.Services;

/// <summary>
/// Observes the Logs folder of every discovered Homecoming account and reports candidate
/// chat-log sources together with what has been directly observed about them.
/// </summary>
/// <remarks>
/// <para>
/// Observation is a periodic metadata scan and nothing else. No file is ever opened, so no
/// chat text is read. The service reports every candidate it finds: it does not select an
/// active log, rank a primary source, assign a source to anything, or create monitoring
/// contexts, parser workers, or gameplay sessions.
/// </para>
/// <para>
/// A file is reported as growing only after a length increase has been observed between two
/// scans in the current application run. Recent timestamps on a historical file prove nothing
/// about whether in-game chat logging is currently enabled, and this service never claims it.
/// </para>
/// </remarks>
public sealed class LogActivityService : ILogActivityService, IDisposable
{
    private static readonly Regex ChatLogFileNamePattern = new(
        @"^chatlog (?<date>\d{4}-\d{2}-\d{2})\.txt$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private const string LogsFolderName = "Logs";

    private readonly Func<IReadOnlyList<HomecomingAccount>> _accountsProvider;
    private readonly LogActivityServiceOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly IDiagnosticLog? _diagnosticLog;
    private readonly SemaphoreSlim _scanGate = new(1, 1);
    private readonly object _sync = new();

    private readonly Dictionary<string, SourceState> _states = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _observedLogsFolders = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _recentScanFailures = [];

    private LogActivitySnapshot _snapshot = LogActivitySnapshot.Empty;
    private IReadOnlyList<LogActivityAccountDiagnostics> _accountDiagnostics = [];
    private long _revision;
    private int _scanCount;
    private int _suppressedNoOpScanCount;
    private int _scanRequested;
    private DateTimeOffset? _lastScanStartedAt;
    private TimeSpan? _lastScanDuration;
    private string? _lastScanFailureMessage;

    private ITimer? _pollTimer;
    private CancellationTokenSource? _lifetimeCts;
    private volatile bool _running;
    private volatile bool _disposed;

    /// <summary>
    /// Creates the service over <see cref="HomecomingAccountDiscoveryService"/>, which is the sole
    /// authority for which account folders exist and are valid. Account discovery rules are never
    /// reimplemented or second-guessed here.
    /// </summary>
    public LogActivityService(
        HomecomingAccountDiscoveryService accountDiscoveryService,
        LogActivityServiceOptions? options = null,
        IDiagnosticLog? diagnosticLog = null)
        : this(() => accountDiscoveryService.Accounts, options, diagnosticLog)
    {
    }

    /// <summary>
    /// Test seam allowing account models to be supplied directly, so observation behavior can be
    /// verified against temporary directories without a real Homecoming installation.
    /// </summary>
    internal LogActivityService(
        Func<IReadOnlyList<HomecomingAccount>> accountsProvider,
        LogActivityServiceOptions? options = null,
        IDiagnosticLog? diagnosticLog = null)
    {
        _accountsProvider = accountsProvider;
        _options = options ?? new LogActivityServiceOptions();
        _timeProvider = _options.TimeProvider;
        _diagnosticLog = diagnosticLog;
    }

    public event EventHandler<LogActivityChangedEventArgs>? ActivityChanged;

    public LogActivitySnapshot Current
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

    public string? LastScanFailureMessage
    {
        get
        {
            lock (_sync)
            {
                return _lastScanFailureMessage;
            }
        }
    }

    /// <summary>
    /// Strict, case-insensitive recognition of the researched Homecoming daily chat-log name
    /// <c>chatlog YYYY-MM-DD.txt</c>. The date must be a real calendar date, so a well-shaped
    /// but impossible name is rejected. File contents are never inspected.
    /// </summary>
    public static bool TryParseChatLogDate(string? fileName, out DateOnly logDate)
    {
        logDate = default;

        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        var match = ChatLogFileNamePattern.Match(fileName);
        return match.Success
            && DateOnly.TryParseExact(
                match.Groups["date"].Value,
                "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out logDate);
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_running)
        {
            return;
        }

        _running = true;
        _lifetimeCts = new CancellationTokenSource();

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetimeCts.Token);

        await ScanAsync(linked.Token).ConfigureAwait(false);

        if (_options.EnablePolling)
        {
            _pollTimer = _timeProvider.CreateTimer(
                PollCallback,
                null,
                _options.PollInterval,
                _options.PollInterval);
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!_running)
        {
            return;
        }

        _running = false;

        var timer = _pollTimer;
        _pollTimer = null;
        if (timer is not null)
        {
            await timer.DisposeAsync().ConfigureAwait(false);
        }

        var lifetimeCts = _lifetimeCts;
        _lifetimeCts = null;
        if (lifetimeCts is not null)
        {
            try
            {
                await lifetimeCts.CancelAsync().ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
            }

            lifetimeCts.Dispose();
        }

        Volatile.Write(ref _scanRequested, 0);

        if (_disposed)
        {
            return;
        }

        // Wait for an in-flight scan to finish so shutdown does not race a publication.
        try
        {
            if (await _scanGate.WaitAsync(TimeSpan.FromSeconds(2), CancellationToken.None).ConfigureAwait(false))
            {
                _scanGate.Release();
            }
        }
        catch (ObjectDisposedException)
        {
        }
    }

    public async Task<LogActivitySnapshot> ScanAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed || !_running || cancellationToken.IsCancellationRequested)
        {
            return Current;
        }

        bool entered;
        try
        {
            entered = await _scanGate.WaitAsync(0, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return Current;
        }
        catch (ObjectDisposedException)
        {
            return Current;
        }

        if (!entered)
        {
            // A scan is already running; collapse this request into a single follow-up pass.
            Volatile.Write(ref _scanRequested, 1);
            return Current;
        }

        try
        {
            do
            {
                Volatile.Write(ref _scanRequested, 0);
                await RunScanPassAsync(cancellationToken).ConfigureAwait(false);
            }
            while (Volatile.Read(ref _scanRequested) == 1
                   && _running
                   && !_disposed
                   && !cancellationToken.IsCancellationRequested);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            Volatile.Write(ref _scanRequested, 0);
            try
            {
                _scanGate.Release();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        return Current;
    }

    public LogActivityDiagnostics GetDiagnostics()
    {
        lock (_sync)
        {
            return new LogActivityDiagnostics
            {
                PollInterval = _options.PollInterval,
                InactivityThreshold = _options.InactivityThreshold,
                IsPollingEnabled = _options.EnablePolling,
                IsRunning = _running,
                ScanCount = _scanCount,
                LastScanStartedAt = _lastScanStartedAt,
                LastScanDuration = _lastScanDuration,
                LastSnapshotRevision = _revision,
                SuppressedNoOpScanCount = _suppressedNoOpScanCount,
                Accounts = _accountDiagnostics,
                Sources = [.. _snapshot.Candidates.Select(ToSourceDiagnostics)],
                RecentScanFailures = [.. _recentScanFailures]
            };
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            StopAsync().ConfigureAwait(false).GetAwaiter().GetResult();
        }
        catch
        {
            // Shutdown of an observation service must never throw from disposal.
        }

        _disposed = true;

        _pollTimer?.Dispose();
        _pollTimer = null;
        _scanGate.Dispose();
    }

    private void PollCallback(object? state) => _ = ScanAsync(_lifetimeCts?.Token ?? CancellationToken.None);

    private async Task RunScanPassAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var startedAt = _timeProvider.GetUtcNow();
        var startTimestamp = _timeProvider.GetTimestamp();
        var accounts = _accountsProvider();

        List<RetainedSource> retained;
        lock (_sync)
        {
            retained =
            [
                .. _states.Values.Select(state => new RetainedSource(
                    state.FilePath,
                    state.AccountStableId,
                    state.AccountDisplayName,
                    state.AccountFolderPath,
                    state.LogDate))
            ];
        }

        ScanPassResult result;
        try
        {
            result = await Task.Run(
                () => ProbeFileSystem(accounts, retained),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            RecordScanFailure(startedAt, startTimestamp, $"Scan failed: {ex.Message}");
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();

        LogActivitySnapshot? published = null;
        var diagnosticEvents = new List<DiagnosticEvent>();

        lock (_sync)
        {
            var currentLogDate = DateOnly.FromDateTime(_timeProvider.GetLocalNow().DateTime);
            var existingRolloverCandidates = _states.Values
                .Where(state => state.IsRolloverCandidate)
                .Select(state => state.FilePath)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var probe in result.Probes)
            {
                var previous = _states.TryGetValue(probe.FilePath, out var previousState)
                    ? SourceDiagnosticState.From(previousState)
                    : null;
                ApplyProbe(probe, startedAt, currentLogDate);
                if (_states.TryGetValue(probe.FilePath, out var nextState))
                {
                    AppendSourceDiagnosticEvents(
                        previous,
                        SourceDiagnosticState.From(nextState),
                        diagnosticEvents);
                }
            }

            foreach (var logsFolder in result.ObservedLogsFolders)
            {
                _observedLogsFolders.Add(logsFolder);
            }

            MarkRolloverCandidates();
            foreach (var rollover in _states.Values.Where(state =>
                         state.IsRolloverCandidate
                         && !existingRolloverCandidates.Contains(state.FilePath)
                         && state.RolloverPredecessorSourceId is not null))
            {
                diagnosticEvents.Add(new LogActivitySourceRolloverDiagnosticEvent
                {
                    SourceId = rollover.SourceId.Value,
                    PredecessorSourceId = rollover.RolloverPredecessorSourceId!,
                    AccountStableId = rollover.AccountStableId,
                    SourceFileName = rollover.SourceId.FileName,
                    ActivityState = rollover.ActivityState,
                    Length = rollover.Length,
                    LastGrowthAt = rollover.LastGrowthAt?.ToUniversalTime(),
                    Reason = "DailyRolloverCandidate"
                });
            }

            var next = LogActivitySnapshot.Create(
                _states.Values.Select(ToCandidate),
                accounts.Count,
                result.LogsFolderCount,
                startedAt,
                _revision);

            if (_snapshot.IsSemanticallyEquivalentTo(next))
            {
                _snapshot = next.WithObservation(startedAt, _revision);
                _suppressedNoOpScanCount++;
            }
            else
            {
                _revision++;
                _snapshot = next.WithObservation(startedAt, _revision);
                published = _snapshot;
            }

            _accountDiagnostics = result.Accounts;
            _lastScanFailureMessage = null;
            _scanCount++;
            _lastScanStartedAt = startedAt;
            _lastScanDuration = _timeProvider.GetElapsedTime(startTimestamp);

            foreach (var failure in result.Failures)
            {
                AppendScanFailure(failure);
            }
        }

        WriteDiagnostics(diagnosticEvents);

        if (published is not null && _running && !_disposed)
        {
            DiagnosticEventSubscriberDispatch.InvokeOrdered(
                ActivityChanged,
                this,
                new LogActivityChangedEventArgs(published),
                onSubscriberFault: (subscriberId, exceptionType) =>
                {
                    try
                    {
                        _diagnosticLog?.Write(new EventSubscriberDispatchFailedDiagnosticEvent
                        {
                            EventSource = "LogActivity.ActivityChanged",
                            SubscriberId = subscriberId,
                            ExceptionType = exceptionType
                        });
                    }
                    catch
                    {
                        // Diagnostics are observational and must never affect source observation.
                    }
                });
        }
    }

    private ScanPassResult ProbeFileSystem(
        IReadOnlyList<HomecomingAccount> accounts,
        IReadOnlyList<RetainedSource> retained)
    {
        var probes = new List<SourceProbe>();
        var probedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var accountDiagnostics = new List<LogActivityAccountDiagnostics>();
        var observedLogsFolders = new List<string>();
        var failures = new List<string>();
        var logsFolderCount = 0;

        foreach (var account in accounts)
        {
            var logsFolder = Path.Combine(account.FolderPath, LogsFolderName);
            var logsFolderPresent = Directory.Exists(logsFolder);
            string? failureReason = null;
            var candidateCount = 0;

            if (logsFolderPresent)
            {
                logsFolderCount++;
                observedLogsFolders.Add(logsFolder);

                try
                {
                    foreach (var filePath in Directory.EnumerateFiles(
                                 logsFolder,
                                 "*.txt",
                                 SearchOption.TopDirectoryOnly))
                    {
                        if (!TryParseChatLogDate(Path.GetFileName(filePath), out var logDate))
                        {
                            continue;
                        }

                        var normalized = Path.GetFullPath(filePath);
                        if (!probedPaths.Add(normalized))
                        {
                            continue;
                        }

                        probes.Add(Probe(
                            normalized,
                            account.StableId,
                            account.DisplayName,
                            account.FolderPath,
                            logDate));
                        candidateCount++;
                    }
                }
                catch (Exception ex)
                {
                    failureReason = ex.Message;
                    failures.Add($"Unable to enumerate Logs folder for account '{account.DisplayName}': {ex.Message}");
                }
            }

            accountDiagnostics.Add(new LogActivityAccountDiagnostics
            {
                AccountStableId = account.StableId,
                AccountDisplayName = account.DisplayName,
                LogsFolderPresent = logsFolderPresent,
                RedactedLogsFolderPath = DiagnosticPathRedactor.Redact(logsFolder),
                CandidateCount = candidateCount,
                FailureReason = failureReason
            });
        }

        // Retained sources are probed individually so a file that disappeared, or a folder that
        // can no longer be enumerated, is still observed rather than silently forgotten.
        foreach (var source in retained)
        {
            if (!probedPaths.Add(source.FilePath))
            {
                continue;
            }

            probes.Add(Probe(
                source.FilePath,
                source.AccountStableId,
                source.AccountDisplayName,
                source.AccountFolderPath,
                source.LogDate));
        }

        return new ScanPassResult(
            probes,
            accountDiagnostics,
            observedLogsFolders,
            failures,
            logsFolderCount);
    }

    private static SourceProbe Probe(
        string filePath,
        string accountStableId,
        string accountDisplayName,
        string accountFolderPath,
        DateOnly logDate)
    {
        try
        {
            var info = new FileInfo(filePath);
            if (!info.Exists)
            {
                return new SourceProbe(
                    filePath,
                    accountStableId,
                    accountDisplayName,
                    accountFolderPath,
                    logDate,
                    Exists: false,
                    Length: 0,
                    LastWriteTime: null,
                    CreationTime: null,
                    Error: null);
            }

            return new SourceProbe(
                filePath,
                accountStableId,
                accountDisplayName,
                accountFolderPath,
                logDate,
                Exists: true,
                info.Length,
                new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero),
                new DateTimeOffset(info.CreationTimeUtc, TimeSpan.Zero),
                Error: null);
        }
        catch (Exception ex)
        {
            return new SourceProbe(
                filePath,
                accountStableId,
                accountDisplayName,
                accountFolderPath,
                logDate,
                Exists: false,
                Length: 0,
                LastWriteTime: null,
                CreationTime: null,
                Error: ex.Message);
        }
    }

    private void ApplyProbe(SourceProbe probe, DateTimeOffset now, DateOnly currentLogDate)
    {
        if (!_states.TryGetValue(probe.FilePath, out var state))
        {
            if (!probe.Exists && probe.Error is null)
            {
                return;
            }

            _states[probe.FilePath] = CreateState(probe, now, currentLogDate);
            return;
        }

        state.LastObservedAt = now;
        state.IsCurrentDailyFile = state.LogDate == currentLogDate;
        state.AccountDisplayName = probe.AccountDisplayName;

        if (probe.Error is not null)
        {
            state.LastChangeKind = LogSourceChangeKind.Inaccessible;
            state.ActivityState = LogSourceActivityState.Unavailable;
            state.UnavailableReason = probe.Error;
            return;
        }

        if (!probe.Exists)
        {
            if (state.Exists)
            {
                state.DisappearedAt = now;
                state.WasMissing = true;
            }

            state.Exists = false;
            state.LastChangeKind = LogSourceChangeKind.Disappeared;
            state.ActivityState = LogSourceActivityState.Unavailable;
            state.UnavailableReason = "File no longer exists at its observed path.";
            return;
        }

        state.UnavailableReason = null;

        if (DetectReplacement(state, probe, now) is { } evidence)
        {
            ApplyReplacement(state, probe, now, evidence);
            return;
        }

        state.Exists = true;
        state.LastWriteTime = probe.LastWriteTime;
        state.CreationTime = probe.CreationTime;

        if (probe.Length > state.Length)
        {
            state.PreviousLength = state.Length;
            state.Length = probe.Length;
            state.FirstGrowthAt ??= now;
            state.LastGrowthAt = now;
            state.LastChangeKind = LogSourceChangeKind.Grew;
            state.ActivityState = LogSourceActivityState.Growing;
            return;
        }

        if (probe.Length < state.Length)
        {
            state.PreviousLength = state.Length;
            state.Length = probe.Length;
            state.IsTruncated = true;
            state.LastChangeKind = LogSourceChangeKind.Truncated;
            state.ActivityState = LogSourceActivityState.Truncated;
            return;
        }

        state.LastChangeKind = LogSourceChangeKind.Unchanged;
        state.ActivityState = DeriveIdleState(state, now);
    }

    private SourceState CreateState(SourceProbe probe, DateTimeOffset now, DateOnly currentLogDate)
    {
        var logsFolder = Path.Combine(probe.AccountFolderPath, LogsFolderName);
        var isCurrentDailyFile = probe.LogDate == currentLogDate;

        var state = new SourceState
        {
            SourceId = LogSourceId.Create(
                probe.AccountStableId,
                probe.AccountDisplayName,
                probe.FilePath,
                probe.LogDate),
            AccountStableId = probe.AccountStableId,
            AccountDisplayName = probe.AccountDisplayName,
            AccountFolderPath = probe.AccountFolderPath,
            FilePath = probe.FilePath,
            LogDate = probe.LogDate,
            Exists = probe.Exists,
            Length = probe.Length,

            // The first observation establishes a baseline. Treating an existing file as having
            // grown would invent live activity that was never observed.
            PreviousLength = probe.Length,
            LastWriteTime = probe.LastWriteTime,
            CreationTime = probe.CreationTime,
            FirstObservedAt = now,
            LastObservedAt = now,
            IsCurrentDailyFile = isCurrentDailyFile,
            LastChangeKind = _observedLogsFolders.Contains(logsFolder)
                ? LogSourceChangeKind.Created
                : LogSourceChangeKind.Discovered
        };

        if (probe.Error is not null)
        {
            state.ActivityState = LogSourceActivityState.Unavailable;
            state.UnavailableReason = probe.Error;
            state.LastChangeKind = LogSourceChangeKind.Inaccessible;
        }
        else
        {
            state.ActivityState = isCurrentDailyFile
                ? LogSourceActivityState.Waiting
                : LogSourceActivityState.Historical;
        }

        return state;
    }

    /// <summary>
    /// Conservative replacement evidence. A length decrease alone is only truncation; see
    /// <see cref="LogActivityDiagnostics.ReplacementDetectionLimitation"/> for what this
    /// cannot distinguish.
    /// </summary>
    private static string? DetectReplacement(SourceState state, SourceProbe probe, DateTimeOffset now)
    {
        if (state.WasMissing)
        {
            return $"Observed absent at {state.DisappearedAt:O} and present again at {now:O}.";
        }

        if (state.CreationTime is { } previousCreation
            && probe.CreationTime is { } currentCreation
            && currentCreation != previousCreation)
        {
            return $"Creation timestamp changed from {previousCreation:O} to {currentCreation:O}.";
        }

        return null;
    }

    private static void ApplyReplacement(
        SourceState state,
        SourceProbe probe,
        DateTimeOffset now,
        string evidence)
    {
        state.SourceId = state.SourceId.NextGeneration();
        state.Exists = true;
        state.PreviousLength = 0;
        state.Length = probe.Length;
        state.LastWriteTime = probe.LastWriteTime;
        state.CreationTime = probe.CreationTime;
        state.FirstObservedAt = now;
        state.FirstGrowthAt = null;
        state.LastGrowthAt = null;
        state.IsTruncated = false;
        state.IsReplaced = true;
        state.ReplacementEvidence = evidence;
        state.IsRolloverCandidate = false;
        state.RolloverPredecessorSourceId = null;
        state.RolloverReason = null;
        state.WasMissing = false;
        state.DisappearedAt = null;
        state.UnavailableReason = null;
        state.LastChangeKind = LogSourceChangeKind.Replaced;
        state.ActivityState = LogSourceActivityState.Replaced;
    }

    private LogSourceActivityState DeriveIdleState(SourceState state, DateTimeOffset now)
    {
        if (state.LastGrowthAt is { } lastGrowth)
        {
            return now - lastGrowth > _options.InactivityThreshold
                ? LogSourceActivityState.Inactive
                : LogSourceActivityState.Growing;
        }

        return state.IsCurrentDailyFile
            ? LogSourceActivityState.Waiting
            : LogSourceActivityState.Historical;
    }

    /// <summary>
    /// Marks a newer same-account source as a likely daily rollover of an older one. This is a
    /// reported observation only: it makes no claim that the character is unchanged or that a
    /// gameplay session should continue, and neither source is suppressed from the snapshot.
    /// </summary>
    private void MarkRolloverCandidates()
    {
        foreach (var group in _states.Values.GroupBy(
                     state => state.AccountStableId,
                     StringComparer.Ordinal))
        {
            var ordered = group.OrderBy(state => state.LogDate).ToList();

            foreach (var candidate in ordered)
            {
                if (candidate.IsRolloverCandidate || candidate.FirstGrowthAt is not { } newFirstGrowth)
                {
                    continue;
                }

                SourceState? predecessor = null;
                foreach (var older in ordered)
                {
                    if (older.LogDate >= candidate.LogDate
                        || older.LastGrowthAt is not { } olderLastGrowth
                        || newFirstGrowth < olderLastGrowth
                        || older.ActivityState is not (LogSourceActivityState.Inactive
                            or LogSourceActivityState.Unavailable))
                    {
                        continue;
                    }

                    if (predecessor is null || older.LogDate > predecessor.LogDate)
                    {
                        predecessor = older;
                    }
                }

                if (predecessor is null)
                {
                    continue;
                }

                candidate.IsRolloverCandidate = true;
                candidate.RolloverPredecessorSourceId = predecessor.SourceId.Value;
                candidate.RolloverReason =
                    $"Same account '{candidate.AccountDisplayName}'. Older source '{predecessor.SourceId.FileName}' "
                    + $"({predecessor.LogDate:yyyy-MM-dd}) last grew at {predecessor.LastGrowthAt:O} and is now "
                    + $"{predecessor.ActivityState}. Newer source '{candidate.SourceId.FileName}' "
                    + $"({candidate.LogDate:yyyy-MM-dd}) first grew at {newFirstGrowth:O}.";
            }
        }
    }

    private void RecordScanFailure(DateTimeOffset startedAt, long startTimestamp, string message)
    {
        lock (_sync)
        {
            _lastScanFailureMessage = message;
            _scanCount++;
            _lastScanStartedAt = startedAt;
            _lastScanDuration = _timeProvider.GetElapsedTime(startTimestamp);
            AppendScanFailure(message);
        }
    }

    private void AppendScanFailure(string message)
    {
        _recentScanFailures.Add(message);
        while (_recentScanFailures.Count > _options.RetainedScanFailureCount)
        {
            _recentScanFailures.RemoveAt(0);
        }
    }

    private static void AppendSourceDiagnosticEvents(
        SourceDiagnosticState? previous,
        SourceDiagnosticState next,
        ICollection<DiagnosticEvent> events)
    {
        if (previous is null)
        {
            events.Add(new LogActivitySourceDiscoveredDiagnosticEvent
            {
                SourceId = next.SourceId,
                AccountStableId = next.AccountStableId,
                SourceFileName = next.SourceFileName,
                ActivityState = next.ActivityState,
                Length = next.Length,
                LastGrowthAt = next.LastGrowthAt?.ToUniversalTime()
            });
            return;
        }

        if (!string.Equals(previous.SourceId, next.SourceId, StringComparison.Ordinal))
        {
            events.Add(new LogActivitySourceReplacedDiagnosticEvent
            {
                PreviousSourceId = previous.SourceId,
                SourceId = next.SourceId,
                AccountStableId = next.AccountStableId,
                SourceFileName = next.SourceFileName,
                PreviousActivityState = previous.ActivityState,
                NextActivityState = next.ActivityState,
                PreviousLength = previous.Length,
                Length = next.Length,
                Reason = "ObservedFileReplacement"
            });
            return;
        }

        if (previous.Exists && !next.Exists)
        {
            events.Add(new LogActivitySourceLostDiagnosticEvent
            {
                SourceId = next.SourceId,
                AccountStableId = next.AccountStableId,
                SourceFileName = next.SourceFileName,
                PreviousActivityState = previous.ActivityState,
                NextActivityState = next.ActivityState,
                Length = next.Length,
                LastGrowthAt = next.LastGrowthAt?.ToUniversalTime(),
                Reason = "FileNoLongerPresent"
            });
            return;
        }

        if (previous.ActivityState == next.ActivityState)
        {
            return;
        }

        events.Add(new LogActivitySourceStateChangedDiagnosticEvent
        {
            SourceId = next.SourceId,
            AccountStableId = next.AccountStableId,
            SourceFileName = next.SourceFileName,
            PreviousActivityState = previous.ActivityState,
            NextActivityState = next.ActivityState,
            PreviousLength = previous.Length,
            Length = next.Length,
            LastGrowthAt = next.LastGrowthAt?.ToUniversalTime(),
            Reason = SourceTransitionReason(next)
        });
    }

    private static string SourceTransitionReason(SourceDiagnosticState state) =>
        state.ChangeKind switch
        {
            LogSourceChangeKind.Grew => "ObservedLengthIncrease",
            LogSourceChangeKind.Truncated => "ObservedLengthDecrease",
            LogSourceChangeKind.Inaccessible => "MetadataUnavailable",
            LogSourceChangeKind.Unchanged when state.ActivityState == LogSourceActivityState.Inactive =>
                "InactivityThresholdElapsed",
            _ => state.ChangeKind.ToString()
        };

    private void WriteDiagnostics(IEnumerable<DiagnosticEvent> diagnosticEvents)
    {
        foreach (var diagnosticEvent in diagnosticEvents)
        {
            try
            {
                _diagnosticLog?.Write(diagnosticEvent);
            }
            catch
            {
                // Diagnostics are observational and must never affect source observation.
            }
        }
    }

    private static LogSourceCandidate ToCandidate(SourceState state) => new()
    {
        SourceId = state.SourceId,
        AccountStableId = state.AccountStableId,
        AccountDisplayName = state.AccountDisplayName,
        FilePath = state.FilePath,
        FileName = state.SourceId.FileName,
        LogDate = state.LogDate,
        Exists = state.Exists,
        Length = state.Length,
        PreviousLength = state.PreviousLength,
        LastWriteTime = state.LastWriteTime,
        CreationTime = state.CreationTime,
        FirstObservedAt = state.FirstObservedAt,
        LastObservedAt = state.LastObservedAt,
        FirstGrowthAt = state.FirstGrowthAt,
        LastGrowthAt = state.LastGrowthAt,
        ActivityState = state.ActivityState,
        LastChangeKind = state.LastChangeKind,
        IsCurrentDailyFile = state.IsCurrentDailyFile,
        IsRolloverCandidate = state.IsRolloverCandidate,
        RolloverPredecessorSourceId = state.RolloverPredecessorSourceId,
        RolloverReason = state.RolloverReason,
        IsTruncated = state.IsTruncated,
        IsReplaced = state.IsReplaced,
        ReplacementEvidence = state.ReplacementEvidence,
        UnavailableReason = state.UnavailableReason
    };

    private static LogActivitySourceDiagnostics ToSourceDiagnostics(LogSourceCandidate candidate) => new()
    {
        SourceId = candidate.SourceId.Value,
        IdentityGeneration = candidate.SourceId.IdentityGeneration,
        AccountDisplayName = candidate.AccountDisplayName,
        FileName = candidate.FileName,
        RedactedFilePath = DiagnosticPathRedactor.Redact(candidate.FilePath),
        LogDate = candidate.LogDate,
        Exists = candidate.Exists,
        PreviousLength = candidate.PreviousLength,
        CurrentLength = candidate.Length,
        FirstObservedAt = candidate.FirstObservedAt,
        LastObservedAt = candidate.LastObservedAt,
        FirstGrowthAt = candidate.FirstGrowthAt,
        LastGrowthAt = candidate.LastGrowthAt,
        ActivityState = candidate.ActivityState,
        LastChangeKind = candidate.LastChangeKind,
        IsCurrentDailyFile = candidate.IsCurrentDailyFile,
        IsTruncated = candidate.IsTruncated,
        IsReplaced = candidate.IsReplaced,
        ReplacementEvidence = candidate.ReplacementEvidence,
        IsRolloverCandidate = candidate.IsRolloverCandidate,
        RolloverReason = candidate.RolloverReason,
        UnavailableReason = candidate.UnavailableReason
    };

    private sealed class SourceState
    {
        public required LogSourceId SourceId { get; set; }

        public required string AccountStableId { get; init; }

        public required string AccountDisplayName { get; set; }

        public required string AccountFolderPath { get; init; }

        public required string FilePath { get; init; }

        public required DateOnly LogDate { get; init; }

        public bool Exists { get; set; }

        public long Length { get; set; }

        public long PreviousLength { get; set; }

        public DateTimeOffset? LastWriteTime { get; set; }

        public DateTimeOffset? CreationTime { get; set; }

        public required DateTimeOffset FirstObservedAt { get; set; }

        public required DateTimeOffset LastObservedAt { get; set; }

        public DateTimeOffset? FirstGrowthAt { get; set; }

        public DateTimeOffset? LastGrowthAt { get; set; }

        public LogSourceActivityState ActivityState { get; set; }

        public LogSourceChangeKind LastChangeKind { get; set; }

        public bool IsCurrentDailyFile { get; set; }

        public bool IsRolloverCandidate { get; set; }

        public string? RolloverPredecessorSourceId { get; set; }

        public string? RolloverReason { get; set; }

        public bool IsTruncated { get; set; }

        public bool IsReplaced { get; set; }

        public string? ReplacementEvidence { get; set; }

        public string? UnavailableReason { get; set; }

        /// <summary>Whether absence has been observed since the file was last present.</summary>
        public bool WasMissing { get; set; }

        public DateTimeOffset? DisappearedAt { get; set; }
    }

    private sealed record SourceDiagnosticState(
        string SourceId,
        string AccountStableId,
        string SourceFileName,
        bool Exists,
        long Length,
        DateTimeOffset? LastGrowthAt,
        LogSourceActivityState ActivityState,
        LogSourceChangeKind ChangeKind)
    {
        public static SourceDiagnosticState From(SourceState state) => new(
            state.SourceId.Value,
            state.AccountStableId,
            state.SourceId.FileName,
            state.Exists,
            state.Length,
            state.LastGrowthAt,
            state.ActivityState,
            state.LastChangeKind);
    }

    private readonly record struct RetainedSource(
        string FilePath,
        string AccountStableId,
        string AccountDisplayName,
        string AccountFolderPath,
        DateOnly LogDate);

    private readonly record struct SourceProbe(
        string FilePath,
        string AccountStableId,
        string AccountDisplayName,
        string AccountFolderPath,
        DateOnly LogDate,
        bool Exists,
        long Length,
        DateTimeOffset? LastWriteTime,
        DateTimeOffset? CreationTime,
        string? Error);

    private sealed record ScanPassResult(
        IReadOnlyList<SourceProbe> Probes,
        IReadOnlyList<LogActivityAccountDiagnostics> Accounts,
        IReadOnlyList<string> ObservedLogsFolders,
        IReadOnlyList<string> Failures,
        int LogsFolderCount);
}
