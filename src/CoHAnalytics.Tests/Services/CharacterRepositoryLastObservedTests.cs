using System.IO;
using System.Text.Json;
using CoHAnalytics.Models;
using CoHAnalytics.Services;
using CoHAnalytics.Tests.Orchestration;

namespace CoHAnalytics.Tests.Services;

public sealed class CharacterRepositoryLastObservedTests
{
  private static CharacterRepository CreateRepository(out string dataDirectory, DateTimeOffset? start = null)
  {
    dataDirectory = Path.Combine(Path.GetTempPath(), "coh-analytics-last-observed", Guid.NewGuid().ToString("n"));
    Directory.CreateDirectory(dataDirectory);
    return new CharacterRepository(new CharacterRepositoryOptions
    {
      DataDirectory = dataDirectory,
      TimeProvider = new ManualTimeProvider(start ?? new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero))
    });
  }

  [Fact]
  public void RecordTrustedActivity_updates_last_observed_at()
  {
    var start = new DateTimeOffset(2026, 8, 14, 10, 0, 0, TimeSpan.Zero);
    var repository = CreateRepository(out _, start);
    var established = repository.EstablishTrustedFromWelcome("acct-a", "Alpha Hero");
    var activityAt = start.AddHours(2);

    var updated = repository.RecordTrustedActivity(established.RecordId!, activityAt);

    Assert.True(updated.IsSuccess);
    var record = repository.TryGetRecord(established.RecordId!);
    Assert.NotNull(record);
    Assert.Equal(activityAt, record!.LastObservedAt);
  }

  [Fact]
  public void RecordTrustedActivity_uses_latest_observation_timestamp()
  {
    var start = new DateTimeOffset(2026, 8, 14, 10, 0, 0, TimeSpan.Zero);
    var repository = CreateRepository(out _, start);
    var established = repository.EstablishTrustedFromWelcome("acct-a", "Alpha Hero");
    var earlier = start.AddHours(1);
    var later = start.AddHours(3);

    repository.RecordTrustedActivity(established.RecordId!, earlier);
    repository.RecordTrustedActivity(established.RecordId!, later);

    var record = repository.TryGetRecord(established.RecordId!);
    Assert.Equal(later, record!.LastObservedAt);
  }

  [Fact]
  public void RecordTrustedActivity_is_isolated_by_character_record_id()
  {
    var start = new DateTimeOffset(2026, 8, 14, 10, 0, 0, TimeSpan.Zero);
    var repository = CreateRepository(out _, start);
    var alpha = repository.EstablishTrustedFromWelcome("acct-a", "Alpha Hero");
    var beta = repository.EstablishTrustedFromWelcome("acct-a", "Beta Hero");
    var alphaActivity = start.AddHours(4);

    repository.RecordTrustedActivity(alpha.RecordId!, alphaActivity);

    Assert.Equal(alphaActivity, repository.TryGetRecord(alpha.RecordId!)!.LastObservedAt);
    Assert.Equal(start, repository.TryGetRecord(beta.RecordId!)!.LastObservedAt);
  }

  [Fact]
  public void RecordTrustedActivity_is_isolated_across_accounts()
  {
    var start = new DateTimeOffset(2026, 8, 14, 10, 0, 0, TimeSpan.Zero);
    var repository = CreateRepository(out _, start);
    var primaryHero = repository.EstablishTrustedFromWelcome("acct-primary", "Dawn's Vanguard");
    var altHero = repository.EstablishTrustedFromWelcome("acct-alt", "D4wn's Vanguard");
    var hellsActivity = start.AddHours(5);
    var altHeroActivity = start.AddHours(6);

    repository.RecordTrustedActivity(primaryHero.RecordId!, hellsActivity);
    repository.RecordTrustedActivity(altHero.RecordId!, altHeroActivity);

    Assert.Equal(hellsActivity, repository.TryGetRecord(primaryHero.RecordId!)!.LastObservedAt);
    Assert.Equal(altHeroActivity, repository.TryGetRecord(altHero.RecordId!)!.LastObservedAt);
  }

  [Fact]
  public void RecordObservedLevel_updates_last_observed_at_from_telemetry_timestamp()
  {
    var start = new DateTimeOffset(2026, 8, 14, 10, 0, 0, TimeSpan.Zero);
    var repository = CreateRepository(out _, start);
    var established = repository.EstablishTrustedFromWelcome("acct-a", "Alpha Hero");
    var levelAt = start.AddHours(3);

    repository.RecordObservedLevel(established.RecordId!, 50, levelAt);

    var record = repository.TryGetRecord(established.RecordId!);
    Assert.Equal(levelAt, record!.ObservedLevelObservedAt);
    Assert.Equal(levelAt, record.LastObservedAt);
  }

  [Fact]
  public void Rename_preserves_last_observed_at_on_same_record_id()
  {
    var start = new DateTimeOffset(2026, 8, 14, 10, 0, 0, TimeSpan.Zero);
    var repository = CreateRepository(out _, start);
    var established = repository.EstablishTrustedFromWelcome("acct-a", "Shadow Vanguard");
    var activityAt = start.AddHours(2);
    repository.RecordTrustedActivity(established.RecordId!, activityAt);

    repository.RecordTrustedObservedDisplayName(
        established.RecordId!,
        "D4wn's Vanguard",
        CharacterTrustState.TrustedFromWelcome);

    var record = repository.TryGetRecord(established.RecordId!);
    Assert.NotNull(record);
    Assert.Equal("D4wn's Vanguard", record!.CurrentDisplayName);
    Assert.Equal(activityAt, record.LastObservedAt);
  }

  [Fact]
  public void Load_recovers_last_observed_at_from_persisted_level_timestamp()
  {
    var dataDirectory = Path.Combine(Path.GetTempPath(), "coh-analytics-last-observed", Guid.NewGuid().ToString("n"));
    Directory.CreateDirectory(dataDirectory);
    var establishedAt = new DateTimeOffset(2026, 8, 10, 8, 0, 0, TimeSpan.Zero);
    var levelAt = new DateTimeOffset(2026, 8, 14, 16, 13, 45, TimeSpan.Zero);
    var recordId = Guid.NewGuid().ToString("n");

    var payload = new
    {
      schemaVersion = CharacterRepository.PersistenceSchemaVersion,
      records = new[]
      {
        new
        {
          recordId,
          accountStableId = "acct-a",
          normalizedCharacterName = "Dawn's Vanguard",
          currentDisplayName = "Dawn's Vanguard",
          firstObservedAt = establishedAt,
          lastObservedAt = default(DateTimeOffset),
          trustState = CharacterTrustState.TrustedFromWelcome,
          provenance = new
          {
            trustState = CharacterTrustState.TrustedFromWelcome,
            establishedAt = establishedAt,
            inferredObservationCount = 0
          },
          observedLevel = 50,
          observedLevelObservedAt = levelAt,
          aliases = Array.Empty<object>(),
          recordSchemaVersion = CharacterRecord.CurrentSchemaVersion
        }
      }
    };

    File.WriteAllText(
        Path.Combine(dataDirectory, "characters.json"),
        JsonSerializer.Serialize(payload));

    var repository = new CharacterRepository(new CharacterRepositoryOptions
    {
      DataDirectory = dataDirectory,
      TimeProvider = new ManualTimeProvider(levelAt)
    });

    var record = Assert.Single(repository.Current.Records);
    Assert.Equal(levelAt, record.LastObservedAt);
    Assert.Equal(establishedAt, record.FirstObservedAt);
  }

  [Fact]
  public void EstablishTrustedFromWelcome_uses_supplied_observation_timestamp()
  {
    var observedAt = new DateTimeOffset(2026, 8, 14, 15, 26, 37, TimeSpan.Zero);
    var repository = CreateRepository(out _);
    var established = repository.EstablishTrustedFromWelcome("acct-a", "Dawn's Vanguard", observedAt);
    var record = repository.TryGetRecord(established.RecordId!);

    Assert.Equal(observedAt, record!.LastObservedAt);
    Assert.Equal(observedAt, record.FirstObservedAt);
  }

  [Fact]
  public void Truly_unloaded_character_without_evidence_stays_unobserved_on_load()
  {
    var dataDirectory = Path.Combine(Path.GetTempPath(), "coh-analytics-last-observed", Guid.NewGuid().ToString("n"));
    Directory.CreateDirectory(dataDirectory);
    var payload = new
    {
      schemaVersion = CharacterRepository.PersistenceSchemaVersion,
      records = new[]
      {
        new
        {
          recordId = Guid.NewGuid().ToString("n"),
          accountStableId = "acct-a",
          normalizedCharacterName = "Ghost Hero",
          currentDisplayName = "Ghost Hero",
          firstObservedAt = default(DateTimeOffset),
          lastObservedAt = default(DateTimeOffset),
          trustState = CharacterTrustState.TrustedFromWelcome,
          provenance = new
          {
            trustState = CharacterTrustState.TrustedFromWelcome,
            establishedAt = default(DateTimeOffset),
            inferredObservationCount = 0
          },
          aliases = Array.Empty<object>(),
          recordSchemaVersion = CharacterRecord.CurrentSchemaVersion
        }
      }
    };

    File.WriteAllText(
        Path.Combine(dataDirectory, "characters.json"),
        JsonSerializer.Serialize(payload));

    var repository = new CharacterRepository(new CharacterRepositoryOptions
    {
      DataDirectory = dataDirectory,
      TimeProvider = new ManualTimeProvider()
    });

    var record = Assert.Single(repository.Current.Records);
    Assert.Equal(default, record.LastObservedAt);
    Assert.Equal(default, record.FirstObservedAt);
  }
}
