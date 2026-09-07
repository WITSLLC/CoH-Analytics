using System.Globalization;
using CoHAnalytics.Replay.Reporting;

namespace CoHAnalytics.Replay;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            return await RunAsync(args, TimeProvider.System, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return (int)ReplayExitCode.IoFailure;
        }
    }

    internal static async Task<int> RunAsync(
        string[] args,
        TimeProvider timeProvider,
        CancellationToken cancellationToken,
        ReplayPerformanceOptions? performanceOptions = null)
    {
        if (args.Length == 0 || args.Contains("--help", StringComparer.OrdinalIgnoreCase))
        {
            PrintHelp();
            return args.Length == 0 ? (int)ReplayExitCode.InvalidConfiguration : (int)ReplayExitCode.Success;
        }

        if (TryGetArgumentValue(args, "--profile", out var profileName))
        {
            return await RunProfileAsync(
                args,
                profileName!,
                timeProvider,
                cancellationToken,
                performanceOptions).ConfigureAwait(false);
        }

        ReplayPlan plan;
        ReplayPerformanceOptions effectivePerformanceOptions;
        try
        {
            (plan, effectivePerformanceOptions) = ParsePlan(args);
            if (performanceOptions is not null)
            {
                effectivePerformanceOptions = performanceOptions;
            }
        }
        catch (ReplayConfigurationException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return (int)ReplayExitCode.InvalidConfiguration;
        }

        var startedAtUtc = timeProvider.GetUtcNow();
        var startTimestamp = timeProvider.GetTimestamp();
        var ledger = new ReplayLedger();
        var timeline = new ReplayTimeline(timeProvider);
        await using var workspace = await ReplayWorkspace.CreateAsync(
            plan.KeepWorkspace,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        timeline.Record(
            ReplayTimelineCategory.Run,
            "run.started",
            ReplayTimelineRetentionClass.Anchor);
        timeline.Record(
            ReplayTimelineCategory.Workspace,
            "workspace.created",
            ReplayTimelineRetentionClass.Milestone);

        var status = "Completed";
        ReplayPipelineResult? pipelineResult = null;
        await using var pipeline = new ReplayPipeline(
            timeProvider,
            characterDataDirectory: null,
            effectivePerformanceOptions);
        try
        {
            pipelineResult = await pipeline.ExecuteAsync(
                new ReplayPipelineRequest
                {
                    Contexts = [new ReplayContextBinding(workspace, plan)]
                },
                ledger,
                timeline,
                cancellationToken).ConfigureAwait(false);

            if (!pipelineResult.Success)
            {
                status = "Failed";
                foreach (var failure in pipelineResult.Correctness.Failures)
                {
                    Console.Error.WriteLine($"{failure.Code}: {failure.Message}");
                }

                timeline.Record(
                    ReplayTimelineCategory.Run,
                    "run.failed",
                    ReplayTimelineRetentionClass.Anchor);
                return (int)ReplayExitCode.CorrectnessFailure;
            }

            timeline.Record(
                ReplayTimelineCategory.Run,
                "run.completed",
                ReplayTimelineRetentionClass.Anchor);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            status = "Cancelled";
            timeline.Record(
                ReplayTimelineCategory.Run,
                "run.failed",
                ReplayTimelineRetentionClass.Anchor);
            return (int)ReplayExitCode.Cancelled;
        }
        catch (ReplayLifecycleTimeoutException exception)
        {
            status = "Failed";
            Console.Error.WriteLine(exception.Message);
            timeline.Record(
                ReplayTimelineCategory.Run,
                "run.failed",
                ReplayTimelineRetentionClass.Anchor);
            return (int)ReplayExitCode.CorrectnessFailure;
        }
        catch (ReplayTimestampException exception)
        {
            status = "Failed";
            Console.Error.WriteLine(exception.Message);
            timeline.Record(
                ReplayTimelineCategory.Run,
                "run.failed",
                ReplayTimelineRetentionClass.Anchor);
            return (int)ReplayExitCode.TimestampValidationFailure;
        }
        catch (ReplayAccountingException exception)
        {
            status = "Failed";
            Console.Error.WriteLine(exception.Message);
            timeline.Record(
                ReplayTimelineCategory.Run,
                "run.failed",
                ReplayTimelineRetentionClass.Anchor);
            return (int)ReplayExitCode.AccountingFailure;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            status = "Failed";
            Console.Error.WriteLine(exception.Message);
            timeline.Record(
                ReplayTimelineCategory.Run,
                "run.failed",
                ReplayTimelineRetentionClass.Anchor);
            return (int)ReplayExitCode.IoFailure;
        }

        timeline.Record(
            ReplayTimelineCategory.Workspace,
            "workspace.cleanup.started",
            ReplayTimelineRetentionClass.Milestone);
        await workspace.CleanupAsync(CancellationToken.None).ConfigureAwait(false);
        timeline.Record(
            ReplayTimelineCategory.Workspace,
            "workspace.cleanup.completed",
            ReplayTimelineRetentionClass.Milestone);

        var completedAtUtc = timeProvider.GetUtcNow();
        var elapsedMilliseconds = (long)((timeProvider.GetTimestamp() - startTimestamp) * 1000.0
            / timeProvider.TimestampFrequency);
        var report = ReplayReportBuilder.Build(
            plan,
            ledger,
            timeline,
            workspace,
            startedAtUtc,
            completedAtUtc,
            elapsedMilliseconds,
            status,
            pipelineResult);

        PrintConsoleSummary(report);

        try
        {
            if (!string.IsNullOrWhiteSpace(plan.JsonReportPath))
            {
                await ReplayReportBuilder.WriteJsonAsync(report, plan.JsonReportPath!, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (!string.IsNullOrWhiteSpace(plan.TextReportPath))
            {
                await ReplayReportBuilder.WriteTextAsync(report, plan.TextReportPath!, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine(exception.Message);
            return (int)ReplayExitCode.ReportFailure;
        }

        return (int)ReplayExitCode.Success;
    }

    internal static async Task<int> RunProfileAsync(
        string[] args,
        string profileName,
        TimeProvider timeProvider,
        CancellationToken cancellationToken,
        ReplayPerformanceOptions? performanceOptions = null)
    {
        ReplayProfileOverrides overrides;
        ReplayPerformanceOptions effectivePerformanceOptions;
        try
        {
            (overrides, effectivePerformanceOptions) = ParseProfileOverrides(args);
            effectivePerformanceOptions = new ReplayPerformanceOptions
            {
                TimeProvider = timeProvider,
                EnableSampling = effectivePerformanceOptions.EnableSampling,
                EnableResourceSampling = effectivePerformanceOptions.EnableResourceSampling,
                SampleInterval = effectivePerformanceOptions.SampleInterval,
                MaxRetainedSamples = effectivePerformanceOptions.MaxRetainedSamples,
                QueueThresholdPercents = effectivePerformanceOptions.QueueThresholdPercents
            };
            if (performanceOptions is not null)
            {
                effectivePerformanceOptions = performanceOptions;
            }
        }
        catch (ReplayConfigurationException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return (int)ReplayExitCode.InvalidConfiguration;
        }

        var startedAtUtc = timeProvider.GetUtcNow();
        var startTimestamp = timeProvider.GetTimestamp();
        var ledger = new ReplayLedger();
        var timeline = new ReplayTimeline(timeProvider);
        var profileWorkspaceRoot = Path.Combine(Path.GetTempPath(), "coh-analytics-replay-profile", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(profileWorkspaceRoot);

        ReplayProfileResolution profileResolution;
        try
        {
            profileResolution = ReplayProfileCatalog.Resolve(profileName, overrides, profileWorkspaceRoot);
        }
        catch (ReplayConfigurationException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return (int)ReplayExitCode.InvalidConfiguration;
        }

        var workspaces = new List<ReplayWorkspace>();
        var bindings = new List<ReplayContextBinding>();
        try
        {
            for (var index = 0; index < profileResolution.ContextPlans.Count; index++)
            {
                var contextPlan = profileResolution.ContextPlans[index];
                var workspace = await ReplayWorkspace.CreateAsync(
                    keepWorkspace: true,
                    accountFolderName: $"replay-profile-{index + 1:D2}",
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                workspaces.Add(workspace);
                bindings.Add(new ReplayContextBinding(workspace, contextPlan.Plan));
            }

            timeline.Record(
                ReplayTimelineCategory.Run,
                "run.started",
                ReplayTimelineRetentionClass.Anchor);
            timeline.Record(
                ReplayTimelineCategory.Workspace,
                "workspace.created",
                ReplayTimelineRetentionClass.Milestone);

            ReplayLatencyTracker? latencyTracker = null;
            if (profileResolution.Profile.LatencyMarkersEnabled)
            {
                latencyTracker = new ReplayLatencyTracker(timeProvider);
            }

            var status = "Completed";
            ReplayPipelineResult? pipelineResult = null;
            await using var pipeline = new ReplayPipeline(
                timeProvider,
                characterDataDirectory: null,
                effectivePerformanceOptions);
            try
            {
                pipelineResult = await pipeline.ExecuteAsync(
                    new ReplayPipelineRequest
                    {
                        Contexts = bindings,
                        ProfileResolution = profileResolution,
                        LatencyTracker = latencyTracker
                    },
                    ledger,
                    timeline,
                    cancellationToken).ConfigureAwait(false);

                var profileOutcome = pipelineResult.Profile?.Outcome;
                var profileSucceeded = profileOutcome is not null
                    && ReplayProfileCatalog.ShouldReturnSuccessExitCode(
                        profileResolution.Profile,
                        profileOutcome.Value);
                if (!profileSucceeded)
                {
                    status = "Failed";
                    foreach (var failure in pipelineResult.Correctness.Failures)
                    {
                        Console.Error.WriteLine($"{failure.Code}: {failure.Message}");
                    }

                    if (profileOutcome is not null)
                    {
                        Console.Error.WriteLine($"Profile outcome: {profileOutcome}");
                        if (pipelineResult.Profile?.OverloadDiagnosticsSummary is not null)
                        {
                            Console.Error.WriteLine(pipelineResult.Profile.OverloadDiagnosticsSummary);
                        }
                    }

                    timeline.Record(
                        ReplayTimelineCategory.Run,
                        "run.failed",
                        ReplayTimelineRetentionClass.Anchor);
                    return profileOutcome == ReplayProfileOutcome.Cancelled
                        ? (int)ReplayExitCode.Cancelled
                        : (int)ReplayExitCode.CorrectnessFailure;
                }

                timeline.Record(
                    ReplayTimelineCategory.Run,
                    "run.completed",
                    ReplayTimelineRetentionClass.Anchor);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                status = "Cancelled";
                timeline.Record(
                    ReplayTimelineCategory.Run,
                    "run.failed",
                    ReplayTimelineRetentionClass.Anchor);
                return (int)ReplayExitCode.Cancelled;
            }
            catch (ReplayLifecycleTimeoutException exception)
            {
                Console.Error.WriteLine(exception.Message);
                return (int)ReplayExitCode.CorrectnessFailure;
            }
            catch (ReplayTimestampException exception)
            {
                Console.Error.WriteLine(exception.Message);
                return (int)ReplayExitCode.TimestampValidationFailure;
            }
            catch (ReplayAccountingException exception)
            {
                Console.Error.WriteLine(exception.Message);
                return (int)ReplayExitCode.AccountingFailure;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine(exception.Message);
                return (int)ReplayExitCode.IoFailure;
            }

            var completedAtUtc = timeProvider.GetUtcNow();
            var elapsedMilliseconds = (long)((timeProvider.GetTimestamp() - startTimestamp) * 1000.0
                / timeProvider.TimestampFrequency);
            var primaryPlan = bindings[0].Plan;
            var report = ReplayReportBuilder.Build(
                primaryPlan,
                ledger,
                timeline,
                workspaces[0],
                startedAtUtc,
                completedAtUtc,
                elapsedMilliseconds,
                status,
                pipelineResult);

            PrintProfileSummary(report, pipelineResult);
            return (int)ReplayExitCode.Success;
        }
        finally
        {
            foreach (var workspace in workspaces)
            {
                await workspace.DisposeAsync().ConfigureAwait(false);
            }

            try
            {
                if (Directory.Exists(profileWorkspaceRoot))
                {
                    Directory.Delete(profileWorkspaceRoot, recursive: true);
                }
            }
            catch
            {
            }
        }
    }

    private static (ReplayProfileOverrides Overrides, ReplayPerformanceOptions PerformanceOptions) ParseProfileOverrides(
        string[] args)
    {
        double? rateLinesPerSecond = null;
        int? burstLines = null;
        int? burstCount = null;
        int? burstQuietMs = null;
        int? burstBytes = null;
        int? stressLines = null;
        int? stressSeed = null;
        int? stressFanOut = null;
        bool? latencyMarkers = null;
        int? latencyMarkerEveryLines = null;
        string? contextARate = null;
        string? contextBRate = null;
        bool? performanceSampling = null;
        var sampleIntervalMs = 250;
        var maxPerformanceSamples = 64;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--rate-lines-per-second":
                    rateLinesPerSecond = double.Parse(
                        ReadValue(args, ref index, "--rate-lines-per-second"),
                        CultureInfo.InvariantCulture);
                    break;
                case "--burst-lines":
                    burstLines = int.Parse(ReadValue(args, ref index, "--burst-lines"), CultureInfo.InvariantCulture);
                    break;
                case "--burst-count":
                    burstCount = int.Parse(ReadValue(args, ref index, "--burst-count"), CultureInfo.InvariantCulture);
                    break;
                case "--burst-quiet-ms":
                    burstQuietMs = int.Parse(ReadValue(args, ref index, "--burst-quiet-ms"), CultureInfo.InvariantCulture);
                    break;
                case "--burst-bytes":
                    burstBytes = int.Parse(ReadValue(args, ref index, "--burst-bytes"), CultureInfo.InvariantCulture);
                    break;
                case "--stress-lines":
                    stressLines = int.Parse(ReadValue(args, ref index, "--stress-lines"), CultureInfo.InvariantCulture);
                    break;
                case "--stress-seed":
                    stressSeed = int.Parse(ReadValue(args, ref index, "--stress-seed"), CultureInfo.InvariantCulture);
                    break;
                case "--stress-fanout":
                    stressFanOut = int.Parse(ReadValue(args, ref index, "--stress-fanout"), CultureInfo.InvariantCulture);
                    break;
                case "--latency-markers":
                    latencyMarkers = ParseOnOff(ReadValue(args, ref index, "--latency-markers"), "--latency-markers");
                    break;
                case "--latency-marker-every-lines":
                    latencyMarkerEveryLines = int.Parse(
                        ReadValue(args, ref index, "--latency-marker-every-lines"),
                        CultureInfo.InvariantCulture);
                    break;
                case "--context-a-rate":
                    contextARate = ReadValue(args, ref index, "--context-a-rate");
                    break;
                case "--context-b-rate":
                    contextBRate = ReadValue(args, ref index, "--context-b-rate");
                    break;
                case "--performance-sampling":
                    performanceSampling = ParseOnOff(
                        ReadValue(args, ref index, "--performance-sampling"),
                        "--performance-sampling");
                    break;
                case "--sample-interval-ms":
                    sampleIntervalMs = int.Parse(
                        ReadValue(args, ref index, "--sample-interval-ms"),
                        CultureInfo.InvariantCulture);
                    break;
                case "--max-performance-samples":
                    maxPerformanceSamples = int.Parse(
                        ReadValue(args, ref index, "--max-performance-samples"),
                        CultureInfo.InvariantCulture);
                    break;
                case "--profile":
                    _ = ReadValue(args, ref index, "--profile");
                    break;
            }
        }

        if (sampleIntervalMs < 1)
        {
            throw new ReplayConfigurationException("--sample-interval-ms must be at least 1.");
        }

        if (maxPerformanceSamples < 0)
        {
            throw new ReplayConfigurationException("--max-performance-samples must be non-negative.");
        }

        return (
            new ReplayProfileOverrides
            {
                RateLinesPerSecond = rateLinesPerSecond,
                BurstLines = burstLines,
                BurstCount = burstCount,
                BurstQuietMs = burstQuietMs,
                BurstBytes = burstBytes,
                StressLines = stressLines,
                StressSeed = stressSeed,
                StressFanOut = stressFanOut,
                LatencyMarkers = latencyMarkers,
                LatencyMarkerEveryLines = latencyMarkerEveryLines,
                ContextARate = contextARate,
                ContextBRate = contextBRate,
                PerformanceSampling = performanceSampling
            },
            new ReplayPerformanceOptions
            {
                EnableSampling = performanceSampling ?? true,
                EnableResourceSampling = performanceSampling ?? true,
                SampleInterval = TimeSpan.FromMilliseconds(sampleIntervalMs),
                MaxRetainedSamples = maxPerformanceSamples
            });
    }

    private static bool TryGetArgumentValue(string[] args, string name, out string? value)
    {
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
            {
                value = args[index + 1];
                return true;
            }
        }

        value = null;
        return false;
    }

    private static void PrintProfileSummary(ReplayReportDocument report, ReplayPipelineResult? pipelineResult)
    {
        PrintConsoleSummary(report);
        if (pipelineResult?.Profile is null)
        {
            return;
        }

        Console.WriteLine($"Profile: {pipelineResult.Profile.ProfileName}");
        Console.WriteLine($"Profile outcome: {pipelineResult.Profile.Outcome}");
        if (pipelineResult.Profile.RequestedRateLinesPerSecond is not null)
        {
            Console.WriteLine($"Requested rate: {pipelineResult.Profile.RequestedRateLinesPerSecond:F0} lines/sec");
        }

        if (pipelineResult.Profile.AchievedRateLinesPerSecond is not null)
        {
            Console.WriteLine($"Achieved rate: {pipelineResult.Profile.AchievedRateLinesPerSecond:F2} lines/sec");
        }

        if (pipelineResult.Performance?.ConcurrentWriteThroughput is { } concurrentThroughput)
        {
            Console.WriteLine(
                $"Aggregate write duration: {concurrentThroughput.AggregateWriteDurationMilliseconds} ms");
            Console.WriteLine(
                $"Aggregate throughput: {concurrentThroughput.AggregateLinesPerSecond:F2} lines/sec, "
                + $"{concurrentThroughput.AggregateBytesPerSecond:F2} bytes/sec");
        }
        else if (pipelineResult.Performance?.WriteDurationMilliseconds is not null)
        {
            Console.WriteLine($"Write duration: {pipelineResult.Performance.WriteDurationMilliseconds} ms");
        }

        if (pipelineResult.Performance is not null)
        {
            Console.WriteLine($"Performance validity: {pipelineResult.Performance.Validity}");
        }
    }

    private static (ReplayPlan Plan, ReplayPerformanceOptions PerformanceOptions) ParsePlan(string[] args)
    {
        var sources = new List<string>();
        var dates = new List<DateOnly>();
        ReplayInputMode? inputMode = null;
        var chunkMode = ReplayChunkMode.SeededVariable;
        var timingMode = ReplayTimingMode.Maximum;
        var minChunkBytes = 1;
        var maxChunkBytes = 4096;
        var fixedChunkBytes = 1024;
        var seed = 12345;
        var linesPerSecond = 10.0;
        var bytesPerSecond = 4096.0;
        var speedMultiplier = 1.0;
        var burstSize = 5;
        var burstQuietMs = 250;
        var keepWorkspace = false;
        string? jsonReport = null;
        string? textReport = null;
        var performanceSamplingEnabled = true;
        var sampleIntervalMs = 250;
        var maxPerformanceSamples = 64;

        for (var index = 0; index < args.Length; index++)
        {
            var token = args[index];
            switch (token)
            {
                case "--source":
                    sources.Add(ReadValue(args, ref index, "--source"));
                    break;
                case "--start-date":
                    dates.Add(DateOnly.Parse(ReadValue(args, ref index, "--start-date"), CultureInfo.InvariantCulture));
                    break;
                case "--input-mode":
                    inputMode = Enum.Parse<ReplayInputMode>(
                        ReadValue(args, ref index, "--input-mode"),
                        ignoreCase: true);
                    break;
                case "--chunk-mode":
                    chunkMode = ParseChunkMode(ReadValue(args, ref index, "--chunk-mode"));
                    break;
                case "--min-chunk-bytes":
                    minChunkBytes = int.Parse(ReadValue(args, ref index, "--min-chunk-bytes"), CultureInfo.InvariantCulture);
                    break;
                case "--max-chunk-bytes":
                    maxChunkBytes = int.Parse(ReadValue(args, ref index, "--max-chunk-bytes"), CultureInfo.InvariantCulture);
                    break;
                case "--fixed-chunk-bytes":
                    fixedChunkBytes = int.Parse(ReadValue(args, ref index, "--fixed-chunk-bytes"), CultureInfo.InvariantCulture);
                    break;
                case "--seed":
                    seed = int.Parse(ReadValue(args, ref index, "--seed"), CultureInfo.InvariantCulture);
                    break;
                case "--timing-mode":
                    timingMode = ParseTimingMode(ReadValue(args, ref index, "--timing-mode"));
                    break;
                case "--lines-per-second":
                    linesPerSecond = double.Parse(ReadValue(args, ref index, "--lines-per-second"), CultureInfo.InvariantCulture);
                    break;
                case "--bytes-per-second":
                    bytesPerSecond = double.Parse(ReadValue(args, ref index, "--bytes-per-second"), CultureInfo.InvariantCulture);
                    break;
                case "--speed-multiplier":
                    speedMultiplier = double.Parse(ReadValue(args, ref index, "--speed-multiplier"), CultureInfo.InvariantCulture);
                    break;
                case "--burst-size":
                    burstSize = int.Parse(ReadValue(args, ref index, "--burst-size"), CultureInfo.InvariantCulture);
                    break;
                case "--burst-lines":
                    burstSize = int.Parse(ReadValue(args, ref index, "--burst-lines"), CultureInfo.InvariantCulture);
                    break;
                case "--burst-count":
                    _ = int.Parse(ReadValue(args, ref index, "--burst-count"), CultureInfo.InvariantCulture);
                    break;
                case "--rate-lines-per-second":
                    linesPerSecond = double.Parse(
                        ReadValue(args, ref index, "--rate-lines-per-second"),
                        CultureInfo.InvariantCulture);
                    timingMode = ReplayTimingMode.FixedLines;
                    break;
                case "--burst-quiet-ms":
                    burstQuietMs = int.Parse(ReadValue(args, ref index, "--burst-quiet-ms"), CultureInfo.InvariantCulture);
                    break;
                case "--keep-workspace":
                    keepWorkspace = true;
                    break;
                case "--json-report":
                    jsonReport = ReadValue(args, ref index, "--json-report");
                    break;
                case "--text-report":
                    textReport = ReadValue(args, ref index, "--text-report");
                    break;
                case "--performance-sampling":
                    performanceSamplingEnabled = ParseOnOff(
                        ReadValue(args, ref index, "--performance-sampling"),
                        "--performance-sampling");
                    break;
                case "--sample-interval-ms":
                    sampleIntervalMs = int.Parse(
                        ReadValue(args, ref index, "--sample-interval-ms"),
                        CultureInfo.InvariantCulture);
                    break;
                case "--max-performance-samples":
                    maxPerformanceSamples = int.Parse(
                        ReadValue(args, ref index, "--max-performance-samples"),
                        CultureInfo.InvariantCulture);
                    break;
                default:
                    throw new ReplayConfigurationException($"Unknown argument '{token}'.");
            }
        }

        if (inputMode is null)
        {
            throw new ReplayConfigurationException("--input-mode is required (exact or bootstrap).");
        }

        if (sources.Count == 0)
        {
            throw new ReplayConfigurationException("At least one --source is required.");
        }

        if (sources.Count != dates.Count)
        {
            throw new ReplayConfigurationException("Each --source must be paired with a --start-date.");
        }

        var segments = sources
            .Select((source, index) => new ReplaySourceSegment(source, dates[index], index + 1))
            .ToArray();

        if (sampleIntervalMs < 1)
        {
            throw new ReplayConfigurationException("--sample-interval-ms must be at least 1.");
        }

        if (maxPerformanceSamples < 0)
        {
            throw new ReplayConfigurationException("--max-performance-samples must be non-negative.");
        }

        var plan = ReplayPlan.Create(
            segments,
            inputMode.Value,
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
            keepWorkspace,
            jsonReport,
            textReport);

        var performanceOptions = new ReplayPerformanceOptions
        {
            EnableSampling = performanceSamplingEnabled,
            EnableResourceSampling = performanceSamplingEnabled,
            SampleInterval = TimeSpan.FromMilliseconds(sampleIntervalMs),
            MaxRetainedSamples = maxPerformanceSamples
        };

        return (plan, performanceOptions);
    }

    private static bool ParseOnOff(string value, string optionName) =>
        value.ToLowerInvariant() switch
        {
            "on" => true,
            "off" => false,
            _ => throw new ReplayConfigurationException($"{optionName} must be 'on' or 'off'.")
        };

    private static string ReadValue(string[] args, ref int index, string name)
    {
        if (index + 1 >= args.Length)
        {
            throw new ReplayConfigurationException($"Missing value for {name}.");
        }

        index++;
        return args[index];
    }

    private static ReplayChunkMode ParseChunkMode(string value) => value.ToLowerInvariant() switch
    {
        "whole-line" => ReplayChunkMode.WholeLine,
        "fixed-bytes" => ReplayChunkMode.FixedBytes,
        "seeded-variable" => ReplayChunkMode.SeededVariable,
        "one-byte" => ReplayChunkMode.OneByte,
        _ => throw new ReplayConfigurationException($"Unknown chunk mode '{value}'.")
    };

    private static ReplayTimingMode ParseTimingMode(string value) => value.ToLowerInvariant() switch
    {
        "maximum" => ReplayTimingMode.Maximum,
        "fixed-lines" => ReplayTimingMode.FixedLines,
        "fixed-bytes" => ReplayTimingMode.FixedBytes,
        "timestamp" => ReplayTimingMode.Timestamp,
        "burst" => ReplayTimingMode.Burst,
        _ => throw new ReplayConfigurationException($"Unknown timing mode '{value}'.")
    };

    private static void PrintConsoleSummary(ReplayReportDocument report)
    {
        Console.WriteLine("CoH Analytics Replay");
        Console.WriteLine($"Status: {report.Run.Status}");
        Console.WriteLine($"Input mode: {report.Input.Mode}");
        if (string.Equals(report.Input.Mode, nameof(ReplayInputMode.Bootstrap), StringComparison.Ordinal))
        {
            Console.WriteLine("Bootstrap input was injected before the first source segment.");
        }

        Console.WriteLine($"Source bytes: {report.Input.SourceBytes}");
        Console.WriteLine($"Bootstrap bytes: {report.Input.BootstrapBytes}");
        Console.WriteLine($"Combined destination bytes: {report.Input.CombinedDestinationBytes}");
        Console.WriteLine($"Begins mid-session: {report.Input.BeginsMidSession}");
        if (report.Run.WorkspaceRetained)
        {
            Console.WriteLine("Workspace: retained (not deleted)");
        }
        else
        {
            Console.WriteLine($"Workspace deleted: {report.Run.WorkspaceCleanupCompleted}");
        }
        if (report.Correctness is not null)
        {
            Console.WriteLine($"Correctness passed: {report.Correctness.Passed}");
            Console.WriteLine($"Parser raw events: {report.Parser?.RawEventsObserved}");
            Console.WriteLine($"Gameplay commits: {report.Gameplay?.CommittedEventsObserved}");
        }

        Console.WriteLine($"Timeline events: {report.Timeline.Events.Count}");
        if (report.Throughput is not null)
        {
            Console.WriteLine($"Source lines: {report.Throughput.SourceCompleteLines}");
            Console.WriteLine($"Source bytes: {report.Throughput.SourceBytes}");
            if (report.Throughput.AggregateWriteDurationMs is not null)
            {
                Console.WriteLine($"Aggregate write duration: {report.Throughput.AggregateWriteDurationMs} ms");
            }

            if (report.Throughput.SourceLinesPerSecond is not null)
            {
                Console.WriteLine(
                    $"Achieved source rate: {report.Throughput.SourceLinesPerSecond:F2} lines/sec, "
                    + $"{report.Throughput.SourceBytesPerSecond:F2} bytes/sec");
            }
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine(
            """
            CoH Analytics Replay (1C.3)

            Usage:
              dotnet run --project src/CoHAnalytics.Replay -- --profile <name> [profile options]

            Profiles:
              baseline, fixed-rate, burst, maximum, dual-client, rollover, expected-overload

            Profile options:
              --rate-lines-per-second <N>
              --burst-lines <N> --burst-count <N> --burst-quiet-ms <N> [--burst-bytes <N>]
              --stress-lines <N> --stress-seed <N> --stress-fanout <N>
              --latency-markers on|off --latency-marker-every-lines <N>
              --context-a-rate <N|maximum> --context-b-rate <N|maximum>
              --performance-sampling on|off --sample-interval-ms <N> --max-performance-samples <N>

            Expected-overload profiles return exit code 0 only when gameplay work-queue overload was
            expected, observed via the admission-gated burst, and parser/gameplay invariants held.
            Dual-client aggregate throughput spans earliest context write start to latest context write end.

            Other profiles require correctness pass.

            Legacy exact replay:
              dotnet run --project src/CoHAnalytics.Replay -- \
                --source <path> --start-date <yyyy-MM-dd> \
                --input-mode exact|bootstrap \
                [--chunk-mode whole-line|fixed-bytes|seeded-variable|one-byte] \
                [--min-chunk-bytes N] [--max-chunk-bytes N] [--fixed-chunk-bytes N] [--seed N] \
                [--timing-mode maximum|fixed-lines|fixed-bytes|timestamp|burst] \
                [--lines-per-second N] [--rate-lines-per-second N] [--bytes-per-second N] [--speed-multiplier N] \
                [--burst-size N] [--burst-lines N] [--burst-quiet-ms N] \
                [--json-report <path>] [--text-report <path>] [--keep-workspace] \
                [--performance-sampling on|off] [--sample-interval-ms N] [--max-performance-samples N]

            Repeat --source and --start-date pairs for ordered rollover replay.

            Exit codes:
              0  Success — replay completed and all correctness checks passed.
              1  InvalidConfiguration — a required argument is missing or invalid.
              2  IoFailure — a source, destination, or report file could not be read or written.
              3  TimestampValidationFailure — source log timestamps are out of order or malformed.
              4  AccountingFailure — replay ledger validation detected a write-count mismatch.
              5  ReportFailure — replay succeeded but the JSON or text report file could not be written.
              6  CorrectnessFailure — the pipeline completed but correctness checks failed.
            130  Cancelled — the operation was cancelled (SIGINT / Ctrl+C).
            """);
    }
}
