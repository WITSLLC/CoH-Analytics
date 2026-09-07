using System.IO;
using CoHAnalytics.Homecoming;
using CoHAnalytics.Models;
using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Services;

public sealed class HomecomingBuildSaveMetadataParserTests
{
    [Fact]
    public void Mastermind_build_with_robotics_powers_resolves_robotics()
    {
        const string build = """
            Scout Primus: Level 29 Technology Class_Mastermind
            Level 1: Mastermind_Summon Robotics Battle_Drones
            Level 6: Mastermind_Summon Robotics Equip_Robot
            Level 8: Mastermind_Summon Robotics Photon_Grenade
            """;

        Assert.True(HomecomingBuildSaveMetadataParser.TryParse(build, out var metadata));
        Assert.Equal("Robotics", metadata.PrimaryPowerSet);
        Assert.Equal("Mastermind", metadata.Archetype);
    }

    [Fact]
    public void Mastermind_build_with_kinetics_powers_resolves_kinetics()
    {
        const string build = """
            Scout Primus: Level 29 Technology Class_Mastermind
            Level 1: Mastermind_Summon Robotics Battle_Drones
            Level 1: Mastermind_Buff Kinetics Transfusion
            Level 10: Mastermind_Buff Kinetics Siphon_Speed
            Level 20: Mastermind_Buff Kinetics Siphon_Power
            """;

        Assert.True(HomecomingBuildSaveMetadataParser.TryParse(build, out var metadata));
        Assert.Equal("Robotics", metadata.PrimaryPowerSet);
        Assert.Equal("Kinetics", metadata.SecondaryPowerSet);
    }

    [Fact]
    public void Generic_category_prefixes_are_not_surfaced_as_powerset_names()
    {
        const string build = """
            Hero: Level 50 Magic Class_mastermind
            Level 1: mastermind_summon Battle_Drones
            Level 2: mastermind_buff Transfusion
            """;

        Assert.True(HomecomingBuildSaveMetadataParser.TryParse(build, out var metadata));
        Assert.Null(metadata.PrimaryPowerSet);
        Assert.Null(metadata.SecondaryPowerSet);
        Assert.NotEqual("Summon", metadata.PrimaryPowerSet);
        Assert.NotEqual("Buff", metadata.SecondaryPowerSet);
    }

    [Fact]
    public void Pool_and_epic_powers_do_not_affect_primary_secondary_resolution()
    {
        const string build = """
            Scout Primus: Level 29 Technology Class_Mastermind
            Level 1: Mastermind_Summon Robotics Battle_Drones
            Level 1: Mastermind_Buff Kinetics Transfusion
            Level 4: Pool Leaping Long_Jump
            Level 35: Epic Fire_Mastery Fire_Ball
            """;

        Assert.True(HomecomingBuildSaveMetadataParser.TryParse(build, out var metadata));
        Assert.Equal("Robotics", metadata.PrimaryPowerSet);
        Assert.Equal("Kinetics", metadata.SecondaryPowerSet);
    }

    [Fact]
    public void Controller_example_resolves_actual_sets_not_control_buff_controller()
    {
        const string build = """
            Dawn's Vanguard: Level 36 Magic Class_Controller
            Level 1: Controller_Control Fire_Control Soot
            Level 6: Controller_Control Fire_Control Fire_Cages
            Level 1: Controller_Buff Kinetics Transfusion
            Level 10: Controller_Buff Kinetics Siphon_Speed
            """;

        Assert.True(HomecomingBuildSaveMetadataParser.TryParse(build, out var metadata));
        Assert.Equal("Controller", metadata.Archetype);
        Assert.Equal("Fire Control", metadata.PrimaryPowerSet);
        Assert.Equal("Kinetics", metadata.SecondaryPowerSet);
    }

    [Fact]
    public void Live_scout_primus_buildsave_resolves_canonical_powersets_when_catalog_loaded()
    {
        const string installRoot = @"C:\Games\Homecoming";
        if (!Directory.Exists(installRoot))
        {
            return;
        }

        var settings = new SettingsService(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        var installationService = new HomecomingInstallationService(settings);
        SetCurrentInstallation(
            installationService,
            new HomecomingInstallation
            {
                InstallRoot = installRoot,
                LauncherPath = Path.Combine(installRoot, "Launch.exe")
            });

        var catalog = new HomecomingArchetypePowerReferenceCatalog(installationService);
        var buildPath = @"C:\Games\Homecoming\accounts\TestAccount\Builds\BUILD0001.txt";
        if (!File.Exists(buildPath))
        {
            return;
        }

        var content = File.ReadAllText(buildPath);
        Assert.True(HomecomingBuildSaveMetadataParser.TryParse(content, catalog, out var metadata));
        Assert.Equal("Mastermind", metadata.Archetype);
        Assert.Equal("Robotics", metadata.PrimaryPowerSet);
        Assert.Equal("Kinetics", metadata.SecondaryPowerSet);
    }

    [Fact]
    public void Badges_earned_section_parses_homecoming_source_ids()
    {
        const string build = """
            Example Brute: Level 38 Magic Class_Brute
            Level 1: Brute_Melee Fiery_Melee Scorch
            ------------------
            Badges Earned:
            ------------------
            AtlasParkTour3
            Patriot
            Collector
            UnknownBadgeId
            """;

        Assert.True(HomecomingBuildSaveMetadataParser.TryParseBadgeSourceIds(build, out var sourceIds));
        Assert.Equal(
            ["AtlasParkTour3", "Patriot", "Collector", "UnknownBadgeId"],
            sourceIds);
    }

    [Fact]
    public void Badges_earned_section_stops_at_next_header()
    {
        const string build = """
            Badges Earned:
            ------------------
            Rookie
            Tourist
            Other Section:
            NotABadge
            """;

        Assert.True(HomecomingBuildSaveMetadataParser.TryParseBadgeSourceIds(build, out var sourceIds));
        Assert.Equal(["Rookie", "Tourist"], sourceIds);
    }

    [Fact]
    public void Missing_badges_earned_section_returns_false()
    {
        const string build = """
            Example Brute: Level 38 Magic Class_Brute
            Level 1: Brute_Melee Fiery_Melee Scorch
            """;

        Assert.False(HomecomingBuildSaveMetadataParser.TryParseBadgeSourceIds(build, out var sourceIds));
        Assert.Empty(sourceIds);
    }

    [Fact]
    public void Empty_badges_earned_section_returns_true_with_no_ids()
    {
        const string build = """
            Badges Earned:
            ------------------
            """;

        Assert.True(HomecomingBuildSaveMetadataParser.TryParseBadgeSourceIds(build, out var sourceIds));
        Assert.Empty(sourceIds);
    }

    private static void SetCurrentInstallation(
        HomecomingInstallationService service,
        HomecomingInstallation installation) =>
        typeof(HomecomingInstallationService)
            .GetProperty(nameof(HomecomingInstallationService.CurrentInstallation))!
            .SetValue(service, installation);
}
