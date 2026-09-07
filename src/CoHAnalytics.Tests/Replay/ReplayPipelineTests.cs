using CoHAnalytics.Models;
using CoHAnalytics.Replay;
using CoHAnalytics.Services;
using CoHAnalytics.Tests.Services;

namespace CoHAnalytics.Tests.Replay;

[Collection(nameof(ReplayPipelineCollection))]
public sealed class ReplayPipelineTests
{
    [Fact]
    public async Task Single_context_exact_replay_drains_parser_and_gameplay()
    {
        var sourcePath = ReplayTestPaths.Fixture("core-session.log");
        var plan = ReplayTestPlanFactory.CreatePlan(
            sourcePath,
            new DateOnly(2026, 1, 15),
            ReplayInputMode.Exact,
            ReplayChunkMode.WholeLine);

        var (result, workspace) = await ExecutePipelineAsync(plan);
        try
        {
            Assert.True(
                result.Success,
                $"raw={result.Parser.RawEventsObserved}; classified={result.Parser.ClassifiedEventsObserved}; expected={result.Parser.ExpectedCompleteLines}; processed={result.Parser.TotalLinesProcessed}; commits={result.Gameplay.CommittedEventsObserved}; totalCommits={result.Gameplay.TotalCommittedEvents}; failedCtx={result.Gameplay.FailedContextCount}; monitoringCtx={result.Gameplay.MonitoringContextCount}; welcome={result.Correctness.WelcomeBoundariesObserved}; rules={string.Join(",", result.Correctness.ClassificationRuleIds)}; failures={string.Join("|", result.Correctness.Failures.Select(failure => failure.Code))}");
            Assert.True(result.Parser.RawEventsObserved > 0);
            Assert.True(result.Parser.ClassifiedEventsObserved > 0);
            Assert.True(result.Gameplay.CommittedEventsObserved > 0);
            Assert.True(result.Parser.ParserDrained);
            Assert.True(result.Gameplay.GameplayDrained);
            Assert.NotNull(result.Performance);
            Assert.Equal(ReplayPerformanceValidity.Valid, result.Performance!.Validity);
            Assert.DoesNotContain("Example Hero", result.Performance.CollectorOverhead.MeasurementNote);
            Assert.False(result.Correctness.BeginsMidSession);
            Assert.True(result.Correctness.WelcomeBoundariesObserved >= 1);
        }
        finally
        {
            Directory.Delete(workspace.RootPath, recursive: true);
        }
    }

    [Fact]
    public async Task Bootstrap_mode_replay_drains_with_separate_accounting()
    {
        var sourcePath = ReplayTestPaths.Fixture("core-session.log");
        var plan = ReplayTestPlanFactory.CreatePlan(
            sourcePath,
            new DateOnly(2026, 1, 15),
            ReplayInputMode.Bootstrap,
            ReplayChunkMode.WholeLine);

        var (result, workspace) = await ExecutePipelineAsync(plan);
        try
        {
            Assert.True(
                result.Success,
                $"raw={result.Parser.RawEventsObserved}; classified={result.Parser.ClassifiedEventsObserved}; expected={result.Parser.ExpectedCompleteLines}; processed={result.Parser.TotalLinesProcessed}; commits={result.Gameplay.CommittedEventsObserved}; totalCommits={result.Gameplay.TotalCommittedEvents}; failedCtx={result.Gameplay.FailedContextCount}; monitoringCtx={result.Gameplay.MonitoringContextCount}; welcome={result.Correctness.WelcomeBoundariesObserved}; rules={string.Join(",", result.Correctness.ClassificationRuleIds)}; failures={string.Join("|", result.Correctness.Failures.Select(failure => failure.Code))}");
            Assert.True(result.Parser.RawEventsObserved > 0);
            Assert.True(result.Correctness.WelcomeBoundariesObserved >= 2);
        }
        finally
        {
            Directory.Delete(workspace.RootPath, recursive: true);
        }
    }

    [Fact]
    public async Task Rollover_replay_flushes_segments_and_drains_parser()
    {
        var plan = ReplayPlan.Create(
            [
                new ReplaySourceSegment(ReplayTestPaths.Fixture("rollover-day-1.log"), new DateOnly(2026, 1, 15), 1),
                new ReplaySourceSegment(ReplayTestPaths.Fixture("rollover-day-2.log"), new DateOnly(2026, 1, 16), 2)
            ],
            ReplayInputMode.Exact,
            ReplayChunkMode.WholeLine,
            1,
            4096,
            1024,
            12345,
            ReplayTimingMode.Maximum,
            10,
            4096,
            1,
            2,
            TimeSpan.FromMilliseconds(100),
            keepWorkspace: true,
            jsonReportPath: null,
            textReportPath: null);

        var (result, workspace) = await ExecutePipelineAsync(plan);
        try
        {
            Assert.True(
                result.Success,
                $"raw={result.Parser.RawEventsObserved}; classified={result.Parser.ClassifiedEventsObserved}; expected={result.Parser.ExpectedCompleteLines}; processed={result.Parser.TotalLinesProcessed}; commits={result.Gameplay.CommittedEventsObserved}; totalCommits={result.Gameplay.TotalCommittedEvents}; failedCtx={result.Gameplay.FailedContextCount}; monitoringCtx={result.Gameplay.MonitoringContextCount}; welcome={result.Correctness.WelcomeBoundariesObserved}; rules={string.Join(",", result.Correctness.ClassificationRuleIds)}; failures={string.Join("|", result.Correctness.Failures.Select(failure => failure.Code))}");
            Assert.True(
                result.Parser.TotalLinesProcessed >= 4,
                $"processed={result.Parser.TotalLinesProcessed}; expected={result.Parser.ExpectedCompleteLines}; monitoringCtx={result.Gameplay.MonitoringContextCount}");
            Assert.True(result.Parser.ParserDrained);
        }
        finally
        {
            Directory.Delete(workspace.RootPath, recursive: true);
        }
    }

    [Fact]
    public async Task Dual_context_replay_keeps_parser_and_gameplay_isolated()
    {
        var midSessionFixturePath = CreateMidSessionFixture();
        var planA = ReplayTestPlanFactory.CreatePlan(
            ReplayTestPaths.Fixture("core-session.log"),
            new DateOnly(2026, 1, 15),
            ReplayInputMode.Exact,
            ReplayChunkMode.WholeLine);
        var planB = ReplayTestPlanFactory.CreatePlan(
            midSessionFixturePath,
            new DateOnly(2026, 1, 16),
            ReplayInputMode.Exact,
            ReplayChunkMode.WholeLine);

        var workspaceA = await ReplayWorkspace.CreateAsync(keepWorkspace: true, accountFolderName: "replay-account-01");
        var workspaceB = await ReplayWorkspace.CreateAsync(keepWorkspace: true, accountFolderName: "replay-account-02");
        var ledger = new ReplayLedger();
        var time = new ManualReplayTimeProvider(new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero));
        var timeline = new ReplayTimeline(time);

        await using var pipeline = new ReplayPipeline(time);
        var result = await pipeline.ExecuteAsync(
            new ReplayPipelineRequest
            {
                Contexts =
                [
                    new ReplayContextBinding(workspaceA, planA),
                    new ReplayContextBinding(workspaceB, planB)
                ]
            },
            ledger,
            timeline);

        try
        {
            Assert.True(
                result.Success,
                $"raw={result.Parser.RawEventsObserved}; classified={result.Parser.ClassifiedEventsObserved}; expected={result.Parser.ExpectedCompleteLines}; processed={result.Parser.TotalLinesProcessed}; commits={result.Gameplay.CommittedEventsObserved}; totalCommits={result.Gameplay.TotalCommittedEvents}; failedCtx={result.Gameplay.FailedContextCount}; monitoringCtx={result.Gameplay.MonitoringContextCount}; welcome={result.Correctness.WelcomeBoundariesObserved}; rules={string.Join(",", result.Correctness.ClassificationRuleIds)}; failures={string.Join("|", result.Correctness.Failures.Select(failure => failure.Code))}");
            Assert.Equal(2, result.Correctness.ContextCount);
            Assert.DoesNotContain(result.Correctness.Failures, failure => failure.Code == "context.contamination");
            Assert.DoesNotContain(result.Correctness.Failures, failure => failure.Code == "account.contamination");
            Assert.True(result.Gameplay.GameplayDrained);
        }
        finally
        {
            Directory.Delete(workspaceA.RootPath, recursive: true);
            Directory.Delete(workspaceB.RootPath, recursive: true);
            if (File.Exists(midSessionFixturePath))
            {
                File.Delete(midSessionFixturePath);
            }
        }
    }

    [Fact]
    public async Task Bootstrap_replay_against_no_welcome_fixture_reports_welcome_missing()
    {
        var sourcePath = CreateNoWelcomeFixture();
        var plan = ReplayTestPlanFactory.CreatePlan(
            sourcePath,
            new DateOnly(2026, 1, 15),
            ReplayInputMode.Bootstrap,
            ReplayChunkMode.WholeLine);

        ReplayPipeline.AdditionalExpectedWelcomeBoundariesForTests = 1;
        var (result, workspace) = await ExecutePipelineAsync(plan);
        try
        {
            Assert.False(result.Success);
            Assert.Contains(
                result.Correctness.Failures,
                failure => failure.Code == "session.welcome-missing");
            Assert.DoesNotContain(
                result.Correctness.Failures,
                failure => failure.Code == "session.welcome-unexpected");
            Assert.DoesNotContain(
                result.Correctness.Failures,
                failure => failure.Code == "parser.duplicate");
            Assert.DoesNotContain(
                result.Correctness.Failures,
                failure => failure.Code == "parser.gap");
            AssertFailureMessagesArePayloadFree(result.Correctness.Failures);
        }
        finally
        {
            ReplayPipeline.AdditionalExpectedWelcomeBoundariesForTests = 0;
            Directory.Delete(workspace.RootPath, recursive: true);
            if (File.Exists(sourcePath))
            {
                File.Delete(sourcePath);
            }
        }
    }

    [Fact]
    public void CountExpectedWelcomeBoundaries_counts_proper_welcome_line()
    {
        var plan = ReplayTestPlanFactory.CreatePlan(
            ReplayTestPaths.Fixture("core-session.log"),
            new DateOnly(2026, 1, 15),
            ReplayInputMode.Exact,
            ReplayChunkMode.WholeLine);

        Assert.Equal(1, ReplayPipeline.CountExpectedWelcomeBoundaries(plan));
    }

    [Fact]
    public void CountExpectedWelcomeBoundaries_ignores_chat_false_positive()
    {
        var sourcePath = CreateChatFalsePositiveFixture();
        try
        {
            var plan = ReplayTestPlanFactory.CreatePlan(
                sourcePath,
                new DateOnly(2026, 1, 15),
                ReplayInputMode.Exact,
                ReplayChunkMode.WholeLine);

            Assert.Equal(0, ReplayPipeline.CountExpectedWelcomeBoundaries(plan));
        }
        finally
        {
            if (File.Exists(sourcePath))
            {
                File.Delete(sourcePath);
            }
        }
    }

    [Fact]
    public void CountExpectedWelcomeBoundaries_ignores_malformed_welcome()
    {
        var sourcePath = CreateMalformedWelcomeFixture();
        try
        {
            var plan = ReplayTestPlanFactory.CreatePlan(
                sourcePath,
                new DateOnly(2026, 1, 15),
                ReplayInputMode.Exact,
                ReplayChunkMode.WholeLine);

            Assert.Equal(0, ReplayPipeline.CountExpectedWelcomeBoundaries(plan));
        }
        finally
        {
            if (File.Exists(sourcePath))
            {
                File.Delete(sourcePath);
            }
        }
    }

    [Fact]
    public void CountExpectedWelcomeBoundaries_counts_rollover_segments_independently()
    {
        var plan = ReplayPlan.Create(
            [
                new ReplaySourceSegment(ReplayTestPaths.Fixture("rollover-day-1.log"), new DateOnly(2026, 1, 15), 1),
                new ReplaySourceSegment(ReplayTestPaths.Fixture("rollover-day-2.log"), new DateOnly(2026, 1, 16), 2)
            ],
            ReplayInputMode.Exact,
            ReplayChunkMode.WholeLine,
            1,
            4096,
            1024,
            12345,
            ReplayTimingMode.Maximum,
            10,
            4096,
            1,
            2,
            TimeSpan.FromMilliseconds(100),
            keepWorkspace: true,
            jsonReportPath: null,
            textReportPath: null);

        Assert.Equal(1, ReplayPipeline.CountExpectedWelcomeBoundaries(plan));
    }

    [Fact]
    public void CountExpectedWelcomeBoundaries_accounts_for_bootstrap_mode()
    {
        var plan = ReplayTestPlanFactory.CreatePlan(
            ReplayTestPaths.Fixture("core-session.log"),
            new DateOnly(2026, 1, 15),
            ReplayInputMode.Bootstrap,
            ReplayChunkMode.WholeLine);

        Assert.Equal(2, ReplayPipeline.CountExpectedWelcomeBoundaries(plan));
    }

    [Fact]
    public void CountExpectedWelcomeBoundaries_accounts_for_exact_mode()
    {
        var plan = ReplayTestPlanFactory.CreatePlan(
            ReplayTestPaths.Fixture("core-session.log"),
            new DateOnly(2026, 1, 15),
            ReplayInputMode.Exact,
            ReplayChunkMode.WholeLine);

        Assert.Equal(1, ReplayPipeline.CountExpectedWelcomeBoundaries(plan));
    }

    [Fact]
    public void Gameplay_quiescence_requires_all_work_completed()
    {
        var quiescent = GameplaySessionTestInfrastructure.IdleGameplayDiagnostics(
            isRunning: true,
            snapshotRevision: 1,
            lastAcceptedWorkSequence: 2,
            lastCompletedWorkSequence: 2,
            activeSessionCount: 1,
            totalCommittedEvents: 2,
            lastCommittedEventAt: DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        var inFlight = GameplaySessionTestInfrastructure.IdleGameplayDiagnostics(
            isRunning: quiescent.IsRunning,
            snapshotRevision: quiescent.SnapshotRevision,
            lastAcceptedWorkSequence: quiescent.LastAcceptedWorkSequence,
            lastCompletedWorkSequence: quiescent.LastCompletedWorkSequence,
            activeSessionCount: quiescent.ActiveSessionCount,
            totalCommittedEvents: quiescent.TotalCommittedEvents,
            lastCommittedEventAt: quiescent.LastCommittedEventAt,
            activeProcessorCallbackCount: 1);
        var pending = GameplaySessionTestInfrastructure.IdleGameplayDiagnostics(
            isRunning: quiescent.IsRunning,
            snapshotRevision: quiescent.SnapshotRevision,
            lastAcceptedWorkSequence: quiescent.LastAcceptedWorkSequence,
            lastCompletedWorkSequence: quiescent.LastCompletedWorkSequence,
            activeSessionCount: quiescent.ActiveSessionCount,
            totalCommittedEvents: quiescent.TotalCommittedEvents,
            lastCommittedEventAt: quiescent.LastCommittedEventAt,
            pendingCommittedEventCount: 1);
        pending = new GameplaySessionDiagnostics
        {
            IsRunning = pending.IsRunning,
            SnapshotRevision = pending.SnapshotRevision,
            LifecycleEpoch = pending.LifecycleEpoch,
            ActiveSessionCount = pending.ActiveSessionCount,
            SuspendedSessionCount = pending.SuspendedSessionCount,
            NeedsAttentionSessionCount = pending.NeedsAttentionSessionCount,
            OverflowedSessionCount = pending.OverflowedSessionCount,
            TotalCommittedEvents = pending.TotalCommittedEvents,
            LastCommittedEventAt = pending.LastCommittedEventAt,
            FailedContextCount = pending.FailedContextCount,
            PendingCommittedEventCount = 1,
            WorkQueue = pending.WorkQueue,
            PendingCommittedEvents = pending.PendingCommittedEvents with { CurrentCount = 1 },
            WorkQueueOverflowed = pending.WorkQueueOverflowed,
            PreStartBufferOverflowed = pending.PreStartBufferOverflowed,
            LastAcceptedWorkSequence = pending.LastAcceptedWorkSequence,
            LastCompletedWorkSequence = pending.LastCompletedWorkSequence,
            ActiveProcessorCallbackCount = pending.ActiveProcessorCallbackCount,
            RecentOperations = pending.RecentOperations
        };
        var incomplete = GameplaySessionTestInfrastructure.IdleGameplayDiagnostics(
            isRunning: quiescent.IsRunning,
            snapshotRevision: quiescent.SnapshotRevision,
            lastAcceptedWorkSequence: quiescent.LastAcceptedWorkSequence,
            lastCompletedWorkSequence: 1,
            activeSessionCount: quiescent.ActiveSessionCount,
            totalCommittedEvents: quiescent.TotalCommittedEvents,
            lastCommittedEventAt: quiescent.LastCommittedEventAt);

        Assert.True(quiescent.IsQuiescent);
        Assert.False(inFlight.IsQuiescent);
        Assert.False(pending.IsQuiescent);
        Assert.False(incomplete.IsQuiescent);
    }

    private static async Task<(ReplayPipelineResult Result, ReplayWorkspace Workspace)> ExecutePipelineAsync(
        ReplayPlan plan)
    {
        var time = new ManualReplayTimeProvider(new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero));
        var workspace = await ReplayWorkspace.CreateAsync(keepWorkspace: true);
        var ledger = new ReplayLedger();
        var timeline = new ReplayTimeline(time);
        await using var pipeline = new ReplayPipeline(time);
        var result = await pipeline.ExecuteAsync(
            new ReplayPipelineRequest
            {
                Contexts = [new ReplayContextBinding(workspace, plan)]
            },
            ledger,
            timeline);
        return (result, workspace);
    }

    private static string CreateMidSessionFixture()
    {
        var path = Path.Combine(Path.GetTempPath(), $"replay-mid-session-{Guid.NewGuid():n}.log");
        File.WriteAllText(
            path,
            "2026-01-16 10:00:01 You are now leaving the example district.\r\n");
        return path;
    }

    private static string CreateNoWelcomeFixture()
    {
        var path = Path.Combine(Path.GetTempPath(), $"replay-no-welcome-{Guid.NewGuid():n}.log");
        File.WriteAllText(
            path,
            """
            2026-01-16 10:00:01 You are now leaving the example district.
            2026-01-16 10:00:02 [Team] Example Hero says, "Welcome to City of Heroes, everyone!"
            """.Replace("\n", "\r\n"));
        return path;
    }

    private static string CreateChatFalsePositiveFixture()
    {
        var path = Path.Combine(Path.GetTempPath(), $"replay-chat-false-positive-{Guid.NewGuid():n}.log");
        File.WriteAllText(
            path,
            """
            2026-01-15 10:00:00 [Team] Example Hero says, "Welcome to City of Heroes, everyone!"
            2026-01-15 10:00:01 You are now leaving the example district.
            """.Replace("\n", "\r\n"));
        return path;
    }

    private static string CreateMalformedWelcomeFixture()
    {
        var path = Path.Combine(Path.GetTempPath(), $"replay-malformed-welcome-{Guid.NewGuid():n}.log");
        File.WriteAllText(
            path,
            "2026-01-15 10:00:00 Welcome to City of Heroes!\r\n");
        return path;
    }

    private static void AssertFailureMessagesArePayloadFree(
        IReadOnlyList<ReplayCorrectnessFailure> failures)
    {
        foreach (var failure in failures)
        {
            Assert.DoesNotContain("Example Hero", failure.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("chatlog", failure.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Welcome to City of Heroes", failure.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(@":\", failure.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Baseline_profile_passes_correctness_and_collects_performance()
    {
        var (result, _) = await ExecuteProfileAsync("baseline", new ReplayProfileOverrides { StressLines = 120 });
        Assert.Equal(
            ReplayProfileOutcome.Passed,
            result.Profile?.Outcome);
        Assert.True(
            result.Correctness.Passed,
            string.Join("|", result.Correctness.Failures.Select(failure => $"{failure.Code}:{failure.Message}")));
        Assert.NotNull(result.Performance);
    }

    [Fact]
    public async Task Dual_client_profile_runs_concurrently_without_contamination()
    {
        var (result, workspaces) = await ExecuteProfileAsync(
            "dual-client",
            new ReplayProfileOverrides
            {
                StressLines = 40,
                ContextARate = "500",
                ContextBRate = "500"
            });
        try
        {
            Assert.True(
                result.Correctness.Passed,
                string.Join("|", result.Correctness.Failures.Select(failure => $"{failure.Code}:{failure.Message}")));
            Assert.Equal(ReplayProfileOutcome.Passed, result.Profile?.Outcome);
            Assert.Equal(2, result.Correctness.ContextCount);
        }
        finally
        {
            foreach (var workspace in workspaces)
            {
                Directory.Delete(workspace.RootPath, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Expected_overload_profile_observes_gameplay_overload()
    {
        var time = new ManualReplayTimeProvider(new DateTimeOffset(2026, 8, 1, 10, 0, 0, TimeSpan.Zero));
        var (result, workspaces) = await ExecuteProfileAsync(
            "expected-overload",
            new ReplayProfileOverrides { StressLines = 120 },
            gameplaySessionOptions: null,
            time);
        try
        {
            Assert.Equal(ReplayProfileOutcome.ExpectedOverloadObserved, result.Profile?.Outcome);
            Assert.True(result.Parser.TotalLinesProcessed > 0);
            Assert.NotNull(result.Performance);
            Assert.True(result.Performance.GameplayWorkQueue.Overflowed);
            Assert.True(
                result.Performance.GameplayWorkQueue.RejectedCount > 0
                || result.Performance.GameplayAbandonedWorkCount > 0);
        }
        finally
        {
            foreach (var workspace in workspaces)
            {
                Directory.Delete(workspace.RootPath, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Expected_overload_missing_when_work_queue_capacity_prevents_overflow()
    {
        var time = new ManualReplayTimeProvider(new DateTimeOffset(2026, 8, 1, 10, 0, 0, TimeSpan.Zero));
        var (result, workspaces) = await ExecuteProfileAsync(
            "expected-overload",
            new ReplayProfileOverrides { StressLines = 40 },
            new GameplaySessionOptions
            {
                TimeProvider = time,
                WorkQueueCapacity = 512
            },
            time);
        try
        {
            Assert.Equal(ReplayProfileOutcome.ExpectedOverloadNotObserved, result.Profile?.Outcome);
            Assert.NotNull(result.Profile?.OverloadDiagnosticsSummary);
        }
        finally
        {
            foreach (var workspace in workspaces)
            {
                Directory.Delete(workspace.RootPath, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Expected_overload_followed_by_baseline_succeeds()
    {
        var time = new ManualReplayTimeProvider(new DateTimeOffset(2026, 8, 1, 10, 0, 0, TimeSpan.Zero));
        var (overloadResult, overloadWorkspaces) = await ExecuteProfileAsync(
            "expected-overload",
            new ReplayProfileOverrides { StressLines = 120 },
            gameplaySessionOptions: null,
            time);
        try
        {
            Assert.Equal(ReplayProfileOutcome.ExpectedOverloadObserved, overloadResult.Profile?.Outcome);

            var (baselineResult, baselineWorkspaces) = await ExecuteProfileAsync(
                "baseline",
                new ReplayProfileOverrides { StressLines = 40 },
                gameplaySessionOptions: null,
                time);
            try
            {
                Assert.Equal(ReplayProfileOutcome.Passed, baselineResult.Profile?.Outcome);
                Assert.True(baselineResult.Correctness.Passed);
                Assert.False(baselineResult.Performance?.GameplayWorkQueue.Overflowed);
            }
            finally
            {
                foreach (var workspace in baselineWorkspaces)
                {
                    Directory.Delete(workspace.RootPath, recursive: true);
                }
            }
        }
        finally
        {
            foreach (var workspace in overloadWorkspaces)
            {
                Directory.Delete(workspace.RootPath, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Dual_client_reports_consistent_aggregate_throughput()
    {
        var time = new ManualReplayTimeProvider(new DateTimeOffset(2026, 8, 1, 10, 0, 0, TimeSpan.Zero));
        var (result, workspaces) = await ExecuteProfileAsync(
            "dual-client",
            new ReplayProfileOverrides
            {
                StressLines = 40,
                ContextARate = "500",
                ContextBRate = "500"
            },
            gameplaySessionOptions: null,
            time);
        try
        {
            Assert.NotNull(result.Performance?.ConcurrentWriteThroughput);
            var throughput = result.Performance.ConcurrentWriteThroughput!;
            Assert.Equal(ReplayProfileOutcome.Passed, result.Profile?.Outcome);
            Assert.Equal(throughput.AggregateSourceCompleteLines, result.Performance.SourceCompleteLines);
            Assert.Equal(throughput.AggregateSourceBytes, result.Performance.SourceBytes);
            Assert.Equal(throughput.AggregateWriteDurationMilliseconds, result.Performance.WriteDurationMilliseconds);
            Assert.Equal(throughput.AggregateLinesPerSecond, result.Performance.AchievedLinesPerSecond);
            Assert.Equal(throughput.AggregateBytesPerSecond, result.Performance.AchievedBytesPerSecond);
            Assert.Equal(throughput.AggregateLinesPerSecond, result.Profile?.AchievedRateLinesPerSecond);
            Assert.Equal(2, throughput.Contexts.Count);
            Assert.True(throughput.AggregateLinesPerSecond > 1);
        }
        finally
        {
            foreach (var workspace in workspaces)
            {
                Directory.Delete(workspace.RootPath, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Rollover_profile_drains_both_segments()
    {
        var (result, _) = await ExecuteProfileAsync(
            "rollover",
            new ReplayProfileOverrides { StressLines = 40 });
        Assert.Equal(ReplayProfileOutcome.Passed, result.Profile?.Outcome);
        Assert.True(result.Parser.TotalLinesProcessed >= 80);
    }

    private static async Task<(ReplayPipelineResult Result, IReadOnlyList<ReplayWorkspace> Workspaces)> ExecuteProfileAsync(
        string profileName,
        ReplayProfileOverrides overrides,
        GameplaySessionOptions? gameplaySessionOptions = null,
        TimeProvider? timeProvider = null)
    {
        var root = Path.Combine(Path.GetTempPath(), $"replay-profile-pipeline-{Guid.NewGuid():n}");
        Directory.CreateDirectory(root);
        var time = timeProvider ?? new ManualReplayTimeProvider(new DateTimeOffset(2026, 8, 1, 10, 0, 0, TimeSpan.Zero));
        var resolution = ReplayProfileCatalog.Resolve(profileName, overrides, root);
        var ledger = new ReplayLedger();
        var timeline = new ReplayTimeline(time);
        var workspaces = new List<ReplayWorkspace>();
        var bindings = new List<ReplayContextBinding>();
        for (var index = 0; index < resolution.ContextPlans.Count; index++)
        {
            var workspace = await ReplayWorkspace.CreateAsync(
                keepWorkspace: true,
                accountFolderName: $"replay-profile-{index + 1:D2}");
            workspaces.Add(workspace);
            bindings.Add(new ReplayContextBinding(workspace, resolution.ContextPlans[index].Plan));
        }

        ReplayLatencyTracker? latencyTracker = resolution.Profile.LatencyMarkersEnabled
            ? new ReplayLatencyTracker(time)
            : null;
        await using var pipeline = new ReplayPipeline(time);
        var result = await pipeline.ExecuteAsync(
            new ReplayPipelineRequest
            {
                Contexts = bindings,
                ProfileResolution = resolution,
                LatencyTracker = latencyTracker,
                GameplaySessionOptions = gameplaySessionOptions
            },
            ledger,
            timeline);
        return (result, workspaces);
    }
}
