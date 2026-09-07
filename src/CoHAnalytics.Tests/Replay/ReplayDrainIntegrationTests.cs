using System.Text;
using CoHAnalytics.Models;
using CoHAnalytics.Replay;
using CoHAnalytics.Services;
using CoHAnalytics.Tests.Services;

namespace CoHAnalytics.Tests.Replay;

[Collection(nameof(ReplayPipelineCollection))]
public sealed class ReplayDrainIntegrationTests
{
    [Fact]
    public async Task Drain_timeout_produces_drain_timeout_failure()
    {
        var replayTime = new ManualReplayTimeProvider();
        var sourcePath = ReplayTestPaths.Fixture("core-session.log");
        var plan = ReplayTestPlanFactory.CreatePlan(
            sourcePath,
            new DateOnly(2026, 1, 15),
            ReplayInputMode.Exact,
            ReplayChunkMode.WholeLine);
        var workspace = await ReplayWorkspace.CreateAsync(keepWorkspace: true);
        var ledger = new ReplayLedger();
        var timeline = new ReplayTimeline(replayTime);
        await using var pipeline = new ReplayPipeline(
            replayTime,
            characterDataDirectory: Path.Combine(Path.GetTempPath(), $"replay-timeout-{Guid.NewGuid():n}"),
            lifecycleTimeProvider: TimeProvider.System,
            lifecycleTimeouts: new ReplayLifecycleTimeouts { Drain = TimeSpan.FromTicks(-1) });

        var result = await pipeline.ExecuteAsync(
            new ReplayPipelineRequest { Contexts = [new ReplayContextBinding(workspace, plan)] },
            ledger,
            timeline);

        try
        {
            Assert.False(result.Success);
            Assert.Contains(result.Correctness.Failures, failure => failure.Code == "drain.timeout");
        }
        finally
        {
            Directory.Delete(workspace.RootPath, recursive: true);
        }
    }

    [Fact]
    public async Task Drain_faulted_produces_drain_fault_failure()
    {
        var replayTime = new ManualReplayTimeProvider();
        var invalidUtf8Path = Path.Combine(Path.GetTempPath(), $"replay-invalid-utf8-{Guid.NewGuid():n}.log");
        await File.WriteAllBytesAsync(invalidUtf8Path, [0xC3, 0x28, 0x0A]);
        var plan = ReplayTestPlanFactory.CreatePlan(
            invalidUtf8Path,
            new DateOnly(2026, 1, 15),
            ReplayInputMode.Exact,
            ReplayChunkMode.WholeLine);
        var workspace = await ReplayWorkspace.CreateAsync(keepWorkspace: true);
        var ledger = new ReplayLedger();
        var timeline = new ReplayTimeline(replayTime);
        await using var pipeline = new ReplayPipeline(
            replayTime,
            characterDataDirectory: Path.Combine(Path.GetTempPath(), $"replay-fault-{Guid.NewGuid():n}"),
            lifecycleTimeProvider: TimeProvider.System,
            lifecycleTimeouts: new ReplayLifecycleTimeouts { Drain = TimeSpan.FromSeconds(30) });

        var result = await pipeline.ExecuteAsync(
            new ReplayPipelineRequest { Contexts = [new ReplayContextBinding(workspace, plan)] },
            ledger,
            timeline);

        try
        {
            Assert.False(result.Success);
            Assert.Contains(result.Correctness.Failures, failure => failure.Code == "drain.fault");
        }
        finally
        {
            Directory.Delete(workspace.RootPath, recursive: true);
            if (File.Exists(invalidUtf8Path))
            {
                File.Delete(invalidUtf8Path);
            }
        }
    }

    [Fact]
    public async Task Drain_timeout_message_includes_decisive_queue_and_stage_diagnostics()
    {
        var replayTime = new ManualReplayTimeProvider();
        var sourcePath = ReplayTestPaths.Fixture("core-session.log");
        var plan = ReplayTestPlanFactory.CreatePlan(
            sourcePath,
            new DateOnly(2026, 1, 15),
            ReplayInputMode.Exact,
            ReplayChunkMode.WholeLine);
        var workspace = await ReplayWorkspace.CreateAsync(keepWorkspace: true);
        var ledger = new ReplayLedger();
        var timeline = new ReplayTimeline(replayTime);
        await using var pipeline = new ReplayPipeline(
            replayTime,
            characterDataDirectory: Path.Combine(Path.GetTempPath(), $"replay-timeout-detail-{Guid.NewGuid():n}"),
            lifecycleTimeProvider: TimeProvider.System,
            lifecycleTimeouts: new ReplayLifecycleTimeouts { Drain = TimeSpan.FromTicks(-1) });

        var result = await pipeline.ExecuteAsync(
            new ReplayPipelineRequest { Contexts = [new ReplayContextBinding(workspace, plan)] },
            ledger,
            timeline);

        try
        {
            var failure = Assert.Single(
                result.Correctness.Failures,
                candidate => candidate.Code == "drain.timeout");
            Assert.Contains("parser-event-inv=", failure.Message, StringComparison.Ordinal);
            Assert.Contains("parser-event(", failure.Message, StringComparison.Ordinal);
            Assert.Contains("parser-monitoring(", failure.Message, StringComparison.Ordinal);
            Assert.Contains("gameplay-work(", failure.Message, StringComparison.Ordinal);
            Assert.Contains("parser-complete=", failure.Message, StringComparison.Ordinal);
            Assert.Contains("gameplay-is-quiescent=", failure.Message, StringComparison.Ordinal);
            Assert.Contains("classified-caught-up=", failure.Message, StringComparison.Ordinal);
            Assert.Contains("oracle-caught-up=", failure.Message, StringComparison.Ordinal);
            Assert.Contains("overall-drain-complete=", failure.Message, StringComparison.Ordinal);
            Assert.Contains("oracle(raw=", failure.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("Example Hero", failure.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("chatlog", failure.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(workspace.RootPath, recursive: true);
        }
    }

    [Fact]
    public async Task Parser_tail_with_immediate_processing_drains_without_phantom_depth()
    {
        using var directory = new ParserTestDirectory();
        var path = directory.CreateFile();
        var contextId = MonitoringContextId.CreateNew();
        var source = ParserTestSnapshots.Source(path);
        var monitoring = new FakeMonitoringSessionManager();
        monitoring.SetInitial(ParserTestSnapshots.Snapshot(
            1,
            ParserTestSnapshots.Context(
                contextId,
                MonitoringContextState.Ready,
                source,
                1,
                MonitoringSourceTransitionKind.SourceAssigned)));

        await using var parser = new ParserManager(
            monitoring,
            ParserTestSnapshots.FastOptions(eventBatchSize: 1, eventQueueCapacity: 128));
        await parser.StartAsync();

        var payload = new StringBuilder();
        for (var line = 0; line < 8; line++)
        {
            payload.Append("2026-08-04 18:20:07 synthetic line ");
            payload.Append(line);
            payload.Append("\r\n");
        }

        directory.Append(path, payload.ToString());
        await ParserTestSnapshots.WaitUntilAsync(() => parser.Current.TotalLinesProcessed >= 8);
        await ParserTestSnapshots.WaitUntilAsync(() => parser.GetDiagnostics().EventQueue.IsDrained);
        await parser.StopAsync();

        var eventQueue = parser.GetDiagnostics().EventQueue;
        Assert.True(eventQueue.IsDrained);
        Assert.Equal(QueuePressureInvariantClassification.ConsistentDrained, eventQueue.ClassifyInvariant());
        Assert.True(eventQueue.MatchesAccountingIdentity());
    }

    [Fact]
    public async Task Bounded_replay_drains_without_phantom_depth_or_timeout()
    {
        using var directory = new ParserTestDirectory();
        var path = directory.CreateFile();
        var payload = new StringBuilder();
        for (var line = 0; line < 32; line++)
        {
            payload.Append("2026-08-04 18:20:07 synthetic line ");
            payload.Append(line);
            payload.Append("\r\n");
        }

        await File.WriteAllTextAsync(path, payload.ToString());

        var replayTime = new ManualReplayTimeProvider();
        var plan = ReplayTestPlanFactory.CreatePlan(
            path,
            new DateOnly(2026, 8, 4),
            ReplayInputMode.Exact,
            ReplayChunkMode.WholeLine);
        var workspace = await ReplayWorkspace.CreateAsync(keepWorkspace: true);
        var ledger = new ReplayLedger();
        var timeline = new ReplayTimeline(replayTime);
        await using var pipeline = new ReplayPipeline(
            replayTime,
            characterDataDirectory: Path.Combine(Path.GetTempPath(), $"replay-tail-drain-{Guid.NewGuid():n}"),
            lifecycleTimeProvider: TimeProvider.System,
            lifecycleTimeouts: new ReplayLifecycleTimeouts());

        var result = await pipeline.ExecuteAsync(
            new ReplayPipelineRequest { Contexts = [new ReplayContextBinding(workspace, plan)] },
            ledger,
            timeline);

        try
        {
            Assert.True(result.Success, string.Join("|", result.Correctness.Failures.Select(f => f.Code)));
            Assert.True(result.Parser.ParserDrained);
            Assert.DoesNotContain(
                result.Correctness.Failures,
                failure => failure.Code == "drain.timeout");
        }
        finally
        {
            Directory.Delete(workspace.RootPath, recursive: true);
        }
    }
}
