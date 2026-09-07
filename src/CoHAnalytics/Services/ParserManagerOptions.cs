namespace CoHAnalytics.Services;

/// <summary>Code-defined parser I/O limits and timings. These are not user settings.</summary>
public sealed class ParserManagerOptions
{
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;

    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMilliseconds(100);

    public int ReadBufferSize { get; init; } = 16 * 1024;

    public int MaximumLineBytes { get; init; } = 256 * 1024;

    public int MaximumRecentSegments { get; init; } = 16;

    public int MonitoringSnapshotQueueCapacity { get; init; } = 256;

    public int EventQueueCapacity { get; init; } = 1024;

    public int EventBatchSize { get; init; } = 64;

    public int MaximumRecentDecisions { get; init; } = 100;

    internal void Validate()
    {
        if (PollInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(PollInterval));
        }

        if (ReadBufferSize < 4)
        {
            throw new ArgumentOutOfRangeException(nameof(ReadBufferSize));
        }

        if (MaximumLineBytes < 4)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumLineBytes));
        }

        if (MaximumRecentSegments <= 0
            || MonitoringSnapshotQueueCapacity <= 0
            || EventQueueCapacity <= 0
            || EventBatchSize <= 0
            || MaximumRecentDecisions <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ParserManagerOptions));
        }
    }
}
