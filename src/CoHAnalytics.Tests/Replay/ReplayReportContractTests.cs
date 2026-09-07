using System.Text.Json;
using CoHAnalytics.Replay;
using CoHAnalytics.Replay.Reporting;

namespace CoHAnalytics.Tests.Replay;

public sealed class ReplayReportContractTests
{
    [Fact]
    public async Task Json_contract_uses_camel_case_and_section_schema_versions()
    {
        var sourcePath = ReplayTestPaths.Fixture("core-session.log");
        var plan = ReplayTestPlanFactory.CreatePlan(
            sourcePath,
            new DateOnly(2026, 1, 15),
            ReplayInputMode.Bootstrap);
        var (ledger, workspace) = await ReplayTestPlanFactory.ExecuteAsync(plan);
        var timeline = new ReplayTimeline(new ManualReplayTimeProvider());
        timeline.Record(ReplayTimelineCategory.Run, "run.started", ReplayTimelineRetentionClass.Anchor);
        timeline.Record(ReplayTimelineCategory.Run, "run.completed", ReplayTimelineRetentionClass.Anchor);

        var report = ReplayReportBuilder.Build(
            plan,
            ledger,
            timeline,
            workspace,
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            DateTimeOffset.Parse("2026-01-01T00:00:01Z"),
            1000,
            "Completed");

        var json = ReplayReportBuilder.ToJson(report);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(1, root.GetProperty("run").GetProperty("schemaVersion").GetInt32());
        Assert.Equal(1, root.GetProperty("input").GetProperty("schemaVersion").GetInt32());
        Assert.Equal(1, root.GetProperty("replay").GetProperty("schemaVersion").GetInt32());
        Assert.Equal(1, root.GetProperty("timeline").GetProperty("schemaVersion").GetInt32());
        Assert.Equal("Bootstrap", root.GetProperty("input").GetProperty("mode").GetString());
        Assert.False(root.TryGetProperty("parser", out _));
        Assert.False(root.TryGetProperty("gameplay", out _));

        Directory.Delete(workspace.RootPath, recursive: true);
    }

    [Fact]
    public void Unknown_top_level_properties_deserialize_without_failure()
    {
        const string json =
            """
            {
              "schemaVersion": 1,
              "run": { "schemaVersion": 1, "status": "Completed", "startedAtUtc": "2026-01-01T00:00:00Z", "completedAtUtc": "2026-01-01T00:00:01Z", "elapsedMilliseconds": 1, "workspaceRetained": false, "workspaceCleanupCompleted": true },
              "input": { "schemaVersion": 1, "mode": "Exact", "beginsMidSession": false, "sourceBytes": 1, "sourceCompleteLines": 1, "bootstrapBytes": 0, "bootstrapCompleteLines": 0, "combinedDestinationBytes": 1, "combinedDestinationCompleteLines": 1, "hasIncompleteFinalFragment": false, "incompleteFinalFragmentBytes": 0, "segmentCount": 1 },
              "replay": { "schemaVersion": 1, "chunkMode": "WholeLine", "timingMode": "Maximum", "seed": 1, "plannedChunks": 1, "appendedChunks": 1, "flushes": 1, "burstCount": 0, "rolloverCount": 0, "segments": [] },
              "timeline": { "schemaVersion": 1, "capacity": 256, "droppedEventCount": 0, "truncated": false, "events": [] },
              "futureSection": { "value": 42 }
            }
            """;

        var report = JsonSerializer.Deserialize<ReplayReportDocument>(json);
        Assert.NotNull(report);
        Assert.NotNull(report!.ExtensionData);
        Assert.True(report.ExtensionData!.ContainsKey("futureSection"));
    }

    [Fact]
    public void Text_reporter_does_not_dump_unknown_json_payloads()
    {
        var report = new ReplayReportDocument
        {
            ExtensionData = new Dictionary<string, JsonElement>
            {
                ["futureSection"] = JsonSerializer.SerializeToElement(new { secret = "value" })
            }
        };

        var text = ReplayReportBuilder.ToText(report);
        Assert.DoesNotContain("futureSection", text);
        Assert.DoesNotContain("secret", text);
        Assert.DoesNotContain("parser", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Bootstrap_counts_are_separate_from_source_throughput()
    {
        var sourcePath = ReplayTestPaths.Fixture("core-session.log");
        var sourceBytes = await File.ReadAllBytesAsync(sourcePath);
        var plan = ReplayTestPlanFactory.CreatePlan(
            sourcePath,
            new DateOnly(2026, 1, 15),
            ReplayInputMode.Bootstrap);
        var (ledger, workspace) = await ReplayTestPlanFactory.ExecuteAsync(plan);
        var report = ReplayReportBuilder.Build(
            plan,
            ledger,
            new ReplayTimeline(new ManualReplayTimeProvider()),
            workspace,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            0,
            "Completed");

        Assert.Equal(sourceBytes.Length, report.Input.SourceBytes);
        Assert.Equal(ReplayPlan.BootstrapBytes.Length, report.Input.BootstrapBytes);
        Assert.Equal(sourceBytes.Length + ReplayPlan.BootstrapBytes.Length, report.Input.CombinedDestinationBytes);
        Assert.Contains("Bootstrap input was injected", ReplayReportBuilder.ToText(report));

        Directory.Delete(workspace.RootPath, recursive: true);
    }

    [Fact]
    public async Task Reports_exclude_source_and_workspace_paths()
    {
        var sourcePath = ReplayTestPaths.Fixture("core-session.log");
        var plan = ReplayTestPlanFactory.CreatePlan(
            sourcePath,
            new DateOnly(2026, 1, 15),
            ReplayInputMode.Exact);
        var (ledger, workspace) = await ReplayTestPlanFactory.ExecuteAsync(plan);
        var report = ReplayReportBuilder.Build(
            plan,
            ledger,
            new ReplayTimeline(new ManualReplayTimeProvider()),
            workspace,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            0,
            "Completed");

        var json = ReplayReportBuilder.ToJson(report);
        var text = ReplayReportBuilder.ToText(report);
        Assert.DoesNotContain(sourcePath, json);
        Assert.DoesNotContain(workspace.RootPath, json);
        Assert.DoesNotContain(sourcePath, text);
        Assert.DoesNotContain(workspace.RootPath, text);

        Directory.Delete(workspace.RootPath, recursive: true);
    }

    [Fact]
    public void Performance_sections_round_trip_in_json_and_text_summary()
    {
        var performance = CreateSamplePerformanceReport();
        var report = new ReplayReportDocument
        {
            Performance = new ReplayPerformanceSection
            {
                Validity = performance.Validity.ToString(),
                RunDurationMs = performance.RunDurationMilliseconds,
                WriteDurationMs = performance.WriteDurationMilliseconds,
                DrainDurationMs = performance.DrainDurationMilliseconds,
                CorrectnessPassed = performance.CorrectnessPassed,
                DrainOutcome = performance.DrainOutcome.ToString(),
                GameplayOverloadState = performance.GameplayOverloadState,
                MeasurementNote = performance.CollectorOverhead.MeasurementNote
            },
            Throughput = new ReplayThroughputSection
            {
                SourceLinesPerSecond = performance.Throughput.SourceLinesPerSecond,
                SourceBytesPerSecond = performance.Throughput.SourceBytesPerSecond,
                SourceBytes = performance.SourceBytes,
                SourceCompleteLines = performance.SourceCompleteLines,
                BootstrapBytes = performance.BootstrapBytes
            },
            Queues = new ReplayQueuesSection
            {
                ParserEventQueue = new ReplayQueuePressureSection
                {
                    Capacity = performance.ParserEventQueue.Capacity,
                    PeakAbsoluteDepth = performance.ParserEventQueue.PeakAbsoluteDepth
                },
                GameplayWorkQueue = new ReplayQueuePressureSection
                {
                    Capacity = performance.GameplayWorkQueue.Capacity,
                    PeakAbsoluteDepth = performance.GameplayWorkQueue.PeakAbsoluteDepth
                }
            },
            Resources = new ReplayResourcesSection
            {
                CpuUtilizationPercentMax = 42.5,
                WorkingSetBytesMax = 2048,
                ManagedHeapBytesMax = 1024,
                SampleCount = performance.CollectorOverhead.SampleCount
            }
        };

        var json = ReplayReportBuilder.ToJson(report);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(1, document.RootElement.GetProperty("performance").GetProperty("schemaVersion").GetInt32());
        Assert.Equal(1, document.RootElement.GetProperty("throughput").GetProperty("schemaVersion").GetInt32());
        Assert.Equal(1, document.RootElement.GetProperty("queues").GetProperty("schemaVersion").GetInt32());
        Assert.Equal(1, document.RootElement.GetProperty("resources").GetProperty("schemaVersion").GetInt32());

        var text = ReplayReportBuilder.ToText(report);
        Assert.Contains("Performance", text);
        Assert.Contains("Throughput", text);
        Assert.Contains("Queues", text);
        Assert.Contains("Resources", text);
        Assert.Contains("Correctness passed", text);
        Assert.Contains("Drain duration", text);
        Assert.Contains("Parser event queue peak", text);
        Assert.Contains("Gameplay work queue peak", text);
        Assert.Contains("CPU estimate max", text);
        Assert.DoesNotContain("Example Hero", text);
        Assert.DoesNotContain(@"C:\", text);
    }

    [Fact]
    public void Unknown_performance_sections_deserialize_without_failure()
    {
        const string json =
            """
            {
              "schemaVersion": 1,
              "run": { "schemaVersion": 1, "status": "Completed", "startedAtUtc": "2026-01-01T00:00:00Z", "completedAtUtc": "2026-01-01T00:00:01Z", "elapsedMilliseconds": 1, "workspaceRetained": false, "workspaceCleanupCompleted": true },
              "input": { "schemaVersion": 1, "mode": "Exact", "beginsMidSession": false, "sourceBytes": 1, "sourceCompleteLines": 1, "bootstrapBytes": 0, "bootstrapCompleteLines": 0, "combinedDestinationBytes": 1, "combinedDestinationCompleteLines": 1, "hasIncompleteFinalFragment": false, "incompleteFinalFragmentBytes": 0, "segmentCount": 1 },
              "replay": { "schemaVersion": 1, "chunkMode": "WholeLine", "timingMode": "Maximum", "seed": 1, "plannedChunks": 1, "appendedChunks": 1, "flushes": 1, "burstCount": 0, "rolloverCount": 0, "segments": [] },
              "timeline": { "schemaVersion": 1, "capacity": 256, "droppedEventCount": 0, "truncated": false, "events": [] },
              "performance": { "schemaVersion": 1, "validity": "Valid", "runDurationMs": 1, "correctnessPassed": true, "drainOutcome": "Completed", "gameplayOverloadState": false, "measurementNote": "note" },
              "futurePerformanceSection": { "value": 42 }
            }
            """;

        var report = JsonSerializer.Deserialize<ReplayReportDocument>(json);
        Assert.NotNull(report);
        Assert.NotNull(report!.Performance);
        Assert.NotNull(report.ExtensionData);
        Assert.True(report.ExtensionData!.ContainsKey("futurePerformanceSection"));
    }

    [Fact]
    public async Task Performance_sections_include_profile_when_present()
    {
        var plan = ReplayTestPlanFactory.CreatePlan(
            ReplayTestPaths.Fixture("core-session.log"),
            new DateOnly(2026, 1, 15),
            ReplayInputMode.Exact);
        var (ledger, workspace) = await ReplayTestPlanFactory.ExecuteAsync(plan);
        var report = ReplayReportBuilder.Build(
            plan,
            ledger,
            new ReplayTimeline(new ManualReplayTimeProvider()),
            workspace,
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            DateTimeOffset.Parse("2026-01-01T00:00:01Z"),
            1000,
            "Completed",
            new ReplayPipelineResult
            {
                Correctness = new ReplayCorrectnessReport { Passed = true },
                Parser = new ReplayParserObservationReport
                {
                    RawEventsObserved = 1,
                    ClassifiedEventsObserved = 1,
                    TotalLinesProcessed = 1,
                    ExpectedCompleteLines = 1,
                    WorkerCount = 1,
                    ParserDrained = true
                },
                Gameplay = new ReplayGameplayObservationReport
                {
                    CommittedEventsObserved = 1,
                    ActiveSessionCount = 1,
                    GameplayDrained = true,
                    FailedContextCount = 0,
                    TotalCommittedEvents = 1,
                    MonitoringContextCount = 1
                },
                Profile = new ReplayProfileObservationReport
                {
                    ProfileName = "baseline",
                    EffectiveOptions = new Dictionary<string, string> { ["profile"] = "baseline" },
                    ContextCount = 1,
                    Outcome = ReplayProfileOutcome.Passed
                },
                Performance = CreateSamplePerformanceReport()
            });

        var json = ReplayReportBuilder.ToJson(report);
        using var document = JsonDocument.Parse(json);
        Assert.Equal("baseline", document.RootElement.GetProperty("profile").GetProperty("name").GetString());
        Directory.Delete(workspace.RootPath, recursive: true);
    }

    [Fact]
    public async Task Dual_client_cli_text_json_and_snapshot_expose_same_aggregate_rates()
    {
        var time = new ManualReplayTimeProvider(new DateTimeOffset(2026, 8, 1, 10, 0, 0, TimeSpan.Zero));
        var root = Path.Combine(Path.GetTempPath(), $"replay-report-dual-{Guid.NewGuid():n}");
        Directory.CreateDirectory(root);
        var resolution = ReplayProfileCatalog.Resolve(
            "dual-client",
            new ReplayProfileOverrides
            {
                StressLines = 40,
                ContextARate = "500",
                ContextBRate = "500"
            },
            root);
        var ledger = new ReplayLedger();
        var timeline = new ReplayTimeline(time);
        var workspaces = new List<ReplayWorkspace>();
        var bindings = new List<ReplayContextBinding>();
        for (var index = 0; index < resolution.ContextPlans.Count; index++)
        {
            var workspace = await ReplayWorkspace.CreateAsync(
                keepWorkspace: true,
                accountFolderName: $"replay-report-dual-{index + 1:D2}");
            workspaces.Add(workspace);
            bindings.Add(new ReplayContextBinding(workspace, resolution.ContextPlans[index].Plan));
        }

        try
        {
            await using var pipeline = new ReplayPipeline(time);
            var pipelineResult = await pipeline.ExecuteAsync(
                new ReplayPipelineRequest
                {
                    Contexts = bindings,
                    ProfileResolution = resolution
                },
                ledger,
                timeline);

            var report = ReplayReportBuilder.Build(
                bindings[0].Plan,
                ledger,
                timeline,
                workspaces[0],
                DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
                DateTimeOffset.Parse("2026-01-01T00:00:01Z"),
                1000,
                "Completed",
                pipelineResult);
            var json = ReplayReportBuilder.ToJson(report);
            using var document = JsonDocument.Parse(json);
            var throughput = pipelineResult.Performance!.ConcurrentWriteThroughput!;
            var jsonThroughput = document.RootElement.GetProperty("throughput");

            Assert.Equal(throughput.AggregateSourceCompleteLines, pipelineResult.Performance.SourceCompleteLines);
            Assert.Equal(throughput.AggregateSourceBytes, pipelineResult.Performance.SourceBytes);
            Assert.Equal(throughput.AggregateWriteDurationMilliseconds, pipelineResult.Performance.WriteDurationMilliseconds);
            Assert.Equal(throughput.AggregateLinesPerSecond, pipelineResult.Performance.AchievedLinesPerSecond);
            Assert.Equal(throughput.AggregateLinesPerSecond, pipelineResult.Profile?.AchievedRateLinesPerSecond);
            Assert.Equal(
                throughput.AggregateLinesPerSecond,
                jsonThroughput.GetProperty("sourceLinesPerSecond").GetDouble());
            Assert.Equal(
                throughput.AggregateSourceCompleteLines,
                jsonThroughput.GetProperty("sourceCompleteLines").GetInt64());
            Assert.Equal(
                throughput.AggregateWriteDurationMilliseconds,
                jsonThroughput.GetProperty("aggregateWriteDurationMs").GetInt64());

            var text = ReplayReportBuilder.ToText(report);
            Assert.Contains($"Source lines: {throughput.AggregateSourceCompleteLines}", text);
            Assert.Contains($"Aggregate write duration: {throughput.AggregateWriteDurationMilliseconds} ms", text);
            Assert.Equal(2, throughput.Contexts.Count);
            foreach (var context in throughput.Contexts)
            {
                Assert.Contains($"Context {context.Label}:", text);
            }
        }
        finally
        {
            foreach (var workspace in workspaces)
            {
                await workspace.DisposeAsync();
            }

            Directory.Delete(root, recursive: true);
        }
    }

    private static ReplayPerformanceObservationReport CreateSamplePerformanceReport() =>
        new()
        {
            Validity = ReplayPerformanceValidity.Valid,
            RunDurationMilliseconds = 1000,
            WriteDurationMilliseconds = 200,
            DrainDurationMilliseconds = 300,
            SourceBytes = 100,
            SourceCompleteLines = 10,
            ReplayedBytes = 100,
            ReplayedCompleteLines = 10,
            BootstrapBytes = 0,
            BootstrapCompleteLines = 0,
            AchievedLinesPerSecond = 50,
            AchievedBytesPerSecond = 500,
            ParserMonitoringSnapshotQueue = CreateQueueSummary(64, 2),
            ParserEventQueue = CreateQueueSummary(256, 12),
            GameplayWorkQueue = CreateQueueSummary(512, 3),
            PendingCommittedEvents = new ReplayPendingCommittedEventSummary
            {
                Capacity = 4096,
                CurrentCount = 0,
                PeakCount = 1,
                DiscardedCount = 0
            },
            ParserTotalLinesProcessed = 10,
            ParserRawEventsObserved = 10,
            ParserClassifiedEventsObserved = 10,
            ParserWorkerCount = 1,
            GameplayAcceptedWorkWatermark = 10,
            GameplayCompletedWorkWatermark = 10,
            GameplayActiveProcessorCallbackCount = 0,
            GameplayCommittedEventCount = 10,
            GameplayOverloadState = false,
            GameplayDiscardedCommittedEvents = 0,
            GameplayAbandonedWorkCount = 0,
            CorrectnessPassed = true,
            DrainOutcome = ReplayDrainOutcome.Completed,
            Throughput = new ReplayThroughputSummary
            {
                SourceLinesPerSecond = 50,
                SourceBytesPerSecond = 500,
                ParserEventsPerSecond = 33.3,
                GameplayEventsPerSecond = 33.3
            },
            CollectorOverhead = new ReplayCollectorOverheadSummary
            {
                SampleCount = 4,
                DroppedSampleCount = 0,
                SampleIntervalMilliseconds = 250,
                TotalSamplingDurationMilliseconds = 1,
                MaxSingleSampleDurationMilliseconds = 1,
                MeasurementNote = "Measurements include Replay harness and observer overhead."
            }
        };

    private static ReplayQueuePressureSummary CreateQueueSummary(int capacity, int peak) =>
        new()
        {
            LifecycleEpoch = 1,
            Capacity = capacity,
            AcceptedCount = peak,
            CompletedCount = peak,
            RejectedCount = 0,
            AbandonedCount = 0,
            CurrentDepth = 0,
            PeakDepth = peak,
            InFlightCount = 0,
            Overflowed = false,
            IsDrained = true,
            PeakAbsoluteDepth = peak,
            PeakDepthFraction = peak / (double)capacity,
            BacklogObserved = peak > 0,
            BacklogCleared = true,
            ThresholdsCrossed = [25]
        };
}
