using System.Collections.Concurrent;
using System.IO;
using CoHAnalytics.Models;
using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Services;

public sealed class ParserManagerTransitionTests
{
    [Fact]
    public async Task Consecutive_manager_events_are_applied_in_order_without_coalescing()
    {
        using var directory = new ParserTestDirectory();
        var path = directory.CreateFile();
        var source = ParserTestSnapshots.Source(path);
        var contextId = MonitoringContextId.CreateNew();
        var monitoring = new FakeMonitoringSessionManager();
        monitoring.SetInitial(ParserTestSnapshots.Snapshot(
            1,
            ParserTestSnapshots.Context(contextId, MonitoringContextState.Ready, source, 1, MonitoringSourceTransitionKind.SourceAssigned)));
        await using var parser = new ParserManager(monitoring, ParserTestSnapshots.FastOptions());

        await parser.StartAsync();
        monitoring.Publish(ParserTestSnapshots.Snapshot(
            2,
            ParserTestSnapshots.Context(contextId, MonitoringContextState.WaitingForSource, null, 2, MonitoringSourceTransitionKind.SourceReleased, source)));
        monitoring.Publish(ParserTestSnapshots.Snapshot(
            3,
            ParserTestSnapshots.Context(contextId, MonitoringContextState.Ready, source, 3, MonitoringSourceTransitionKind.SourceReclaimed, source)));

        await ParserTestSnapshots.WaitUntilAsync(() =>
            Assert.Single(parser.Current.Workers).AppliedSourceBindingGeneration == 3);
        var worker = Assert.Single(parser.Current.Workers);
        Assert.Equal(MonitoringSourceTransitionKind.SourceReclaimed, worker.LastAppliedTransitionKind);
        Assert.Contains(worker.RecentSegments, segment => segment.State == ParserSourceSegmentState.Released);
    }

    [Theory]
    [InlineData(MonitoringSourceTransitionKind.AutomaticRollover)]
    [InlineData(MonitoringSourceTransitionKind.SourceReplaced)]
    public async Task Source_switch_transition_retains_worker_starts_zero_and_preserves_sequence(
        MonitoringSourceTransitionKind transition)
    {
        using var directory = new ParserTestDirectory();
        var oldPath = directory.CreateFile("old.txt");
        var newPath = directory.CreateFile("new.txt", "new existing\r\n"u8.ToArray());
        var oldSource = ParserTestSnapshots.Source(oldPath);
        var newSource = transition == MonitoringSourceTransitionKind.SourceReplaced
            ? ParserTestSnapshots.Source(newPath, identityGeneration: 1)
            : ParserTestSnapshots.Source(newPath);
        var contextId = MonitoringContextId.CreateNew();
        var monitoring = new FakeMonitoringSessionManager();
        monitoring.SetInitial(ParserTestSnapshots.Snapshot(
            1,
            ParserTestSnapshots.Context(contextId, MonitoringContextState.Ready, oldSource, 1, MonitoringSourceTransitionKind.SourceAssigned)));
        await using var parser = new ParserManager(monitoring, ParserTestSnapshots.FastOptions());
        var events = CollectEvents(parser);

        await parser.StartAsync();
        var workerId = Assert.Single(parser.Current.Workers).WorkerId;
        directory.Append(oldPath, "old append\r\n");
        await ParserTestSnapshots.WaitUntilAsync(() => events.Count == 1);

        monitoring.Publish(ParserTestSnapshots.Snapshot(
            2,
            ParserTestSnapshots.Context(contextId, MonitoringContextState.Ready, newSource, 2, transition, oldSource)));
        await ParserTestSnapshots.WaitUntilAsync(() => events.Count == 2);

        var worker = Assert.Single(parser.Current.Workers);
        Assert.Equal(workerId, worker.WorkerId);
        Assert.Equal(0, worker.CurrentSegment!.StartingOffset);
        Assert.Equal(newSource, worker.CurrentSourceId);
        Assert.Equal(
            transition == MonitoringSourceTransitionKind.AutomaticRollover
                ? ParserSourceSegmentState.RolledOver
                : ParserSourceSegmentState.Replaced,
            worker.RecentSegments.Single().State);
        Assert.Equal([1L, 2L], events.Select(item => item.Sequence).ToArray());
        Assert.Equal("new existing", events.Last().RawLine);
    }

    [Fact]
    public async Task Truncation_reset_reuses_source_and_starts_new_segment_zero_once()
    {
        using var directory = new ParserTestDirectory();
        var path = directory.CreateFile();
        var source = ParserTestSnapshots.Source(path);
        var contextId = MonitoringContextId.CreateNew();
        var monitoring = new FakeMonitoringSessionManager();
        monitoring.SetInitial(ParserTestSnapshots.Snapshot(
            1,
            ParserTestSnapshots.Context(contextId, MonitoringContextState.Ready, source, 1, MonitoringSourceTransitionKind.SourceAssigned)));
        await using var parser = new ParserManager(monitoring, ParserTestSnapshots.FastOptions());
        var events = CollectEvents(parser);

        await parser.StartAsync();
        directory.Append(path, "before\r\n");
        await ParserTestSnapshots.WaitUntilAsync(() => events.Count == 1);
        monitoring.Publish(ParserTestSnapshots.Snapshot(
            2,
            ParserTestSnapshots.Context(contextId, MonitoringContextState.WaitingForSource, source, 1, MonitoringSourceTransitionKind.SourceAssigned)));
        await ParserTestSnapshots.WaitUntilAsync(() => Assert.Single(parser.Current.Workers).State == ParserWorkerState.WaitingForSource);
        File.WriteAllText(path, "after reset\r\n");
        monitoring.Publish(ParserTestSnapshots.Snapshot(
            3,
            ParserTestSnapshots.Context(contextId, MonitoringContextState.Ready, source, 2, MonitoringSourceTransitionKind.TruncationReset)));
        await ParserTestSnapshots.WaitUntilAsync(() => events.Count == 2);
        await ParserTestSnapshots.WaitUntilAsync(() => Assert.Single(parser.Current.Workers).State == ParserWorkerState.WaitingForData);
        var revision = parser.Current.Revision;

        monitoring.Publish(ParserTestSnapshots.Snapshot(
            4,
            ParserTestSnapshots.Context(contextId, MonitoringContextState.Ready, source, 2, MonitoringSourceTransitionKind.TruncationReset)));
        await Task.Delay(50);

        var worker = Assert.Single(parser.Current.Workers);
        Assert.Equal(0, worker.CurrentSegment!.StartingOffset);
        Assert.Equal(2, worker.AppliedSourceBindingGeneration);
        Assert.Equal(revision, parser.Current.Revision);
        Assert.Equal([1L, 2L], events.Select(item => item.Sequence).ToArray());
    }

    [Fact]
    public async Task Release_waits_reclaim_uses_attachment_eof_and_removal_disposes_worker()
    {
        using var directory = new ParserTestDirectory();
        var path = directory.CreateFile();
        var source = ParserTestSnapshots.Source(path);
        var contextId = MonitoringContextId.CreateNew();
        var monitoring = new FakeMonitoringSessionManager();
        monitoring.SetInitial(ParserTestSnapshots.Snapshot(
            1,
            ParserTestSnapshots.Context(contextId, MonitoringContextState.Ready, source, 1, MonitoringSourceTransitionKind.SourceAssigned)));
        await using var parser = new ParserManager(monitoring, ParserTestSnapshots.FastOptions());
        var events = CollectEvents(parser);

        await parser.StartAsync();
        var workerId = Assert.Single(parser.Current.Workers).WorkerId;
        directory.Append(path, "first\r\n");
        await ParserTestSnapshots.WaitUntilAsync(() => events.Count == 1);

        monitoring.Publish(ParserTestSnapshots.Snapshot(
            2,
            ParserTestSnapshots.Context(contextId, MonitoringContextState.WaitingForSource, null, 2, MonitoringSourceTransitionKind.SourceReleased, source)));
        await ParserTestSnapshots.WaitUntilAsync(() => Assert.Single(parser.Current.Workers).State == ParserWorkerState.WaitingForSource);
        Assert.Equal(
            ParserSourceSegmentState.Released,
            Assert.Single(parser.Current.Workers).RecentSegments.Last().State);
        directory.Append(path, "while released\r\n");

        monitoring.Publish(ParserTestSnapshots.Snapshot(
            3,
            ParserTestSnapshots.Context(contextId, MonitoringContextState.Ready, source, 3, MonitoringSourceTransitionKind.SourceReclaimed, source)));
        await ParserTestSnapshots.WaitUntilAsync(() => Assert.Single(parser.Current.Workers).AppliedSourceBindingGeneration == 3);
        Assert.Equal(workerId, Assert.Single(parser.Current.Workers).WorkerId);
        Assert.Equal(new FileInfo(path).Length, Assert.Single(parser.Current.Workers).CurrentSegment!.StartingOffset);
        Assert.Single(events);

        directory.Append(path, "after reclaim\r\n");
        await ParserTestSnapshots.WaitUntilAsync(() => events.Count == 2);
        Assert.Equal(2, events.Last().Sequence);

        monitoring.Publish(ParserTestSnapshots.Snapshot(
            4,
            ParserTestSnapshots.Context(contextId, MonitoringContextState.Stopped, null, 4, MonitoringSourceTransitionKind.ContextRemoved, source)));
        await ParserTestSnapshots.WaitUntilAsync(() => parser.Current.WorkerCount == 0);
    }

    [Fact]
    public async Task Unexpected_binding_generation_jump_faults_safely()
    {
        using var directory = new ParserTestDirectory();
        var path = directory.CreateFile();
        var source = ParserTestSnapshots.Source(path);
        var contextId = MonitoringContextId.CreateNew();
        var monitoring = new FakeMonitoringSessionManager();
        monitoring.SetInitial(ParserTestSnapshots.Snapshot(
            1,
            ParserTestSnapshots.Context(contextId, MonitoringContextState.Ready, source, 1, MonitoringSourceTransitionKind.SourceAssigned)));
        await using var parser = new ParserManager(monitoring, ParserTestSnapshots.FastOptions());

        await parser.StartAsync();
        monitoring.Publish(ParserTestSnapshots.Snapshot(
            2,
            ParserTestSnapshots.Context(contextId, MonitoringContextState.Ready, source, 3, MonitoringSourceTransitionKind.TruncationReset)));

        await ParserTestSnapshots.WaitUntilAsync(() => parser.Current.FaultedCount == 1);
        Assert.Equal("binding_generation_jump", Assert.Single(parser.Current.Workers).FaultCode);
    }

    [Fact]
    public async Task Monitoring_snapshot_queue_records_accepted_items_and_drains_after_reconciliation()
    {
        using var directory = new ParserTestDirectory();
        var path = directory.CreateFile();
        var source = ParserTestSnapshots.Source(path);
        var contextId = MonitoringContextId.CreateNew();
        var monitoring = new FakeMonitoringSessionManager();
        monitoring.SetInitial(ParserTestSnapshots.Snapshot(
            1,
            ParserTestSnapshots.Context(
                contextId,
                MonitoringContextState.Ready,
                source,
                1,
                MonitoringSourceTransitionKind.SourceAssigned)));
        var options = ParserTestSnapshots.FastOptions(monitoringSnapshotQueueCapacity: 2);
        await using var parser = new ParserManager(monitoring, options);

        await parser.StartAsync();
        monitoring.Publish(ParserTestSnapshots.Snapshot(
            2,
            ParserTestSnapshots.Context(
                contextId,
                MonitoringContextState.WaitingForSource,
                null,
                2,
                MonitoringSourceTransitionKind.SourceReleased,
                source)));
        monitoring.Publish(ParserTestSnapshots.Snapshot(
            3,
            ParserTestSnapshots.Context(
                contextId,
                MonitoringContextState.Ready,
                source,
                3,
                MonitoringSourceTransitionKind.SourceReclaimed,
                source)));

        await ParserTestSnapshots.WaitUntilAsync(() =>
            parser.GetDiagnostics().MonitoringSnapshotQueue.IsDrained);

        var diagnostics = parser.GetDiagnostics();
        Assert.Equal(2, diagnostics.MonitoringSnapshotQueue.Capacity);
        Assert.True(diagnostics.MonitoringSnapshotQueue.PeakDepth >= 1);
        Assert.True(diagnostics.MonitoringSnapshotQueue.IsDrained);
        Assert.True(diagnostics.MonitoringSnapshotQueue.AcceptedCount >= 3);
    }

    [Fact]
    public async Task Older_monitoring_revision_cannot_remove_worker_created_by_newer_revision()
    {
        using var directory = new ParserTestDirectory();
        var path = directory.CreateFile();
        var source = ParserTestSnapshots.Source(path);
        var contextId = MonitoringContextId.CreateNew();
        var monitoring = new FakeMonitoringSessionManager();
        monitoring.SetInitial(ParserTestSnapshots.Snapshot(
            2,
            ParserTestSnapshots.Context(
                contextId,
                MonitoringContextState.Ready,
                source,
                1,
                MonitoringSourceTransitionKind.SourceAssigned)));
        await using var parser = new ParserManager(monitoring, ParserTestSnapshots.FastOptions());

        await parser.StartAsync();
        var workerId = Assert.Single(parser.Current.Workers).WorkerId;
        monitoring.Publish(ParserTestSnapshots.Snapshot(1));
        await ParserTestSnapshots.WaitUntilAsync(() =>
            parser.GetDiagnostics().MonitoringSnapshotQueue.IsDrained
            && parser.GetDiagnostics().RecentDecisions.Any(decision =>
                decision.Contains("Ignored stale monitoring snapshot revision 1 after 2", StringComparison.Ordinal)));

        Assert.Equal(workerId, Assert.Single(parser.Current.Workers).WorkerId);
        Assert.Equal(2, parser.GetDiagnostics().LastMonitoringSnapshotRevision);
    }

    private static ConcurrentQueue<ParserRawEvent> CollectEvents(ParserManager parser)
    {
        var events = new ConcurrentQueue<ParserRawEvent>();
        parser.EventsAvailable += (_, args) =>
        {
            foreach (var parserEvent in args.Events)
            {
                events.Enqueue(parserEvent);
            }
        };
        return events;
    }
}
