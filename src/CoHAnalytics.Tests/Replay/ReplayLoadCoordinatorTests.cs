using CoHAnalytics.Replay;

namespace CoHAnalytics.Tests.Replay;

[Collection(nameof(ReplayPipelineCollection))]
public sealed class ReplayLoadCoordinatorTests
{
    [Fact]
    public async Task Concurrent_contexts_start_together_and_complete()
    {
        var root = Path.Combine(Path.GetTempPath(), $"replay-coordinator-{Guid.NewGuid():n}");
        Directory.CreateDirectory(root);
        var time = new ManualReplayTimeProvider();
        var timeline = new ReplayTimeline(time);
        var suffix = Guid.NewGuid().ToString("n");
        var workspaceA = await ReplayWorkspace.CreateAsync(
            keepWorkspace: true,
            accountFolderName: $"replay-coordinator-a-{suffix}");
        var workspaceB = await ReplayWorkspace.CreateAsync(
            keepWorkspace: true,
            accountFolderName: $"replay-coordinator-b-{suffix}");
        try
        {
            var resolution = ReplayProfileCatalog.Resolve(
                "dual-client",
                new ReplayProfileOverrides
                {
                    StressLines = 40,
                    ContextARate = "maximum",
                    ContextBRate = "maximum"
                },
                root);
            var coordinator = new ReplayLoadCoordinator(time, timeline);
            var jobs = new[]
            {
                new ReplayLoadContextJob
                {
                    ContextIndex = 0,
                    Label = "context-a",
                    Workspace = workspaceA,
                    Plan = resolution.ContextPlans[0].Plan,
                    Ledger = new ReplayLedger()
                },
                new ReplayLoadContextJob
                {
                    ContextIndex = 1,
                    Label = "context-b",
                    Workspace = workspaceB,
                    Plan = resolution.ContextPlans[1].Plan,
                    Ledger = new ReplayLedger()
                }
            };

            var result = await coordinator.ExecuteAsync(jobs, performanceCollector: null, CancellationToken.None);
            Assert.Equal(2, result.ContextResults.Count);
            Assert.True(result.ContextResults.All(context => context.Ledger.OriginalSourceCompleteLines > 0));
            Assert.Contains(timeline.Events, entry => entry.Code == "load.coordination.started");
            Assert.Equal(2, timeline.Events.Count(entry => entry.Code == "load.context.completed"));
        }
        finally
        {
            await workspaceA.DisposeAsync();
            await workspaceB.DisposeAsync();
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Cancellation_shuts_down_coordinator_authoritatively()
    {
        var root = Path.Combine(Path.GetTempPath(), $"replay-coordinator-cancel-{Guid.NewGuid():n}");
        Directory.CreateDirectory(root);
        var time = new ManualReplayTimeProvider();
        var timeline = new ReplayTimeline(time);
        var workspace = await ReplayWorkspace.CreateAsync(keepWorkspace: true);
        try
        {
            var resolution = ReplayProfileCatalog.Resolve(
                "fixed-rate",
                new ReplayProfileOverrides { StressLines = 500, RateLinesPerSecond = 100 },
                root);
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var coordinator = new ReplayLoadCoordinator(time, timeline);
            var jobs = new[]
            {
                new ReplayLoadContextJob
                {
                    ContextIndex = 0,
                    Label = "context-1",
                    Workspace = workspace,
                    Plan = resolution.ContextPlans[0].Plan,
                    Ledger = new ReplayLedger()
                }
            };

            await Assert.ThrowsAsync<OperationCanceledException>(() =>
                coordinator.ExecuteAsync(jobs, performanceCollector: null, cts.Token));
        }
        finally
        {
            await workspace.DisposeAsync();
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Aggregate_rate_spans_earliest_start_to_latest_end()
    {
        var time = new ManualReplayTimeProvider();
        var throughput = ReplayConcurrentWriteThroughput.Calculate(
            [
                new ReplayContextWriteTimingInput
                {
                    Label = "context-a",
                    SourceCompleteLines = 100,
                    SourceBytes = 1_000,
                    WriteStartTimestamp = 0,
                    WriteEndTimestamp = 100
                },
                new ReplayContextWriteTimingInput
                {
                    Label = "context-b",
                    SourceCompleteLines = 100,
                    SourceBytes = 1_000,
                    WriteStartTimestamp = 50,
                    WriteEndTimestamp = 150
                }
            ],
            time);

        Assert.NotNull(throughput);
        Assert.Equal(200, throughput!.AggregateSourceCompleteLines);
        Assert.Equal(150, throughput.AggregateWriteDurationMilliseconds);
        Assert.Equal(200 / 0.15, throughput.AggregateLinesPerSecond!.Value, 3);
        var summedContextRates = throughput.Contexts.Sum(context => context.AchievedLinesPerSecond ?? 0);
        Assert.NotEqual(throughput.AggregateLinesPerSecond, summedContextRates);
    }

    [Fact]
    public void Zero_line_context_does_not_inflate_aggregate_rate()
    {
        var time = new ManualReplayTimeProvider();
        var throughput = ReplayConcurrentWriteThroughput.Calculate(
            [
                new ReplayContextWriteTimingInput
                {
                    Label = "context-a",
                    SourceCompleteLines = 80,
                    SourceBytes = 800,
                    WriteStartTimestamp = 0,
                    WriteEndTimestamp = 80
                },
                new ReplayContextWriteTimingInput
                {
                    Label = "context-b",
                    SourceCompleteLines = 0,
                    SourceBytes = 0,
                    WriteStartTimestamp = 10,
                    WriteEndTimestamp = 40
                }
            ],
            time);

        Assert.NotNull(throughput);
        Assert.Equal(80, throughput!.AggregateSourceCompleteLines);
        Assert.Equal(80, throughput.AggregateWriteDurationMilliseconds);
        Assert.Equal(80 / 0.08, throughput.AggregateLinesPerSecond!.Value, 3);
        Assert.Null(throughput.Contexts[1].AchievedLinesPerSecond);
    }

    [Fact]
    public void Invalid_aggregate_duration_reports_unavailable_rates()
    {
        var time = new ManualReplayTimeProvider();
        var throughput = ReplayConcurrentWriteThroughput.Calculate(
            [
                new ReplayContextWriteTimingInput
                {
                    Label = "context-a",
                    SourceCompleteLines = 10,
                    SourceBytes = 100,
                    WriteStartTimestamp = 100,
                    WriteEndTimestamp = 100
                }
            ],
            time);

        Assert.NotNull(throughput);
        Assert.Null(throughput!.AggregateWriteDurationMilliseconds);
        Assert.Null(throughput.AggregateLinesPerSecond);
        Assert.Null(throughput.AggregateBytesPerSecond);
    }

    [Theory]
    [InlineData(3)]
    public void Expected_overload_admission_splits_after_complete_lines(int admissionLineCount)
    {
        var source = "line-1\nline-2\nline-3\nline-4\n"u8.ToArray();
        var (prefix, suffix) = ReplayExpectedOverloadAdmission.SplitAfterCompleteLines(
            source,
            admissionLineCount);
        Assert.Equal(admissionLineCount, prefix.Count(c => c == (byte)'\n'));
        Assert.Equal(source.Length - prefix.Length, suffix.Length);
    }
}
