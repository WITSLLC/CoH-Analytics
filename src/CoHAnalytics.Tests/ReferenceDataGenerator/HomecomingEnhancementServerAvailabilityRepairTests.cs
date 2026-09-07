using System.Security.Cryptography;
using System.Text;
using CoHAnalytics.ReferenceData;
using CoHAnalytics.ReferenceDataGenerator;

namespace CoHAnalytics.Tests.ReferenceDataGenerator;

[Collection(LiveInstallPromotionCollection.Name)]
public sealed class HomecomingEnhancementServerAvailabilityRepairTests
{
    private readonly LiveInstallPromotionFixture _promotion;

    public HomecomingEnhancementServerAvailabilityRepairTests(LiveInstallPromotionFixture promotion)
    {
        _promotion = promotion;
    }

    private static string LiveInstallRoot => LiveInstallTestEnvironment.InstallRoot;

    [Fact]
    public void Production_catalog_current_and_historical_census_matches_audit()
    {
        var catalog = (ItemReferenceCatalog)ItemReferenceCatalogFactory.LoadEmbeddedProduction();
        var enhancements = catalog.DebugItems.Values
            .Where(item => item.Family == ReferenceItemFamily.Enhancement)
            .ToArray();

        Assert.Equal(1797, enhancements.Length);
        Assert.Equal(1628, CountHomecoming(enhancements, ReferenceServerAvailabilityStatus.Current));
        Assert.Equal(169, CountHomecoming(enhancements, ReferenceServerAvailabilityStatus.Historical));

        var sets = catalog.GetEnhancementSets(ReferenceCatalogQueryScope.AllHomecomingIdentities).Values.ToArray();
        Assert.Equal(230, sets.Length);
        Assert.Equal(227, CountHomecoming(sets, ReferenceServerAvailabilityStatus.Current));
        Assert.Equal(3, CountHomecoming(sets, ReferenceServerAvailabilityStatus.Historical));
    }

    [Fact]
    public void Historical_enhancement_classification_accounts_for_all_169_records()
    {
        var catalog = (ItemReferenceCatalog)ItemReferenceCatalogFactory.LoadEmbeddedProduction();
        var historical = catalog.DebugItems.Values
            .Where(item =>
                item.Family == ReferenceItemFamily.Enhancement
                && ReferenceServerAvailabilitySupport.IsHistoricalHomecoming(item.ServerAvailability))
            .ToArray();

        var historicalSetIds = catalog.GetEnhancementSets(ReferenceCatalogQueryScope.AllHomecomingIdentities)
            .Values
            .Where(set => ReferenceServerAvailabilitySupport.IsHistoricalHomecoming(set.ServerAvailability))
            .Select(set => set.CatalogItemId)
            .ToHashSet(StringComparer.Ordinal);
        var currentSetIds = catalog.GetEnhancementSets(ReferenceCatalogQueryScope.CurrentHomecoming)
            .Keys
            .ToHashSet(StringComparer.Ordinal);

        var buckets = historical
            .Select(item => ClassifyHistoricalEnhancement(item, historicalSetIds, currentSetIds))
            .GroupBy(value => value, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        Assert.Equal(123, buckets.GetValueOrDefault("SetPieceMissingFromLiveLogical"));
        Assert.Equal(33, buckets.GetValueOrDefault("LegacyNonSetOther"));
        Assert.Equal(10, buckets.GetValueOrDefault("LegacyNonSetInventionNamed"));
        Assert.Equal(3, buckets.GetValueOrDefault("PieceOfHistoricalSet"));
        Assert.Equal(169, historical.Length);
    }

    [Fact]
    public void Representative_current_records_are_homecoming_current()
    {
        var catalog = ItemReferenceCatalogFactory.LoadEmbeddedProduction();

        Assert.True(catalog.TryGetById("ENH-00001", out var crafted));
        Assert.True(ReferenceServerAvailabilitySupport.IsCurrentHomecoming(crafted.ServerAvailability));

        Assert.True(catalog.TryGetById("ENH-01411", out var origin));
        Assert.True(ReferenceServerAvailabilitySupport.IsCurrentHomecoming(origin.ServerAvailability));

        Assert.True(catalog.TryGetById("ENH-01373", out var hamidon));
        Assert.Equal(ReferenceEnhancementFamily.Hamidon, hamidon.EnhancementFamily);
        Assert.True(ReferenceServerAvailabilitySupport.IsCurrentHomecoming(hamidon.ServerAvailability));

        Assert.True(catalog.TryGetById("ENH-00026", out var setPiece));
        Assert.True(ReferenceServerAvailabilitySupport.IsCurrentHomecoming(setPiece.ServerAvailability));
    }

    [Fact]
    public void Representative_historical_records_are_homecoming_historical()
    {
        var catalog = ItemReferenceCatalogFactory.LoadEmbeddedProduction();

        Assert.True(catalog.TryGetById("ENH-00847", out var legacyCommonIo));
        Assert.True(ReferenceServerAvailabilitySupport.IsHistoricalHomecoming(legacyCommonIo.ServerAvailability));
        Assert.Equal("CommonIO", legacyCommonIo.Subtype);

        Assert.True(catalog.TryGetEnhancementSetById("SET-00085", out var ascendency));
        Assert.True(ReferenceServerAvailabilitySupport.IsHistoricalHomecoming(ascendency.ServerAvailability));

        Assert.True(catalog.TryGetEnhancementSetById("SET-00229", out var ascendancy));
        Assert.True(ReferenceServerAvailabilitySupport.IsCurrentHomecoming(ascendancy.ServerAvailability));
    }

    [Fact]
    public void Current_browse_and_search_exclude_historical_records()
    {
        var catalog = ItemReferenceCatalogFactory.LoadEmbeddedProduction();

        Assert.Equal(227, catalog.GetEnhancementSets(ReferenceCatalogQueryScope.CurrentHomecoming).Count);
        Assert.Equal(230, catalog.GetEnhancementSets(ReferenceCatalogQueryScope.AllHomecomingIdentities).Count);
        Assert.False(catalog.GetEnhancementSets(ReferenceCatalogQueryScope.CurrentHomecoming).ContainsKey("SET-00085"));
        Assert.True(catalog.TryGetEnhancementSetById("SET-00085", out _));

        var searchResults = catalog.Search("Ascend", ReferenceItemFamily.Enhancement);
        Assert.Contains(searchResults, result => result.EnhancementSetId == "SET-00229");
        Assert.DoesNotContain(searchResults, result => result.EnhancementSetId == "SET-00085");

        Assert.False(catalog.TryResolve("Power of Grey: Endurance Reduction", out _));
        Assert.True(catalog.TryResolve("Power of Grey (Endurance Reduction)", out var currentGrey));
        Assert.True(ReferenceServerAvailabilitySupport.IsCurrentHomecoming(currentGrey.Item.ServerAvailability));
    }

    [Fact]
    public void Explicit_stable_id_lookup_returns_historical_records()
    {
        var catalog = ItemReferenceCatalogFactory.LoadEmbeddedProduction();

        Assert.True(catalog.TryGetEnhancementSetById("SET-00085", out var ascendency));
        Assert.True(ReferenceServerAvailabilitySupport.IsHistoricalHomecoming(ascendency.ServerAvailability));

        Assert.True(catalog.TryGetEnhancementSetById("SET-00228", out var bands));
        Assert.True(ReferenceServerAvailabilitySupport.IsHistoricalHomecoming(bands.ServerAvailability));
        Assert.Equal("Bands of Hermes", bands.CurrentDisplayName);

        Assert.True(catalog.TryGetById("ENH-00847", out var historicalEnhancement));
        Assert.True(ReferenceServerAvailabilitySupport.IsHistoricalHomecoming(historicalEnhancement.ServerAvailability));
    }

    [Fact]
    public void Loader_rejects_missing_duplicate_and_invalid_availability()
    {
        var missing = LoadCatalog("""
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
                  "verificationStatus": "VerifiedMultiSource"
                }
              ],
              "aliases": [],
              "enhancementSets": []
            }
            """);
        Assert.False(missing.Succeeded);
        Assert.Contains("ServerAvailability", missing.FailureReason, StringComparison.OrdinalIgnoreCase);

        var duplicate = LoadCatalog("""
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
                    { "serverKey": "Homecoming", "status": "Current" },
                    { "serverKey": "Homecoming", "status": "Historical" }
                  ],
                  "verificationStatus": "VerifiedMultiSource"
                }
              ],
              "aliases": [],
              "enhancementSets": []
            }
            """);
        Assert.False(duplicate.Succeeded);
        Assert.Contains("duplicate ServerAvailability", duplicate.FailureReason, StringComparison.OrdinalIgnoreCase);

        var invalidStatus = LoadCatalog("""
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
                    { "serverKey": "Homecoming", "status": "Missing" }
                  ],
                  "verificationStatus": "VerifiedMultiSource"
                }
              ],
              "aliases": [],
              "enhancementSets": []
            }
            """);
        Assert.False(invalidStatus.Succeeded);
        Assert.Contains("unknown ServerAvailability Status", invalidStatus.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "LiveInstall")]
    public void LiveInstall_Promotion_with_blank_historical_help_still_resolves_current_aliases()
    {
        var catalogPath = Path.Combine(Path.GetTempPath(), $"coh-availability-{Guid.NewGuid():N}.json");
        try
        {
            using (var embedded = typeof(ItemReferenceCatalogFactory).Assembly.GetManifestResourceStream(
                       ItemReferenceCatalogFactory.ProductionCatalogResourceName)
                   ?? throw new InvalidOperationException("Embedded production catalog missing."))
            using (var output = File.Create(catalogPath))
            {
                embedded.CopyTo(output);
            }

            var beforeBytes = File.ReadAllBytes(catalogPath);
            var first = _promotion.Promote(catalogPath);
            var afterBytes = File.ReadAllBytes(catalogPath);

            Assert.Equal(first.CatalogSha256, Convert.ToHexString(SHA256.HashData(afterBytes)));
            Assert.Equal(beforeBytes, afterBytes);
            Assert.Equal(0, first.Stats.UnresolvedBonusHelp);
            Assert.Equal(1628, first.Stats.HomecomingCurrentEnhancements);
            Assert.Equal(169, first.Stats.HomecomingHistoricalEnhancements);
        }
        finally
        {
            if (File.Exists(catalogPath))
            {
                File.Delete(catalogPath);
            }
        }
    }

    private static ItemReferenceCatalogLoadResult LoadCatalog(string json)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return ItemReferenceCatalogLoader.Load(stream);
    }

    private static int CountHomecoming<TRecord>(
        IEnumerable<TRecord> records,
        ReferenceServerAvailabilityStatus status)
        where TRecord : class
    {
        return records.Count(record =>
        {
            var availability = record switch
            {
                ItemReferenceRecord item => item.ServerAvailability,
                EnhancementSetReferenceRecord set => set.ServerAvailability,
                _ => throw new InvalidOperationException("Unexpected record type.")
            };

            return ReferenceServerAvailabilitySupport.TryGetHomecomingStatus(availability, out var parsed)
                   && parsed == status;
        });
    }

    private static string ClassifyHistoricalEnhancement(
        ItemReferenceRecord item,
        IReadOnlySet<string> historicalSetIds,
        IReadOnlySet<string> currentSetIds)
    {
        if (item.EnhancementSetId is not null && historicalSetIds.Contains(item.EnhancementSetId))
        {
            return "PieceOfHistoricalSet";
        }

        if (item.EnhancementSetId is not null && currentSetIds.Contains(item.EnhancementSetId))
        {
            return "SetPieceMissingFromLiveLogical";
        }

        if (item.CurrentDisplayName.StartsWith("Invention:", StringComparison.Ordinal))
        {
            return "LegacyNonSetInventionNamed";
        }

        if (item.CurrentDisplayName.Contains(':', StringComparison.Ordinal))
        {
            return "LegacyNonSetOther";
        }

        return "UnclassifiedHistoricalEnhancement";
    }
}
