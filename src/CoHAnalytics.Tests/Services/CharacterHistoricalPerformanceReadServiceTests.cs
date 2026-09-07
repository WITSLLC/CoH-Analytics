using CoHAnalytics.Models;
using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Services;

public sealed class CharacterHistoricalPerformanceReadServiceTests
{
    [Fact]
    public void GetSegments_returns_included_and_excluded_history_newest_first_and_filters_other_characters()
    {
        var character = CharacterRecordId.CreateNew();
        var otherCharacter = CharacterRecordId.CreateNew();
        var older = CreateObservation(character) with
        {
            EndedAtUtc = Start.AddMinutes(10),
            IncludeInOverview = false
        };
        var newer = CreateObservation(character, GameplaySessionId.CreateNew()) with
        {
            StartedAtUtc = Start.AddHours(1),
            EndedAtUtc = Start.AddHours(2)
        };
        var unrelated = CreateObservation(otherCharacter, GameplaySessionId.CreateNew());

        var segments = CreateService(older, unrelated, newer).GetSegments(character);

        Assert.Collection(
            segments,
            segment =>
            {
                Assert.Equal(newer, segment.Observation);
                Assert.True(segment.Observation.IncludeInOverview);
            },
            segment =>
            {
                Assert.Equal(older, segment.Observation);
                Assert.False(segment.Observation.IncludeInOverview);
            });
    }

    [Fact]
    public void GetSegments_uses_stable_session_and_ordinal_order_when_timestamps_match()
    {
        var character = CharacterRecordId.CreateNew();
        var firstSession = GameplaySessionId.FromGuid(Guid.Parse("00000000-0000-0000-0000-000000000001"));
        var secondSession = GameplaySessionId.FromGuid(Guid.Parse("00000000-0000-0000-0000-000000000002"));
        var observations = new[]
        {
            CreateObservation(character, secondSession, segmentOrdinal: 0),
            CreateObservation(character, firstSession, segmentOrdinal: 1),
            CreateObservation(character, firstSession, segmentOrdinal: 0)
        };

        var segments = CreateService(observations).GetSegments(character);

        Assert.Equal(
            [
                (firstSession, 0),
                (firstSession, 1),
                (secondSession, 0)
            ],
            segments.Select(segment =>
                (segment.Observation.GameplaySessionId, segment.Observation.SegmentOrdinal)));
    }

    [Fact]
    public void GetSegments_reuses_aggregate_formulas_for_one_observation_metrics()
    {
        var character = CharacterRecordId.CreateNew();
        var observation = CreateObservation(character, duration: TimeSpan.FromMinutes(30)) with
        {
            DamageDealt = new CombatScaledAmount(180_000),
            Attempts = 20,
            Hits = 19,
            ExperienceGained = 6_200_000,
            GameplayInfluenceGained = 13_550_000
        };

        var metrics = Assert.Single(CreateService(observation).GetSegments(character)).Metrics;

        Assert.Equal(12_400_000, metrics.ExperiencePerHour);
        Assert.Equal(27_100_000, metrics.GameplayInfluencePerHour);
        Assert.Equal(100, metrics.DamagePerSecondHundredths);
        Assert.Equal(95.0m, metrics.HitPercent);
    }

    [Fact]
    public void GetSegments_empty_history_is_empty_and_zero_duration_has_safe_unavailable_rates()
    {
        var character = CharacterRecordId.CreateNew();
        Assert.Empty(CreateService().GetSegments(character));
        var invalid = CreateObservation(character) with { EndedAtUtc = Start };

        var metrics = Assert.Single(CreateService(invalid).GetSegments(character)).Metrics;

        Assert.False(metrics.HasHistory);
        Assert.Null(metrics.ExperiencePerHour);
        Assert.Null(metrics.GameplayInfluencePerHour);
        Assert.Null(metrics.HitPercent);
        Assert.Null(metrics.DamagePerSecond);
    }

    [Fact]
    public void Empty_history_returns_explicit_no_evidence_snapshot()
    {
        var character = CharacterRecordId.CreateNew();

        var snapshot = CreateService().GetLifetime(character);

        Assert.Equal(character, snapshot.CharacterRecordId);
        Assert.False(snapshot.HasHistory);
        Assert.Equal(0, snapshot.ObservationCount);
        Assert.Equal(TimeSpan.Zero, snapshot.ObservedDuration);
        Assert.Null(snapshot.DamagePerSecondHundredths);
        Assert.Null(snapshot.HitPercent);
        Assert.Null(snapshot.AverageDisplayedChance);
        Assert.Null(snapshot.AverageRoll);
        Assert.Null(snapshot.ExperiencePerHour);
        Assert.Null(snapshot.GameplayInfluencePerHour);
    }

    [Fact]
    public void Single_observation_returns_exact_totals_and_derived_metrics()
    {
        var character = CharacterRecordId.CreateNew();
        var observation = CreateObservation(character: character) with
        {
            DamageDealt = new CombatScaledAmount(1_234_567),
            Attempts = 100,
            Hits = 90,
            RolledAttempts = 80,
            DisplayedChanceSumHundredths = 600_000,
            RollSumHundredths = 400_000,
            ForcedHits = 7,
            Autohits = 11,
            TotalDefeated = 30,
            MyDefeats = 24,
            ExperienceGained = 1_000,
            GameplayInfluenceGained = 500
        };

        var snapshot = CreateService(observation).GetLifetime(character);

        Assert.True(snapshot.HasHistory);
        Assert.Equal(1, snapshot.ObservationCount);
        Assert.Equal(TimeSpan.FromMinutes(30), snapshot.ObservedDuration);
        Assert.Equal(1_234_567, snapshot.DamageDealt.Hundredths);
        Assert.Equal(685, snapshot.DamagePerSecondHundredths);
        Assert.Equal(6.85, snapshot.DamagePerSecond);
        Assert.Equal(100, snapshot.Attempts);
        Assert.Equal(90, snapshot.Hits);
        Assert.Equal(10, snapshot.Misses);
        Assert.Equal(90.0m, snapshot.HitPercent);
        Assert.Equal(75.0m, snapshot.AverageDisplayedChance);
        Assert.Equal(50.0m, snapshot.AverageRoll);
        Assert.Equal(7, snapshot.ForcedHits);
        Assert.Equal(11, snapshot.Autohits);
        Assert.Equal(30, snapshot.TotalDefeated);
        Assert.Equal(24, snapshot.MyDefeats);
        Assert.Equal(1_000, snapshot.ExperienceGained);
        Assert.Equal(2_000, snapshot.ExperiencePerHour);
        Assert.Equal(500, snapshot.GameplayInfluenceGained);
        Assert.Equal(1_000, snapshot.GameplayInfluencePerHour);
    }

    [Fact]
    public void Weighted_DPS_uses_summed_damage_over_active_and_idle_duration()
    {
        var character = CharacterRecordId.CreateNew();
        var active = CreateObservation(character: character, duration: TimeSpan.FromMinutes(10)) with
        {
            DamageDealt = new CombatScaledAmount(12_000_000)
        };
        var idle = CreateObservation(
            character: character,
            session: GameplaySessionId.CreateNew(),
            duration: TimeSpan.FromMinutes(20));

        var snapshot = CreateService(active, idle).GetLifetime(character);

        Assert.Equal(TimeSpan.FromMinutes(30), snapshot.ObservedDuration);
        Assert.Equal(6_666, snapshot.DamagePerSecondHundredths);
        Assert.NotEqual(10_000, snapshot.DamagePerSecondHundredths);
    }

    [Fact]
    public void Weighted_experience_rate_uses_total_observed_duration_without_warmup()
    {
        var character = CharacterRecordId.CreateNew();
        var active = CreateObservation(character: character, duration: TimeSpan.FromMinutes(10)) with
        {
            ExperienceGained = 9_000
        };
        var idle = CreateObservation(
            character: character,
            session: GameplaySessionId.CreateNew(),
            duration: TimeSpan.FromMinutes(20));

        var snapshot = CreateService(active, idle).GetLifetime(character);

        Assert.Equal(18_000, snapshot.ExperiencePerHour);
        Assert.NotEqual(27_000, snapshot.ExperiencePerHour);
    }

    [Fact]
    public void Weighted_influence_rate_uses_total_observed_duration()
    {
        var character = CharacterRecordId.CreateNew();
        var active = CreateObservation(character: character, duration: TimeSpan.FromMinutes(10)) with
        {
            GameplayInfluenceGained = 6_000
        };
        var idle = CreateObservation(
            character: character,
            session: GameplaySessionId.CreateNew(),
            duration: TimeSpan.FromMinutes(20));

        var snapshot = CreateService(active, idle).GetLifetime(character);

        Assert.Equal(12_000, snapshot.GameplayInfluencePerHour);
        Assert.NotEqual(18_000, snapshot.GameplayInfluencePerHour);
    }

    [Fact]
    public void Accuracy_uses_weighted_totals_and_keeps_autohits_outside_attempts()
    {
        var character = CharacterRecordId.CreateNew();
        var first = CreateObservation(character: character) with
        {
            Attempts = 10,
            Hits = 10,
            RolledAttempts = 10,
            DisplayedChanceSumHundredths = 90_000,
            RollSumHundredths = 20_000,
            ForcedHits = 2,
            Autohits = 3
        };
        var second = CreateObservation(
            character: character,
            session: GameplaySessionId.CreateNew()) with
        {
            Attempts = 90,
            Hits = 45,
            RolledAttempts = 90,
            DisplayedChanceSumHundredths = 450_000,
            RollSumHundredths = 540_000,
            ForcedHits = 4,
            Autohits = 7
        };

        var snapshot = CreateService(first, second).GetLifetime(character);

        Assert.Equal(100, snapshot.Attempts);
        Assert.Equal(55, snapshot.Hits);
        Assert.Equal(45, snapshot.Misses);
        Assert.Equal(55.0m, snapshot.HitPercent);
        Assert.NotEqual(75.0m, snapshot.HitPercent);
        Assert.Equal(54.0m, snapshot.AverageDisplayedChance);
        Assert.NotEqual(70.0m, snapshot.AverageDisplayedChance);
        Assert.Equal(56.0m, snapshot.AverageRoll);
        Assert.NotEqual(40.0m, snapshot.AverageRoll);
        Assert.Equal(6, snapshot.ForcedHits);
        Assert.Equal(10, snapshot.Autohits);
    }

    [Fact]
    public void Positive_duration_zero_activity_counts_as_history_and_time()
    {
        var character = CharacterRecordId.CreateNew();
        var snapshot = CreateService(
                CreateObservation(character: character, duration: TimeSpan.FromMinutes(15)))
            .GetLifetime(character);

        Assert.True(snapshot.HasHistory);
        Assert.Equal(1, snapshot.ObservationCount);
        Assert.Equal(TimeSpan.FromMinutes(15), snapshot.ObservedDuration);
        Assert.Equal(0, snapshot.DamagePerSecondHundredths);
        Assert.Equal(0, snapshot.ExperiencePerHour);
        Assert.Equal(0, snapshot.GameplayInfluencePerHour);
        Assert.Null(snapshot.HitPercent);
        Assert.Null(snapshot.AverageDisplayedChance);
        Assert.Null(snapshot.AverageRoll);
    }

    [Fact]
    public void Attempts_without_rolled_attempts_expose_hit_percent_only()
    {
        var character = CharacterRecordId.CreateNew();
        var observation = CreateObservation(character: character) with
        {
            Attempts = 8,
            Hits = 3
        };

        var snapshot = CreateService(observation).GetLifetime(character);

        Assert.Equal(37.5m, snapshot.HitPercent);
        Assert.Null(snapshot.AverageDisplayedChance);
        Assert.Null(snapshot.AverageRoll);
    }

    [Fact]
    public void Same_session_segments_and_multiple_sessions_all_contribute()
    {
        var character = CharacterRecordId.CreateNew();
        var firstSession = GameplaySessionId.CreateNew();
        var observations = new[]
        {
            CreateObservation(character, firstSession, segmentOrdinal: 0) with
            {
                ExperienceGained = 10,
                TotalDefeated = 2,
                MyDefeats = 1
            },
            CreateObservation(character, firstSession, segmentOrdinal: 1) with
            {
                ExperienceGained = 20,
                TotalDefeated = 3,
                MyDefeats = 2
            },
            CreateObservation(character, firstSession, segmentOrdinal: 2) with
            {
                ExperienceGained = 30,
                TotalDefeated = 5,
                MyDefeats = 4
            },
            CreateObservation(character, GameplaySessionId.CreateNew(), segmentOrdinal: 0) with
            {
                ExperienceGained = 40,
                TotalDefeated = 7,
                MyDefeats = 6
            }
        };

        var snapshot = CreateService(observations).GetLifetime(character);

        Assert.Equal(4, snapshot.ObservationCount);
        Assert.Equal(TimeSpan.FromHours(2), snapshot.ObservedDuration);
        Assert.Equal(100, snapshot.ExperienceGained);
        Assert.Equal(17, snapshot.TotalDefeated);
        Assert.Equal(13, snapshot.MyDefeats);
    }

    [Fact]
    public void Excluded_segment_contributes_to_no_historical_metric_and_reinclude_restores_it()
    {
        var character = CharacterRecordId.CreateNew();
        var included = CreateObservation(
            character,
            duration: TimeSpan.FromMinutes(30)) with
        {
            DamageDealt = new CombatScaledAmount(180_000),
            Attempts = 10,
            Hits = 8,
            RolledAttempts = 9,
            DisplayedChanceSumHundredths = 63_000,
            RollSumHundredths = 36_000,
            ForcedHits = 1,
            Autohits = 2,
            TotalDefeated = 5,
            MyDefeats = 4,
            ExperienceGained = 1_000,
            GameplayInfluenceGained = 500
        };
        var managed = CreateObservation(
            character,
            GameplaySessionId.CreateNew(),
            segmentOrdinal: 1,
            duration: TimeSpan.FromMinutes(90)) with
        {
            DamageDealt = new CombatScaledAmount(9_000_000),
            Attempts = 100,
            Hits = 20,
            RolledAttempts = 80,
            DisplayedChanceSumHundredths = 720_000,
            RollSumHundredths = 640_000,
            ForcedHits = 3,
            Autohits = 7,
            TotalDefeated = 50,
            MyDefeats = 10,
            ExperienceGained = 20_000,
            GameplayInfluenceGained = 8_000
        };
        var prior = CreateService(included, managed).GetLifetime(character);

        var excluded = CreateService(
            included,
            managed with { IncludeInOverview = false }).GetLifetime(character);
        var reincluded = CreateService(
            included,
            managed with { IncludeInOverview = true }).GetLifetime(character);

        Assert.Equal(CreateService(included).GetLifetime(character), excluded);
        Assert.Equal(1, excluded.ObservationCount);
        Assert.Equal(TimeSpan.FromMinutes(30), excluded.ObservedDuration);
        Assert.Equal(prior, reincluded);
    }

    [Fact]
    public void All_excluded_observations_return_the_valid_empty_history_snapshot()
    {
        var character = CharacterRecordId.CreateNew();
        var excluded = CreateObservation(character: character) with
        {
            IncludeInOverview = false,
            DamageDealt = new CombatScaledAmount(1_000),
            Attempts = 1,
            Hits = 1,
            RolledAttempts = 1,
            DisplayedChanceSumHundredths = 10_000,
            RollSumHundredths = 5_000,
            ExperienceGained = 100,
            GameplayInfluenceGained = 50
        };

        var snapshot = CreateService(excluded).GetLifetime(character);

        Assert.Equal(CharacterHistoricalPerformanceSnapshot.Empty(character), snapshot);
        Assert.False(snapshot.HasHistory);
        Assert.Null(snapshot.DamagePerSecondHundredths);
        Assert.Null(snapshot.ExperiencePerHour);
        Assert.Null(snapshot.GameplayInfluencePerHour);
    }

    [Fact]
    public void Sixty_minute_history_equals_twenty_thirty_ten_minute_segmentation_for_every_metric()
    {
        var character = CharacterRecordId.CreateNew();
        var session = GameplaySessionId.CreateNew();
        var uninterruptedObservation = CreateObservation(
            character,
            session,
            duration: TimeSpan.FromMinutes(60)) with
        {
            DamageDealt = new CombatScaledAmount(360_000),
            Attempts = 60,
            Hits = 45,
            RolledAttempts = 50,
            DisplayedChanceSumHundredths = 375_000,
            RollSumHundredths = 250_000,
            ForcedHits = 5,
            Autohits = 4,
            TotalDefeated = 12,
            MyDefeats = 8,
            ExperienceGained = 6_000,
            GameplayInfluenceGained = 3_000
        };
        CharacterPerformanceObservation[] segmentedObservations =
        [
            CreateObservation(character, session, segmentOrdinal: 0, duration: TimeSpan.FromMinutes(20)) with
            {
                DamageDealt = new CombatScaledAmount(120_000),
                Attempts = 20,
                Hits = 15,
                RolledAttempts = 18,
                DisplayedChanceSumHundredths = 135_000,
                RollSumHundredths = 90_000,
                ForcedHits = 2,
                Autohits = 1,
                TotalDefeated = 4,
                MyDefeats = 3,
                ExperienceGained = 2_000,
                GameplayInfluenceGained = 1_000
            },
            CreateObservation(character, session, segmentOrdinal: 1, duration: TimeSpan.FromMinutes(30)) with
            {
                StartedAtUtc = Start.AddMinutes(20),
                EndedAtUtc = Start.AddMinutes(50),
                DamageDealt = new CombatScaledAmount(180_000),
                Attempts = 30,
                Hits = 22,
                RolledAttempts = 24,
                DisplayedChanceSumHundredths = 180_000,
                RollSumHundredths = 120_000,
                ForcedHits = 2,
                Autohits = 2,
                TotalDefeated = 6,
                MyDefeats = 4,
                ExperienceGained = 3_000,
                GameplayInfluenceGained = 1_500
            },
            CreateObservation(character, session, segmentOrdinal: 2, duration: TimeSpan.FromMinutes(10)) with
            {
                StartedAtUtc = Start.AddMinutes(50),
                EndedAtUtc = Start.AddMinutes(60),
                DamageDealt = new CombatScaledAmount(60_000),
                Attempts = 10,
                Hits = 8,
                RolledAttempts = 8,
                DisplayedChanceSumHundredths = 60_000,
                RollSumHundredths = 40_000,
                ForcedHits = 1,
                Autohits = 1,
                TotalDefeated = 2,
                MyDefeats = 1,
                ExperienceGained = 1_000,
                GameplayInfluenceGained = 500
            }
        ];

        var uninterrupted = CreateService(uninterruptedObservation).GetLifetime(character);
        var segmented = CreateService(segmentedObservations).GetLifetime(character);

        Assert.Equal(1, uninterrupted.ObservationCount);
        Assert.Equal(3, segmented.ObservationCount);
        Assert.Equal(
            uninterrupted,
            segmented with { ObservationCount = uninterrupted.ObservationCount });
    }

    [Fact]
    public void Durable_character_query_isolates_other_character_observations()
    {
        var characterA = CharacterRecordId.CreateNew();
        var characterB = CharacterRecordId.CreateNew();
        var observationA = CreateObservation(character: characterA) with { ExperienceGained = 10 };
        var observationB = CreateObservation(character: characterB) with { ExperienceGained = 900 };
        var service = CreateService(observationA, observationB);

        var snapshotA = service.GetLifetime(characterA);
        var snapshotB = service.GetLifetime(characterB);

        Assert.Equal(1, snapshotA.ObservationCount);
        Assert.Equal(10, snapshotA.ExperienceGained);
        Assert.Equal(1, snapshotB.ObservationCount);
        Assert.Equal(900, snapshotB.ExperienceGained);
    }

    [Fact]
    public void Aggregate_is_independent_of_observation_order()
    {
        var character = CharacterRecordId.CreateNew();
        CharacterPerformanceObservation[] observations =
        [
            CreateObservation(character: character) with { DamageDealt = new CombatScaledAmount(101) },
            CreateObservation(character: character, session: GameplaySessionId.CreateNew()) with
            {
                DamageDealt = new CombatScaledAmount(202)
            },
            CreateObservation(character: character, session: GameplaySessionId.CreateNew()) with
            {
                DamageDealt = new CombatScaledAmount(303)
            }
        ];

        var forward = CharacterHistoricalPerformanceReadService.Aggregate(character, observations);
        var reverse = CharacterHistoricalPerformanceReadService.Aggregate(
            character,
            observations.Reverse());

        Assert.Equal(forward, reverse);
    }

    [Fact]
    public void Large_totals_use_overflow_safe_aggregation_and_derived_math()
    {
        var character = CharacterRecordId.CreateNew();
        var large = long.MaxValue / 4;
        var first = CreateObservation(character: character, duration: TimeSpan.FromDays(1)) with
        {
            DamageDealt = new CombatScaledAmount(large),
            Attempts = large,
            Hits = large / 2,
            RolledAttempts = large,
            DisplayedChanceSumHundredths = large,
            RollSumHundredths = large,
            TotalDefeated = large,
            MyDefeats = large / 2,
            ExperienceGained = large,
            GameplayInfluenceGained = large
        };
        var second = first with
        {
            GameplaySessionId = GameplaySessionId.CreateNew()
        };

        var snapshot = CreateService(first, second).GetLifetime(character);

        Assert.Equal(checked(large * 2), snapshot.DamageDealt.Hundredths);
        Assert.Equal(checked(large * 2), snapshot.Attempts);
        Assert.Equal(checked((large / 2) * 2), snapshot.Hits);
        Assert.Equal(50.0m, snapshot.HitPercent);
        Assert.Equal(checked(large * 2), snapshot.TotalDefeated);
        Assert.Equal(checked(large * 2), snapshot.ExperienceGained);
        Assert.True(snapshot.DamagePerSecondHundredths > 0);
        Assert.True(snapshot.ExperiencePerHour > 0);
        Assert.True(snapshot.GameplayInfluencePerHour > 0);
    }

    [Fact]
    public void Non_positive_duration_repository_result_is_not_used()
    {
        var character = CharacterRecordId.CreateNew();
        var invalid = CreateObservation(character: character) with
        {
            EndedAtUtc = Start
        };

        var snapshot = CreateService(invalid).GetLifetime(character);

        Assert.False(snapshot.HasHistory);
        Assert.Equal(0, snapshot.ObservationCount);
    }

    private static CharacterHistoricalPerformanceReadService CreateService(
        params CharacterPerformanceObservation[] observations) =>
        new(new StubObservationRepository(observations));

    private static CharacterHistoricalPerformanceReadService CreateService(
        IEnumerable<CharacterPerformanceObservation> observations) =>
        new(new StubObservationRepository(observations));

    private static CharacterPerformanceObservation CreateObservation(
        CharacterRecordId? character = null,
        GameplaySessionId? session = null,
        int segmentOrdinal = 0,
        TimeSpan? duration = null) =>
        new()
        {
            CharacterRecordId = character ?? CharacterRecordId.CreateNew(),
            GameplaySessionId = session ?? GameplaySessionId.CreateNew(),
            SegmentOrdinal = segmentOrdinal,
            StartedAtUtc = Start,
            EndedAtUtc = Start + (duration ?? TimeSpan.FromMinutes(30))
        };

    private sealed class StubObservationRepository(
        IEnumerable<CharacterPerformanceObservation> observations)
        : ICharacterPerformanceObservationRepository
    {
        private readonly CharacterPerformanceObservation[] _observations = observations.ToArray();

        public string ObservationDirectory => string.Empty;

        public IReadOnlyList<string> MalformedFileReports => [];

        public event EventHandler? Changed
        {
            add { }
            remove { }
        }

        public CharacterPerformanceObservationWriteResult Persist(
            CharacterPerformanceObservation observation) =>
            throw new NotSupportedException();

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
            CharacterRecordId characterRecordId) => _observations;

        public IReadOnlyList<CharacterPerformanceObservation> GetAll() => _observations;
    }

    private static readonly DateTimeOffset Start =
        new(2026, 8, 18, 12, 0, 0, TimeSpan.Zero);
}
