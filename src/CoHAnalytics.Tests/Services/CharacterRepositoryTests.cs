using System.IO;
using CoHAnalytics.Models;
using CoHAnalytics.Services;
using CoHAnalytics.Tests.Orchestration;

namespace CoHAnalytics.Tests.Services;

public sealed class CharacterRepositoryTests
{
  private static CharacterRepository CreateRepository(out string dataDirectory)
  {
    dataDirectory = Path.Combine(Path.GetTempPath(), "coh-analytics-character-repo", Guid.NewGuid().ToString("n"));
    Directory.CreateDirectory(dataDirectory);
    return new CharacterRepository(new CharacterRepositoryOptions
    {
      DataDirectory = dataDirectory,
      TimeProvider = new ManualTimeProvider()
    });
  }

  [Fact]
  public void EstablishTrustedFromWelcome_creates_trusted_record()
  {
    var repository = CreateRepository(out _);

    var result = repository.EstablishTrustedFromWelcome("acct-1", "Dawn's Vanguard");

    Assert.True(result.IsSuccess);
    var record = Assert.Single(repository.Current.Records);
    Assert.Equal(CharacterTrustState.TrustedFromWelcome, record.TrustState);
    Assert.Equal("Dawn's Vanguard", record.CurrentDisplayName);
    Assert.Equal("acct-1", record.AccountStableId);
    Assert.Equal(CharacterTrustState.TrustedFromWelcome, record.Provenance.TrustState);
  }

  [Fact]
  public void EstablishTrustedFromManualConfirmation_creates_trusted_record()
  {
    var repository = CreateRepository(out _);

    var result = repository.EstablishTrustedFromManualConfirmation("acct-1", "Alpha Hero");

    Assert.True(result.IsSuccess);
    var record = Assert.Single(repository.Current.Records);
    Assert.Equal(CharacterTrustState.TrustedFromManualConfirmation, record.TrustState);
  }

  [Fact]
  public void Repeated_welcome_for_same_account_and_name_updates_existing_record()
  {
    var repository = CreateRepository(out _);

    var first = repository.EstablishTrustedFromWelcome("acct-1", "Alpha Hero");
    var second = repository.EstablishTrustedFromWelcome("acct-1", "Alpha Hero");

    Assert.Equal(first.RecordId, second.RecordId);
    Assert.Single(repository.Current.Records);
  }

  [Fact]
  public void TryFindTrustedByDisplayName_matches_normalized_name_within_account()
  {
    var repository = CreateRepository(out _);
    repository.EstablishTrustedFromWelcome("acct-1", "  Alpha Hero  ");

    var found = repository.TryFindTrustedByDisplayName("acct-1", "Alpha Hero");

    Assert.NotNull(found);
    Assert.Equal("Alpha Hero", found!.CurrentDisplayName);
  }

  [Fact]
  public void RecordInferredObservation_updates_provenance_without_changing_trust()
  {
    var repository = CreateRepository(out _);
    var established = repository.EstablishTrustedFromWelcome("acct-1", "Alpha Hero");

    var result = repository.RecordInferredObservation(established.RecordId!);

    Assert.True(result.IsSuccess);
    var record = repository.TryGetRecord(established.RecordId!);
    Assert.NotNull(record);
    Assert.Equal(1, record!.Provenance.InferredObservationCount);
    Assert.NotNull(record.Provenance.LastInferredObservationAt);
    Assert.Equal(CharacterTrustState.TrustedFromWelcome, record.TrustState);
  }

  [Fact]
  public void Repository_snapshot_is_immutable_across_further_mutation()
  {
    var repository = CreateRepository(out _);
    repository.EstablishTrustedFromWelcome("acct-1", "Alpha Hero");
    var before = repository.Current;

    repository.EstablishTrustedFromWelcome("acct-2", "Beta Hero");

    Assert.Single(before.Records);
    Assert.Equal(2, repository.Current.RecordCount);
  }

  [Fact]
  public void StateChanged_is_raised_for_semantic_mutation()
  {
    var repository = CreateRepository(out _);
    var notifications = 0;
    repository.StateChanged += (_, _) => notifications++;

    repository.EstablishTrustedFromWelcome("acct-1", "Alpha Hero");

    Assert.Equal(1, notifications);
  }

  [Fact]
  public void Record_not_found_does_not_raise_state_changed()
  {
    var repository = CreateRepository(out _);
    var notifications = 0;
    repository.StateChanged += (_, _) => notifications++;

    var result = repository.RecordInferredObservation(CharacterRecordId.CreateNew());

    Assert.False(result.IsSuccess);
    Assert.Equal(0, notifications);
  }

  [Fact]
  public void Imported_build_paths_cannot_establish_trusted_records()
  {
    var repository = CreateRepository(out _);
    var observedAt = DateTimeOffset.Parse("2026-08-16T12:00:00Z");

    var unknownImport = repository.ImportBuildMetadata(
        CharacterRecordId.CreateNew(),
        "Fire Blast",
        "Energy Manipulation",
        "Blaster",
        currentBuildNumber: 2,
        observedAt);

    Assert.False(unknownImport.IsSuccess);
    Assert.Equal(CharacterRepositoryOutcome.RecordNotFound, unknownImport.Outcome);
    Assert.Empty(repository.Current.Records);

    var established = repository.EstablishTrustedFromWelcome("acct-1", "Alpha Hero", observedAt);
    Assert.True(established.IsSuccess);
    var trustBeforeImport = repository.TryGetRecord(established.RecordId!)!.TrustState;

    var trustedImport = repository.ImportBuildMetadata(
        established.RecordId!,
        "Fire Blast",
        "Energy Manipulation",
        "Blaster",
        currentBuildNumber: 2,
        observedAt.AddMinutes(1));

    Assert.True(trustedImport.IsSuccess);
    var record = Assert.Single(repository.Current.Records);
    Assert.Equal(established.RecordId, record.RecordId);
    Assert.Equal("acct-1", record.AccountStableId);
    Assert.Equal("Alpha Hero", record.CurrentDisplayName);
    Assert.Equal("Fire Blast", record.PrimaryPowerSet);
    Assert.Equal("Energy Manipulation", record.SecondaryPowerSet);
    Assert.Equal("Blaster", record.Archetype);
    Assert.Equal(2, record.CurrentBuildNumber);
    Assert.Equal(trustBeforeImport, record.TrustState);
    Assert.Equal(trustBeforeImport, record.Provenance.TrustState);
  }

  [Fact]
  public async Task Concurrent_establish_operations_remain_deterministic()
  {
    var repository = CreateRepository(out _);

    var results = await Task.WhenAll(
        Enumerable.Range(0, 20)
            .Select(index => Task.Run(() =>
                repository.EstablishTrustedFromWelcome("acct-1", $"Hero {index}"))));

    Assert.Equal(20, results.Count(result => result.IsSuccess));
    Assert.Equal(20, repository.Current.RecordCount);
  }
}
