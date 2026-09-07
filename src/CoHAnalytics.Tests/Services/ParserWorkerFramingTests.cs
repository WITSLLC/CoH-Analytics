using System.Collections.Concurrent;
using CoHAnalytics.Models;
using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Services;

public sealed class ParserWorkerFramingTests
{
    [Fact]
    public async Task Frames_crlf_lf_empty_multiple_and_split_terminators_once()
    {
        using var directory = new ParserTestDirectory();
        var path = directory.CreateFile();
        var source = ParserTestSnapshots.Source(path);
        var contextId = MonitoringContextId.CreateNew();
        await using var worker = new ParserWorker(contextId, ParserTestSnapshots.FastOptions(readBufferSize: 4));
        var events = new ConcurrentQueue<ParserRawEvent>();
        worker.RawEventAvailable += (_, args) => events.Enqueue(args.ParserEvent);

        await worker.StartAsync();
        await worker.ApplyContextAsync(ParserTestSnapshots.Context(
            contextId,
            MonitoringContextState.Ready,
            source,
            1,
            MonitoringSourceTransitionKind.SourceAssigned));
        directory.Append(path, "one\r");
        await Task.Delay(30);
        Assert.Empty(events);
        directory.Append(path, "\ntwo\n\r\npartial");

        await ParserTestSnapshots.WaitUntilAsync(() => events.Count == 3);
        Assert.Equal(["one", "two", ""], events.Select(item => item.RawLine).ToArray());
        Assert.Equal([1L, 2L, 3L], events.Select(item => item.Sequence).ToArray());
        Assert.Equal(7, worker.Current.Checkpoint!.PartialBufferByteCount);

        directory.Append(path, " end\r\n");
        await ParserTestSnapshots.WaitUntilAsync(() => events.Count == 4);
        Assert.Equal("partial end", events.Last().RawLine);
        await Task.Delay(30);
        Assert.Equal(4, events.Count);
    }

    [Fact]
    public async Task Oversized_complete_line_emits_bounded_too_large_event()
    {
        using var directory = new ParserTestDirectory();
        var path = directory.CreateFile();
        var source = ParserTestSnapshots.Source(path);
        var contextId = MonitoringContextId.CreateNew();
        await using var worker = new ParserWorker(
            contextId,
            ParserTestSnapshots.FastOptions(maximumLineBytes: 8));
        var events = new ConcurrentQueue<ParserRawEvent>();
        worker.RawEventAvailable += (_, args) => events.Enqueue(args.ParserEvent);

        await worker.StartAsync();
        await worker.ApplyContextAsync(ParserTestSnapshots.Context(
            contextId,
            MonitoringContextState.Ready,
            source,
            1,
            MonitoringSourceTransitionKind.SourceAssigned));
        directory.Append(path, "0123456789\r\n");

        await ParserTestSnapshots.WaitUntilAsync(() => events.Count == 1);
        var parsed = Assert.Single(events);
        Assert.Equal(ParserLineStatus.TooLarge, parsed.LineStatus);
        Assert.Empty(parsed.RawLine);
        Assert.Equal(0, worker.Current.Checkpoint!.PartialBufferByteCount);
    }

    [Fact]
    public async Task Transition_abandons_partial_fragment_without_emitting_or_merging_it()
    {
        using var directory = new ParserTestDirectory();
        var oldPath = directory.CreateFile("old.txt");
        var newPath = directory.CreateFile("new.txt", "new line\r\n"u8.ToArray());
        var oldSource = ParserTestSnapshots.Source(oldPath);
        var newSource = ParserTestSnapshots.Source(newPath);
        var contextId = MonitoringContextId.CreateNew();
        await using var worker = new ParserWorker(contextId, ParserTestSnapshots.FastOptions());
        var events = new ConcurrentQueue<ParserRawEvent>();
        worker.RawEventAvailable += (_, args) => events.Enqueue(args.ParserEvent);

        await worker.StartAsync();
        await worker.ApplyContextAsync(ParserTestSnapshots.Context(
            contextId,
            MonitoringContextState.Ready,
            oldSource,
            1,
            MonitoringSourceTransitionKind.SourceAssigned));
        directory.Append(oldPath, "abandoned");
        await ParserTestSnapshots.WaitUntilAsync(() => worker.Current.Checkpoint?.PartialBufferByteCount == 9);

        await worker.ApplyContextAsync(ParserTestSnapshots.Context(
            contextId,
            MonitoringContextState.Ready,
            newSource,
            2,
            MonitoringSourceTransitionKind.AutomaticRollover,
            oldSource));

        await ParserTestSnapshots.WaitUntilAsync(() => events.Count == 1);
        Assert.Equal("new line", Assert.Single(events).RawLine);
        Assert.True(worker.Current.RecentSegments.Single().IncompleteFragmentAtEnd);
        Assert.Equal(ParserSourceSegmentState.RolledOver, worker.Current.RecentSegments.Single().State);
    }
}
