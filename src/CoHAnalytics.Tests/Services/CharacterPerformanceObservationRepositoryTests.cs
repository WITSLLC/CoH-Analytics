using System.Text.Json.Nodes;
using CoHAnalytics.Models;
using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Services;

public sealed class CharacterPerformanceObservationRepositoryTests
{
    [Fact]
    public void Persist_and_reload_round_trips_complete_observation()
    {
        var directory = CreateDataDirectory();
        var observation = CreateObservation(
            GameplaySessionId.FromGuid(Guid.Parse("11111111-1111-1111-1111-111111111111")),
            CharacterRecordId.FromGuid(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")),
            segmentOrdinal: 7,
            startedAt: Start);

        try
        {
            var repository = new CharacterPerformanceObservationRepository(directory);
            var result = repository.Persist(observation);

            Assert.Equal(CharacterPerformanceObservationWriteOutcome.Persisted, result.Outcome);
            Assert.True(result.IsSuccess);
            Assert.NotNull(result.FilePath);
            Assert.True(File.Exists(result.FilePath));
            Assert.Equal(
                $"{observation.GameplaySessionId}_{observation.SegmentOrdinal:D10}.json",
                Path.GetFileName(result.FilePath));
            var document = JsonNode.Parse(File.ReadAllText(result.FilePath!))!.AsObject();
            Assert.Equal(2, document["schemaVersion"]!.GetValue<int>());
            Assert.True(document["includeInOverview"]!.GetValue<bool>());

            var reloaded = new CharacterPerformanceObservationRepository(directory);
            Assert.Equal(observation, Assert.Single(reloaded.GetAll()));
            Assert.Empty(reloaded.MalformedFileReports);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Version_1_document_without_inclusion_state_loads_as_included()
    {
        var directory = CreateDataDirectory();

        try
        {
            var writer = new CharacterPerformanceObservationRepository(directory);
            var result = writer.Persist(CreateObservation());
            var document = JsonNode.Parse(File.ReadAllText(result.FilePath!))!.AsObject();
            document["schemaVersion"] = 1;
            document.Remove("includeInOverview");
            File.WriteAllText(result.FilePath!, document.ToJsonString());

            var repository = new CharacterPerformanceObservationRepository(directory);
            var observation = Assert.Single(repository.GetAll());

            Assert.Equal(1, observation.SchemaVersion);
            Assert.True(observation.IncludeInOverview);
            var unchangedDocument = JsonNode.Parse(File.ReadAllText(result.FilePath!))!.AsObject();
            Assert.Equal(1, unchangedDocument["schemaVersion"]!.GetValue<int>());
            Assert.False(unchangedDocument.ContainsKey("includeInOverview"));

            Assert.True(repository.SetIncludeInOverview(
                observation.GameplaySessionId,
                observation.SegmentOrdinal,
                includeInOverview: false).IsSuccess);
            var updatedDocument = JsonNode.Parse(File.ReadAllText(result.FilePath!))!.AsObject();
            Assert.Equal(2, updatedDocument["schemaVersion"]!.GetValue<int>());
            Assert.False(updatedDocument["includeInOverview"]!.GetValue<bool>());
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Version_2_false_inclusion_state_round_trips_explicitly()
    {
        var directory = CreateDataDirectory();
        var excluded = CreateObservation() with { IncludeInOverview = false };

        try
        {
            var repository = new CharacterPerformanceObservationRepository(directory);
            var result = repository.Persist(excluded);
            var document = JsonNode.Parse(File.ReadAllText(result.FilePath!))!.AsObject();

            Assert.False(document["includeInOverview"]!.GetValue<bool>());
            Assert.Equal(
                excluded,
                Assert.Single(new CharacterPerformanceObservationRepository(directory).GetAll()));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Positive_duration_zero_activity_observation_persists()
    {
        var directory = CreateDataDirectory();
        var observation = CreateObservation() with
        {
            EndedAtUtc = Start.AddMinutes(15),
            DamageDealt = CombatScaledAmount.Zero,
            Attempts = 0,
            Hits = 0,
            RolledAttempts = 0,
            DisplayedChanceSumHundredths = 0,
            RollSumHundredths = 0,
            ForcedHits = 0,
            Autohits = 0,
            TotalDefeated = 0,
            MyDefeats = 0,
            ExperienceGained = 0,
            GameplayInfluenceGained = 0
        };

        try
        {
            var repository = new CharacterPerformanceObservationRepository(directory);
            Assert.Equal(
                CharacterPerformanceObservationWriteOutcome.Persisted,
                repository.Persist(observation).Outcome);

            var persisted = Assert.Single(
                new CharacterPerformanceObservationRepository(directory).GetAll());
            Assert.Equal(TimeSpan.FromMinutes(15), persisted.ObservedDuration);
            Assert.Equal(0, persisted.Misses);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Zero_duration_observation_is_rejected()
    {
        var directory = CreateDataDirectory();
        var observation = CreateObservation() with { EndedAtUtc = Start };

        try
        {
            var result = new CharacterPerformanceObservationRepository(directory).Persist(observation);

            Assert.Equal(CharacterPerformanceObservationWriteOutcome.InvalidObservation, result.Outcome);
            Assert.False(result.IsSuccess);
            Assert.Empty(GetObservationFiles(directory));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void End_before_start_is_rejected()
    {
        var directory = CreateDataDirectory();
        var observation = CreateObservation() with { EndedAtUtc = Start.AddTicks(-1) };

        try
        {
            var result = new CharacterPerformanceObservationRepository(directory).Persist(observation);

            Assert.Equal(CharacterPerformanceObservationWriteOutcome.InvalidObservation, result.Outcome);
            Assert.Empty(GetObservationFiles(directory));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Invalid_accuracy_invariants_are_rejected()
    {
        var directory = CreateDataDirectory();
        var valid = CreateObservation();
        CharacterPerformanceObservation[] invalid =
        [
            valid with { Attempts = 2, Hits = 3 },
            valid with { Attempts = 4, Hits = 2, ForcedHits = 3 },
            valid with { Attempts = 2, RolledAttempts = 3 },
            valid with { Attempts = -1 },
            valid with { Hits = -1 },
            valid with { RolledAttempts = -1 },
            valid with { DisplayedChanceSumHundredths = -1 },
            valid with { RollSumHundredths = -1 },
            valid with { ForcedHits = -1 },
            valid with { Autohits = -1 }
        ];

        try
        {
            var repository = new CharacterPerformanceObservationRepository(directory);

            Assert.All(invalid, observation => Assert.Equal(
                CharacterPerformanceObservationWriteOutcome.InvalidObservation,
                repository.Persist(observation).Outcome));
            Assert.Empty(repository.GetAll());
            Assert.Empty(GetObservationFiles(directory));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Invalid_enemy_invariants_are_rejected()
    {
        var directory = CreateDataDirectory();
        var valid = CreateObservation();
        CharacterPerformanceObservation[] invalid =
        [
            valid with { TotalDefeated = -1, MyDefeats = 0 },
            valid with { MyDefeats = -1 },
            valid with { TotalDefeated = 2, MyDefeats = 3 }
        ];

        try
        {
            var repository = new CharacterPerformanceObservationRepository(directory);

            Assert.All(invalid, observation => Assert.Equal(
                CharacterPerformanceObservationWriteOutcome.InvalidObservation,
                repository.Persist(observation).Outcome));
            Assert.Empty(repository.GetAll());
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Negative_damage_and_earnings_are_rejected()
    {
        var directory = CreateDataDirectory();
        var valid = CreateObservation();
        CharacterPerformanceObservation[] invalid =
        [
            valid with { DamageDealt = new CombatScaledAmount(-1) },
            valid with { ExperienceGained = -1 },
            valid with { GameplayInfluenceGained = -1 }
        ];

        try
        {
            var repository = new CharacterPerformanceObservationRepository(directory);

            Assert.All(invalid, observation => Assert.Equal(
                CharacterPerformanceObservationWriteOutcome.InvalidObservation,
                repository.Persist(observation).Outcome));
            Assert.Empty(repository.GetAll());
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Negative_segment_ordinal_is_rejected()
    {
        var directory = CreateDataDirectory();

        try
        {
            var result = new CharacterPerformanceObservationRepository(directory)
                .Persist(CreateObservation() with { SegmentOrdinal = -1 });

            Assert.Equal(CharacterPerformanceObservationWriteOutcome.InvalidObservation, result.Outcome);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Exact_duplicate_is_idempotent_across_repository_reload()
    {
        var directory = CreateDataDirectory();
        var observation = CreateObservation();

        try
        {
            var first = new CharacterPerformanceObservationRepository(directory).Persist(observation);
            var second = new CharacterPerformanceObservationRepository(directory).Persist(observation);

            Assert.Equal(CharacterPerformanceObservationWriteOutcome.Persisted, first.Outcome);
            Assert.Equal(CharacterPerformanceObservationWriteOutcome.Duplicate, second.Outcome);
            Assert.True(second.IsSuccess);
            Assert.Single(GetObservationFiles(directory));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Inclusion_update_targets_one_segment_survives_reload_and_is_idempotent()
    {
        var directory = CreateDataDirectory();
        var first = CreateObservation(segmentOrdinal: 0);
        var second = CreateObservation(
            GameplaySessionId.FromGuid(Guid.Parse("22222222-2222-2222-2222-222222222222")),
            first.CharacterRecordId,
            segmentOrdinal: 1,
            startedAt: Start.AddHours(1));

        try
        {
            var repository = new CharacterPerformanceObservationRepository(directory);
            Assert.True(repository.Persist(first).IsSuccess);
            Assert.True(repository.Persist(second).IsSuccess);
            var changeCount = 0;
            repository.Changed += (_, _) => changeCount++;

            var updated = repository.SetIncludeInOverview(
                first.GameplaySessionId,
                first.SegmentOrdinal,
                includeInOverview: false);
            var unchanged = repository.SetIncludeInOverview(
                first.GameplaySessionId,
                first.SegmentOrdinal,
                includeInOverview: false);

            Assert.Equal(CharacterPerformanceObservationUpdateOutcome.Updated, updated.Outcome);
            Assert.Equal(CharacterPerformanceObservationUpdateOutcome.Unchanged, unchanged.Outcome);
            Assert.True(updated.IsSuccess);
            Assert.True(unchanged.IsSuccess);
            Assert.Equal(1, changeCount);
            Assert.Equal(
                first with { IncludeInOverview = false },
                repository.GetAll().Single(item => item.SegmentOrdinal == 0));
            Assert.True(repository.GetAll().Single(item => item.SegmentOrdinal == 1).IncludeInOverview);

            var reloaded = new CharacterPerformanceObservationRepository(directory).GetAll();
            Assert.False(reloaded.Single(item => item.SegmentOrdinal == 0).IncludeInOverview);
            Assert.True(reloaded.Single(item => item.SegmentOrdinal == 1).IncludeInOverview);
            Assert.Equal(
                second,
                reloaded.Single(item => item.SegmentOrdinal == 1));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Failed_inclusion_replacement_preserves_disk_memory_and_changed_state()
    {
        var directory = CreateDataDirectory();
        var observation = CreateObservation();

        try
        {
            var writer = new CharacterPerformanceObservationRepository(directory);
            var persisted = writer.Persist(observation);
            var originalJson = File.ReadAllText(persisted.FilePath!);
            var repository = new CharacterPerformanceObservationRepository(
                directory,
                (_, _) => throw new IOException("Injected replacement failure."));
            var changeCount = 0;
            repository.Changed += (_, _) => changeCount++;

            var result = repository.SetIncludeInOverview(
                observation.GameplaySessionId,
                observation.SegmentOrdinal,
                includeInOverview: false);

            Assert.Equal(
                CharacterPerformanceObservationUpdateOutcome.PersistenceFailed,
                result.Outcome);
            Assert.False(result.IsSuccess);
            Assert.Equal(0, changeCount);
            Assert.True(Assert.Single(repository.GetAll()).IncludeInOverview);
            Assert.Equal(originalJson, File.ReadAllText(persisted.FilePath!));
            Assert.True(Assert.Single(
                new CharacterPerformanceObservationRepository(directory).GetAll()).IncludeInOverview);
            Assert.Empty(Directory.GetFiles(writer.ObservationDirectory, "*.tmp"));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Duplicate_capture_preserves_user_exclusion_without_conflict()
    {
        var directory = CreateDataDirectory();
        var observation = CreateObservation();

        try
        {
            var repository = new CharacterPerformanceObservationRepository(directory);
            Assert.True(repository.Persist(observation).IsSuccess);
            Assert.True(repository.SetIncludeInOverview(
                observation.GameplaySessionId,
                observation.SegmentOrdinal,
                includeInOverview: false).IsSuccess);

            var duplicate = repository.Persist(observation);

            Assert.Equal(CharacterPerformanceObservationWriteOutcome.Duplicate, duplicate.Outcome);
            Assert.False(Assert.Single(repository.GetAll()).IncludeInOverview);
            Assert.False(Assert.Single(
                new CharacterPerformanceObservationRepository(directory).GetAll()).IncludeInOverview);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Changed_is_raised_only_when_a_new_observation_is_persisted()
    {
        var directory = CreateDataDirectory();
        var observation = CreateObservation();

        try
        {
            var repository = new CharacterPerformanceObservationRepository(directory);
            var changeCount = 0;
            repository.Changed += (_, _) => throw new InvalidOperationException("subscriber failure");
            repository.Changed += (_, _) => changeCount++;

            Assert.Equal(
                CharacterPerformanceObservationWriteOutcome.Persisted,
                repository.Persist(observation).Outcome);
            Assert.Equal(
                CharacterPerformanceObservationWriteOutcome.Duplicate,
                repository.Persist(observation).Outcome);
            Assert.Equal(
                CharacterPerformanceObservationWriteOutcome.Conflict,
                repository.Persist(observation with { ExperienceGained = observation.ExperienceGained + 1 }).Outcome);
            Assert.Equal(
                CharacterPerformanceObservationWriteOutcome.InvalidObservation,
                repository.Persist(observation with { EndedAtUtc = observation.StartedAtUtc }).Outcome);

            Assert.Equal(1, changeCount);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Conflicting_duplicate_does_not_overwrite_original()
    {
        var directory = CreateDataDirectory();
        var original = CreateObservation();
        var conflicting = original with { ExperienceGained = original.ExperienceGained + 1 };

        try
        {
            var repository = new CharacterPerformanceObservationRepository(directory);
            Assert.True(repository.Persist(original).IsSuccess);

            var conflict = repository.Persist(conflicting);

            Assert.Equal(CharacterPerformanceObservationWriteOutcome.Conflict, conflict.Outcome);
            Assert.False(conflict.IsSuccess);
            Assert.Equal(original, Assert.Single(repository.GetAll()));
            Assert.Equal(
                original,
                Assert.Single(new CharacterPerformanceObservationRepository(directory).GetAll()));
            Assert.Single(GetObservationFiles(directory));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Character_query_returns_only_requested_character()
    {
        var directory = CreateDataDirectory();
        var characterA = CharacterRecordId.FromGuid(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
        var characterB = CharacterRecordId.FromGuid(
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"));
        var observationA1 = CreateObservation(characterRecordId: characterA, segmentOrdinal: 0);
        var observationA2 = CreateObservation(
            GameplaySessionId.FromGuid(Guid.Parse("22222222-2222-2222-2222-222222222222")),
            characterA,
            segmentOrdinal: 1,
            startedAt: Start.AddHours(1));
        var observationB = CreateObservation(
            GameplaySessionId.FromGuid(Guid.Parse("33333333-3333-3333-3333-333333333333")),
            characterB,
            segmentOrdinal: 0,
            startedAt: Start.AddHours(2));

        try
        {
            var repository = new CharacterPerformanceObservationRepository(directory);
            Assert.True(repository.Persist(observationA1).IsSuccess);
            Assert.True(repository.Persist(observationB).IsSuccess);
            Assert.True(repository.Persist(observationA2).IsSuccess);

            Assert.Equal([observationA1, observationA2], repository.GetByCharacter(characterA));
            Assert.Equal([observationB], repository.GetByCharacter(characterB));
            Assert.Equal(3, repository.GetAll().Count);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Query_order_is_start_session_then_segment()
    {
        var directory = CreateDataDirectory();
        var character = CharacterRecordId.CreateNew();
        var sessionA = GameplaySessionId.FromGuid(
            Guid.Parse("11111111-1111-1111-1111-111111111111"));
        var sessionB = GameplaySessionId.FromGuid(
            Guid.Parse("22222222-2222-2222-2222-222222222222"));
        var sessionC = GameplaySessionId.FromGuid(
            Guid.Parse("33333333-3333-3333-3333-333333333333"));
        var earlier = CreateObservation(sessionC, character, 0, Start.AddMinutes(-10));
        var a1 = CreateObservation(sessionA, character, 1, Start);
        var a2 = CreateObservation(sessionA, character, 2, Start);
        var b0 = CreateObservation(sessionB, character, 0, Start);

        try
        {
            var repository = new CharacterPerformanceObservationRepository(directory);
            Assert.True(repository.Persist(b0).IsSuccess);
            Assert.True(repository.Persist(a2).IsSuccess);
            Assert.True(repository.Persist(earlier).IsSuccess);
            Assert.True(repository.Persist(a1).IsSuccess);

            Assert.Equal([earlier, a1, a2, b0], repository.GetAll());
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Corrupt_file_is_reported_without_poisoning_valid_history()
    {
        var directory = CreateDataDirectory();
        var first = CreateObservation(segmentOrdinal: 0);
        var second = CreateObservation(
            GameplaySessionId.FromGuid(Guid.Parse("22222222-2222-2222-2222-222222222222")),
            first.CharacterRecordId,
            segmentOrdinal: 1,
            startedAt: Start.AddHours(1));

        try
        {
            var writer = new CharacterPerformanceObservationRepository(directory);
            Assert.True(writer.Persist(first).IsSuccess);
            Assert.True(writer.Persist(second).IsSuccess);
            File.WriteAllText(Path.Combine(writer.ObservationDirectory, "corrupt.json"), "{ not-json");

            var reloaded = new CharacterPerformanceObservationRepository(directory);

            Assert.Equal([first, second], reloaded.GetAll());
            Assert.Single(reloaded.MalformedFileReports);
            Assert.Contains("corrupt.json", reloaded.MalformedFileReports[0], StringComparison.OrdinalIgnoreCase);

            var third = CreateObservation(
                GameplaySessionId.FromGuid(Guid.Parse("33333333-3333-3333-3333-333333333333")),
                first.CharacterRecordId,
                segmentOrdinal: 2,
                startedAt: Start.AddHours(2));
            Assert.True(reloaded.Persist(third).IsSuccess);
            Assert.Equal(3, reloaded.GetAll().Count);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Unsupported_future_schema_is_reported_without_poisoning_valid_history()
    {
        var directory = CreateDataDirectory();
        var valid = CreateObservation();

        try
        {
            var writer = new CharacterPerformanceObservationRepository(directory);
            var persisted = writer.Persist(valid);
            Assert.True(persisted.IsSuccess);
            var futureJson = File.ReadAllText(persisted.FilePath!);
            File.WriteAllText(
                Path.Combine(writer.ObservationDirectory, "future.json"),
                futureJson.Replace(
                    "\"schemaVersion\": 2",
                    "\"schemaVersion\": 3",
                    StringComparison.Ordinal));

            var reloaded = new CharacterPerformanceObservationRepository(directory);

            Assert.Equal(valid, Assert.Single(reloaded.GetAll()));
            Assert.Single(reloaded.MalformedFileReports);
            Assert.Contains("unsupported schemaVersion 3", reloaded.MalformedFileReports[0]);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Successful_persistence_leaves_no_temporary_file()
    {
        var directory = CreateDataDirectory();

        try
        {
            var repository = new CharacterPerformanceObservationRepository(directory);
            Assert.True(repository.Persist(CreateObservation()).IsSuccess);

            Assert.Empty(Directory.GetFiles(repository.ObservationDirectory, "*.tmp"));
            Assert.Single(Directory.GetFiles(repository.ObservationDirectory, "*.json"));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Repository_does_not_prune_observations()
    {
        var directory = CreateDataDirectory();
        var character = CharacterRecordId.CreateNew();

        try
        {
            var repository = new CharacterPerformanceObservationRepository(directory);
            for (var index = 0; index < 20; index++)
            {
                var observation = CreateObservation(
                    GameplaySessionId.FromGuid(Guid.Parse($"00000000-0000-0000-0000-{index + 1:D12}")),
                    character,
                    segmentOrdinal: index,
                    startedAt: Start.AddHours(index));
                Assert.True(repository.Persist(observation).IsSuccess);
            }

            Assert.Equal(20, repository.GetAll().Count);
            Assert.Equal(20, GetObservationFiles(directory).Length);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Delete_removes_only_exact_segment_survives_reload_and_publishes_once()
    {
        var directory = CreateDataDirectory();
        var session = GameplaySessionId.CreateNew();
        var otherSession = GameplaySessionId.CreateNew();

        try
        {
            var repository = new CharacterPerformanceObservationRepository(directory);
            var deleted = CreateObservation(session, segmentOrdinal: 0);
            var sibling = CreateObservation(session, segmentOrdinal: 1, startedAt: Start.AddHours(1));
            var unrelated = CreateObservation(otherSession, segmentOrdinal: 0, startedAt: Start.AddHours(2));
            Assert.True(repository.Persist(deleted).IsSuccess);
            Assert.True(repository.Persist(sibling).IsSuccess);
            Assert.True(repository.Persist(unrelated).IsSuccess);
            Assert.True(repository.SetIncludeInOverview(
                sibling.GameplaySessionId,
                sibling.SegmentOrdinal,
                includeInOverview: false).IsSuccess);

            var changedCount = 0;
            repository.Changed += (_, _) => changedCount++;

            var result = repository.Delete(deleted.GameplaySessionId, deleted.SegmentOrdinal);

            Assert.Equal(CharacterPerformanceObservationDeleteOutcome.Deleted, result.Outcome);
            Assert.True(result.IsSuccess);
            Assert.Equal(1, changedCount);
            Assert.Equal(2, repository.GetAll().Count);
            Assert.DoesNotContain(repository.GetAll(), item =>
                item.GameplaySessionId == deleted.GameplaySessionId &&
                item.SegmentOrdinal == deleted.SegmentOrdinal);
            Assert.False(repository.GetAll().Single(item =>
                item.GameplaySessionId == sibling.GameplaySessionId &&
                item.SegmentOrdinal == sibling.SegmentOrdinal).IncludeInOverview);

            var reloaded = new CharacterPerformanceObservationRepository(directory);
            Assert.Equal(2, reloaded.GetAll().Count);
            Assert.False(reloaded.GetAll().Single(item =>
                item.GameplaySessionId == sibling.GameplaySessionId &&
                item.SegmentOrdinal == sibling.SegmentOrdinal).IncludeInOverview);
            Assert.Contains(reloaded.GetAll(), item =>
                item.GameplaySessionId == unrelated.GameplaySessionId &&
                item.SegmentOrdinal == unrelated.SegmentOrdinal);
            Assert.Equal(2, GetObservationFiles(directory).Length);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Delete_failure_preserves_disk_memory_and_publishes_nothing()
    {
        var directory = CreateDataDirectory();

        try
        {
            var writer = new CharacterPerformanceObservationRepository(directory);
            var observation = CreateObservation();
            var persisted = writer.Persist(observation);
            Assert.True(persisted.IsSuccess);
            var originalJson = File.ReadAllText(persisted.FilePath!);

            var repository = new CharacterPerformanceObservationRepository(
                directory,
                (temporaryPath, finalPath) => File.Move(temporaryPath, finalPath, overwrite: true),
                _ => throw new IOException("Injected deletion failure."));
            var changedCount = 0;
            repository.Changed += (_, _) => changedCount++;

            var result = repository.Delete(observation.GameplaySessionId, observation.SegmentOrdinal);

            Assert.Equal(CharacterPerformanceObservationDeleteOutcome.PersistenceFailed, result.Outcome);
            Assert.False(result.IsSuccess);
            Assert.Equal(0, changedCount);
            Assert.Equal(observation, Assert.Single(repository.GetAll()));
            Assert.Equal(originalJson, File.ReadAllText(persisted.FilePath!));
            Assert.Equal(observation, Assert.Single(
                new CharacterPerformanceObservationRepository(directory).GetAll()));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Delete_reconciles_missing_managed_file_and_not_found_is_idempotent()
    {
        var directory = CreateDataDirectory();

        try
        {
            var repository = new CharacterPerformanceObservationRepository(directory);
            var observation = CreateObservation();
            var persisted = repository.Persist(observation);
            Assert.True(persisted.IsSuccess);
            File.Delete(persisted.FilePath!);
            var changedCount = 0;
            repository.Changed += (_, _) => changedCount++;

            var reconciled = repository.Delete(observation.GameplaySessionId, observation.SegmentOrdinal);
            var repeated = repository.Delete(observation.GameplaySessionId, observation.SegmentOrdinal);

            Assert.Equal(CharacterPerformanceObservationDeleteOutcome.Deleted, reconciled.Outcome);
            Assert.Equal(CharacterPerformanceObservationDeleteOutcome.NotFound, repeated.Outcome);
            Assert.True(repeated.IsSuccess);
            Assert.Equal(1, changedCount);
            Assert.Empty(repository.GetAll());
            Assert.Empty(new CharacterPerformanceObservationRepository(directory).GetAll());
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Delete_API_accepts_only_stable_segment_identity()
    {
        var method = typeof(ICharacterPerformanceObservationRepository).GetMethod(
            nameof(ICharacterPerformanceObservationRepository.Delete));

        Assert.NotNull(method);
        var parameters = method.GetParameters();
        Assert.Collection(
            parameters,
            parameter => Assert.Equal(typeof(GameplaySessionId), parameter.ParameterType),
            parameter => Assert.Equal(typeof(int), parameter.ParameterType));
        Assert.DoesNotContain(parameters, parameter => parameter.ParameterType == typeof(string));
    }

    private static CharacterPerformanceObservation CreateObservation(
        GameplaySessionId? gameplaySessionId = null,
        CharacterRecordId? characterRecordId = null,
        int segmentOrdinal = 0,
        DateTimeOffset? startedAt = null)
    {
        var start = startedAt ?? Start;
        return new CharacterPerformanceObservation
        {
            GameplaySessionId = gameplaySessionId ?? GameplaySessionId.FromGuid(
                Guid.Parse("11111111-1111-1111-1111-111111111111")),
            SegmentOrdinal = segmentOrdinal,
            CharacterRecordId = characterRecordId ?? CharacterRecordId.FromGuid(
                Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")),
            StartedAtUtc = start,
            EndedAtUtc = start.AddMinutes(30),
            DamageDealt = new CombatScaledAmount(1_234_567),
            Attempts = 100,
            Hits = 90,
            RolledAttempts = 95,
            DisplayedChanceSumHundredths = 902_500,
            RollSumHundredths = 450_000,
            ForcedHits = 5,
            Autohits = 3,
            TotalDefeated = 20,
            MyDefeats = 17,
            ExperienceGained = 250_000,
            GameplayInfluenceGained = 50_000
        };
    }

    private static string CreateDataDirectory()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "coh-performance-observations",
            Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string[] GetObservationFiles(string dataDirectory)
    {
        var directory = ApplicationDataPaths.GetCharacterPerformanceObservationsDirectory(dataDirectory);
        return Directory.Exists(directory)
            ? Directory.GetFiles(directory, "*.json")
            : [];
    }

    private static void DeleteDirectory(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch
        {
        }
    }

    private static readonly DateTimeOffset Start =
        new(2026, 8, 18, 12, 0, 0, TimeSpan.Zero);
}
