using System.Collections.Concurrent;
using System.IO;
using System.Text;
using CoHAnalytics.Models;
using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Services;

public sealed class ParserStartupIdentityRecoveryTests
{
    private const string WelcomeLine = "[03:57] Welcome to City of Heroes, Dawn's Vanguard!\r\n";
    private const string GameplayLine = "[03:58] You gain 100 experience.\r\n";

    [Fact]
    public async Task App_before_game_recovers_welcome_written_before_attach()
    {
        using var directory = new ParserTestDirectory();
        var path = directory.CreateFile(content: Encoding.UTF8.GetBytes(WelcomeLine + GameplayLine));
        var source = ParserTestSnapshots.Source(path, accountId: "TestAccount");
        var contextId = MonitoringContextId.CreateNew();
        await using var worker = new ParserWorker(contextId, ParserTestSnapshots.FastOptions());
        var events = new ConcurrentQueue<ParserRawEvent>();
        worker.RawEventAvailable += (_, args) => events.Enqueue(args.ParserEvent);

        await worker.StartAsync();
        await worker.ApplyContextAsync(ParserTestSnapshots.Context(
            contextId,
            MonitoringContextState.Ready,
            source,
            1,
            MonitoringSourceTransitionKind.SourceAssigned));

        var recovered = Assert.Single(events);
        Assert.Contains("Welcome to City of Heroes, Dawn's Vanguard!", recovered.RawLine, StringComparison.Ordinal);
        Assert.Equal(new FileInfo(path).Length, worker.Current.CurrentSegment!.StartingOffset);

        directory.Append(path, "[03:59] You gain 50 experience.\r\n");
        await ParserTestSnapshots.WaitUntilAsync(() => events.Count == 2);
        Assert.Equal(2, events.Count);
    }

    [Fact]
    public async Task Attach_before_welcome_still_resolves_when_line_is_appended()
    {
        using var directory = new ParserTestDirectory();
        var path = directory.CreateFile();
        var source = ParserTestSnapshots.Source(path);
        var contextId = MonitoringContextId.CreateNew();
        await using var worker = new ParserWorker(contextId, ParserTestSnapshots.FastOptions());
        var events = new ConcurrentQueue<ParserRawEvent>();
        worker.RawEventAvailable += (_, args) => events.Enqueue(args.ParserEvent);

        await worker.StartAsync();
        await worker.ApplyContextAsync(ParserTestSnapshots.Context(
            contextId,
            MonitoringContextState.Ready,
            source,
            1,
            MonitoringSourceTransitionKind.SourceAssigned));

        Assert.Empty(events);
        Assert.Equal(0, worker.Current.CurrentSegment!.StartingOffset);

        directory.Append(path, WelcomeLine);
        await ParserTestSnapshots.WaitUntilAsync(() => events.Count == 1);
        Assert.Contains("Welcome to City of Heroes", Assert.Single(events).RawLine, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Boundary_welcome_is_not_processed_twice()
    {
        using var directory = new ParserTestDirectory();
        var path = directory.CreateFile(content: Encoding.UTF8.GetBytes(WelcomeLine));
        var source = ParserTestSnapshots.Source(path);
        var contextId = MonitoringContextId.CreateNew();
        await using var worker = new ParserWorker(contextId, ParserTestSnapshots.FastOptions());
        var events = new ConcurrentQueue<ParserRawEvent>();
        worker.RawEventAvailable += (_, args) => events.Enqueue(args.ParserEvent);

        await worker.StartAsync();
        await worker.ApplyContextAsync(ParserTestSnapshots.Context(
            contextId,
            MonitoringContextState.Ready,
            source,
            1,
            MonitoringSourceTransitionKind.SourceAssigned));

        await ParserTestSnapshots.WaitUntilAsync(() => events.Count == 1);
        Assert.Single(events);
        await Task.Delay(100);
        Assert.Single(events);
    }

    [Fact]
    public async Task Relaunch_boundary_rejects_historical_welcome_then_forward_welcome_resolves()
    {
        using var directory = new ParserTestDirectory();
        var path = directory.CreateFile(
            content: Encoding.UTF8.GetBytes(
                "[03:57] Welcome to City of Heroes, Scout!\r\n"));
        var runtimeBoundary = new FileInfo(path).Length;
        var source = ParserTestSnapshots.Source(path, accountId: "TestAccount");
        var contextId = MonitoringContextId.CreateNew();
        await using var worker = new ParserWorker(contextId, ParserTestSnapshots.FastOptions());
        var events = new ConcurrentQueue<ParserRawEvent>();
        worker.RawEventAvailable += (_, args) => events.Enqueue(args.ParserEvent);

        await worker.StartAsync();
        await worker.ApplyContextAsync(ParserTestSnapshots.Context(
            contextId,
            MonitoringContextState.Ready,
            source,
            1,
            MonitoringSourceTransitionKind.SourceAssigned,
            startupRecoveryStartOffset: runtimeBoundary));

        Assert.Empty(events);

        directory.Append(path, "[03:58] Welcome to City of Heroes, Dawn's Vanguard!\r\n");
        await ParserTestSnapshots.WaitUntilAsync(() => events.Count == 1);

        var forwardWelcome = Assert.Single(events);
        Assert.Contains("Dawn's Vanguard", forwardWelcome.RawLine, StringComparison.Ordinal);
        Assert.True(forwardWelcome.SourceByteStart >= runtimeBoundary);
    }

    [Fact]
    public async Task Historical_gameplay_in_pre_attach_content_is_not_replayed()
    {
        using var directory = new ParserTestDirectory();
        var historical = string.Join(
            string.Empty,
            Enumerable.Range(0, 20).Select(index => $"[03:{index:00}] You gain {index} experience.\r\n"));
        var content = historical + WelcomeLine;
        var path = directory.CreateFile(content: Encoding.UTF8.GetBytes(content));
        var source = ParserTestSnapshots.Source(path);
        var contextId = MonitoringContextId.CreateNew();
        await using var worker = new ParserWorker(contextId, ParserTestSnapshots.FastOptions());
        var events = new ConcurrentQueue<ParserRawEvent>();
        worker.RawEventAvailable += (_, args) => events.Enqueue(args.ParserEvent);

        await worker.StartAsync();
        await worker.ApplyContextAsync(ParserTestSnapshots.Context(
            contextId,
            MonitoringContextState.Ready,
            source,
            1,
            MonitoringSourceTransitionKind.SourceAssigned));

        var recovered = Assert.Single(events);
        Assert.Contains("Welcome to City of Heroes", recovered.RawLine, StringComparison.Ordinal);
        Assert.DoesNotContain("You gain", recovered.RawLine, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Welcome_just_before_attach_is_recovered_from_an_oversized_file()
    {
        using var directory = new ParserTestDirectory();
        var path = directory.CreateFile();
        File.WriteAllText(path, new string('x', 40 * 1024) + "\r\n" + WelcomeLine);
        var source = ParserTestSnapshots.Source(path);
        var contextId = MonitoringContextId.CreateNew();
        await using var worker = new ParserWorker(contextId, ParserTestSnapshots.FastOptions());
        var events = new ConcurrentQueue<ParserRawEvent>();
        worker.RawEventAvailable += (_, args) => events.Enqueue(args.ParserEvent);

        await worker.StartAsync();
        await worker.ApplyContextAsync(ParserTestSnapshots.Context(
            contextId,
            MonitoringContextState.Ready,
            source,
            1,
            MonitoringSourceTransitionKind.SourceAssigned));

        // File size is irrelevant; the welcome sits inside the window that ends at the attach
        // offset, so it is provably the current character.
        var recovered = Assert.Single(events);
        Assert.Contains("Dawn's Vanguard", recovered.RawLine, StringComparison.Ordinal);
        Assert.Equal(new FileInfo(path).Length, worker.Current.CurrentSegment!.StartingOffset);
    }

    [Fact]
    public async Task Welcome_far_before_eof_is_still_recovered()
    {
        using var directory = new ParserTestDirectory();
        var path = directory.CreateFile();
        File.WriteAllText(path, WelcomeLine + new string('x', 40 * 1024) + "\r\n");
        var source = ParserTestSnapshots.Source(path);
        var contextId = MonitoringContextId.CreateNew();
        await using var worker = new ParserWorker(contextId, ParserTestSnapshots.FastOptions());
        var events = new ConcurrentQueue<ParserRawEvent>();
        worker.RawEventAvailable += (_, args) => events.Enqueue(args.ParserEvent);

        await worker.StartAsync();
        await worker.ApplyContextAsync(ParserTestSnapshots.Context(
            contextId,
            MonitoringContextState.Ready,
            source,
            1,
            MonitoringSourceTransitionKind.SourceAssigned));

        var recovered = Assert.Single(events);
        Assert.Contains("Dawn's Vanguard", recovered.RawLine, StringComparison.Ordinal);
        Assert.Equal(new FileInfo(path).Length, worker.Current.CurrentSegment!.StartingOffset);
    }

    [Fact]
    public async Task Welcome_megabytes_before_eof_is_recovered_across_chunks()
    {
        using var directory = new ParserTestDirectory();
        var path = directory.CreateFile();
        var padding = new string('x', 2 * 1024 * 1024);
        File.WriteAllText(path, WelcomeLine + padding + "\r\n");
        var source = ParserTestSnapshots.Source(path);
        var contextId = MonitoringContextId.CreateNew();
        await using var worker = new ParserWorker(
            contextId,
            ParserTestSnapshots.FastOptions(readBufferSize: 4096));
        var events = new ConcurrentQueue<ParserRawEvent>();
        worker.RawEventAvailable += (_, args) => events.Enqueue(args.ParserEvent);

        await worker.StartAsync();
        await worker.ApplyContextAsync(ParserTestSnapshots.Context(
            contextId,
            MonitoringContextState.Ready,
            source,
            1,
            MonitoringSourceTransitionKind.SourceAssigned));

        var recovered = Assert.Single(events);
        Assert.Contains("Dawn's Vanguard", recovered.RawLine, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Log_without_welcome_stays_unresolved()
    {
        using var directory = new ParserTestDirectory();
        var path = directory.CreateFile(content: Encoding.UTF8.GetBytes(GameplayLine));
        var source = ParserTestSnapshots.Source(path);
        var contextId = MonitoringContextId.CreateNew();
        await using var worker = new ParserWorker(contextId, ParserTestSnapshots.FastOptions());
        var events = new ConcurrentQueue<ParserRawEvent>();
        worker.RawEventAvailable += (_, args) => events.Enqueue(args.ParserEvent);

        await worker.StartAsync();
        await worker.ApplyContextAsync(ParserTestSnapshots.Context(
            contextId,
            MonitoringContextState.Ready,
            source,
            1,
            MonitoringSourceTransitionKind.SourceAssigned));

        Assert.Empty(events);
    }

    [Fact]
    public async Task Cross_midnight_attach_recovers_from_only_the_validated_previous_day_source()
    {
        using var directory = new ParserTestDirectory();
        var predecessorPath = directory.CreateFile(
            "chatlog 2026-08-18.txt",
            Encoding.UTF8.GetBytes(
                "2026-08-18 19:54:25 Welcome to City of Heroes, Dawn's Vanguard!\r\n"));
        var currentPath = directory.CreateFile(
            "chatlog 2026-08-19.txt",
            Encoding.UTF8.GetBytes("2026-08-19 00:05:00 You gain 100 experience.\r\n"));
        var predecessor = ParserTestSnapshots.Source(
            predecessorPath,
            accountId: "TestAccount",
            logDate: new DateOnly(2026, 8, 18));
        var current = ParserTestSnapshots.Source(
            currentPath,
            accountId: "TestAccount",
            logDate: new DateOnly(2026, 8, 19));
        var process = FakeGameRuntimeService.CreateClient(
            7_476,
            new DateTimeOffset(2026, 8, 18, 19, 53, 56, TimeSpan.Zero));
        var contextId = MonitoringContextId.CreateNew();
        await using var worker = new ParserWorker(contextId, ParserTestSnapshots.FastOptions());
        var events = new ConcurrentQueue<ParserRawEvent>();
        worker.RawEventAvailable += (_, args) => events.Enqueue(args.ParserEvent);

        await worker.StartAsync();
        await worker.ApplyContextAsync(ParserTestSnapshots.Context(
            contextId,
            MonitoringContextState.Ready,
            current,
            1,
            MonitoringSourceTransitionKind.SourceAssigned,
            startupRecoveryPredecessorSource: predecessor,
            processInstance: process));

        var recovered = Assert.Single(events);
        Assert.Equal(predecessor, recovered.SourceId);
        Assert.Contains("Dawn's Vanguard", recovered.RawLine, StringComparison.Ordinal);
        Assert.Equal(new FileInfo(currentPath).Length, worker.Current.CurrentSegment!.StartingOffset);
    }

    [Fact]
    public async Task Current_file_welcome_before_boundary_blocks_predecessor_fallback()
    {
        using var directory = new ParserTestDirectory();
        var predecessorPath = directory.CreateFile(
            "chatlog 2026-08-18.txt",
            Encoding.UTF8.GetBytes(
                "2026-08-18 19:54:25 Welcome to City of Heroes, Dawn's Vanguard!\r\n"));
        var currentPath = directory.CreateFile(
            "chatlog 2026-08-19.txt",
            Encoding.UTF8.GetBytes(
                "2026-08-19 00:04:00 Welcome to City of Heroes, Scout!\r\n"));
        var currentBoundary = new FileInfo(currentPath).Length;
        var predecessor = ParserTestSnapshots.Source(
            predecessorPath,
            accountId: "TestAccount",
            logDate: new DateOnly(2026, 8, 18));
        var current = ParserTestSnapshots.Source(
            currentPath,
            accountId: "TestAccount",
            logDate: new DateOnly(2026, 8, 19));
        var process = FakeGameRuntimeService.CreateClient(
            7_476,
            new DateTimeOffset(2026, 8, 18, 19, 53, 56, TimeSpan.Zero));
        var contextId = MonitoringContextId.CreateNew();
        await using var worker = new ParserWorker(contextId, ParserTestSnapshots.FastOptions());
        var events = new ConcurrentQueue<ParserRawEvent>();
        worker.RawEventAvailable += (_, args) => events.Enqueue(args.ParserEvent);

        await worker.StartAsync();
        await worker.ApplyContextAsync(ParserTestSnapshots.Context(
            contextId,
            MonitoringContextState.Ready,
            current,
            1,
            MonitoringSourceTransitionKind.SourceAssigned,
            startupRecoveryStartOffset: currentBoundary,
            startupRecoveryPredecessorSource: predecessor,
            processInstance: process));

        Assert.Empty(events);
    }

    [Fact]
    public async Task Previous_day_welcome_before_process_start_is_rejected_without_searching_other_logs()
    {
        using var directory = new ParserTestDirectory();
        var predecessorPath = directory.CreateFile(
            "chatlog 2026-08-18.txt",
            Encoding.UTF8.GetBytes(
                "2026-08-18 18:00:00 Welcome to City of Heroes, Scout!\r\n"));
        var currentPath = directory.CreateFile("chatlog 2026-08-19.txt");
        var predecessor = ParserTestSnapshots.Source(
            predecessorPath,
            accountId: "TestAccount",
            logDate: new DateOnly(2026, 8, 18));
        var current = ParserTestSnapshots.Source(
            currentPath,
            accountId: "TestAccount",
            logDate: new DateOnly(2026, 8, 19));
        var process = FakeGameRuntimeService.CreateClient(
            7_476,
            new DateTimeOffset(2026, 8, 18, 19, 53, 56, TimeSpan.Zero));
        var contextId = MonitoringContextId.CreateNew();
        await using var worker = new ParserWorker(contextId, ParserTestSnapshots.FastOptions());
        var events = new ConcurrentQueue<ParserRawEvent>();
        worker.RawEventAvailable += (_, args) => events.Enqueue(args.ParserEvent);

        await worker.StartAsync();
        await worker.ApplyContextAsync(ParserTestSnapshots.Context(
            contextId,
            MonitoringContextState.Ready,
            current,
            1,
            MonitoringSourceTransitionKind.SourceAssigned,
            startupRecoveryPredecessorSource: predecessor,
            processInstance: process));

        Assert.Empty(events);
    }

    [Fact]
    public async Task Only_the_last_welcome_before_attach_is_recovered()
    {
        using var directory = new ParserTestDirectory();
        var path = directory.CreateFile();
        File.WriteAllText(
            path,
            "[05:33] Welcome to City of Heroes, Alpha Hero!\r\n"
            + "[05:34] You gain 10 experience.\r\n"
            + "[05:37] Welcome to City of Heroes, Dawn's Vanguard!\r\n");
        var source = ParserTestSnapshots.Source(path, accountId: "TestAccount");
        var contextId = MonitoringContextId.CreateNew();
        await using var worker = new ParserWorker(contextId, ParserTestSnapshots.FastOptions());
        var events = new ConcurrentQueue<ParserRawEvent>();
        worker.RawEventAvailable += (_, args) => events.Enqueue(args.ParserEvent);

        await worker.StartAsync();
        await worker.ApplyContextAsync(ParserTestSnapshots.Context(
            contextId,
            MonitoringContextState.Ready,
            source,
            1,
            MonitoringSourceTransitionKind.SourceAssigned));

        var recovered = Assert.Single(events);
        Assert.Contains("Dawn's Vanguard", recovered.RawLine, StringComparison.Ordinal);
        Assert.DoesNotContain("Alpha Hero", recovered.RawLine, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Startup_recovery_classifies_welcome_through_parser_manager()
    {
        using var directory = new ParserTestDirectory();
        var path = directory.CreateFile(content: Encoding.UTF8.GetBytes(WelcomeLine));
        var source = ParserTestSnapshots.Source(path, accountId: "TestAccount");
        var contextId = MonitoringContextId.CreateNew();
        var monitoring = new FakeMonitoringSessionManager();
        monitoring.SetInitial(ParserTestSnapshots.Snapshot(
            1,
            ParserTestSnapshots.Context(
                contextId,
                MonitoringContextState.Ready,
                source,
                1,
                MonitoringSourceTransitionKind.SourceAssigned)));
        await using var parser = new ParserManager(monitoring, ParserTestSnapshots.FastOptions());
        var classified = new ConcurrentQueue<ParserEvent>();
        parser.ClassifiedEventsAvailable += (_, args) =>
        {
            foreach (var parserEvent in args.Events)
            {
                classified.Enqueue(parserEvent);
            }
        };

        await parser.StartAsync();
        await ParserTestSnapshots.WaitUntilAsync(() => classified.Count == 1);

        var welcome = classified.Single();
        Assert.True(CharacterIdentityResolver.IsWelcomeEvidence(welcome));
        Assert.Equal("Dawn's Vanguard", CharacterIdentityResolver.GetStrongCandidateName(welcome));
    }

    [Fact]
    public async Task Startup_recovery_resolves_identity_through_gameplay_session_pipeline()
    {
        var monitoring = new FakeMonitoringSessionManager();
        var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dir);

        try
        {
            using var directory = new ParserTestDirectory();
            var path = directory.CreateFile(content: Encoding.UTF8.GetBytes(WelcomeLine));
            var source = ParserTestSnapshots.Source(path, accountId: "TestAccount");
            var contextId = MonitoringContextId.CreateNew();
            monitoring.SetInitial(ParserTestSnapshots.Snapshot(
                1,
                GameplaySessionTestInfrastructure.ReadyContext(contextId, source)));

            await using var parser = new ParserManager(monitoring, ParserTestSnapshots.FastOptions());
            var manager = new GameplaySessionManager(monitoring, parser, repository);
            await manager.StartAsync();
            await parser.StartAsync();

            await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
                manager.Current.Sessions.Any(session =>
                    session.CharacterIdentityConfidence == CharacterIdentityConfidence.Confirmed
                    && session.CharacterDisplayName == "Dawn's Vanguard"));

            directory.Append(path, "[04:00] You gain 25 experience.\r\n");
            await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
                manager.Current.Sessions[0].SessionExperienceGained == 25);
        }
        finally
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch
            {
            }
        }
    }

    [Fact]
    public async Task Attaching_mid_session_recovers_most_recent_welcome_not_earlier_character()
    {
        var monitoring = new FakeMonitoringSessionManager();
        var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dir);

        try
        {
            using var directory = new ParserTestDirectory();
            var path = directory.CreateFile();

            File.WriteAllText(
                path,
                "[05:33] Welcome to City of Heroes, Alpha Hero!\r\n"
                + "[05:37] Welcome to City of Heroes, Dawn's Vanguard!\r\n"
                + string.Concat(Enumerable.Repeat("[05:40] You gain 1 experience.\r\n", 2000)));

            var source = ParserTestSnapshots.Source(path, accountId: "TestAccount");
            var contextId = MonitoringContextId.CreateNew();
            monitoring.SetInitial(ParserTestSnapshots.Snapshot(
                1,
                GameplaySessionTestInfrastructure.ReadyContext(contextId, source)));

            repository.EstablishTrustedFromWelcome("TestAccount", "Alpha Hero");
            repository.EstablishTrustedFromWelcome("TestAccount", "Dawn's Vanguard");

            await using var parser = new ParserManager(monitoring, ParserTestSnapshots.FastOptions());
            var manager = new GameplaySessionManager(monitoring, parser, repository);
            await manager.StartAsync();
            await parser.StartAsync();

            await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
                manager.Current.Sessions.Any(session =>
                    session.CharacterIdentityConfidence == CharacterIdentityConfidence.Confirmed
                    && session.CharacterDisplayName == "Dawn's Vanguard"));

            Assert.DoesNotContain(
                manager.Current.Sessions,
                session => session.CharacterDisplayName == "Alpha Hero");

            directory.Append(path, "[06:00] You gain 25 experience.\r\n");
            await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
                manager.Current.Sessions[0].SessionExperienceGained == 25);
        }
        finally
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch
            {
            }
        }
    }

    [Fact]
    public async Task Multiple_contexts_recover_independent_welcome_identities()
    {
        const string welcomeA = "[03:57] Welcome to City of Heroes, Dawn's Vanguard!\r\n";
        const string welcomeB = "[03:57] Welcome to City of Heroes, Fire Farmer!\r\n";

        using var directory = new ParserTestDirectory();
        var pathA = directory.CreateFile("a.log", content: Encoding.UTF8.GetBytes(welcomeA));
        var pathB = directory.CreateFile("b.log", content: Encoding.UTF8.GetBytes(welcomeB));
        var sourceA = ParserTestSnapshots.Source(pathA, accountId: "acct-1");
        var sourceB = ParserTestSnapshots.Source(pathB, accountId: "acct-2");
        var contextA = MonitoringContextId.CreateNew();
        var contextB = MonitoringContextId.CreateNew();
        var monitoring = new FakeMonitoringSessionManager();
        monitoring.SetInitial(ParserTestSnapshots.Snapshot(
            1,
            ParserTestSnapshots.Context(contextA, MonitoringContextState.Ready, sourceA, 1, MonitoringSourceTransitionKind.SourceAssigned),
            ParserTestSnapshots.Context(contextB, MonitoringContextState.Ready, sourceB, 1, MonitoringSourceTransitionKind.SourceAssigned)));
        await using var parser = new ParserManager(monitoring, ParserTestSnapshots.FastOptions());
        var classified = new ConcurrentQueue<ParserEvent>();
        parser.ClassifiedEventsAvailable += (_, args) =>
        {
            foreach (var parserEvent in args.Events)
            {
                classified.Enqueue(parserEvent);
            }
        };

        await parser.StartAsync();
        await ParserTestSnapshots.WaitUntilAsync(() => classified.Count == 2);

        var welcomes = classified.Where(CharacterIdentityResolver.IsWelcomeEvidence).ToArray();
        Assert.Equal(2, welcomes.Length);
        Assert.Contains(welcomes, item =>
            item.ContextId == contextA
            && CharacterIdentityResolver.GetStrongCandidateName(item) == "Dawn's Vanguard");
        Assert.Contains(welcomes, item =>
            item.ContextId == contextB
            && CharacterIdentityResolver.GetStrongCandidateName(item) == "Fire Farmer");
    }
}
