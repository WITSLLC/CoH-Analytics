using System.Collections.Concurrent;
using System.IO;
using CoHAnalytics.Models;
using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Services;

public sealed class ParserWorkerTests
{
    [Fact]
    public async Task Existing_initial_source_starts_at_attachment_eof_and_reads_only_appends()
    {
        using var directory = new ParserTestDirectory();
        var path = directory.CreateFile(content: "historical\r\n"u8.ToArray());
        var source = ParserTestSnapshots.Source(path);
        var contextId = MonitoringContextId.CreateNew();
        await using var worker = new ParserWorker(contextId, ParserTestSnapshots.FastOptions());
        var events = new ConcurrentQueue<ParserRawEvent>();
        worker.RawEventAvailable += (_, args) => events.Enqueue(args.ParserEvent);

        await worker.StartAsync();
        await worker.ApplyContextAsync(ParserTestSnapshots.Context(
            contextId,
            MonitoringContextState.Ready,
            source,
            1,
            MonitoringSourceTransitionKind.SourceAssigned));

        Assert.Equal(new FileInfo(path).Length, worker.Current.CurrentSegment!.StartingOffset);
        Assert.Empty(events);

        directory.Append(path, "new line\r\n");
        await ParserTestSnapshots.WaitUntilAsync(() => events.Count == 1);

        var parsed = Assert.Single(events);
        Assert.Equal("new line", parsed.RawLine);
        Assert.Equal("historical\r\n"u8.Length, parsed.SourceByteStart);
        Assert.Equal(new FileInfo(path).Length, parsed.SourceByteEnd);
    }

    [Fact]
    public async Task Empty_initial_source_starts_zero_and_shared_handle_allows_append_and_delete()
    {
        using var directory = new ParserTestDirectory();
        var path = directory.CreateFile();
        var source = ParserTestSnapshots.Source(path);
        var contextId = MonitoringContextId.CreateNew();
        await using var worker = new ParserWorker(contextId, ParserTestSnapshots.FastOptions());
        var events = new ConcurrentQueue<ParserRawEvent>();
        worker.RawEventAvailable += (_, args) => events.Enqueue(args.ParserEvent);

        await worker.StartAsync();
        await worker.ApplyContextAsync(ParserTestSnapshots.Context(
            contextId,
            MonitoringContextState.Ready,
            source,
            1,
            MonitoringSourceTransitionKind.SourceAssigned));

        Assert.Equal(0, worker.Current.CurrentSegment!.StartingOffset);
        directory.Append(path, "one\r\n");
        await ParserTestSnapshots.WaitUntilAsync(() => events.Count == 1);
        File.Delete(path);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task Suspension_and_source_loss_preserve_cursor_and_partial_buffer_without_replay()
    {
        using var directory = new ParserTestDirectory();
        var path = directory.CreateFile();
        var source = ParserTestSnapshots.Source(path);
        var contextId = MonitoringContextId.CreateNew();
        await using var worker = new ParserWorker(contextId, ParserTestSnapshots.FastOptions());
        var events = new ConcurrentQueue<ParserRawEvent>();
        worker.RawEventAvailable += (_, args) => events.Enqueue(args.ParserEvent);

        await worker.StartAsync();
        var ready = ParserTestSnapshots.Context(
            contextId,
            MonitoringContextState.Ready,
            source,
            1,
            MonitoringSourceTransitionKind.SourceAssigned);
        await worker.ApplyContextAsync(ready);
        directory.Append(path, "partial");
        await ParserTestSnapshots.WaitUntilAsync(() => worker.Current.Checkpoint?.PartialBufferByteCount == 7);
        var cursor = worker.Current.Checkpoint!.ByteOffset;

        await worker.ApplyContextAsync(ready with { State = MonitoringContextState.RuntimeSuspended });
        Assert.Equal(ParserWorkerState.Suspended, worker.Current.State);
        Assert.Equal(cursor, worker.Current.Checkpoint!.ByteOffset);

        directory.Append(path, " line\r\n");
        await worker.ApplyContextAsync(ready);
        await ParserTestSnapshots.WaitUntilAsync(() => events.Count == 1);
        Assert.Equal("partial line", Assert.Single(events).RawLine);

        await worker.ApplyContextAsync(ready with { State = MonitoringContextState.WaitingForSource });
        Assert.Equal(ParserWorkerState.WaitingForSource, worker.Current.State);
        var recoveredCursor = worker.Current.Checkpoint!.ByteOffset;
        await worker.ApplyContextAsync(ready);
        await Task.Delay(50);
        Assert.Equal(recoveredCursor, worker.Current.Checkpoint!.ByteOffset);
        Assert.Single(events);
    }

    [Fact]
    public async Task Length_regression_without_approved_reset_faults_worker()
    {
        using var directory = new ParserTestDirectory();
        var path = directory.CreateFile();
        var source = ParserTestSnapshots.Source(path);
        var contextId = MonitoringContextId.CreateNew();
        await using var worker = new ParserWorker(contextId, ParserTestSnapshots.FastOptions());

        await worker.StartAsync();
        var ready = ParserTestSnapshots.Context(
            contextId,
            MonitoringContextState.Ready,
            source,
            1,
            MonitoringSourceTransitionKind.SourceAssigned);
        await worker.ApplyContextAsync(ready);
        directory.Append(path, "complete\r\n");
        await ParserTestSnapshots.WaitUntilAsync(() => worker.Current.TotalLinesEmitted == 1);
        await worker.ApplyContextAsync(ready with { State = MonitoringContextState.WaitingForSource });
        File.WriteAllBytes(path, []);

        await worker.ApplyContextAsync(ready);

        Assert.Equal(ParserWorkerState.Faulted, worker.Current.State);
        Assert.Equal("source_io_failure", worker.Current.FaultCode);
    }
}
