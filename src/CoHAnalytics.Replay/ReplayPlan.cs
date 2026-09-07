namespace CoHAnalytics.Replay;

public enum ReplayInputMode
{
    Exact,
    Bootstrap
}

public enum ReplayChunkMode
{
    WholeLine,
    FixedBytes,
    SeededVariable,
    OneByte
}

public enum ReplayTimingMode
{
    Maximum,
    FixedLines,
    FixedBytes,
    Timestamp,
    Burst
}

public sealed record ReplaySourceSegment(string SourcePath, DateOnly DestinationDate, int Ordinal);

public sealed class ReplayPlan
{
    public const string BootstrapLine = "2026-01-01 00:00:00 Welcome to City of Heroes, Example Hero!";
    public static readonly byte[] BootstrapBytes = System.Text.Encoding.UTF8.GetBytes(BootstrapLine + "\r\n");

    public IReadOnlyList<ReplaySourceSegment> Segments { get; }
    public ReplayInputMode InputMode { get; }
    public ReplayChunkMode ChunkMode { get; }
    public int MinChunkBytes { get; }
    public int MaxChunkBytes { get; }
    public int FixedChunkBytes { get; }
    public int Seed { get; }
    public ReplayTimingMode TimingMode { get; }
    public double LinesPerSecond { get; }
    public double BytesPerSecond { get; }
    public double TimestampSpeedMultiplier { get; }
    public int BurstSize { get; }
    public int? BurstCount { get; }
    public TimeSpan BurstQuietInterval { get; }
    public double? RequestedLinesPerSecond { get; }
    public bool KeepWorkspace { get; }
    public string? JsonReportPath { get; }
    public string? TextReportPath { get; }

    private ReplayPlan(
        IReadOnlyList<ReplaySourceSegment> segments,
        ReplayInputMode inputMode,
        ReplayChunkMode chunkMode,
        int minChunkBytes,
        int maxChunkBytes,
        int fixedChunkBytes,
        int seed,
        ReplayTimingMode timingMode,
        double linesPerSecond,
        double bytesPerSecond,
        double timestampSpeedMultiplier,
        int burstSize,
        TimeSpan burstQuietInterval,
        bool keepWorkspace,
        string? jsonReportPath,
        string? textReportPath,
        int? burstCount,
        double? requestedLinesPerSecond)
    {
        Segments = segments;
        InputMode = inputMode;
        ChunkMode = chunkMode;
        MinChunkBytes = minChunkBytes;
        MaxChunkBytes = maxChunkBytes;
        FixedChunkBytes = fixedChunkBytes;
        Seed = seed;
        TimingMode = timingMode;
        LinesPerSecond = linesPerSecond;
        BytesPerSecond = bytesPerSecond;
        TimestampSpeedMultiplier = timestampSpeedMultiplier;
        BurstSize = burstSize;
        BurstCount = burstCount;
        BurstQuietInterval = burstQuietInterval;
        RequestedLinesPerSecond = requestedLinesPerSecond;
        KeepWorkspace = keepWorkspace;
        JsonReportPath = jsonReportPath;
        TextReportPath = textReportPath;
    }

    public static ReplayPlan Create(
        IReadOnlyList<ReplaySourceSegment> segments,
        ReplayInputMode inputMode,
        ReplayChunkMode chunkMode,
        int minChunkBytes,
        int maxChunkBytes,
        int fixedChunkBytes,
        int seed,
        ReplayTimingMode timingMode,
        double linesPerSecond,
        double bytesPerSecond,
        double timestampSpeedMultiplier,
        int burstSize,
        TimeSpan burstQuietInterval,
        bool keepWorkspace,
        string? jsonReportPath,
        string? textReportPath,
        int? burstCount = null,
        double? requestedLinesPerSecond = null)
    {
        if (segments.Count == 0)
        {
            throw new ReplayConfigurationException("At least one --source segment is required.");
        }

        for (var index = 0; index < segments.Count; index++)
        {
            var segment = segments[index];
            if (string.IsNullOrWhiteSpace(segment.SourcePath))
            {
                throw new ReplayConfigurationException($"Source segment {index + 1} is missing a path.");
            }

            if (!File.Exists(segment.SourcePath))
            {
                throw new ReplayConfigurationException($"Source segment {index + 1} does not exist.");
            }

            if (segment.Ordinal != index + 1)
            {
                throw new ReplayConfigurationException("Source segment ordinals must be contiguous starting at 1.");
            }
        }

        if (minChunkBytes < 1)
        {
            throw new ReplayConfigurationException("--min-chunk-bytes must be at least 1.");
        }

        if (maxChunkBytes < minChunkBytes)
        {
            throw new ReplayConfigurationException("--max-chunk-bytes must be greater than or equal to --min-chunk-bytes.");
        }

        if (chunkMode == ReplayChunkMode.FixedBytes && fixedChunkBytes < 1)
        {
            throw new ReplayConfigurationException("--fixed-chunk-bytes must be at least 1 for fixed-byte chunk mode.");
        }

        switch (timingMode)
        {
            case ReplayTimingMode.FixedLines when linesPerSecond <= 0:
                throw new ReplayConfigurationException("--lines-per-second must be positive for fixed-lines timing.");
            case ReplayTimingMode.FixedBytes when bytesPerSecond <= 0:
                throw new ReplayConfigurationException("--bytes-per-second must be positive for fixed-bytes timing.");
            case ReplayTimingMode.Timestamp when timestampSpeedMultiplier <= 0:
                throw new ReplayConfigurationException("--speed-multiplier must be positive for timestamp timing.");
            case ReplayTimingMode.Burst when burstSize < 1:
                throw new ReplayConfigurationException("--burst-size must be at least 1 for burst timing.");
            case ReplayTimingMode.Burst when burstQuietInterval < TimeSpan.Zero:
                throw new ReplayConfigurationException("--burst-quiet-ms must be non-negative for burst timing.");
            case ReplayTimingMode.Burst when burstCount is < 1:
                throw new ReplayConfigurationException("--burst-count must be at least 1 for burst timing.");
        }

        if (requestedLinesPerSecond is <= 0)
        {
            throw new ReplayConfigurationException("--rate-lines-per-second must be positive.");
        }

        return new ReplayPlan(
            segments,
            inputMode,
            chunkMode,
            minChunkBytes,
            maxChunkBytes,
            fixedChunkBytes,
            seed,
            timingMode,
            linesPerSecond,
            bytesPerSecond,
            timestampSpeedMultiplier,
            burstSize,
            burstQuietInterval,
            keepWorkspace,
            jsonReportPath,
            textReportPath,
            burstCount,
            requestedLinesPerSecond);
    }
}

public sealed class ReplayConfigurationException : Exception
{
    public ReplayConfigurationException(string message) : base(message)
    {
    }
}

public enum ReplayExitCode
{
    Success = 0,
    InvalidConfiguration = 1,
    IoFailure = 2,
    TimestampValidationFailure = 3,
    AccountingFailure = 4,
    ReportFailure = 5,
    CorrectnessFailure = 6,
    Cancelled = 130
}
