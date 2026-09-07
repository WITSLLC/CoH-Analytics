using System.Security.Cryptography;
using CoHAnalytics.ReferenceDataGenerator;

namespace CoHAnalytics.Tests.ReferenceDataGenerator;

public sealed class HomecomingStaticDataSourceDiscoveryTests
{
    [Fact]
    public void Discover_ValidInstall_ReturnsExpectedSource()
    {
        using var fixture = HomecomingInstallFixture.Create();

        var source = HomecomingStaticDataSourceDiscovery.Discover(fixture.Root);

        Assert.Equal(Path.GetFullPath(fixture.Root), source.InstallRoot);
        Assert.Equal(fixture.BinPiggPath, source.BinPiggPath);
        Assert.Equal(fixture.BinPowersPiggPath, source.BinPowersPiggPath);
        Assert.Equal(HomecomingInstallFixture.BuildVersion, source.BuildVersion);
        Assert.Equal(HomecomingInstallFixture.PackageRevision, source.PackageRevision);
    }

    [Fact]
    public void Discover_ReadsBuildAndPackageVersionWithoutSemanticRewriting()
    {
        using var fixture = HomecomingInstallFixture.Create(
            buildVersion: "Issue 99, Page 4 - custom-build-label",
            packageRevision: "package.revision,7");

        var source = HomecomingStaticDataSourceDiscovery.Discover(fixture.Root);

        Assert.Equal("Issue 99, Page 4 - custom-build-label", source.BuildVersion);
        Assert.Equal("package.revision,7", source.PackageRevision);
    }

    [Fact]
    public void Discover_MissingInstallRoot_FailsClearly()
    {
        var missingRoot = Path.Combine(Path.GetTempPath(), $"coh-missing-{Guid.NewGuid():N}");

        var exception = Assert.Throws<HomecomingStaticDataSourceException>(() =>
            HomecomingStaticDataSourceDiscovery.Discover(missingRoot));

        Assert.Contains("install root does not exist", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(Path.GetFullPath(missingRoot), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Discover_MissingBinPigg_FailsClearly()
    {
        using var fixture = HomecomingInstallFixture.Create();
        File.Delete(fixture.BinPiggPath);

        var exception = Assert.Throws<HomecomingStaticDataSourceException>(() =>
            HomecomingStaticDataSourceDiscovery.Discover(fixture.Root));

        Assert.Contains("live bin.pigg", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(fixture.BinPiggPath, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Discover_MissingBinPowersPigg_FailsClearly()
    {
        using var fixture = HomecomingInstallFixture.Create();
        File.Delete(fixture.BinPowersPiggPath);

        var exception = Assert.Throws<HomecomingStaticDataSourceException>(() =>
            HomecomingStaticDataSourceDiscovery.Discover(fixture.Root));

        Assert.Contains("live bin_powers.pigg", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(fixture.BinPowersPiggPath, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Discover_MissingPackageMetadata_FailsClearly()
    {
        using var fixture = HomecomingInstallFixture.Create();
        File.Delete(fixture.LivePackageMetadataPath);

        var exception = Assert.Throws<HomecomingStaticDataSourceException>(() =>
            HomecomingStaticDataSourceDiscovery.Discover(fixture.Root));

        Assert.Contains("live package metadata", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(fixture.LivePackageMetadataPath, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Discover_MalformedPackageMetadata_IdentifiesFileAndProblem()
    {
        using var fixture = HomecomingInstallFixture.Create();
        File.WriteAllText(
            fixture.LiveDataPackageMetadataPath,
            """
            -----BEGIN CONTENT-----
            { not valid JSON }
            -----END CONTENT-----
            """);

        var exception = Assert.Throws<HomecomingStaticDataSourceException>(() =>
            HomecomingStaticDataSourceDiscovery.Discover(fixture.Root));

        Assert.Contains(fixture.LiveDataPackageMetadataPath, exception.Message, StringComparison.Ordinal);
        Assert.Contains("contains invalid JSON", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Discover_PerformsNoWritesInsideInstallRoot()
    {
        using var fixture = HomecomingInstallFixture.Create();
        var before = SnapshotFiles(fixture.Root);

        _ = HomecomingStaticDataSourceDiscovery.Discover(fixture.Root);

        var after = SnapshotFiles(fixture.Root);
        Assert.Equal(before, after);
    }

    [Fact]
    public void Discover_IdenticalInputs_ReturnsDeterministicResult()
    {
        using var fixture = HomecomingInstallFixture.Create();

        var first = HomecomingStaticDataSourceDiscovery.Discover(fixture.Root);
        var second = HomecomingStaticDataSourceDiscovery.Discover(fixture.Root);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Command_ValidationFailure_ReturnsNonZeroWithoutCreatingOutput()
    {
        var missingRoot = Path.Combine(Path.GetTempPath(), $"coh-missing-{Guid.NewGuid():N}");
        var candidatePath = Path.Combine(Path.GetTempPath(), $"coh-candidate-{Guid.NewGuid():N}.json");
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = HomecomingImportCommand.Run(
            ["--install", missingRoot, "--output", candidatePath],
            output,
            error);

        Assert.NotEqual(0, exitCode);
        Assert.Contains("Importer source validation: FAIL", error.ToString(), StringComparison.Ordinal);
        Assert.False(File.Exists(candidatePath));
    }

    [Fact]
    public void Command_MissingSalvageMember_ReturnsNonZeroWithoutCreatingOutput()
    {
        using var fixture = HomecomingInstallFixture.Create();
        var messages = HomecomingBinaryFixtureBuilder.CreateMessageStore(
            ("P1671735844", "Absolute Amazement"));
        File.WriteAllBytes(
            fixture.BinPiggPath,
            HomecomingBinaryFixtureBuilder.CreatePigg(
                (HomecomingBinaryFixtureBuilder.MessageMemberName, messages)));
        var candidatePath = Path.Combine(Path.GetTempPath(), $"coh-candidate-{Guid.NewGuid():N}.json");
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = HomecomingImportCommand.Run(
            ["--install", fixture.Root, "--output", candidatePath],
            output,
            error);

        Assert.NotEqual(0, exitCode);
        Assert.Contains("bin/salvage.bin", error.ToString(), StringComparison.Ordinal);
        Assert.Contains("not found", error.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(candidatePath));
    }

    [Fact]
    public void Command_MissingPowersMember_ReturnsNonZeroWithoutCreatingOutput()
    {
        using var fixture = HomecomingInstallFixture.Create();
        File.WriteAllBytes(
            fixture.BinPowersPiggPath,
            HomecomingBinaryFixtureBuilder.CreatePigg(("bin/other.bin", new byte[] { 1 })));
        var candidatePath = Path.Combine(Path.GetTempPath(), $"coh-candidate-{Guid.NewGuid():N}.json");
        var enhancementCandidatePath = HomecomingEnhancementCandidateWriter.GetOutputPath(candidatePath);
        var inspirationCandidatePath = HomecomingInspirationCandidateWriter.GetOutputPath(candidatePath);
        var recipeCandidatePath = HomecomingRecipeCandidateWriter.GetOutputPath(candidatePath);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = HomecomingImportCommand.Run(
            ["--install", fixture.Root, "--output", candidatePath],
            output,
            error);

        Assert.NotEqual(0, exitCode);
        Assert.Contains("bin/powers.bin", error.ToString(), StringComparison.Ordinal);
        Assert.Contains("not found", error.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(candidatePath));
        Assert.False(File.Exists(enhancementCandidatePath));
        Assert.False(File.Exists(inspirationCandidatePath));
        Assert.False(File.Exists(recipeCandidatePath));
    }

    [Fact]
    public void Command_MissingBoostSetsMember_ReturnsNonZeroWithoutCreatingOutput()
    {
        using var fixture = HomecomingInstallFixture.Create();
        var messages = HomecomingBinaryFixtureBuilder.CreateMessageStore(
            ("P1671735844", "Absolute Amazement"),
            ("P_SET_FIXTURE", "Fixture Set"),
            ("P_BOOST_FIXTURE", "Fixture Set: Accuracy"));
        var salvage = HomecomingBinaryFixtureBuilder.CreateSalvage(
            new SyntheticSalvageRecord(
                "S_Fixture",
                "P1671735844",
                "salvage_Fixture.tga"));
        File.WriteAllBytes(
            fixture.BinPiggPath,
            HomecomingBinaryFixtureBuilder.CreatePigg(
                (HomecomingBinaryFixtureBuilder.MessageMemberName, messages),
                (HomecomingBinaryFixtureBuilder.SalvageMemberName, salvage)));
        var candidatePath = Path.Combine(Path.GetTempPath(), $"coh-candidate-{Guid.NewGuid():N}.json");
        var enhancementCandidatePath = HomecomingEnhancementCandidateWriter.GetOutputPath(candidatePath);
        var inspirationCandidatePath = HomecomingInspirationCandidateWriter.GetOutputPath(candidatePath);
        var recipeCandidatePath = HomecomingRecipeCandidateWriter.GetOutputPath(candidatePath);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = HomecomingImportCommand.Run(
            ["--install", fixture.Root, "--output", candidatePath],
            output,
            error);

        Assert.NotEqual(0, exitCode);
        Assert.Contains("bin/boostsets.bin", error.ToString(), StringComparison.Ordinal);
        Assert.Contains("not found", error.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(candidatePath));
        Assert.False(File.Exists(enhancementCandidatePath));
        Assert.False(File.Exists(inspirationCandidatePath));
        Assert.False(File.Exists(recipeCandidatePath));
    }

    [Fact]
    public void Command_MissingBaseRecipesMember_ReturnsNonZeroWithoutCreatingAnyOutput()
    {
        using var fixture = HomecomingInstallFixture.Create();
        var archiveWithoutRecipes = HomecomingPiggMemberReader.ReadMember(
            fixture.BinPiggPath,
            HomecomingBinaryFixtureBuilder.MessageMemberName);
        var salvage = HomecomingBinaryFixtureBuilder.CreateSalvage(
            new SyntheticSalvageRecord("S_Fixture", "P1671735844", "salvage_Fixture.tga"));
        var boostSourceId = "Boosts.Crafted_Fixture_A.Crafted_Fixture_A";
        var boostSets = HomecomingBinaryFixtureBuilder.CreateBoostSets(
            new SyntheticBoostSetRecord(
                "Fixture_Set", "P_SET_FIXTURE", "ECUncommon", "ECMelee", 10, 50,
                [[boostSourceId]]));
        File.WriteAllBytes(
            fixture.BinPiggPath,
            HomecomingBinaryFixtureBuilder.CreatePigg(
                (HomecomingBinaryFixtureBuilder.MessageMemberName, archiveWithoutRecipes),
                (HomecomingBinaryFixtureBuilder.SalvageMemberName, salvage),
                (HomecomingBinaryFixtureBuilder.BoostSetsMemberName, boostSets)));
        var candidatePath = Path.Combine(Path.GetTempPath(), $"coh-candidate-{Guid.NewGuid():N}.json");
        var enhancementCandidatePath = HomecomingEnhancementCandidateWriter.GetOutputPath(candidatePath);
        var inspirationCandidatePath = HomecomingInspirationCandidateWriter.GetOutputPath(candidatePath);
        var recipeCandidatePath = HomecomingRecipeCandidateWriter.GetOutputPath(candidatePath);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = HomecomingImportCommand.Run(
            ["--install", fixture.Root, "--output", candidatePath],
            output,
            error);

        Assert.NotEqual(0, exitCode);
        Assert.Contains("bin/baserecipes.bin", error.ToString(), StringComparison.Ordinal);
        Assert.Contains("not found", error.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(candidatePath));
        Assert.False(File.Exists(enhancementCandidatePath));
        Assert.False(File.Exists(inspirationCandidatePath));
        Assert.False(File.Exists(recipeCandidatePath));
    }

    [Fact]
    public void Command_MissingBadgesMember_ReturnsNonZeroWithoutCreatingAnyOutput()
    {
        using var fixture = HomecomingInstallFixture.Create();
        var messages = HomecomingPiggMemberReader.ReadMember(
            fixture.BinPiggPath,
            HomecomingBinaryFixtureBuilder.MessageMemberName);
        var salvage = HomecomingPiggMemberReader.ReadMember(
            fixture.BinPiggPath,
            HomecomingBinaryFixtureBuilder.SalvageMemberName);
        var boostSets = HomecomingPiggMemberReader.ReadMember(
            fixture.BinPiggPath,
            HomecomingBinaryFixtureBuilder.BoostSetsMemberName);
        var baseRecipes = HomecomingPiggMemberReader.ReadMember(
            fixture.BinPiggPath,
            HomecomingBinaryFixtureBuilder.BaseRecipesMemberName);
        File.WriteAllBytes(
            fixture.BinPiggPath,
            HomecomingBinaryFixtureBuilder.CreatePigg(
                (HomecomingBinaryFixtureBuilder.MessageMemberName, messages),
                (HomecomingBinaryFixtureBuilder.SalvageMemberName, salvage),
                (HomecomingBinaryFixtureBuilder.BoostSetsMemberName, boostSets),
                (HomecomingBinaryFixtureBuilder.BaseRecipesMemberName, baseRecipes)));
        var candidatePath = Path.Combine(Path.GetTempPath(), $"coh-candidate-{Guid.NewGuid():N}.json");
        var enhancementCandidatePath = HomecomingEnhancementCandidateWriter.GetOutputPath(candidatePath);
        var inspirationCandidatePath = HomecomingInspirationCandidateWriter.GetOutputPath(candidatePath);
        var recipeCandidatePath = HomecomingRecipeCandidateWriter.GetOutputPath(candidatePath);
        var badgeCandidatePath = HomecomingBadgeCandidateWriter.GetOutputPath(candidatePath);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = HomecomingImportCommand.Run(
            ["--install", fixture.Root, "--output", candidatePath],
            output,
            error);

        Assert.NotEqual(0, exitCode);
        Assert.Contains("bin/badges.bin", error.ToString(), StringComparison.Ordinal);
        Assert.Contains("not found", error.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(candidatePath));
        Assert.False(File.Exists(enhancementCandidatePath));
        Assert.False(File.Exists(inspirationCandidatePath));
        Assert.False(File.Exists(recipeCandidatePath));
        Assert.False(File.Exists(badgeCandidatePath));
    }

    [Fact]
    public void Command_ValidSource_WritesSalvageCandidateAndReportsSummary()
    {
        using var fixture = HomecomingInstallFixture.Create();
        var candidatePath = Path.Combine(Path.GetTempPath(), $"coh-candidate-{Guid.NewGuid():N}.json");
        var enhancementCandidatePath = HomecomingEnhancementCandidateWriter.GetOutputPath(candidatePath);
        var inspirationCandidatePath = HomecomingInspirationCandidateWriter.GetOutputPath(candidatePath);
        var recipeCandidatePath = HomecomingRecipeCandidateWriter.GetOutputPath(candidatePath);
        var badgeCandidatePath = HomecomingBadgeCandidateWriter.GetOutputPath(candidatePath);
        using var output = new StringWriter();
        using var error = new StringWriter();

        try
        {
            var exitCode = HomecomingImportCommand.Run(
                ["--install", fixture.Root, "--output", candidatePath],
                output,
                error);

            Assert.Equal(0, exitCode);
            Assert.Empty(error.ToString());
            Assert.Contains($"Build: {HomecomingInstallFixture.BuildVersion}", output.ToString(), StringComparison.Ordinal);
            Assert.Contains("bin.pigg: found", output.ToString(), StringComparison.Ordinal);
            Assert.Contains("bin_powers.pigg: found", output.ToString(), StringComparison.Ordinal);
            Assert.Contains("Message count: 13", output.ToString(), StringComparison.Ordinal);
            Assert.Contains("Message-store validation: PASS", output.ToString(), StringComparison.Ordinal);
            Assert.Contains("Salvage records: 1", output.ToString(), StringComparison.Ordinal);
            Assert.Contains("Salvage candidate generation: PASS", output.ToString(), StringComparison.Ordinal);
            Assert.Contains("Concrete Boost records: 1", output.ToString(), StringComparison.Ordinal);
            Assert.Contains("Concrete Boost names resolved: 1", output.ToString(), StringComparison.Ordinal);
            Assert.Contains("Logical Enhancements: 1", output.ToString(), StringComparison.Ordinal);
            Assert.Contains("Enhancement Sets: 1", output.ToString(), StringComparison.Ordinal);
            Assert.Contains("Enhancement Set rarity codes: ECUncommon=1", output.ToString(), StringComparison.Ordinal);
            Assert.Contains("Enhancement Set category codes: ECMelee=1", output.ToString(), StringComparison.Ordinal);
            Assert.Contains("Enhancement candidate generation: PASS", output.ToString(), StringComparison.Ordinal);
            Assert.Contains("Homecoming Inspirations: 1", output.ToString(), StringComparison.Ordinal);
            Assert.Contains("Inspiration names resolved: 1", output.ToString(), StringComparison.Ordinal);
            Assert.Contains("Inspiration candidate generation: PASS", output.ToString(), StringComparison.Ordinal);
            Assert.Contains("Base Recipe records: 1", output.ToString(), StringComparison.Ordinal);
            Assert.Contains("Crafting Recipes: 1", output.ToString(), StringComparison.Ordinal);
            Assert.Contains("Recipe product joins: 1", output.ToString(), StringComparison.Ordinal);
            Assert.Contains("Recipe candidate generation: PASS", output.ToString(), StringComparison.Ordinal);
            Assert.Contains("Live Badge records: 1", output.ToString(), StringComparison.Ordinal);
            Assert.Contains("Player-facing Badges: 1", output.ToString(), StringComparison.Ordinal);
            Assert.Contains("Badge category distribution: TOURISM=1", output.ToString(), StringComparison.Ordinal);
            Assert.Contains("Badge candidate generation: PASS", output.ToString(), StringComparison.Ordinal);
            Assert.Contains("Importer source validation: PASS", output.ToString(), StringComparison.Ordinal);
            Assert.True(File.Exists(candidatePath));
            Assert.True(File.Exists(enhancementCandidatePath));
            Assert.True(File.Exists(inspirationCandidatePath));
            Assert.True(File.Exists(recipeCandidatePath));
            Assert.True(File.Exists(badgeCandidatePath));
            Assert.Contains("S_Fixture", File.ReadAllText(candidatePath), StringComparison.Ordinal);
            Assert.Contains("Boosts.Crafted_Fixture_A.Crafted_Fixture_A", File.ReadAllText(enhancementCandidatePath), StringComparison.Ordinal);
            var inspirationCandidateText = File.ReadAllText(inspirationCandidatePath);
            Assert.Contains("Inspirations.Fixture.Fixture", inspirationCandidateText, StringComparison.Ordinal);
            Assert.Contains("Fixture inspiration help", inspirationCandidateText, StringComparison.Ordinal);
            Assert.Contains("Inspiration_Fixture.tga", inspirationCandidateText, StringComparison.Ordinal);
            Assert.Contains("Recipe_Invention_Fixture_10", File.ReadAllText(recipeCandidatePath), StringComparison.Ordinal);
            Assert.Contains("Badge_Fixture", File.ReadAllText(badgeCandidatePath), StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(candidatePath))
            {
                File.Delete(candidatePath);
            }

            if (File.Exists(enhancementCandidatePath))
            {
                File.Delete(enhancementCandidatePath);
            }

            if (File.Exists(inspirationCandidatePath))
            {
                File.Delete(inspirationCandidatePath);
            }

            if (File.Exists(recipeCandidatePath))
            {
                File.Delete(recipeCandidatePath);
            }

            if (File.Exists(badgeCandidatePath))
            {
                File.Delete(badgeCandidatePath);
            }
        }
    }

    private static IReadOnlyList<string> SnapshotFiles(string root) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .Select(path =>
            {
                var info = new FileInfo(path);
                var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
                return $"{Path.GetRelativePath(root, path)}|{info.Length}|{info.LastWriteTimeUtc:O}|{hash}";
            })
            .ToArray();

    private sealed class HomecomingInstallFixture : IDisposable
    {
        public const string BuildVersion = "Issue 28, Page 3 - 28.3.7927";
        public const string PackageRevision = "1.20260707.124731.7927";

        private HomecomingInstallFixture(string root)
        {
            Root = root;
            BinPiggPath = Path.GetFullPath(Path.Combine(root, "assets", "live", "bin.pigg"));
            BinPowersPiggPath = Path.GetFullPath(Path.Combine(root, "assets", "live", "bin_powers.pigg"));
            LivePackageMetadataPath = Path.GetFullPath(Path.Combine(
                root,
                "settings",
                "launcher",
                "packages",
                "hc_live.json"));
            LiveDataPackageMetadataPath = Path.GetFullPath(Path.Combine(
                root,
                "settings",
                "launcher",
                "packages",
                "hc_data_live.json"));
        }

        public string Root { get; }

        public string BinPiggPath { get; }

        public string BinPowersPiggPath { get; }

        public string LivePackageMetadataPath { get; }

        public string LiveDataPackageMetadataPath { get; }

        public static HomecomingInstallFixture Create(
            string buildVersion = BuildVersion,
            string packageRevision = PackageRevision)
        {
            var root = Path.Combine(Path.GetTempPath(), $"coh-homecoming-source-{Guid.NewGuid():N}");
            var fixture = new HomecomingInstallFixture(root);
            Directory.CreateDirectory(Path.GetDirectoryName(fixture.BinPiggPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(fixture.LivePackageMetadataPath)!);
            var messages = HomecomingBinaryFixtureBuilder.CreateMessageStore(
                ("P1671735844", "Absolute Amazement"),
                ("P_SET_FIXTURE", "Fixture Set"),
                ("P_BOOST_FIXTURE", "Fixture Set: Accuracy"),
                ("P_INSPIRATION_FIXTURE", "Fixture Inspiration"),
                ("P_INSPIRATION_FIXTURE_HELP", "Fixture inspiration help"),
                ("P_INSPIRATION_FIXTURE_SHORT", "Fixture inspiration short help"),
                ("P_RECIPE_FIXTURE", "Fixture Enhancement Recipe"),
                ("P_BADGE_HERO_NAME", "Fixture Hero Badge"),
                ("P_BADGE_VILLAIN_NAME", "Fixture Villain Badge"),
                ("P_BADGE_HERO_DESCRIPTION", "Fixture hero description"),
                ("P_BADGE_VILLAIN_DESCRIPTION", "Fixture villain description"),
                ("ECUncommon", "Rarity: Uncommon"),
                ("ECMelee", "Category: Melee"));
            var salvage = HomecomingBinaryFixtureBuilder.CreateSalvage(
                new SyntheticSalvageRecord(
                    "S_Fixture",
                    "P1671735844",
                    "salvage_Fixture.tga"));
            var boostSourceId = "Boosts.Crafted_Fixture_A.Crafted_Fixture_A";
            var boostSets = HomecomingBinaryFixtureBuilder.CreateBoostSets(
                new SyntheticBoostSetRecord(
                    "Fixture_Set",
                    "P_SET_FIXTURE",
                    "ECUncommon",
                    "ECMelee",
                    10,
                    50,
                    [[boostSourceId]]));
            var powers = HomecomingBinaryFixtureBuilder.CreatePowersDiscovery(
                new SyntheticInspirationDiscoveryRecord(boostSourceId, "P_BOOST_FIXTURE"),
                new SyntheticInspirationDiscoveryRecord(
                    "Inspirations.Fixture.Fixture",
                    "P_INSPIRATION_FIXTURE",
                    "P_INSPIRATION_FIXTURE_HELP",
                    "P_INSPIRATION_FIXTURE_SHORT",
                    "Inspiration_Fixture.tga"));
            var baseRecipes = HomecomingBinaryFixtureBuilder.CreateBaseRecipes(
                new SyntheticBaseRecipeRecord(
                    "Recipe_Invention_Fixture_10",
                    "P_RECIPE_FIXTURE",
                    boostSourceId));
            var badges = HomecomingBinaryFixtureBuilder.CreateBadges(
                new SyntheticBadgeRecord(
                    "Badge_Fixture",
                    heroNameMessageKey: "P_BADGE_HERO_NAME",
                    villainNameMessageKey: "P_BADGE_VILLAIN_NAME",
                    heroDescriptionMessageKey: "P_BADGE_HERO_DESCRIPTION",
                    villainDescriptionMessageKey: "P_BADGE_VILLAIN_DESCRIPTION"));
            File.WriteAllBytes(
                fixture.BinPiggPath,
                HomecomingBinaryFixtureBuilder.CreatePigg(
                    (HomecomingBinaryFixtureBuilder.MessageMemberName, messages),
                    (HomecomingBinaryFixtureBuilder.SalvageMemberName, salvage),
                    (HomecomingBinaryFixtureBuilder.BoostSetsMemberName, boostSets),
                    (HomecomingBinaryFixtureBuilder.BaseRecipesMemberName, baseRecipes),
                    (HomecomingBinaryFixtureBuilder.BadgesMemberName, badges)));
            File.WriteAllBytes(
                fixture.BinPowersPiggPath,
                HomecomingBinaryFixtureBuilder.CreatePigg(
                    (HomecomingBinaryFixtureBuilder.PowersMemberName, powers)));
            File.WriteAllText(
                fixture.LivePackageMetadataPath,
                CreateSignedMetadata($$"""
                    {
                      "displayver": "{{buildVersion}}"
                    }
                    """));
            File.WriteAllText(
                fixture.LiveDataPackageMetadataPath,
                CreateSignedMetadata($$"""
                    {
                      "version": "{{packageRevision}}"
                    }
                    """));
            return fixture;
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }

        private static string CreateSignedMetadata(string json) =>
            $$"""
            -----BEGIN CERTIFICATE-----
            fixture
            -----END CERTIFICATE-----
            -----BEGIN SIGNATURE-----
            fixture
            -----END SIGNATURE-----
            -----BEGIN CONTENT-----
            {{json}}
            -----END CONTENT-----
            """;
    }
}
