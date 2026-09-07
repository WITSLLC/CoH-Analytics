using CoHAnalytics.Replay;

namespace CoHAnalytics.Tests.Replay;

public sealed class ReplayFixedRateAndBurstTests
{
    [Fact]
    public async Task Burst_profile_records_exact_burst_count()
    {
        var root = Path.Combine(Path.GetTempPath(), $"replay-burst-{Guid.NewGuid():n}");
        Directory.CreateDirectory(root);
        var resolution = ReplayProfileCatalog.Resolve(
            "burst",
            new ReplayProfileOverrides
            {
                BurstLines = 10,
                BurstCount = 3,
                BurstQuietMs = 0
            },
            root);
        var time = new ManualReplayTimeProvider();
        var ledger = new ReplayLedger();
        var timeline = new ReplayTimeline(time);
        var workspace = await ReplayWorkspace.CreateAsync(keepWorkspace: true);
        var writer = new ReplayFileWriter(time, ledger, timeline);
        await writer.ExecuteAsync(resolution.ContextPlans[0].Plan, workspace);
        Assert.Equal(3, ledger.BurstCount);
        await workspace.DisposeAsync();
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public async Task Fixed_rate_writer_reports_requested_and_achieved_rate()
    {
        var root = Path.Combine(Path.GetTempPath(), $"replay-fixed-rate-{Guid.NewGuid():n}");
        Directory.CreateDirectory(root);
        var resolution = ReplayProfileCatalog.Resolve(
            "fixed-rate",
            new ReplayProfileOverrides
            {
                RateLinesPerSecond = 500,
                StressLines = 5
            },
            root);
        var time = new ManualReplayTimeProvider();
        var ledger = new ReplayLedger();
        var timeline = new ReplayTimeline(time);
        var workspace = await ReplayWorkspace.CreateAsync(keepWorkspace: true);
        var writer = new ReplayFileWriter(time, ledger, timeline);
        await writer.ExecuteAsync(resolution.ContextPlans[0].Plan, workspace);
        Assert.Equal(500, ledger.Pacing.RequestedLinesPerSecond);
        Assert.NotNull(ledger.Pacing.AchievedLinesPerSecond);
        await workspace.DisposeAsync();
        Directory.Delete(root, recursive: true);
    }
}
