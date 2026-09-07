using CoHAnalytics.Models;
using CoHAnalytics.Services;
using CoHAnalytics.Tests.Orchestration;

namespace CoHAnalytics.Tests.Services;

public sealed class GameplaySessionTelemetryRatesTests
{
  private static readonly DateTimeOffset SessionStart =
      new(2026, 8, 4, 12, 0, 0, TimeSpan.Zero);

  [Fact]
  public void Thirty_minute_session_xp_rate_uses_full_elapsed_time()
  {
    var elapsed = TimeSpan.FromMinutes(30);
    var rate = GameplaySessionTelemetryPresentation.CalculateRatePerHour(100_000, elapsed);
    Assert.Equal(200_000, rate);
    Assert.Equal("200K/hr", GameplaySessionTelemetryPresentation.FormatRatePerHour(100_000, elapsed));
  }

  [Fact]
  public void Sixty_minute_session_xp_rate_uses_full_elapsed_time()
  {
    var elapsed = TimeSpan.FromMinutes(60);
    var rate = GameplaySessionTelemetryPresentation.CalculateRatePerHour(100_000, elapsed);
    Assert.Equal(100_000, rate);
    Assert.Equal("100K/hr", GameplaySessionTelemetryPresentation.FormatRatePerHour(100_000, elapsed));
  }

  [Fact]
  public void Zero_xp_shows_zero_rate_after_minimum_duration()
  {
    var elapsed = TimeSpan.FromMinutes(30);
    Assert.Equal("0/hr", GameplaySessionTelemetryPresentation.FormatRatePerHour(0, elapsed));
  }

  [Fact]
  public void Zero_influence_shows_zero_rate_after_minimum_duration()
  {
    var elapsed = TimeSpan.FromMinutes(30);
    Assert.Equal("0/hr", GameplaySessionTelemetryPresentation.FormatRatePerHour(0, elapsed));
  }

  [Fact]
  public void Rates_show_placeholder_before_minimum_duration()
  {
    var elapsed = TimeSpan.FromSeconds(45);
    Assert.False(GameplaySessionTelemetryPresentation.CanShowRates(elapsed));
    Assert.Equal("—", GameplaySessionTelemetryPresentation.FormatRatePerHour(100_000, elapsed));
    Assert.Equal("—", GameplaySessionTelemetryPresentation.FormatRatePerHour(50_000, elapsed));
  }

  [Fact]
  public void Rates_show_after_one_minute_elapsed()
  {
    var elapsed = TimeSpan.FromMinutes(1);
    Assert.True(GameplaySessionTelemetryPresentation.CanShowRates(elapsed));
    Assert.Equal("60K/hr", GameplaySessionTelemetryPresentation.FormatRatePerHour(1_000, elapsed));
  }

  [Fact]
  public void Finalized_session_uses_fixed_end_timestamp()
  {
    var finalizedAt = SessionStart.AddMinutes(45);
    var elapsed = GameplaySessionTelemetryPresentation.GetElapsedDuration(
        SessionStart,
        finalizedAt.AddMinutes(10),
        finalizedAt);
    Assert.Equal(TimeSpan.FromMinutes(45), elapsed);
    Assert.Equal(
        "133K/hr",
        GameplaySessionTelemetryPresentation.FormatRatePerHour(100_000, elapsed));
    Assert.Equal("00:45:00", GameplaySessionTelemetryPresentation.FormatDuration(elapsed));
  }

  [Fact]
  public void Multi_context_timing_inputs_remain_isolated()
  {
    var contextAStart = SessionStart;
    var contextBStart = SessionStart.AddMinutes(20);
    var reference = SessionStart.AddMinutes(40);

    var elapsedA = GameplaySessionTelemetryPresentation.GetElapsedDuration(
        contextAStart,
        reference,
        timingEndAt: null);
    var elapsedB = GameplaySessionTelemetryPresentation.GetElapsedDuration(
        contextBStart,
        reference,
        timingEndAt: null);

    Assert.Equal(TimeSpan.FromMinutes(40), elapsedA);
    Assert.Equal(TimeSpan.FromMinutes(20), elapsedB);
    Assert.Equal(
        "150K/hr",
        GameplaySessionTelemetryPresentation.FormatRatePerHour(100_000, elapsedA));
    Assert.Equal(
        "150K/hr",
        GameplaySessionTelemetryPresentation.FormatRatePerHour(50_000, elapsedB));
  }
}

public sealed class GameplaySessionTelemetryDurationTests
{
  [Fact]
  public async Task Different_character_handoff_resets_started_at_totals_and_rates()
  {
    var monitoring = new FakeMonitoringSessionManager();
    var parser = new GameplaySessionTestInfrastructure.FakeGameplayParserManager();
    var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dir);
    var time = new ManualTimeProvider(new DateTimeOffset(2026, 8, 4, 12, 0, 0, TimeSpan.Zero));

    try
    {
      var contextId = MonitoringContextId.CreateNew();
      var source = GameplaySessionTestInfrastructure.DefaultSource();
      monitoring.SetInitial(ParserTestSnapshots.Snapshot(
          1,
          GameplaySessionTestInfrastructure.ReadyContext(contextId, source)));

      var manager = await GameplaySessionTestInfrastructure.CreateStartedManager(
          monitoring,
          parser,
          repository,
          new GameplaySessionOptions { TimeProvider = time });

      parser.PublishClassified([
          GameplaySessionTestInfrastructure.Classify(
              "2026-08-04 12:00:00 Welcome to City of Heroes, Example Hero!",
              contextId,
              source,
              sequence: 1,
              observedAt: time.GetUtcNow()),
          GameplaySessionTestInfrastructure.Classify(
              "2026-08-04 12:00:01 You gain 10,000 experience and 100 influence.",
              contextId,
              source,
              sequence: 2,
              observedAt: time.GetUtcNow().AddSeconds(1))
      ]);

      await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
          manager.Current.Sessions.Any(session =>
              session.ContextId == contextId
              && session.CharacterDisplayName == "Example Hero"
              && session.SessionExperienceGained == 10_000
              && session.SessionGameplayInfluenceGained == 100));

      var firstSession = manager.Current.Sessions[0];
      var firstStartedAt = firstSession.StartedAt;
      time.Advance(TimeSpan.FromMinutes(30));

      parser.PublishClassified([
          GameplaySessionTestInfrastructure.Classify(
              "2026-08-04 12:30:00 Welcome to City of Heroes, Another Hero!",
              contextId,
              source,
              sequence: 3,
              observedAt: time.GetUtcNow()),
          GameplaySessionTestInfrastructure.Classify(
              "2026-08-04 12:30:01 You gain 1,000 experience and 250 influence.",
              contextId,
              source,
              sequence: 4,
              observedAt: time.GetUtcNow().AddSeconds(1))
      ]);

      await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
          manager.Current.Sessions.Any(session =>
              session.SessionId != firstSession.SessionId
              && session.CharacterDisplayName == "Another Hero"
              && session.SessionExperienceGained == 1_000
              && session.SessionGameplayInfluenceGained == 250
              && session.StartedAt > firstStartedAt));

      var secondSession = manager.Current.Sessions[0];
      Assert.NotEqual(firstSession.SessionId, secondSession.SessionId);
      Assert.Equal(time.GetUtcNow(), secondSession.StartedAt);
      Assert.Equal(1_000, secondSession.SessionExperienceGained);
      Assert.Equal(250, secondSession.SessionGameplayInfluenceGained);
      var elapsedImmediately = GameplaySessionTelemetryPresentation.GetElapsedDuration(
          secondSession.StartedAt,
          time.GetUtcNow(),
          timingEndAt: null);
      Assert.Equal("—", GameplaySessionTelemetryPresentation.FormatRatePerHour(
          secondSession.SessionExperienceGained,
          elapsedImmediately));

      time.Advance(TimeSpan.FromMinutes(1));

      var elapsed = GameplaySessionTelemetryPresentation.GetElapsedDuration(
          secondSession.StartedAt,
          time.GetUtcNow(),
          timingEndAt: null);

      Assert.Equal(TimeSpan.FromMinutes(1), elapsed);
      Assert.Equal("60K/hr", GameplaySessionTelemetryPresentation.FormatRatePerHour(
          secondSession.SessionExperienceGained,
          elapsed));
      Assert.Equal("15K/hr", GameplaySessionTelemetryPresentation.FormatRatePerHour(
          secondSession.SessionGameplayInfluenceGained,
          elapsed));
      Assert.Equal(
          "20K/hr",
          GameplaySessionTelemetryPresentation.FormatRatePerHour(
              firstSession.SessionExperienceGained,
              GameplaySessionTelemetryPresentation.GetElapsedDuration(
                  firstStartedAt,
                  firstStartedAt.AddMinutes(30),
                  timingEndAt: null)));
      Assert.Equal(
          "200/hr",
          GameplaySessionTelemetryPresentation.FormatRatePerHour(
              firstSession.SessionGameplayInfluenceGained,
              GameplaySessionTelemetryPresentation.GetElapsedDuration(
                  firstStartedAt,
                  firstStartedAt.AddMinutes(30),
                  timingEndAt: null)));
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
  public async Task Multi_context_sessions_keep_isolated_session_start_times()
  {
    var monitoring = new FakeMonitoringSessionManager();
    var parser = new GameplaySessionTestInfrastructure.FakeGameplayParserManager();
    var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dir);
    var time = new ManualTimeProvider(new DateTimeOffset(2026, 8, 4, 12, 0, 0, TimeSpan.Zero));

    try
    {
      var contextA = MonitoringContextId.CreateNew();
      var contextB = MonitoringContextId.CreateNew();
      var sourceA = GameplaySessionTestInfrastructure.DefaultSource("acct-a");
      var sourceB = GameplaySessionTestInfrastructure.DefaultSource("acct-b");
      monitoring.SetInitial(ParserTestSnapshots.Snapshot(
          1,
          [
              GameplaySessionTestInfrastructure.ReadyContext(contextA, sourceA),
              GameplaySessionTestInfrastructure.ReadyContext(contextB, sourceB)
          ]));

      var manager = await GameplaySessionTestInfrastructure.CreateStartedManager(
          monitoring,
          parser,
          repository,
          new GameplaySessionOptions { TimeProvider = time });

      await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
          manager.Current.Sessions.Any(session =>
              session.ContextId == contextA
              && session.CharacterIdentityConfidence == CharacterIdentityConfidence.Unknown
              && session.CharacterIdentityResolutionState == CharacterIdentityResolutionState.Unresolved)
          && manager.Current.Sessions.Any(session =>
              session.ContextId == contextB
              && session.CharacterIdentityConfidence == CharacterIdentityConfidence.Unknown
              && session.CharacterIdentityResolutionState == CharacterIdentityResolutionState.Unresolved));

      var readySessionA = manager.Current.Sessions.Single(session => session.ContextId == contextA);
      var readySessionB = manager.Current.Sessions.Single(session => session.ContextId == contextB);

      parser.PublishClassified([
          GameplaySessionTestInfrastructure.Classify(
              "2026-08-04 12:00:00 Welcome to City of Heroes, Hero A!",
              contextA,
              sourceA,
              sequence: 1,
              observedAt: time.GetUtcNow())
      ]);

      await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
          manager.Current.Sessions.Any(session =>
              session.ContextId == contextA
              && session.CharacterRecordId is not null
              && session.CharacterDisplayName == "Hero A"));

      var resolvedSessionA = manager.Current.Sessions.Single(session => session.ContextId == contextA);
      var unresolvedSessionB = manager.Current.Sessions.Single(session => session.ContextId == contextB);
      Assert.NotEqual(readySessionA.SessionId, resolvedSessionA.SessionId);
      Assert.Equal(readySessionA.StartedAt, resolvedSessionA.StartedAt);
      Assert.Equal(readySessionB.SessionId, unresolvedSessionB.SessionId);
      Assert.Equal(readySessionB.StartedAt, unresolvedSessionB.StartedAt);

      time.Advance(TimeSpan.FromMinutes(15));

      parser.PublishClassified([
          GameplaySessionTestInfrastructure.Classify(
              "2026-08-04 12:15:00 Welcome to City of Heroes, Hero B!",
              contextB,
              sourceB,
              sequence: 2,
              observedAt: time.GetUtcNow())
      ]);

      await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
          manager.Current.Sessions.Any(session =>
              session.ContextId == contextB
              && session.CharacterRecordId is not null
              && session.CharacterDisplayName == "Hero B"));

      var resolvedSessionB = manager.Current.Sessions.Single(session => session.ContextId == contextB);
      var unchangedSessionA = manager.Current.Sessions.Single(session => session.ContextId == contextA);
      Assert.Equal(resolvedSessionA.SessionId, unchangedSessionA.SessionId);
      Assert.Equal(resolvedSessionA.StartedAt, unchangedSessionA.StartedAt);
      Assert.NotEqual(readySessionB.SessionId, resolvedSessionB.SessionId);
      Assert.Equal(time.GetUtcNow(), resolvedSessionB.StartedAt);

      time.Advance(TimeSpan.FromMinutes(15));

      var sessionA = manager.Current.Sessions.Single(session => session.ContextId == contextA);
      var sessionB = manager.Current.Sessions.Single(session => session.ContextId == contextB);
      Assert.Equal(resolvedSessionA.SessionId, sessionA.SessionId);
      Assert.Equal(resolvedSessionA.StartedAt, sessionA.StartedAt);
      Assert.Equal(resolvedSessionB.SessionId, sessionB.SessionId);
      Assert.Equal(resolvedSessionB.StartedAt, sessionB.StartedAt);
      var elapsedA = GameplaySessionTelemetryPresentation.GetElapsedDuration(
          sessionA.StartedAt,
          time.GetUtcNow(),
          timingEndAt: null);
      var elapsedB = GameplaySessionTelemetryPresentation.GetElapsedDuration(
          sessionB.StartedAt,
          time.GetUtcNow(),
          timingEndAt: null);

      Assert.Equal(TimeSpan.FromMinutes(30), elapsedA);
      Assert.Equal(TimeSpan.FromMinutes(15), elapsedB);
      Assert.NotEqual(sessionA.StartedAt, sessionB.StartedAt);
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
}
