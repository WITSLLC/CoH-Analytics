using System.Text;
using System.Text.Json;
using CoHAnalytics.ReferenceData;
using CoHAnalytics.ReferenceDataGenerator;

namespace CoHAnalytics.Tests.ReferenceDataGenerator;

public sealed class CatalogPromotionWriteGuardTests
{
    [Fact]
    public void Commit_AllowsOwnedBadgeMutationOnCurrentCatalog()
    {
        var catalogPath = CreateTempCatalog(CreateCurrentCatalog());
        var originalBytes = File.ReadAllBytes(catalogPath);
        try
        {
            var document = Deserialize(originalBytes);
            var badge = document.Items.Single(item => item.CatalogItemId == "BAD-00004");
            badge.Subtype = "Exploration";

            var written = CatalogPromotionWriteGuard.Commit(
                catalogPath,
                originalBytes,
                document,
                CatalogPromotionOwnership.Badges);

            Assert.NotEqual(originalBytes, written);
            using var json = JsonDocument.Parse(File.ReadAllBytes(catalogPath));
            Assert.Equal("item-ref-3.1.0", json.RootElement.GetProperty("manifest").GetProperty("catalogVersion").GetString());
            Assert.Equal("Exploration", FindItem(json.RootElement, "BAD-00004").GetProperty("subtype").GetString());
            Assert.Equal(
                "Inspirations.Small.Insight",
                FindItem(json.RootElement, "INS-00001").GetProperty("homecomingSourceId").GetString());
            Assert.Equal(
                "Tiny Fire",
                json.RootElement.GetProperty("enhancementSets")[0].GetProperty("bonuses")[0]
                    .GetProperty("autoPowers")[0].GetProperty("displayName").GetString());
        }
        finally
        {
            File.Delete(catalogPath);
        }
    }

    [Fact]
    public void Commit_RejectsVersionDowngradeAndLeavesOriginalBytesUnchanged()
    {
        var catalogPath = CreateTempCatalog(CreateCurrentCatalog());
        var originalBytes = File.ReadAllBytes(catalogPath);
        try
        {
            var document = Deserialize(originalBytes);
            document.Manifest!.CatalogVersion = "item-ref-3.0.0";
            document.Manifest.SourceRevision = "homecoming-badge-promotion-2026-08-14";

            var exception = Assert.Throws<CatalogPromotionWriteException>(() =>
                CatalogPromotionWriteGuard.Commit(
                    catalogPath,
                    originalBytes,
                    document,
                    CatalogPromotionOwnership.Badges));

            Assert.Contains("downgrade", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(originalBytes, File.ReadAllBytes(catalogPath));
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(catalogPath)!, Path.GetFileName(catalogPath) + ".*.tmp"));
        }
        finally
        {
            File.Delete(catalogPath);
        }
    }

    [Fact]
    public void Commit_RejectsCatalogNewerThanSimulatedOlderTool()
    {
        var catalogPath = CreateTempCatalog(CreateCurrentCatalog());
        var originalBytes = File.ReadAllBytes(catalogPath);
        try
        {
            var document = Deserialize(originalBytes);
            var exception = Assert.Throws<CatalogPromotionWriteException>(() =>
                CatalogPromotionWriteGuard.Commit(
                    catalogPath,
                    originalBytes,
                    document,
                    CatalogPromotionOwnership.Badges,
                    maximumUnderstoodCatalogVersion: "item-ref-3.0.0"));

            Assert.Contains("understands at most", exception.Message, StringComparison.Ordinal);
            Assert.Equal(originalBytes, File.ReadAllBytes(catalogPath));
        }
        finally
        {
            File.Delete(catalogPath);
        }
    }

    [Fact]
    public void Commit_RejectsFailedValidationWithoutWriting()
    {
        var catalogPath = CreateTempCatalog(CreateCurrentCatalog());
        var originalBytes = File.ReadAllBytes(catalogPath);
        try
        {
            var document = Deserialize(originalBytes);
            document.Items.Single(item => item.CatalogItemId == "BAD-00004").CurrentDisplayName = null;

            var exception = Assert.Throws<CatalogPromotionWriteException>(() =>
                CatalogPromotionWriteGuard.Commit(
                    catalogPath,
                    originalBytes,
                    document,
                    CatalogPromotionOwnership.Badges));

            Assert.Contains("failed validation", exception.Message, StringComparison.Ordinal);
            Assert.Equal(originalBytes, File.ReadAllBytes(catalogPath));
        }
        finally
        {
            File.Delete(catalogPath);
        }
    }

    [Fact]
    public void Commit_RejectsSimulatedBadgePromotionThatDestroysInspirationAndSetBonusData()
    {
        var catalogPath = CreateTempCatalog(CreateCurrentCatalog());
        var originalBytes = File.ReadAllBytes(catalogPath);
        try
        {
            var document = Deserialize(originalBytes);
            document.Manifest!.CatalogVersion = "item-ref-3.0.0";
            document.Manifest.SourceRevision = "homecoming-badge-promotion-2026-08-14";
            document.Items.RemoveAll(item => item.CatalogItemId == "INS-00076");
            var insight = document.Items.Single(item => item.CatalogItemId == "INS-00001");
            insight.HomecomingSourceId = null;
            insight.Icon = null;
            insight.DisplayHelp = null;
            insight.InspirationForm = null;
            insight.InspirationStandardTier = null;
            insight.HomecomingCategory = null;
            document.EnhancementSets[0].Bonuses![0].AutoPowers![0].DisplayName = null;

            var exception = Assert.Throws<CatalogPromotionWriteException>(() =>
                CatalogPromotionWriteGuard.Commit(
                    catalogPath,
                    originalBytes,
                    document,
                    CatalogPromotionOwnership.Badges));

            Assert.True(
                exception.Message.Contains("downgrade", StringComparison.OrdinalIgnoreCase)
                || exception.Message.Contains("unrelated", StringComparison.OrdinalIgnoreCase),
                exception.Message);
            Assert.Equal(originalBytes, File.ReadAllBytes(catalogPath));
        }
        finally
        {
            File.Delete(catalogPath);
        }
    }

    [Fact]
    public void Commit_RejectsRemovingUnownedInspirationEvenWhenVersionIsUnchanged()
    {
        var catalogPath = CreateTempCatalog(CreateCurrentCatalog());
        var originalBytes = File.ReadAllBytes(catalogPath);
        try
        {
            var document = Deserialize(originalBytes);
            document.Items.RemoveAll(item => item.Family == nameof(ReferenceItemFamily.Inspiration));
            document.Aliases.RemoveAll(alias => alias.CatalogItemId?.StartsWith("INS-", StringComparison.Ordinal) == true);

            var exception = Assert.Throws<CatalogPromotionWriteException>(() =>
                CatalogPromotionWriteGuard.Commit(
                    catalogPath,
                    originalBytes,
                    document,
                    CatalogPromotionOwnership.Badges));

            Assert.Contains("INS-00001", exception.Message, StringComparison.Ordinal);
            Assert.Equal(originalBytes, File.ReadAllBytes(catalogPath));
        }
        finally
        {
            File.Delete(catalogPath);
        }
    }

    [Fact]
    public void Commit_RejectsProductionPathWithoutExplicitFlag()
    {
        var root = Path.Combine(Path.GetTempPath(), "coh-catalog-guard-" + Guid.NewGuid().ToString("N"));
        var catalogPath = Path.Combine(root, "src", "CoHAnalytics", "ReferenceData", "item-catalog.v1.json");
        Directory.CreateDirectory(Path.GetDirectoryName(catalogPath)!);
        var originalBytes = Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(CreateCurrentCatalog(), CatalogPromotionWriteGuard.WriteOptions) + "\n");
        File.WriteAllBytes(catalogPath, originalBytes);
        try
        {
            Assert.True(CatalogPromotionWriteGuard.IsProductionCatalogPath(catalogPath));
            var document = Deserialize(originalBytes);
            document.Items.Single(item => item.CatalogItemId == "BAD-00004").Subtype = "Exploration";

            var exception = Assert.Throws<CatalogPromotionWriteException>(() =>
                CatalogPromotionWriteGuard.Commit(
                    catalogPath,
                    originalBytes,
                    document,
                    CatalogPromotionOwnership.Badges));

            Assert.Contains(CatalogPromotionWriteGuard.AllowProductionWriteOption, exception.Message, StringComparison.Ordinal);
            Assert.Equal(originalBytes, File.ReadAllBytes(catalogPath));

            CatalogPromotionWriteGuard.Commit(
                catalogPath,
                originalBytes,
                document,
                CatalogPromotionOwnership.Badges,
                allowProductionWrite: true);
            using var json = JsonDocument.Parse(File.ReadAllBytes(catalogPath));
            Assert.Equal("Exploration", FindItem(json.RootElement, "BAD-00004").GetProperty("subtype").GetString());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTempCatalog(ItemReferenceCatalogDocument document)
    {
        var path = Path.Combine(Path.GetTempPath(), $"coh-catalog-guard-{Guid.NewGuid():N}.json");
        File.WriteAllBytes(
            path,
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(document, CatalogPromotionWriteGuard.WriteOptions) + "\n"));
        return path;
    }

    private static ItemReferenceCatalogDocument Deserialize(byte[] bytes) =>
        JsonSerializer.Deserialize<ItemReferenceCatalogDocument>(
            bytes,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
        ?? throw new InvalidOperationException("Catalog document is empty.");

    private static JsonElement FindItem(JsonElement root, string catalogItemId) =>
        root.GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("catalogItemId").GetString() == catalogItemId);

    private static ItemReferenceCatalogDocument CreateCurrentCatalog() =>
        new()
        {
            Manifest = new ItemReferenceManifestDocument
            {
                CatalogVersion = "item-ref-3.1.0",
                SourceRevision = "homecoming-inspiration-promotion-2026-08-16",
                SourceNotes = "Promote Homecoming Inspirations into the canonical catalog with client-derived metadata.",
                HomecomingCompatibility = new ItemReferenceHomecomingCompatibilityDocument
                {
                    BuildMin = "Issue 28, Page 3 - 28.3.7927",
                    BuildMax = "Issue 28, Page 3 - 28.3.7927"
                }
            },
            Items =
            [
                new ItemReferenceRecordDocument
                {
                    CatalogItemId = "BAD-00004",
                    Family = nameof(ReferenceItemFamily.Badge),
                    Subtype = "ACHIEVEMENT",
                    CurrentDisplayName = "Still Standing",
                    ActiveStatus = nameof(ReferenceActiveStatus.Active),
                    VerificationStatus = nameof(ReferenceVerificationStatus.SecondaryOnly),
                    Icon = "badge_count_1750.tga"
                },
                new ItemReferenceRecordDocument
                {
                    CatalogItemId = "INS-00001",
                    Family = nameof(ReferenceItemFamily.Inspiration),
                    Subtype = "Single",
                    CurrentDisplayName = "Insight",
                    ActiveStatus = nameof(ReferenceActiveStatus.Active),
                    VerificationStatus = nameof(ReferenceVerificationStatus.VerifiedMultiSource),
                    Icon = "Inspiration_Accuracy_Lvl_1.tga",
                    DisplayHelp = "Makes your attacks more accurate.",
                    HomecomingSourceId = "Inspirations.Small.Insight",
                    HomecomingCategory = "Small",
                    InspirationStandardTier = "Small",
                    InspirationForm = "Single"
                },
                new ItemReferenceRecordDocument
                {
                    CatalogItemId = "INS-00076",
                    Family = nameof(ReferenceItemFamily.Inspiration),
                    Subtype = "Special",
                    CurrentDisplayName = "Happy Anniversary!",
                    ActiveStatus = nameof(ReferenceActiveStatus.Active),
                    VerificationStatus = nameof(ReferenceVerificationStatus.VerifiedMultiSource),
                    Icon = "Inspiration_Anniversary.tga",
                    HomecomingSourceId = "Inspirations.Anniversary.Anniversary_Defense",
                    HomecomingCategory = "Anniversary",
                    InspirationForm = "Special/Event"
                }
            ],
            Aliases =
            [
                new ItemReferenceAliasRecordDocument
                {
                    CatalogItemId = "INS-00001",
                    Locale = "en",
                    Text = "Insight",
                    NameKind = "LogReceipt",
                    IsPreferred = true
                }
            ],
            EnhancementSets =
            [
                new EnhancementSetReferenceRecordDocument
                {
                    CatalogItemId = "SET-00001",
                    CurrentDisplayName = "Aegis",
                    ActiveStatus = nameof(ReferenceActiveStatus.Active),
                    VerificationStatus = nameof(ReferenceVerificationStatus.VerifiedMultiSource),
                    HomecomingSetId = "Aegis",
                    ServerAvailability =
                    [
                        new ReferenceServerAvailabilityDocument
                        {
                            ServerKey = "Homecoming",
                            Status = "Current"
                        }
                    ],
                    Bonuses =
                    [
                        new EnhancementSetBonusReferenceRecordDocument
                        {
                            MinimumBoosts = 2,
                            MaximumBoosts = 3,
                            RequiresPattern = "None",
                            RequiresTokens = [],
                            RequiredEnhancementIds = [],
                            AutoPowers =
                            [
                                new EnhancementSetBonusPowerReferenceRecordDocument
                                {
                                    HomecomingSourceId = "Set_Bonus.Set_Bonus.Fire_Cold_Mez_Res_1",
                                    DisplayName = "Tiny Fire",
                                    DisplayHelp = "Increases Fire and Cold resistance."
                                }
                            ]
                        }
                    ]
                }
            ],
            Badges =
            [
                new BadgeReferenceRecordDocument
                {
                    CatalogItemId = "BAD-00004",
                    HomecomingSourceId = "StillStanding",
                    SetTitleId = 1,
                    CanonicalCategory = "ACHIEVEMENT",
                    BadgeType = 1,
                    ReferenceKind = nameof(ReferenceBadgeKind.Achievement),
                    HeroName = "Still Standing",
                    VillainName = "Still Standing",
                    VerificationStatus = nameof(ReferenceVerificationStatus.SecondaryOnly)
                }
            ]
        };
}
