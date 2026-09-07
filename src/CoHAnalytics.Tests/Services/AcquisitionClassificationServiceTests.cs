using System.Text;
using System.Text.Json;
using CoHAnalytics.Models;
using CoHAnalytics.Observations;
using CoHAnalytics.ReferenceData;
using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Services;

public sealed class AcquisitionClassificationServiceTests
{
    [Fact]
    public async Task Map_to_existing_adds_alias_reloads_catalog_and_resolves_observation()
    {
        using var fixture = await ClassificationFixture.CreateAsync(
            "A Previously Unknown Receipt",
            ReferenceItemFamily.Enhancement);

        var result = fixture.Service.MapToExistingReference(
            fixture.ObservationKey,
            "ENH-00001");

        Assert.True(result.IsSuccess, result.Detail);
        Assert.True(fixture.Catalog.TryResolve("A Previously Unknown Receipt", out var resolution));
        Assert.Equal("ENH-00001", resolution.Item.CatalogItemId);
        Assert.Empty(fixture.Observations.GetNeedsClassification());
        Assert.Contains(
            ReadAliases(fixture.SourcePath),
            alias => alias.Text == "A Previously Unknown Receipt"
                && alias.CatalogItemId == "ENH-00001");

        var repeated = fixture.Service.MapToExistingReference(
            fixture.ObservationKey,
            "ENH-00001");
        Assert.Equal(AcquisitionClassificationOutcome.ObservationNotFound, repeated.Outcome);
    }

    [Fact]
    public async Task Developer_mode_gate_prevents_catalog_mutation()
    {
        using var fixture = await ClassificationFixture.CreateAsync(
            "A Previously Unknown Receipt",
            ReferenceItemFamily.Enhancement,
            developerMode: false);
        var originalSource = File.ReadAllText(fixture.SourcePath);

        var result = fixture.Service.MapToExistingReference(
            fixture.ObservationKey,
            "ENH-00001");

        Assert.Equal(AcquisitionClassificationOutcome.DeveloperModeRequired, result.Outcome);
        Assert.Equal(originalSource, File.ReadAllText(fixture.SourcePath));
        Assert.Single(fixture.Observations.GetNeedsClassification());
    }

    [Fact]
    public async Task Recipe_creation_writes_first_class_recipe_identity()
    {
        using var fixture = await ClassificationFixture.CreateAsync(
            "Fixture Enhancement (Recipe)",
            ReferenceItemFamily.Recipe);

        var result = fixture.Service.CreateRecipeReference(
            fixture.ObservationKey,
            new NewRecipeReference
            {
                CurrentDisplayName = "Fixture Enhancement Recipe",
                Subtype = "SetIO",
                ProducedItemId = "ENH-00001",
                EnhancementSetId = "SET-00001",
                Rarity = "Rare"
            });

        Assert.True(result.IsSuccess, result.Detail);
        Assert.StartsWith("REC-", result.CatalogItemId, StringComparison.Ordinal);
        Assert.True(fixture.Catalog.TryResolve("Fixture Enhancement (Recipe)", out var resolution));
        Assert.Equal(ReferenceItemFamily.Recipe, resolution.Item.Family);
        Assert.Equal("ENH-00001", resolution.Item.ProducedItemId);
        Assert.Equal("SET-00001", resolution.Item.EnhancementSetId);
        Assert.Empty(fixture.Observations.GetNeedsClassification());
    }

    [Fact]
    public async Task Enhancement_creation_writes_minimum_identity_and_resolves_observation()
    {
        using var fixture = await ClassificationFixture.CreateAsync(
            "New Set Piece (Damage)",
            ReferenceItemFamily.Enhancement);

        var result = fixture.Service.CreateEnhancementReference(
            fixture.ObservationKey,
            new NewEnhancementReference
            {
                CurrentDisplayName = "New Set Piece: Damage",
                Subtype = "SetIO",
                EnhancementSetId = "SET-00001",
                Variant = "Regular"
            });

        Assert.True(result.IsSuccess, result.Detail);
        Assert.StartsWith("ENH-", result.CatalogItemId, StringComparison.Ordinal);
        Assert.True(fixture.Catalog.TryResolve("New Set Piece (Damage)", out var resolution));
        Assert.Equal(ReferenceItemFamily.Enhancement, resolution.Item.Family);
        Assert.Equal("SET-00001", resolution.Item.EnhancementSetId);
        Assert.Empty(fixture.Observations.GetNeedsClassification());
    }

    private static IReadOnlyList<(string CatalogItemId, string Text)> ReadAliases(string sourcePath)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(sourcePath));
        return document.RootElement.GetProperty("aliases")
            .EnumerateArray()
            .Select(alias => (
                alias.GetProperty("catalogItemId").GetString()!,
                alias.GetProperty("text").GetString()!))
            .ToArray();
    }

    private sealed class ClassificationFixture : IDisposable
    {
        private ClassificationFixture(
            string root,
            string sourcePath,
            string observationKey,
            SqliteReferenceStore catalog,
            AcquisitionObservationService observations,
            AcquisitionClassificationService service)
        {
            Root = root;
            SourcePath = sourcePath;
            ObservationKey = observationKey;
            Catalog = catalog;
            Observations = observations;
            Service = service;
        }

        public string Root { get; }

        public string SourcePath { get; }

        public string ObservationKey { get; }

        public SqliteReferenceStore Catalog { get; }

        public AcquisitionObservationService Observations { get; }

        public AcquisitionClassificationService Service { get; }

        public static async Task<ClassificationFixture> CreateAsync(
            string observedText,
            ReferenceItemFamily family,
            bool developerMode = true)
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                "coh-classification-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var sourcePath = Path.Combine(root, "item-catalog.v1.json");
            var databasePath = Path.Combine(root, "ReferenceData", "reference.db");
            File.WriteAllText(sourcePath, CatalogJson, new UTF8Encoding(false));
            using (var stream = File.OpenRead(sourcePath))
            {
                ItemReferenceCatalogImporter.ImportFromJsonStream(stream, databasePath);
            }

            var catalog = new SqliteReferenceStore(databasePath);
            var observations = new AcquisitionObservationService(catalog, root);
            var observationKey = ItemReferenceLookup.NormalizeLookupKey(observedText);
            observations.TryRecordUnresolvedAcquisition(new AcquisitionObservationRecord
            {
                ProducerId = AcquisitionObservationConstants.ProducerId,
                ObservationKind = AcquisitionObservationConstants.ObservationKind,
                ObservedText = observedText,
                NormalizedLookupKey = observationKey,
                CapturedAtUtc = DateTimeOffset.UtcNow,
                FailedCatalogVersion = catalog.Manifest!.CatalogVersion,
                GrammarSource = AcquisitionGrammarSource.ReceivedSimple,
                FamilyHint = family,
                ResolutionStateAtCapture = AcquisitionIdentityResolutionState.PresentedUnresolved
            });

            for (var attempt = 0; attempt < 100; attempt++)
            {
                if (observations.GetObservationDetail(observationKey) is not null)
                {
                    break;
                }

                await Task.Delay(20);
            }

            Assert.NotNull(observations.GetObservationDetail(observationKey));
            var service = new AcquisitionClassificationService(
                new StubInternalFeatureGate(developerMode),
                observations,
                catalog,
                new AcquisitionClassificationOptions
                {
                    AuthoritativeCatalogPath = sourcePath,
                    RuntimeDatabasePath = databasePath
                });
            return new ClassificationFixture(
                root,
                sourcePath,
                observationKey,
                catalog,
                observations,
                service);
        }

        public void Dispose()
        {
            Observations.Dispose();
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch
            {
            }
        }

        private const string CatalogJson =
            """
            {
              "manifest": {
                "catalogVersion": "item-ref-1.0.0",
                "homecomingCompatibility": { "buildMin": "1", "buildMax": "1" },
                "sourceRevision": "test"
              },
              "items": [
                {
                  "catalogItemId": "ENH-00001",
                  "family": "Enhancement",
                  "subtype": "SetIO",
                  "currentDisplayName": "Fixture Enhancement",
                  "activeStatus": "Active",
                  "serverAvailability": [
                    { "serverKey": "Homecoming", "status": "Current" }
                  ],
                  "verificationStatus": "VerifiedDirect",
                  "enhancementSetId": "SET-00001",
                  "variant": "Regular"
                }
              ],
              "aliases": [
                {
                  "catalogItemId": "ENH-00001",
                  "locale": "en",
                  "text": "Fixture Enhancement",
                  "nameKind": "Display",
                  "isPreferred": true
                }
              ],
              "enhancementSets": [
                {
                  "catalogItemId": "SET-00001",
                  "currentDisplayName": "Fixture Set",
                  "activeStatus": "Active",
                  "serverAvailability": [
                    { "serverKey": "Homecoming", "status": "Current" }
                  ],
                  "verificationStatus": "VerifiedDirect"
                }
              ]
            }
            """;
    }
}
