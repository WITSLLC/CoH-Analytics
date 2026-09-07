using System.IO;
using System.Text;
using CoHAnalytics.Models;
using CoHAnalytics.ReferenceData;
using CoHAnalytics.Services;
using CoHAnalytics.Tests.Orchestration;

namespace CoHAnalytics.Tests.Services;

public sealed class CharacterBadgeBuildSyncServiceTests
{
    private static readonly string[] BlueDevilBadgeSourceIds =
    [
        "BigTime",
        "TruckerTour",
        "OuroborosTour1",
        "MissionArchitectTourism",
        "KingsRowTour6",
        "KingsRowTour7",
        "KingsRowTour8",
        "PeregrineIslandTour3",
        "AtlasParkTour1",
        "AtlasParkTour3",
        "AtlasParkTour4",
        "AtlasParkTour5",
        "AtlasParkTour6",
        "AtlasParkTour7",
        "AtlasParkTour8",
        "Rookie",
        "KingsRowTour1",
        "KingsRowTour2",
        "KingsRowTour3",
        "KingsRowTour4",
        "KingsRowTour5",
        "P_GotTip",
        "P_MoralityMission",
        "SL1_SewerTrial_Complete",
        "Level10",
        "Level20",
        "Level30",
        "P_VigilanteAlignment",
        "P_VillainAlignment",
        "P_Ascended",
        "P_Descended",
        "P_ComeFullCircle",
        "P_Marauder",
        "Celebrity",
        "Sensation",
        "SL1_SewerTrial_LostWorshippersKilled",
        "SupergroupBaseHome",
        "Tourist",
        "Collector",
        "Explorer",
        "LRTAccolade",
        "TheStingerPatron",
        "AtlasParkExplorer",
        "KingsRowExplorer",
        "DVDEdition",
        "ButtonMan",
        "CrabSpider",
        "FireCaster",
        "LegacyEarth",
        "NebulaBuckshot",
        "NightWidow",
        "TacOps",
        "WolfSpider",
        "P_DefeatClockwork",
        "Halloween2008Badge1",
        "Halloween2008Badge2",
        "Halloween2008Badge3",
        "Halloween2008Badge4",
        "OuroborosEnabled",
        "AuctionRecipes",
        "AuctionSeller1",
        "AuctionSeller2",
        "AuctionSeller3",
        "ArchitectTickets100",
        "ArchitectAuthor50",
        "ArchitectTotalStars100",
        "ArchitectFirstPlay",
        "ArchitectCustomBoss"
    ];

    [Fact]
    public void Known_homecoming_source_ids_resolve_to_catalog_badge_ids()
    {
        var catalog = ItemReferenceCatalogFactory.LoadEmbeddedProduction();
        Assert.True(catalog.IsLoaded, catalog.LoadFailureReason);

        Assert.True(catalog.TryGetBadgeByHomecomingSourceId("AtlasParkTour3", out var heroCorps));
        Assert.Equal("BAD-01921", heroCorps.CatalogItemId);
        Assert.True(catalog.TryGetBadgeByHomecomingSourceId("AtlasParkTour4", out var patriot));
        Assert.Equal("BAD-01922", patriot.CatalogItemId);
        Assert.True(catalog.TryGetBadgeByHomecomingSourceId("Collector", out var collector));
        Assert.Equal("BAD-02061", collector.CatalogItemId);
        Assert.True(catalog.TryGetBadgeByHomecomingSourceId("AuctionSeller2", out var salesman));
        Assert.Equal("BAD-01936", salesman.CatalogItemId);
    }

    [Fact]
    public void Sync_adds_missing_acquisitions_and_is_idempotent()
    {
        using var harness = CreateHarness(
            """
            Badges Earned:
            ------------------
            AtlasParkTour3
            AtlasParkTour4
            Collector
            """);

        var first = harness.Service.SyncFromBuild(
            harness.AccountStableId,
            harness.AccountFolder,
            harness.RecordId);

        Assert.Equal(CharacterBadgeBuildSyncStatus.Completed, first.Status);
        Assert.Equal(3, first.AddedCount);
        Assert.Equal(0, first.AlreadyTrackedCount);
        Assert.Equal(0, first.UnrecognizedCount);
        Assert.Equal(3, harness.BadgeRepository.GetAcquiredBadgeIds(harness.RecordId).Count);

        var second = harness.Service.SyncFromBuild(
            harness.AccountStableId,
            harness.AccountFolder,
            harness.RecordId);

        Assert.Equal(0, second.AddedCount);
        Assert.Equal(3, second.AlreadyTrackedCount);
        Assert.Equal(0, second.UnrecognizedCount);
        Assert.Equal(3, harness.BadgeRepository.GetAcquiredBadgeIds(harness.RecordId).Count);
    }

    [Fact]
    public void Sync_does_not_remove_existing_acquisitions_absent_from_build()
    {
        using var harness = CreateHarness(
            """
            Badges Earned:
            ------------------
            Rookie
            """);

        var preexisting = harness.Catalog.TryGetBadgeByHomecomingSourceId("Collector", out var collector)
            ? collector
            : throw new InvalidOperationException("Collector badge missing from catalog.");
        harness.BadgeRepository.RecordAcquisition(
            harness.RecordId,
            harness.AccountStableId,
            preexisting.CatalogItemId,
            preexisting.HeroName,
            DateTimeOffset.UtcNow);

        var result = harness.Service.SyncFromBuild(
            harness.AccountStableId,
            harness.AccountFolder,
            harness.RecordId);

        Assert.Equal(1, result.AddedCount);
        Assert.Equal(2, harness.BadgeRepository.GetAcquiredBadgeIds(harness.RecordId).Count);
        Assert.True(harness.BadgeRepository.IsBadgeAcquired(harness.RecordId, preexisting.CatalogItemId));
    }

    [Fact]
    public void Unknown_source_id_is_skipped_and_counted_as_unrecognized()
    {
        using var harness = CreateHarness(
            """
            Badges Earned:
            ------------------
            Rookie
            TotallyFakeBadgeSourceId
            """);

        var result = harness.Service.SyncFromBuild(
            harness.AccountStableId,
            harness.AccountFolder,
            harness.RecordId);

        Assert.Equal(1, result.AddedCount);
        Assert.Equal(0, result.AlreadyTrackedCount);
        Assert.Equal(1, result.UnrecognizedCount);
        Assert.Equal(
            "1 badges added • 0 already tracked • 1 unrecognized",
            result.FormatUserMessage());
    }

    [Fact]
    public void Missing_build_file_returns_expected_message()
    {
        using var harness = CreateHarness(buildContent: null);

        var result = harness.Service.SyncFromBuild(
            harness.AccountStableId,
            harness.AccountFolder,
            harness.RecordId);

        Assert.Equal(CharacterBadgeBuildSyncStatus.MissingFile, result.Status);
        Assert.Equal(
            "No build file was found for this character. Save or export the character build in Homecoming, then try again.",
            result.FormatUserMessage());
    }

    [Fact]
    public void Build_without_badge_section_returns_expected_message()
    {
        using var harness = CreateHarness(
            """
            Example Brute: Level 38 Magic Class_Brute
            Level 1: Brute_Melee Fiery_Melee Scorch
            """);

        var result = harness.Service.SyncFromBuild(
            harness.AccountStableId,
            harness.AccountFolder,
            harness.RecordId);

        Assert.Equal(CharacterBadgeBuildSyncStatus.NoBadgeSection, result.Status);
        Assert.Equal("No badge data was found in the current build.", result.FormatUserMessage());
    }

    [Fact]
    public void Blue_devil_style_fixture_adds_thirteen_then_zero_on_resync()
    {
        Assert.Equal(68, BlueDevilBadgeSourceIds.Length);

        var catalog = ItemReferenceCatalogFactory.LoadEmbeddedProduction();
        Assert.True(catalog.IsLoaded, catalog.LoadFailureReason);

        var resolved = new List<BadgeReferenceRecord>();
        foreach (var sourceId in BlueDevilBadgeSourceIds)
        {
            Assert.True(
                catalog.TryGetBadgeByHomecomingSourceId(sourceId, out var badge),
                $"Expected catalog resolution for '{sourceId}'.");
            resolved.Add(badge);
        }

        Assert.Equal(68, resolved.Count);

        var preexisting = resolved.Take(55).ToArray();
        var expectedAdded = resolved.Skip(55).Select(badge => badge.CatalogItemId).ToHashSet(StringComparer.Ordinal);

        using var harness = CreateHarness(BuildBlueDevilContent(), catalog);
        foreach (var badge in preexisting)
        {
            harness.BadgeRepository.RecordAcquisition(
                harness.RecordId,
                harness.AccountStableId,
                badge.CatalogItemId,
                badge.HeroName,
                DateTimeOffset.UtcNow);
        }

        Assert.Equal(55, harness.BadgeRepository.GetAcquiredBadgeIds(harness.RecordId).Count);

        var first = harness.Service.SyncFromBuild(
            harness.AccountStableId,
            harness.AccountFolder,
            harness.RecordId);

        Assert.Equal(13, first.AddedCount);
        Assert.Equal(55, first.AlreadyTrackedCount);
        Assert.Equal(0, first.UnrecognizedCount);
        Assert.Equal(68, harness.BadgeRepository.GetAcquiredBadgeIds(harness.RecordId).Count);
        Assert.All(expectedAdded, id => Assert.True(harness.BadgeRepository.IsBadgeAcquired(harness.RecordId, id)));

        var second = harness.Service.SyncFromBuild(
            harness.AccountStableId,
            harness.AccountFolder,
            harness.RecordId);

        Assert.Equal(0, second.AddedCount);
        Assert.Equal(68, second.AlreadyTrackedCount);
        Assert.Equal(0, second.UnrecognizedCount);
        Assert.Equal(68, harness.BadgeRepository.GetAcquiredBadgeIds(harness.RecordId).Count);
    }

    private static string BuildBlueDevilContent()
    {
        var builder = new StringBuilder();
        builder.AppendLine("Example Brute: Level 38 Magic Class_Brute");
        builder.AppendLine("------------------");
        builder.AppendLine("Badges Earned:");
        builder.AppendLine("------------------");
        foreach (var sourceId in BlueDevilBadgeSourceIds)
        {
            builder.AppendLine(sourceId);
        }

        return builder.ToString();
    }

    private static SyncHarness CreateHarness(string? buildContent, IItemReferenceCatalog? catalog = null)
    {
        var dataDirectory = Path.Combine(
            Path.GetTempPath(),
            "coh-analytics-badge-sync",
            Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dataDirectory);

        var accountFolder = Path.Combine(dataDirectory, "account");
        Directory.CreateDirectory(Path.Combine(accountFolder, "Builds"));

        var characterRepository = new CharacterRepository(new CharacterRepositoryOptions
        {
            DataDirectory = dataDirectory,
            TimeProvider = new ManualTimeProvider()
        });
        var established = characterRepository.EstablishTrustedFromWelcome("acct-sync", "Example Brute");
        var record = characterRepository.TryGetRecord(established.RecordId!)!;
        Assert.True(CharacterShortId.TryParse(record.CharacterShortId, out var shortId));

        if (buildContent is not null)
        {
            File.WriteAllText(
                CharacterBuildImportService.GetBuildSaveFilePath(accountFolder, shortId),
                buildContent);
        }

        var badgeRepository = new CharacterBadgeAcquisitionRepository(
            new CharacterBadgeAcquisitionRepositoryOptions
            {
                DataDirectory = dataDirectory,
                TimeProvider = new ManualTimeProvider()
            });

        catalog ??= ItemReferenceCatalogFactory.LoadEmbeddedProduction();
        Assert.True(catalog.IsLoaded, catalog.LoadFailureReason);

        var service = new CharacterBadgeBuildSyncService(
            characterRepository,
            badgeRepository,
            catalog);

        return new SyncHarness(
            dataDirectory,
            accountFolder,
            record.AccountStableId,
            established.RecordId!,
            badgeRepository,
            catalog,
            service);
    }

    private sealed class SyncHarness : IDisposable
    {
        private readonly string _dataDirectory;

        public SyncHarness(
            string dataDirectory,
            string accountFolder,
            string accountStableId,
            CharacterRecordId recordId,
            CharacterBadgeAcquisitionRepository badgeRepository,
            IItemReferenceCatalog catalog,
            CharacterBadgeBuildSyncService service)
        {
            _dataDirectory = dataDirectory;
            AccountFolder = accountFolder;
            AccountStableId = accountStableId;
            RecordId = recordId;
            BadgeRepository = badgeRepository;
            Catalog = catalog;
            Service = service;
        }

        public string AccountFolder { get; }

        public string AccountStableId { get; }

        public CharacterRecordId RecordId { get; }

        public CharacterBadgeAcquisitionRepository BadgeRepository { get; }

        public IItemReferenceCatalog Catalog { get; }

        public CharacterBadgeBuildSyncService Service { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_dataDirectory))
                {
                    Directory.Delete(_dataDirectory, recursive: true);
                }
            }
            catch (IOException)
            {
            }
        }
    }
}
