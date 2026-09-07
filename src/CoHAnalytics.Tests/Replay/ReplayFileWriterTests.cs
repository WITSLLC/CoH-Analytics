using System.Text;
using CoHAnalytics.Replay;

namespace CoHAnalytics.Tests.Replay;

public sealed class ReplayFileWriterTests
{
    [Fact]
    public async Task Exact_mode_preserves_source_bytes_without_bootstrap()
    {
        var sourcePath = ReplayTestPaths.Fixture("core-session.log");
        var sourceBytes = await File.ReadAllBytesAsync(sourcePath);
        var plan = ReplayTestPlanFactory.CreatePlan(
            sourcePath,
            new DateOnly(2026, 1, 15),
            ReplayInputMode.Exact);

        var (ledger, workspace) = await ReplayTestPlanFactory.ExecuteAsync(plan);
        var destination = await File.ReadAllBytesAsync(
            workspace.ResolveDestinationLogPath(new DateOnly(2026, 1, 15)));

        Assert.Equal(sourceBytes, destination);
        Assert.Equal(0, ledger.BootstrapBytes);
        Assert.Equal(sourceBytes.Length, ledger.CombinedDestinationBytes);

        Directory.Delete(workspace.RootPath, recursive: true);
    }

    [Fact]
    public async Task Bootstrap_mode_inserts_fictional_bootstrap_once()
    {
        var sourcePath = ReplayTestPaths.Fixture("core-session.log");
        var sourceBytes = await File.ReadAllBytesAsync(sourcePath);
        var plan = ReplayTestPlanFactory.CreatePlan(
            sourcePath,
            new DateOnly(2026, 1, 15),
            ReplayInputMode.Bootstrap);

        var (ledger, workspace) = await ReplayTestPlanFactory.ExecuteAsync(plan);
        var destination = await File.ReadAllBytesAsync(
            workspace.ResolveDestinationLogPath(new DateOnly(2026, 1, 15)));

        Assert.Equal(ReplayPlan.BootstrapBytes, destination.AsSpan(0, ReplayPlan.BootstrapBytes.Length).ToArray());
        Assert.Equal(sourceBytes, destination.AsSpan(ReplayPlan.BootstrapBytes.Length).ToArray());
        Assert.Equal(ReplayPlan.BootstrapBytes.Length, ledger.BootstrapBytes);
        Assert.Equal(sourceBytes.Length, ledger.OriginalSourceBytes);
        Assert.Equal(sourceBytes.Length + ReplayPlan.BootstrapBytes.Length, ledger.CombinedDestinationBytes);

        Directory.Delete(workspace.RootPath, recursive: true);
    }

    [Fact]
    public async Task Bootstrap_is_not_repeated_on_rollover()
    {
        var sourceOne = ReplayTestPaths.Fixture("rollover-day-1.log");
        var sourceTwo = ReplayTestPaths.Fixture("rollover-day-2.log");
        var plan = ReplayPlan.Create(
            [
                new ReplaySourceSegment(sourceOne, new DateOnly(2026, 1, 15), 1),
                new ReplaySourceSegment(sourceTwo, new DateOnly(2026, 1, 16), 2)
            ],
            ReplayInputMode.Bootstrap,
            ReplayChunkMode.WholeLine,
            1,
            4096,
            1024,
            12345,
            ReplayTimingMode.Maximum,
            10,
            4096,
            1,
            2,
            TimeSpan.FromMilliseconds(100),
            keepWorkspace: true,
            jsonReportPath: null,
            textReportPath: null);

        var (ledger, workspace) = await ReplayTestPlanFactory.ExecuteAsync(plan);
        var dayOne = await File.ReadAllBytesAsync(workspace.ResolveDestinationLogPath(new DateOnly(2026, 1, 15)));
        var dayTwo = await File.ReadAllBytesAsync(workspace.ResolveDestinationLogPath(new DateOnly(2026, 1, 16)));

        Assert.StartsWith(Encoding.UTF8.GetString(ReplayPlan.BootstrapBytes), Encoding.UTF8.GetString(dayOne));
        Assert.DoesNotContain(
            ReplayPlan.BootstrapLine,
            Encoding.UTF8.GetString(dayTwo));
        Assert.Equal(ReplayPlan.BootstrapBytes.Length, ledger.BootstrapBytes);
        Assert.Equal(1, ledger.RolloverCount);

        Directory.Delete(workspace.RootPath, recursive: true);
    }

    [Fact]
    public void Seeded_variable_chunks_are_deterministic_for_identical_input()
    {
        var source = Encoding.UTF8.GetBytes("abcdefghijklmnopqrstuvwxyz");
        var planA = ReplayTestPlanFactory.CreatePlan(
            ReplayTestPaths.Fixture("core-session.log"),
            new DateOnly(2026, 1, 1),
            ReplayInputMode.Exact,
            ReplayChunkMode.SeededVariable,
            seed: 42,
            minChunkBytes: 3,
            maxChunkBytes: 7);

        var chunksA = ReplayFileWriter.PlanChunks(planA, source);
        var chunksB = ReplayFileWriter.PlanChunks(planA, source);
        Assert.Equal(chunksA.Count, chunksB.Count);
        for (var index = 0; index < chunksA.Count; index++)
        {
            Assert.Equal(chunksA[index], chunksB[index]);
        }
    }

    [Fact]
    public void Different_seeds_can_produce_different_chunk_boundaries()
    {
        var source = Encoding.UTF8.GetBytes(new string('x', 200));
        var basePlan = ReplayTestPlanFactory.CreatePlan(
            ReplayTestPaths.Fixture("core-session.log"),
            new DateOnly(2026, 1, 1),
            ReplayInputMode.Exact,
            ReplayChunkMode.SeededVariable,
            seed: 1,
            minChunkBytes: 3,
            maxChunkBytes: 17);
        var alternatePlan = ReplayPlan.Create(
            basePlan.Segments,
            basePlan.InputMode,
            basePlan.ChunkMode,
            basePlan.MinChunkBytes,
            basePlan.MaxChunkBytes,
            basePlan.FixedChunkBytes,
            99,
            basePlan.TimingMode,
            basePlan.LinesPerSecond,
            basePlan.BytesPerSecond,
            basePlan.TimestampSpeedMultiplier,
            basePlan.BurstSize,
            basePlan.BurstQuietInterval,
            basePlan.KeepWorkspace,
            basePlan.JsonReportPath,
            basePlan.TextReportPath);

        var chunksA = ReplayFileWriter.PlanChunks(basePlan, source);
        var chunksB = ReplayFileWriter.PlanChunks(alternatePlan, source);
        Assert.NotEqual(
            chunksA.Select(chunk => chunk.Length).ToArray(),
            chunksB.Select(chunk => chunk.Length).ToArray());
    }

    [Fact]
    public async Task Fixed_byte_and_one_byte_modes_preserve_destination_bytes()
    {
        var sourcePath = ReplayTestPaths.Fixture("framing-and-encoding.bin");
        var sourceBytes = await File.ReadAllBytesAsync(sourcePath);

        foreach (var chunkMode in new[] { ReplayChunkMode.FixedBytes, ReplayChunkMode.OneByte })
        {
            var plan = ReplayTestPlanFactory.CreatePlan(
                sourcePath,
                new DateOnly(2026, 1, 1),
                ReplayInputMode.Exact,
                chunkMode,
                fixedChunkBytes: 4);
            var (_, workspace) = await ReplayTestPlanFactory.ExecuteAsync(plan);
            var destination = await File.ReadAllBytesAsync(workspace.ResolveDestinationLogPath(new DateOnly(2026, 1, 1)));
            Assert.Equal(sourceBytes, destination);
            Directory.Delete(workspace.RootPath, recursive: true);
        }
    }

    [Fact]
    public async Task Whole_line_mode_preserves_terminators_and_incomplete_final_fragment()
    {
        var sourcePath = ReplayTestPaths.Fixture("framing-and-encoding.bin");
        var sourceBytes = await File.ReadAllBytesAsync(sourcePath);
        var plan = ReplayTestPlanFactory.CreatePlan(
            sourcePath,
            new DateOnly(2026, 1, 1),
            ReplayInputMode.Exact,
            ReplayChunkMode.WholeLine);

        var (ledger, workspace) = await ReplayTestPlanFactory.ExecuteAsync(plan);
        var destination = await File.ReadAllBytesAsync(workspace.ResolveDestinationLogPath(new DateOnly(2026, 1, 1)));

        Assert.Equal(sourceBytes, destination);
        Assert.True(ledger.HasIncompleteFinalFragment);

        Directory.Delete(workspace.RootPath, recursive: true);
    }

    [Fact]
    public async Task Source_file_remains_unchanged_after_replay()
    {
        var sourcePath = ReplayTestPaths.Fixture("core-session.log");
        var before = await File.ReadAllBytesAsync(sourcePath);
        var plan = ReplayTestPlanFactory.CreatePlan(
            sourcePath,
            new DateOnly(2026, 1, 15),
            ReplayInputMode.Bootstrap,
            ReplayChunkMode.SeededVariable,
            seed: 7,
            minChunkBytes: 1,
            maxChunkBytes: 13);

        var (_, workspace) = await ReplayTestPlanFactory.ExecuteAsync(plan);
        var after = await File.ReadAllBytesAsync(sourcePath);
        Assert.Equal(before, after);

        Directory.Delete(workspace.RootPath, recursive: true);
    }

    [Fact]
    public async Task Timestamp_pacing_uses_first_source_timestamp_not_bootstrap()
    {
        var sourcePath = CreateTempSource(
            "2026-01-02 00:00:10 First source line.\n2026-01-02 00:00:20 Second source line.");
        var time = new ManualReplayTimeProvider();
        var plan = ReplayTestPlanFactory.CreatePlan(
            sourcePath,
            new DateOnly(2026, 1, 2),
            ReplayInputMode.Bootstrap,
            ReplayChunkMode.WholeLine,
            ReplayTimingMode.Timestamp,
            speedMultiplier: 1);

        await ReplayTestPlanFactory.ExecuteAsync(plan, time);
        Assert.Single(time.Delays);
        Assert.Equal(TimeSpan.FromSeconds(10), time.Delays[0]);

        File.Delete(sourcePath);
    }

    [Fact]
    public async Task Missing_timestamp_in_timestamp_mode_fails()
    {
        var sourcePath = CreateTempSource("no timestamp here\r\n");
        var plan = ReplayTestPlanFactory.CreatePlan(
            sourcePath,
            new DateOnly(2026, 1, 2),
            ReplayInputMode.Exact,
            ReplayChunkMode.WholeLine,
            ReplayTimingMode.Timestamp);

        await Assert.ThrowsAsync<ReplayTimestampException>(
            () => ReplayTestPlanFactory.ExecuteAsync(plan));

        File.Delete(sourcePath);
    }

    [Fact]
    public async Task Regressing_timestamp_in_timestamp_mode_fails()
    {
        var sourcePath = CreateTempSource(
            """
            2026-01-02 00:00:20 First line.
            2026-01-02 00:00:10 Regressed line.
            """);
        var plan = ReplayTestPlanFactory.CreatePlan(
            sourcePath,
            new DateOnly(2026, 1, 2),
            ReplayInputMode.Exact,
            ReplayChunkMode.WholeLine,
            ReplayTimingMode.Timestamp);

        await Assert.ThrowsAsync<ReplayTimestampException>(
            () => ReplayTestPlanFactory.ExecuteAsync(plan));

        File.Delete(sourcePath);
    }

    [Fact]
    public async Task Fixed_line_and_fixed_byte_timing_are_deterministic()
    {
        var sourcePath = ReplayTestPaths.Fixture("combat-churn.log");

        var linePlan = ReplayTestPlanFactory.CreatePlan(
            sourcePath,
            new DateOnly(2026, 1, 15),
            ReplayInputMode.Exact,
            ReplayChunkMode.WholeLine,
            ReplayTimingMode.FixedLines,
            linesPerSecond: 2);
        var lineTime = new ManualReplayTimeProvider();
        await ReplayTestPlanFactory.ExecuteAsync(linePlan, lineTime);
        Assert.All(lineTime.Delays, delay => Assert.Equal(TimeSpan.FromSeconds(0.5), delay));

        var bytePlan = ReplayTestPlanFactory.CreatePlan(
            sourcePath,
            new DateOnly(2026, 1, 15),
            ReplayInputMode.Exact,
            ReplayChunkMode.FixedBytes,
            ReplayTimingMode.FixedBytes,
            fixedChunkBytes: 10,
            bytesPerSecond: 100);
        var byteTime = new ManualReplayTimeProvider();
        await ReplayTestPlanFactory.ExecuteAsync(bytePlan, byteTime);
        Assert.All(byteTime.Delays.Take(byteTime.Delays.Count - 1), delay => Assert.Equal(TimeSpan.FromSeconds(0.1), delay));
        Assert.Equal(TimeSpan.FromSeconds(0.06), byteTime.Delays[^1]);
    }

    [Fact]
    public async Task Burst_timing_records_bursts_and_quiet_interval()
    {
        var sourcePath = ReplayTestPaths.Fixture("combat-churn.log");
        var time = new ManualReplayTimeProvider();
        var plan = ReplayTestPlanFactory.CreatePlan(
            sourcePath,
            new DateOnly(2026, 1, 15),
            ReplayInputMode.Exact,
            ReplayChunkMode.WholeLine,
            ReplayTimingMode.Burst,
            burstSize: 2,
            burstQuietMs: 50);

        var (ledger, workspace) = await ReplayTestPlanFactory.ExecuteAsync(plan, time);
        Assert.True(ledger.BurstCount > 0);
        Assert.Contains(TimeSpan.FromMilliseconds(50), time.Delays);

        Directory.Delete(workspace.RootPath, recursive: true);
    }

    [Fact]
    public async Task Large_input_is_streamed_without_loading_entire_file_into_writer_buffers()
    {
        var sourcePath = Path.Combine(Path.GetTempPath(), $"replay-large-{Guid.NewGuid():n}.bin");
        await using (var stream = new FileStream(sourcePath, FileMode.Create, FileAccess.Write, FileShare.Read))
        {
            var buffer = new byte[8192];
            for (var index = 0; index < buffer.Length; index++)
            {
                buffer[index] = (byte)(index % 251);
            }

            for (var block = 0; block < 256; block++)
            {
                await stream.WriteAsync(buffer);
            }
        }

        var plan = ReplayTestPlanFactory.CreatePlan(
            sourcePath,
            new DateOnly(2026, 2, 1),
            ReplayInputMode.Exact,
            ReplayChunkMode.FixedBytes,
            fixedChunkBytes: 1024);
        var (ledger, workspace) = await ReplayTestPlanFactory.ExecuteAsync(plan);
        Assert.Equal(8192L * 256, ledger.OriginalSourceBytes);
        var destination = await File.ReadAllBytesAsync(workspace.ResolveDestinationLogPath(new DateOnly(2026, 2, 1)));
        Assert.Equal(ledger.OriginalSourceBytes, destination.LongLength);

        File.Delete(sourcePath);
        Directory.Delete(workspace.RootPath, recursive: true);
    }

    [Fact]
    public async Task Whole_line_mode_detects_welcome_and_does_not_begin_mid_session()
    {
        var sourcePath = ReplayTestPaths.Fixture("core-session.log");
        var plan = ReplayTestPlanFactory.CreatePlan(
            sourcePath,
            new DateOnly(2026, 1, 15),
            ReplayInputMode.Exact,
            ReplayChunkMode.WholeLine);

        var (ledger, workspace) = await ReplayTestPlanFactory.ExecuteAsync(plan);
        Assert.False(ledger.BeginsMidSession);

        Directory.Delete(workspace.RootPath, recursive: true);
    }

    [Theory]
    [InlineData(ReplayChunkMode.FixedBytes)]
    [InlineData(ReplayChunkMode.OneByte)]
    [InlineData(ReplayChunkMode.SeededVariable)]
    public async Task Byte_chunk_modes_detect_welcome_split_across_chunks(ReplayChunkMode chunkMode)
    {
        var sourcePath = CreateWelcomeSource();
        var sourceBytes = await File.ReadAllBytesAsync(sourcePath);
        var plan = ReplayTestPlanFactory.CreatePlan(
            sourcePath,
            new DateOnly(2026, 1, 15),
            ReplayInputMode.Exact,
            chunkMode,
            seed: 17,
            minChunkBytes: 3,
            maxChunkBytes: 11,
            fixedChunkBytes: 13);

        var (ledger, workspace) = await ReplayTestPlanFactory.ExecuteAsync(plan);
        var destination = await File.ReadAllBytesAsync(
            workspace.ResolveDestinationLogPath(new DateOnly(2026, 1, 15)));

        Assert.False(ledger.BeginsMidSession);
        Assert.Equal(sourceBytes, destination);

        File.Delete(sourcePath);
        Directory.Delete(workspace.RootPath, recursive: true);
    }

    [Fact]
    public async Task Fixed_bytes_mode_detects_welcome_when_phrase_is_split_at_two_chunk_boundary()
    {
        var welcomeLine = "2026-01-15 10:00:00 Welcome to City of Heroes, Example Hero!\r\n";
        var sourceBytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(welcomeLine);
        var phraseIndex = welcomeLine.IndexOf("Welcome to City of Heroes", StringComparison.Ordinal);
        var splitIndex = phraseIndex + "Welcome to City ".Length;
        var chunkSize = splitIndex;

        var sourcePath = Path.Combine(Path.GetTempPath(), $"replay-welcome-split-{Guid.NewGuid():n}.log");
        await File.WriteAllBytesAsync(sourcePath, sourceBytes);

        var plan = ReplayTestPlanFactory.CreatePlan(
            sourcePath,
            new DateOnly(2026, 1, 15),
            ReplayInputMode.Exact,
            ReplayChunkMode.FixedBytes,
            fixedChunkBytes: chunkSize);

        var (ledger, workspace) = await ReplayTestPlanFactory.ExecuteAsync(plan);
        Assert.False(ledger.BeginsMidSession);
        Assert.Equal(sourceBytes, await File.ReadAllBytesAsync(
            workspace.ResolveDestinationLogPath(new DateOnly(2026, 1, 15))));

        File.Delete(sourcePath);
        Directory.Delete(workspace.RootPath, recursive: true);
    }

    [Fact]
    public async Task Source_without_welcome_reports_begins_mid_session_in_exact_mode()
    {
        var sourcePath = CreateMidSessionSource();
        var plan = ReplayTestPlanFactory.CreatePlan(
            sourcePath,
            new DateOnly(2026, 1, 15),
            ReplayInputMode.Exact,
            ReplayChunkMode.OneByte);

        var (ledger, workspace) = await ReplayTestPlanFactory.ExecuteAsync(plan);
        Assert.True(ledger.BeginsMidSession);

        File.Delete(sourcePath);
        Directory.Delete(workspace.RootPath, recursive: true);
    }

    [Fact]
    public async Task Bootstrap_mode_reports_mid_session_when_source_lacks_welcome()
    {
        var sourcePath = CreateMidSessionSource();
        var sourceBytes = await File.ReadAllBytesAsync(sourcePath);
        var plan = ReplayTestPlanFactory.CreatePlan(
            sourcePath,
            new DateOnly(2026, 1, 15),
            ReplayInputMode.Bootstrap,
            ReplayChunkMode.SeededVariable,
            seed: 99,
            minChunkBytes: 2,
            maxChunkBytes: 9);

        var (ledger, workspace) = await ReplayTestPlanFactory.ExecuteAsync(plan);
        Assert.True(ledger.BeginsMidSession);
        Assert.Equal(ReplayPlan.BootstrapBytes.Length, ledger.BootstrapBytes);
        Assert.Equal(sourceBytes.Length, ledger.OriginalSourceBytes);
        Assert.Equal(sourceBytes.Length + ReplayPlan.BootstrapBytes.Length, ledger.CombinedDestinationBytes);

        File.Delete(sourcePath);
        Directory.Delete(workspace.RootPath, recursive: true);
    }

    [Fact]
    public async Task Partial_welcome_lookalike_does_not_trigger_false_positive()
    {
        var sourcePath = CreateTempSource("2026-01-15 10:00:00 Welcome to City of Villains!\r\n");
        var plan = ReplayTestPlanFactory.CreatePlan(
            sourcePath,
            new DateOnly(2026, 1, 15),
            ReplayInputMode.Exact,
            ReplayChunkMode.OneByte);

        var (ledger, workspace) = await ReplayTestPlanFactory.ExecuteAsync(plan);
        Assert.True(ledger.BeginsMidSession);

        File.Delete(sourcePath);
        Directory.Delete(workspace.RootPath, recursive: true);
    }

    [Fact]
    public async Task Malformed_binary_source_does_not_crash_welcome_detection()
    {
        var sourcePath = ReplayTestPaths.Fixture("framing-and-encoding.bin");
        var sourceBytes = await File.ReadAllBytesAsync(sourcePath);
        var plan = ReplayTestPlanFactory.CreatePlan(
            sourcePath,
            new DateOnly(2026, 1, 1),
            ReplayInputMode.Exact,
            ReplayChunkMode.OneByte);

        var (ledger, workspace) = await ReplayTestPlanFactory.ExecuteAsync(plan);
        Assert.True(ledger.BeginsMidSession);
        Assert.Equal(sourceBytes, await File.ReadAllBytesAsync(
            workspace.ResolveDestinationLogPath(new DateOnly(2026, 1, 1))));

        Directory.Delete(workspace.RootPath, recursive: true);
    }

    [Fact]
    public async Task Rollover_replay_flushes_once_per_segment_and_accounts_bootstrap_once()
    {
        var sourceOne = ReplayTestPaths.Fixture("rollover-day-1.log");
        var sourceTwo = ReplayTestPaths.Fixture("rollover-day-2.log");
        var sourceOneBytes = await File.ReadAllBytesAsync(sourceOne);
        var sourceTwoBytes = await File.ReadAllBytesAsync(sourceTwo);
        var plan = ReplayPlan.Create(
            [
                new ReplaySourceSegment(sourceOne, new DateOnly(2026, 1, 15), 1),
                new ReplaySourceSegment(sourceTwo, new DateOnly(2026, 1, 16), 2)
            ],
            ReplayInputMode.Bootstrap,
            ReplayChunkMode.WholeLine,
            1,
            4096,
            1024,
            12345,
            ReplayTimingMode.Maximum,
            10,
            4096,
            1,
            2,
            TimeSpan.FromMilliseconds(100),
            keepWorkspace: true,
            jsonReportPath: null,
            textReportPath: null);

        var (ledger, workspace) = await ReplayTestPlanFactory.ExecuteAsync(plan);
        var dayOne = await File.ReadAllBytesAsync(workspace.ResolveDestinationLogPath(new DateOnly(2026, 1, 15)));
        var dayTwo = await File.ReadAllBytesAsync(workspace.ResolveDestinationLogPath(new DateOnly(2026, 1, 16)));

        Assert.Equal(2, ledger.Flushes);
        Assert.Equal(1, ledger.RolloverCount);
        Assert.Equal(ReplayPlan.BootstrapBytes.Length, ledger.BootstrapBytes);
        Assert.Equal(sourceOneBytes.Length + ReplayPlan.BootstrapBytes.Length, dayOne.Length);
        Assert.Equal(sourceTwoBytes.Length, dayTwo.Length);
        Assert.DoesNotContain(ReplayPlan.BootstrapLine, Encoding.UTF8.GetString(dayTwo));

        Directory.Delete(workspace.RootPath, recursive: true);
    }

    [Fact]
    public void Welcome_detector_matches_phrase_split_across_many_one_byte_chunks()
    {
        var source = Encoding.UTF8.GetBytes("prefix Welcome to City of Heroes suffix");
        var detector = new WelcomeBoundaryDetector();
        foreach (var value in source)
        {
            detector.Process([value]);
        }

        Assert.True(detector.SawWelcome);
    }

    private static string CreateWelcomeSource()
    {
        var path = Path.Combine(Path.GetTempPath(), $"replay-welcome-{Guid.NewGuid():n}.log");
        var payload = "2026-01-15 10:00:00 Welcome to City of Heroes, Example Hero!\r\n";
        File.WriteAllBytes(path, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(payload));
        return path;
    }

    private static string CreateMidSessionSource()
    {
        var path = Path.Combine(Path.GetTempPath(), $"replay-mid-session-{Guid.NewGuid():n}.log");
        var payload = "2026-01-15 10:00:01 You are now leaving the example district.\r\n";
        File.WriteAllBytes(path, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(payload));
        return path;
    }

    private static string CreateTempSource(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"replay-source-{Guid.NewGuid():n}.log");
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var payload = string.Join("\r\n", lines) + "\r\n";
        File.WriteAllBytes(path, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(payload));
        return path;
    }
}
