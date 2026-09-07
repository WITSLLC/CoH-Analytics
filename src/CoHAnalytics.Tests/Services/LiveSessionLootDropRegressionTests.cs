using System.IO;
using CoHAnalytics.Models;
using CoHAnalytics.ReferenceData;
using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Services;

public sealed class LiveSessionLootDropRegressionTests
{
    [Fact]
    public void Production_classifier_recognizes_observed_salvage_names()
    {
        var classifier = CreateProductionClassifier();

        Assert.True(classifier.TryClassify("Fortune", out var fortuneCategory, out _));
        Assert.Equal(GameplaySessionRewardCategory.Salvage, fortuneCategory);

        Assert.True(classifier.TryClassify("Regenerating Flesh", out var fleshCategory, out _));
        Assert.Equal(GameplaySessionRewardCategory.Salvage, fleshCategory);

        Assert.True(classifier.TryClassify("Silver", out var silverCategory, out _));
        Assert.Equal(GameplaySessionRewardCategory.Salvage, silverCategory);
    }

    [Fact]
    public void Production_classifier_recognizes_current_bands_of_hermes_parenthetical_receipt()
    {
        var classifier = CreateProductionClassifier();

        Assert.True(classifier.TryClassify(
            "Bands of Hermes (Immobilize)",
            out var category,
            out var metadata));
        Assert.Equal(GameplaySessionRewardCategory.Enhancement, category);
        Assert.Equal("OriginOrTraining", metadata!.EnhancementTypeLabel);
    }

    [Fact]
    public void All_homecoming_identities_resolve_historical_set_piece_colon_receipt()
    {
        var catalog = ItemReferenceCatalogFactory.LoadProductionDatabase();

        Assert.False(catalog.TryResolve("Bands of Hermes: Immobilize", out _));
        Assert.True(catalog.TryResolve(
            "Bands of Hermes: Immobilize",
            out var resolution,
            ReferenceCatalogQueryScope.AllHomecomingIdentities));
        Assert.Equal("ENH-00846", resolution.Item.CatalogItemId);
        Assert.True(ReferenceServerAvailabilitySupport.IsHistoricalHomecoming(resolution.Item.ServerAvailability));
    }

    [Fact]
    public async Task Fortune_receipts_aggregate_to_salvage_totals()
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
                    "2026-08-08 00:10:14 You received Fortune.",
                    contextId,
                    source,
                    sequence: 2),
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-08 00:11:48 You received Fortune.",
                    contextId,
                    source,
                    sequence: 3),
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-08 00:06:52 You received Regenerating Flesh.",
                    contextId,
                    source,
                    sequence: 4)
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
                manager.Current.Sessions.Any(session =>
                    session.SalvageTotals.Any(total =>
                        total.DisplayName == "Fortune" && total.Quantity == 2)
                    && session.SalvageTotals.Any(total =>
                        total.DisplayName == "Regenerating Flesh" && total.Quantity == 1)));
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
    public async Task Bands_of_hermes_parenthetical_receipt_populates_enhancement_totals()
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
                    "2026-08-08 00:13:29 You received Bands of Hermes (Immobilize).",
                    contextId,
                    source,
                    sequence: 2)
            ]);

            await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
                manager.Current.Sessions.Any(session =>
                    session.EnhancementTotals.Any(total =>
                        total.DisplayName == "Bands of Hermes (Immobilize)"
                        && total.Quantity == 1)));
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
        var itemReferenceCatalog = ItemReferenceCatalogFactory.LoadProductionDatabase();
        var taxonomyCatalog = new ChainedReceivedItemTaxonomyCatalog([
            new ItemReferenceReceivedItemTaxonomyCatalog(itemReferenceCatalog)
        ]);
        return new GameplayReceivedItemClassifier(taxonomyCatalog);
    }
}
