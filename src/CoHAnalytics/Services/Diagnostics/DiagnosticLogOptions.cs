namespace CoHAnalytics.Services.Diagnostics;

public sealed class DiagnosticLogOptions
{
    public const int DefaultQueueCapacity = 2048;
    public const int DefaultMaximumBatchSize = 50;
    public const long DefaultMaximumActiveFileBytes = 5L * 1024 * 1024;
    public const int DefaultMaximumArchiveCount = 4;

    public int QueueCapacity { get; init; } = DefaultQueueCapacity;

    public int MaximumBatchSize { get; init; } = DefaultMaximumBatchSize;

    public long MaximumActiveFileBytes { get; init; } = DefaultMaximumActiveFileBytes;

    public int MaximumArchiveCount { get; init; } = DefaultMaximumArchiveCount;

    public TimeSpan MaximumArchiveAge { get; init; } = TimeSpan.FromDays(14);

    public TimeSpan ShutdownTimeout { get; init; } = TimeSpan.FromSeconds(5);

    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;

    internal Action<DiagnosticEvent>? BeforeSerialize { get; init; }

    internal Action? BeforeWrite { get; init; }

    internal Action? BeforeRotation { get; init; }

    internal void Validate()
    {
        if (QueueCapacity <= 0
            || MaximumBatchSize <= 0
            || MaximumActiveFileBytes <= 0
            || MaximumArchiveCount < 0
            || MaximumArchiveAge <= TimeSpan.Zero
            || ShutdownTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(DiagnosticLogOptions));
        }

        ArgumentNullException.ThrowIfNull(TimeProvider);
    }
}
