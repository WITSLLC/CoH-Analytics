using CoHAnalytics.Models;
using CoHAnalytics.Replay;
using CoHAnalytics.Tests.Services;

namespace CoHAnalytics.Tests.Replay;

public sealed class ReplayProfileTests
{
    [Theory]
    [InlineData("baseline")]
    [InlineData("fixed-rate")]
    [InlineData("burst")]
    [InlineData("maximum")]
    [InlineData("dual-client")]
    [InlineData("rollover")]
    [InlineData("expected-overload")]
    public void Every_named_profile_resolves(string profileName)
    {
        var root = CreateWorkspaceRoot();
        try
        {
            var resolution = ReplayProfileCatalog.Resolve(profileName, new ReplayProfileOverrides(), root);
            Assert.Equal(profileName, resolution.Profile.Name);
            Assert.NotEmpty(resolution.ContextPlans);
            Assert.NotEmpty(resolution.EffectiveOptions);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Fixed_rate_profile_uses_default_rate_when_not_overridden()
    {
        var root = CreateWorkspaceRoot();
        try
        {
            var resolution = ReplayProfileCatalog.Resolve("fixed-rate", new ReplayProfileOverrides(), root);
            Assert.Equal(1000, resolution.RequestedRateLinesPerSecond);
            Assert.Equal(ReplayTimingMode.FixedLines, resolution.ContextPlans[0].Plan.TimingMode);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Fixed_rate_profile_accepts_explicit_override()
    {
        var root = CreateWorkspaceRoot();
        try
        {
            var resolution = ReplayProfileCatalog.Resolve(
                "fixed-rate",
                new ReplayProfileOverrides { RateLinesPerSecond = 777 },
                root);
            Assert.Equal(777, resolution.RequestedRateLinesPerSecond);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Incompatible_burst_override_is_rejected()
    {
        var root = CreateWorkspaceRoot();
        var exception = Assert.Throws<ReplayConfigurationException>(() =>
            ReplayProfileCatalog.Resolve(
                "burst",
                new ReplayProfileOverrides { BurstLines = 0 },
                root));
        Assert.Contains("burst-lines", exception.Message, StringComparison.OrdinalIgnoreCase);
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void Profile_report_round_trip_fields_are_populated()
    {
        var root = CreateWorkspaceRoot();
        try
        {
            var resolution = ReplayProfileCatalog.Resolve("baseline", new ReplayProfileOverrides(), root);
            var report = new ReplayProfileObservationReport
            {
                ProfileName = resolution.Profile.Name,
                EffectiveOptions = resolution.EffectiveOptions,
                ContextCount = resolution.Profile.ContextCount,
                RequestedRateLinesPerSecond = resolution.RequestedRateLinesPerSecond,
                AchievedRateLinesPerSecond = 42,
                StressSeed = resolution.Profile.SourceWorkload.Seed,
                GeneratedLineCount = resolution.Profile.SourceWorkload.LineCount,
                ExpectedOverloadTarget = resolution.ExpectedOverloadTarget,
                Outcome = ReplayProfileOutcome.Passed
            };

            Assert.Equal("baseline", report.ProfileName);
            Assert.Equal(ReplayProfileOutcome.Passed, report.Outcome);
            Assert.NotNull(report.StressSeed);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Expected_overload_profile_uses_admission_gate_settings()
    {
        var root = CreateWorkspaceRoot();
        try
        {
            var resolution = ReplayProfileCatalog.Resolve("expected-overload", new ReplayProfileOverrides(), root);
            Assert.Equal(ReplayChunkMode.WholeLine, resolution.Profile.ChunkMode);
            Assert.Equal(120, resolution.Profile.SourceWorkload.LineCount);
            Assert.Equal(1, resolution.Profile.GameplayWorkQueueCapacity);
            Assert.Equal(
                ReplayExpectedOverloadAdmission.DefaultAdmissionLineCount,
                resolution.Profile.ExpectedOverloadAdmissionLineCount);
            Assert.Null(resolution.Profile.GameplayMaxPreStartBufferCapacity);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Expected_overload_not_observed_when_work_queue_does_not_overflow()
    {
        var root = CreateWorkspaceRoot();
        try
        {
            var resolution = ReplayProfileCatalog.Resolve("expected-overload", new ReplayProfileOverrides(), root);
            var parserSnapshot = ParserManagerSnapshot.Create(
                [
                    new ParserWorkerSnapshot
                    {
                        WorkerId = ParserWorkerId.CreateNew(),
                        ContextId = MonitoringContextId.CreateNew(),
                        State = ParserWorkerState.Reading,
                        AppliedSourceBindingGeneration = 1,
                        LastAppliedTransitionKind = MonitoringSourceTransitionKind.SourceAssigned,
                        RecentSegments = [],
                        TotalBytesRead = 100,
                        TotalLinesEmitted = 10,
                        LastEventSequence = 10
                    }
                ],
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                1);

            var outcome = ReplayProfileCatalog.DetermineOutcome(
                resolution.Profile,
                new ReplayCorrectnessReport { Passed = false },
                new ReplayDrainResult(
                    ReplayDrainOutcome.Completed,
                    new ReplayDrainDiagnostics(
                        MonitoringReady: true,
                        ProcessedLines: 10,
                        ExpectedLines: 10,
                        ParserQueuedEvents: 0,
                        ParserWorkersReady: true,
                        RawObserved: 10,
                        ClassifiedObserved: 10,
                        CommittedObserved: 10,
                        GameplayQuiescent: true,
                        PendingCommittedEvents: 0,
                        FailedContextCount: 0,
                        ParserOverflowed: false,
                        GameplayOverloaded: false,
                        UnsatisfiedStages: [])),
                GameplaySessionTestInfrastructure.IdleGameplayDiagnostics(workQueueOverflowed: false),
                GameplaySessionTestInfrastructure.IdleParserDiagnostics(),
                parserSnapshot,
                harnessFailed: false);

            Assert.Equal(ReplayProfileOutcome.ExpectedOverloadNotObserved, outcome);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateWorkspaceRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"replay-profile-test-{Guid.NewGuid():n}");
        Directory.CreateDirectory(root);
        return root;
    }
}
