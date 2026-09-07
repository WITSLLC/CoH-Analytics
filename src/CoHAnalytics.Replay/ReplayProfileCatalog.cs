using System.Globalization;
using CoHAnalytics.Models;
using CoHAnalytics.Services;

namespace CoHAnalytics.Replay;

public static class ReplayProfileCatalog
{
    private static readonly HashSet<double> SupportedFixedRates =
    [
        100,
        500,
        1000,
        2500,
        5000
    ];

    public static IReadOnlyList<string> ProfileNames { get; } =
    [
        "baseline",
        "fixed-rate",
        "burst",
        "maximum",
        "dual-client",
        "rollover",
        "expected-overload"
    ];

    public static ReplayProfileName ParseProfileName(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "baseline" => ReplayProfileName.Baseline,
            "fixed-rate" => ReplayProfileName.FixedRate,
            "burst" => ReplayProfileName.Burst,
            "maximum" => ReplayProfileName.Maximum,
            "dual-client" => ReplayProfileName.DualClient,
            "rollover" => ReplayProfileName.Rollover,
            "expected-overload" => ReplayProfileName.ExpectedOverload,
            _ => throw new ReplayConfigurationException($"Unknown profile '{value}'.")
        };
    }

    public static ReplayProfileResolution Resolve(
        string profileName,
        ReplayProfileOverrides overrides,
        string workspaceRoot)
    {
        var name = ParseProfileName(profileName);
        return name switch
        {
            ReplayProfileName.Baseline => ResolveBaseline(overrides, workspaceRoot),
            ReplayProfileName.FixedRate => ResolveFixedRate(overrides, workspaceRoot),
            ReplayProfileName.Burst => ResolveBurst(overrides, workspaceRoot),
            ReplayProfileName.Maximum => ResolveMaximum(overrides, workspaceRoot),
            ReplayProfileName.DualClient => ResolveDualClient(overrides, workspaceRoot),
            ReplayProfileName.Rollover => ResolveRollover(overrides, workspaceRoot),
            ReplayProfileName.ExpectedOverload => ResolveExpectedOverload(overrides, workspaceRoot),
            _ => throw new ReplayConfigurationException($"Unsupported profile '{profileName}'.")
        };
    }

    public static ReplayProfileOutcome DetermineOutcome(
        ReplayProfile profile,
        ReplayCorrectnessReport correctness,
        ReplayDrainResult drainResult,
        GameplaySessionDiagnostics gameplayDiagnostics,
        ParserManagerDiagnostics parserDiagnostics,
        ParserManagerSnapshot parserSnapshot,
        bool harnessFailed)
    {
        if (drainResult.Outcome == ReplayDrainOutcome.Cancelled)
        {
            return ReplayProfileOutcome.Cancelled;
        }

        if (harnessFailed)
        {
            return ReplayProfileOutcome.HarnessFailed;
        }

        var workQueueOverloadObserved = IsGameplayWorkQueueOverloadObserved(gameplayDiagnostics);
        var overloadObserved = workQueueOverloadObserved
            || gameplayDiagnostics.PreStartBufferOverflowed;

        if (profile.ExpectedOverloadBehavior == ReplayExpectedOverloadBehavior.GameplayWorkQueue)
        {
            if (!workQueueOverloadObserved)
            {
                return ReplayProfileOutcome.ExpectedOverloadNotObserved;
            }

            if (parserSnapshot.TotalLinesProcessed > 0
                && !parserDiagnostics.EventQueue.Overflowed)
            {
                if (!correctness.Passed
                    && profile.ExpectedCorrectnessBehavior == ReplayExpectedCorrectnessBehavior.AcceptExpectedOverloadOnly)
                {
                    return ReplayProfileOutcome.ExpectedOverloadObserved;
                }

                return correctness.Passed
                    ? ReplayProfileOutcome.ExpectedOverloadObserved
                    : ReplayProfileOutcome.CorrectnessFailed;
            }

            return ReplayProfileOutcome.CorrectnessFailed;
        }

        if (overloadObserved)
        {
            return ReplayProfileOutcome.UnexpectedOverload;
        }

        return correctness.Passed
            ? ReplayProfileOutcome.Passed
            : ReplayProfileOutcome.CorrectnessFailed;
    }

    public static bool IsGameplayWorkQueueOverloadObserved(GameplaySessionDiagnostics gameplayDiagnostics) =>
        gameplayDiagnostics.WorkQueueOverflowed || gameplayDiagnostics.WorkQueue.Overflowed;

    public static string BuildOverloadDiagnosticsSummary(
        ReplayExpectedOverloadBehavior expectedOverloadTarget,
        GameplaySessionDiagnostics gameplayDiagnostics,
        ParserManagerDiagnostics parserDiagnostics) =>
        $"Expected overload target={expectedOverloadTarget}; "
        + $"workQueueOverflowed={gameplayDiagnostics.WorkQueueOverflowed}; "
        + $"workQueue.overflowed={gameplayDiagnostics.WorkQueue.Overflowed}; "
        + $"accepted={gameplayDiagnostics.WorkQueue.AcceptedCount}; "
        + $"rejected={gameplayDiagnostics.WorkQueue.RejectedCount}; "
        + $"abandoned={gameplayDiagnostics.WorkQueue.AbandonedCount}; "
        + $"depth={gameplayDiagnostics.WorkQueue.CurrentDepth}; "
        + $"parserEventQueueOverflowed={parserDiagnostics.EventQueue.Overflowed}";

    public static bool ShouldReturnSuccessExitCode(ReplayProfile profile, ReplayProfileOutcome outcome) =>
        outcome switch
        {
            ReplayProfileOutcome.Passed => true,
            ReplayProfileOutcome.ExpectedOverloadObserved
                when profile.ExpectedOverloadBehavior != ReplayExpectedOverloadBehavior.None => true,
            _ => false
        };

    private static ReplayProfileResolution ResolveBaseline(ReplayProfileOverrides overrides, string workspaceRoot)
    {
        var stressLines = overrides.StressLines ?? 800;
        var seed = overrides.StressSeed ?? 42;
        var fanOut = overrides.StressFanOut ?? 8;
        var latencyMarkers = overrides.LatencyMarkers ?? true;
        var markerEvery = overrides.LatencyMarkerEveryLines ?? 100;
        var performanceSampling = overrides.PerformanceSampling ?? true;

        var profile = new ReplayProfile
        {
            Name = "baseline",
            ContextCount = 1,
            TimingMode = ReplayTimingMode.Maximum,
            ChunkMode = ReplayChunkMode.WholeLine,
            SourceWorkload = new ReplayStressWorkloadDefinition
            {
                LineCount = stressLines,
                Seed = seed,
                FanOut = fanOut,
                ContextId = 0,
                EnableLatencyMarkers = latencyMarkers,
                LatencyMarkerEveryLines = markerEvery
            },
            ExpectedOverloadBehavior = ReplayExpectedOverloadBehavior.None,
            ExpectedCorrectnessBehavior = ReplayExpectedCorrectnessBehavior.MustPass,
            DefaultDurationLines = stressLines,
            LatencyMarkersEnabled = latencyMarkers,
            PerformanceSamplingEnabled = performanceSampling
        };

        var workload = profile.SourceWorkload;
        var sourcePath = Path.Combine(workspaceRoot, "stress-baseline.log");
        ReplayStressFixtureGenerator.GenerateToFileAsync(workload, sourcePath).GetAwaiter().GetResult();
        var plan = BuildPlan(
            profile,
            [new ReplaySourceSegment(sourcePath, new DateOnly(2026, 8, 1), 1)],
            requestedRate: null);

        return BuildResolution(profile, plan, workload, sourcePath, overrides, requestedRate: null);
    }

    private static ReplayProfileResolution ResolveFixedRate(ReplayProfileOverrides overrides, string workspaceRoot)
    {
        var rate = overrides.RateLinesPerSecond ?? 1000;
        ValidateFixedRate(rate);
        var stressLines = overrides.StressLines ?? 200;
        var seed = overrides.StressSeed ?? 1001;
        var fanOut = overrides.StressFanOut ?? 6;

        var profile = new ReplayProfile
        {
            Name = "fixed-rate",
            ContextCount = 1,
            TimingMode = ReplayTimingMode.FixedLines,
            ChunkMode = ReplayChunkMode.WholeLine,
            LinesPerSecond = rate,
            SourceWorkload = new ReplayStressWorkloadDefinition
            {
                LineCount = stressLines,
                Seed = seed,
                FanOut = fanOut,
                ContextId = 0,
                EnableLatencyMarkers = overrides.LatencyMarkers ?? true,
                LatencyMarkerEveryLines = overrides.LatencyMarkerEveryLines ?? 100
            },
            ExpectedOverloadBehavior = ReplayExpectedOverloadBehavior.None,
            ExpectedCorrectnessBehavior = ReplayExpectedCorrectnessBehavior.MustPass,
            DefaultDurationLines = stressLines,
            LatencyMarkersEnabled = overrides.LatencyMarkers ?? true,
            PerformanceSamplingEnabled = overrides.PerformanceSampling ?? true
        };

        var sourcePath = Path.Combine(workspaceRoot, "stress-fixed-rate.log");
        ReplayStressFixtureGenerator.GenerateToFileAsync(profile.SourceWorkload, sourcePath).GetAwaiter().GetResult();
        var plan = BuildPlan(profile, [new ReplaySourceSegment(sourcePath, new DateOnly(2026, 8, 1), 1)], rate);
        return BuildResolution(profile, plan, profile.SourceWorkload, sourcePath, overrides, rate);
    }

    private static ReplayProfileResolution ResolveBurst(ReplayProfileOverrides overrides, string workspaceRoot)
    {
        var burstLines = overrides.BurstLines ?? 50;
        var burstCount = overrides.BurstCount ?? 5;
        var quietMs = overrides.BurstQuietMs ?? 100;
        if (burstLines < 1)
        {
            throw new ReplayConfigurationException("--burst-lines must be at least 1.");
        }

        if (burstCount < 1)
        {
            throw new ReplayConfigurationException("--burst-count must be at least 1.");
        }

        if (quietMs < 0)
        {
            throw new ReplayConfigurationException("--burst-quiet-ms must be non-negative.");
        }

        var totalLines = burstLines * burstCount;
        var profile = new ReplayProfile
        {
            Name = "burst",
            ContextCount = 1,
            TimingMode = ReplayTimingMode.Burst,
            ChunkMode = ReplayChunkMode.WholeLine,
            BurstSettings = new ReplayBurstSettings
            {
                LinesPerBurst = burstLines,
                BytesPerBurst = overrides.BurstBytes,
                BurstCount = burstCount,
                QuietInterval = TimeSpan.FromMilliseconds(quietMs)
            },
            SourceWorkload = new ReplayStressWorkloadDefinition
            {
                LineCount = totalLines,
                Seed = overrides.StressSeed ?? 2002,
                FanOut = overrides.StressFanOut ?? 10,
                ContextId = 0,
                EnableLatencyMarkers = overrides.LatencyMarkers ?? false,
                LatencyMarkerEveryLines = overrides.LatencyMarkerEveryLines ?? 1000
            },
            ExpectedOverloadBehavior = ReplayExpectedOverloadBehavior.None,
            ExpectedCorrectnessBehavior = ReplayExpectedCorrectnessBehavior.MustPass,
            DefaultDurationLines = totalLines,
            LatencyMarkersEnabled = overrides.LatencyMarkers ?? false,
            PerformanceSamplingEnabled = overrides.PerformanceSampling ?? true
        };

        var sourcePath = Path.Combine(workspaceRoot, "stress-burst.log");
        ReplayStressFixtureGenerator.GenerateToFileAsync(profile.SourceWorkload, sourcePath).GetAwaiter().GetResult();
        var plan = BuildPlan(
            profile,
            [new ReplaySourceSegment(sourcePath, new DateOnly(2026, 8, 1), 1)],
            requestedRate: null,
            burstSize: burstLines,
            burstQuietMs: quietMs);
        return BuildResolution(profile, plan, profile.SourceWorkload, sourcePath, overrides, requestedRate: null);
    }

    private static ReplayProfileResolution ResolveMaximum(ReplayProfileOverrides overrides, string workspaceRoot)
    {
        var stressLines = overrides.StressLines ?? 2000;
        var profile = new ReplayProfile
        {
            Name = "maximum",
            ContextCount = 1,
            TimingMode = ReplayTimingMode.Maximum,
            ChunkMode = ReplayChunkMode.OneByte,
            SourceWorkload = new ReplayStressWorkloadDefinition
            {
                LineCount = stressLines,
                Seed = overrides.StressSeed ?? 3003,
                FanOut = overrides.StressFanOut ?? 12,
                ContextId = 0,
                EnableLatencyMarkers = overrides.LatencyMarkers ?? false,
                LatencyMarkerEveryLines = overrides.LatencyMarkerEveryLines ?? 1000
            },
            ExpectedOverloadBehavior = ReplayExpectedOverloadBehavior.None,
            ExpectedCorrectnessBehavior = ReplayExpectedCorrectnessBehavior.MustPass,
            DefaultDurationLines = stressLines,
            LatencyMarkersEnabled = overrides.LatencyMarkers ?? false,
            PerformanceSamplingEnabled = overrides.PerformanceSampling ?? true
        };

        var sourcePath = Path.Combine(workspaceRoot, "stress-maximum.log");
        ReplayStressFixtureGenerator.GenerateToFileAsync(profile.SourceWorkload, sourcePath).GetAwaiter().GetResult();
        var plan = BuildPlan(profile, [new ReplaySourceSegment(sourcePath, new DateOnly(2026, 8, 1), 1)], requestedRate: null);
        return BuildResolution(profile, plan, profile.SourceWorkload, sourcePath, overrides, requestedRate: null);
    }

    private static ReplayProfileResolution ResolveDualClient(ReplayProfileOverrides overrides, string workspaceRoot)
    {
        var contextARate = ParseContextRate(overrides.ContextARate ?? "1000");
        var contextBRate = ParseContextRate(overrides.ContextBRate ?? "1000");
        var stressLines = overrides.StressLines ?? 300;
        var seed = overrides.StressSeed ?? 4004;

        var profile = new ReplayProfile
        {
            Name = "dual-client",
            ContextCount = 2,
            TimingMode = ReplayTimingMode.Maximum,
            ChunkMode = ReplayChunkMode.WholeLine,
            UseConcurrentContexts = true,
            ContextRates =
            [
                contextARate,
                contextBRate
            ],
            SourceWorkload = new ReplayStressWorkloadDefinition
            {
                LineCount = stressLines,
                Seed = seed,
                FanOut = overrides.StressFanOut ?? 8,
                ContextId = 0,
                EnableLatencyMarkers = overrides.LatencyMarkers ?? true,
                LatencyMarkerEveryLines = overrides.LatencyMarkerEveryLines ?? 100
            },
            ExpectedOverloadBehavior = ReplayExpectedOverloadBehavior.None,
            ExpectedCorrectnessBehavior = ReplayExpectedCorrectnessBehavior.MustPass,
            DefaultDurationLines = stressLines,
            LatencyMarkersEnabled = overrides.LatencyMarkers ?? true,
            PerformanceSamplingEnabled = overrides.PerformanceSampling ?? true
        };

        var contexts = new List<ReplayProfileContextPlan>();
        for (var contextIndex = 0; contextIndex < 2; contextIndex++)
        {
            var rate = contextIndex == 0 ? contextARate : contextBRate;
            var workload = profile.SourceWorkload with
            {
                ContextId = contextIndex,
                Seed = seed + contextIndex,
                LineCount = contextIndex == 0 && contextARate.Mode == ReplayContextRateMode.Maximum
                    ? Math.Max(stressLines, 600)
                    : stressLines
            };
            var sourcePath = Path.Combine(workspaceRoot, $"stress-dual-{contextIndex}.log");
            ReplayStressFixtureGenerator.GenerateToFileAsync(workload, sourcePath).GetAwaiter().GetResult();
            var timingMode = rate.Mode == ReplayContextRateMode.Maximum
                ? ReplayTimingMode.Maximum
                : ReplayTimingMode.FixedLines;
            var contextProfile = profile with
            {
                TimingMode = timingMode,
                LinesPerSecond = rate.Mode == ReplayContextRateMode.FixedLines ? rate.LinesPerSecond : null
            };
            var plan = BuildPlan(
                contextProfile,
                [new ReplaySourceSegment(sourcePath, new DateOnly(2026, 8, 1), 1)],
                rate.Mode == ReplayContextRateMode.FixedLines ? rate.LinesPerSecond : null);
            contexts.Add(new ReplayProfileContextPlan
            {
                Label = contextIndex == 0 ? "context-a" : "context-b",
                Plan = plan,
                Workload = workload,
                GeneratedSourcePaths = [sourcePath],
                StressWorkloadHash = ReplayStressFixtureGenerator.ComputeDeterministicHash(workload),
                RequestedLinesPerSecond = rate.Mode == ReplayContextRateMode.FixedLines ? rate.LinesPerSecond : null
            });
        }

        return new ReplayProfileResolution
        {
            Profile = profile,
            EffectiveOptions = BuildEffectiveOptions(profile, overrides),
            ContextPlans = contexts,
            RequestedRateLinesPerSecond = null,
            ExpectedOverloadTarget = profile.ExpectedOverloadBehavior
        };
    }

    private static ReplayProfileResolution ResolveRollover(ReplayProfileOverrides overrides, string workspaceRoot)
    {
        var linesPerSegment = overrides.StressLines ?? 120;
        var seed = overrides.StressSeed ?? 5005;
        var rate = overrides.RateLinesPerSecond;
        var timingMode = rate is > 0 ? ReplayTimingMode.FixedLines : ReplayTimingMode.Maximum;
        if (rate is > 0)
        {
            ValidateFixedRate(rate.Value);
        }

        var profile = new ReplayProfile
        {
            Name = "rollover",
            ContextCount = 1,
            TimingMode = timingMode,
            ChunkMode = ReplayChunkMode.WholeLine,
            LinesPerSecond = rate,
            SourceWorkload = new ReplayStressWorkloadDefinition
            {
                LineCount = linesPerSegment,
                Seed = seed,
                FanOut = overrides.StressFanOut ?? 6,
                ContextId = 0,
                SegmentCount = 2,
                EnableLatencyMarkers = overrides.LatencyMarkers ?? false,
                LatencyMarkerEveryLines = overrides.LatencyMarkerEveryLines ?? 1000
            },
            ExpectedOverloadBehavior = ReplayExpectedOverloadBehavior.None,
            ExpectedCorrectnessBehavior = ReplayExpectedCorrectnessBehavior.MustPass,
            DefaultDurationLines = linesPerSegment * 2,
            LatencyMarkersEnabled = overrides.LatencyMarkers ?? false,
            PerformanceSamplingEnabled = overrides.PerformanceSampling ?? true
        };

        var segment1Path = Path.Combine(workspaceRoot, "stress-rollover-day1.log");
        var segment2Path = Path.Combine(workspaceRoot, "stress-rollover-day2.log");
        var workload1 = profile.SourceWorkload;
        var workload2 = profile.SourceWorkload with { Seed = seed + 1, IncludeWelcome = false };
        ReplayStressFixtureGenerator.GenerateToFileAsync(workload1, segment1Path).GetAwaiter().GetResult();
        ReplayStressFixtureGenerator.GenerateToFileAsync(workload2, segment2Path).GetAwaiter().GetResult();
        var plan = BuildPlan(
            profile,
            [
                new ReplaySourceSegment(segment1Path, new DateOnly(2026, 8, 1), 1),
                new ReplaySourceSegment(segment2Path, new DateOnly(2026, 8, 2), 2)
            ],
            rate);
        return BuildResolution(
            profile,
            plan,
            profile.SourceWorkload,
            $"{segment1Path};{segment2Path}",
            overrides,
            rate);
    }

    private static ReplayProfileResolution ResolveExpectedOverload(ReplayProfileOverrides overrides, string workspaceRoot)
    {
        var stressLines = overrides.StressLines ?? 120;
        var profile = new ReplayProfile
        {
            Name = "expected-overload",
            ContextCount = 1,
            TimingMode = ReplayTimingMode.Maximum,
            ChunkMode = ReplayChunkMode.WholeLine,
            SourceWorkload = new ReplayStressWorkloadDefinition
            {
                LineCount = stressLines,
                Seed = overrides.StressSeed ?? 6006,
                FanOut = overrides.StressFanOut ?? 32,
                ContextId = 0,
                EnableLatencyMarkers = false,
                LatencyMarkerEveryLines = 1000
            },
            ExpectedOverloadBehavior = ReplayExpectedOverloadBehavior.GameplayWorkQueue,
            ExpectedCorrectnessBehavior = ReplayExpectedCorrectnessBehavior.AcceptExpectedOverloadOnly,
            DefaultDurationLines = stressLines,
            LatencyMarkersEnabled = false,
            PerformanceSamplingEnabled = overrides.PerformanceSampling ?? true,
            GameplayWorkQueueCapacity = 1,
            ExpectedOverloadAdmissionLineCount = ReplayExpectedOverloadAdmission.DefaultAdmissionLineCount
        };

        var sourcePath = Path.Combine(workspaceRoot, "stress-expected-overload.log");
        ReplayStressFixtureGenerator.GenerateToFileAsync(profile.SourceWorkload, sourcePath).GetAwaiter().GetResult();
        var plan = BuildPlan(
            profile,
            [new ReplaySourceSegment(sourcePath, new DateOnly(2026, 8, 1), 1)],
            requestedRate: null);
        return BuildResolution(profile, plan, profile.SourceWorkload, sourcePath, overrides, requestedRate: null);
    }

    private static ReplayProfileResolution BuildResolution(
        ReplayProfile profile,
        ReplayPlan plan,
        ReplayStressWorkloadDefinition workload,
        string sourcePath,
        ReplayProfileOverrides overrides,
        double? requestedRate) =>
        new()
        {
            Profile = profile,
            EffectiveOptions = BuildEffectiveOptions(profile, overrides),
            ContextPlans =
            [
                new ReplayProfileContextPlan
                {
                    Label = "context-1",
                    Plan = plan,
                    Workload = workload,
                    GeneratedSourcePaths = sourcePath.Contains(';', StringComparison.Ordinal)
                        ? sourcePath.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        : [sourcePath],
                    StressWorkloadHash = ReplayStressFixtureGenerator.ComputeDeterministicHash(workload),
                    RequestedLinesPerSecond = requestedRate
                }
            ],
            RequestedRateLinesPerSecond = requestedRate,
            ExpectedOverloadTarget = profile.ExpectedOverloadBehavior
        };

    private static ReplayPlan BuildPlan(
        ReplayProfile profile,
        IReadOnlyList<ReplaySourceSegment> segments,
        double? requestedRate,
        int? burstSize = null,
        int? burstQuietMs = null) =>
        ReplayPlan.Create(
            segments,
            profile.InputMode,
            profile.ChunkMode,
            profile.MinChunkBytes,
            profile.MaxChunkBytes,
            profile.FixedChunkBytes,
            profile.Seed,
            profile.TimingMode,
            requestedRate ?? profile.LinesPerSecond ?? 0,
            4096,
            1,
            burstSize ?? profile.BurstSettings?.LinesPerBurst ?? 1,
            TimeSpan.FromMilliseconds(burstQuietMs ?? profile.BurstSettings?.QuietInterval.TotalMilliseconds ?? 0),
            keepWorkspace: true,
            jsonReportPath: null,
            textReportPath: null,
            burstCount: profile.BurstSettings?.BurstCount,
            requestedLinesPerSecond: requestedRate ?? profile.LinesPerSecond);

    private static void ValidateFixedRate(double rate)
    {
        if (rate <= 0)
        {
            throw new ReplayConfigurationException("--rate-lines-per-second must be positive.");
        }
    }

    private static ReplayContextRate ParseContextRate(string value)
    {
        if (string.Equals(value, "maximum", StringComparison.OrdinalIgnoreCase))
        {
            return new ReplayContextRate(ReplayContextRateMode.Maximum);
        }

        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var rate) || rate <= 0)
        {
            throw new ReplayConfigurationException(
                $"Invalid context rate '{value}'. Use 'maximum' or a positive number.");
        }

        return new ReplayContextRate(ReplayContextRateMode.FixedLines, rate);
    }

    private static Dictionary<string, string> BuildEffectiveOptions(
        ReplayProfile profile,
        ReplayProfileOverrides overrides)
    {
        var options = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["profile"] = profile.Name,
            ["contextCount"] = profile.ContextCount.ToString(CultureInfo.InvariantCulture),
            ["timingMode"] = profile.TimingMode.ToString(),
            ["chunkMode"] = profile.ChunkMode.ToString(),
            ["stressLines"] = profile.SourceWorkload.LineCount.ToString(CultureInfo.InvariantCulture),
            ["stressSeed"] = profile.SourceWorkload.Seed.ToString(CultureInfo.InvariantCulture),
            ["stressFanOut"] = profile.SourceWorkload.FanOut.ToString(CultureInfo.InvariantCulture),
            ["latencyMarkers"] = profile.LatencyMarkersEnabled.ToString(),
            ["performanceSampling"] = profile.PerformanceSamplingEnabled.ToString()
        };

        if (profile.LinesPerSecond is > 0)
        {
            options["rateLinesPerSecond"] = profile.LinesPerSecond.Value.ToString(CultureInfo.InvariantCulture);
        }

        if (profile.BurstSettings is not null)
        {
            options["burstLines"] = profile.BurstSettings.LinesPerBurst.ToString(CultureInfo.InvariantCulture);
            options["burstCount"] = profile.BurstSettings.BurstCount.ToString(CultureInfo.InvariantCulture);
            options["burstQuietMs"] = ((long)profile.BurstSettings.QuietInterval.TotalMilliseconds)
                .ToString(CultureInfo.InvariantCulture);
        }

        if (overrides.ContextARate is not null)
        {
            options["contextARate"] = overrides.ContextARate;
        }

        if (overrides.ContextBRate is not null)
        {
            options["contextBRate"] = overrides.ContextBRate;
        }

        if (profile.GameplayWorkQueueCapacity is not null)
        {
            options["gameplayWorkQueueCapacity"] = profile.GameplayWorkQueueCapacity.Value
                .ToString(CultureInfo.InvariantCulture);
        }

        return options;
    }
}
