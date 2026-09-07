using System.Threading.Channels;
using CoHAnalytics.Models;
using CoHAnalytics.Services;

namespace CoHAnalytics.Replay;

public sealed record ReplayOracleProgress(
    long RawEventsObserved,
    long ClassifiedEventsObserved,
    long CommittedGameplayEventsObserved,
    int WelcomeBoundariesObserved,
    int ContextCount)
{
    public static ReplayOracleProgress Empty { get; } = new(0, 0, 0, 0, 0);
}

public sealed class ReplayCorrectnessOracle
{
    private const int ObservationChannelCapacity = 512;

    private readonly List<ReplayCorrectnessFailure> _failures = [];
    private readonly Dictionary<MonitoringContextId, ContextTracker> _contexts = [];
    private readonly Dictionary<string, MonitoringContextId> _accountBindings = new(StringComparer.Ordinal);
    private readonly List<string> _classificationRuleIds = [];
    private readonly Channel<OracleObservation> _channel;
    private readonly CancellationTokenSource _lifetimeCts = new();
    private readonly Task _consumerTask;
    private readonly object _completeLock = new();
    private long _rawEventsObserved;
    private long _classifiedEventsObserved;
    private long _committedEventsObserved;
    private int _welcomeBoundariesObserved;
    private int _lateObservationCount;
    private volatile ReplayOracleProgress _progress = ReplayOracleProgress.Empty;
    private OracleState _state = OracleState.Accepting;
    private Exception? _consumerFault;
    private ReplayCorrectnessReport? _cachedReport;

    public ReplayCorrectnessOracle()
    {
        _channel = Channel.CreateBounded<OracleObservation>(new BoundedChannelOptions(ObservationChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
        _consumerTask = Task.Run(() => ConsumeAsync(_lifetimeCts.Token));
    }

    public ReplayOracleProgress Progress => _progress;

    public IReadOnlyList<ReplayCorrectnessFailure> Failures => _failures;

    public long RawEventsObserved => _progress.RawEventsObserved;

    public long ClassifiedEventsObserved => _progress.ClassifiedEventsObserved;

    public long CommittedGameplayEventsObserved => _progress.CommittedGameplayEventsObserved;

    public int WelcomeBoundariesObserved => _progress.WelcomeBoundariesObserved;

    public int ContextCount => _progress.ContextCount;

    public int LateObservationCount => _lateObservationCount;

    public IReadOnlyList<string> ClassificationRuleIds => _classificationRuleIds;

    internal bool HasConsumerFault => _consumerFault is not null;

    public void BindAccount(string accountStableId, MonitoringContextId contextId) =>
        EnqueueObservation(new BindAccountObservation(accountStableId, contextId));

    public void ObserveRawEvents(IReadOnlyList<ParserRawEvent> events) =>
        EnqueueObservation(new RawEventsObservation(CopyRawEvents(events)));

    public void ObserveClassifiedEvents(IReadOnlyList<ParserEvent> events) =>
        EnqueueObservation(new ClassifiedEventsObservation(CopyClassifiedEvents(events)));

    public void ObserveCommittedEvents(IReadOnlyList<GameplaySessionEvent> events) =>
        EnqueueObservation(new CommittedEventsObservation(CopyCommittedEvents(events)));

    public Task RecordExternalFailureAsync(string code, string message)
    {
        if (_state != OracleState.Accepting)
        {
            Interlocked.Increment(ref _lateObservationCount);
            return Task.CompletedTask;
        }

        EnqueueObservation(new ExternalFailureObservation(code, message));
        return Task.CompletedTask;
    }

    public async Task<ReplayCorrectnessReport> CompleteAsync(
        bool beginsMidSession,
        int expectedWelcomeBoundaries,
        int expectedContextCount)
    {
        lock (_completeLock)
        {
            if (_state == OracleState.Finalized)
            {
                return _cachedReport!;
            }

            if (_state == OracleState.Accepting)
            {
                _state = OracleState.Draining;
            }
        }

        _channel.Writer.TryComplete();

        try
        {
            await _consumerTask.ConfigureAwait(false);
        }
        catch
        {
        }

        if (_consumerFault is not null)
        {
            AddFailure("consumer.fault", $"Oracle consumer faulted ({_consumerFault.GetType().Name}).");
        }

        VerifyContextIsolation(expectedContextCount);
        var report = BuildReport(beginsMidSession, expectedWelcomeBoundaries);

        lock (_completeLock)
        {
            _state = OracleState.Finalized;
            _cachedReport = report;
        }

        return report;
    }

    private async Task ConsumeAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var observation in _channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                ApplyObservation(observation);
                UpdateProgressSnapshot();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _consumerFault = ex;
        }
    }

    private void EnqueueObservation(OracleObservation observation)
    {
        if (_state != OracleState.Accepting)
        {
            Interlocked.Increment(ref _lateObservationCount);
            return;
        }

        if (!_channel.Writer.TryWrite(observation))
        {
            _channel.Writer.WriteAsync(observation).AsTask().GetAwaiter().GetResult();
        }
    }

    private void ApplyObservation(OracleObservation observation)
    {
        switch (observation)
        {
            case BindAccountObservation(var accountStableId, var contextId):
                _accountBindings[accountStableId] = contextId;
                break;
            case RawEventsObservation(var events):
                ApplyRawEvents(events);
                break;
            case ClassifiedEventsObservation(var events):
                ApplyClassifiedEvents(events);
                break;
            case CommittedEventsObservation(var events):
                ApplyCommittedEvents(events);
                break;
            case ExternalFailureObservation(var code, var message):
                AddFailure(code, message);
                break;
        }
    }

    private void ApplyRawEvents(IReadOnlyList<RawEventData> events)
    {
        foreach (var parserEvent in events)
        {
            _rawEventsObserved++;
            var tracker = GetTracker(parserEvent.ContextId, parserEvent.AccountStableId);
            if (parserEvent.Sequence <= tracker.LastRawSequence)
            {
                AddFailure(
                    "parser.duplicate",
                    $"Duplicate raw parser sequence {parserEvent.Sequence} in context {parserEvent.ContextId}.");
            }

            if (tracker.LastRawSequence > 0 && parserEvent.Sequence != tracker.LastRawSequence + 1)
            {
                AddFailure(
                    "parser.gap",
                    $"Raw parser sequence gap before {parserEvent.Sequence} in context {parserEvent.ContextId}.");
            }

            tracker.LastRawSequence = parserEvent.Sequence;
        }
    }

    private void ApplyClassifiedEvents(IReadOnlyList<ClassifiedEventData> events)
    {
        foreach (var parserEvent in events)
        {
            _classifiedEventsObserved++;
            _classificationRuleIds.Add(parserEvent.ClassificationRuleId);
            var tracker = GetTracker(parserEvent.ContextId, parserEvent.AccountStableId);
            if (parserEvent.Sequence <= tracker.LastClassifiedSequence)
            {
                AddFailure(
                    "parser.duplicate",
                    $"Duplicate classified parser sequence {parserEvent.Sequence} in context {parserEvent.ContextId}.");
            }

            if (tracker.LastClassifiedSequence > 0 && parserEvent.Sequence != tracker.LastClassifiedSequence + 1)
            {
                AddFailure(
                    "parser.gap",
                    $"Classified parser sequence gap before {parserEvent.Sequence} in context {parserEvent.ContextId}.");
            }

            tracker.LastClassifiedSequence = parserEvent.Sequence;

            if (parserEvent.IsWelcomeBoundary)
            {
                _welcomeBoundariesObserved++;
                tracker.WelcomeBoundariesObserved++;
            }
        }
    }

    private void ApplyCommittedEvents(IReadOnlyList<CommittedEventData> events)
    {
        foreach (var gameplayEvent in events)
        {
            _committedEventsObserved++;
            var tracker = GetTracker(gameplayEvent.ContextId, gameplayEvent.AccountStableId);

            if (gameplayEvent.IsWelcomeBoundary)
            {
                tracker.ResetCommittedSession(gameplayEvent.SessionId);
            }

            if (!tracker.CommittedSequences.Add(gameplayEvent.SessionSequence))
            {
                AddFailure(
                    "gameplay.duplicate",
                    $"Duplicate committed gameplay sequence {gameplayEvent.SessionSequence} in context {gameplayEvent.ContextId}.");
            }

            if (tracker.LastCommittedSequence > 0
                && gameplayEvent.SessionSequence != tracker.LastCommittedSequence + 1)
            {
                AddFailure(
                    "gameplay.gap",
                    $"Committed gameplay sequence gap before {gameplayEvent.SessionSequence} in context {gameplayEvent.ContextId}.");
            }

            tracker.LastCommittedSequence = gameplayEvent.SessionSequence;

            if (tracker.ActiveSessionId is not null
                && tracker.ActiveSessionId != gameplayEvent.SessionId
                && !gameplayEvent.IsWelcomeBoundary)
            {
                AddFailure(
                    "gameplay.session-crossing",
                    $"Committed gameplay event crossed sessions in context {gameplayEvent.ContextId}.");
            }

            tracker.ActiveSessionId = gameplayEvent.SessionId;
        }
    }

    private void UpdateProgressSnapshot() =>
        _progress = new ReplayOracleProgress(
            _rawEventsObserved,
            _classifiedEventsObserved,
            _committedEventsObserved,
            _welcomeBoundariesObserved,
            _contexts.Count);

    public void VerifyContextIsolation(int expectedContextCount)
    {
        var expectedContextIds = new HashSet<MonitoringContextId>(_accountBindings.Values);
        var observedContextIds = new HashSet<MonitoringContextId>(_contexts.Keys);

        foreach (var expectedContextId in expectedContextIds)
        {
            if (!observedContextIds.Contains(expectedContextId))
            {
                AddFailure(
                    "context.missing",
                    $"Expected replay context {expectedContextId} was not observed.");
            }
        }

        foreach (var observedContextId in observedContextIds)
        {
            if (!expectedContextIds.Contains(observedContextId))
            {
                AddFailure(
                    "context.isolation",
                    $"Unexpected replay context {observedContextId} was observed.");
            }
        }

        var boundContextIds = _accountBindings.Values.ToList();
        if (boundContextIds.Count != boundContextIds.Distinct().Count())
        {
            AddFailure(
                "account.multi-context",
                "One or more accounts were bound to multiple replay contexts.");
        }
    }

    private ReplayCorrectnessReport BuildReport(bool beginsMidSession, int expectedWelcomeBoundaries)
    {
        if (expectedWelcomeBoundaries > 0 && _welcomeBoundariesObserved < expectedWelcomeBoundaries)
        {
            AddFailure(
                "session.welcome-missing",
                "Expected Welcome boundary was not observed in parser output.");
        }

        if (_welcomeBoundariesObserved > expectedWelcomeBoundaries)
        {
            AddFailure(
                "session.welcome-unexpected",
                "Unexpected Welcome boundary count in parser output.");
        }

        return new ReplayCorrectnessReport
        {
            Passed = _failures.Count == 0,
            RawEventsObserved = _rawEventsObserved,
            ClassifiedEventsObserved = _classifiedEventsObserved,
            CommittedGameplayEventsObserved = _committedEventsObserved,
            ContextCount = _contexts.Count,
            WelcomeBoundariesObserved = _welcomeBoundariesObserved,
            BeginsMidSession = beginsMidSession,
            ClassificationRuleIds = _classificationRuleIds.ToArray(),
            Failures = _failures.ToArray(),
            LateObservationCount = _lateObservationCount
        };
    }

    private ContextTracker GetTracker(MonitoringContextId contextId, string accountStableId)
    {
        if (_accountBindings.TryGetValue(accountStableId, out var boundContext)
            && boundContext != contextId)
        {
            AddFailure(
                "context.contamination",
                $"Parser event account crossed into context {contextId}.");
        }

        if (!_contexts.TryGetValue(contextId, out var tracker))
        {
            tracker = new ContextTracker(accountStableId);
            _contexts[contextId] = tracker;
        }
        else if (!string.Equals(tracker.AccountStableId, accountStableId, StringComparison.Ordinal))
        {
            AddFailure(
                "account.contamination",
                $"Account ownership changed within context {contextId}.");
        }

        return tracker;
    }

    internal static bool IsWelcomeBoundary(ParserEvent parserEvent) =>
        parserEvent.EventKind == ParserEventKind.PotentialIdentityEvidence
        && parserEvent.StructuralEvidence is
        {
            EvidenceKind: ParserStructuralEvidenceKind.WelcomeAttribution,
            AttributionStrength: ParserAttributionStrength.Strong
        }
        && !string.IsNullOrWhiteSpace(parserEvent.StructuralEvidence.CandidateName);

    private static IReadOnlyList<RawEventData> CopyRawEvents(IReadOnlyList<ParserRawEvent> events)
    {
        var copied = new RawEventData[events.Count];
        for (var index = 0; index < events.Count; index++)
        {
            var parserEvent = events[index];
            copied[index] = new RawEventData(
                parserEvent.ContextId,
                parserEvent.SourceId.AccountStableId,
                parserEvent.Sequence);
        }

        return copied;
    }

    private static IReadOnlyList<ClassifiedEventData> CopyClassifiedEvents(IReadOnlyList<ParserEvent> events)
    {
        var copied = new ClassifiedEventData[events.Count];
        for (var index = 0; index < events.Count; index++)
        {
            var parserEvent = events[index];
            copied[index] = new ClassifiedEventData(
                parserEvent.ContextId,
                parserEvent.SourceId.AccountStableId,
                parserEvent.Sequence,
                parserEvent.ClassificationRuleId,
                IsWelcomeBoundary(parserEvent));
        }

        return copied;
    }

    private static IReadOnlyList<CommittedEventData> CopyCommittedEvents(IReadOnlyList<GameplaySessionEvent> events)
    {
        var copied = new CommittedEventData[events.Count];
        for (var index = 0; index < events.Count; index++)
        {
            var gameplayEvent = events[index];
            copied[index] = new CommittedEventData(
                gameplayEvent.ContextId,
                gameplayEvent.ParserEvent.SourceId.AccountStableId,
                gameplayEvent.SessionId,
                gameplayEvent.SessionSequence,
                IsWelcomeBoundary(gameplayEvent.ParserEvent));
        }

        return copied;
    }

    private void AddFailure(string code, string message) =>
        _failures.Add(new ReplayCorrectnessFailure(code, message));

    private enum OracleState
    {
        Accepting,
        Draining,
        Finalized
    }

    private abstract record OracleObservation;

    private sealed record BindAccountObservation(string AccountStableId, MonitoringContextId ContextId)
        : OracleObservation;

    private sealed record RawEventsObservation(IReadOnlyList<RawEventData> Events) : OracleObservation;

    private sealed record ClassifiedEventsObservation(IReadOnlyList<ClassifiedEventData> Events) : OracleObservation;

    private sealed record CommittedEventsObservation(IReadOnlyList<CommittedEventData> Events) : OracleObservation;

    private sealed record ExternalFailureObservation(string Code, string Message) : OracleObservation;

    private readonly record struct RawEventData(
        MonitoringContextId ContextId,
        string AccountStableId,
        long Sequence);

    private readonly record struct ClassifiedEventData(
        MonitoringContextId ContextId,
        string AccountStableId,
        long Sequence,
        string ClassificationRuleId,
        bool IsWelcomeBoundary);

    private readonly record struct CommittedEventData(
        MonitoringContextId ContextId,
        string AccountStableId,
        GameplaySessionId SessionId,
        long SessionSequence,
        bool IsWelcomeBoundary);

    private sealed class ContextTracker(string accountStableId)
    {
        public string AccountStableId { get; } = accountStableId;

        public HashSet<long> CommittedSequences { get; } = [];

        public long LastRawSequence { get; set; }

        public long LastClassifiedSequence { get; set; }

        public long LastCommittedSequence { get; set; }

        public int WelcomeBoundariesObserved { get; set; }

        public GameplaySessionId? ActiveSessionId { get; set; }

        public void ResetCommittedSession(GameplaySessionId sessionId)
        {
            CommittedSequences.Clear();
            LastCommittedSequence = 0;
            ActiveSessionId = sessionId;
        }
    }
}
