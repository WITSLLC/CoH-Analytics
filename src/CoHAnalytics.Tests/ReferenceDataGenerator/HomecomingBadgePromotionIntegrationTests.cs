using System.Text;
using CoHAnalytics.ReferenceData;
using CoHAnalytics.ReferenceDataGenerator;

namespace CoHAnalytics.Tests.ReferenceDataGenerator;

public sealed class HomecomingBadgePromotionIntegrationTests
{
    private const string EmbeddedExplorationMarkdown =
        """
        ## Exploration badge set inventory

        | Set / zone | Completion badge(s) | Distinct member badges | Alignment / access | Source |
        |---|---|---:|---|---|
        | Atlas Park | Atlas Tour Guide | 1 | Hero | source |

        ## Badge records by zone/location

        ### Atlas Park

        | Badge | Alignment restrictions / aliases | X | Y | Z | Copy command | Nearby landmark / guidance | Variant / instance | Notes | Verification | Sources |
        |---|---|---:|---:|---:|---|---|---|---|---|---|
        | Atlas Tour Guide | Hero | 128.5 | 16.4 | -233 | `/thumbtack 128.5 16.4 -233` | marker | Standard zone | note | **Verified** | source |
        """;

    private const string EmbeddedCrosswalkMarkdown =
        """
        ## Complete crosswalk

        | SetTitle | Internal name | Category | Issue/status | Hero male | Hero female | Villain male | Villain female | Praetorian male | Praetorian female |
        |---:|---|---|---|---|---|---|---|---|---|
        | 1517 | `AtlasParkExplorer` | Accolades | Issue 02 | Atlas Tour Guide | - | Atlas Tour Guide | - | - | - |
        """;

    [Fact]
    public void Loader_ParsesEmbeddedExplorationAndCrosswalkSnippets()
    {
        var root = CreateFixtureRoot(EmbeddedExplorationMarkdown, EmbeddedCrosswalkMarkdown);
        try
        {
            var package = BadgeResearchPackageLoader.Load(root);
            Assert.Single(package.ExplorationZoneSets);
            Assert.Single(package.ExplorationLocations);
            Assert.Equal("Atlas Park", package.ExplorationLocations[0].ZoneName);
            Assert.Contains(
                package.TitleCrosswalk,
                row => row.InternalName == "AtlasParkExplorer" && row.SetTitleId == 1517);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Build_JoinsExplorationLocationByDisplayName()
    {
        var root = CreateFixtureRoot(EmbeddedExplorationMarkdown, EmbeddedCrosswalkMarkdown);
        try
        {
            var research = BadgeResearchPackageLoader.Load(root);
            var candidates = new[]
            {
                new HomecomingBadgeCandidateRecord(
                    "BAD-00001",
                    "AtlasParkExplorer",
                    1,
                    1,
                    "DEFS/BADGES/BADGES_TOURISM.DEF",
                    "TOURISM",
                    "P_HERO",
                    "Atlas Tour Guide",
                    "P_VILLAIN",
                    "Atlas Tour Guide",
                    null,
                    null,
                    null,
                    null,
                    "badge_tourist_01",
                    null,
                    nameof(HomecomingBadgeMatchStatus.NewFromHomecoming),
                    [])
            };

            var artifacts = HomecomingBadgePromotionSupport.Build(candidates, research);
            var location = Assert.Single(artifacts.BadgeLocations);
            Assert.Equal("BAD-00001", location.BadgeCatalogItemId);
            Assert.Equal("zone-atlas-park", location.ZoneId);
            Assert.Equal("ExplorationBadge", location.MarkerType);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Build_JoinsExplorationLocationWhenResearchUsesShortGenderVariantTitle()
    {
        const string explorationMarkdown =
            """
            ## Exploration badge set inventory

            | Set / zone | Completion badge(s) | Distinct member badges | Alignment / access | Source |
            |---|---|---:|---|---|
            | Bloody Bay | Bad Blood | 1 | Hero | source |

            ## Badge records by zone/location

            ### Bloody Bay

            | Badge | Alignment restrictions / aliases | X | Y | Z | Copy command | Nearby landmark / guidance | Variant / instance | Notes | Verification | Sources |
            |---|---|---:|---:|---:|---|---|---|---|---|---|
            | Burgermeister | All Primal alignments; aliases/variants: Burgermeister / Burgermeisterin | 1948 | -15.5 | 1805 | `/thumbtack 1948 -15.5 1805` | marker | Standard zone | note | **Likely Correct** | source |
            """;

        const string crosswalkMarkdown =
            """
            ## Complete crosswalk

            | SetTitle | Internal name | Category | Issue/status | Hero male | Hero female | Villain male | Villain female | Praetorian male | Praetorian female |
            |---:|---|---|---|---|---|---|---|---|---|
            | 2412 | `BloodyBayTour6` | Exploration | Issue 26 | Burgermeister | Burgermeisterin | - | - | - | - |
            """;

        var root = CreateFixtureRoot(explorationMarkdown, crosswalkMarkdown);
        try
        {
            File.WriteAllText(
                Path.Combine(root, "03 - Zone Index.md"),
                "## Bloody Bay\n- Exploration badges: Burgermeister\n",
                Encoding.UTF8);

            var research = BadgeResearchPackageLoader.Load(root);
            var candidates = new[]
            {
                new HomecomingBadgeCandidateRecord(
                    "BAD-01975",
                    "BloodyBayTour6",
                    2412,
                    1,
                    "DEFS/BADGES/BADGES_TOURISM.DEF",
                    "TOURISM",
                    "P_HERO",
                    "Burger{Hero.gender=male meister|meisterin}",
                    "P_VILLAIN",
                    "Burger{Hero.gender=male meister|meisterin}",
                    null,
                    null,
                    null,
                    null,
                    "badge_tourist_01",
                    null,
                    nameof(HomecomingBadgeMatchStatus.NewFromHomecoming),
                    [])
            };

            var artifacts = HomecomingBadgePromotionSupport.Build(candidates, research);
            var location = Assert.Single(artifacts.BadgeLocations);
            Assert.Equal("BAD-01975", location.BadgeCatalogItemId);
            Assert.Equal("zone-bloody-bay", location.ZoneId);
            Assert.Equal(1948, location.CoordinateX);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateFixtureRoot(string explorationMarkdown, string crosswalkMarkdown)
    {
        var root = Path.Combine(Path.GetTempPath(), $"coh-badge-fixture-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "01 - Exploration Badge Inventory.md"), explorationMarkdown, Encoding.UTF8);
        File.WriteAllText(Path.Combine(root, "02 - History Plaque Inventory.md"), "## Academic\n", Encoding.UTF8);
        File.WriteAllText(Path.Combine(root, "03 - Zone Index.md"), "## Atlas Park\n- Badge count: 1\n", Encoding.UTF8);
        var masterDir = Path.Combine(root, "Badge and Accolade Master Inventory");
        Directory.CreateDirectory(masterDir);
        File.WriteAllText(Path.Combine(masterDir, "07 - Master Badge Catalog.md"), "### Accolades\n| Reference ID | SetTitle | Internal name | Display titles | Requirement | Required by accolade(s) | Verification | Sources |\n|---|---:|---|---|---|---|---|---|\n| `badge.settitle.1517` | 1517 | `AtlasParkExplorer` | Hero: Atlas Tour Guide | req | — | Verified | source |\n", Encoding.UTF8);
        File.WriteAllText(Path.Combine(masterDir, "08 - Accolade Requirements.md"), "## Accolade index\n| SetTitle | Accolade title(s) | Prerequisite links | Reward/power | Verification |\n|---:|---|---:|---|---|\n| 1517 | Atlas Tour Guide | 0 | none | Verified |\n\n## Detailed requirements\n", Encoding.UTF8);
        File.WriteAllText(Path.Combine(masterDir, "09 - Badge-Accolade Dependency Crosswalk.md"), "## Badge-to-Accolade Dependency Crosswalk\n| SetTitle | Badge/title | Category | Accolade(s) using it |\n|---:|---|---|---|\n", Encoding.UTF8);
        File.WriteAllText(Path.Combine(masterDir, "10 - Title and Internal Name Crosswalk.md"), crosswalkMarkdown, Encoding.UTF8);
        return root;
    }
}
