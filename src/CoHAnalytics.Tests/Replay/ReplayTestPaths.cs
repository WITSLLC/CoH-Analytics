using CoHAnalytics.Replay;

namespace CoHAnalytics.Tests.Replay;

internal static class ReplayTestPaths
{
    public static string FixturesRoot =>
        Path.Combine(AppContext.BaseDirectory, "Replay", "Fixtures");

    public static string Fixture(string fileName) => Path.Combine(FixturesRoot, fileName);
}

/// <remarks>
/// Concurrently monitored replay contexts advance this clock from several writer loops at once,
/// so every mutation is serialized.
/// </remarks>
internal sealed class ManualReplayTimeProvider : TimeProvider
{
    private readonly object _sync = new();
    private readonly List<TimeSpan> _delays = [];
    private DateTimeOffset _utcNow;
    private long _timestamp;

    public ManualReplayTimeProvider(DateTimeOffset? start = null)
    {
        _utcNow = start ?? new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    }

    public override DateTimeOffset GetUtcNow()
    {
        lock (_sync)
        {
            return _utcNow;
        }
    }

    public override long GetTimestamp()
    {
        lock (_sync)
        {
            return _timestamp;
        }
    }

    public override long TimestampFrequency => 1_000;

    public IReadOnlyList<TimeSpan> Delays
    {
        get
        {
            lock (_sync)
            {
                return _delays.ToArray();
            }
        }
    }

    public void Advance(TimeSpan delta)
    {
        lock (_sync)
        {
            AdvanceLocked(delta);
        }
    }

    public long AdvanceAndGetTimestamp(TimeSpan delta)
    {
        lock (_sync)
        {
            AdvanceLocked(delta);
            return _timestamp;
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        lock (_sync)
        {
            _delays.Add(dueTime);
            AdvanceLocked(dueTime);
        }

        callback(state);
        return new NoopTimer();
    }

    private void AdvanceLocked(TimeSpan delta)
    {
        _utcNow += delta;
        _timestamp += (long)delta.TotalMilliseconds;
    }

    private sealed class NoopTimer : ITimer
    {
        public bool Change(TimeSpan dueTime, TimeSpan period) => true;

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

internal static class ReplayTestPlanFactory
{
    public static ReplayPlan CreatePlan(
        string sourcePath,
        DateOnly destinationDate,
        ReplayInputMode inputMode,
        ReplayChunkMode chunkMode = ReplayChunkMode.WholeLine,
        ReplayTimingMode timingMode = ReplayTimingMode.Maximum,
        int seed = 12345,
        int minChunkBytes = 1,
        int maxChunkBytes = 4096,
        int fixedChunkBytes = 4,
        double linesPerSecond = 10,
        double bytesPerSecond = 100,
        double speedMultiplier = 1,
        int burstSize = 2,
        int burstQuietMs = 100) =>
        ReplayPlan.Create(
            [new ReplaySourceSegment(sourcePath, destinationDate, 1)],
            inputMode,
            chunkMode,
            minChunkBytes,
            maxChunkBytes,
            fixedChunkBytes,
            seed,
            timingMode,
            linesPerSecond,
            bytesPerSecond,
            speedMultiplier,
            burstSize,
            TimeSpan.FromMilliseconds(burstQuietMs),
            keepWorkspace: true,
            jsonReportPath: null,
            textReportPath: null);

    public static async Task<(ReplayLedger Ledger, ReplayWorkspace Workspace)> ExecuteAsync(
        ReplayPlan plan,
        TimeProvider? timeProvider = null)
    {
        var ledger = new ReplayLedger();
        var timeline = new ReplayTimeline(timeProvider ?? TimeProvider.System);
        var workspace = await ReplayWorkspace.CreateAsync(plan.KeepWorkspace);
        var writer = new ReplayFileWriter(timeProvider ?? TimeProvider.System, ledger, timeline);
        await writer.ExecuteAsync(plan, workspace);
        return (ledger, workspace);
    }
}
