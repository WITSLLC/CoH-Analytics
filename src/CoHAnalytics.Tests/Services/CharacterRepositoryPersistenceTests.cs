using System.IO;
using System.Text.Json;
using CoHAnalytics.Models;
using CoHAnalytics.Services;
using CoHAnalytics.Tests.Orchestration;

namespace CoHAnalytics.Tests.Services;

public sealed class CharacterRepositoryPersistenceTests
{
  private static string CreateDataDirectory()
  {
    var directory = Path.Combine(Path.GetTempPath(), "coh-analytics-character-repo", Guid.NewGuid().ToString("n"));
    Directory.CreateDirectory(directory);
    return directory;
  }

  private static CharacterRepository CreateRepository(string dataDirectory) =>
      new(new CharacterRepositoryOptions
      {
        DataDirectory = dataDirectory,
        TimeProvider = new ManualTimeProvider()
      });

  [Fact]
  public void Persisted_records_reload_across_repository_instances()
  {
    var dataDirectory = CreateDataDirectory();
    var writer = CreateRepository(dataDirectory);
    var established = writer.EstablishTrustedFromWelcome("acct-1", "Alpha Hero");
    Assert.True(established.IsSuccess);

    var reader = CreateRepository(dataDirectory);
    var record = reader.TryGetRecord(established.RecordId!);

    Assert.NotNull(record);
    Assert.Equal("Alpha Hero", record!.CurrentDisplayName);
    Assert.Equal(CharacterTrustState.TrustedFromWelcome, record.TrustState);
  }

  [Fact]
  public void Atomic_save_uses_temporary_file_and_leaves_valid_json()
  {
    var dataDirectory = CreateDataDirectory();
    var repository = CreateRepository(dataDirectory);
    repository.EstablishTrustedFromWelcome("acct-1", "Alpha Hero");

    var path = repository.GetDiagnostics().PersistencePath;
    Assert.True(File.Exists(path));
    Assert.False(File.Exists(path + ".tmp"));

    var json = File.ReadAllText(path);
    using var document = JsonDocument.Parse(json);
    Assert.Equal(CharacterRepository.PersistenceSchemaVersion, document.RootElement.GetProperty("schemaVersion").GetInt32());
  }

  [Fact]
  public void Corrupt_repository_file_is_isolated_and_startup_continues_empty()
  {
    var dataDirectory = CreateDataDirectory();
    var path = Path.Combine(dataDirectory, "characters.json");
    File.WriteAllText(path, "{ this is not valid json");

    var repository = CreateRepository(dataDirectory);

    Assert.Empty(repository.Current.Records);
    Assert.True(repository.GetDiagnostics().RepositoryFileCorrupted);
  }

  [Fact]
  public void Unsupported_schema_version_is_isolated()
  {
    var dataDirectory = CreateDataDirectory();
    var path = Path.Combine(dataDirectory, "characters.json");
    File.WriteAllText(path, JsonSerializer.Serialize(new { schemaVersion = 999, records = new List<object>() }));

    var repository = CreateRepository(dataDirectory);

    Assert.Empty(repository.Current.Records);
    Assert.True(repository.GetDiagnostics().RepositoryFileCorrupted);
  }

  [Fact]
  public void Invalid_records_are_skipped_without_contaminating_valid_records()
  {
    var dataDirectory = CreateDataDirectory();
    var path = Path.Combine(dataDirectory, "characters.json");
    var payload = new
    {
      schemaVersion = CharacterRepository.PersistenceSchemaVersion,
      records = new object[]
      {
        new
        {
          recordId = Guid.NewGuid().ToString("n"),
          accountStableId = "acct-1",
          normalizedCharacterName = "Alpha Hero",
          currentDisplayName = "Alpha Hero",
          firstObservedAt = DateTimeOffset.UtcNow,
          lastObservedAt = DateTimeOffset.UtcNow,
          trustState = CharacterTrustState.TrustedFromWelcome,
          provenance = new
          {
            trustState = CharacterTrustState.TrustedFromWelcome,
            establishedAt = DateTimeOffset.UtcNow,
            inferredObservationCount = 0
          },
          aliases = Array.Empty<object>(),
          recordSchemaVersion = CharacterRecord.CurrentSchemaVersion
        },
        new
        {
          recordId = "not-a-guid",
          accountStableId = "acct-2",
          currentDisplayName = "Broken",
          firstObservedAt = DateTimeOffset.UtcNow,
          lastObservedAt = DateTimeOffset.UtcNow,
          trustState = CharacterTrustState.TrustedFromWelcome,
          recordSchemaVersion = CharacterRecord.CurrentSchemaVersion
        }
      }
    };

    File.WriteAllText(path, JsonSerializer.Serialize(payload));

    var repository = CreateRepository(dataDirectory);

    Assert.Single(repository.Current.Records);
    Assert.Equal(1, repository.Current.SkippedCorruptRecordCount);
  }

  [Fact]
  public void Persisted_json_contains_no_raw_chat_payload_fields()
  {
    var dataDirectory = CreateDataDirectory();
    var repository = CreateRepository(dataDirectory);
    repository.EstablishTrustedFromWelcome("acct-1", "Alpha Hero");

    var json = File.ReadAllText(repository.GetDiagnostics().PersistencePath);

    Assert.DoesNotContain("Welcome to City of Heroes", json, StringComparison.Ordinal);
    Assert.DoesNotContain("rawLine", json, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("parser", json, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("chatlog", json, StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public void Persistence_failure_retains_in_memory_record_and_reports_unsaved_state()
  {
    var dataDirectory = CreateDataDirectory();
    var repository = new CharacterRepository(new CharacterRepositoryOptions
    {
      DataDirectory = dataDirectory,
      TimeProvider = new ManualTimeProvider(),
      SimulatePersistenceFailure = true
    });

    var result = repository.EstablishTrustedFromWelcome("acct-1", "Alpha Hero");

    Assert.False(result.IsSuccess);
    Assert.Equal(CharacterRepositoryOutcome.PersistenceFailed, result.Outcome);
    Assert.Single(repository.Current.Records);
    Assert.True(repository.GetDiagnostics().HasUnsavedChanges);
    Assert.NotNull(repository.TryFindTrustedByDisplayName("acct-1", "Alpha Hero"));
  }

  [Fact]
  public void Casing_only_lookup_matches_existing_record()
  {
    var dataDirectory = CreateDataDirectory();
    var repository = CreateRepository(dataDirectory);
    var established = repository.EstablishTrustedFromWelcome("acct-1", "Dawn's Vanguard");
    Assert.True(established.IsSuccess);

    var found = repository.TryFindTrustedByDisplayName("acct-1", "DAWN'S VANGUARD");
    Assert.NotNull(found);
    Assert.Equal(established.RecordId, found!.RecordId);
  }

  [Fact]
  public void Casing_only_establishment_updates_existing_record()
  {
    var dataDirectory = CreateDataDirectory();
    var repository = CreateRepository(dataDirectory);
    var first = repository.EstablishTrustedFromWelcome("acct-1", "Dawn's Vanguard");
    var second = repository.EstablishTrustedFromWelcome("acct-1", "dawn's vanguard");

    Assert.Equal(first.RecordId, second.RecordId);
    Assert.True(second.IsSuccess);
    Assert.Single(repository.Current.Records);
    Assert.Equal("dawn's vanguard", repository.TryGetRecord(first.RecordId!)!.CurrentDisplayName);
  }

  [Fact]
  public void Different_punctuation_remains_distinct()
  {
    var dataDirectory = CreateDataDirectory();
    var repository = CreateRepository(dataDirectory);
    repository.EstablishTrustedFromWelcome("acct-1", "Dawn's Vanguard");
    var other = repository.EstablishTrustedFromWelcome("acct-1", "Dawns Vanguard");

    Assert.True(other.IsSuccess);
    Assert.Equal(2, repository.Current.RecordCount);
  }

  [Fact]
  public void Decomposed_unicode_reloads_as_form_c()
  {
    var dataDirectory = CreateDataDirectory();
    var path = Path.Combine(dataDirectory, "characters.json");
    var decomposed = "e\u0301" + " Hero";
    var recordId = Guid.NewGuid().ToString("n");
    var now = DateTimeOffset.UtcNow;
    var payload = new
    {
      schemaVersion = CharacterRepository.PersistenceSchemaVersion,
      records = new[]
      {
        new
        {
          recordId,
          accountStableId = "acct-1",
          normalizedCharacterName = decomposed.ToLowerInvariant(),
          currentDisplayName = decomposed,
          firstObservedAt = now,
          lastObservedAt = now,
          trustState = CharacterTrustState.TrustedFromWelcome,
          provenance = new
          {
            trustState = CharacterTrustState.TrustedFromWelcome,
            establishedAt = now,
            inferredObservationCount = 0
          },
          aliases = Array.Empty<object>(),
          recordSchemaVersion = CharacterRecord.CurrentSchemaVersion
        }
      }
    };

    File.WriteAllText(path, JsonSerializer.Serialize(payload));

    var repository = CreateRepository(dataDirectory);
    var record = repository.Current.Records.Single();
    var composed = "é Hero";
    Assert.Equal(composed, record.CurrentDisplayName);
    Assert.Equal(CharacterNameNormalizer.Normalize(composed), record.NormalizedCharacterName);
  }

  [Fact]
  public void Casing_only_duplicates_consolidate_on_load()
  {
    var dataDirectory = CreateDataDirectory();
    var path = Path.Combine(dataDirectory, "characters.json");
    var earlier = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
    var later = DateTimeOffset.Parse("2026-02-01T00:00:00Z");
    var survivorId = Guid.NewGuid().ToString("n");
    var duplicateId = Guid.NewGuid().ToString("n");
    var payload = new
    {
      schemaVersion = CharacterRepository.PersistenceSchemaVersion,
      records = new[]
      {
        new
        {
          recordId = survivorId,
          accountStableId = "acct-1",
          normalizedCharacterName = "alpha hero",
          currentDisplayName = "Alpha Hero",
          firstObservedAt = earlier,
          lastObservedAt = earlier,
          trustState = CharacterTrustState.TrustedFromWelcome,
          provenance = new
          {
            trustState = CharacterTrustState.TrustedFromWelcome,
            establishedAt = earlier,
            inferredObservationCount = 1,
            lastInferredObservationAt = (DateTimeOffset?)null
          },
          aliases = Array.Empty<object>(),
          recordSchemaVersion = CharacterRecord.CurrentSchemaVersion
        },
        new
        {
          recordId = duplicateId,
          accountStableId = "acct-1",
          normalizedCharacterName = "ALPHA HERO",
          currentDisplayName = "alpha hero",
          firstObservedAt = later,
          lastObservedAt = later,
          trustState = CharacterTrustState.TrustedFromWelcome,
          provenance = new
          {
            trustState = CharacterTrustState.TrustedFromWelcome,
            establishedAt = later,
            inferredObservationCount = 2,
            lastInferredObservationAt = (DateTimeOffset?)later
          },
          aliases = Array.Empty<object>(),
          recordSchemaVersion = CharacterRecord.CurrentSchemaVersion
        }
      }
    };

    File.WriteAllText(path, JsonSerializer.Serialize(payload));

    var repository = CreateRepository(dataDirectory);
    Assert.Single(repository.Current.Records);
    var record = repository.Current.Records.Single();
    Assert.Equal(CharacterRecordId.FromGuid(Guid.Parse(survivorId)), record.RecordId);
    Assert.Equal("alpha hero", record.CurrentDisplayName);
    Assert.Equal(earlier, record.FirstObservedAt);
    Assert.Equal(later, record.LastObservedAt);
    Assert.Equal(3, record.Provenance.InferredObservationCount);
  }

  [Fact]
  public void Canonical_rewrite_is_idempotent_on_second_load()
  {
    var dataDirectory = CreateDataDirectory();
    var path = Path.Combine(dataDirectory, "characters.json");
    var now = DateTimeOffset.UtcNow;
    File.WriteAllText(path, JsonSerializer.Serialize(new
    {
      schemaVersion = CharacterRepository.PersistenceSchemaVersion,
      records = new[]
      {
        new
        {
          recordId = Guid.NewGuid().ToString("n"),
          accountStableId = "acct-1",
          normalizedCharacterName = "  alpha hero  ",
          currentDisplayName = "  Alpha Hero  ",
          firstObservedAt = now,
          lastObservedAt = now,
          trustState = CharacterTrustState.TrustedFromWelcome,
          provenance = new
          {
            trustState = CharacterTrustState.TrustedFromWelcome,
            establishedAt = now,
            inferredObservationCount = 0
          },
          aliases = Array.Empty<object>(),
          recordSchemaVersion = CharacterRecord.CurrentSchemaVersion
        }
      }
    }));

    CreateRepository(dataDirectory);
    var firstWrite = File.ReadAllText(path);
    CreateRepository(dataDirectory);
    var secondWrite = File.ReadAllText(path);
    Assert.Equal(firstWrite, secondWrite);
  }

  [Fact]
  public void Rewrite_failure_preserves_canonical_in_memory_state()
  {
    var dataDirectory = CreateDataDirectory();
    var path = Path.Combine(dataDirectory, "characters.json");
    var now = DateTimeOffset.UtcNow;
    var decomposedDisplay = "e\u0301 Hero";
    File.WriteAllText(path, JsonSerializer.Serialize(new
    {
      schemaVersion = CharacterRepository.PersistenceSchemaVersion,
      records = new[]
      {
        new
        {
          recordId = Guid.NewGuid().ToString("n"),
          accountStableId = "acct-1",
          normalizedCharacterName = "wrong-normalized",
          currentDisplayName = decomposedDisplay,
          firstObservedAt = now,
          lastObservedAt = now,
          trustState = CharacterTrustState.TrustedFromWelcome,
          provenance = new
          {
            trustState = CharacterTrustState.TrustedFromWelcome,
            establishedAt = now,
            inferredObservationCount = 0
          },
          aliases = Array.Empty<object>(),
          recordSchemaVersion = CharacterRecord.CurrentSchemaVersion
        }
      }
    }));

    var originalJson = File.ReadAllText(path);
    var repository = new CharacterRepository(new CharacterRepositoryOptions
    {
      DataDirectory = dataDirectory,
      TimeProvider = new ManualTimeProvider(),
      SimulatePersistenceFailure = true
    });

    Assert.Equal("é Hero", repository.Current.Records.Single().CurrentDisplayName);
    Assert.True(repository.GetDiagnostics().HasUnsavedChanges);
    Assert.Equal(originalJson, File.ReadAllText(path));
  }

  [Fact]
  public void Duplicate_consolidation_bounds_and_canonicalizes_aliases_deterministically()
  {
    var dataDirectory = CreateDataDirectory();
    var path = Path.Combine(dataDirectory, "characters.json");
    var first = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
    var second = first.AddDays(1);
    var third = first.AddDays(2);
    var fourth = first.AddDays(3);
    var fifth = first.AddDays(4);
    var sixth = first.AddDays(5);
    var survivorId = Guid.NewGuid().ToString("n");
    var duplicateId = Guid.NewGuid().ToString("n");
    object Alias(string displayName, DateTimeOffset observedAt) => new
    {
      displayName,
      firstObservedAt = observedAt,
      lastObservedAt = observedAt
    };
    object Provenance(DateTimeOffset establishedAt) => new
    {
      trustState = CharacterTrustState.TrustedFromWelcome,
      establishedAt,
      inferredObservationCount = 0
    };

    File.WriteAllText(path, JsonSerializer.Serialize(new
    {
      schemaVersion = CharacterRepository.PersistenceSchemaVersion,
      records = new object[]
      {
        new
        {
          recordId = survivorId,
          accountStableId = "acct-1",
          normalizedCharacterName = "Bounded Hero",
          currentDisplayName = "Bounded Hero",
          firstObservedAt = first,
          lastObservedAt = third,
          trustState = CharacterTrustState.TrustedFromWelcome,
          provenance = Provenance(first),
          aliases = new[]
          {
            Alias("e\u0301 Alias", first),
            Alias("Punct-One", second),
            Alias("Later One", sixth)
          },
          recordSchemaVersion = CharacterRecord.CurrentSchemaVersion
        },
        new
        {
          recordId = duplicateId,
          accountStableId = "acct-1",
          normalizedCharacterName = "bounded hero",
          currentDisplayName = "bounded hero",
          firstObservedAt = second,
          lastObservedAt = sixth,
          trustState = CharacterTrustState.TrustedFromWelcome,
          provenance = Provenance(second),
          aliases = new[]
          {
            Alias("É ALIAS", third),
            Alias("Punct One", third),
            Alias("Keep Four", fourth),
            Alias("Excess Five", fifth),
            Alias("   ", sixth)
          },
          recordSchemaVersion = CharacterRecord.CurrentSchemaVersion
        }
      }
    }));

    var options = new CharacterRepositoryOptions
    {
      DataDirectory = dataDirectory,
      TimeProvider = new ManualTimeProvider(),
      MaximumAliasesPerCharacter = 4
    };
    var repository = new CharacterRepository(options);
    var record = Assert.Single(repository.Current.Records);
    var aliases = record.Aliases.Select(alias => alias.DisplayName).ToArray();

    Assert.Equal(4, aliases.Length);
    Assert.Equal(["É ALIAS", "Punct-One", "Punct One", "Keep Four"], aliases);
    Assert.Single(aliases, alias =>
        CharacterNameNormalizer.NamesMatch(
            CharacterNameNormalizer.Normalize(alias),
            CharacterNameNormalizer.Normalize("é alias")));
    Assert.Contains("Punct-One", aliases);
    Assert.Contains("Punct One", aliases);
    Assert.True(repository.GetDiagnostics().DiscardedAliasCount >= 4);
    Assert.All(repository.GetDiagnostics().RecentOperations, operation =>
    {
      Assert.DoesNotContain("É ALIAS", operation, StringComparison.Ordinal);
      Assert.DoesNotContain("Punct-One", operation, StringComparison.Ordinal);
      Assert.DoesNotContain("Excess Five", operation, StringComparison.Ordinal);
    });

    var firstCanonicalJson = File.ReadAllText(path);
    var reloaded = new CharacterRepository(options);
    Assert.Equal(aliases, reloaded.Current.Records.Single().Aliases.Select(alias => alias.DisplayName));
    Assert.Equal(0, reloaded.GetDiagnostics().DiscardedAliasCount);
    Assert.Equal(firstCanonicalJson, File.ReadAllText(path));
  }

  [Fact]
  public void Alias_limit_must_be_positive()
  {
    var dataDirectory = CreateDataDirectory();
    Assert.Throws<ArgumentOutOfRangeException>(() => new CharacterRepository(
        new CharacterRepositoryOptions
        {
          DataDirectory = dataDirectory,
          MaximumAliasesPerCharacter = 0
        }));
  }
}
