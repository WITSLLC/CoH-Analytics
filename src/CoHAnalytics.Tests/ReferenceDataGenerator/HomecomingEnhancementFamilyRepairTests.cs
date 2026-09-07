using System.Text;
using CoHAnalytics.ReferenceData;
using CoHAnalytics.ReferenceDataGenerator;
using Microsoft.Data.Sqlite;

namespace CoHAnalytics.Tests.ReferenceDataGenerator;

[Collection(LiveInstallPromotionCollection.Name)]
public sealed class HomecomingEnhancementFamilyRepairTests
{
    private readonly LiveInstallPromotionFixture _promotion;

    public HomecomingEnhancementFamilyRepairTests(LiveInstallPromotionFixture promotion)
    {
        _promotion = promotion;
    }

    private static string LiveInstallRoot => LiveInstallTestEnvironment.InstallRoot;

    [Theory]
    [InlineData("Boosts.Crafted_Accuracy.Crafted_Accuracy", "Accuracy", ReferenceEnhancementFamily.CraftedInvention)]
    [InlineData("Boosts.Crafted_Damage.Crafted_Damage", "Damage", ReferenceEnhancementFamily.CraftedInvention)]
    [InlineData("Boosts.Crafted_Recharge.Crafted_Recharge", "Recharge", ReferenceEnhancementFamily.CraftedInvention)]
    [InlineData("Boosts.Crafted_Endurance_Discount.Crafted_Endurance_Discount", "EnduranceDiscount", ReferenceEnhancementFamily.CraftedInvention)]
    [InlineData("Boosts.Crafted_Hold.Crafted_Hold", "Hold", ReferenceEnhancementFamily.CraftedInvention)]
    [InlineData("Boosts.Crafted_Res_Damage.Crafted_Res_Damage", "Res_Damage", ReferenceEnhancementFamily.CraftedInvention)]
    public void ClassifySource_CraftedInvention_UsesStructuralEvidence(
        string sourceId,
        string aspectType,
        ReferenceEnhancementFamily expected)
    {
        var discovery = Discovery(
            sourceId,
            [aspectType],
            ["Science", "Mutation", "Magic", "Technology", "Natural", aspectType]);

        Assert.Equal(expected, HomecomingEnhancementFamilySupport.ClassifySource(sourceId, discovery));
    }

    [Theory]
    [InlineData("Boosts.Science_Accuracy.Science_Accuracy", "Accuracy")]
    [InlineData("Boosts.Magic_Damage.Magic_Damage", "Damage")]
    [InlineData("Boosts.Technology_Recharge.Technology_Recharge", "Recharge")]
    public void ClassifySource_OriginOrTraining_DoesNotUseDisplayName(
        string sourceId,
        string aspectType)
    {
        var discovery = Discovery(
            sourceId,
            [aspectType],
            ["Science", aspectType]);

        Assert.Equal(
            ReferenceEnhancementFamily.OriginOrTraining,
            HomecomingEnhancementFamilySupport.ClassifySource(sourceId, discovery));
    }

    [Theory]
    [InlineData("Boosts.Hamidon_Accuracy_Mez.Hamidon_Accuracy_Mez", "Accuracy+Hamidon", ReferenceEnhancementFamily.Hamidon)]
    [InlineData("Boosts.DSync_Accuracy_Mez.DSync_Accuracy_Mez", "Accuracy+Hamidon", ReferenceEnhancementFamily.DSync)]
    [InlineData("Boosts.Hydra_Accuracy_Mez.Hydra_Accuracy_Mez", "Accuracy+Hamidon", ReferenceEnhancementFamily.Hydra)]
    [InlineData("Boosts.Titan_Accuracy_Mez.Titan_Accuracy_Mez", "Accuracy+Hamidon", ReferenceEnhancementFamily.Titan)]
    [InlineData("Boosts.Synthetic_Hamidon_Accuracy_Mez.Synthetic_Hamidon_Accuracy_Mez", "Accuracy+Hamidon", ReferenceEnhancementFamily.Synthetic)]
    [InlineData("Boosts.Yins_Magic_Accuracy.Yins_Magic_Accuracy", "Accuracy", ReferenceEnhancementFamily.Yin)]
    public void ClassifySource_SpecialFamilies_UseSourceIdentity(
        string sourceId,
        string aspectTypesCsv,
        ReferenceEnhancementFamily expected)
    {
        var aspectTypes = aspectTypesCsv.Split('+', StringSplitOptions.RemoveEmptyEntries);
        var discovery = Discovery(
            sourceId,
            aspectTypes,
            ["Science", "Mutation", "Magic", "Technology", "Natural", .. aspectTypes]);

        Assert.Equal(expected, HomecomingEnhancementFamilySupport.ClassifySource(sourceId, discovery));
    }

    [Fact]
    public void ClassifySource_CompositeApplicability_DoesNotImplyFamily()
    {
        var discovery = Discovery(
            "Boosts.Hamidon_Accuracy_Mez.Hamidon_Accuracy_Mez",
            ["Accuracy", "Confuse", "Hamidon"],
            ["Science", "Mutation", "Magic", "Technology", "Natural", "Accuracy", "Confuse", "Hamidon"]);

        Assert.Equal(
            ReferenceEnhancementFamily.Hamidon,
            HomecomingEnhancementFamilySupport.ClassifySource(
                "Boosts.Hamidon_Accuracy_Mez.Hamidon_Accuracy_Mez",
                discovery));
    }

    [Fact]
    public void ClassifySource_UnknownStructure_FailsExplicitly()
    {
        var discovery = Discovery(
            "Boosts.Unknown_Future.Unknown_Future",
            ["Accuracy"],
            ["Science", "Accuracy"]);

        var exception = Assert.Throws<HomecomingEnhancementPromotionException>(() =>
            HomecomingEnhancementFamilySupport.ClassifySource(
                "Boosts.Unknown_Future.Unknown_Future",
                discovery));

        Assert.Contains("could not be structurally classified", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void JsonAndSqlite_Roundtrip_PreservesEnhancementFamily()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"coh-family-repair-{Guid.NewGuid():N}.db");
        try
        {
            var json = """
                {
                  "manifest": {
                    "catalogVersion": "item-ref-test",
                    "homecomingCompatibility": { "buildMin": "1", "buildMax": "1" }
                  },
                  "items": [
                    {
                      "catalogItemId": "ENH-90001",
                      "family": "Enhancement",
                      "subtype": "CraftedInvention",
                      "enhancementFamily": "CraftedInvention",
                      "currentDisplayName": "Invention: Accuracy",
                      "activeStatus": "Active",
                      "serverAvailability": [
                        { "serverKey": "Homecoming", "status": "Current" }
                      ],
                      "verificationStatus": "VerifiedMultiSource",
                      "commonIoBoostType": "Accuracy",
                      "commonIoBoostTypeDisplayText": "Accuracy"
                    },
                    {
                      "catalogItemId": "ENH-90002",
                      "family": "Enhancement",
                      "subtype": "Hamidon",
                      "enhancementFamily": "Hamidon",
                      "currentDisplayName": "Hamidon: Damage",
                      "activeStatus": "Active",
                      "serverAvailability": [
                        { "serverKey": "Homecoming", "status": "Current" }
                      ],
                      "verificationStatus": "VerifiedMultiSource",
                      "commonIoBoostType": "Accuracy+Damage+Hamidon"
                    },
                    {
                      "catalogItemId": "ENH-90003",
                      "family": "Enhancement",
                      "subtype": "SetIO",
                      "currentDisplayName": "Fixture: Set Piece",
                      "activeStatus": "Active",
                      "serverAvailability": [
                        { "serverKey": "Homecoming", "status": "Current" }
                      ],
                      "verificationStatus": "VerifiedMultiSource",
                      "enhancementSetId": "SET-90001"
                    }
                  ],
                  "aliases": [],
                  "enhancementSets": [
                    {
                      "catalogItemId": "SET-90001",
                      "currentDisplayName": "Fixture Set",
                      "activeStatus": "Active",
                      "serverAvailability": [
                        { "serverKey": "Homecoming", "status": "Current" }
                      ],
                      "verificationStatus": "VerifiedMultiSource"
                    }
                  ]
                }
                """;

            ItemReferenceCatalogImporter.ImportFromJsonStream(
                new MemoryStream(Encoding.UTF8.GetBytes(json)),
                databasePath);

            using (var connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadOnly;Pooling=False"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText =
                    """
                    SELECT enhancement_family
                    FROM Item
                    WHERE catalog_item_id = 'ENH-90002';
                    """;
                Assert.Equal("Hamidon", command.ExecuteScalar()?.ToString());
            }

            var loadResult = SqliteReferenceCatalogLoader.Load(databasePath);
            Assert.True(loadResult.Succeeded, loadResult.FailureReason);
            var catalog = (ItemReferenceCatalog)ItemReferenceCatalog.FromLoadResult(loadResult);
            Assert.Equal(ReferenceEnhancementFamily.Hamidon, catalog.DebugItems["ENH-90002"].EnhancementFamily);
            Assert.Null(catalog.DebugItems["ENH-90003"].EnhancementFamily);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }

    [Fact]
    public void Loader_RejectsInvalidEnhancementFamily()
    {
        var json = """
            {
              "manifest": {
                "catalogVersion": "item-ref-test",
                "homecomingCompatibility": { "buildMin": "1", "buildMax": "1" }
              },
              "items": [
                {
                  "catalogItemId": "ENH-90001",
                  "family": "Enhancement",
                  "subtype": "CommonIO",
                  "enhancementFamily": "CommonIO",
                  "currentDisplayName": "Bad",
                  "activeStatus": "Active",
                  "serverAvailability": [
                    { "serverKey": "Homecoming", "status": "Current" }
                  ],
                  "verificationStatus": "VerifiedMultiSource"
                }
              ],
              "aliases": [],
              "enhancementSets": []
            }
            """;

        var result = ItemReferenceCatalogLoader.Load(new MemoryStream(Encoding.UTF8.GetBytes(json)));
        Assert.False(result.Succeeded);
        Assert.Contains("unknown EnhancementFamily", result.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "LiveInstall")]
    public void LiveInstall_Promotion_ReportsExpectedFamilyCensus()
    {
        var catalogPath = Path.Combine(Path.GetTempPath(), $"coh-family-live-{Guid.NewGuid():N}.json");
        try
        {
            using (var embedded = typeof(ItemReferenceCatalogFactory).Assembly.GetManifestResourceStream(
                       ItemReferenceCatalogFactory.ProductionCatalogResourceName)
                   ?? throw new InvalidOperationException("Embedded production catalog missing."))
            using (var output = File.Create(catalogPath))
            {
                embedded.CopyTo(output);
            }

            var result = _promotion.Promote(catalogPath);
            Assert.Equal(397, result.Stats.NonSetEnhancements);
            Assert.Equal(27, result.Stats.EnhancementFamilyCounts[ReferenceEnhancementFamily.CraftedInvention]);
            Assert.Equal(291, result.Stats.EnhancementFamilyCounts[ReferenceEnhancementFamily.OriginOrTraining]);
            Assert.Equal(20, result.Stats.EnhancementFamilyCounts[ReferenceEnhancementFamily.Hamidon]);
            Assert.Equal(20, result.Stats.EnhancementFamilyCounts[ReferenceEnhancementFamily.DSync]);
            Assert.Equal(11, result.Stats.EnhancementFamilyCounts[ReferenceEnhancementFamily.Hydra]);
            Assert.Equal(11, result.Stats.EnhancementFamilyCounts[ReferenceEnhancementFamily.Titan]);
            Assert.Equal(11, result.Stats.EnhancementFamilyCounts[ReferenceEnhancementFamily.Synthetic]);
            Assert.Equal(6, result.Stats.EnhancementFamilyCounts[ReferenceEnhancementFamily.Yin]);
        }
        finally
        {
            if (File.Exists(catalogPath))
            {
                File.Delete(catalogPath);
            }
        }
    }

    private static HomecomingBoostDiscoveryRecord Discovery(
        string sourceId,
        IReadOnlyList<string> nonOriginBoostTypes,
        IReadOnlyList<string> boostsAllowed) =>
        new(
            sourceId,
            "P_DISPLAY",
            "P_HELP",
            string.Empty,
            "icon.tga",
            boostsAllowed,
            nonOriginBoostTypes,
            0f,
            0f,
            0f,
            0f);
}
