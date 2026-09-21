using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CoHAnalytics.ReferenceData;
using CoHAnalytics.ReferenceDataGenerator;

namespace CoHAnalytics.Tests.ReferenceDataGenerator;

[Collection(LiveInstallPromotionCollection.Name)]
public sealed class HomecomingEnhancementPromotionTests
{
    private readonly LiveInstallPromotionFixture _promotion;

    public HomecomingEnhancementPromotionTests(LiveInstallPromotionFixture promotion)
    {
        _promotion = promotion;
    }

    private static string LiveInstallRoot => LiveInstallTestEnvironment.InstallRoot;

    [Fact]
    [Trait("Category", "LiveInstall")]
    public void LiveInstall_Promotion_IsDeterministicAndPreservesStableIds()
    {
        var catalogPath = Path.Combine(
            Path.GetTempPath(),
            $"coh-enh-promote-{Guid.NewGuid():N}.json");
        try
        {
            using (var embedded = typeof(ItemReferenceCatalogFactory).Assembly.GetManifestResourceStream(
                       ItemReferenceCatalogFactory.ProductionCatalogResourceName)
                   ?? throw new InvalidOperationException("Embedded production catalog missing."))
            using (var output = File.Create(catalogPath))
            {
                embedded.CopyTo(output);
            }

            PromotionManifestOwnershipTestSupport.WriteFutureManifest(catalogPath);
            var beforeStableIds = PromotionManifestOwnershipTestSupport.ReadStableIds(catalogPath, "Enhancement");
            var beforeBytes = File.ReadAllBytes(catalogPath);
            using var beforeDocument = JsonDocument.Parse(beforeBytes);
            var beforeAccuracyId = FindItemId(beforeDocument, "Invention: Accuracy");
            var beforeBonesnapId = FindSetId(beforeDocument, "Bonesnap");
            Assert.Equal("ENH-00001", beforeAccuracyId);
            Assert.Equal("SET-00001", beforeBonesnapId);

            var beforeHashes = SnapshotPiggs(LiveInstallRoot);
            var first = _promotion.Promote(catalogPath);
            var midBytes = File.ReadAllBytes(catalogPath);
            var second = _promotion.Promote(catalogPath);
            var afterBytes = File.ReadAllBytes(catalogPath);
            var afterHashes = SnapshotPiggs(LiveInstallRoot);

            Assert.Equal(first.CatalogSha256, second.CatalogSha256);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(midBytes)), first.CatalogSha256);
            Assert.Equal(1, _promotion.SourceLoadCount);
            Assert.Equal(1, _promotion.Diagnostics.SourceParseCount);
            Assert.Equal(2, _promotion.Diagnostics.PiggDirectoryParseCount);
            Assert.Equal(midBytes, afterBytes);
            Assert.Equal(beforeBytes, midBytes);
            Assert.Equal(beforeHashes, afterHashes);
            PromotionManifestOwnershipTestSupport.AssertFutureManifestPreserved(catalogPath);
            Assert.Equal(
                beforeStableIds,
                PromotionManifestOwnershipTestSupport.ReadStableIds(catalogPath, "Enhancement"));

            using var afterDocument = JsonDocument.Parse(afterBytes);
            Assert.Equal("ENH-00001", FindItemId(afterDocument, "Invention: Accuracy"));
            Assert.Equal("SET-00001", FindSetId(afterDocument, "Bonesnap"));
            Assert.Equal(
                "Recharge",
                FindItemProperty(afterDocument, "Invention: Recharge Reduction", "commonIoBoostType"));
            Assert.Equal(
                "Recharge Reduction",
                FindItemProperty(
                    afterDocument,
                    "Invention: Recharge Reduction",
                    "commonIoBoostTypeDisplayText"));
            Assert.Equal(
                "ECToHitDeBuff",
                FindSetProperty(afterDocument, "Dampened Spirits", "categoryCode"));
            Assert.Equal(
                "To-Hit Debuff",
                FindSetProperty(afterDocument, "Dampened Spirits", "categoryDisplayText"));
            Assert.EndsWith(
                ".tga",
                FindItemProperty(afterDocument, "Invention: Accuracy", "icon")!,
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("\"iconBytes\"", Encoding.UTF8.GetString(afterBytes), StringComparison.Ordinal);

            Assert.True(first.Stats.EnhancementsPromoted > 0);
            Assert.True(first.Stats.SetsPromoted > 0);
            Assert.Equal(0, first.Stats.DifferingVariantIconEnhancements);
            Assert.Equal(0, first.Stats.UnresolvedBonusHelp);
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
    [Trait("Category", "LiveInstall")]
    public void LiveInstall_BonusAutoPowerHelp_DerivedFromLivePowers_NotPriorCatalog()
    {
        var catalogPath = Path.Combine(
            Path.GetTempPath(),
            $"coh-enh-promote-help-{Guid.NewGuid():N}.json");
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
                baseline.RootElement.GetProperty("items").WriteTo(writer);
                writer.WritePropertyName("aliases");
                baseline.RootElement.GetProperty("aliases").WriteTo(writer);
                writer.WritePropertyName("enhancementSets");
                writer.WriteStartArray();
                foreach (var set in baseline.RootElement.GetProperty("enhancementSets").EnumerateArray())
                {
                    writer.WriteStartObject();
                    foreach (var property in set.EnumerateObject())
                    {
                        if (property.NameEquals("bonuses"))
                        {
                            writer.WritePropertyName("bonuses");
                            writer.WriteStartArray();
                            foreach (var bonus in property.Value.EnumerateArray())
                            {
                                writer.WriteStartObject();
                                foreach (var bonusProperty in bonus.EnumerateObject())
                                {
                                    if (bonusProperty.NameEquals("autoPowers"))
                                    {
                                        writer.WritePropertyName("autoPowers");
                                        writer.WriteStartArray();
                                        foreach (var autoPower in bonusProperty.Value.EnumerateArray())
                                        {
                                            writer.WriteStartObject();
                                            writer.WriteString(
                                                "homecomingSourceId",
                                                autoPower.GetProperty("homecomingSourceId").GetString()!);
                                            writer.WriteNull("displayHelp");
                                            writer.WriteEndObject();
                                        }

                                        writer.WriteEndArray();
                                        continue;
                                    }

                                    bonusProperty.WriteTo(writer);
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
                writer.WriteEndObject();
            }

            File.WriteAllBytes(catalogPath, clearedStream.ToArray());

            var result = _promotion.Promote(catalogPath);
            Assert.Equal(0, result.Stats.UnresolvedBonusHelp);
            Assert.Equal(1138, result.Stats.BonusAutoPowers);

            using var promoted = JsonDocument.Parse(File.ReadAllBytes(catalogPath));
            Assert.All(
                promoted.RootElement.GetProperty("enhancementSets").EnumerateArray()
                    .Where(set => set.TryGetProperty("bonuses", out _))
                    .SelectMany(set => set.GetProperty("bonuses").EnumerateArray())
                    .Where(bonus => bonus.TryGetProperty("autoPowers", out _))
                    .SelectMany(bonus => bonus.GetProperty("autoPowers").EnumerateArray()),
                autoPower => Assert.False(string.IsNullOrWhiteSpace(
                    autoPower.GetProperty("displayHelp").GetString())));

            var hecatombRecovery = promoted.RootElement.GetProperty("enhancementSets")
                .EnumerateArray()
                .First(set => set.GetProperty("currentDisplayName").GetString() == "Hecatomb")
                .GetProperty("bonuses")[0]
                .GetProperty("autoPowers")[0];
            Assert.Equal("Set_Bonus.Set_Bonus.Improved_Recovery_7", hecatombRecovery.GetProperty("homecomingSourceId").GetString());
            Assert.Equal("Improves your Recovery by 4%.", hecatombRecovery.GetProperty("displayHelp").GetString());
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
    [Trait("Category", "LiveInstall")]
    public void LiveInstall_CanonicalBonusAutoPowerHelpResolution_UsesPowersAndMessages()
    {
        var source = HomecomingStaticDataSourceDiscovery.Discover(LiveInstallRoot);
        var powersBytes = HomecomingPiggMemberReader.ReadMember(
            source.BinPowersPiggPath,
            HomecomingEnhancementCandidateGenerator.PowersMember);
        var messages = HomecomingMessageStoreReader.Read(
            HomecomingPiggMemberReader.ReadMember(source.BinPiggPath, "bin/clientmessages-en.bin"));
        var discovery = HomecomingPowersBoostDiscoveryReader.TryGetBoost(
            powersBytes,
            "Set_Bonus.Set_Bonus.Improved_Recovery_7");
        Assert.NotNull(discovery);
        Assert.False(string.IsNullOrWhiteSpace(discovery!.DisplayHelpMessageKey));
        Assert.True(messages.TryResolve(discovery.DisplayHelpMessageKey, out var help));
        Assert.Equal("Improves your Recovery by 4%.", help);
    }

    [Fact]
    [Trait("Category", "LiveInstall")]
    public void LiveInstall_MessageStore_ContainsCanonicalPvpRarityIdentity()
    {
        var source = HomecomingStaticDataSourceDiscovery.Discover(LiveInstallRoot);
        var messages = HomecomingMessageStoreReader.Read(
            HomecomingPiggMemberReader.ReadMember(source.BinPiggPath, "bin/clientmessages-en.bin"));

        Assert.True(messages.TryResolve("ECPVP", out var label));
        Assert.Equal("Rarity: PvP", label);
        Assert.False(messages.TryResolve("ECPvP", out _));
    }

    [Fact]
    [Trait("Category", "LiveInstall")]
    public void LiveInstall_RepresentativeConditionalBonuses_PreserveRequiresAndHelp()
    {
        var catalogPath = Path.Combine(
            Path.GetTempPath(),
            $"coh-enh-promote-conditional-{Guid.NewGuid():N}.json");
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
            using var document = JsonDocument.Parse(File.ReadAllBytes(catalogPath));
            var sets = document.RootElement.GetProperty("enhancementSets")
                .EnumerateArray()
                .Where(set => set.TryGetProperty("homecomingSetId", out _))
                .ToDictionary(
                    set => set.GetProperty("homecomingSetId").GetString()!,
                    set => set,
                    StringComparer.Ordinal);

            var hecatomb = sets["Hecatomb"];
            var ordinary = hecatomb.GetProperty("bonuses")[0];
            Assert.Equal("None", ordinary.GetProperty("requiresPattern").GetString());
            Assert.Empty(ordinary.GetProperty("requiresTokens").EnumerateArray().Select(token => token.GetString()!).ToArray());
            Assert.Equal("Improves your Recovery by 4%.", ordinary.GetProperty("autoPowers")[0].GetProperty("displayHelp").GetString());

            var aegis = sets["Aegis"];
            var pieceGate = aegis.GetProperty("bonuses").EnumerateArray()
                .First(bonus => bonus.GetProperty("requiresPattern").GetString() == "PieceGate");
            Assert.Contains("Crafted_Aegis_F", pieceGate.GetProperty("requiresTokens").EnumerateArray().Select(token => token.GetString()!));
            Assert.NotEmpty(pieceGate.GetProperty("requiredEnhancementIds").EnumerateArray());
            Assert.False(string.IsNullOrWhiteSpace(
                pieceGate.GetProperty("autoPowers")[0].GetProperty("displayHelp").GetString()));

            var luck = sets["Luck_of_the_Gambler"];
            var pieceGateLuck = luck.GetProperty("bonuses").EnumerateArray()
                .First(bonus => bonus.GetProperty("requiresPattern").GetString() == "PieceGate");
            Assert.Contains("Crafted_Luck_of_the_Gambler_F", pieceGateLuck.GetProperty("requiresTokens").EnumerateArray().Select(token => token.GetString()!));
            Assert.False(string.IsNullOrWhiteSpace(
                pieceGateLuck.GetProperty("autoPowers")[0].GetProperty("displayHelp").GetString()));

            var pvpSet = sets["Gladiators_Strike"];
            Assert.Equal("ECPVP", pvpSet.GetProperty("rarityCode").GetString());
            Assert.Equal("PvP", pvpSet.GetProperty("rarityDisplayText").GetString());
            var pvp = pvpSet.GetProperty("bonuses").EnumerateArray()
                .First(bonus => bonus.GetProperty("requiresPattern").GetString() == "PvPMap");
            Assert.Equal(["isPVPMap?"], pvp.GetProperty("requiresTokens").EnumerateArray().Select(token => token.GetString()!).ToArray());
            Assert.False(string.IsNullOrWhiteSpace(
                pvp.GetProperty("autoPowers")[0].GetProperty("displayHelp").GetString()));

            var multiAutoPowerTier = sets["Unrelenting_Fury"].GetProperty("bonuses").EnumerateArray()
                .First(bonus => bonus.GetProperty("autoPowers").GetArrayLength() > 1);
            Assert.Equal(3, multiAutoPowerTier.GetProperty("autoPowers").GetArrayLength());
            Assert.All(
                multiAutoPowerTier.GetProperty("autoPowers").EnumerateArray(),
                autoPower => Assert.False(string.IsNullOrWhiteSpace(
                    autoPower.GetProperty("displayHelp").GetString())));

            Assert.Equal(0, result.Stats.UnresolvedBonusHelp);
        }
        finally
        {
            if (File.Exists(catalogPath))
            {
                File.Delete(catalogPath);
            }
        }
    }

    private static IReadOnlyList<string> SnapshotPiggs(string installRoot)
    {
        var source = HomecomingStaticDataSourceDiscovery.Discover(installRoot);
        return
        [
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source.BinPiggPath))),
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source.BinPowersPiggPath)))
        ];
    }

    private static string? FindItemId(JsonDocument document, string displayName) =>
        document.RootElement.GetProperty("items")
            .EnumerateArray()
            .First(item => item.GetProperty("currentDisplayName").GetString() == displayName)
            .GetProperty("catalogItemId")
            .GetString();

    private static string? FindSetId(JsonDocument document, string displayName) =>
        document.RootElement.GetProperty("enhancementSets")
            .EnumerateArray()
            .First(item => item.GetProperty("currentDisplayName").GetString() == displayName)
            .GetProperty("catalogItemId")
            .GetString();

    private static string? FindItemProperty(JsonDocument document, string displayName, string property) =>
        document.RootElement.GetProperty("items")
            .EnumerateArray()
            .First(item => item.GetProperty("currentDisplayName").GetString() == displayName)
            .GetProperty(property)
            .GetString();

    private static string? FindSetProperty(JsonDocument document, string displayName, string property) =>
        document.RootElement.GetProperty("enhancementSets")
            .EnumerateArray()
            .First(item => item.GetProperty("currentDisplayName").GetString() == displayName)
            .GetProperty(property)
            .GetString();
}
