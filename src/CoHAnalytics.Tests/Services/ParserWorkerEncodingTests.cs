using System.Collections.Concurrent;
using System.Text;
using CoHAnalytics.Models;
using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Services;

public sealed class ParserWorkerEncodingTests
{
    [Fact]
    public async Task Strict_utf8_supports_no_bom_non_ascii_and_split_code_points()
    {
        using var directory = new ParserTestDirectory();
        var path = directory.CreateFile();
        var source = ParserTestSnapshots.Source(path);
        var contextId = MonitoringContextId.CreateNew();
        await using var worker = new ParserWorker(
            contextId,
            ParserTestSnapshots.FastOptions(readBufferSize: 4));
        var events = new ConcurrentQueue<ParserRawEvent>();
        worker.RawEventAvailable += (_, args) => events.Enqueue(args.ParserEvent);

        await worker.StartAsync();
        await worker.ApplyContextAsync(ParserTestSnapshots.Context(
            contextId,
            MonitoringContextState.Ready,
            source,
            1,
            MonitoringSourceTransitionKind.SourceAssigned));

        var encoded = Encoding.UTF8.GetBytes("Renée 雪\r\n");
        var split = Array.IndexOf(encoded, (byte)0xC3) + 1;
        directory.Append(path, encoded[..split]);
        await Task.Delay(30);
        Assert.Empty(events);
        directory.Append(path, encoded[split..]);

        await ParserTestSnapshots.WaitUntilAsync(() => events.Count == 1);
        var parsed = Assert.Single(events);
        Assert.Equal("Renée 雪", parsed.RawLine);
        Assert.Equal(encoded.Length, parsed.SourceByteEnd - parsed.SourceByteStart);
    }

    [Fact]
    public async Task Utf8_bom_at_zero_is_consumed_and_byte_range_starts_after_it()
    {
        using var directory = new ParserTestDirectory();
        var content = new byte[] { 0xEF, 0xBB, 0xBF }.Concat("hello\r\n"u8.ToArray()).ToArray();
        var path = directory.CreateFile(content: content);
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
        await worker.ApplyContextAsync(ParserTestSnapshots.Context(
            contextId,
            MonitoringContextState.Ready,
            source,
            2,
            MonitoringSourceTransitionKind.TruncationReset));

        await ParserTestSnapshots.WaitUntilAsync(() => events.Count == 1);
        var parsed = Assert.Single(events);
        Assert.Equal("hello", parsed.RawLine);
        Assert.Equal(3, parsed.SourceByteStart);
        Assert.Equal(content.Length, parsed.SourceByteEnd);
    }

    [Fact]
    public async Task Invalid_utf8_faults_explicitly_without_replacement_text_event()
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
        directory.Append(path, [0xC3, 0x28, 0x0A]);

        await ParserTestSnapshots.WaitUntilAsync(() => worker.Current.State == ParserWorkerState.Faulted);
        Assert.Equal("invalid_utf8", worker.Current.FaultCode);
        Assert.Empty(events);
    }
}
