using System.Collections.Concurrent;
using System.Text;
using CoHAnalytics.Models;
using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Services;

public sealed class ParserDrainTests
{
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static ParserManagerOptions Options(int capacity = 128) => new()
    { PollInterval = TimeSpan.FromDays(1), ReadBufferSize = 4, EventQueueCapacity = capacity };

    [Theory]
    [InlineData("", 0, 0)]
    [InlineData("\uFEFF", 3, 0)]
    [InlineData("unknown\n", 8, 1)]
    [InlineData("chat\r\n", 6, 1)]
    [InlineData("é界\n", 6, 1)]
    [InlineData("ok\nunfinished", 3, 1)]
    public async Task Complete_prefix_is_reported_and_held(string text, int effective, int count)
    {
        await using var f = await Fixture.Create();
        var lines = new ConcurrentQueue<ParserEvent>();
        f.Parser.ClassifiedEventsAvailable += (_, e) => { foreach (var line in e.Events) lines.Enqueue(line); };
        f.Directory.Append(f.Path, text);
        var result = await f.Parser.PauseAndDrainThroughAsync(f.Id);
        Assert.Equal(DrainOutcome.Success, result.Outcome);
        await using var hold = result.Fence!;
        Assert.True(hold.IsHeld);
        Assert.Equal(Encoding.UTF8.GetByteCount(text), hold.RequestedLimit);
        Assert.Equal(effective, hold.EffectiveBoundary);
        Assert.Equal(count, lines.Count);
        Assert.Equal(DrainOutcome.AlreadyHeld, (await f.Parser.PauseAndDrainThroughAsync(f.Id)).Outcome);
    }

    [Fact]
    public async Task Partial_utf8_and_later_append_are_preserved_for_resume()
    {
        await using var f = await Fixture.Create();
        var lines = new List<ParserEvent>();
        f.Parser.ClassifiedEventsAvailable += (_, e) => lines.AddRange(e.Events);
        f.Directory.Append(f.Path, [0xc3]);
        var first = await f.Parser.PauseAndDrainThroughAsync(f.Id);
        Assert.Equal(0, first.Fence!.EffectiveBoundary);
        f.Directory.Append(f.Path, [0xa9, 10]);
        Assert.Empty(lines);
        await first.Fence.ResumeAsync();
        var second = await f.Parser.PauseAndDrainThroughAsync(f.Id);
        await using var hold = second.Fence!;
        Assert.Equal("é", Assert.Single(lines).RawLine);
        Assert.Equal(3, hold.EffectiveBoundary);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Lease_resolution_is_explicit_and_idempotent(bool close)
    {
        await using var f = await Fixture.Create();
        var result = await f.Parser.PauseAndDrainThroughAsync(f.Id);
        var hold = result.Fence!;
        if (close) await hold.CloseAsync(); else await hold.DisposeAsync();
        await hold.AbortAsync();
        Assert.False(hold.IsHeld);
        var next = await f.Parser.PauseAndDrainThroughAsync(f.Id);
        Assert.Equal(close ? DrainOutcome.ServiceStopped : DrainOutcome.Success, next.Outcome);
        if (next.Fence is not null) await next.Fence.DisposeAsync();
    }

    [Fact]
    public async Task Explicit_boundary_rejects_wrong_identity_unreachable_and_already_passed()
    {
        await using var f = await Fixture.Create();
        var initial = await f.Parser.PauseAndDrainThroughAsync(f.Id);
        var position = initial.Fence!.Position;
        await initial.Fence.ResumeAsync();
        Assert.Equal(DrainOutcome.SourceChanged, (await f.Parser.PauseAndDrainThroughAsync(f.Id,
            position with { BindingGeneration = 99 })).Outcome);
        Assert.Equal(DrainOutcome.BoundaryUnreachable, (await f.Parser.PauseAndDrainThroughAsync(f.Id,
            position with { ByteOffset = 20 })).Outcome);
        f.Directory.Append(f.Path, "a\nb\n");
        var prefix = await f.Parser.PauseAndDrainThroughAsync(f.Id, position with { ByteOffset = 2 });
        Assert.Equal(2, prefix.Fence!.EffectiveBoundary);
        await prefix.Fence.ResumeAsync();
        Assert.Equal(DrainOutcome.BoundaryAlreadyPassed, (await f.Parser.PauseAndDrainThroughAsync(f.Id, position)).Outcome);
        var rest = await f.Parser.PauseAndDrainThroughAsync(f.Id);
        await using var hold = rest.Fence!;
        Assert.Equal(4, hold.EffectiveBoundary);
    }

    [Fact]
    public async Task Truncation_is_not_a_successful_drain()
    {
        await using var f = await Fixture.Create();
        f.Directory.Append(f.Path, "long\n");
        var first = await f.Parser.PauseAndDrainThroughAsync(f.Id);
        await first.Fence!.ResumeAsync();
        using (var stream = new FileStream(f.Path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete)) stream.SetLength(0);
        Assert.Equal(DrainOutcome.BoundaryUnreachable, (await f.Parser.PauseAndDrainThroughAsync(f.Id)).Outcome);
    }

    [Fact]
    public async Task Classification_finishes_before_marker_and_cancellation_releases_hold()
    {
        var entered = Signal();
        using var release = new ManualResetEventSlim();
        await using var f = await Fixture.Create(new CallbackClassifier(raw =>
        {
            entered.TrySetResult(); release.Wait(); return new ParserClassifier().Classify(raw);
        }));
        f.Directory.Append(f.Path, "chat\n");
        using var cancel = new CancellationTokenSource();
        var drain = f.Parser.PauseAndDrainThroughAsync(f.Id, cancellationToken: cancel.Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(drain.IsCompleted);
            cancel.Cancel();
            Assert.Equal(DrainOutcome.Cancelled, (await drain).Outcome);
        }
        finally { release.Set(); }
        var next = await f.Parser.PauseAndDrainThroughAsync(f.Id);
        Assert.Equal(DrainOutcome.Success, next.Outcome);
        await next.Fence!.AbortAsync();
    }

    [Fact]
    public async Task Classifier_failure_is_sticky_even_after_a_successful_line()
    {
        await using var f = await Fixture.Create(new CallbackClassifier(raw => raw.RawLine == "bad"
            ? throw new InvalidOperationException("fixture") : new ParserClassifier().Classify(raw)));
        f.Directory.Append(f.Path, "bad\ngood\n");
        Assert.Equal(DrainOutcome.ProcessingFailed, (await f.Parser.PauseAndDrainThroughAsync(f.Id)).Outcome);
        Assert.Equal(DrainOutcome.ProcessingFailed, (await f.Parser.PauseAndDrainThroughAsync(f.Id)).Outcome);
    }

    [Fact]
    public async Task Subscriber_failure_fails_transport()
    {
        await using var f = await Fixture.Create();
        f.Parser.ClassifiedEventsAvailable += (_, _) => throw new InvalidOperationException("fixture");
        f.Directory.Append(f.Path, "chat\n");
        Assert.Equal(DrainOutcome.ProcessingFailed, (await f.Parser.PauseAndDrainThroughAsync(f.Id)).Outcome);
    }

    [Fact]
    public async Task Held_context_does_not_stop_another_context()
    {
        await using var f = await Fixture.Create();
        var other = MonitoringContextId.CreateNew();
        var path = f.Directory.CreateFile("other.txt");
        var context = ParserTestSnapshots.Context(other, MonitoringContextState.Ready, ParserTestSnapshots.Source(path), 1,
            MonitoringSourceTransitionKind.SourceAssigned);
        var ready = Signal();
        f.Parser.StateChanged += (_, _) => { if (f.Parser.Current.Workers.Any(w => w.ContextId == other && w.Checkpoint is not null)) ready.TrySetResult(); };
        f.Monitoring.Publish(ParserTestSnapshots.Snapshot(2, f.Context, context));
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var a = await f.Parser.PauseAndDrainThroughAsync(f.Id);
        await using var aHold = a.Fence!;
        f.Directory.Append(path, "other\n");
        var b = await f.Parser.PauseAndDrainThroughAsync(other);
        await using var bHold = b.Fence!;
        Assert.Equal(DrainOutcome.Success, b.Outcome);
        Assert.True(aHold.IsHeld);
        Assert.Equal(6, bHold.EffectiveBoundary);
    }

    [Fact]
    public async Task Worker_emitter_orders_delayed_batch_and_marker_before_later_output()
    {
        using var directory = new ParserTestDirectory();
        var path = directory.CreateFile();
        var id = MonitoringContextId.CreateNew();
        await using var worker = new ParserWorker(id, Options());
        await worker.StartAsync();
        await worker.ApplyContextAsync(ParserTestSnapshots.Context(id, MonitoringContextState.Ready,
            ParserTestSnapshots.Source(path), 1, MonitoringSourceTransitionKind.SourceAssigned));
        var entered = Signal();
        using var release = new ManualResetEventSlim();
        var output = new ConcurrentQueue<string>();
        worker.RawEventAvailable += (_, e) =>
        {
            if (e.ParserEvent.RawLine == "first") { entered.TrySetResult(); release.Wait(); }
            output.Enqueue(e.ParserEvent.RawLine);
        };
        worker.BoundaryAvailable += (_, e) => { output.Enqueue("marker"); e.Fence.Transport.TrySetResult(DrainOutcome.Success); };
        directory.Append(path, "first\n");
        using var cancel = new CancellationTokenSource();
        var first = worker.PauseAndDrainThroughAsync(cancellationToken: cancel.Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            cancel.Cancel();
            Assert.Equal(DrainOutcome.Cancelled, (await first).Outcome);
            directory.Append(path, "second\n");
            var second = worker.PauseAndDrainThroughAsync();
            Assert.False(second.IsCompleted);
            Assert.Empty(output);
            release.Set();
            var result = await second;
            await using var hold = result.Fence!;
            Assert.Equal(new[] { "first", "marker", "second", "marker" }, output);
        }
        finally { release.Set(); }
    }

    private sealed class CallbackClassifier(Func<ParserRawEvent, ParserEvent> classify) : IParserClassifier
    { public ParserEvent Classify(ParserRawEvent rawEvent) => classify(rawEvent); }

    [Theory]
    [InlineData(MonitoringSourceTransitionKind.SourceReplaced)]
    [InlineData(MonitoringSourceTransitionKind.AutomaticRollover)]
    [InlineData(MonitoringSourceTransitionKind.TruncationReset)]
    public async Task Source_transition_invalidates_held_identity(MonitoringSourceTransitionKind transition)
    {
        await using var f = await Fixture.Create();
        var result = await f.Parser.PauseAndDrainThroughAsync(f.Id);
        await using var hold = result.Fence!;
        f.Monitoring.Publish(ParserTestSnapshots.Snapshot(2, f.Context with
        { SourceBindingGeneration = 2, LastSourceBindingTransitionKind = transition }));
        Assert.Equal(DrainOutcome.SourceChanged, await hold.Invalidated.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.False(hold.IsHeld);
    }

    [Fact]
    public async Task Context_removal_invalidates_hold()
    {
        await using var f = await Fixture.Create();
        var result = await f.Parser.PauseAndDrainThroughAsync(f.Id);
        await using var hold = result.Fence!;
        f.Monitoring.Publish(ParserTestSnapshots.Snapshot(2, f.Context with
        { State = MonitoringContextState.Stopped, SourceBindingGeneration = 2, LastSourceBindingTransitionKind = MonitoringSourceTransitionKind.ContextRemoved }));
        Assert.Equal(DrainOutcome.ContextGone, await hold.Invalidated.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task Parser_stop_while_classification_pending_fails_drain()
    {
        var entered = Signal();
        using var release = new ManualResetEventSlim();
        await using var f = await Fixture.Create(new CallbackClassifier(raw =>
        { entered.TrySetResult(); release.Wait(); return new ParserClassifier().Classify(raw); }));
        f.Directory.Append(f.Path, "chat\n");
        var drain = f.Parser.PauseAndDrainThroughAsync(f.Id);
        Task? stop = null;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            stop = f.Parser.StopAsync();
            Assert.Equal(DrainOutcome.ServiceStopped, (await drain).Outcome);
        }
        finally { release.Set(); if (stop is not null) await stop; }
    }

    [Fact]
    public async Task Invalid_utf8_fails_instead_of_acknowledging_a_gap()
    {
        await using var f = await Fixture.Create();
        f.Directory.Append(f.Path, [0xff, 10]);
        Assert.Equal(DrainOutcome.ParserFault, (await f.Parser.PauseAndDrainThroughAsync(f.Id)).Outcome);
    }

    [Fact]
    public async Task Oversized_line_still_has_an_acknowledged_envelope()
    {
        await using var f = await Fixture.Create();
        ParserEvent? last = null;
        f.Parser.ClassifiedEventsAvailable += (_, e) => last = e.Events.Last();
        f.Directory.Append(f.Path, new string('x', 256 * 1024 + 1) + "\n");
        var drain = await f.Parser.PauseAndDrainThroughAsync(f.Id);
        await using var hold = drain.Fence!;
        Assert.Equal(DrainOutcome.Success, drain.Outcome);
        Assert.Equal(ParserLineStatus.TooLarge, last!.LineStatus);
        Assert.Equal(256 * 1024 + 2, hold.EffectiveBoundary);
    }

    [Fact]
    public async Task Raw_channel_rejection_fails_the_boundary()
    {
        var entered = Signal();
        using var release = new ManualResetEventSlim();
        await using var f = await Fixture.Create(new CallbackClassifier(raw =>
        { entered.TrySetResult(); release.Wait(); return new ParserClassifier().Classify(raw); }), capacity: 1);
        // More envelopes than the bounded queue can admit while its reader is blocked.
        f.Directory.Append(f.Path, string.Concat(Enumerable.Repeat("line\n", 100)));
        var drain = f.Parser.PauseAndDrainThroughAsync(f.Id);
        try
        {
            Assert.Equal(DrainOutcome.Overloaded, (await drain.WaitAsync(TimeSpan.FromSeconds(10))).Outcome);
        }
        finally { release.Set(); }
    }

    internal sealed class Fixture : IAsyncDisposable
    {
        public ParserTestDirectory Directory { get; } = new();
        public string Path { get; private set; } = "";
        public MonitoringContextId Id { get; } = MonitoringContextId.CreateNew();
        public MonitoringContextSnapshot Context { get; private set; } = null!;
        public FakeMonitoringSessionManager Monitoring { get; } = new();
        public ParserManager Parser { get; private set; } = null!;
        public static async Task<Fixture> Create(IParserClassifier? classifier = null, int capacity = 128)
        {
            var f = new Fixture();
            f.Path = f.Directory.CreateFile();
            f.Context = ParserTestSnapshots.Context(f.Id, MonitoringContextState.Ready, ParserTestSnapshots.Source(f.Path), 1,
                MonitoringSourceTransitionKind.SourceAssigned);
            f.Monitoring.SetInitial(ParserTestSnapshots.Snapshot(1, f.Context));
            f.Parser = new ParserManager(f.Monitoring, Options(capacity), classifier: classifier);
            await f.Parser.StartAsync();
            return f;
        }
        public async ValueTask DisposeAsync() { await Parser.DisposeAsync(); Directory.Dispose(); }
    }
}
