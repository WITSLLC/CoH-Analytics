using CoHAnalytics.Models;
using CoHAnalytics.Services;
using CoHAnalytics.Tests.Orchestration;

namespace CoHAnalytics.Tests.Services;

public sealed class GameplaySessionHistoricalPerformanceTests
{
    private static readonly DateTimeOffset Start =
        new(2026, 8, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Orderly_stop_persists_one_complete_terminal_observation()
    {
        using var fixture = await Fixture.CreateAsync();
        var resolved = await fixture.ResolveAsync(0, "Terminal Hero");

        fixture.Parser.PublishClassified([
            fixture.Event(0, "You gain 1,234 experience and 567 influence.", 2, Start.AddSeconds(1)),
            fixture.Event(0, "You hit Lusca with your Hot Feet for 13.88 points of Fire damage.", 3, Start.AddSeconds(2)),
            fixture.Event(0, "You have defeated Sprocket", 4, Start.AddSeconds(3)),
            fixture.Event(0, "Psiche has defeated Prototype Oscillator", 5, Start.AddSeconds(4))
        ]);
        await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
            fixture.Manager.Current.Sessions.Any(session =>
                session.SessionExperienceGained == 1234
                && session.Combat.DamageDealt == new CombatScaledAmount(1388)
                && session.Combat.TotalDefeated == 2
                && session.Combat.MyDefeats == 1));
        var live = Assert.Single(fixture.Manager.Current.Sessions);

        fixture.Time.Advance(TimeSpan.FromMinutes(10));
        await fixture.Manager.StopAsync();
        await fixture.Manager.StopAsync();

        var observation = Assert.Single(fixture.History.Durable);
        Assert.Equal(live.SessionId, observation.GameplaySessionId);
        Assert.Equal(resolved, observation.CharacterRecordId);
        Assert.Equal(0, observation.SegmentOrdinal);
        Assert.Equal(Start, observation.StartedAtUtc);
        Assert.Equal(Start.AddMinutes(10), observation.EndedAtUtc);
        Assert.Equal(live.Combat.DamageDealt, observation.DamageDealt);
        Assert.Equal(live.Combat.Accuracy.Attempts, observation.Attempts);
        Assert.Equal(live.Combat.Accuracy.Hits, observation.Hits);
        Assert.Equal(live.Combat.Accuracy.RolledAttempts, observation.RolledAttempts);
        Assert.Equal(
            live.Combat.Accuracy.DisplayedChanceSumHundredths,
            observation.DisplayedChanceSumHundredths);
        Assert.Equal(live.Combat.Accuracy.RollSumHundredths, observation.RollSumHundredths);
        Assert.Equal(live.Combat.Accuracy.ForcedHits, observation.ForcedHits);
        Assert.Equal(live.Combat.Accuracy.Autohits, observation.Autohits);
        Assert.Equal(live.Combat.TotalDefeated, observation.TotalDefeated);
        Assert.Equal(live.Combat.MyDefeats, observation.MyDefeats);
        Assert.Equal(1234, observation.ExperienceGained);
        Assert.Equal(567, observation.GameplayInfluenceGained);
        Assert.Single(fixture.History.Attempts);
    }

    [Fact]
    public async Task Positive_duration_zero_activity_session_is_persisted()
    {
        using var fixture = await Fixture.CreateAsync();
        await fixture.ResolveAsync(0, "Idle Hero");

        fixture.Time.Advance(TimeSpan.FromMinutes(5));
        await fixture.Manager.StopAsync();

        var observation = Assert.Single(fixture.History.Durable);
        Assert.Equal(TimeSpan.FromMinutes(5), observation.ObservedDuration);
        Assert.Equal(CombatScaledAmount.Zero, observation.DamageDealt);
        Assert.Equal(0, observation.Attempts);
        Assert.Equal(0, observation.TotalDefeated);
        Assert.Equal(0, observation.ExperienceGained);
        Assert.Equal(0, observation.GameplayInfluenceGained);
    }

    [Fact]
    public async Task Pre_resolution_retained_evidence_uses_the_original_session_baseline()
    {
        using var fixture = await Fixture.CreateAsync();
        fixture.Parser.PublishClassified([
            fixture.Event(0, "You gain 100 experience.", 1, Start.AddSeconds(1))
        ]);
        await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
            Assert.Single(fixture.Manager.Current.Sessions).RetainedEventCount == 1);

        var record = fixture.Characters.EstablishTrustedFromManualConfirmation(
            "acct-1",
            "Retained Hero");
        Assert.True(fixture.Manager.ConfirmCharacter(fixture.Contexts[0], record.RecordId!).IsSuccess);
        await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
            Assert.Single(fixture.Manager.Current.Sessions).SessionExperienceGained == 100);

        fixture.Time.Advance(TimeSpan.FromMinutes(5));
        await fixture.Manager.StopAsync();

        var observation = Assert.Single(fixture.History.Durable);
        Assert.Equal(Start, observation.StartedAtUtc);
        Assert.Equal(100, observation.ExperienceGained);
    }

    [Fact]
    public async Task Unresolved_or_zero_duration_session_is_not_persisted()
    {
        using var unresolved = await Fixture.CreateAsync();
        unresolved.Time.Advance(TimeSpan.FromMinutes(5));
        await unresolved.Manager.StopAsync();
        Assert.Empty(unresolved.History.Durable);

        using var zeroDuration = await Fixture.CreateAsync();
        await zeroDuration.ResolveAsync(0, "Instant Hero");
        await zeroDuration.Manager.StopAsync();
        Assert.Empty(zeroDuration.History.Durable);
    }

    [Fact]
    public async Task Retention_overflow_is_conservatively_skipped()
    {
        using var fixture = await Fixture.CreateAsync(new GameplaySessionOptions
        {
            TimeProvider = new ManualTimeProvider(Start),
            MaxRetainedEventCount = 1
        });
        fixture.Parser.PublishClassified([
            fixture.Event(0, "ordinary line one", 1, Start.AddSeconds(1)),
            fixture.Event(0, "ordinary line two", 2, Start.AddSeconds(2))
        ]);
        await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
            Assert.Single(fixture.Manager.Current.Sessions).RetentionOverflowed);

        var record = fixture.Characters.EstablishTrustedFromManualConfirmation(
            "acct-1",
            "Overflow Hero");
        Assert.True(fixture.Manager.ConfirmCharacter(fixture.Contexts[0], record.RecordId!).IsSuccess);
        fixture.Time.Advance(TimeSpan.FromMinutes(5));
        await fixture.Manager.StopAsync();

        Assert.Empty(fixture.History.Durable);
        Assert.Empty(fixture.History.Attempts);
    }

    [Fact]
    public async Task Same_character_welcome_does_not_create_an_extra_segment()
    {
        using var fixture = await Fixture.CreateAsync();
        await fixture.ResolveAsync(0, "Repeat Hero");
        fixture.Parser.PublishClassified([
            fixture.Event(0, "You gain 100 experience.", 2, Start.AddSeconds(1))
        ]);
        await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
            Assert.Single(fixture.Manager.Current.Sessions).SessionExperienceGained == 100);

        fixture.Time.Advance(TimeSpan.FromMinutes(1));
        fixture.Parser.PublishClassified([
            fixture.Event(0, "Welcome to City of Heroes, Repeat Hero!", 3, fixture.Time.GetUtcNow())
        ]);
        await GameplaySessionTestInfrastructure.WaitForWorkQueueToDrainAsync(fixture.Manager);
        Assert.Empty(fixture.History.Durable);

        fixture.Time.Advance(TimeSpan.FromMinutes(4));
        await fixture.Manager.StopAsync();
        var observation = Assert.Single(fixture.History.Durable);
        Assert.Equal(0, observation.SegmentOrdinal);
        Assert.Equal(100, observation.ExperienceGained);
    }

    [Fact]
    public async Task Different_character_welcome_finalizes_A_and_starts_B_cleanly()
    {
        using var fixture = await Fixture.CreateAsync();
        var characterA = await fixture.ResolveAsync(0, "Hero A");
        fixture.Parser.PublishClassified([
            fixture.Event(0, "You gain 100 experience.", 2, Start.AddSeconds(1))
        ]);
        await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
            Assert.Single(fixture.Manager.Current.Sessions).SessionExperienceGained == 100);

        fixture.Time.Advance(TimeSpan.FromMinutes(5));
        var characterB = await fixture.ResolveAsync(0, "Hero B", 3, fixture.Time.GetUtcNow());
        var observationA = Assert.Single(fixture.History.Durable);
        Assert.Equal(characterA, observationA.CharacterRecordId);
        Assert.Equal(100, observationA.ExperienceGained);

        fixture.Parser.PublishClassified([
            fixture.Event(0, "You gain 50 experience.", 4, fixture.Time.GetUtcNow().AddSeconds(1))
        ]);
        await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
            Assert.Single(fixture.Manager.Current.Sessions).SessionExperienceGained == 50);
        fixture.Time.Advance(TimeSpan.FromMinutes(5));
        await fixture.Manager.StopAsync();

        var observationB = Assert.Single(
            fixture.History.Durable,
            item => item.CharacterRecordId == characterB);
        Assert.Equal(50, observationB.ExperienceGained);
        Assert.All(fixture.History.Durable, item => Assert.Equal(0, item.SegmentOrdinal));
        Assert.NotEqual(observationA.GameplaySessionId, observationB.GameplaySessionId);
    }

    [Fact]
    public async Task ClearIdentity_closes_A_and_advances_baseline_before_B_resolution()
    {
        using var fixture = await Fixture.CreateAsync();
        var characterA = await fixture.ResolveAsync(0, "Clear A");
        fixture.Parser.PublishClassified([
            fixture.Event(0, "You gain 100 experience.", 2, Start.AddSeconds(1))
        ]);
        await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
            Assert.Single(fixture.Manager.Current.Sessions).SessionExperienceGained == 100);

        fixture.Time.Advance(TimeSpan.FromMinutes(5));
        Assert.True(fixture.Manager.ClearIdentity(fixture.Contexts[0]).IsSuccess);
        var observationA = Assert.Single(fixture.History.Durable);
        Assert.Equal(characterA, observationA.CharacterRecordId);
        Assert.Equal(0, observationA.SegmentOrdinal);

        var establishB = fixture.Characters.EstablishTrustedFromManualConfirmation("acct-1", "Clear B");
        Assert.True(fixture.Manager.ConfirmCharacter(fixture.Contexts[0], establishB.RecordId!).IsSuccess);
        fixture.Parser.PublishClassified([
            fixture.Event(0, "You gain 50 experience.", 3, fixture.Time.GetUtcNow().AddSeconds(1))
        ]);
        await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
            Assert.Single(fixture.Manager.Current.Sessions).SessionExperienceGained == 150);

        fixture.Time.Advance(TimeSpan.FromMinutes(5));
        await fixture.Manager.StopAsync();
        var observationB = Assert.Single(
            fixture.History.Durable,
            item => item.CharacterRecordId == establishB.RecordId);
        Assert.Equal(observationA.GameplaySessionId, observationB.GameplaySessionId);
        Assert.Equal(1, observationB.SegmentOrdinal);
        Assert.Equal(Start.AddMinutes(5), observationB.StartedAtUtc);
        Assert.Equal(50, observationB.ExperienceGained);
    }

    [Fact]
    public async Task Suspended_session_uses_suspension_time_when_finalized_later()
    {
        using var fixture = await Fixture.CreateAsync();
        await fixture.ResolveAsync(0, "Suspended Hero");
        fixture.Time.Advance(TimeSpan.FromMinutes(5));
        fixture.Monitoring.Publish(ParserTestSnapshots.Snapshot(
            2,
            fixture.ContextSnapshot(0, MonitoringContextState.RuntimeSuspended)));
        await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
            Assert.Single(fixture.Manager.Current.Sessions).LifecycleState
                == GameplaySessionLifecycleState.Suspended);
        Assert.Empty(fixture.History.Durable);

        var suspendedAt = fixture.Time.GetUtcNow();
        fixture.Time.Advance(TimeSpan.FromMinutes(30));
        fixture.Monitoring.Publish(ParserTestSnapshots.Snapshot(
            3,
            fixture.ContextSnapshot(0, MonitoringContextState.Stopped)));
        await GameplaySessionTestInfrastructure.WaitUntilAsync(() => fixture.History.Durable.Count == 1);

        Assert.Equal(suspendedAt, Assert.Single(fixture.History.Durable).EndedAtUtc);
    }

    [Fact]
    public async Task Delayed_suspension_delivery_uses_authoritative_monitoring_timestamp()
    {
        using var fixture = await Fixture.CreateAsync();
        var character = await fixture.ResolveAsync(0, "Delayed Suspension");
        fixture.Time.Advance(TimeSpan.FromMinutes(5));
        var authoritativeSuspendedAt = fixture.Time.GetUtcNow();
        fixture.Time.Advance(TimeSpan.FromMinutes(20));

        fixture.Monitoring.Publish(ParserTestSnapshots.Snapshot(
            2,
            fixture.ContextSnapshot(0, MonitoringContextState.RuntimeSuspended) with
            {
                SuspendedAt = authoritativeSuspendedAt,
                LastStateChangedAt = authoritativeSuspendedAt
            }));
        await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
            Assert.Single(fixture.Manager.Current.Sessions).LifecycleState
                == GameplaySessionLifecycleState.Suspended);

        fixture.Time.Advance(TimeSpan.FromMinutes(30));
        fixture.Monitoring.Publish(ParserTestSnapshots.Snapshot(
            3,
            fixture.ContextSnapshot(0, MonitoringContextState.Stopped)));
        await GameplaySessionTestInfrastructure.WaitUntilAsync(() => fixture.History.Durable.Count == 1);

        var observation = Assert.Single(fixture.History.Durable);
        Assert.Equal(character, observation.CharacterRecordId);
        Assert.Equal(authoritativeSuspendedAt, observation.EndedAtUtc);
        Assert.NotEqual(fixture.Time.GetUtcNow(), observation.EndedAtUtc);
    }

    [Fact]
    public async Task Delayed_source_loss_delivery_uses_authoritative_monitoring_timestamp()
    {
        using var fixture = await Fixture.CreateAsync();
        var character = await fixture.ResolveAsync(0, "Delayed Source Loss");
        fixture.Time.Advance(TimeSpan.FromMinutes(5));
        var authoritativeSourceLostAt = fixture.Time.GetUtcNow();
        fixture.Time.Advance(TimeSpan.FromMinutes(20));

        fixture.Monitoring.Publish(ParserTestSnapshots.Snapshot(
            2,
            fixture.ContextSnapshot(0, MonitoringContextState.WaitingForSource) with
            {
                SourceLostAt = authoritativeSourceLostAt,
                LastStateChangedAt = authoritativeSourceLostAt
            }));
        await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
            Assert.Single(fixture.Manager.Current.Sessions).LifecycleState
                == GameplaySessionLifecycleState.Suspended);

        fixture.Time.Advance(TimeSpan.FromMinutes(30));
        fixture.Monitoring.Publish(ParserTestSnapshots.Snapshot(
            3,
            fixture.ContextSnapshot(0, MonitoringContextState.Stopped)));
        await GameplaySessionTestInfrastructure.WaitUntilAsync(() => fixture.History.Durable.Count == 1);

        var observation = Assert.Single(fixture.History.Durable);
        Assert.Equal(character, observation.CharacterRecordId);
        Assert.Equal(authoritativeSourceLostAt, observation.EndedAtUtc);
        Assert.NotEqual(fixture.Time.GetUtcNow(), observation.EndedAtUtc);
    }

    [Fact]
    public async Task Suspension_timestamp_before_current_baseline_is_clamped_without_false_segment()
    {
        using var fixture = await Fixture.CreateAsync();
        await fixture.ResolveAsync(0, "Clamp Hero");
        fixture.Time.Advance(TimeSpan.FromMinutes(5));
        Assert.True(fixture.Manager.CaptureHistoricalPerformanceBoundary(fixture.Contexts[0]).IsSuccess);
        var first = Assert.Single(fixture.History.Durable);

        fixture.Time.Advance(TimeSpan.FromMinutes(5));
        fixture.Monitoring.Publish(ParserTestSnapshots.Snapshot(
            2,
            fixture.ContextSnapshot(0, MonitoringContextState.RuntimeSuspended) with
            {
                SuspendedAt = Start.AddMinutes(2),
                LastStateChangedAt = Start.AddMinutes(2)
            }));
        await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
            Assert.Single(fixture.Manager.Current.Sessions).LifecycleState
                == GameplaySessionLifecycleState.Suspended);
        fixture.Monitoring.Publish(ParserTestSnapshots.Snapshot(
            3,
            fixture.ContextSnapshot(0, MonitoringContextState.Stopped)));
        await GameplaySessionTestInfrastructure.WaitUntilAsync(() => fixture.Manager.Current.Sessions.Count == 0);

        Assert.Equal(first, Assert.Single(fixture.History.Durable));
        Assert.Contains(
            fixture.Manager.GetDiagnostics().RecentOperations,
            operation => operation.Contains("clamped", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Runtime_generation_reset_and_context_removal_use_terminal_convergence()
    {
        using var reset = await Fixture.CreateAsync();
        await reset.ResolveAsync(0, "Reset Hero");
        reset.Time.Advance(TimeSpan.FromMinutes(5));
        reset.Manager.ResetForNewRuntimeGeneration();
        await GameplaySessionTestInfrastructure.WaitUntilAsync(() => reset.History.Durable.Count == 1);
        Assert.Equal("Reset Hero", reset.Characters.TryGetRecord(
            Assert.Single(reset.History.Durable).CharacterRecordId)?.CurrentDisplayName);

        using var removed = await Fixture.CreateAsync();
        await removed.ResolveAsync(0, "Removed Hero");
        removed.Time.Advance(TimeSpan.FromMinutes(5));
        removed.Monitoring.Publish(ParserTestSnapshots.Snapshot(2));
        await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
            removed.History.Durable.Count == 1
            && removed.Manager.Current.Sessions.Count == 0);
    }

    [Fact]
    public async Task Failed_write_does_not_block_handoff_and_exact_observation_is_retried()
    {
        var history = new RecordingObservationRepository { FailuresRemaining = 1 };
        using var fixture = await Fixture.CreateAsync(history: history);
        var characterA = await fixture.ResolveAsync(0, "Retry A");
        fixture.Parser.PublishClassified([
            fixture.Event(0, "You gain 100 experience.", 2, Start.AddSeconds(1))
        ]);
        await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
            Assert.Single(fixture.Manager.Current.Sessions).SessionExperienceGained == 100);

        fixture.Time.Advance(TimeSpan.FromMinutes(5));
        await fixture.ResolveAsync(0, "Retry B", 3, fixture.Time.GetUtcNow());
        Assert.Empty(history.Durable);
        Assert.Equal("Retry B", Assert.Single(fixture.Manager.Current.Sessions).CharacterDisplayName);

        fixture.Time.Advance(TimeSpan.FromMinutes(5));
        await fixture.Manager.StopAsync();

        Assert.Equal(2, history.Durable.Count);
        Assert.Equal(characterA, history.Durable[0].CharacterRecordId);
        Assert.Equal(history.Attempts[0], history.Attempts[1]);
        Assert.Equal(3, history.Attempts.Count);
    }

    [Fact]
    public async Task Conflicting_duplicate_is_reported_without_wedging_finalization()
    {
        var history = new RecordingObservationRepository { ForceConflict = true };
        using var fixture = await Fixture.CreateAsync(history: history);
        await fixture.ResolveAsync(0, "Conflict Hero");
        fixture.Time.Advance(TimeSpan.FromMinutes(5));

        await fixture.Manager.StopAsync();

        Assert.Empty(fixture.Manager.Current.Sessions);
        Assert.Empty(history.Durable);
        Assert.Single(history.Attempts);
        Assert.Contains(
            fixture.Manager.GetDiagnostics().RecentOperations,
            operation => operation.Contains("Historical performance conflict", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Multiple_contexts_persist_only_their_own_character_evidence()
    {
        using var fixture = await Fixture.CreateAsync(contextCount: 2);
        var characterA = await fixture.ResolveAsync(0, "Context A");
        var characterB = await fixture.ResolveAsync(1, "Context B");
        fixture.Parser.PublishClassified([
            fixture.Event(0, "You gain 100 experience.", 2, Start.AddSeconds(1)),
            fixture.Event(1, "You gain 40 influence.", 2, Start.AddSeconds(1))
        ]);
        await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
            fixture.Manager.Current.Sessions.Any(session => session.SessionExperienceGained == 100)
            && fixture.Manager.Current.Sessions.Any(session => session.SessionGameplayInfluenceGained == 40));

        fixture.Time.Advance(TimeSpan.FromMinutes(5));
        await fixture.Manager.StopAsync();

        var observationA = Assert.Single(historyFor(characterA));
        var observationB = Assert.Single(historyFor(characterB));
        Assert.Equal(100, observationA.ExperienceGained);
        Assert.Equal(0, observationA.GameplayInfluenceGained);
        Assert.Equal(0, observationB.ExperienceGained);
        Assert.Equal(40, observationB.GameplayInfluenceGained);

        IEnumerable<CharacterPerformanceObservation> historyFor(CharacterRecordId character) =>
            fixture.History.Durable.Where(item => item.CharacterRecordId == character);
    }

    [Fact]
    public async Task Single_Clear_splits_one_active_session_without_double_counting()
    {
        using var fixture = await Fixture.CreateAsync();
        await fixture.ResolveAsync(0, "Clear Hero");
        fixture.Parser.PublishClassified([
            fixture.Event(0, "You gain 100 experience and 10 influence.", 2, Start.AddSeconds(1)),
            fixture.Event(0, "You hit Lusca with your Hot Feet for 13.88 points of Fire damage.", 3, Start.AddSeconds(2))
        ]);
        await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
            Assert.Single(fixture.Manager.Current.Sessions).SessionExperienceGained == 100);

        fixture.Time.Advance(TimeSpan.FromMinutes(10));
        Assert.True(fixture.Manager.CaptureHistoricalPerformanceBoundary(fixture.Contexts[0]).IsSuccess);
        var first = Assert.Single(fixture.History.Durable);
        Assert.Equal(0, first.SegmentOrdinal);
        Assert.Equal(100, first.ExperienceGained);
        Assert.Equal(new CombatScaledAmount(1388), first.DamageDealt);

        fixture.Parser.PublishClassified([
            fixture.Event(0, "You gain 50 experience and 5 influence.", 4, fixture.Time.GetUtcNow().AddSeconds(1)),
            fixture.Event(0, "You hit Skull with your Fire Cages for 8.21 points of Fire damage.", 5, fixture.Time.GetUtcNow().AddSeconds(2))
        ]);
        await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
            Assert.Single(fixture.Manager.Current.Sessions).SessionExperienceGained == 150);
        fixture.Time.Advance(TimeSpan.FromMinutes(10));
        await fixture.Manager.StopAsync();

        var second = Assert.Single(fixture.History.Durable, item => item.SegmentOrdinal == 1);
        Assert.Equal(first.GameplaySessionId, second.GameplaySessionId);
        Assert.Equal(50, second.ExperienceGained);
        Assert.Equal(5, second.GameplayInfluenceGained);
        Assert.Equal(new CombatScaledAmount(821), second.DamageDealt);
        Assert.Equal(150, fixture.History.Durable.Sum(item => item.ExperienceGained));
        Assert.Equal(15, fixture.History.Durable.Sum(item => item.GameplayInfluenceGained));
        Assert.Equal(new CombatScaledAmount(2209), new CombatScaledAmount(
            fixture.History.Durable.Sum(item => item.DamageDealt.Hundredths)));
    }

    [Fact]
    public async Task Clear_then_terminal_flows_through_read_model_and_Overview_exactly_once()
    {
        using var fixture = await Fixture.CreateAsync();
        var character = await fixture.ResolveAsync(0, "Overview Chain");
        fixture.Parser.PublishClassified([
            fixture.Event(0, "You gain 100 experience and 10 influence.", 2, Start.AddSeconds(1)),
            fixture.Event(0, "You hit Lusca with your Hot Feet for 13.88 points of Fire damage.", 3, Start.AddSeconds(2))
        ]);
        await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
            Assert.Single(fixture.Manager.Current.Sessions).SessionExperienceGained == 100);
        fixture.Time.Advance(TimeSpan.FromMinutes(20));
        Assert.True(fixture.Manager.CaptureHistoricalPerformanceBoundary(fixture.Contexts[0]).IsSuccess);

        fixture.Parser.PublishClassified([
            fixture.Event(0, "You gain 50 experience and 5 influence.", 4, fixture.Time.GetUtcNow().AddSeconds(1)),
            fixture.Event(0, "You hit Skull with your Fire Cages for 8.21 points of Fire damage.", 5, fixture.Time.GetUtcNow().AddSeconds(2))
        ]);
        await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
            Assert.Single(fixture.Manager.Current.Sessions).SessionExperienceGained == 150);
        fixture.Time.Advance(TimeSpan.FromMinutes(10));
        await fixture.Manager.StopAsync();

        var readService = new CharacterHistoricalPerformanceReadService(
            fixture.History,
            fixture.Characters);
        var lifetime = readService.GetLifetime(character);
        var overview = AnalyticsOverviewPresentation.Build(lifetime);

        Assert.Equal(2, lifetime.ObservationCount);
        Assert.Equal(TimeSpan.FromMinutes(30), lifetime.ObservedDuration);
        Assert.Equal(new CombatScaledAmount(2209), lifetime.DamageDealt);
        Assert.Equal(150, lifetime.ExperienceGained);
        Assert.Equal(15, lifetime.GameplayInfluenceGained);
        Assert.Equal("2", overview.ObservationCountLabel);
        Assert.Equal("150", overview.TotalExperienceLabel);
        Assert.Equal("15", overview.TotalGameplayInfluenceLabel);
    }

    [Fact]
    public async Task Multiple_Clears_create_contiguous_additive_segments()
    {
        using var fixture = await Fixture.CreateAsync();
        await fixture.ResolveAsync(0, "Many Clears");
        fixture.Parser.PublishClassified([
            fixture.Event(0, "You gain 100 experience and 10 influence.", 2, Start.AddSeconds(1)),
            fixture.Event(0, "You hit Lusca with your Hot Feet for 13.88 points of Fire damage.", 3, Start.AddSeconds(2)),
            fixture.Event(0, "You have defeated Sprocket", 4, Start.AddSeconds(3))
        ]);
        await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
            Assert.Single(fixture.Manager.Current.Sessions).Combat.MyDefeats == 1);
        fixture.Time.Advance(TimeSpan.FromMinutes(10));
        Assert.True(fixture.Manager.CaptureHistoricalPerformanceBoundary(fixture.Contexts[0]).IsSuccess);

        fixture.Parser.PublishClassified([
            fixture.Event(0, "You gain 200 experience and 20 influence.", 5, fixture.Time.GetUtcNow().AddSeconds(1)),
            fixture.Event(0, "You hit Skull with your Fire Cages for 8.21 points of Fire damage.", 6, fixture.Time.GetUtcNow().AddSeconds(2)),
            fixture.Event(0, "Psiche has defeated Prototype Oscillator", 7, fixture.Time.GetUtcNow().AddSeconds(3))
        ]);
        await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
            Assert.Single(fixture.Manager.Current.Sessions).SessionExperienceGained == 300);
        fixture.Time.Advance(TimeSpan.FromMinutes(10));
        Assert.True(fixture.Manager.CaptureHistoricalPerformanceBoundary(fixture.Contexts[0]).IsSuccess);

        fixture.Parser.PublishClassified([
            fixture.Event(0, "You gain 300 experience and 30 influence.", 8, fixture.Time.GetUtcNow().AddSeconds(1)),
            fixture.Event(0, "You hit Clockwork with your Blaze for 5.59 points of Fire damage.", 9, fixture.Time.GetUtcNow().AddSeconds(2))
        ]);
        await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
            Assert.Single(fixture.Manager.Current.Sessions).SessionExperienceGained == 600);
        var authoritative = Assert.Single(fixture.Manager.Current.Sessions);
        fixture.Time.Advance(TimeSpan.FromMinutes(10));
        await fixture.Manager.StopAsync();

        Assert.Equal([0, 1, 2], fixture.History.Durable.Select(item => item.SegmentOrdinal));
        Assert.Equal(authoritative.SessionExperienceGained, fixture.History.Durable.Sum(item => item.ExperienceGained));
        Assert.Equal(authoritative.SessionGameplayInfluenceGained, fixture.History.Durable.Sum(item => item.GameplayInfluenceGained));
        Assert.Equal(authoritative.Combat.DamageDealt.Hundredths, fixture.History.Durable.Sum(item => item.DamageDealt.Hundredths));
        Assert.Equal(authoritative.Combat.Accuracy.Attempts, fixture.History.Durable.Sum(item => item.Attempts));
        Assert.Equal(authoritative.Combat.TotalDefeated, fixture.History.Durable.Sum(item => item.TotalDefeated));
        Assert.Equal(authoritative.Combat.MyDefeats, fixture.History.Durable.Sum(item => item.MyDefeats));
        Assert.Equal(TimeSpan.FromMinutes(30), TimeSpan.FromTicks(
            fixture.History.Durable.Sum(item => item.ObservedDuration.Ticks)));
    }

    [Fact]
    public async Task Clear_handles_zero_activity_zero_duration_unresolved_and_overflow_without_false_segments()
    {
        using var zeroActivity = await Fixture.CreateAsync();
        await zeroActivity.ResolveAsync(0, "Idle Clear");
        Assert.True(zeroActivity.Manager.CaptureHistoricalPerformanceBoundary(zeroActivity.Contexts[0]).IsSuccess);
        Assert.Empty(zeroActivity.History.Durable);
        zeroActivity.Time.Advance(TimeSpan.FromMinutes(15));
        Assert.True(zeroActivity.Manager.CaptureHistoricalPerformanceBoundary(zeroActivity.Contexts[0]).IsSuccess);
        var idle = Assert.Single(zeroActivity.History.Durable);
        Assert.Equal(0, idle.SegmentOrdinal);
        Assert.Equal(TimeSpan.FromMinutes(15), idle.ObservedDuration);
        Assert.Equal(0, idle.ExperienceGained);

        using var unresolved = await Fixture.CreateAsync();
        unresolved.Time.Advance(TimeSpan.FromMinutes(5));
        Assert.True(unresolved.Manager.CaptureHistoricalPerformanceBoundary(unresolved.Contexts[0]).IsSuccess);
        var record = unresolved.Characters.EstablishTrustedFromManualConfirmation("acct-1", "Late Identity");
        Assert.True(unresolved.Manager.ConfirmCharacter(unresolved.Contexts[0], record.RecordId!).IsSuccess);
        unresolved.Time.Advance(TimeSpan.FromMinutes(5));
        await unresolved.Manager.StopAsync();
        Assert.Equal(Start, Assert.Single(unresolved.History.Durable).StartedAtUtc);

        using var overflow = await Fixture.CreateAsync(new GameplaySessionOptions
        {
            TimeProvider = new ManualTimeProvider(Start),
            MaxRetainedEventCount = 1
        });
        overflow.Parser.PublishClassified([
            overflow.Event(0, "ordinary one", 1, Start.AddSeconds(1)),
            overflow.Event(0, "ordinary two", 2, Start.AddSeconds(2))
        ]);
        await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
            Assert.Single(overflow.Manager.Current.Sessions).RetentionOverflowed);
        var overflowRecord = overflow.Characters.EstablishTrustedFromManualConfirmation("acct-1", "Overflow Clear");
        Assert.True(overflow.Manager.ConfirmCharacter(overflow.Contexts[0], overflowRecord.RecordId!).IsSuccess);
        overflow.Time.Advance(TimeSpan.FromMinutes(5));
        Assert.True(overflow.Manager.CaptureHistoricalPerformanceBoundary(overflow.Contexts[0]).IsSuccess);
        Assert.Empty(overflow.History.Attempts);
    }

    [Fact]
    public async Task Failed_Clear_write_advances_cursor_and_retries_the_exact_segment()
    {
        var history = new RecordingObservationRepository { FailuresRemaining = 1 };
        using var fixture = await Fixture.CreateAsync(history: history);
        await fixture.ResolveAsync(0, "Failed Clear");
        fixture.Parser.PublishClassified([
            fixture.Event(0, "You gain 100 experience.", 2, Start.AddSeconds(1))
        ]);
        await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
            Assert.Single(fixture.Manager.Current.Sessions).SessionExperienceGained == 100);
        fixture.Time.Advance(TimeSpan.FromMinutes(5));

        Assert.True(fixture.Manager.CaptureHistoricalPerformanceBoundary(fixture.Contexts[0]).IsSuccess);
        Assert.Empty(history.Durable);
        var failedSegment = Assert.Single(history.Attempts);

        fixture.Parser.PublishClassified([
            fixture.Event(0, "You gain 50 experience.", 3, fixture.Time.GetUtcNow().AddSeconds(1))
        ]);
        await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
            Assert.Single(fixture.Manager.Current.Sessions).SessionExperienceGained == 150);
        fixture.Time.Advance(TimeSpan.FromMinutes(5));
        await fixture.Manager.StopAsync();

        Assert.Equal(failedSegment, history.Attempts[1]);
        Assert.Equal([0, 1], history.Durable.Select(item => item.SegmentOrdinal));
        Assert.Equal([100L, 50L], history.Durable.Select(item => item.ExperienceGained));
    }

    [Fact]
    public async Task Clear_then_ClearIdentity_keeps_both_segments_with_the_resolved_character()
    {
        using var fixture = await Fixture.CreateAsync();
        var character = await fixture.ResolveAsync(0, "Identity Clear");
        fixture.Parser.PublishClassified([
            fixture.Event(0, "You gain 100 experience.", 2, Start.AddSeconds(1))
        ]);
        await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
            Assert.Single(fixture.Manager.Current.Sessions).SessionExperienceGained == 100);
        fixture.Time.Advance(TimeSpan.FromMinutes(5));
        Assert.True(fixture.Manager.CaptureHistoricalPerformanceBoundary(fixture.Contexts[0]).IsSuccess);

        fixture.Parser.PublishClassified([
            fixture.Event(0, "You gain 50 experience.", 3, fixture.Time.GetUtcNow().AddSeconds(1))
        ]);
        await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
            Assert.Single(fixture.Manager.Current.Sessions).SessionExperienceGained == 150);
        fixture.Time.Advance(TimeSpan.FromMinutes(5));
        Assert.True(fixture.Manager.ClearIdentity(fixture.Contexts[0]).IsSuccess);

        Assert.Equal([0, 1], fixture.History.Durable.Select(item => item.SegmentOrdinal));
        Assert.All(fixture.History.Durable, item => Assert.Equal(character, item.CharacterRecordId));
        Assert.Equal([100L, 50L], fixture.History.Durable.Select(item => item.ExperienceGained));
    }

    [Fact]
    public async Task Welcome_boundaries_after_Clear_preserve_same_character_and_isolate_handoff()
    {
        using var repeated = await Fixture.CreateAsync();
        await repeated.ResolveAsync(0, "Same Clear");
        repeated.Time.Advance(TimeSpan.FromMinutes(5));
        Assert.True(repeated.Manager.CaptureHistoricalPerformanceBoundary(repeated.Contexts[0]).IsSuccess);
        repeated.Time.Advance(TimeSpan.FromMinutes(1));
        await repeated.ResolveAsync(0, "Same Clear", 2, repeated.Time.GetUtcNow());
        Assert.Single(repeated.History.Durable);
        repeated.Time.Advance(TimeSpan.FromMinutes(4));
        await repeated.Manager.StopAsync();
        Assert.Equal([0, 1], repeated.History.Durable.Select(item => item.SegmentOrdinal));

        using var handoff = await Fixture.CreateAsync();
        var characterA = await handoff.ResolveAsync(0, "Handoff Clear A");
        handoff.Time.Advance(TimeSpan.FromMinutes(5));
        Assert.True(handoff.Manager.CaptureHistoricalPerformanceBoundary(handoff.Contexts[0]).IsSuccess);
        handoff.Parser.PublishClassified([
            handoff.Event(0, "You gain 50 experience.", 2, handoff.Time.GetUtcNow().AddSeconds(1))
        ]);
        await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
            Assert.Single(handoff.Manager.Current.Sessions).SessionExperienceGained == 50);
        handoff.Time.Advance(TimeSpan.FromMinutes(5));
        var characterB = await handoff.ResolveAsync(0, "Handoff Clear B", 3, handoff.Time.GetUtcNow());
        handoff.Time.Advance(TimeSpan.FromMinutes(5));
        await handoff.Manager.StopAsync();

        Assert.Equal([0, 1], handoff.History.Durable
            .Where(item => item.CharacterRecordId == characterA)
            .Select(item => item.SegmentOrdinal));
        Assert.Equal([0], handoff.History.Durable
            .Where(item => item.CharacterRecordId == characterB)
            .Select(item => item.SegmentOrdinal));
    }

    [Fact]
    public async Task Historical_Clear_does_not_rebase_tracked_or_rolling_state()
    {
        using var fixture = await Fixture.CreateAsync();
        await fixture.ResolveAsync(0, "Independent Clear");
        Assert.True(fixture.Manager.StartTrackedCombat(fixture.Contexts[0]).IsSuccess);
        fixture.Parser.PublishClassified([
            fixture.Event(0, "You hit Lusca with your Hot Feet for 13.88 points of Fire damage.", 2, Start.AddSeconds(1))
        ]);
        await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
            Assert.Single(fixture.Manager.Current.Sessions).Combat.Tracked.DamageDealt
                == new CombatScaledAmount(1388));
        fixture.Time.Advance(TimeSpan.FromMinutes(5));
        var before = Assert.Single(fixture.Manager.Current.Sessions);

        Assert.True(fixture.Manager.CaptureHistoricalPerformanceBoundary(fixture.Contexts[0]).IsSuccess);

        var after = Assert.Single(fixture.Manager.Current.Sessions);
        Assert.Equal(before.Combat, after.Combat);
        Assert.True(after.Combat.Tracked.IsTracking);
        Assert.Equal(before.Combat.Tracked, after.Combat.Tracked);
        Assert.Equal(before.Combat.Rolling, after.Combat.Rolling);
    }

    [Fact]
    public async Task Monitoring_Error_finalizes_historical_observation_once()
    {
        using var fixture = await Fixture.CreateAsync();
        var character = await fixture.ResolveAsync(0, "Error Hero");
        fixture.Time.Advance(TimeSpan.FromMinutes(5));

        fixture.Monitoring.Publish(ParserTestSnapshots.Snapshot(
            2,
            fixture.ContextSnapshot(0, MonitoringContextState.Error)));
        await GameplaySessionTestInfrastructure.WaitUntilAsync(() => fixture.Manager.Current.Sessions.Count == 0);
        fixture.Monitoring.Publish(ParserTestSnapshots.Snapshot(
            3,
            fixture.ContextSnapshot(0, MonitoringContextState.Error)));
        await GameplaySessionTestInfrastructure.WaitForWorkQueueToDrainAsync(fixture.Manager);

        var observation = Assert.Single(fixture.History.Durable);
        Assert.Equal(character, observation.CharacterRecordId);
        Assert.Equal(Start.AddMinutes(5), observation.EndedAtUtc);
        Assert.Single(fixture.History.Attempts);
    }

    [Fact]
    public async Task Ready_replacement_finalizes_suspended_history_once_at_authoritative_boundary()
    {
        using var fixture = await Fixture.CreateAsync();
        var character = await fixture.ResolveAsync(0, "Resume Hero");
        var originalSessionId = Assert.Single(fixture.Manager.Current.Sessions).SessionId;
        fixture.Time.Advance(TimeSpan.FromMinutes(5));
        var suspendedAt = fixture.Time.GetUtcNow();
        fixture.Monitoring.Publish(ParserTestSnapshots.Snapshot(
            2,
            fixture.ContextSnapshot(0, MonitoringContextState.RuntimeSuspended) with
            {
                SuspendedAt = suspendedAt,
                LastStateChangedAt = suspendedAt,
                PreviousStateBeforeSuspension = MonitoringContextState.Ready
            }));
        await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
            Assert.Single(fixture.Manager.Current.Sessions).LifecycleState
                == GameplaySessionLifecycleState.Suspended);

        fixture.Time.Advance(TimeSpan.FromMinutes(30));
        fixture.Monitoring.Publish(ParserTestSnapshots.Snapshot(
            3,
            fixture.ContextSnapshot(0, MonitoringContextState.Ready)));
        await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
            fixture.History.Durable.Count == 1
            && fixture.Manager.Current.Sessions.Count == 1
            && fixture.Manager.Current.Sessions[0].SessionId != originalSessionId);

        var observation = Assert.Single(fixture.History.Durable);
        Assert.Equal(character, observation.CharacterRecordId);
        Assert.Equal(originalSessionId, observation.GameplaySessionId);
        Assert.Equal(suspendedAt, observation.EndedAtUtc);
        Assert.Single(fixture.History.Attempts);
    }

    private sealed class Fixture : IDisposable
    {
        private Fixture(
            FakeMonitoringSessionManager monitoring,
            GameplaySessionTestInfrastructure.FakeGameplayParserManager parser,
            CharacterRepository characters,
            string dataDirectory,
            RecordingObservationRepository history,
            ManualTimeProvider time,
            GameplaySessionManager manager,
            MonitoringContextId[] contexts,
            LogSourceId[] sources)
        {
            Monitoring = monitoring;
            Parser = parser;
            Characters = characters;
            DataDirectory = dataDirectory;
            History = history;
            Time = time;
            Manager = manager;
            Contexts = contexts;
            Sources = sources;
        }

        public FakeMonitoringSessionManager Monitoring { get; }
        public GameplaySessionTestInfrastructure.FakeGameplayParserManager Parser { get; }
        public CharacterRepository Characters { get; }
        public string DataDirectory { get; }
        public RecordingObservationRepository History { get; }
        public ManualTimeProvider Time { get; }
        public GameplaySessionManager Manager { get; }
        public MonitoringContextId[] Contexts { get; }
        public LogSourceId[] Sources { get; }

        public static async Task<Fixture> CreateAsync(
            GameplaySessionOptions? options = null,
            RecordingObservationRepository? history = null,
            int contextCount = 1)
        {
            var monitoring = new FakeMonitoringSessionManager();
            var parser = new GameplaySessionTestInfrastructure.FakeGameplayParserManager();
            var characters = GameplaySessionTestInfrastructure.CreateRepository(out var directory);
            var time = options?.TimeProvider as ManualTimeProvider ?? new ManualTimeProvider(Start);
            options ??= new GameplaySessionOptions
            {
                TimeProvider = time,
                CombatSnapshotPublishInterval = TimeSpan.Zero
            };
            history ??= new RecordingObservationRepository();
            var contexts = Enumerable.Range(1, contextCount)
                .Select(_ => MonitoringContextId.CreateNew())
                .ToArray();
            var sources = Enumerable.Range(1, contextCount)
                .Select(index => GameplaySessionTestInfrastructure.DefaultSource($"acct-{index}"))
                .ToArray();
            monitoring.SetInitial(ParserTestSnapshots.Snapshot(
                1,
                contexts.Select((context, index) =>
                    GameplaySessionTestInfrastructure.ReadyContext(context, sources[index])).ToArray()));
            var manager = await GameplaySessionTestInfrastructure.CreateStartedManager(
                monitoring,
                parser,
                characters,
                options,
                historicalObservationRepository: history);
            return new Fixture(
                monitoring,
                parser,
                characters,
                directory,
                history,
                time,
                manager,
                contexts,
                sources);
        }

        public async Task<CharacterRecordId> ResolveAsync(
            int contextIndex,
            string name,
            long sequence = 1,
            DateTimeOffset? observedAt = null)
        {
            Parser.PublishClassified([
                Event(
                    contextIndex,
                    $"Welcome to City of Heroes, {name}!",
                    sequence,
                    observedAt ?? Start)
            ]);
            await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
                Manager.Current.Sessions.Any(session =>
                    session.ContextId == Contexts[contextIndex]
                    && session.CharacterDisplayName == name
                    && session.CharacterRecordId is not null));
            return Assert.IsType<CharacterRecordId>(Manager.Current.Sessions.Single(session =>
                session.ContextId == Contexts[contextIndex]).CharacterRecordId);
        }

        public ParserEvent Event(
            int contextIndex,
            string line,
            long sequence,
            DateTimeOffset observedAt) =>
            GameplaySessionTestInfrastructure.Classify(
                line,
                Contexts[contextIndex],
                Sources[contextIndex],
                sequence,
                observedAt: observedAt);

        public MonitoringContextSnapshot ContextSnapshot(
            int contextIndex,
            MonitoringContextState state) =>
            ParserTestSnapshots.Context(
                Contexts[contextIndex],
                state,
                Sources[contextIndex],
                1,
                MonitoringSourceTransitionKind.SourceAssigned);

        public void Dispose()
        {
            Manager.Dispose();
            try
            {
                Directory.Delete(DataDirectory, recursive: true);
            }
            catch
            {
            }
        }
    }

    private sealed class RecordingObservationRepository : ICharacterPerformanceObservationRepository
    {
        private readonly object _sync = new();
        private readonly List<CharacterPerformanceObservation> _attempts = [];
        private readonly List<CharacterPerformanceObservation> _durable = [];

        public string ObservationDirectory => string.Empty;
        public IReadOnlyList<string> MalformedFileReports => [];
        public event EventHandler? Changed;
        public IReadOnlyList<CharacterPerformanceObservation> Attempts
        {
            get
            {
                lock (_sync)
                {
                    return _attempts.ToArray();
                }
            }
        }

        public IReadOnlyList<CharacterPerformanceObservation> Durable
        {
            get
            {
                lock (_sync)
                {
                    return _durable.ToArray();
                }
            }
        }

        public int FailuresRemaining { get; set; }
        public bool ForceConflict { get; set; }

        public CharacterPerformanceObservationWriteResult Persist(
            CharacterPerformanceObservation observation)
        {
            lock (_sync)
            {
                _attempts.Add(observation);
                if (FailuresRemaining > 0)
                {
                    FailuresRemaining--;
                    return Result(CharacterPerformanceObservationWriteOutcome.PersistenceFailed);
                }

                if (ForceConflict)
                {
                    return Result(CharacterPerformanceObservationWriteOutcome.Conflict);
                }

                var existing = _durable.FirstOrDefault(item =>
                    item.GameplaySessionId == observation.GameplaySessionId
                    && item.SegmentOrdinal == observation.SegmentOrdinal);
                if (existing is not null)
                {
                    return Result(existing == observation
                        ? CharacterPerformanceObservationWriteOutcome.Duplicate
                        : CharacterPerformanceObservationWriteOutcome.Conflict);
                }

                _durable.Add(observation);
                Changed?.Invoke(this, EventArgs.Empty);
                return Result(CharacterPerformanceObservationWriteOutcome.Persisted);
            }
        }

        public CharacterPerformanceObservationUpdateResult SetIncludeInOverview(
            GameplaySessionId gameplaySessionId,
            int segmentOrdinal,
            bool includeInOverview) =>
            throw new NotSupportedException();

        public CharacterPerformanceObservationDeleteResult Delete(
            GameplaySessionId gameplaySessionId,
            int segmentOrdinal) =>
            throw new NotSupportedException();

        public IReadOnlyList<CharacterPerformanceObservation> GetByCharacter(
            CharacterRecordId characterRecordId)
        {
            lock (_sync)
            {
                return _durable.Where(item => item.CharacterRecordId == characterRecordId).ToArray();
            }
        }

        public IReadOnlyList<CharacterPerformanceObservation> GetAll()
        {
            lock (_sync)
            {
                return _durable.ToArray();
            }
        }

        private static CharacterPerformanceObservationWriteResult Result(
            CharacterPerformanceObservationWriteOutcome outcome) =>
            new() { Outcome = outcome, Detail = outcome.ToString() };
    }
}
