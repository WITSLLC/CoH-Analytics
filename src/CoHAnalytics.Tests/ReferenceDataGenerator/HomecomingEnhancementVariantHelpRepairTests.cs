using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CoHAnalytics.ReferenceData;
using CoHAnalytics.ReferenceDataGenerator;
using Microsoft.Data.Sqlite;

namespace CoHAnalytics.Tests.ReferenceDataGenerator;

[Collection(LiveInstallPromotionCollection.Name)]
public sealed class HomecomingEnhancementVariantHelpRepairTests
{
    private readonly LiveInstallPromotionFixture _promotion;

    public HomecomingEnhancementVariantHelpRepairTests(LiveInstallPromotionFixture promotion)
    {
        _promotion = promotion;
    }

    private static string LiveInstallRoot => LiveInstallTestEnvironment.InstallRoot;

    [Fact]
    public void ResolveLogicalHelpFromVariantTexts_agreement_returns_shared_text()
    {
        var texts = new string?[] { "Help X", "Help X", "Help X" };
        Assert.Equal("Help X", HomecomingEnhancementVariantHelpSupport.ResolveLogicalHelpFromVariantTexts(texts));
    }

    [Fact]
    public void ResolveLogicalHelpFromVariantTexts_disagreement_returns_null()
    {
        var texts = new string?[] { "Help X", "Help Y" };
        Assert.Null(HomecomingEnhancementVariantHelpSupport.ResolveLogicalHelpFromVariantTexts(texts));
    }

    [Fact]
    public void ResolveLogicalHelpFromVariantTexts_all_null_agrees_as_null()
    {
        var texts = new string?[] { null, null };
        Assert.Null(HomecomingEnhancementVariantHelpSupport.ResolveLogicalHelpFromVariantTexts(texts));
    }

    [Fact]
    public void ResolveLogicalHelpFromVariantTexts_null_and_text_disagrees()
    {
        var texts = new string?[] { null, "Help X" };
        Assert.Null(HomecomingEnhancementVariantHelpSupport.ResolveLogicalHelpFromVariantTexts(texts));
    }

    [Fact]
    public void ResolveVariantDisplayHelp_unresolved_required_key_fails_promotion()
    {
        var messages = HomecomingMessageStoreReader.Read(
            HomecomingBinaryFixtureBuilder.CreateMessageStore([("P_OTHER", "Other")]));
        var discovery = new HomecomingBoostDiscoveryRecord(
            "Boosts.Test.Test",
            "P_DISPLAY",
            "P_MISSING",
            string.Empty,
            "icon.tga",
            [],
            [],
            0f,
            0f,
            0f,
            0f);

        var exception = Assert.Throws<HomecomingEnhancementPromotionException>(() =>
            HomecomingEnhancementVariantHelpSupport.ResolveVariantDisplayHelp(messages, discovery, "ENH-90001"));
        Assert.Contains("P_MISSING", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Loader_rejects_logical_help_that_disagrees_with_source_variants()
    {
        var load = LoadCatalog("""
            {
              "manifest": {
                "catalogVersion": "item-ref-test",
                "homecomingCompatibility": { "buildMin": "1", "buildMax": "1" }
              },
              "items": [
                {
                  "catalogItemId": "ENH-90001",
                  "family": "Enhancement",
                  "subtype": "SetIO",
                  "currentDisplayName": "Test: Damage",
                  "activeStatus": "Active",
                  "serverAvailability": [{ "serverKey": "Homecoming", "status": "Current" }],
                  "verificationStatus": "VerifiedMultiSource",
                  "enhancementSetId": "SET-90001",
                  "displayHelp": "Shared help",
                  "sourceVariants": [
                    {
                      "homecomingSourceId": "Boosts.Test_A.Test_A",
                      "sourceForm": "Crafted",
                      "displayHelp": "Crafted help"
                    },
                    {
                      "homecomingSourceId": "Boosts.Test_B.Test_B",
                      "sourceForm": "Attuned",
                      "displayHelp": "Attuned help"
                    }
                  ]
                }
              ],
              "aliases": [],
              "enhancementSets": [
                {
                  "catalogItemId": "SET-90001",
                  "currentDisplayName": "Test Set",
                  "activeStatus": "Active",
                  "serverAvailability": [{ "serverKey": "Homecoming", "status": "Current" }],
                  "verificationStatus": "VerifiedDirect",
                  "homecomingSetId": "TestSet",
                  "bonuses": []
                }
              ]
            }
            """);
        Assert.False(load.Succeeded);
        Assert.Contains("logical DisplayHelp disagrees", load.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Loader_accepts_variant_help_disagreement_with_null_logical_help()
    {
        var load = LoadCatalog("""
            {
              "manifest": {
                "catalogVersion": "item-ref-test",
                "homecomingCompatibility": { "buildMin": "1", "buildMax": "1" }
              },
              "items": [
                {
                  "catalogItemId": "ENH-90001",
                  "family": "Enhancement",
                  "subtype": "SetIO",
                  "currentDisplayName": "Test: Damage",
                  "activeStatus": "Active",
                  "serverAvailability": [{ "serverKey": "Homecoming", "status": "Current" }],
                  "verificationStatus": "VerifiedMultiSource",
                  "enhancementSetId": "SET-90001",
                  "sourceVariants": [
                    {
                      "homecomingSourceId": "Boosts.Test_A.Test_A",
                      "sourceForm": "Crafted",
                      "displayHelp": "Crafted help",
                      "shortHelp": "Crafted short"
                    },
                    {
                      "homecomingSourceId": "Boosts.Test_B.Test_B",
                      "sourceForm": "Attuned",
                      "displayHelp": "Attuned help",
                      "shortHelp": "Attuned short"
                    }
                  ]
                }
              ],
              "aliases": [],
              "enhancementSets": [
                {
                  "catalogItemId": "SET-90001",
                  "currentDisplayName": "Test Set",
                  "activeStatus": "Active",
                  "serverAvailability": [{ "serverKey": "Homecoming", "status": "Current" }],
                  "verificationStatus": "VerifiedDirect",
                  "homecomingSetId": "TestSet",
                  "bonuses": []
                }
              ]
            }
            """);
        Assert.True(load.Succeeded);
        var item = load.Items!["ENH-90001"];
        Assert.Null(item.DisplayHelp);
        Assert.Null(item.ShortHelp);
        Assert.Equal(
            "Crafted help",
            item.SourceVariants.First(variant => variant.SourceForm == "Crafted").DisplayHelp);
        Assert.Equal(
            "Attuned help",
            item.SourceVariants.First(variant => variant.SourceForm == "Attuned").DisplayHelp);
    }

    [Fact]
    public void Loader_rejects_duplicate_source_variant_ids()
    {
        var load = LoadCatalog("""
            {
              "manifest": {
                "catalogVersion": "item-ref-test",
                "homecomingCompatibility": { "buildMin": "1", "buildMax": "1" }
              },
              "items": [
                {
                  "catalogItemId": "ENH-90001",
                  "family": "Enhancement",
                  "subtype": "SetIO",
                  "currentDisplayName": "Test",
                  "activeStatus": "Active",
                  "serverAvailability": [{ "serverKey": "Homecoming", "status": "Current" }],
                  "verificationStatus": "VerifiedMultiSource",
                  "enhancementSetId": "SET-90001",
                  "sourceVariants": [
                    {
                      "homecomingSourceId": "Boosts.Test_A.Test_A",
                      "sourceForm": "Crafted",
                      "displayHelp": "Help"
                    },
                    {
                      "homecomingSourceId": "Boosts.Test_A.Test_A",
                      "sourceForm": "Attuned",
                      "displayHelp": "Help"
                    }
                  ]
                }
              ],
              "aliases": [],
              "enhancementSets": [
                {
                  "catalogItemId": "SET-90001",
                  "currentDisplayName": "Test Set",
                  "activeStatus": "Active",
                  "serverAvailability": [{ "serverKey": "Homecoming", "status": "Current" }],
                  "verificationStatus": "VerifiedDirect",
                  "homecomingSetId": "TestSet",
                  "bonuses": []
                }
              ]
            }
            """);
        Assert.False(load.Succeeded);
        Assert.Contains("duplicate HomecomingSourceId", load.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sqlite_roundtrip_preserves_variant_help_text()
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
                  "subtype": "SetIO",
                  "currentDisplayName": "Test: Damage",
                  "activeStatus": "Active",
                  "serverAvailability": [{ "serverKey": "Homecoming", "status": "Current" }],
                  "verificationStatus": "VerifiedMultiSource",
                  "enhancementSetId": "SET-90001",
                  "displayHelp": "Shared help",
                  "shortHelp": "Shared short",
                  "sourceVariants": [
                    {
                      "homecomingSourceId": "Boosts.Test_A.Test_A",
                      "sourceForm": "Crafted",
                      "displayHelp": "Shared help",
                      "shortHelp": "Shared short"
                    },
                    {
                      "homecomingSourceId": "Boosts.Test_B.Test_B",
                      "sourceForm": "Attuned",
                      "displayHelp": "Shared help",
                      "shortHelp": "Shared short"
                    }
                  ]
                }
              ],
              "aliases": [],
              "enhancementSets": [
                {
                  "catalogItemId": "SET-90001",
                  "currentDisplayName": "Test Set",
                  "activeStatus": "Active",
                  "serverAvailability": [{ "serverKey": "Homecoming", "status": "Current" }],
                  "verificationStatus": "VerifiedDirect",
                  "homecomingSetId": "TestSet",
                  "bonuses": []
                }
              ]
            }
            """;

        var databasePath = Path.Combine(Path.GetTempPath(), $"coh-variant-help-{Guid.NewGuid():N}.db");
        try
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
            ItemReferenceCatalogImporter.ImportFromJsonStream(stream, databasePath);

            using var connection = new SqliteConnection($"Data Source={databasePath}");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT homecoming_source_id, display_help, short_help
                FROM EnhancementSourceVariant
                ORDER BY variant_index;
                """;
            using var reader = command.ExecuteReader();
            Assert.True(reader.Read());
            Assert.Equal("Boosts.Test_A.Test_A", reader.GetString(0));
            Assert.Equal("Shared help", reader.GetString(1));
            Assert.Equal("Shared short", reader.GetString(2));
            Assert.True(reader.Read());
            Assert.Equal("Boosts.Test_B.Test_B", reader.GetString(0));
            Assert.Equal("Shared help", reader.GetString(1));
            Assert.Equal("Shared short", reader.GetString(2));

            var sqliteCatalog = SqliteReferenceCatalogLoader.Load(databasePath);
            Assert.True(sqliteCatalog.Succeeded);
            var item = sqliteCatalog.Items!["ENH-90001"];
            Assert.Equal("Shared help", item.DisplayHelp);
            Assert.Equal("Shared short", item.ShortHelp);
            Assert.All(item.SourceVariants, variant =>
            {
                Assert.Equal("Shared help", variant.DisplayHelp);
                Assert.Equal("Shared short", variant.ShortHelp);
            });
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
    [Trait("Category", "LiveInstall")]
    public void LiveInstall_PriorCatalogVariantHelpIndependence_restores_from_live_source()
    {
        var catalogPath = Path.Combine(Path.GetTempPath(), $"coh-variant-help-live-{Guid.NewGuid():N}.json");
        try
        {
            using (var embedded = typeof(ItemReferenceCatalogFactory).Assembly.GetManifestResourceStream(
                       ItemReferenceCatalogFactory.ProductionCatalogResourceName)
                   ?? throw new InvalidOperationException("Embedded production catalog missing."))
            using (var output = File.Create(catalogPath))
            {
                embedded.CopyTo(output);
            }

            var baseline = JsonDocument.Parse(File.ReadAllBytes(catalogPath));
            using var clearedStream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(clearedStream, new JsonWriterOptions { Indented = true }))
            {
                writer.WriteStartObject();
                writer.WritePropertyName("manifest");
                baseline.RootElement.GetProperty("manifest").WriteTo(writer);
                writer.WritePropertyName("items");
                writer.WriteStartArray();
                foreach (var item in baseline.RootElement.GetProperty("items").EnumerateArray())
                {
                    writer.WriteStartObject();
                    foreach (var property in item.EnumerateObject())
                    {
                        if (property.NameEquals("sourceVariants"))
                        {
                            writer.WritePropertyName("sourceVariants");
                            writer.WriteStartArray();
                            foreach (var variant in property.Value.EnumerateArray())
                            {
                                writer.WriteStartObject();
                                foreach (var variantProperty in variant.EnumerateObject())
                                {
                                    if (variantProperty.NameEquals("displayHelp")
                                        || variantProperty.NameEquals("shortHelp"))
                                    {
                                        writer.WriteNull(variantProperty.Name);
                                    }
                                    else
                                    {
                                        variantProperty.WriteTo(writer);
                                    }
                                }

                                writer.WriteEndObject();
                            }

                            writer.WriteEndArray();
                            continue;
                        }

                        property.WriteTo(writer);
                    }

                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
                writer.WritePropertyName("aliases");
                baseline.RootElement.GetProperty("aliases").WriteTo(writer);
                writer.WritePropertyName("enhancementSets");
                baseline.RootElement.GetProperty("enhancementSets").WriteTo(writer);
                writer.WriteEndObject();
            }

            File.WriteAllBytes(catalogPath, clearedStream.ToArray());
            var result = _promotion.Promote(catalogPath);
            Assert.Equal(2739, result.Stats.VariantsWithDisplayHelp);
            Assert.Equal(2739, result.Stats.VariantsWithShortHelp);
            Assert.Equal(56, result.Stats.DisplayHelpDisagreementGroups);

            using var promoted = JsonDocument.Parse(File.ReadAllBytes(catalogPath));
            var hecatomb = promoted.RootElement.GetProperty("items").EnumerateArray()
                .First(item => item.GetProperty("catalogItemId").GetString() == "ENH-00792");
            Assert.False(hecatomb.TryGetProperty("displayHelp", out _));
            var variants = hecatomb.GetProperty("sourceVariants").EnumerateArray().ToArray();
            Assert.Equal(2, variants.Length);
            Assert.Equal(2, variants.Select(v => v.GetProperty("displayHelp").GetString()).Distinct(StringComparer.Ordinal).Count());
        }
        finally
        {
            if (File.Exists(catalogPath))
            {
                File.Delete(catalogPath);
            }
        }
    }

    [Fact]
    public void Production_catalog_variant_help_census_matches_live_audit()
    {
        var catalog = (ItemReferenceCatalog)ItemReferenceCatalogFactory.LoadEmbeddedProduction();
        var enhancements = catalog.DebugItems.Values
            .Where(item => item.Family == ReferenceItemFamily.Enhancement)
            .ToArray();

        Assert.Equal(1797, enhancements.Length);
        var variants = enhancements.SelectMany(item => item.SourceVariants).ToArray();
        Assert.Equal(2739, variants.Length);
        Assert.Equal(2739, variants.Count(variant => !string.IsNullOrWhiteSpace(variant.DisplayHelp)));
        Assert.Equal(2739, variants.Count(variant => !string.IsNullOrWhiteSpace(variant.ShortHelp)));

        var displayDisagreement = enhancements.Count(item =>
            item.SourceVariants.Select(variant => variant.DisplayHelp).Distinct(StringComparer.Ordinal).Count() > 1);
        var shortDisagreement = enhancements.Count(item =>
            item.SourceVariants.Select(variant => variant.ShortHelp).Distinct(StringComparer.Ordinal).Count() > 1);
        Assert.Equal(56, displayDisagreement);
        Assert.Equal(1, shortDisagreement);
    }

    [Fact]
    public void Production_catalog_representative_examples_cover_disagreement_patterns()
    {
        var catalog = ItemReferenceCatalogFactory.LoadEmbeddedProduction();

        Assert.True(catalog.TryGetById("ENH-00792", out var hecatombDisagreement));
        Assert.Equal("SET-00011", hecatombDisagreement.EnhancementSetId);
        Assert.Null(hecatombDisagreement.DisplayHelp);
        Assert.Contains(
            hecatombDisagreement.SourceVariants,
            variant => variant.SourceForm == "Crafted");
        Assert.Contains(
            hecatombDisagreement.SourceVariants,
            variant => variant.SourceForm == "Superior_Attuned");
        Assert.Equal(
            2,
            hecatombDisagreement.SourceVariants.Select(variant => variant.DisplayHelp).Distinct(StringComparer.Ordinal).Count());

        Assert.True(catalog.TryGetById("ENH-00094", out var eradication));
        Assert.Equal("SET-00016", eradication.EnhancementSetId);
        Assert.Null(eradication.DisplayHelp);
        Assert.Contains(eradication.SourceVariants, variant => variant.SourceForm == "Crafted");
        Assert.Contains(eradication.SourceVariants, variant => variant.SourceForm == "Attuned");

        Assert.True(catalog.TryGetById("ENH-01344", out var trainingRange));
        Assert.Null(trainingRange.EnhancementSetId);
        Assert.Null(trainingRange.DisplayHelp);
        Assert.Equal(2, trainingRange.SourceVariants.Count);

        Assert.True(catalog.TryGetById("ENH-00001", out var accuracy));
        Assert.NotNull(accuracy.DisplayHelp);
        Assert.All(accuracy.SourceVariants, variant => Assert.Equal(accuracy.DisplayHelp, variant.DisplayHelp));
    }

    [Fact]
    [Trait("Category", "LiveInstall")]
    public void LiveInstall_Promotion_IsDeterministic()
    {
        var firstPath = Path.Combine(Path.GetTempPath(), $"coh-slice5-det-1-{Guid.NewGuid():N}.json");
        var secondPath = Path.Combine(Path.GetTempPath(), $"coh-slice5-det-2-{Guid.NewGuid():N}.json");
        try
        {
            foreach (var path in new[] { firstPath, secondPath })
            {
                using var embedded = typeof(ItemReferenceCatalogFactory).Assembly.GetManifestResourceStream(
                                           ItemReferenceCatalogFactory.ProductionCatalogResourceName)
                                       ?? throw new InvalidOperationException("Embedded production catalog missing.");
                using var output = File.Create(path);
                embedded.CopyTo(output);
            }

            var first = _promotion.Promote(firstPath);
            var second = _promotion.Promote(secondPath);
            Assert.Equal(first.CatalogSha256, second.CatalogSha256);
            Assert.Equal(File.ReadAllBytes(firstPath), File.ReadAllBytes(secondPath));
        }
        finally
        {
            foreach (var path in new[] { firstPath, secondPath })
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }

    private static ItemReferenceCatalogLoadResult LoadCatalog(string json)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return ItemReferenceCatalogLoader.Load(stream);
    }
}
