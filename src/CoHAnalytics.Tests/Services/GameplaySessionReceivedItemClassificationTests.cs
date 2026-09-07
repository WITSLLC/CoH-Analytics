using System.IO;
using CoHAnalytics.Models;
using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Services;

public sealed class GameplaySessionReceivedItemClassificationTests
{
  private static IGameplayReceivedItemClassifier CreateTestClassifier()
  {
    var catalog = new InMemoryReceivedItemTaxonomyCatalog();
    catalog.AddSalvage(
        "Luck Charm",
        new ReceivedItemClassificationMetadata { SalvageRarity = "Common" });
    catalog.AddEnhancement(
        "Accuracy SO",
        new ReceivedItemClassificationMetadata
        {
          EnhancementTypeLabel = "Set",
          EnhancementSetName = "Blaster's Aim",
          EnhancementRarity = "Superior"
        });
    return new GameplayReceivedItemClassifier(catalog);
  }

  [Fact]
  public async Task Recipe_received_item_classifies_in_recent_rewards()
  {
    var monitoring = new FakeMonitoringSessionManager();
    var parser = new GameplaySessionTestInfrastructure.FakeGameplayParserManager();
    var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dir);
    var classifier = CreateTestClassifier();

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
          receivedItemClassifier: classifier);

      parser.PublishClassified([
          GameplaySessionTestInfrastructure.Classify(
              "2026-08-04 06:27:10 Welcome to City of Heroes, Example Hero!",
              contextId,
              source,
              sequence: 1),
          GameplaySessionTestInfrastructure.Classify(
              "2026-08-04 06:27:11 You received Armageddon: Damage (Recipe).",
              contextId,
              source,
              sequence: 2)
      ]);

      await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
          manager.Current.Sessions.Any(session =>
              session.RecentRewards.Any(entry =>
                  entry.Category == GameplaySessionRewardCategory.Recipe
                  && entry.DisplayName == "Armageddon: Damage (Recipe)"
                  && entry.RawReceivedItemText == "Armageddon: Damage (Recipe)")
              && session.RewardCategoryCounts.RecipeDropCount == 1));
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
  public async Task Salvage_lookup_classifies_and_preserves_display_text()
  {
    var monitoring = new FakeMonitoringSessionManager();
    var parser = new GameplaySessionTestInfrastructure.FakeGameplayParserManager();
    var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dir);
    var classifier = CreateTestClassifier();

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
          receivedItemClassifier: classifier);

      parser.PublishClassified([
          GameplaySessionTestInfrastructure.Classify(
              "2026-08-04 06:27:10 Welcome to City of Heroes, Example Hero!",
              contextId,
              source,
              sequence: 1),
          GameplaySessionTestInfrastructure.Classify(
              "You received Luck Charm.",
              contextId,
              source,
              sequence: 2)
      ]);

      await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
          manager.Current.Sessions.Any(session =>
              session.RecentRewards.Any(entry =>
                  entry.Category == GameplaySessionRewardCategory.Salvage
                  && entry.DisplayName == "Luck Charm"
                  && entry.RawReceivedItemText == "Luck Charm")
              && session.RewardCategoryCounts.SalvageDropCount == 1
              && session.SalvageTotals.Any(total =>
                  total.DisplayName == "Luck Charm" && total.Quantity == 1)));
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
  public async Task Enhancement_lookup_classifies_in_recent_rewards()
  {
    var monitoring = new FakeMonitoringSessionManager();
    var parser = new GameplaySessionTestInfrastructure.FakeGameplayParserManager();
    var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dir);
    var classifier = CreateTestClassifier();

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
          receivedItemClassifier: classifier);

      parser.PublishClassified([
          GameplaySessionTestInfrastructure.Classify(
              "[04:29] Welcome to City of Heroes, Example Hero!",
              contextId,
              source,
              sequence: 1),
          GameplaySessionTestInfrastructure.Classify(
              "[04:29] You received Accuracy SO.",
              contextId,
              source,
              sequence: 2)
      ]);

      await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
          manager.Current.Sessions.Any(session =>
              session.RecentRewards.Any(entry =>
                  entry.Category == GameplaySessionRewardCategory.Enhancement
                  && entry.DisplayName == "Accuracy SO")
              && session.RewardCategoryCounts.EnhancementDropCount == 1));
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
  public async Task Unknown_received_item_stays_generic_and_visible()
  {
    var monitoring = new FakeMonitoringSessionManager();
    var parser = new GameplaySessionTestInfrastructure.FakeGameplayParserManager();
    var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dir);
    var classifier = CreateTestClassifier();

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
          receivedItemClassifier: classifier);

      parser.PublishClassified([
          GameplaySessionTestInfrastructure.Classify(
              "2026-08-04 06:27:10 Welcome to City of Heroes, Example Hero!",
              contextId,
              source,
              sequence: 1),
          GameplaySessionTestInfrastructure.Classify(
              "2026-08-04 06:27:11 You received Mystery Thing.",
              contextId,
              source,
              sequence: 2)
      ]);

      await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
          manager.Current.Sessions.Any(session =>
              session.RecentRewards.Any(entry =>
                  entry.Category == GameplaySessionRewardCategory.ReceivedItem
                  && entry.DisplayName == "Mystery Thing")));
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
  public async Task Punctuation_in_display_text_is_preserved()
  {
    var monitoring = new FakeMonitoringSessionManager();
    var parser = new GameplaySessionTestInfrastructure.FakeGameplayParserManager();
    var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dir);
    var classifier = CreateTestClassifier();

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
          receivedItemClassifier: classifier);

      parser.PublishClassified([
          GameplaySessionTestInfrastructure.Classify(
              "2026-08-04 06:27:10 Welcome to City of Heroes, Example Hero!",
              contextId,
              source,
              sequence: 1),
          GameplaySessionTestInfrastructure.Classify(
              "2026-08-04 06:27:11 You received Armageddon: Damage (Recipe).",
              contextId,
              source,
              sequence: 2)
      ]);

      await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
          manager.Current.Sessions.Any(session =>
              session.RecentRewards.Any(entry =>
                  entry.DisplayName == "Armageddon: Damage (Recipe)"
                  && entry.RawReceivedItemText == "Armageddon: Damage (Recipe)")));
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
  public async Task Category_counts_accumulate_across_multiple_drops()
  {
    var monitoring = new FakeMonitoringSessionManager();
    var parser = new GameplaySessionTestInfrastructure.FakeGameplayParserManager();
    var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dir);
    var classifier = CreateTestClassifier();

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
          receivedItemClassifier: classifier);

      parser.PublishClassified([
          GameplaySessionTestInfrastructure.Classify(
              "2026-08-04 06:27:10 Welcome to City of Heroes, Example Hero!",
              contextId,
              source,
              sequence: 1),
          GameplaySessionTestInfrastructure.Classify(
              "2026-08-04 06:27:11 You received Luck Charm.",
              contextId,
              source,
              sequence: 2),
          GameplaySessionTestInfrastructure.Classify(
              "2026-08-04 06:27:12 You received Luck Charm.",
              contextId,
              source,
              sequence: 3),
          GameplaySessionTestInfrastructure.Classify(
              "2026-08-04 06:27:13 You received Armageddon: Damage (Recipe).",
              contextId,
              source,
              sequence: 4)
      ]);

      await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
          manager.Current.Sessions.Any(session =>
              session.RewardCategoryCounts.SalvageDropCount == 2
              && session.RewardCategoryCounts.RecipeDropCount == 1));
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
  public async Task Retained_pre_identity_classified_reward_commits_exactly_once()
  {
    var monitoring = new FakeMonitoringSessionManager();
    var parser = new GameplaySessionTestInfrastructure.FakeGameplayParserManager();
    var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dir);
    var classifier = CreateTestClassifier();

    try
    {
      repository.EstablishTrustedFromWelcome("acct-1", "Example Hero");
      repository.EstablishTrustedFromManualConfirmation("acct-1", "Other Hero");

      var contextId = MonitoringContextId.CreateNew();
      var source = GameplaySessionTestInfrastructure.DefaultSource();
      monitoring.SetInitial(ParserTestSnapshots.Snapshot(
          1,
          GameplaySessionTestInfrastructure.ReadyContext(contextId, source)));

      var manager = await GameplaySessionTestInfrastructure.CreateStartedManager(
          monitoring,
          parser,
          repository,
          receivedItemClassifier: classifier);

      parser.PublishClassified([
          GameplaySessionTestInfrastructure.Classify(
              "2026-08-04 06:27:01 You received Luck Charm.",
              contextId,
              source,
              sequence: 1)
      ]);

      await GameplaySessionTestInfrastructure.WaitUntilAsync(
          () => manager.Current.Sessions.Any(session => session.RetainedEventCount == 1));

      var otherRecord = repository.TryFindTrustedByDisplayName("acct-1", "Other Hero")!;
      Assert.True(manager.ConfirmCharacter(contextId, otherRecord.RecordId).IsSuccess);

      await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
          manager.Current.Sessions.Any(session =>
              session.RecentRewards.Count == 1
              && session.RewardCategoryCounts.SalvageDropCount == 1));

      parser.PublishClassified([
          GameplaySessionTestInfrastructure.Classify(
              "2026-08-04 06:27:02 You received Luck Charm.",
              contextId,
              source,
              sequence: 2)
      ]);

      await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
          manager.Current.Sessions.Any(session =>
              session.RecentRewards.Count == 2
              && session.RewardCategoryCounts.SalvageDropCount == 2));
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
  public async Task Repeated_welcome_for_same_character_preserves_category_counts()
  {
    var monitoring = new FakeMonitoringSessionManager();
    var parser = new GameplaySessionTestInfrastructure.FakeGameplayParserManager();
    var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dir);
    var classifier = CreateTestClassifier();

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
          receivedItemClassifier: classifier);

      parser.PublishClassified([
          GameplaySessionTestInfrastructure.Classify(
              "2026-08-04 06:27:10 Welcome to City of Heroes, Example Hero!",
              contextId,
              source,
              sequence: 1),
          GameplaySessionTestInfrastructure.Classify(
              "2026-08-04 06:27:11 You received Luck Charm.",
              contextId,
              source,
              sequence: 2),
          GameplaySessionTestInfrastructure.Classify(
              "2026-08-04 06:27:20 Welcome to City of Heroes, Example Hero!",
              contextId,
              source,
              sequence: 3),
          GameplaySessionTestInfrastructure.Classify(
              "2026-08-04 06:27:21 You received Armageddon: Damage (Recipe).",
              contextId,
              source,
              sequence: 4)
      ]);

      await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
          manager.Current.Sessions.Any(session =>
              session.RewardCategoryCounts.SalvageDropCount == 1
              && session.RewardCategoryCounts.RecipeDropCount == 1));
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
  public async Task Multi_context_sessions_keep_isolated_classification()
  {
    var monitoring = new FakeMonitoringSessionManager();
    var parser = new GameplaySessionTestInfrastructure.FakeGameplayParserManager();
    var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dir);
    var classifier = CreateTestClassifier();

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
          receivedItemClassifier: classifier);

      parser.PublishClassified([
          GameplaySessionTestInfrastructure.Classify(
              "2026-08-04 06:27:10 Welcome to City of Heroes, Hero A!",
              contextA,
              sourceA,
              sequence: 1),
          GameplaySessionTestInfrastructure.Classify(
              "2026-08-04 06:27:11 You received Luck Charm.",
              contextA,
              sourceA,
              sequence: 2),
          GameplaySessionTestInfrastructure.Classify(
              "2026-08-04 06:27:10 Welcome to City of Heroes, Hero B!",
              contextB,
              sourceB,
              sequence: 3),
          GameplaySessionTestInfrastructure.Classify(
              "2026-08-04 06:27:11 You received Accuracy SO.",
              contextB,
              sourceB,
              sequence: 4)
      ]);

      await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
      {
        var sessions = manager.Current.Sessions;
        return sessions.Count == 2
            && sessions.Any(session =>
                session.ContextId == contextA
                && session.RewardCategoryCounts.SalvageDropCount == 1
                && session.RewardCategoryCounts.EnhancementDropCount == 0)
            && sessions.Any(session =>
                session.ContextId == contextB
                && session.RewardCategoryCounts.EnhancementDropCount == 1
                && session.RewardCategoryCounts.SalvageDropCount == 0);
      });
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
