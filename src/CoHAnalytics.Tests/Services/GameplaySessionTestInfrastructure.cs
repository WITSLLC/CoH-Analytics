using System.IO;
using CoHAnalytics.Models;
using CoHAnalytics.ReferenceData;
using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Services;

internal static class GameplaySessionTestInfrastructure
{
    internal const int DefaultWorkQueueCapacity = 512;

    internal const int DefaultPendingCommittedCapacity = 4096;

    internal const int DefaultMonitoringSnapshotQueueCapacity = 256;

    internal const int DefaultEventQueueCapacity = 1024;

    internal static QueuePressureDiagnostics IdleQueue(int capacity, long lifecycleEpoch = 0) =>
        QueuePressureDiagnostics.Idle(capacity, lifecycleEpoch);

    internal static PendingCommittedEventDiagnostics IdlePending(int capacity) =>
        PendingCommittedEventDiagnostics.Empty(capacity);

    internal static ParserManagerDiagnostics IdleParserDiagnostics(
        bool isRunning = true,
        int monitoringSnapshotQueueCapacity = DefaultMonitoringSnapshotQueueCapacity,
        int eventQueueCapacity = DefaultEventQueueCapacity,
        long lifecycleEpoch = 0,
        bool monitoringSnapshotQueueOverflowed = false,
        bool eventQueueOverflowed = false) =>
        new()
        {
            IsRunning = isRunning,
            LastMonitoringSnapshotRevision = 0,
            LastParserSnapshotRevision = 0,
            LifecycleEpoch = lifecycleEpoch,
            QueuedEventCount = 0,
            MonitoringSnapshotQueue = IdleQueue(
                monitoringSnapshotQueueCapacity,
                lifecycleEpoch) with { Overflowed = monitoringSnapshotQueueOverflowed },
            EventQueue = IdleQueue(eventQueueCapacity, lifecycleEpoch) with { Overflowed = eventQueueOverflowed },
            MonitoringSnapshotQueueOverflowed = monitoringSnapshotQueueOverflowed,
            EventQueueOverflowed = eventQueueOverflowed,
            Classification = new ParserClassificationDiagnostics
            {
                SnapshotRevision = 0,
                TotalClassifiedLines = 0,
                RecognizedLineCount = 0,
                UnknownLineCount = 0,
                MalformedLineCount = 0,
                PotentialIdentityEvidenceCount = 0,
                ClassifierFailureCount = 0,
                LastClassifiedEventAt = null,
                RuleMatchCounts = new Dictionary<string, long>(),
                RecentClassifierFailures = []
            },
            Workers = [],
            RecentDecisions = []
        };

    internal static GameplaySessionDiagnostics IdleGameplayDiagnostics(
        bool isRunning = true,
        long snapshotRevision = 0,
        int workQueueCapacity = DefaultWorkQueueCapacity,
        int pendingCommittedCapacity = DefaultPendingCommittedCapacity,
        long lifecycleEpoch = 0,
        bool workQueueOverflowed = false,
        long lastAcceptedWorkSequence = 0,
        long lastCompletedWorkSequence = 0,
        int activeProcessorCallbackCount = 0,
        int activeSessionCount = 0,
        long totalCommittedEvents = 0,
        DateTimeOffset? lastCommittedEventAt = null,
        int failedContextCount = 0,
        int pendingCommittedEventCount = 0) =>
        new()
        {
            IsRunning = isRunning,
            SnapshotRevision = snapshotRevision,
            LifecycleEpoch = lifecycleEpoch,
            ActiveSessionCount = activeSessionCount,
            SuspendedSessionCount = 0,
            NeedsAttentionSessionCount = 0,
            OverflowedSessionCount = 0,
            TotalCommittedEvents = totalCommittedEvents,
            LastCommittedEventAt = lastCommittedEventAt,
            FailedContextCount = failedContextCount,
            PendingCommittedEventCount = pendingCommittedEventCount,
            WorkQueue = IdleQueue(workQueueCapacity, lifecycleEpoch) with { Overflowed = workQueueOverflowed },
            PendingCommittedEvents = IdlePending(pendingCommittedCapacity) with
            {
                CurrentCount = pendingCommittedEventCount
            },
            WorkQueueOverflowed = workQueueOverflowed,
            PreStartBufferOverflowed = false,
            LastAcceptedWorkSequence = lastAcceptedWorkSequence,
            LastCompletedWorkSequence = lastCompletedWorkSequence,
            ActiveProcessorCallbackCount = activeProcessorCallbackCount,
            RecentOperations = []
        };

    internal static CharacterRepository CreateRepository(out string dataDirectory)
    {
        dataDirectory = Path.Combine(Path.GetTempPath(), "coh-analytics-gameplay", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dataDirectory);
        return new CharacterRepository(new CharacterRepositoryOptions { DataDirectory = dataDirectory });
    }

    internal static async Task WaitUntilAsync(Func<bool> condition, int timeoutMilliseconds = 3000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException("Gameplay session test condition was not reached.");
            }

            await Task.Delay(10);
        }
    }

    internal static Task WaitForWorkQueueToDrainAsync(IGameplaySessionManager manager) =>
        WaitUntilAsync(() =>
        {
            var diagnostics = manager.GetDiagnostics();
            return diagnostics.WorkQueue.IsDrained
                && diagnostics.LastAcceptedWorkSequence == diagnostics.LastCompletedWorkSequence
                && diagnostics.ActiveProcessorCallbackCount == 0;
        });

    internal static ParserEvent Classify(
        string line,
        MonitoringContextId contextId,
        LogSourceId source,
        long sequence = 1,
        MonitoringSourceTransitionKind transition = MonitoringSourceTransitionKind.SourceAssigned,
        long bindingGeneration = 1,
        DateTimeOffset? observedAt = null)
    {
        var classifier = new ParserClassifier();
        var raw = new ParserRawEvent
        {
            ContextId = contextId,
            SourceId = source,
            SourceSegmentId = ParserSourceSegmentId.CreateNew(),
            BindingGeneration = bindingGeneration,
            SourceTransitionKind = transition,
            Sequence = sequence,
            ObservedAt = observedAt
                ?? new DateTimeOffset(2026, 8, 4, 6, 28, 14, TimeSpan.Zero).AddSeconds(sequence),
            RawLine = line,
            SourceByteStart = 0,
            SourceByteEnd = line.Length + 2,
            LineStatus = ParserLineStatus.Complete
        };

        return classifier.Classify(raw);
    }

    internal static LogSourceId DefaultSource(string accountId = "acct-1") =>
        LogSourceId.Create(
            accountId,
            accountId,
            @"C:\fake\chatlog 2026-08-04.txt",
            new DateOnly(2026, 8, 4));

    internal static MonitoringContextSnapshot ReadyContext(
        MonitoringContextId contextId,
        LogSourceId source,
        MonitoringSourceTransitionKind transition = MonitoringSourceTransitionKind.SourceAssigned,
        long startupRecoveryStartOffset = 0,
        LogSourceId? startupRecoveryPredecessorSource = null,
        long startupRecoveryPredecessorStartOffset = 0,
        HomecomingProcessInstance? processInstance = null) =>
        ParserTestSnapshots.Context(
            contextId,
            MonitoringContextState.Ready,
            source,
            1,
            transition,
            startupRecoveryStartOffset: startupRecoveryStartOffset,
            startupRecoveryPredecessorSource: startupRecoveryPredecessorSource,
            startupRecoveryPredecessorStartOffset: startupRecoveryPredecessorStartOffset,
            processInstance: processInstance);

    internal sealed class FakeGameplayParserManager : IParserManager
    {
        public ParserManagerSnapshot Current { get; set; } = ParserManagerSnapshot.Empty;

        public ParserClassificationSnapshot ClassificationCurrent { get; set; } =
            ParserClassificationSnapshot.Empty;

        public int ClassifiedEventsSubscriptionCount { get; private set; }

        private event EventHandler<ParserEventsClassifiedEventArgs>? ClassifiedEventsAvailableCore;

        public event EventHandler<ParserManagerChangedEventArgs>? StateChanged
        {
            add { }
            remove { }
        }

        public event EventHandler<ParserEventsAvailableEventArgs>? EventsAvailable
        {
            add { }
            remove { }
        }

        public event EventHandler<ParserClassificationChangedEventArgs>? ClassificationChanged
        {
            add { }
            remove { }
        }

        public event EventHandler<ParserEventsClassifiedEventArgs>? ClassifiedEventsAvailable
        {
            add
            {
                ClassifiedEventsSubscriptionCount++;
                ClassifiedEventsAvailableCore += value;
            }
            remove
            {
                ClassifiedEventsSubscriptionCount--;
                ClassifiedEventsAvailableCore -= value;
            }
        }

        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public ParserManagerDiagnostics GetDiagnostics() =>
            GameplaySessionTestInfrastructure.IdleParserDiagnostics();

        public ParserClassificationDiagnostics GetClassificationDiagnostics() => new()
        {
            SnapshotRevision = 0,
            TotalClassifiedLines = 0,
            RecognizedLineCount = 0,
            UnknownLineCount = 0,
            MalformedLineCount = 0,
            PotentialIdentityEvidenceCount = 0,
            ClassifierFailureCount = 0,
            LastClassifiedEventAt = null,
            RuleMatchCounts = new Dictionary<string, long>(),
            RecentClassifierFailures = []
        };

        public void PublishClassified(IReadOnlyList<ParserEvent> events) =>
            ClassifiedEventsAvailableCore?.Invoke(this, new ParserEventsClassifiedEventArgs(events));
    }

    internal static async Task CauseOverloadAsync(
        GameplaySessionManager manager,
        FakeMonitoringSessionManager monitoring,
        FakeGameplayParserManager parser,
        MonitoringContextId contextId,
        LogSourceId source)
    {
        var processingStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseProcessing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void BlockProcessor()
        {
            processingStarted.TrySetResult();
            releaseProcessing.Task.Wait();
        }

        manager.CommittedEventsAvailable += (_, _) => BlockProcessor();
        manager.StateChanged += (_, _) => BlockProcessor();

        parser.PublishClassified([
            Classify(
                "2026-08-04 06:27:10 Welcome to City of Heroes, Stall Hero!",
                contextId,
                source)
        ]);

        await processingStarted.Task;
        monitoring.Publish(ParserTestSnapshots.Snapshot(
            2,
            ReadyContext(contextId, source)));
        parser.PublishClassified([
            Classify("2026-08-04 06:27:11 overflow line", contextId, source, 2)
        ]);

        await WaitUntilAsync(() => manager.GetDiagnostics().WorkQueueOverflowed);
        releaseProcessing.TrySetResult();
    }

    internal static CharacterBadgeAcquisitionRepository CreateBadgeRepository(
        out string dataDirectory,
        CharacterRepositoryOptions? characterOptions = null)
    {
        dataDirectory = Path.Combine(Path.GetTempPath(), "coh-analytics-badge", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dataDirectory);
        return new CharacterBadgeAcquisitionRepository(new CharacterBadgeAcquisitionRepositoryOptions
        {
            DataDirectory = dataDirectory
        });
    }

    internal static IBadgeAcquisitionResolver CreateProductionBadgeResolver() =>
        new BadgeAcquisitionResolver(ItemReferenceCatalogFactory.LoadEmbeddedProduction());

    internal static async Task<GameplaySessionManager> CreateStartedManager(
        FakeMonitoringSessionManager monitoring,
        FakeGameplayParserManager parser,
        CharacterRepository repository,
        GameplaySessionOptions? options = null,
        IGameplayReceivedItemClassifier? receivedItemClassifier = null,
        IBadgeAcquisitionResolver? badgeAcquisitionResolver = null,
        ICharacterBadgeAcquisitionRepository? badgeAcquisitionRepository = null,
        ICharacterPerformanceObservationRepository? historicalObservationRepository = null)
    {
        var manager = new GameplaySessionManager(
            monitoring,
            parser,
            repository,
            options,
            receivedItemClassifier: receivedItemClassifier,
            badgeAcquisitionResolver: badgeAcquisitionResolver,
            badgeAcquisitionRepository: badgeAcquisitionRepository,
            historicalObservationRepository: historicalObservationRepository);
        await manager.StartAsync();
        return manager;
    }

    internal static string BadgeAwardLine(string badgeTitle) =>
        $"Congratulations! You earned the {badgeTitle} badge.";
}
