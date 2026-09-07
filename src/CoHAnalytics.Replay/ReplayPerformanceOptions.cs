namespace CoHAnalytics.Replay;

public sealed class ReplayPerformanceOptions
{
    public static readonly int[] DefaultQueueThresholdPercents = [25, 50, 75, 90, 100];

    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;

    public TimeSpan SampleInterval { get; init; } = TimeSpan.FromMilliseconds(250);

    public int MaxRetainedSamples { get; init; } = 64;

    public IReadOnlyList<int> QueueThresholdPercents { get; init; } = DefaultQueueThresholdPercents;

    public bool EnableSampling { get; init; } = true;

    public bool EnableResourceSampling { get; init; } = true;

    public static ReplayPerformanceOptions Disabled { get; } = new()
    {
        EnableSampling = false,
        EnableResourceSampling = false
    };
}
