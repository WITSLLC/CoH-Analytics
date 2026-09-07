using System.IO;
using CoHAnalytics.Models;
using CoHAnalytics.ReferenceData;
using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Services;

public sealed class LiveSessionInspirationDropRegressionTests
{
    [Fact]
    public void Production_classifier_recognizes_observed_inspiration_names()
    {
        var classifier = CreateProductionClassifier();

        Assert.True(classifier.TryClassify("Luck", out var luckCategory, out var luckMetadata));
        Assert.Equal(GameplaySessionRewardCategory.Inspiration, luckCategory);
        Assert.Equal("Single", luckMetadata!.InspirationForm);

        Assert.True(classifier.TryClassify("Respite", out var respiteCategory, out _));
        Assert.Equal(GameplaySessionRewardCategory.Inspiration, respiteCategory);

        Assert.True(classifier.TryClassify("Insight", out var insightCategory, out _));
        Assert.Equal(GameplaySessionRewardCategory.Inspiration, insightCategory);

        Assert.True(classifier.TryClassify("Luck Imbuement", out var teamCategory, out var teamMetadata));
        Assert.Equal(GameplaySessionRewardCategory.Inspiration, teamCategory);
        Assert.Equal("Team", teamMetadata!.InspirationForm);

        Assert.True(classifier.TryClassify("Tactical", out var dualCategory, out var dualMetadata));
        Assert.Equal(GameplaySessionRewardCategory.Inspiration, dualCategory);
        Assert.Equal("Dual", dualMetadata!.InspirationForm);
    }

    [Fact]
    public void Unknown_item_remains_unclassified()
    {
        var classifier = CreateProductionClassifier();
        Assert.False(classifier.TryClassify("Mystery Inspiration Thing", out _, out _));
    }

    [Fact]
    public async Task Inspiration_receipts_aggregate_per_item()
    {
        var monitoring = new FakeMonitoringSessionManager();
        var parser = new GameplaySessionTestInfrastructure.FakeGameplayParserManager();
        var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dir);
        var classifier = CreateProductionClassifier();

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
                    "2026-08-08 00:10:10 Welcome to City of Heroes, Example Hero!",
                    contextId,
                    source,
                    sequence: 1),
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-08 00:10:14 You received Luck.",
                    contextId,
                    source,
                    sequence: 2),
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-08 00:11:48 You received Luck.",
                    contextId,
                    source,
                    sequence: 3),
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-08 00:12:00 You received Respite.",
                    contextId,
                    source,
                    sequence: 4)
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
                manager.Current.Sessions.Any(session =>
                    session.InspirationTotals.Any(total =>
                        total.DisplayName == "Luck" && total.Quantity == 2)
                    && session.InspirationTotals.Any(total =>
                        total.DisplayName == "Respite" && total.Quantity == 1)
                    && session.RewardCategoryCounts.InspirationDropCount == 3));
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
    public async Task Retained_pre_identity_inspiration_commits_exactly_once()
    {
        var monitoring = new FakeMonitoringSessionManager();
        var parser = new GameplaySessionTestInfrastructure.FakeGameplayParserManager();
        var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dir);
        var classifier = CreateProductionClassifier();

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
                    "2026-08-08 00:10:01 You received Luck.",
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
                    session.InspirationTotals.Any(total =>
                        total.DisplayName == "Luck" && total.Quantity == 1)
                    && session.RewardCategoryCounts.InspirationDropCount == 1));

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-08 00:10:02 You received Luck.",
                    contextId,
                    source,
                    sequence: 2)
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
                manager.Current.Sessions.Any(session =>
                    session.InspirationTotals.Any(total =>
                        total.DisplayName == "Luck" && total.Quantity == 2)
                    && session.RewardCategoryCounts.InspirationDropCount == 2));
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
    public async Task Repeated_welcome_for_same_character_preserves_inspiration_totals()
    {
        var monitoring = new FakeMonitoringSessionManager();
        var parser = new GameplaySessionTestInfrastructure.FakeGameplayParserManager();
        var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dir);
        var classifier = CreateProductionClassifier();

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
                    "2026-08-08 00:10:10 Welcome to City of Heroes, Example Hero!",
                    contextId,
                    source,
                    sequence: 1),
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-08 00:10:11 You received Luck.",
                    contextId,
                    source,
                    sequence: 2),
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-08 00:10:20 Welcome to City of Heroes, Example Hero!",
                    contextId,
                    source,
                    sequence: 3),
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-08 00:10:21 You received Respite.",
                    contextId,
                    source,
                    sequence: 4)
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
                manager.Current.Sessions.Any(session =>
                    session.RewardCategoryCounts.InspirationDropCount == 2
                    && session.InspirationTotals.Any(total =>
                        total.DisplayName == "Luck" && total.Quantity == 1)
                    && session.InspirationTotals.Any(total =>
                        total.DisplayName == "Respite" && total.Quantity == 1)));
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
    public async Task Multi_context_sessions_keep_isolated_inspiration_totals()
    {
        var monitoring = new FakeMonitoringSessionManager();
        var parser = new GameplaySessionTestInfrastructure.FakeGameplayParserManager();
        var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dir);
        var classifier = CreateProductionClassifier();

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
                    "2026-08-08 00:10:10 Welcome to City of Heroes, Hero A!",
                    contextA,
                    sourceA,
                    sequence: 1),
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-08 00:10:11 You received Luck.",
                    contextA,
                    sourceA,
                    sequence: 2),
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-08 00:10:10 Welcome to City of Heroes, Hero B!",
                    contextB,
                    sourceB,
                    sequence: 3),
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-08 00:10:11 You received Respite.",
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
                        && session.InspirationTotals.Any(total =>
                            total.DisplayName == "Luck" && total.Quantity == 1)
                        && session.RewardCategoryCounts.InspirationDropCount == 1)
                    && sessions.Any(session =>
                        session.ContextId == contextB
                        && session.InspirationTotals.Any(total =>
                            total.DisplayName == "Respite" && total.Quantity == 1)
                        && session.RewardCategoryCounts.InspirationDropCount == 1);
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

    private static IGameplayReceivedItemClassifier CreateProductionClassifier()
    {
        var itemReferenceCatalog = ItemReferenceCatalogFactory.LoadEmbeddedProduction();
        var taxonomyCatalog = new ChainedReceivedItemTaxonomyCatalog([
            new ItemReferenceReceivedItemTaxonomyCatalog(itemReferenceCatalog)
        ]);
        return new GameplayReceivedItemClassifier(taxonomyCatalog);
    }
}
