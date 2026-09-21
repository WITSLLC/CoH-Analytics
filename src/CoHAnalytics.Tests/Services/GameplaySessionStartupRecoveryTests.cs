using System.IO;
using System.Text;
using CoHAnalytics.Models;
using CoHAnalytics.Services;
using CoHAnalytics.Tests.Orchestration;

namespace CoHAnalytics.Tests.Services;

public sealed class GameplaySessionStartupRecoveryTests
{
  private const string WelcomeLine =
      "[03:57] Welcome to City of Heroes, Dawn's Vanguard!\r\n";

  [Fact]
  public async Task Ready_monitoring_without_parser_events_recovers_welcome_and_active_session()
  {
    using var directory = new ParserTestDirectory();
    var path = directory.CreateFile(content: Encoding.UTF8.GetBytes(WelcomeLine));
    var monitoring = new FakeMonitoringSessionManager();
    var parser = new GameplaySessionTestInfrastructure.FakeGameplayParserManager();
    var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dataDir);
    GameplaySessionManager? manager = null;

    try
    {
      var contextId = MonitoringContextId.CreateNew();
      var source = ParserTestSnapshots.Source(path, accountId: "TestAccount");
      monitoring.SetInitial(ParserTestSnapshots.Snapshot(
          1,
          GameplaySessionTestInfrastructure.ReadyContext(contextId, source)));
      manager = new GameplaySessionManager(monitoring, parser, repository);

      await manager.StartAsync();
      await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
          manager.Current.Sessions.Any(session =>
              session.ContextId == contextId
              && session.LifecycleState == GameplaySessionLifecycleState.Active
              && session.CharacterDisplayName == "Dawn's Vanguard"));

      var session = manager.Current.Sessions.Single(session => session.ContextId == contextId);
      Assert.Equal(CharacterIdentityResolutionState.Resolved, session.CharacterIdentityResolutionState);
      Assert.NotNull(session.CharacterRecordId);
    }
    finally
    {
      if (manager is not null)
      {
        await manager.StopAsync();
      }
      TryDeleteDirectory(dataDir);
    }
  }

  [Fact]
  public async Task Ready_monitoring_without_welcome_establishes_provisional_session_for_manual_selection()
  {
    using var directory = new ParserTestDirectory();
    var path = directory.CreateFile(content: Encoding.UTF8.GetBytes("[03:58] You gain 10 experience.\r\n"));
    var monitoring = new FakeMonitoringSessionManager();
    var parser = new GameplaySessionTestInfrastructure.FakeGameplayParserManager();
    var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dataDir);
    repository.EstablishTrustedFromWelcome("TestAccount", "Dawn's Vanguard");
    GameplaySessionManager? manager = null;
    GameplaySessionIdentityReadService? identityRead = null;

    try
    {
      var contextId = MonitoringContextId.CreateNew();
      var source = ParserTestSnapshots.Source(path, accountId: "TestAccount");
      monitoring.SetInitial(ParserTestSnapshots.Snapshot(
          1,
          GameplaySessionTestInfrastructure.ReadyContext(contextId, source)));
      manager = new GameplaySessionManager(monitoring, parser, repository);
      identityRead = new GameplaySessionIdentityReadService(manager, monitoring, repository);

      await manager.StartAsync();
      await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
          manager.Current.Sessions.Any(session =>
              session.ContextId == contextId
              && session.LifecycleState == GameplaySessionLifecycleState.Active));

      var readContext = identityRead.Current.Contexts.Single(context => context.ContextId == contextId);
      Assert.Equal("TestAccount", readContext.AccountStableId);
      Assert.True(readContext.HasActiveSession);
      Assert.Null(readContext.CharacterRecordId);
      Assert.True(readContext.RequiresManualSelection);
      Assert.NotEmpty(readContext.PickerCharacters);
    }
    finally
    {
      if (manager is not null)
      {
        await manager.StopAsync();
      }

      identityRead?.Dispose();
      TryDeleteDirectory(dataDir);
    }
  }

  [Fact]
  public async Task Manual_confirmation_from_provisional_session_resolves_character()
  {
    using var directory = new ParserTestDirectory();
    var path = directory.CreateFile(content: Encoding.UTF8.GetBytes("[03:58] You gain 10 experience.\r\n"));
    var monitoring = new FakeMonitoringSessionManager();
    var parser = new GameplaySessionTestInfrastructure.FakeGameplayParserManager();
    var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dataDir);
    var trusted = repository.EstablishTrustedFromWelcome("TestAccount", "Dawn's Vanguard");
    GameplaySessionManager? manager = null;

    try
    {
      var contextId = MonitoringContextId.CreateNew();
      var source = ParserTestSnapshots.Source(path, accountId: "TestAccount");
      monitoring.SetInitial(ParserTestSnapshots.Snapshot(
          1,
          GameplaySessionTestInfrastructure.ReadyContext(contextId, source)));
      manager = new GameplaySessionManager(monitoring, parser, repository);

      await manager.StartAsync();
      await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
          manager.Current.Sessions.Any(session => session.ContextId == contextId));

      var result = manager.ConfirmCharacter(contextId, trusted.RecordId!);
      Assert.True(result.IsSuccess);

      var session = manager.Current.Sessions.Single(session => session.ContextId == contextId);
      Assert.Equal(trusted.RecordId, session.CharacterRecordId);
      Assert.Equal(CharacterIdentityResolutionState.Resolved, session.CharacterIdentityResolutionState);
    }
    finally
    {
      if (manager is not null)
      {
        await manager.StopAsync();
      }
      TryDeleteDirectory(dataDir);
    }
  }

  [Fact]
  public async Task Ready_monitoring_after_midnight_recovers_current_runtime_welcome_from_predecessor()
  {
    using var directory = new ParserTestDirectory();
    var predecessorPath = directory.CreateFile(
        "chatlog 2026-08-18.txt",
        Encoding.UTF8.GetBytes(
            "2026-08-18 19:54:25 Welcome to City of Heroes, Dawn's Vanguard!\r\n"));
    var currentPath = directory.CreateFile(
        "chatlog 2026-08-19.txt",
        Encoding.UTF8.GetBytes("2026-08-19 00:05:00 You gain 10 experience.\r\n"));
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
    var monitoring = new FakeMonitoringSessionManager();
    var parser = new GameplaySessionTestInfrastructure.FakeGameplayParserManager();
    var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dataDir);
    GameplaySessionManager? manager = null;

    try
    {
      var contextId = MonitoringContextId.CreateNew();
      monitoring.SetInitial(ParserTestSnapshots.Snapshot(
          1,
          GameplaySessionTestInfrastructure.ReadyContext(
              contextId,
              current,
              startupRecoveryPredecessorSource: predecessor,
              processInstance: process)));
      manager = new GameplaySessionManager(monitoring, parser, repository);

      await manager.StartAsync();
      await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
          manager.Current.Sessions.Any(session =>
              session.ContextId == contextId
              && session.CharacterDisplayName == "Dawn's Vanguard"));

      var session = manager.Current.Sessions.Single(session => session.ContextId == contextId);
      Assert.Equal(CharacterIdentityResolutionState.Resolved, session.CharacterIdentityResolutionState);
      Assert.Contains(
          manager.GetDiagnostics().RecentOperations,
          operation => operation.Contains("Startup predecessor welcome recovery", StringComparison.Ordinal));
    }
    finally
    {
      if (manager is not null)
      {
        await manager.StopAsync();
      }
      TryDeleteDirectory(dataDir);
    }
  }

  [Fact]
  public async Task First_post_establishment_activity_retries_current_welcome_recovery_once()
  {
    using var directory = new ParserTestDirectory();
    var path = directory.CreateFile(
        content: Encoding.UTF8.GetBytes(
            "[03:57] Welcome to City of Heroes, Scout!\r\n"));
    var runtimeBoundary = new FileInfo(path).Length;
    var monitoring = new FakeMonitoringSessionManager();
    var parser = new GameplaySessionTestInfrastructure.FakeGameplayParserManager();
    var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dataDir);
    GameplaySessionManager? manager = null;

    try
    {
      var contextId = MonitoringContextId.CreateNew();
      var source = ParserTestSnapshots.Source(path, accountId: "TestAccount");
      monitoring.SetInitial(ParserTestSnapshots.Snapshot(
          1,
          GameplaySessionTestInfrastructure.ReadyContext(
              contextId,
              source,
              startupRecoveryStartOffset: runtimeBoundary)));
      manager = new GameplaySessionManager(monitoring, parser, repository);

      await manager.StartAsync();
      await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
          manager.Current.Sessions.Any(session =>
              session.ContextId == contextId
              && session.CharacterIdentityResolutionState == CharacterIdentityResolutionState.Unresolved));

      directory.Append(path, "[03:58] Welcome to City of Heroes, Dawn's Vanguard!\r\n");
      directory.Append(path, "[03:59] You gain 123 experience.\r\n");
      parser.PublishClassified([
          GameplaySessionTestInfrastructure.Classify(
              "[03:59] You gain 123 experience.",
              contextId,
              source,
              sequence: 2)
      ]);

      await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
          manager.Current.Sessions.Any(session =>
              session.CharacterDisplayName == "Dawn's Vanguard"
              && session.SessionExperienceGained == 123));

      Assert.Single(
          manager.GetDiagnostics().RecentOperations,
          operation => operation.Contains("Activity-triggered welcome recovery", StringComparison.Ordinal));
    }
    finally
    {
      if (manager is not null)
      {
        await manager.StopAsync();
      }
      TryDeleteDirectory(dataDir);
    }
  }

  [Fact]
  public async Task Welcome_far_from_eof_still_recovers_on_ready_monitoring()
  {
    using var directory = new ParserTestDirectory();
    var path = directory.CreateFile();
    File.WriteAllText(path, WelcomeLine + new string('x', 64 * 1024) + "\r\n");
    var monitoring = new FakeMonitoringSessionManager();
    var parser = new GameplaySessionTestInfrastructure.FakeGameplayParserManager();
    var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dataDir);
    GameplaySessionManager? manager = null;

    try
    {
      var contextId = MonitoringContextId.CreateNew();
      var source = ParserTestSnapshots.Source(path, accountId: "TestAccount");
      monitoring.SetInitial(ParserTestSnapshots.Snapshot(
          1,
          GameplaySessionTestInfrastructure.ReadyContext(contextId, source)));
      manager = new GameplaySessionManager(monitoring, parser, repository);

      await manager.StartAsync();
      await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
          manager.Current.Sessions.Any(session =>
              session.CharacterDisplayName == "Dawn's Vanguard"));

      Assert.Contains(
          manager.Current.Sessions,
          session => session.CharacterDisplayName == "Dawn's Vanguard");
    }
    finally
    {
      if (manager is not null)
      {
        await manager.StopAsync();
      }
      TryDeleteDirectory(dataDir);
    }
  }

    [Fact]
    public async Task Runtime_generation_reset_after_startup_recovery_reestablishes_active_session()
    {
        using var directory = new ParserTestDirectory();
        var path = directory.CreateFile(content: Encoding.UTF8.GetBytes(WelcomeLine));
        var runtime = new FakeGameRuntimeService
        {
            CurrentStatus = GameRuntimeStatus.Running,
            RunningClientCount = 1
        };
        var logActivity = new FakeLogActivityService();
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero));
        var source = ParserTestSnapshots.Source(path, accountId: "TestAccount");
        logActivity.Current = TestLogCandidates.Snapshot(
            time.GetUtcNow(),
            TestLogCandidates.Create(source, LogSourceActivityState.Growing, time.GetUtcNow()));

        using var monitoring = new MonitoringSessionManager(
            runtime,
            logActivity,
            new MonitoringSessionManagerOptions { TimeProvider = time });
        var parser = new GameplaySessionTestInfrastructure.FakeGameplayParserManager();
        var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dataDir);
        GameplaySessionManager? manager = null;

        try
        {
            var startA = new DateTimeOffset(2026, 8, 14, 1, 15, 39, TimeSpan.Zero);
            var startB = new DateTimeOffset(2026, 8, 14, 2, 15, 39, TimeSpan.Zero);
            var clientA = FakeGameRuntimeService.CreateClient(3_524, startA);
            runtime.RaiseStatusChanged(GameRuntimeStatus.Unconfigured, GameRuntimeStatus.Running, [clientA]);

            await monitoring.StartAsync();
            manager = new GameplaySessionManager(monitoring, parser, repository);
            using var generation = new LiveRuntimeGenerationService(
                runtime,
                monitoring,
                manager,
                new RecordingViewedContextService());

            await manager.StartAsync();
            var contextId = Assert.Single(monitoring.Current.Contexts).ContextId;
            await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
                manager.Current.Sessions.Any(session =>
                    session.ContextId == contextId
                    && session.LifecycleState == GameplaySessionLifecycleState.Active
                    && session.CharacterDisplayName == "Dawn's Vanguard"));

            var originalSessionId = manager.Current.Sessions.Single(session =>
                session.ContextId == contextId
                && session.CharacterDisplayName == "Dawn's Vanguard").SessionId;
            var replacementObserved = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);

            bool IsRecoveredReplacement(GameplaySessionManagerSnapshot snapshot) =>
                snapshot.Sessions.Any(session =>
                    session.ContextId != contextId
                    && session.SessionId != originalSessionId
                    && session.LifecycleState == GameplaySessionLifecycleState.Active
                    && session.CharacterIdentityResolutionState == CharacterIdentityResolutionState.Resolved
                    && session.CharacterDisplayName == "Dawn's Vanguard");

            EventHandler<GameplaySessionManagerChangedEventArgs> replacementHandler = (_, args) =>
            {
                if (IsRecoveredReplacement(args.Snapshot))
                {
                    replacementObserved.TrySetResult();
                }
            };

            manager.StateChanged += replacementHandler;
            try
            {
                runtime.RaiseStatusChanged(
                    GameRuntimeStatus.Running,
                    GameRuntimeStatus.Running,
                    [FakeGameRuntimeService.CreateClient(3_524, startB)]);

                if (IsRecoveredReplacement(manager.Current))
                {
                    replacementObserved.TrySetResult();
                }

                await replacementObserved.Task;
            }
            finally
            {
                manager.StateChanged -= replacementHandler;
            }

            var session = manager.Current.Sessions.Single(session =>
                session.CharacterDisplayName == "Dawn's Vanguard");
            Assert.NotEqual(contextId, session.ContextId);
            Assert.NotEqual(originalSessionId, session.SessionId);
            Assert.Equal("Dawn's Vanguard", session.CharacterDisplayName);
            Assert.Equal(CharacterIdentityResolutionState.Resolved, session.CharacterIdentityResolutionState);
        }
        finally
        {
            if (manager is not null)
            {
                await manager.StopAsync();
            }

            TryDeleteDirectory(dataDir);
        }
    }

    [Fact]
    public async Task Badge_acquisition_after_startup_recovery_uses_resolved_character()
  {
    using var directory = new ParserTestDirectory();
    var path = directory.CreateFile(content: Encoding.UTF8.GetBytes(WelcomeLine));
    var monitoring = new FakeMonitoringSessionManager();
    var parser = new GameplaySessionTestInfrastructure.FakeGameplayParserManager();
    var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dataDir);
    var badgeRepo = new CharacterBadgeAcquisitionRepository(
        new CharacterBadgeAcquisitionRepositoryOptions { DataDirectory = dataDir });
    var resolver = GameplaySessionTestInfrastructure.CreateProductionBadgeResolver();
    var badgeId = resolver.Resolve("Atlas Tour Guide").ResolvedCatalogItemId!;
    GameplaySessionManager? manager = null;

    try
    {
      var contextId = MonitoringContextId.CreateNew();
      var source = ParserTestSnapshots.Source(path, accountId: "TestAccount");
      monitoring.SetInitial(ParserTestSnapshots.Snapshot(
          1,
          GameplaySessionTestInfrastructure.ReadyContext(contextId, source)));
      manager = new GameplaySessionManager(
          monitoring,
          parser,
          repository,
          badgeAcquisitionResolver: resolver,
          badgeAcquisitionRepository: badgeRepo);

      await manager.StartAsync();
      await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
          manager.Current.Sessions.Any(session =>
              session.CharacterDisplayName == "Dawn's Vanguard"));

      parser.PublishClassified([
        GameplaySessionTestInfrastructure.Classify(
            $"2026-08-14 12:00:01 {GameplaySessionTestInfrastructure.BadgeAwardLine("Atlas Tour Guide")}",
            contextId,
            source,
            sequence: 2)
      ]);

      await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
      {
        var primaryHero = repository.TryFindTrustedByDisplayName("TestAccount", "Dawn's Vanguard");
        return primaryHero is not null && badgeRepo.IsBadgeAcquired(primaryHero.RecordId, badgeId);
      });
    }
    finally
    {
      if (manager is not null)
      {
        await manager.StopAsync();
      }
      TryDeleteDirectory(dataDir);
    }
  }

    private sealed class RecordingViewedContextService : IViewedContextService
    {
        public ViewedContextState Current { get; private set; } =
            TestGameplaySessionContextSupport.FollowingLive();

        public event EventHandler<ViewedContextChangedEventArgs>? Changed
        {
            add { }
            remove { }
        }

        public void SelectViewedAccount(string accountStableId) =>
            throw new NotSupportedException();

        public void SelectViewedCharacter(string accountStableId, CharacterRecordId characterRecordId) =>
            throw new NotSupportedException();

        public void ReturnToLive() => throw new NotSupportedException();

        public void SelectGameplaySessionContext(MonitoringContextId contextId) =>
            throw new NotSupportedException();

        public void ResetForNewRuntimeGeneration()
        {
            Current = TestGameplaySessionContextSupport.FollowingLive();
        }
    }

    private static void TryDeleteDirectory(string path)
  {
    try
    {
      if (Directory.Exists(path))
      {
        Directory.Delete(path, recursive: true);
      }
    }
    catch
    {
    }
  }
}
