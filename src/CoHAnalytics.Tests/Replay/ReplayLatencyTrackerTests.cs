using CoHAnalytics.Replay;

namespace CoHAnalytics.Tests.Replay;

public sealed class ReplayLatencyTrackerTests
{
    [Fact]
    public void Marker_write_and_observe_correlates_parser_latency()
    {
        var time = new ManualReplayTimeProvider();
        var tracker = new ReplayLatencyTracker(time);
        tracker.RecordMarkerWritten(1);
        time.Advance(TimeSpan.FromMilliseconds(25));
        tracker.ObserveRawLine("2026-08-01 10:00:00 [System] Replay marker 000001");

        var report = tracker.BuildReport();
        Assert.Equal(1, report.ParserCount);
        Assert.Equal(25, report.ParserP50Milliseconds);
        Assert.Equal(25, report.ParserMaxMilliseconds);
    }

    [Fact]
    public void Bounded_retention_drops_oldest_samples()
    {
        var time = new ManualReplayTimeProvider();
        var tracker = new ReplayLatencyTracker(time, maxSamples: 2);
        for (var marker = 1; marker <= 3; marker++)
        {
            tracker.RecordMarkerWritten(marker);
            time.Advance(TimeSpan.FromMilliseconds(marker));
            tracker.ObserveRawLine($"2026-08-01 10:00:00 [System] Replay marker {marker:D6}");
        }

        var report = tracker.BuildReport();
        Assert.Equal(2, report.ParserCount);
        Assert.Equal(2.5, report.ParserP50Milliseconds);
    }

    [Fact]
    public void Percentiles_use_deterministic_algorithm()
    {
        var values = new[] { 1.0, 2.0, 3.0, 4.0, 5.0 };
        Assert.Equal(3.0, ReplayLatencyTracker.ComputePercentile(values, 0.50));
        Assert.Equal(4.8, ReplayLatencyTracker.ComputePercentile(values, 0.95));
        Assert.Equal(4.96, ReplayLatencyTracker.ComputePercentile(values, 0.99));
        Assert.Equal(5.0, ReplayLatencyTracker.ComputePercentile(values, 1.0));
    }

    [Fact]
    public void Insufficient_samples_report_unavailable()
    {
        var tracker = new ReplayLatencyTracker(new ManualReplayTimeProvider());
        var report = tracker.BuildReport();
        Assert.Equal(0, report.Count);
        Assert.Null(report.P50Milliseconds);
        Assert.Null(report.MaxMilliseconds);
    }

    [Fact]
    public void Missing_write_increments_missing_counter()
    {
        var tracker = new ReplayLatencyTracker(new ManualReplayTimeProvider());
        tracker.ObserveRawLine("2026-08-01 10:00:00 [System] Replay marker 000099");
        Assert.Equal(1, tracker.MissingWriteCount);
    }

    [Fact]
    public void Duplicate_marker_increments_duplicate_counter()
    {
        var tracker = new ReplayLatencyTracker(new ManualReplayTimeProvider());
        tracker.RecordMarkerWritten(5);
        tracker.RecordMarkerWritten(5);
        Assert.Equal(1, tracker.DuplicateMarkerCount);
    }
}
