using System.Windows.Media;
using CoHAnalytics.Homecoming;
using CoHAnalytics.Models;
using CoHAnalytics.ReferenceData;
using CoHAnalytics.Services;
using CoHAnalytics.ViewModels.Workspaces;

namespace CoHAnalytics.Tests.Workspaces;

public sealed class AccountsBuildPresentationSupportTests
{
    private static readonly IItemReferenceCatalog ItemCatalog =
        ItemReferenceCatalogFactory.LoadEmbeddedProduction();

    [Fact]
    public void Build_uses_canonical_names_and_separates_primary_secondary_and_additional_sections()
    {
        var snapshot = ParseFixture();
        var presentation = Build(snapshot);

        Assert.Equal("Fiery Melee", presentation.PrimarySection!.DisplayName);
        Assert.Equal(["Scorch", "Breath of Fire"], presentation.PrimarySection.Powers.Select(power => power.DisplayName));
        Assert.Equal("Fiery Aura", presentation.SecondarySection!.DisplayName);
        Assert.Equal(["Inherent", "Inherent Fitness", "Leaping", "Mu Mastery", "Fighting", "Inherents"],
            presentation.AdditionalSections.Select(section => section.DisplayName));
    }

    [Fact]
    public void Build_preserves_first_appearance_order_and_keeps_pool_epic_and_helper_sections_distinct()
    {
        var presentation = Build(ParseFixture());

        Assert.Equal("Leaping", presentation.AdditionalSections[2].DisplayName);
        Assert.Equal("Mu Mastery", presentation.AdditionalSections[3].DisplayName);
        Assert.Equal("Fighting", presentation.AdditionalSections[4].DisplayName);
        Assert.Equal("Inherents", presentation.AdditionalSections[5].DisplayName);
        Assert.Equal("Fury", Assert.Single(presentation.AdditionalSections[5].Powers).DisplayName);
    }

    [Fact]
    public void Build_preserves_slot_order_duplicates_and_explicit_empty_slots()
    {
        var presentation = Build(ParseFixture());
        var scorch = presentation.PrimarySection!.Powers[0];

        Assert.Equal(4, scorch.EnhancementSlots.Count);
        Assert.False(scorch.EnhancementSlots[0].IsEmpty);
        Assert.False(scorch.EnhancementSlots[1].IsEmpty);
        Assert.True(scorch.EnhancementSlots[2].IsEmpty);
        Assert.False(scorch.EnhancementSlots[3].IsEmpty);
        Assert.Equal(scorch.EnhancementSlots[0].Tooltip, scorch.EnhancementSlots[1].Tooltip);
        Assert.Contains("Attuned", scorch.EnhancementSlots[3].Tooltip, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_flows_power_empty_slot_and_enhancement_identities_through_existing_icon_services()
    {
        var assets = new RecordingAssetProvider();
        var compositor = new RecordingEnhancementCompositor();

        Build(ParseFixture(), assets, compositor);

        Assert.Contains("power_scorch.tga", assets.Identities);
        Assert.Contains(EnhancementIconIdentity.EmptySlot, assets.Identities);
        Assert.Contains(compositor.Requests, request => request.IconIdentity == "E_ICON_GEN_DAMAGE_01.tga");
        Assert.Contains(compositor.Requests, request => request.IconIdentity == "E_ICON_ReactiveArmor.tga");
    }

    [Fact]
    public void Build_keeps_missing_power_or_enhancement_art_null_without_losing_the_row_or_slot()
    {
        var assets = new RecordingAssetProvider(resolveImages: false);
        var compositor = new RecordingEnhancementCompositor(resolveImages: false);

        var presentation = Build(ParseFixture(), assets, compositor);

        var scorch = presentation.PrimarySection!.Powers[0];
        Assert.Null(scorch.IconSource);
        Assert.Equal(4, scorch.EnhancementSlots.Count);
        Assert.All(scorch.EnhancementSlots, slot => Assert.Null(slot.IconSource));
    }

    [Fact]
    public void Build_with_unavailable_catalogs_gracefully_preserves_friendly_raw_presentation()
    {
        var snapshot = ParseFixture();

        var presentation = AccountsBuildPresentationSupport.Build(
            snapshot,
            "Fiery Melee",
            "Fiery Aura",
            powerCatalog: null,
            assetProvider: null,
            itemCatalog: null,
            enhancementIconCompositor: null,
            boostMetadataProvider: null);

        Assert.Equal("Fiery Melee", presentation.PrimarySection!.DisplayName);
        Assert.Equal("Breath of Fire", presentation.PrimarySection.Powers[1].DisplayName);
        Assert.All(presentation.PrimarySection.Powers, power => Assert.Null(power.IconSource));
    }

    private static AccountsBuildPresentation Build(
        HomecomingBuildLayoutSnapshot snapshot,
        RecordingAssetProvider? assetProvider = null,
        RecordingEnhancementCompositor? compositor = null) =>
        AccountsBuildPresentationSupport.Build(
            snapshot,
            "Fiery Melee",
            "Fiery Aura",
            CreatePowerCatalog(),
            assetProvider ?? new RecordingAssetProvider(),
            ItemCatalog,
            compositor ?? new RecordingEnhancementCompositor(),
            new FakeBoostMetadataProvider());

    private static HomecomingBuildLayoutSnapshot ParseFixture()
    {
        const string content = """
            Alpha Hero: Level 38 Magic Class_Brute
            Level 1: Inherent Inherent Brawl
                EMPTY
            Level 1: Inherent Fitness Swift
                EMPTY
            Level 1: Brute_Melee Fiery_Melee Scorch
                Crafted_Damage (35)
                Crafted_Damage (35)
                EMPTY
                Attuned_Reactive_Armor_F (1)
            Level 10: Brute_Melee Fiery_Melee Breath_of_Fire
                Crafted_Recharge (35+2)
            Level 1: Brute_Defense Fiery_Aura Blazing_Aura
                EMPTY
            Level 8: Pool Leaping Long_Jump
                Crafted_Jump (35)
            Level 38: Epic Brute_Mu_Mastery Electrifying_Fences
                EMPTY
            Level 35: Pool Fighting Boxing
                EMPTY
            Level 38: Redirects Inherents Fury_Proc
            """;
        Assert.True(HomecomingBuildLayoutParser.TryParse(content, out var snapshot));
        return snapshot;
    }

    private static IHomecomingPowerReferenceCatalog CreatePowerCatalog() =>
        new FakePowerCatalog(
        [
            Power("Inherent", "Inherent", "Brawl", "Inherent", "Brawl"),
            Power("Inherent", "Fitness", "Swift", "Inherent Fitness", "Swift"),
            Power("Brute_Melee", "Fiery_Melee", "Scorch", "Fiery Melee", "Scorch", "power_scorch.tga"),
            Power("Brute_Melee", "Fiery_Melee", "Breath_of_Fire", "Fiery Melee", "Breath of Fire"),
            Power("Brute_Defense", "Fiery_Aura", "Blazing_Aura", "Fiery Aura", "Blazing Aura"),
            Power("Pool", "Leaping", "Long_Jump", "Leaping", "Super Jump"),
            Power("Epic", "Brute_Mu_Mastery", "Electrifying_Fences", "Mu Mastery", "Electrifying Fences"),
            Power("Pool", "Fighting", "Boxing", "Fighting", "Boxing"),
            Power("Redirects", "Inherents", "Fury_Proc", "Inherents", "Fury")
        ]);

    private static HomecomingPowerReference Power(
        string category,
        string powerSet,
        string power,
        string powerSetDisplayName,
        string powerDisplayName,
        string? iconIdentity = null) =>
        new(
            category,
            powerSet,
            power,
            powerSetDisplayName,
            powerDisplayName,
            iconIdentity,
            false,
            false,
            HomecomingPowerType.Click);

    private sealed class FakePowerCatalog(IEnumerable<HomecomingPowerReference> powers)
        : IHomecomingPowerReferenceCatalog
    {
        private readonly Dictionary<string, HomecomingPowerReference> _powers = powers.ToDictionary(
            power => $"{power.CategoryId}.{power.PowersetId}.{power.PowerId}",
            StringComparer.OrdinalIgnoreCase);

        public bool IsLoaded => true;

        public bool TryResolve(
            string categoryId,
            string powersetId,
            string powerId,
            out HomecomingPowerReference power) =>
            _powers.TryGetValue($"{categoryId}.{powersetId}.{powerId}", out power);
    }

    private sealed class RecordingAssetProvider(bool resolveImages = true) : IInstalledGameAssetProvider
    {
        public List<string> Identities { get; } = [];

        public ImageSource? TryResolve(string? iconIdentity)
        {
            if (iconIdentity is null)
            {
                return null;
            }

            Identities.Add(iconIdentity);
            return resolveImages ? new DrawingImage() : null;
        }
    }

    private sealed class RecordingEnhancementCompositor(bool resolveImages = true)
        : IEnhancementIconCompositor
    {
        public List<EnhancementIconCompositionRequest> Requests { get; } = [];

        public ImageSource? TryCompose(EnhancementIconCompositionRequest request)
        {
            Requests.Add(request);
            return resolveImages ? new DrawingImage() : null;
        }
    }

    private sealed class FakeBoostMetadataProvider : IHomecomingBoostMetadataProvider
    {
        public IReadOnlyList<string>? TryGetBoostsAllowed(string? homecomingSourceId) =>
            homecomingSourceId?.Contains("Reactive_Armor_F", StringComparison.Ordinal) == true
                ? ["Endurance"]
                : null;
    }
}
