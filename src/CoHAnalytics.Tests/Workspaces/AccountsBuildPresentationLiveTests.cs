using CoHAnalytics.Homecoming;
using CoHAnalytics.Models;
using CoHAnalytics.ReferenceData;
using CoHAnalytics.Services;
using CoHAnalytics.Tests.Homecoming;
using CoHAnalytics.ViewModels.Workspaces;

namespace CoHAnalytics.Tests.Workspaces;

public sealed class AccountsBuildPresentationLiveTests
{
    private const string RivenForestBuildsPath =
        @"C:\Games\Homecoming\accounts\RivenForest\Builds";
    private const string BlueDevilBuildPath =
        @"C:\Games\Homecoming\accounts\RivenForest\Builds\DAS4MNTU.txt";

    [Theory]
    [Trait("Category", "LiveInstall")]
    [InlineData("K98KQUND.txt", "Hell's Vengence", 50, "Controller", "Fire Control", "Kinetics")]
    [InlineData("DAS4MNTU.txt", "BIue Devil", 38, "Brute", "Fiery Melee", "Fiery Aura")]
    [InlineData("HKK7FU3K.txt", "Gerald Tarrent", 29, "Mastermind", "Robotics", "Kinetics")]
    public void LiveInstall_RivenForest_build_headers_are_authoritative_character_evidence(
        string fileName,
        string expectedName,
        int expectedLevel,
        string expectedArchetype,
        string expectedPrimary,
        string expectedSecondary)
    {
        var path = Path.Combine(RivenForestBuildsPath, fileName);
        Assert.True(File.Exists(path), $"Live build fixture was not found: {path}");
        var content = File.ReadAllText(path);

        Assert.True(HomecomingBuildLayoutParser.TryParse(content, out var layout));
        Assert.True(HomecomingBuildSaveMetadataParser.TryParse(content, out var metadata));

        Assert.Equal(expectedName, layout.CharacterName);
        Assert.Equal(expectedLevel, layout.CharacterLevel);
        Assert.Equal(expectedName, metadata.CharacterName);
        Assert.Equal(expectedArchetype, metadata.Archetype);
        Assert.Equal(expectedPrimary, metadata.PrimaryPowerSet);
        Assert.Equal(expectedSecondary, metadata.SecondaryPowerSet);
    }

    [Theory]
    [Trait("Category", "LiveInstall")]
    [InlineData("K98KQUND.txt", "0a312144d39d41d69c4633fbbebdad84", "Hell's Vengence")]
    [InlineData("DAS4MNTU.txt", "b41a755219b04c8bbfcb6ac6dcd75246", "BIue Devil")]
    [InlineData("HKK7FU3K.txt", "8dd98df77dba41adb269025711132e77", "Gerald Tarrent")]
    public void LiveInstall_RivenForest_build_snapshots_round_trip_without_modifying_source(
        string fileName,
        string recordIdText,
        string expectedName)
    {
        var sourcePath = Path.Combine(RivenForestBuildsPath, fileName);
        Assert.True(File.Exists(sourcePath), $"Live build fixture was not found: {sourcePath}");
        var sourceBytes = File.ReadAllBytes(sourcePath);
        var sourceLastWrite = File.GetLastWriteTimeUtc(sourcePath);
        Assert.True(HomecomingBuildLayoutParser.TryParse(
            File.ReadAllText(sourcePath),
            out var layout));
        var dataDirectory = Path.Combine(
            Path.GetTempPath(),
            "coh-analytics-live-build-snapshot",
            Guid.NewGuid().ToString("n"));
        var store = new CharacterBuildSnapshotStore(new CharacterBuildSnapshotStoreOptions
        {
            DataDirectory = dataDirectory
        });
        var recordId = CharacterRecordId.FromGuid(Guid.Parse(recordIdText));

        var save = store.Save(new CharacterBuildSnapshot
        {
            CharacterRecordId = recordId,
            CharacterShortId = Path.GetFileNameWithoutExtension(fileName),
            CharacterName = layout.CharacterName,
            SyncedAtUtc = DateTimeOffset.UtcNow,
            SourceBuildFile = fileName,
            SourceLastWriteUtc = sourceLastWrite,
            Layout = layout
        });
        var reloaded = new CharacterBuildSnapshotStore(new CharacterBuildSnapshotStoreOptions
        {
            DataDirectory = dataDirectory
        }).TryLoad(recordId);

        Assert.True(save.IsSuccess);
        Assert.True(reloaded.IsSuccess);
        Assert.Equal(expectedName, reloaded.Snapshot!.Layout.CharacterName);
        Assert.Equal(layout.Powers.Count, reloaded.Snapshot.Layout.Powers.Count);
        Assert.Equal(
            layout.Powers.Select(power => power.RawPowerToken),
            reloaded.Snapshot.Layout.Powers.Select(power => power.RawPowerToken));
        Assert.Equal(sourceBytes, File.ReadAllBytes(sourcePath));
        Assert.Equal(sourceLastWrite, File.GetLastWriteTimeUtc(sourcePath));
    }

    [Fact]
    [Trait("Category", "LiveInstall")]
    public void LiveInstall_BlueDevilBuild_resolves_three_column_canonical_presentation()
    {
        Assert.True(File.Exists(BlueDevilBuildPath), $"Live build fixture was not found: {BlueDevilBuildPath}");
        Assert.True(HomecomingBuildLayoutParser.TryParse(
            File.ReadAllText(BlueDevilBuildPath),
            out var snapshot));
        var installation = CreateInstallationService();
        var assets = new InstalledGameAssetProvider(installation);
        var itemCatalog = ItemReferenceCatalogFactory.LoadEmbeddedProduction();
        var presentation = AccountsBuildPresentationSupport.Build(
            snapshot,
            "Fiery Melee",
            "Fiery Aura",
            new HomecomingPowerReferenceCatalog(installation),
            assets,
            itemCatalog,
            new EnhancementIconCompositor(assets),
            new HomecomingBoostMetadataProvider(installation));

        Assert.Equal("Fiery Melee", presentation.PrimarySection!.DisplayName);
        Assert.Equal("Fiery Aura", presentation.SecondarySection!.DisplayName);
        Assert.Equal(
            ["Inherent", "Inherent Fitness", "Leaping", "Speed", "Fighting", "Mu Mastery", "Inherents"],
            presentation.AdditionalSections.Select(section => section.DisplayName));
        Assert.Contains(presentation.PrimarySection.Powers, power => power.DisplayName == "Breath of Fire");
        Assert.Contains(
            presentation.AdditionalSections.SelectMany(section => section.Powers),
            power => power.DisplayName == "Super Jump");
        Assert.All(
            presentation.PrimarySection.Powers
                .Concat(presentation.SecondarySection.Powers)
                .Concat(presentation.AdditionalSections.SelectMany(section => section.Powers)),
            power => Assert.NotNull(power.IconSource));

        var slots = presentation.PrimarySection.Powers
            .Concat(presentation.SecondarySection.Powers)
            .Concat(presentation.AdditionalSections.SelectMany(section => section.Powers))
            .SelectMany(power => power.EnhancementSlots)
            .ToArray();
        Assert.Contains(slots, slot => slot.IsEmpty && slot.IconSource is not null);
        Assert.Contains(slots, slot => !slot.IsEmpty && slot.IconSource is not null);
    }

    private static HomecomingInstallationService CreateInstallationService()
    {
        var settings = new SettingsService(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        var installationService = new HomecomingInstallationService(settings);
        typeof(HomecomingInstallationService)
            .GetProperty(nameof(HomecomingInstallationService.CurrentInstallation))!
            .SetValue(
                installationService,
                new HomecomingInstallation
                {
                    InstallRoot = LiveInstallTestEnvironment.InstallRoot,
                    LauncherPath = Path.Combine(
                        LiveInstallTestEnvironment.InstallRoot,
                        "Homecoming Launcher.exe")
                });
        return installationService;
    }
}
