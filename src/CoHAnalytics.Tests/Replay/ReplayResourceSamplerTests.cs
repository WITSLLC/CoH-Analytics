using CoHAnalytics.Replay;

namespace CoHAnalytics.Tests.Replay;

public sealed class ReplayResourceSamplerTests
{
    [Fact]
    public void Cpu_calculation_uses_injected_elapsed_time()
    {
        var sampler = new FakeReplayResourceSampler();
        sampler.Enqueue(new ReplayResourceSample(
            CpuUtilizationPercent: null,
            WorkingSetBytes: 1000,
            PrivateMemoryBytes: 900,
            ManagedHeapBytes: 500,
            TotalAllocatedBytes: 2000,
            Gen0CollectionCount: 0,
            Gen1CollectionCount: 0,
            Gen2CollectionCount: 0,
            ThreadCount: 2,
            HandleCount: 10,
            HandleCountSupported: true));

        var first = sampler.Capture(TimeSpan.FromMilliseconds(100));
        Assert.Null(first.CpuUtilizationPercent);

        sampler.Enqueue(new ReplayResourceSample(
            25,
            1100,
            950,
            520,
            2100,
            1,
            0,
            0,
            3,
            11,
            true));
        var second = sampler.Capture(TimeSpan.FromMilliseconds(100));
        Assert.NotNull(second.CpuUtilizationPercent);
    }

    [Fact]
    public void Unsupported_metrics_report_honestly_without_throwing()
    {
        IReplayResourceMetricSource sampler = new ReplayResourceSampler();
        var sample = sampler.Capture(TimeSpan.Zero);
        Assert.Null(sample.CpuUtilizationPercent);
    }

    [Fact]
    public void Collector_aggregates_resource_samples_from_fake_sampler()
    {
        var time = new ManualReplayTimeProvider();
        var timeline = new ReplayTimeline(time);
        var fakeSampler = new FakeReplayResourceSampler();
        fakeSampler.Enqueue(new ReplayResourceSample(10, 1000, 900, 400, 1000, 0, 0, 0, 2, 50, true));
        fakeSampler.Enqueue(new ReplayResourceSample(20, 2000, 1500, 800, 2000, 1, 0, 0, 3, 55, true));

        var collector = new ReplayPerformanceCollector(
            new ReplayPerformanceOptions
            {
                TimeProvider = time,
                SampleInterval = TimeSpan.FromMilliseconds(1),
                MaxRetainedSamples = 8,
                EnableSampling = true,
                EnableResourceSampling = true
            },
            fakeSampler);
        collector.Start(timeline);
        collector.BeginWrite();
        collector.EndWrite(CreateLedger());
        collector.BeginDrain();

        time.Advance(TimeSpan.FromMilliseconds(2));
        collector.ObserveDuringDrain(
            () => ReplayPerformanceCollectorTestsHelpers.CreateDiagnostics(),
            timeline,
            CancellationToken.None);
        time.Advance(TimeSpan.FromMilliseconds(2));
        collector.ObserveDuringDrain(
            () => ReplayPerformanceCollectorTestsHelpers.CreateDiagnostics(),
            timeline,
            CancellationToken.None);

        collector.EndDrain(
            new ReplayDrainResult(ReplayDrainOutcome.Completed, ReplayPerformanceCollectorTestsHelpers.CreateDrainDiagnostics()),
            timeline);
        var report = ReplayPerformanceCollectorTestsHelpers.CompleteCollector(collector, timeline, time);

        Assert.NotNull(report.Resources);
        Assert.Equal(2000, report.Resources.WorkingSetBytesMax);
        Assert.Equal(800, report.Resources.ManagedHeapBytesMax);
        Assert.True(report.Resources.HandleCountSupported);
    }

    private static ReplayLedger CreateLedger()
    {
        var ledger = new ReplayLedger();
        ledger.RecordSourceAnalysis(1, 100, 4, false, 0);
        return ledger;
    }
}

internal static class ReplayPerformanceCollectorTestsHelpers
{
    public static ReplayDrainDiagnostics CreateDrainDiagnostics() =>
        new(
            MonitoringReady: true,
            ProcessedLines: 4,
            ExpectedLines: 4,
            ParserQueuedEvents: 0,
            ParserWorkersReady: true,
            RawObserved: 4,
            ClassifiedObserved: 4,
            CommittedObserved: 4,
            GameplayQuiescent: true,
            PendingCommittedEvents: 0,
            FailedContextCount: 0,
            ParserOverflowed: false,
            GameplayOverloaded: false,
            UnsatisfiedStages: []);

    public static (CoHAnalytics.Services.ParserManagerDiagnostics Parser, CoHAnalytics.Services.GameplaySessionDiagnostics Gameplay)
        CreateDiagnostics(int eventDepth = 0) =>
        (
            CoHAnalytics.Tests.Services.GameplaySessionTestInfrastructure.IdleParserDiagnostics(),
            CoHAnalytics.Tests.Services.GameplaySessionTestInfrastructure.IdleGameplayDiagnostics());

    public static ReplayPerformanceObservationReport CompleteCollector(
        ReplayPerformanceCollector collector,
        ReplayTimeline timeline,
        ManualReplayTimeProvider time,
        bool correctnessPassed = true) =>
        collector.Complete(
            new ReplayCorrectnessReport
            {
                Passed = correctnessPassed,
                RawEventsObserved = 4,
                ClassifiedEventsObserved = 4,
                CommittedGameplayEventsObserved = 4,
                ContextCount = 1,
                WelcomeBoundariesObserved = 1
            },
            new ReplayDrainResult(ReplayDrainOutcome.Completed, CreateDrainDiagnostics()),
            CreateLedger(),
            CreateDiagnostics().Parser,
            CreateDiagnostics().Gameplay,
            CoHAnalytics.Models.ParserManagerSnapshot.Empty,
            timeline);

    private static ReplayLedger CreateLedger()
    {
        var ledger = new ReplayLedger();
        ledger.RecordSourceAnalysis(1, 100, 4, false, 0);
        return ledger;
    }
}
