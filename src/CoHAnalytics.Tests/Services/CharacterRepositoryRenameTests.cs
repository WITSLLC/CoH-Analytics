using System.IO;
using CoHAnalytics.Models;
using CoHAnalytics.Services;
using CoHAnalytics.Tests.Orchestration;

namespace CoHAnalytics.Tests.Services;

public sealed class CharacterRepositoryRenameTests
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
  public void Repeated_welcome_for_unchanged_name_preserves_record_id()
  {
    var repository = CreateRepository(out _);
    var first = repository.EstablishTrustedFromWelcome("acct-1", "Alpha Hero");
    var second = repository.EstablishTrustedFromWelcome("acct-1", "Alpha Hero");

    Assert.Equal(first.RecordId, second.RecordId);
    Assert.Single(repository.Current.Records);
    Assert.Equal("Alpha Hero", repository.Current.Records[0].CurrentDisplayName);
  }

  [Fact]
  public void Welcome_for_known_alias_preserves_record_id_and_updates_display_name()
  {
    var repository = CreateRepository(out _);
    var established = repository.EstablishTrustedFromWelcome("acct-1", "Shadow Vanguard");
    Assert.True(established.IsSuccess);

    repository.RecordTrustedObservedDisplayName(
        established.RecordId!,
        "D4wn's Vanguard",
        CharacterTrustState.TrustedFromWelcome);

    var renamed = repository.EstablishTrustedFromWelcome("acct-1", "D4wn's Vanguard");

    Assert.Equal(established.RecordId, renamed.RecordId);
    var record = Assert.Single(repository.Current.Records);
    Assert.Equal("D4wn's Vanguard", record.CurrentDisplayName);
    Assert.Contains(record.Aliases, alias => alias.DisplayName == "Shadow Vanguard");
  }

  [Fact]
  public void Welcome_for_unrelated_new_name_on_account_creates_separate_record()
  {
    var repository = CreateRepository(out _);
    repository.EstablishTrustedFromWelcome("acct-1", "Alpha Hero");

    var second = repository.EstablishTrustedFromWelcome("acct-1", "Beta Hero");

    Assert.Equal(2, repository.Current.RecordCount);
    Assert.NotEqual(
        repository.TryFindTrustedByDisplayName("acct-1", "Alpha Hero")!.RecordId,
        second.RecordId);
  }

  [Fact]
  public void TryFindTrustedByDisplayName_matches_prior_display_name_alias()
  {
    var repository = CreateRepository(out _);
    var established = repository.EstablishTrustedFromWelcome("acct-1", "Shadow Vanguard");
    repository.RecordTrustedObservedDisplayName(
        established.RecordId!,
        "D4wn's Vanguard",
        CharacterTrustState.TrustedFromWelcome);

    var found = repository.TryFindTrustedByDisplayName("acct-1", "Shadow Vanguard");

    Assert.NotNull(found);
    Assert.Equal(established.RecordId, found!.RecordId);
    Assert.Equal("D4wn's Vanguard", found.CurrentDisplayName);
  }

  [Fact]
  public void RecordTrustedObservedDisplayName_reconciles_existing_duplicate_display_name()
  {
    var repository = CreateRepository(out _);
    var shadow = repository.EstablishTrustedFromWelcome("acct-1", "Shadow Vanguard");
    var altHero = repository.EstablishTrustedFromWelcome("acct-1", "D4wn's Vanguard");

    Assert.Equal(2, repository.Current.RecordCount);

    var reconciled = repository.RecordTrustedObservedDisplayName(
        shadow.RecordId!,
        "D4wn's Vanguard",
        CharacterTrustState.TrustedFromManualConfirmation);

    Assert.True(reconciled.IsSuccess);
    Assert.Single(repository.Current.Records);
    var record = repository.TryGetRecord(shadow.RecordId!);
    Assert.NotNull(record);
    Assert.Equal("D4wn's Vanguard", record!.CurrentDisplayName);
    Assert.Contains(record.Aliases, alias => alias.DisplayName == "Shadow Vanguard");
    Assert.Null(repository.TryGetRecord(altHero.RecordId!));
  }

  [Fact]
  public void RecordTrustedObservedDisplayName_preserves_record_id_and_prior_alias()
  {
    var repository = CreateRepository(out _);
    var established = repository.EstablishTrustedFromWelcome("acct-1", "Shadow Vanguard");
    Assert.True(established.IsSuccess);

    var updated = repository.RecordTrustedObservedDisplayName(
        established.RecordId!,
        "D4wn's Vanguard",
        CharacterTrustState.TrustedFromManualConfirmation);

    Assert.True(updated.IsSuccess);
    var record = repository.TryGetRecord(established.RecordId!);
    Assert.NotNull(record);
    Assert.Equal("D4wn's Vanguard", record!.CurrentDisplayName);
    Assert.Equal(CharacterTrustState.TrustedFromManualConfirmation, record.TrustState);
    Assert.Contains(record.Aliases, alias => alias.DisplayName == "Shadow Vanguard");
  }

  [Fact]
  public void Welcome_rename_preserves_badge_acquisitions_on_same_record_id()
  {
    var repository = CreateRepository(out var characterDir);
    var badgeRepository = new CharacterBadgeAcquisitionRepository(
        new CharacterBadgeAcquisitionRepositoryOptions { DataDirectory = characterDir });

    var established = repository.EstablishTrustedFromWelcome("acct-1", "Shadow Vanguard");
    Assert.True(established.IsSuccess);

    badgeRepository.RecordAcquisition(
        established.RecordId!,
        "acct-1",
        "BADGE-00001",
        "Example Badge",
        DateTimeOffset.UtcNow);

    repository.RecordTrustedObservedDisplayName(
        established.RecordId!,
        "D4wn's Vanguard",
        CharacterTrustState.TrustedFromWelcome);
    repository.EstablishTrustedFromWelcome("acct-1", "D4wn's Vanguard");

    Assert.True(badgeRepository.IsBadgeAcquired(established.RecordId!, "BADGE-00001"));
    Assert.Equal(["BADGE-00001"], badgeRepository.GetAcquiredBadgeIds(established.RecordId!));
  }

  [Fact]
  public async Task Mid_session_manual_confirmation_with_new_observed_name_renames_selected_record()
  {
    var monitoring = new FakeMonitoringSessionManager();
    var parser = new GameplaySessionTestInfrastructure.FakeGameplayParserManager();
    var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dir);

    try
    {
      var contextId = MonitoringContextId.CreateNew();
      var source = GameplaySessionTestInfrastructure.DefaultSource("acct-1");
      monitoring.SetInitial(ParserTestSnapshots.Snapshot(
          1,
          GameplaySessionTestInfrastructure.ReadyContext(contextId, source)));

      var established = repository.EstablishTrustedFromWelcome("acct-1", "Shadow Vanguard");
      var other = repository.EstablishTrustedFromWelcome("acct-1", "Other Hero");

      var manager = await GameplaySessionTestInfrastructure.CreateStartedManager(
          monitoring,
          parser,
          repository);

      parser.PublishClassified([
        GameplaySessionTestInfrastructure.Classify(
            "2026-08-10 12:00:00 Welcome to City of Heroes, D4wn's Vanguard!",
            contextId,
            source,
            sequence: 1)
      ]);

      await GameplaySessionTestInfrastructure.WaitUntilAsync(
          () => manager.Current.Sessions.Any(session =>
              session.CharacterDisplayName == "D4wn's Vanguard"));

      var confirm = manager.ConfirmCharacter(contextId, established.RecordId!);
      Assert.True(confirm.IsSuccess);

      var record = repository.TryGetRecord(established.RecordId!);
      Assert.NotNull(record);
      Assert.Equal("D4wn's Vanguard", record!.CurrentDisplayName);
      Assert.Contains(record.Aliases, alias => alias.DisplayName == "Shadow Vanguard");
      Assert.Equal(2, repository.Current.RecordCount);
      Assert.Equal(other.RecordId, repository.TryFindTrustedByDisplayName("acct-1", "Other Hero")!.RecordId);
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
