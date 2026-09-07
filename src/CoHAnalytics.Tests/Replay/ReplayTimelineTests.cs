using CoHAnalytics.Replay;

namespace CoHAnalytics.Tests.Replay;

public sealed class ReplayTimelineTests
{
    [Fact]
    public void Sequence_numbers_are_monotonic()
    {
        var timeline = new ReplayTimeline(new ManualReplayTimeProvider());
        timeline.Record(ReplayTimelineCategory.Run, "run.started", ReplayTimelineRetentionClass.Anchor);
        timeline.Record(ReplayTimelineCategory.Workspace, "workspace.created", ReplayTimelineRetentionClass.Milestone);
        timeline.Record(ReplayTimelineCategory.Replay, "replay.segment.started", ReplayTimelineRetentionClass.Milestone, 1);

        Assert.Equal(new long[] { 1, 2, 3 }, timeline.Events.Select(entry => entry.Sequence).ToArray());
    }

    [Fact]
    public void Elapsed_times_are_monotonic()
    {
        var time = new ManualReplayTimeProvider();
        var timeline = new ReplayTimeline(time);
        timeline.Record(ReplayTimelineCategory.Run, "run.started", ReplayTimelineRetentionClass.Anchor);
        time.Advance(TimeSpan.FromMilliseconds(25));
        timeline.Record(ReplayTimelineCategory.Workspace, "workspace.created", ReplayTimelineRetentionClass.Milestone);
        time.Advance(TimeSpan.FromMilliseconds(40));
        timeline.Record(ReplayTimelineCategory.Replay, "replay.segment.started", ReplayTimelineRetentionClass.Milestone, 1);

        var elapsed = timeline.Events.Select(entry => entry.ElapsedMilliseconds).ToArray();
        Assert.True(elapsed[1] >= elapsed[0]);
        Assert.True(elapsed[2] >= elapsed[1]);
    }

    [Fact]
    public void Capacity_enforces_sample_first_then_milestone_eviction()
    {
        var timeline = new ReplayTimeline(new ManualReplayTimeProvider());
        timeline.Record(ReplayTimelineCategory.Run, "run.started", ReplayTimelineRetentionClass.Anchor);

        for (var index = 0; index < ReplayTimeline.Capacity + 50; index++)
        {
            timeline.Record(
                ReplayTimelineCategory.Replay,
                "replay.burst.started",
                ReplayTimelineRetentionClass.Sample,
                1);
        }

        timeline.Record(ReplayTimelineCategory.Run, "run.completed", ReplayTimelineRetentionClass.Anchor);

        Assert.True(timeline.Truncated);
        Assert.True(timeline.DroppedEventCount > 0);
        Assert.Contains(timeline.Events, entry => entry.Code == "run.started");
        Assert.Contains(timeline.Events, entry => entry.Code == "run.completed");
        Assert.Equal(1, timeline.Events.Count(entry => entry.Code == "timeline.truncated"));
        Assert.True(timeline.Events.Count <= ReplayTimeline.Capacity + 1);
        Assert.DoesNotContain(timeline.Events, entry => entry.Value is not null && entry.Value.ToString()!.Contains(':'));
    }

    [Fact]
    public void Capacity_evicts_oldest_non_anchor_milestone_when_no_samples_exist()
    {
        var timeline = new ReplayTimeline(new ManualReplayTimeProvider());
        timeline.Record(ReplayTimelineCategory.Run, "run.started", ReplayTimelineRetentionClass.Anchor);

        for (var index = 0; index < ReplayTimeline.Capacity + 25; index++)
        {
            timeline.Record(
                ReplayTimelineCategory.Replay,
                $"replay.milestone.{index:D4}",
                ReplayTimelineRetentionClass.Milestone,
                1);
        }

        timeline.Record(ReplayTimelineCategory.Run, "run.completed", ReplayTimelineRetentionClass.Anchor);

        Assert.Contains(timeline.Events, entry => entry.Code == "run.started");
        Assert.Contains(timeline.Events, entry => entry.Code == "run.completed");
        Assert.DoesNotContain(timeline.Events, entry => entry.Code == "replay.milestone.0000");
        Assert.DoesNotContain(timeline.Events, entry => entry.RetentionClass == ReplayTimelineRetentionClass.Sample);
        Assert.True(timeline.Truncated);
        Assert.Equal(1, timeline.Events.Count(entry => entry.Code == "timeline.truncated"));
        Assert.True(timeline.DroppedEventCount >= 25);
        Assert.True(timeline.Events.Count <= ReplayTimeline.Capacity);
    }

    [Fact]
    public void Events_remain_payload_and_path_free()
    {
        var timeline = new ReplayTimeline(new ManualReplayTimeProvider());
        timeline.Record(ReplayTimelineCategory.Run, "run.started", ReplayTimelineRetentionClass.Anchor);

        foreach (var entry in timeline.Events)
        {
            Assert.DoesNotContain(":\\", entry.Code);
            Assert.DoesNotContain("/", entry.Code);
        }
    }
}
