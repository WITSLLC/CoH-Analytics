using CoHAnalytics.Services;

namespace CoHAnalytics.Replay;

public sealed class ReplayLoadCoordinator
{
    private readonly TimeProvider _timeProvider;
    private readonly ReplayTimeline _timeline;

    public ReplayLoadCoordinator(TimeProvider timeProvider, ReplayTimeline timeline)
    {
        _timeProvider = timeProvider;
        _timeline = timeline;
    }

    public async Task<ReplayLoadCoordinatorResult> ExecuteAsync(
        IReadOnlyList<ReplayLoadContextJob> jobs,
        ReplayPerformanceCollector? performanceCollector,
        CancellationToken cancellationToken)
    {
        if (jobs.Count == 0)
        {
            throw new ReplayConfigurationException("At least one load context is required.");
        }

        _timeline.Record(
            ReplayTimelineCategory.Analytics,
            "load.coordination.ready",
            ReplayTimelineRetentionClass.Milestone,
            jobs.Count);

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writeStartTimestamp = 0L;
        var writeEndTimestamp = 0L;

        performanceCollector?.BeginWrite();
        writeStartTimestamp = _timeProvider.GetTimestamp();

        _timeline.Record(
            ReplayTimelineCategory.Analytics,
            "load.coordination.started",
            ReplayTimelineRetentionClass.Milestone,
            jobs.Count);

        var tasks = jobs
            .Select(job => RunContextAsync(job, startGate.Task, linkedCts, performanceCollector))
            .ToArray();

        startGate.SetResult();

        ReplayLoadContextResult[] results;
        try
        {
            results = await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            linkedCts.Cancel();
            try
            {
                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
            catch
            {
            }

            throw;
        }

        writeEndTimestamp = _timeProvider.GetTimestamp();
        var timingInputs = results
            .Select(
                result => new ReplayContextWriteTimingInput
                {
                    Label = result.Label,
                    SourceCompleteLines = result.Ledger.OriginalSourceCompleteLines,
                    SourceBytes = result.Ledger.OriginalSourceBytes,
                    WriteStartTimestamp = result.WriteStartTimestamp,
                    WriteEndTimestamp = result.WriteEndTimestamp
                })
            .ToArray();
        var concurrentThroughput = ReplayConcurrentWriteThroughput.Calculate(timingInputs, _timeProvider);
        var aggregatedLedger = ReplayAggregatedLedger.Combine(
            results.Select(result => result.Ledger).ToArray(),
            concurrentThroughput);
        performanceCollector?.EndWrite(aggregatedLedger.PrimaryLedger, aggregatedLedger, concurrentThroughput);

        return new ReplayLoadCoordinatorResult
        {
            ContextResults = results,
            AggregatedLedger = aggregatedLedger,
            ConcurrentWriteThroughput = concurrentThroughput,
            WriteDuration = concurrentThroughput?.AggregateWriteDurationMilliseconds is long durationMilliseconds
                ? TimeSpan.FromMilliseconds(durationMilliseconds)
                : _timeProvider.GetElapsedTime(writeStartTimestamp, writeEndTimestamp)
        };
    }

    private async Task<ReplayLoadContextResult> RunContextAsync(
        ReplayLoadContextJob job,
        Task startSignal,
        CancellationTokenSource linkedCts,
        ReplayPerformanceCollector? performanceCollector)
    {
        try
        {
            await startSignal.ConfigureAwait(false);
            linkedCts.Token.ThrowIfCancellationRequested();

            _timeline.Record(
                ReplayTimelineCategory.Analytics,
                "load.context.started",
                ReplayTimelineRetentionClass.Milestone,
                value: job.ContextIndex);

            var writeStartTimestamp = _timeProvider.GetTimestamp();
            var writer = new ReplayFileWriter(_timeProvider, job.Ledger, _timeline);
            await writer.ExecuteAsync(
                    job.Plan,
                    job.Workspace,
                    linkedCts.Token,
                    betweenSegmentsAsync: job.BetweenSegmentsAsync,
                    latencyTracker: job.LatencyTracker)
                .ConfigureAwait(false);
            var writeEndTimestamp = _timeProvider.GetTimestamp();
            if (writeEndTimestamp <= writeStartTimestamp
                && job.Ledger.OriginalSourceCompleteLines > 0)
            {
                writeEndTimestamp = writeStartTimestamp + job.Ledger.OriginalSourceCompleteLines;
            }

            _timeline.Record(
                ReplayTimelineCategory.Analytics,
                "load.context.completed",
                ReplayTimelineRetentionClass.Milestone,
                value: job.ContextIndex);

            return new ReplayLoadContextResult
            {
                ContextIndex = job.ContextIndex,
                Label = job.Label,
                Ledger = job.Ledger,
                Pacing = job.Ledger.Pacing,
                WriteStartTimestamp = writeStartTimestamp,
                WriteEndTimestamp = writeEndTimestamp
            };
        }
        catch (Exception exception)
        {
            linkedCts.Cancel();
            if (exception is OperationCanceledException && linkedCts.IsCancellationRequested)
            {
                throw;
            }

            throw;
        }
    }
}

public sealed class ReplayLoadContextJob
{
    public required int ContextIndex { get; init; }

    public required string Label { get; init; }

    public required ReplayWorkspace Workspace { get; init; }

    public required ReplayPlan Plan { get; init; }

    public required ReplayLedger Ledger { get; init; }

    public Func<int, Task>? BetweenSegmentsAsync { get; init; }

    public ReplayLatencyTracker? LatencyTracker { get; init; }
}

public sealed class ReplayLoadContextResult
{
    public required int ContextIndex { get; init; }

    public required string Label { get; init; }

    public required ReplayLedger Ledger { get; init; }

    public ReplayPacingSummary? Pacing { get; init; }

    public required long WriteStartTimestamp { get; init; }

    public required long WriteEndTimestamp { get; init; }
}

public sealed class ReplayLoadCoordinatorResult
{
    public required IReadOnlyList<ReplayLoadContextResult> ContextResults { get; init; }

    public required ReplayAggregatedLedger AggregatedLedger { get; init; }

    public ReplayConcurrentWriteThroughput? ConcurrentWriteThroughput { get; init; }

    public required TimeSpan WriteDuration { get; init; }
}

public sealed class ReplayAggregatedLedger
{
    public required ReplayLedger PrimaryLedger { get; init; }

    public required IReadOnlyList<ReplayLedger> ContextLedgers { get; init; }

    public long OriginalSourceCompleteLines { get; init; }

    public long OriginalSourceBytes { get; init; }

    public long CombinedDestinationBytes { get; init; }

    public long CombinedDestinationCompleteLines { get; init; }

    public long BootstrapBytes { get; init; }

    public long BootstrapCompleteLines { get; init; }

    public bool BeginsMidSession { get; init; }

    public ReplayConcurrentWriteThroughput? ConcurrentWriteThroughput { get; init; }

    public static ReplayAggregatedLedger Combine(
        IReadOnlyList<ReplayLedger> ledgers,
        ReplayConcurrentWriteThroughput? concurrentWriteThroughput = null)
    {
        if (ledgers.Count == 0)
        {
            throw new ArgumentException("At least one ledger is required.", nameof(ledgers));
        }

        return new ReplayAggregatedLedger
        {
            PrimaryLedger = ledgers[0],
            ContextLedgers = ledgers,
            OriginalSourceCompleteLines = ledgers.Sum(ledger => ledger.OriginalSourceCompleteLines),
            OriginalSourceBytes = ledgers.Sum(ledger => ledger.OriginalSourceBytes),
            CombinedDestinationBytes = ledgers.Sum(ledger => ledger.CombinedDestinationBytes),
            CombinedDestinationCompleteLines = ledgers.Sum(ledger => ledger.CombinedDestinationCompleteLines),
            BootstrapBytes = ledgers.Sum(ledger => ledger.BootstrapBytes),
            BootstrapCompleteLines = ledgers.Sum(ledger => ledger.BootstrapCompleteLines),
            BeginsMidSession = ledgers.Any(ledger => ledger.BeginsMidSession),
            ConcurrentWriteThroughput = concurrentWriteThroughput
        };
    }
}

public static class ReplayExpectedOverloadAdmission
{
    public const int DefaultAdmissionLineCount = 1;

    public static async Task ExecuteAsync(
        ReplayFileWriter writer,
        ReplayPlan plan,
        ReplayWorkspace workspace,
        ReplayTimeline timeline,
        int admissionLineCount,
        CancellationToken cancellationToken,
        ReplayLatencyTracker? latencyTracker = null)
    {
        if (plan.Segments.Count != 1)
        {
            throw new ReplayConfigurationException("Expected-overload admission requires a single source segment.");
        }

        var segment = plan.Segments[0];
        var sourceBytes = await File.ReadAllBytesAsync(segment.SourcePath, cancellationToken).ConfigureAwait(false);
        var (prefixBytes, suffixBytes) = SplitAfterCompleteLines(sourceBytes, admissionLineCount);
        var prefixPath = Path.Combine(workspace.RootPath, "expected-overload-prefix.log");
        var suffixPath = Path.Combine(workspace.RootPath, "expected-overload-suffix.log");
        await File.WriteAllBytesAsync(prefixPath, prefixBytes, cancellationToken).ConfigureAwait(false);
        await File.WriteAllBytesAsync(suffixPath, suffixBytes, cancellationToken).ConfigureAwait(false);

        timeline.Record(
            ReplayTimelineCategory.Analytics,
            "overload.expected",
            ReplayTimelineRetentionClass.Milestone);

        var prefixPlan = CreateSegmentPlan(
            plan,
            new ReplaySourceSegment(prefixPath, segment.DestinationDate, 1),
            timingModeOverride: ReplayTimingMode.Maximum);
        await writer.ExecuteAsync(
                prefixPlan,
                workspace,
                cancellationToken,
                latencyTracker: latencyTracker,
                validateOnCompletion: false)
            .ConfigureAwait(false);

        var suffixPlan = CreateSegmentPlan(
            plan,
            new ReplaySourceSegment(suffixPath, segment.DestinationDate, 1),
            ReplayInputMode.Exact,
            chunkModeOverride: ReplayChunkMode.WholeLine,
            timingModeOverride: ReplayTimingMode.Maximum);
        await writer.ExecuteAsync(
                suffixPlan,
                workspace,
                cancellationToken,
                latencyTracker: latencyTracker,
                validateOnCompletion: false)
            .ConfigureAwait(false);
    }

    public static void ReconcileSegmentAccounting(ReplayLedger ledger, int ordinal)
    {
        var segment = ledger.GetOrCreateSegment(ordinal);
        segment.SourceBytes = ledger.OriginalSourceBytes;
        segment.SourceCompleteLines = ledger.OriginalSourceCompleteLines;
    }

    internal static (byte[] Prefix, byte[] Suffix) SplitAfterCompleteLines(byte[] sourceBytes, int completeLineCount)
    {
        if (completeLineCount < 1)
        {
            throw new ReplayConfigurationException("Admission line count must be at least 1.");
        }

        var lineEnds = 0;
        var splitIndex = 0;
        for (var index = 0; index < sourceBytes.Length; index++)
        {
            if (sourceBytes[index] != (byte)'\n')
            {
                continue;
            }

            lineEnds++;
            splitIndex = index + 1;
            if (lineEnds >= completeLineCount)
            {
                break;
            }
        }

        if (lineEnds < completeLineCount)
        {
            throw new ReplayConfigurationException(
                $"Admission line count {completeLineCount} exceeds source complete lines {lineEnds}.");
        }

        return (sourceBytes[..splitIndex], sourceBytes[splitIndex..]);
    }

    private static ReplayPlan CreateSegmentPlan(
        ReplayPlan template,
        ReplaySourceSegment segment,
        ReplayInputMode? inputModeOverride = null,
        ReplayChunkMode? chunkModeOverride = null,
        ReplayTimingMode? timingModeOverride = null,
        double? linesPerSecondOverride = null) =>
        ReplayPlan.Create(
            [segment],
            inputModeOverride ?? template.InputMode,
            chunkModeOverride ?? template.ChunkMode,
            template.MinChunkBytes,
            template.MaxChunkBytes,
            template.FixedChunkBytes,
            template.Seed,
            timingModeOverride ?? template.TimingMode,
            linesPerSecondOverride ?? template.LinesPerSecond,
            template.BytesPerSecond,
            template.TimestampSpeedMultiplier,
            template.BurstSize,
            template.BurstQuietInterval,
            template.KeepWorkspace,
            template.JsonReportPath,
            template.TextReportPath,
            template.BurstCount,
            template.RequestedLinesPerSecond);
}

public sealed class ReplayPacingSummary
{
    public double? RequestedLinesPerSecond { get; init; }

    public double? AchievedLinesPerSecond { get; init; }

    public long UnderrunCount { get; init; }

    public double OverrunMilliseconds { get; init; }
}
