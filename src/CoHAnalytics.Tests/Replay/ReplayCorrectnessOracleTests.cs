using CoHAnalytics.Models;
using CoHAnalytics.Replay;
using CoHAnalytics.Services;
namespace CoHAnalytics.Tests.Replay;

public sealed class ReplayCorrectnessOracleTests
{
    [Fact]
    public async Task Duplicate_raw_sequence_is_reported()
    {
        var oracle = new ReplayCorrectnessOracle();
        var contextId = MonitoringContextId.CreateNew();
        var source = LogSourceId.Create(
            "acct-a",
            "acct-a",
            @"C:\fake\chatlog 2026-01-01.txt",
            new DateOnly(2026, 1, 1));
        oracle.BindAccount("acct-a", contextId);

        var events = new[]
        {
            CreateRaw(contextId, source, 1),
            CreateRaw(contextId, source, 1)
        };

        oracle.ObserveRawEvents(events);
        var report = await oracle.CompleteAsync(beginsMidSession: false, expectedWelcomeBoundaries: 0, expectedContextCount: 1);

        Assert.False(report.Passed);
        AssertContainsFailureCode(report, "parser.duplicate");
        AssertDoesNotContainFailureCodes(report, "gameplay.gap", "session.welcome-missing", "context.missing");
        AssertFailureMessagesArePayloadFree(report);
    }

    [Fact]
    public async Task Duplicate_classified_sequence_is_reported()
    {
        var oracle = new ReplayCorrectnessOracle();
        var contextId = MonitoringContextId.CreateNew();
        var source = LogSourceId.Create(
            "acct-a",
            "acct-a",
            @"C:\fake\chatlog 2026-01-01.txt",
            new DateOnly(2026, 1, 1));
        oracle.BindAccount("acct-a", contextId);

        var classified = CreateClassified(
            contextId,
            source,
            1,
            "2026-01-01 00:00:00 You are now leaving the example district.");
        oracle.ObserveClassifiedEvents([classified, classified]);
        var report = await oracle.CompleteAsync(beginsMidSession: false, expectedWelcomeBoundaries: 0, expectedContextCount: 1);

        Assert.False(report.Passed);
        AssertContainsFailureCode(report, "parser.duplicate");
        AssertDoesNotContainFailureCodes(report, "gameplay.gap", "session.welcome-missing", "context.missing");
        AssertFailureMessagesArePayloadFree(report);
    }

    [Fact]
    public async Task Raw_sequence_gap_is_reported()
    {
        var oracle = new ReplayCorrectnessOracle();
        var contextId = MonitoringContextId.CreateNew();
        var source = LogSourceId.Create(
            "acct-a",
            "acct-a",
            @"C:\fake\chatlog 2026-01-01.txt",
            new DateOnly(2026, 1, 1));
        oracle.BindAccount("acct-a", contextId);

        oracle.ObserveRawEvents([CreateRaw(contextId, source, 1), CreateRaw(contextId, source, 3)]);
        var report = await oracle.CompleteAsync(beginsMidSession: false, expectedWelcomeBoundaries: 0, expectedContextCount: 1);

        Assert.False(report.Passed);
        AssertContainsFailureCode(report, "parser.gap");
        AssertDoesNotContainFailureCodes(report, "parser.duplicate", "gameplay.gap", "session.welcome-missing");
        AssertFailureMessagesArePayloadFree(report);
    }

    [Fact]
    public async Task Classified_sequence_gap_is_reported()
    {
        var oracle = new ReplayCorrectnessOracle();
        var contextId = MonitoringContextId.CreateNew();
        var source = LogSourceId.Create(
            "acct-a",
            "acct-a",
            @"C:\fake\chatlog 2026-01-01.txt",
            new DateOnly(2026, 1, 1));
        oracle.BindAccount("acct-a", contextId);

        oracle.ObserveClassifiedEvents(
        [
            CreateClassified(contextId, source, 1, "2026-01-01 00:00:00 You are now leaving the example district."),
            CreateClassified(contextId, source, 3, "2026-01-01 00:00:01 You have received a souvenir.")
        ]);
        var report = await oracle.CompleteAsync(beginsMidSession: false, expectedWelcomeBoundaries: 0, expectedContextCount: 1);

        Assert.False(report.Passed);
        AssertContainsFailureCode(report, "parser.gap");
        AssertDoesNotContainFailureCodes(report, "parser.duplicate", "gameplay.gap", "session.welcome-missing");
        AssertFailureMessagesArePayloadFree(report);
    }

    [Fact]
    public async Task Gameplay_sequence_gap_is_reported()
    {
        var oracle = new ReplayCorrectnessOracle();
        var contextId = MonitoringContextId.CreateNew();
        var source = LogSourceId.Create(
            "acct-a",
            "acct-a",
            @"C:\fake\chatlog 2026-01-01.txt",
            new DateOnly(2026, 1, 1));
        oracle.BindAccount("acct-a", contextId);

        var welcome = CreateClassified(
            contextId,
            source,
            1,
            "2026-01-01 00:00:00 Welcome to City of Heroes, Example Hero!");
        var followUp = CreateClassified(
            contextId,
            source,
            2,
            "2026-01-01 00:00:01 You are now leaving the example district.");
        var sessionId = GameplaySessionId.CreateNew();

        oracle.ObserveCommittedEvents(
        [
            CreateCommitted(contextId, welcome, sessionId, 1),
            CreateCommitted(contextId, followUp, sessionId, 3)
        ]);

        var report = await oracle.CompleteAsync(beginsMidSession: false, expectedWelcomeBoundaries: 0, expectedContextCount: 1);
        Assert.False(report.Passed);
        AssertContainsFailureCode(report, "gameplay.gap");
        AssertDoesNotContainFailureCodes(report, "parser.gap", "parser.duplicate", "session.welcome-missing");
        AssertFailureMessagesArePayloadFree(report);
    }

    [Fact]
    public async Task Cross_context_account_contamination_is_reported()
    {
        var oracle = new ReplayCorrectnessOracle();
        var contextA = MonitoringContextId.CreateNew();
        var contextB = MonitoringContextId.CreateNew();
        oracle.BindAccount("acct-a", contextA);
        oracle.BindAccount("acct-b", contextB);

        var sourceB = LogSourceId.Create(
            "acct-b",
            "acct-b",
            @"C:\fake\chatlog 2026-01-02.txt",
            new DateOnly(2026, 1, 2));
        oracle.ObserveRawEvents(
        [
            CreateRaw(contextA, sourceB, 1),
            CreateRaw(contextB, sourceB, 2)
        ]);

        var report = await oracle.CompleteAsync(beginsMidSession: false, expectedWelcomeBoundaries: 0, expectedContextCount: 2);
        Assert.False(report.Passed);
        AssertContainsFailureCode(report, "context.contamination");
        AssertDoesNotContainFailureCodes(report, "context.missing", "context.isolation", "parser.gap", "session.welcome-missing");
        AssertFailureMessagesArePayloadFree(report);
    }

    [Fact]
    public async Task Complete_reports_session_welcome_missing_when_boundary_not_observed()
    {
        var oracle = new ReplayCorrectnessOracle();
        var report = await oracle.CompleteAsync(beginsMidSession: false, expectedWelcomeBoundaries: 1, expectedContextCount: 0);

        Assert.False(report.Passed);
        AssertContainsFailureCode(report, "session.welcome-missing");
        AssertDoesNotContainFailureCodes(report, "session.welcome-unexpected", "parser.gap", "context.missing");
        AssertFailureMessagesArePayloadFree(report);
    }

    [Fact]
    public async Task Context_isolation_passes_when_zero_expected_contexts_observed()
    {
        var oracle = new ReplayCorrectnessOracle();
        var report = await oracle.CompleteAsync(beginsMidSession: false, expectedWelcomeBoundaries: 0, expectedContextCount: 0);

        Assert.True(report.Passed);
    }

    [Fact]
    public async Task Context_isolation_passes_when_one_expected_context_observed()
    {
        var oracle = new ReplayCorrectnessOracle();
        var contextId = MonitoringContextId.CreateNew();
        var source = LogSourceId.Create(
            "acct-a",
            "acct-a",
            @"C:\fake\chatlog 2026-01-01.txt",
            new DateOnly(2026, 1, 1));
        oracle.BindAccount("acct-a", contextId);
        oracle.ObserveRawEvents([CreateRaw(contextId, source, 1)]);

        var report = await oracle.CompleteAsync(beginsMidSession: false, expectedWelcomeBoundaries: 0, expectedContextCount: 1);
        Assert.True(report.Passed);
    }

    [Fact]
    public async Task Context_isolation_passes_for_two_isolated_contexts()
    {
        var oracle = new ReplayCorrectnessOracle();
        var contextA = MonitoringContextId.CreateNew();
        var contextB = MonitoringContextId.CreateNew();
        var sourceA = LogSourceId.Create(
            "acct-a",
            "acct-a",
            @"C:\fake\chatlog 2026-01-01.txt",
            new DateOnly(2026, 1, 1));
        var sourceB = LogSourceId.Create(
            "acct-b",
            "acct-b",
            @"C:\fake\chatlog 2026-01-02.txt",
            new DateOnly(2026, 1, 2));
        oracle.BindAccount("acct-a", contextA);
        oracle.BindAccount("acct-b", contextB);
        oracle.ObserveRawEvents([CreateRaw(contextA, sourceA, 1), CreateRaw(contextB, sourceB, 1)]);

        var report = await oracle.CompleteAsync(beginsMidSession: false, expectedWelcomeBoundaries: 0, expectedContextCount: 2);
        Assert.True(report.Passed);
    }

    [Fact]
    public async Task Expected_context_not_observed_reports_context_missing()
    {
        var oracle = new ReplayCorrectnessOracle();
        var contextId = MonitoringContextId.CreateNew();
        oracle.BindAccount("acct-a", contextId);

        var report = await oracle.CompleteAsync(beginsMidSession: false, expectedWelcomeBoundaries: 0, expectedContextCount: 1);
        Assert.False(report.Passed);
        AssertContainsFailureCode(report, "context.missing");
        AssertDoesNotContainFailureCodes(report, "context.isolation", "parser.gap", "session.welcome-missing");
        AssertFailureMessagesArePayloadFree(report);
    }

    [Fact]
    public async Task Unexpected_context_reports_context_isolation()
    {
        var oracle = new ReplayCorrectnessOracle();
        var contextId = MonitoringContextId.CreateNew();
        var source = LogSourceId.Create(
            "acct-a",
            "acct-a",
            @"C:\fake\chatlog 2026-01-01.txt",
            new DateOnly(2026, 1, 1));
        oracle.ObserveRawEvents([CreateRaw(contextId, source, 1)]);

        var report = await oracle.CompleteAsync(beginsMidSession: false, expectedWelcomeBoundaries: 0, expectedContextCount: 0);
        Assert.False(report.Passed);
        AssertContainsFailureCode(report, "context.isolation");
        AssertDoesNotContainFailureCodes(report, "context.missing", "parser.gap", "session.welcome-missing");
        AssertFailureMessagesArePayloadFree(report);
    }

    [Fact]
    public async Task Duplicate_accounts_bound_to_same_context_reports_account_multi_context()
    {
        var oracle = new ReplayCorrectnessOracle();
        var sharedContext = MonitoringContextId.CreateNew();
        oracle.BindAccount("acct-a", sharedContext);
        oracle.BindAccount("acct-b", sharedContext);
        var source = LogSourceId.Create(
            "acct-a",
            "acct-a",
            @"C:\fake\chatlog 2026-01-01.txt",
            new DateOnly(2026, 1, 1));
        oracle.ObserveRawEvents([CreateRaw(sharedContext, source, 1)]);

        var report = await oracle.CompleteAsync(beginsMidSession: false, expectedWelcomeBoundaries: 0, expectedContextCount: 1);
        Assert.False(report.Passed);
        AssertContainsFailureCode(report, "account.multi-context");
        AssertDoesNotContainFailureCodes(report, "context.missing", "context.contamination", "session.welcome-missing");
        AssertFailureMessagesArePayloadFree(report);
    }

    [Fact]
    public async Task Welcome_committed_event_resets_session_sequence_tracking()
    {
        var oracle = new ReplayCorrectnessOracle();
        var contextId = MonitoringContextId.CreateNew();
        var source = LogSourceId.Create(
            "acct-a",
            "acct-a",
            @"C:\fake\chatlog 2026-01-01.txt",
            new DateOnly(2026, 1, 1));
        oracle.BindAccount("acct-a", contextId);

        var welcome = CreateClassified(
            contextId,
            source,
            1,
            "2026-01-01 00:00:00 Welcome to City of Heroes, Example Hero!");
        var followUp = CreateClassified(
            contextId,
            source,
            2,
            "2026-01-01 00:00:01 You are now leaving the example district.");

        var firstSession = GameplaySessionId.CreateNew();
        var secondSession = GameplaySessionId.CreateNew();

        oracle.ObserveCommittedEvents(
        [
            CreateCommitted(contextId, welcome, firstSession, 1),
            CreateCommitted(contextId, welcome, secondSession, 1),
            CreateCommitted(contextId, followUp, secondSession, 2)
        ]);

        var report = await oracle.CompleteAsync(beginsMidSession: false, expectedWelcomeBoundaries: 0, expectedContextCount: 1);
        Assert.True(report.Passed);
    }

    [Fact]
    public async Task Concurrent_observations_are_applied_without_corruption()
    {
        var oracle = new ReplayCorrectnessOracle();
        var contextId = MonitoringContextId.CreateNew();
        var source = LogSourceId.Create(
            "acct-a",
            "acct-a",
            @"C:\fake\chatlog 2026-01-01.txt",
            new DateOnly(2026, 1, 1));
        oracle.BindAccount("acct-a", contextId);

        const int producerCount = 12;
        const int eventsPerProducer = 8;
        long rawSequence = 0;
        long classifiedSequence = 0;
        long committedSequence = 0;
        var sequenceLock = new object();
        var committedSessionId = GameplaySessionId.CreateNew();
        using var barrier = new Barrier(producerCount);
        var tasks = new Task[producerCount];
        for (var producer = 0; producer < producerCount; producer++)
        {
            var producerIndex = producer;
            tasks[producer] = Task.Run(() =>
            {
                barrier.SignalAndWait();
                for (var index = 0; index < eventsPerProducer; index++)
                {
                    switch (producerIndex % 3)
                    {
                        case 0:
                            lock (sequenceLock)
                            {
                                oracle.ObserveRawEvents(
                                [
                                    CreateRaw(contextId, source, ++rawSequence)
                                ]);
                            }

                            break;
                        case 1:
                            lock (sequenceLock)
                            {
                                oracle.ObserveClassifiedEvents(
                                [
                                    CreateClassified(
                                        contextId,
                                        source,
                                        ++classifiedSequence,
                                        "2026-01-01 00:00:00 You are now leaving the example district.")
                                ]);
                            }

                            break;
                        default:
                            lock (sequenceLock)
                            {
                                oracle.ObserveCommittedEvents(
                                [
                                    CreateCommitted(
                                        contextId,
                                        CreateClassified(
                                            contextId,
                                            source,
                                            ++committedSequence,
                                            "2026-01-01 00:00:01 You have received a souvenir."),
                                        committedSessionId,
                                        committedSequence)
                                ]);
                            }

                            break;
                    }
                }
            });
        }

        await Task.WhenAll(tasks);
        var report = await oracle.CompleteAsync(beginsMidSession: false, expectedWelcomeBoundaries: 0, expectedContextCount: 1);

        Assert.Equal(32, report.RawEventsObserved);
        Assert.Equal(32, report.ClassifiedEventsObserved);
        Assert.Equal(32, report.CommittedGameplayEventsObserved);
        Assert.Equal(1, report.ContextCount);
        Assert.True(report.Passed);
    }

    [Fact]
    public async Task Completion_race_accepted_observation_drains_before_report()
    {
        var oracle = new ReplayCorrectnessOracle();
        var contextId = MonitoringContextId.CreateNew();
        var source = LogSourceId.Create(
            "acct-a",
            "acct-a",
            @"C:\fake\chatlog 2026-01-01.txt",
            new DateOnly(2026, 1, 1));
        oracle.BindAccount("acct-a", contextId);
        oracle.ObserveRawEvents([CreateRaw(contextId, source, 1)]);

        var report = await oracle.CompleteAsync(beginsMidSession: false, expectedWelcomeBoundaries: 0, expectedContextCount: 1);

        Assert.Equal(1, report.RawEventsObserved);
        Assert.Equal(1, oracle.RawEventsObserved);

        oracle.ObserveRawEvents([CreateRaw(contextId, source, 2)]);
        var secondReport = await oracle.CompleteAsync(beginsMidSession: false, expectedWelcomeBoundaries: 0, expectedContextCount: 1);
        Assert.Equal(1, secondReport.RawEventsObserved);
        Assert.True(oracle.LateObservationCount > 0);
    }

    [Fact]
    public async Task Late_observation_after_finalization_is_rejected()
    {
        var oracle = new ReplayCorrectnessOracle();
        var contextId = MonitoringContextId.CreateNew();
        var source = LogSourceId.Create(
            "acct-a",
            "acct-a",
            @"C:\fake\chatlog 2026-01-01.txt",
            new DateOnly(2026, 1, 1));
        oracle.BindAccount("acct-a", contextId);

        var report = await oracle.CompleteAsync(beginsMidSession: false, expectedWelcomeBoundaries: 0, expectedContextCount: 1);
        oracle.ObserveRawEvents([CreateRaw(contextId, source, 1)]);

        Assert.Equal(0, report.RawEventsObserved);
        Assert.True(oracle.LateObservationCount > 0);
    }

    private static void AssertContainsFailureCode(ReplayCorrectnessReport report, string expectedCode) =>
        Assert.Contains(report.Failures, failure => failure.Code == expectedCode);

    private static void AssertDoesNotContainFailureCodes(
        ReplayCorrectnessReport report,
        params string[] excludedCodes)
    {
        foreach (var excludedCode in excludedCodes)
        {
            Assert.DoesNotContain(report.Failures, failure => failure.Code == excludedCode);
        }
    }

    private static void AssertFailureMessagesArePayloadFree(ReplayCorrectnessReport report)
    {
        foreach (var failure in report.Failures)
        {
            Assert.DoesNotContain("Example Hero", failure.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(@"C:\fake", failure.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("chatlog", failure.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Welcome to City of Heroes", failure.Message, StringComparison.Ordinal);
        }
    }

    private static ParserRawEvent CreateRaw(MonitoringContextId contextId, LogSourceId source, long sequence) =>
        new()
        {
            ContextId = contextId,
            SourceId = source,
            SourceSegmentId = ParserSourceSegmentId.CreateNew(),
            BindingGeneration = 1,
            Sequence = sequence,
            ObservedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            RawLine = "synthetic",
            SourceByteStart = 0,
            SourceByteEnd = 1,
            LineStatus = ParserLineStatus.Complete
        };

    private static ParserEvent CreateClassified(
        MonitoringContextId contextId,
        LogSourceId source,
        long sequence,
        string line) =>
        new ParserClassifier().Classify(new ParserRawEvent
        {
            ContextId = contextId,
            SourceId = source,
            SourceSegmentId = ParserSourceSegmentId.CreateNew(),
            BindingGeneration = 1,
            Sequence = sequence,
            ObservedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            RawLine = line,
            SourceByteStart = 0,
            SourceByteEnd = line.Length,
            LineStatus = ParserLineStatus.Complete
        });

    private static GameplaySessionEvent CreateCommitted(
        MonitoringContextId contextId,
        ParserEvent parserEvent,
        GameplaySessionId sessionId,
        long sessionSequence) =>
        new()
        {
            ContextId = contextId,
            SessionId = sessionId,
            ParserEvent = parserEvent,
            CommittedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            SessionSequence = sessionSequence
        };
}
