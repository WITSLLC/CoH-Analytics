using CoHAnalytics.Homecoming;
using CoHAnalytics.Models;
using CoHAnalytics.ReferenceData;
using CoHAnalytics.Services;
using CoHAnalytics.Tests.Homecoming;
using CoHAnalytics.ViewModels.Workspaces;

namespace CoHAnalytics.Tests.Workspaces;

public sealed class AccountsBuildPresentationLiveTests
{
    private const string BlueDevilBuildPath =
        @"C:\Games\Homecoming\accounts\RivenForest\Builds\DAS4MNTU.txt";

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
