using CoHAnalytics.Models;
using CoHAnalytics.Services;
using CoHAnalytics.Tests.Orchestration;

namespace CoHAnalytics.Tests.Services;

public sealed class GameplaySessionBadgeAcquisitionTests
{
    [Fact]
    public async Task Badge_award_persists_to_resolved_character_with_canonical_id()
    {
        var (manager, badgeRepo, characterRepo, contextId, source, parser, dir) =
            await CreateBadgeSessionStackAsync();

        try
        {
            var resolver = GameplaySessionTestInfrastructure.CreateProductionBadgeResolver();
            var resolved = resolver.Resolve("Defiler");
            Assert.Equal(AcquisitionIdentityResolutionState.Resolved, resolved.ResolutionState);

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-14 12:00:00 Welcome to City of Heroes, Example Hero!",
                    contextId,
                    source,
                    sequence: 1),
                GameplaySessionTestInfrastructure.Classify(
                    $"2026-08-14 12:00:01 {GameplaySessionTestInfrastructure.BadgeAwardLine("Defiler")}",
                    contextId,
                    source,
                    sequence: 2)
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
                manager.Current.Sessions.Any(session =>
                    session.RecentRewards.Any(entry =>
                        entry.Category == GameplaySessionRewardCategory.Badge
                        && entry.BadgeAcquisitionMetadata?.CatalogItemId == resolved.ResolvedCatalogItemId)));

            var record = characterRepo.TryFindTrustedByDisplayName("acct-1", "Example Hero")!;
            Assert.True(badgeRepo.IsBadgeAcquired(record.RecordId, resolved.ResolvedCatalogItemId!));
            Assert.Equal([resolved.ResolvedCatalogItemId!], badgeRepo.GetAcquiredBadgeIds(record.RecordId));
            var acquisition = Assert.Single(badgeRepo.GetSnapshot(record.RecordId).Acquisitions);
            Assert.Equal("Defiler", acquisition.ObservedTitle);
            Assert.Equal(CharacterBadgeAcquisitionProvenance.LogReceipt, acquisition.Provenance);
        }
        finally
        {
            await manager.StopAsync();
            TryDeleteDirectory(dir);
        }
    }

    [Fact]
    public async Task Unknown_badge_title_does_not_persist_completion()
    {
        var (manager, badgeRepo, characterRepo, contextId, source, parser, dir) =
            await CreateBadgeSessionStackAsync();

        try
        {
            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-14 12:00:00 Welcome to City of Heroes, Example Hero!",
                    contextId,
                    source,
                    sequence: 1),
                GameplaySessionTestInfrastructure.Classify(
                    $"2026-08-14 12:00:01 {GameplaySessionTestInfrastructure.BadgeAwardLine("Totally Unknown Badge Title")}",
                    contextId,
                    source,
                    sequence: 2)
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
                manager.Current.Sessions.Any(session =>
                    session.RecentRewards.Any(entry =>
                        entry.Category == GameplaySessionRewardCategory.Badge
                        && entry.BadgeAcquisitionMetadata is null)));

            var record = characterRepo.TryFindTrustedByDisplayName("acct-1", "Example Hero")!;
            Assert.Empty(badgeRepo.GetAcquiredBadgeIds(record.RecordId));
        }
        finally
        {
            await manager.StopAsync();
            TryDeleteDirectory(dir);
        }
    }

    [Fact]
    public async Task Pre_identity_badge_award_is_retained_then_committed_once_identity_resolves()
    {
        var (manager, badgeRepo, characterRepo, contextId, source, parser, dir) =
            await CreateBadgeSessionStackAsync();

        try
        {
            characterRepo.EstablishTrustedFromWelcome("acct-1", "Example Hero");
            characterRepo.EstablishTrustedFromManualConfirmation("acct-1", "Other Hero");
            var resolver = GameplaySessionTestInfrastructure.CreateProductionBadgeResolver();
            var resolved = resolver.Resolve("Nutrient-Rich");

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    $"2026-08-14 12:00:01 {GameplaySessionTestInfrastructure.BadgeAwardLine("Nutrient-Rich")}",
                    contextId,
                    source,
                    sequence: 1)
            ]);

            await GameplaySessionTestInfrastructure.WaitForWorkQueueToDrainAsync(manager);
            var provisionalSession = Assert.Single(
                manager.Current.Sessions,
                session => session.ContextId == contextId);
            Assert.Equal(1, provisionalSession.RetainedEventCount);

            var otherRecord = characterRepo.TryFindTrustedByDisplayName("acct-1", "Other Hero")!;
            Assert.True(manager.ConfirmCharacter(contextId, otherRecord.RecordId).IsSuccess);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
            {
                var record = characterRepo.TryFindTrustedByDisplayName("acct-1", "Other Hero");
                return record is not null
                    && badgeRepo.IsBadgeAcquired(record.RecordId, resolved.ResolvedCatalogItemId!);
            });

            var exampleRecord = characterRepo.TryFindTrustedByDisplayName("acct-1", "Example Hero")!;
            Assert.False(badgeRepo.IsBadgeAcquired(exampleRecord.RecordId, resolved.ResolvedCatalogItemId!));
        }
        finally
        {
            await manager.StopAsync();
            TryDeleteDirectory(dir);
        }
    }

    [Fact]
    public async Task Permanently_unresolved_identity_does_not_assign_badge_to_guessed_character()
    {
        var (manager, badgeRepo, _, contextId, source, parser, dir) =
            await CreateBadgeSessionStackAsync();

        try
        {
            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    $"2026-08-14 12:00:01 {GameplaySessionTestInfrastructure.BadgeAwardLine("Atlas Tour Guide")}",
                    contextId,
                    source,
                    sequence: 1)
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(
                () => manager.Current.Sessions.Any(session => session.RetainedEventCount == 1));

            Assert.Empty(badgeRepo.GetAcquiredBadgeIds(CharacterRecordId.CreateNew()));
            Assert.Equal(0, manager.Current.Sessions.Sum(session => session.RecentRewards.Count));
        }
        finally
        {
            await manager.StopAsync();
            TryDeleteDirectory(dir);
        }
    }

    [Fact]
    public async Task Character_isolation_on_same_account()
    {
        var (manager, badgeRepo, characterRepo, contextId, source, parser, dir) =
            await CreateBadgeSessionStackAsync();

        try
        {
            characterRepo.EstablishTrustedFromWelcome("acct-1", "Alpha Hero");
            characterRepo.EstablishTrustedFromWelcome("acct-1", "Beta Hero");
            var alpha = characterRepo.TryFindTrustedByDisplayName("acct-1", "Alpha Hero")!;
            var beta = characterRepo.TryFindTrustedByDisplayName("acct-1", "Beta Hero")!;
            var resolver = GameplaySessionTestInfrastructure.CreateProductionBadgeResolver();
            var badgeId = resolver.Resolve("Atlas Tour Guide").ResolvedCatalogItemId!;
            var badgeEventCommitted = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            manager.CommittedEventsAvailable += (_, args) =>
            {
                if (args.Events.Any(event_ =>
                        event_.ContextId == contextId
                        && event_.CharacterRecordId == alpha.RecordId
                        && event_.ParserEvent.Sequence == 2))
                {
                    badgeEventCommitted.TrySetResult();
                }
            };

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-14 12:00:00 Welcome to City of Heroes, Alpha Hero!",
                    contextId,
                    source,
                    sequence: 1),
                GameplaySessionTestInfrastructure.Classify(
                    $"2026-08-14 12:00:01 {GameplaySessionTestInfrastructure.BadgeAwardLine("Atlas Tour Guide")}",
                    contextId,
                    source,
                    sequence: 2)
            ]);

            await badgeEventCommitted.Task;

            Assert.True(badgeRepo.IsBadgeAcquired(alpha.RecordId, badgeId));
            Assert.False(badgeRepo.IsBadgeAcquired(beta.RecordId, badgeId));
        }
        finally
        {
            await manager.StopAsync();
            TryDeleteDirectory(dir);
        }
    }

    [Fact]
    public async Task Account_isolation()
    {
        var dataDir = CreateDataDirectory();
        var characterRepo = new CharacterRepository(new CharacterRepositoryOptions { DataDirectory = dataDir });
        var badgeRepo = new CharacterBadgeAcquisitionRepository(new CharacterBadgeAcquisitionRepositoryOptions
        {
            DataDirectory = dataDir
        });
        var resolver = GameplaySessionTestInfrastructure.CreateProductionBadgeResolver();
        var badgeId = resolver.Resolve("Atlas Tour Guide").ResolvedCatalogItemId!;

        var monitoring = new FakeMonitoringSessionManager();
        var parser = new GameplaySessionTestInfrastructure.FakeGameplayParserManager();
        var contextA = MonitoringContextId.CreateNew();
        var contextB = MonitoringContextId.CreateNew();
        var sourceA = LogSourceId.Create("acct-a", "acct-a", @"C:\fake\a.txt", new DateOnly(2026, 8, 14));
        var sourceB = LogSourceId.Create("acct-b", "acct-b", @"C:\fake\b.txt", new DateOnly(2026, 8, 14));

        monitoring.SetInitial(ParserTestSnapshots.Snapshot(
            1,
            GameplaySessionTestInfrastructure.ReadyContext(contextA, sourceA),
            GameplaySessionTestInfrastructure.ReadyContext(contextB, sourceB)));

        var manager = await GameplaySessionTestInfrastructure.CreateStartedManager(
            monitoring,
            parser,
            characterRepo,
            badgeAcquisitionResolver: resolver,
            badgeAcquisitionRepository: badgeRepo);

        try
        {
            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-14 12:00:00 Welcome to City of Heroes, Shared Name!",
                    contextA,
                    sourceA,
                    sequence: 1),
                GameplaySessionTestInfrastructure.Classify(
                    $"2026-08-14 12:00:01 {GameplaySessionTestInfrastructure.BadgeAwardLine("Atlas Tour Guide")}",
                    contextA,
                    sourceA,
                    sequence: 2),
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-14 12:00:00 Welcome to City of Heroes, Shared Name!",
                    contextB,
                    sourceB,
                    sequence: 1)
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
            {
                var recordA = characterRepo.TryFindTrustedByDisplayName("acct-a", "Shared Name");
                var recordB = characterRepo.TryFindTrustedByDisplayName("acct-b", "Shared Name");
                return recordA is not null
                    && recordB is not null
                    && badgeRepo.IsBadgeAcquired(recordA.RecordId, badgeId);
            });
            await GameplaySessionTestInfrastructure.WaitForWorkQueueToDrainAsync(manager);

            var recordA = characterRepo.TryFindTrustedByDisplayName("acct-a", "Shared Name")!;
            var recordB = characterRepo.TryFindTrustedByDisplayName("acct-b", "Shared Name")!;
            Assert.NotEqual(recordA.RecordId, recordB.RecordId);
            Assert.False(badgeRepo.IsBadgeAcquired(recordB.RecordId, badgeId));
        }
        finally
        {
            await manager.StopAsync();
            TryDeleteDirectory(dataDir);
        }
    }

    [Fact]
    public async Task Same_process_character_handoff_preserves_badge_isolation()
    {
        var processInstance = FakeGameRuntimeService.CreateClient(
            3_524,
            new DateTimeOffset(2026, 8, 14, 1, 15, 39, TimeSpan.Zero),
            @"C:\Games\Homecoming\bin\win64\live\cityofheroes.exe");
        var dataDir = CreateDataDirectory();
        var characterRepo = new CharacterRepository(new CharacterRepositoryOptions { DataDirectory = dataDir });
        var badgeRepo = new CharacterBadgeAcquisitionRepository(new CharacterBadgeAcquisitionRepositoryOptions
        {
            DataDirectory = dataDir
        });
        var resolver = GameplaySessionTestInfrastructure.CreateProductionBadgeResolver();
        var badgeId = resolver.Resolve("Atlas Tour Guide").ResolvedCatalogItemId!;

        var runtime = new FakeGameRuntimeService
        {
            CurrentStatus = GameRuntimeStatus.Running,
            RunningClients = [processInstance],
            RunningClientCount = 1
        };
        var logActivity = new FakeLogActivityService();
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero));
        var monitoring = new MonitoringSessionManager(runtime, logActivity, new MonitoringSessionManagerOptions
        {
            TimeProvider = time
        });
        var parser = new GameplaySessionTestInfrastructure.FakeGameplayParserManager();
        var gameplay = new GameplaySessionManager(
            monitoring,
            parser,
            characterRepo,
            new GameplaySessionOptions { TimeProvider = time },
            badgeAcquisitionResolver: resolver,
            badgeAcquisitionRepository: badgeRepo);

        await monitoring.StartAsync();
        await gameplay.StartAsync();

        try
        {
            var source = LogSourceId.Create("TestAccount", "TestAccount", @"C:\fake\chatlog.txt", new DateOnly(2026, 8, 14));
            logActivity.Current = TestLogCandidates.Snapshot(
                time.GetUtcNow(),
                TestLogCandidates.Create(source, LogSourceActivityState.Growing, time.GetUtcNow()));
            logActivity.RaiseActivityChanged();

            var contextId = Assert.Single(monitoring.Current.Contexts).ContextId;

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-14 12:00:00 Welcome to City of Heroes, Dawn's Vanguard!",
                    contextId,
                    source,
                    sequence: 1),
                GameplaySessionTestInfrastructure.Classify(
                    $"2026-08-14 12:00:01 {GameplaySessionTestInfrastructure.BadgeAwardLine("Atlas Tour Guide")}",
                    contextId,
                    source,
                    sequence: 2)
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
            {
                var primaryHero = characterRepo.TryFindTrustedByDisplayName("TestAccount", "Dawn's Vanguard");
                return primaryHero is not null && badgeRepo.IsBadgeAcquired(primaryHero.RecordId, badgeId);
            });

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-14 12:05:00 Welcome to City of Heroes, Alpha Hero!",
                    contextId,
                    source,
                    sequence: 3)
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
                gameplay.Current.Sessions.Any(session =>
                    string.Equals(session.CharacterDisplayName, "Alpha Hero", StringComparison.Ordinal)));

            var primaryHero = characterRepo.TryFindTrustedByDisplayName("TestAccount", "Dawn's Vanguard")!;
            var alphaHero = characterRepo.TryFindTrustedByDisplayName("TestAccount", "Alpha Hero")!;
            Assert.True(badgeRepo.IsBadgeAcquired(primaryHero.RecordId, badgeId));
            Assert.False(badgeRepo.IsBadgeAcquired(alphaHero.RecordId, badgeId));
        }
        finally
        {
            await gameplay.StopAsync();
            await monitoring.StopAsync();
            TryDeleteDirectory(dataDir);
        }
    }

    [Fact]
    public async Task Other_badge_categories_capture_accomplishment_and_history_and_accolade()
    {
        var (manager, badgeRepo, characterRepo, contextId, source, parser, dir) =
            await CreateBadgeSessionStackAsync();

        try
        {
            var resolver = GameplaySessionTestInfrastructure.CreateProductionBadgeResolver();
            var accomplishment = resolver.Resolve("Nutrient-Rich");
            var history = resolver.Resolve("Bicentennial");
            var accolade = resolver.Resolve("Master Plumber");

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-14 12:00:00 Welcome to City of Heroes, Example Hero!",
                    contextId,
                    source,
                    sequence: 1),
                GameplaySessionTestInfrastructure.Classify(
                    $"2026-08-14 12:00:01 {GameplaySessionTestInfrastructure.BadgeAwardLine("Nutrient-Rich")}",
                    contextId,
                    source,
                    sequence: 2),
                GameplaySessionTestInfrastructure.Classify(
                    $"2026-08-14 12:00:02 {GameplaySessionTestInfrastructure.BadgeAwardLine("Bicentennial")}",
                    contextId,
                    source,
                    sequence: 3),
                GameplaySessionTestInfrastructure.Classify(
                    $"2026-08-14 12:00:03 {GameplaySessionTestInfrastructure.BadgeAwardLine("Master Plumber")}",
                    contextId,
                    source,
                    sequence: 4)
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
            {
                var record = characterRepo.TryFindTrustedByDisplayName("acct-1", "Example Hero");
                return record is not null
                    && badgeRepo.IsBadgeAcquired(record.RecordId, accomplishment.ResolvedCatalogItemId!)
                    && badgeRepo.IsBadgeAcquired(record.RecordId, history.ResolvedCatalogItemId!)
                    && badgeRepo.IsBadgeAcquired(record.RecordId, accolade.ResolvedCatalogItemId!);
            });
        }
        finally
        {
            await manager.StopAsync();
            TryDeleteDirectory(dir);
        }
    }

    [Fact]
    public async Task Xp_reward_telemetry_regression_is_unchanged_when_badge_pipeline_enabled()
    {
        var (manager, _, _, contextId, source, parser, dir) =
            await CreateBadgeSessionStackAsync();

        try
        {
            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-14 12:00:00 Welcome to City of Heroes, Example Hero!",
                    contextId,
                    source,
                    sequence: 1),
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-14 12:00:01 You gain 900 experience and 100 influence.",
                    contextId,
                    source,
                    sequence: 2)
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
                manager.Current.Sessions.Any(session =>
                    session.SessionExperienceGained == 900
                    && session.SessionGameplayInfluenceGained == 100));
        }
        finally
        {
            await manager.StopAsync();
            TryDeleteDirectory(dir);
        }
    }

    private static async Task<(
        GameplaySessionManager Manager,
        CharacterBadgeAcquisitionRepository BadgeRepo,
        CharacterRepository CharacterRepo,
        MonitoringContextId ContextId,
        LogSourceId Source,
        GameplaySessionTestInfrastructure.FakeGameplayParserManager Parser,
        string DataDirectory)> CreateBadgeSessionStackAsync(string accountId = "acct-1")
    {
        var dataDir = CreateDataDirectory();
        var characterRepo = new CharacterRepository(new CharacterRepositoryOptions { DataDirectory = dataDir });
        var badgeRepo = new CharacterBadgeAcquisitionRepository(new CharacterBadgeAcquisitionRepositoryOptions
        {
            DataDirectory = dataDir
        });
        var monitoring = new FakeMonitoringSessionManager();
        var parser = new GameplaySessionTestInfrastructure.FakeGameplayParserManager();
        var contextId = MonitoringContextId.CreateNew();
        var source = GameplaySessionTestInfrastructure.DefaultSource(accountId);
        monitoring.SetInitial(ParserTestSnapshots.Snapshot(
            1,
            GameplaySessionTestInfrastructure.ReadyContext(contextId, source)));

        var manager = await GameplaySessionTestInfrastructure.CreateStartedManager(
            monitoring,
            parser,
            characterRepo,
            badgeAcquisitionResolver: GameplaySessionTestInfrastructure.CreateProductionBadgeResolver(),
            badgeAcquisitionRepository: badgeRepo);

        return (manager, badgeRepo, characterRepo, contextId, source, parser, dataDir);
    }

    private static string CreateDataDirectory()
    {
        var dataDir = Path.Combine(Path.GetTempPath(), "coh-badge-session", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dataDir);
        return dataDir;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch
        {
        }
    }
}
