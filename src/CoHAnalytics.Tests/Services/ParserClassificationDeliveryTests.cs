using System.Collections.Concurrent;
using CoHAnalytics.Models;
using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Services;

public sealed class ParserClassificationDeliveryTests
{
    [Fact]
    public void Empty_classification_update_is_semantic_noop_and_event_batch_is_copied()
    {
        var snapshot = new ParserClassificationSnapshot { Revision = 4, TotalClassifiedLines = 2 };
        Assert.Same(snapshot, snapshot.Apply([], DateTimeOffset.UtcNow));

        var source = new List<ParserEvent>
        {
            new ParserClassifier().Classify(ParserClassifierTests.Raw("unknown"))
        };
        var args = new ParserEventsClassifiedEventArgs(source);
        source.Clear();
        Assert.Single(args.Events);
    }

    [Fact]
    public async Task Every_raw_event_is_classified_once_in_sequence_and_counts_are_coherent()
    {
        const string firstRawLine = "plain unknown";
        const string secondRawLine = "2026-08-04 06:27:10 Entering Test Zone.";
        const string thirdRawLine = "2026-08-04 06:28:14 [General] Speaker: hello";
        using var directory = new ParserTestDirectory();
        var path = directory.CreateFile();
        var source = ParserTestSnapshots.Source(path);
        var contextId = MonitoringContextId.CreateNew();
        var monitoring = new FakeMonitoringSessionManager();
        monitoring.SetInitial(ParserTestSnapshots.Snapshot(
            1,
            ParserTestSnapshots.Context(contextId, MonitoringContextState.Ready, source, 1, MonitoringSourceTransitionKind.SourceAssigned)));
        await using var parser = new ParserManager(monitoring, ParserTestSnapshots.FastOptions());
        var capture = new ClassifiedEventCapture(contextId, source, 3, thirdRawLine);
        parser.ClassifiedEventsAvailable += capture.OnClassifiedEventsAvailable;

        await parser.StartAsync();
        var queueBaseline = parser.GetDiagnostics().EventQueue;
        Assert.Equal(0, queueBaseline.AcceptedCount);
        Assert.Equal(0, queueBaseline.CompletedCount);
        Assert.Equal(0, queueBaseline.RejectedCount);
        directory.Append(
            path,
            $"{firstRawLine}\r\n{secondRawLine}\r\n{thirdRawLine}\r\n");
        await capture.TerminalEventObserved;
        var expectedAcceptedCount = queueBaseline.AcceptedCount + 3;
        var expectedCompletedCount = queueBaseline.CompletedCount + 3;
        await ParserTestSnapshots.WaitUntilAsync(() =>
        {
            var eventQueue = parser.GetDiagnostics().EventQueue;
            return eventQueue.AcceptedCount == expectedAcceptedCount
                && eventQueue.CompletedCount == expectedCompletedCount
                && eventQueue.RejectedCount == queueBaseline.RejectedCount
                && eventQueue.CurrentDepth == 0
                && eventQueue.InFlightCount == 0
                && eventQueue.MatchesAccountingIdentity();
        });

        var classified = capture.Snapshot();

        Assert.Equal([1L, 2L, 3L], classified.Select(item => item.Sequence).ToArray());
        Assert.Equal(3, classified.Count);
        Assert.Equal(3, classified.Select(item => item.Sequence).Distinct().Count());
        Assert.Equal([firstRawLine, secondRawLine, thirdRawLine], classified.Select(item => item.RawLine).ToArray());
        Assert.All(classified, item =>
        {
            Assert.Equal(contextId, item.ContextId);
            Assert.Equal(source, item.SourceId);
        });
        Assert.Equal(1, parser.ClassificationCurrent.UnknownLineCount);
        Assert.Equal(2, parser.ClassificationCurrent.RecognizedLineCount);
        Assert.Equal(0, parser.ClassificationCurrent.MalformedLineCount);
        Assert.True(parser.ClassificationCurrent.Revision > 0);
        var eventQueue = parser.GetDiagnostics().EventQueue;
        Assert.Equal(expectedAcceptedCount, eventQueue.AcceptedCount);
        Assert.Equal(expectedCompletedCount, eventQueue.CompletedCount);
        Assert.Equal(0, eventQueue.RejectedCount);
        Assert.Equal(0, eventQueue.CurrentDepth);
        Assert.Equal(0, eventQueue.InFlightCount);
        Assert.True(eventQueue.MatchesAccountingIdentity());
        Assert.DoesNotContain(
            parser.GetClassificationDiagnostics().RuleMatchCounts.Keys,
            key => key.Contains("plain unknown", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Classifier_exception_produces_one_safe_fallback_and_does_not_stop_later_events()
    {
        using var directory = new ParserTestDirectory();
        var path = directory.CreateFile();
        var source = ParserTestSnapshots.Source(path);
        var contextId = MonitoringContextId.CreateNew();
        var monitoring = new FakeMonitoringSessionManager();
        monitoring.SetInitial(ParserTestSnapshots.Snapshot(
            1,
            ParserTestSnapshots.Context(contextId, MonitoringContextState.Ready, source, 1, MonitoringSourceTransitionKind.SourceAssigned)));
        await using var parser = new ParserManager(
            monitoring,
            ParserTestSnapshots.FastOptions(),
            classifier: new ThrowingClassifier());
        var capture = new ClassifiedEventCapture(contextId, source, 2, "plain unknown");
        parser.ClassifiedEventsAvailable += capture.OnClassifiedEventsAvailable;

        await parser.StartAsync();
        directory.Append(path, "explode\r\nplain unknown\r\n");
        await capture.TerminalEventObserved;

        var classified = capture.Snapshot();
        Assert.Collection(
            classified,
            fallback =>
            {
                Assert.Equal(1, fallback.Sequence);
                Assert.Equal("explode", fallback.RawLine);
                Assert.Equal(contextId, fallback.ContextId);
                Assert.Equal(source, fallback.SourceId);
                Assert.Equal(1, fallback.BindingGeneration);
                Assert.Equal(MonitoringSourceTransitionKind.SourceAssigned, fallback.SourceTransitionKind);
                Assert.Equal(ParserLineStatus.Complete, fallback.LineStatus);
                Assert.Equal(ParserEventKind.Malformed, fallback.EventKind);
                Assert.Equal(ParserClassificationStatus.ClassifierFailed, fallback.ClassificationStatus);
                Assert.Equal("classifier_failed", fallback.ClassificationRuleId);
            },
            continued =>
            {
                Assert.Equal(2, continued.Sequence);
                Assert.Equal("plain unknown", continued.RawLine);
                Assert.Equal(contextId, continued.ContextId);
                Assert.Equal(source, continued.SourceId);
                Assert.Equal(1, continued.BindingGeneration);
                Assert.Equal(MonitoringSourceTransitionKind.SourceAssigned, continued.SourceTransitionKind);
                Assert.Equal(ParserLineStatus.Complete, continued.LineStatus);
                Assert.Equal(ParserEventKind.Unknown, continued.EventKind);
                Assert.Equal(ParserClassificationStatus.Unknown, continued.ClassificationStatus);
            });

        Assert.Single(
            classified,
            item => item.ClassificationStatus == ParserClassificationStatus.ClassifierFailed);
        Assert.Equal(classified[0].SourceSegmentId, classified[1].SourceSegmentId);
        Assert.Equal(classified[0].SourceByteEnd, classified[1].SourceByteStart);
        Assert.Equal(1, parser.ClassificationCurrent.ClassifierFailureCount);
        Assert.All(parser.GetClassificationDiagnostics().RecentClassifierFailures, failure => Assert.DoesNotContain("explode", failure));
    }

    [Fact]
    public async Task Multiple_contexts_preserve_their_own_provenance_and_sequence()
    {
        using var directory = new ParserTestDirectory();
        var firstPath = directory.CreateFile("first.txt");
        var secondPath = directory.CreateFile("second.txt");
        var firstId = MonitoringContextId.CreateNew();
        var secondId = MonitoringContextId.CreateNew();
        var monitoring = new FakeMonitoringSessionManager();
        monitoring.SetInitial(ParserTestSnapshots.Snapshot(
            1,
            ParserTestSnapshots.Context(firstId, MonitoringContextState.Ready, ParserTestSnapshots.Source(firstPath), 1, MonitoringSourceTransitionKind.SourceAssigned),
            ParserTestSnapshots.Context(secondId, MonitoringContextState.Ready, ParserTestSnapshots.Source(secondPath, "acct-2"), 1, MonitoringSourceTransitionKind.SourceAssigned)));
        await using var parser = new ParserManager(monitoring, ParserTestSnapshots.FastOptions());
        var events = new ConcurrentQueue<ParserEvent>();
        parser.ClassifiedEventsAvailable += (_, args) =>
        {
            foreach (var item in args.Events)
            {
                events.Enqueue(item);
            }
        };

        await parser.StartAsync();
        directory.Append(firstPath, "first\n");
        directory.Append(secondPath, "second\n");
        await ParserTestSnapshots.WaitUntilAsync(() => events.Count == 2);

        Assert.Contains(events, item => item.ContextId == firstId && item.Sequence == 1 && item.RawLine == "first");
        Assert.Contains(events, item => item.ContextId == secondId && item.Sequence == 1 && item.RawLine == "second");
    }

    private sealed class ClassifiedEventCapture(
        MonitoringContextId terminalContextId,
        LogSourceId terminalSourceId,
        long terminalSequence,
        string terminalRawLine)
    {
        private readonly object _sync = new();
        private readonly List<ParserEvent> _events = [];
        private readonly TaskCompletionSource _terminalEventObserved = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Task TerminalEventObserved => _terminalEventObserved.Task;

        public void OnClassifiedEventsAvailable(object? sender, ParserEventsClassifiedEventArgs args)
        {
            var observedTerminalEvent = false;
            lock (_sync)
            {
                foreach (var parserEvent in args.Events)
                {
                    _events.Add(parserEvent);
                    observedTerminalEvent |= parserEvent.ContextId == terminalContextId
                        && parserEvent.SourceId == terminalSourceId
                        && parserEvent.Sequence == terminalSequence
                        && parserEvent.RawLine == terminalRawLine;
                }
            }

            if (observedTerminalEvent)
            {
                _terminalEventObserved.TrySetResult();
            }
        }

        public IReadOnlyList<ParserEvent> Snapshot()
        {
            lock (_sync)
            {
                return [.. _events];
            }
        }
    }

    private sealed class ThrowingClassifier : IParserClassifier
    {
        private readonly ParserClassifier _inner = new();

        public ParserEvent Classify(ParserRawEvent rawEvent)
        {
            if (rawEvent.RawLine == "explode")
            {
                throw new InvalidOperationException("Raw content must not enter diagnostics.");
            }

            return _inner.Classify(rawEvent);
        }
    }
}
