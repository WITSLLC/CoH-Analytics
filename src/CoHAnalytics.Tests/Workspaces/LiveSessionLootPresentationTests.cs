using System.Windows.Media;
using System.Windows.Media.Imaging;
using CoHAnalytics.Homecoming;
using CoHAnalytics.ReferenceData;
using CoHAnalytics.ViewModels.Workspaces;

namespace CoHAnalytics.Tests.Workspaces;

public sealed class LiveSessionLootPresentationTests
{
    private readonly IItemReferenceCatalog _catalog = ItemReferenceCatalogFactory.LoadEmbeddedProduction();

    [Fact]
    public void Common_enhancement_resolves_default_rarity_presentation()
    {
        Assert.True(_catalog.TryResolve("Invention: Accuracy", out _));
        var row = CreateRow("Invention: Accuracy", ReferenceItemFamily.Enhancement);

        Assert.Null(row.RarityCode);
        Assert.Equal(
            ReferenceRarityPresentation.DefaultBrushKey,
            row.PresentationBrushKey);
    }

    [Fact]
    public void Uncommon_enhancement_resolves_canonical_rarity()
    {
        Assert.True(_catalog.TryResolve("Adjusted Targeting: To Hit Buff", out _));
        var row = CreateRow("Adjusted Targeting: To Hit Buff", ReferenceItemFamily.Enhancement);

        Assert.Equal("ECUncommon", row.RarityCode);
        Assert.Equal(
            ReferenceRarityPresentation.UncommonBrushKey,
            row.PresentationBrushKey);
    }

    [Fact]
    public void Rare_enhancement_resolves_canonical_rarity()
    {
        Assert.True(_catalog.TryResolve("Positron's Blast: Accuracy/Damage", out _));
        var row = CreateRow("Positron's Blast: Accuracy/Damage", ReferenceItemFamily.Enhancement);

        Assert.Equal("ECRare", row.RarityCode);
        Assert.Equal(
            ReferenceRarityPresentation.RareBrushKey,
            row.PresentationBrushKey);
    }

    [Fact]
    public void Enhancement_resolves_canonical_artwork()
    {
        Assert.True(_catalog.TryResolve("Invention: Accuracy", out _));
        var compositor = new RecordingEnhancementIconCompositor();
        var row = LiveSessionLootPresentationSupport.CreateRow(
            _catalog,
            compositor,
            null,
            null,
            "Invention: Accuracy",
            "1",
            ReferenceItemFamily.Enhancement);

        Assert.NotNull(row.IconSource);
        Assert.NotEmpty(compositor.Requests);
    }

    [Fact]
    public void Recipe_resolves_canonical_rarity()
    {
        Assert.True(_catalog.TryResolve("Adjusted Targeting: To Hit Buff (Recipe)", out var resolution));
        Assert.Equal("Uncommon", resolution.Item.Rarity);
        var row = CreateRow("Adjusted Targeting: To Hit Buff (Recipe)", ReferenceItemFamily.Recipe);

        Assert.Equal("ECUncommon", row.RarityCode);
        Assert.Equal(
            ReferenceRarityPresentation.UncommonBrushKey,
            row.PresentationBrushKey);
    }

    [Fact]
    public void Recipe_resolves_canonical_artwork()
    {
        Assert.True(_catalog.TryResolve("Invention: Accuracy (Recipe)", out _));
        var compositor = new RecordingEnhancementIconCompositor();
        var row = LiveSessionLootPresentationSupport.CreateRow(
            _catalog,
            compositor,
            null,
            null,
            "Invention: Accuracy (Recipe)",
            "1",
            ReferenceItemFamily.Recipe);

        Assert.NotNull(row.IconSource);
        Assert.NotEmpty(compositor.Requests);
    }

    [Fact]
    public void Same_rarity_uses_same_presentation_semantics_as_salvage()
    {
        var uncommonSalvage = CreateRow("Mutant Blood Sample", ReferenceItemFamily.Salvage);
        var uncommonRecipe = CreateRow("Adjusted Targeting: To Hit Buff (Recipe)", ReferenceItemFamily.Recipe);

        Assert.Equal(uncommonSalvage.RarityCode, uncommonRecipe.RarityCode);
        Assert.Equal(uncommonSalvage.PresentationBrushKey, uncommonRecipe.PresentationBrushKey);
    }

    [Fact]
    public void Missing_enhancement_artwork_retains_colored_row()
    {
        Assert.True(_catalog.TryResolve("Adjusted Targeting: To Hit Buff", out _));
        var compositor = new RecordingEnhancementIconCompositor { ReturnNull = true };
        var row = LiveSessionLootPresentationSupport.CreateRow(
            _catalog,
            compositor,
            null,
            null,
            "Adjusted Targeting: To Hit Buff",
            "2",
            ReferenceItemFamily.Enhancement);

        Assert.Null(row.IconSource);
        Assert.False(row.HasIcon);
        Assert.Equal("ECUncommon", row.RarityCode);
        Assert.Equal(ReferenceRarityPresentation.UncommonBrushKey, row.PresentationBrushKey);
        Assert.Equal("Adjusted Targeting: To Hit Buff", row.DisplayName);
        Assert.Equal("2", row.QuantityLabel);
    }

    [Fact]
    public void Missing_recipe_artwork_retains_colored_row()
    {
        Assert.True(_catalog.TryResolve("Adjusted Targeting: To Hit Buff (Recipe)", out _));
        var compositor = new RecordingEnhancementIconCompositor { ReturnNull = true };
        var row = LiveSessionLootPresentationSupport.CreateRow(
            _catalog,
            compositor,
            null,
            null,
            "Adjusted Targeting: To Hit Buff (Recipe)",
            "3",
            ReferenceItemFamily.Recipe);

        Assert.Null(row.IconSource);
        Assert.False(row.HasIcon);
        Assert.Equal("ECUncommon", row.RarityCode);
        Assert.Equal(ReferenceRarityPresentation.UncommonBrushKey, row.PresentationBrushKey);
    }

    [Fact]
    public void Unresolved_enhancement_remains_visible_without_fabricated_presentation()
    {
        var row = CreateRow("Totally Unknown Enhancement Drop", ReferenceItemFamily.Enhancement);

        Assert.Equal("Totally Unknown Enhancement Drop", row.DisplayName);
        Assert.Null(row.RarityCode);
        Assert.Null(row.PresentationBrushKey);
        Assert.Null(row.IconSource);
        Assert.False(row.HasIcon);
    }

    [Fact]
    public void Unresolved_recipe_remains_visible_without_fabricated_presentation()
    {
        var row = CreateRow("Totally Unknown Recipe Drop", ReferenceItemFamily.Recipe);

        Assert.Equal("Totally Unknown Recipe Drop", row.DisplayName);
        Assert.Null(row.RarityCode);
        Assert.Null(row.PresentationBrushKey);
        Assert.Null(row.IconSource);
        Assert.False(row.HasIcon);
    }

    [Fact]
    public void Salvage_presentation_unchanged_for_catalog_rarities()
    {
        var common = CreateRow("Human Blood Sample", ReferenceItemFamily.Salvage);
        var uncommon = CreateRow("Mutant Blood Sample", ReferenceItemFamily.Salvage);
        var rare = CreateRow("Alien Blood Sample", ReferenceItemFamily.Salvage);

        Assert.Null(common.RarityCode);
        Assert.Equal("ECUncommon", uncommon.RarityCode);
        Assert.Equal("ECRare", rare.RarityCode);
        Assert.Equal(ReferenceRarityPresentation.UncommonBrushKey, uncommon.PresentationBrushKey);
        Assert.Equal(ReferenceRarityPresentation.RareBrushKey, rare.PresentationBrushKey);
        Assert.Null(common.IconSource);
    }

    [Fact]
    public void Protected_resolves_canonical_record_with_defense_presentation_and_icon()
    {
        Assert.True(_catalog.TryResolve("Protected", out var resolution));
        var assetProvider = new RecordingInstalledGameAssetProvider();
        var row = LiveSessionLootPresentationSupport.CreateRow(
            _catalog,
            null,
            null,
            assetProvider,
            "Protected",
            "1",
            ReferenceItemFamily.Inspiration);

        Assert.Equal("INS-00039", resolution.Item.CatalogItemId);
        Assert.Equal("Protected", row.DisplayName);
        Assert.Null(row.RarityCode);
        Assert.Equal(InspirationPresentation.DefenseBrushKey, row.PresentationBrushKey);
        Assert.Equal("Inspiration_Dual_Def_Res_Lvl_3.tga", assetProvider.LastResolvedIdentity);
        Assert.NotNull(row.IconSource);
        Assert.True(row.HasIcon);
    }

    [Fact]
    public void Revitalize_uses_health_presentation_and_resolves_icon()
    {
        var assetProvider = new RecordingInstalledGameAssetProvider();
        var row = LiveSessionLootPresentationSupport.CreateRow(
            _catalog,
            null,
            null,
            assetProvider,
            "Revitalize",
            "2",
            ReferenceItemFamily.Inspiration);

        Assert.Equal("Revitalize", row.DisplayName);
        Assert.Equal(InspirationPresentation.HealthBrushKey, row.PresentationBrushKey);
        Assert.Equal("Inspiration_Dual_Health_End_Lvl_1.tga", assetProvider.LastResolvedIdentity);
        Assert.NotNull(row.IconSource);
    }

    [Theory]
    [InlineData("Enrage", InspirationPresentation.DamageBrushKey)]
    [InlineData("Insight", InspirationPresentation.AccuracyBrushKey)]
    [InlineData("Catch a Breath", InspirationPresentation.EnduranceBrushKey)]
    [InlineData("Awaken", InspirationPresentation.ResurrectionBrushKey)]
    [InlineData("Emerge", InspirationPresentation.StatusProtectionBrushKey)]
    public void Single_inspirations_use_semantic_family_brushes(string displayName, string expectedBrushKey)
    {
        var row = CreateInspirationRow(displayName);

        Assert.Equal(expectedBrushKey, row.PresentationBrushKey);
        Assert.Null(row.RarityCode);
    }

    [Fact]
    public void Team_inspiration_uses_underlying_family_color()
    {
        var row = CreateInspirationRow("Insight Imbuement");

        Assert.Equal(InspirationPresentation.AccuracyBrushKey, row.PresentationBrushKey);
    }

    [Fact]
    public void Team_dual_inspiration_uses_primary_family_color()
    {
        var row = CreateInspirationRow("Tactical Imbuement");

        Assert.Equal(InspirationPresentation.DamageBrushKey, row.PresentationBrushKey);
    }

    [Fact]
    public void Ultimate_uses_level_shift_presentation_and_icon()
    {
        var assetProvider = new RecordingInstalledGameAssetProvider();
        var row = LiveSessionLootPresentationSupport.CreateRow(
            _catalog,
            null,
            null,
            assetProvider,
            "Ultimate",
            "1",
            ReferenceItemFamily.Inspiration);

        Assert.Equal(InspirationPresentation.LevelShiftBrushKey, row.PresentationBrushKey);
        Assert.Equal("Inspiration_LevelShift_Lvl_4.tga", assetProvider.LastResolvedIdentity);
        Assert.NotNull(row.IconSource);
    }

    [Theory]
    [InlineData("Present")]
    [InlineData("Happy Anniversary!")]
  [InlineData("Ambrosia")]
    public void Special_inspirations_use_special_presentation(string displayName)
    {
        var row = CreateInspirationRow(displayName);

        Assert.Equal(InspirationPresentation.SpecialBrushKey, row.PresentationBrushKey);
        Assert.Null(row.RarityCode);
    }

    [Fact]
    public void Missing_inspiration_artwork_retains_semantic_colored_row()
    {
        var assetProvider = new RecordingInstalledGameAssetProvider { ReturnNull = true };
        var row = LiveSessionLootPresentationSupport.CreateRow(
            _catalog,
            null,
            null,
            assetProvider,
            "Protected",
            "1",
            ReferenceItemFamily.Inspiration);

        Assert.Null(row.IconSource);
        Assert.False(row.HasIcon);
        Assert.Equal("Protected", row.DisplayName);
        Assert.Equal(InspirationPresentation.DefenseBrushKey, row.PresentationBrushKey);
    }

    [Fact]
    public void Unresolved_inspiration_remains_visible_without_fabricated_presentation()
    {
        var row = CreateInspirationRow("Totally Unknown Inspiration Drop");

        Assert.Equal("Totally Unknown Inspiration Drop", row.DisplayName);
        Assert.Null(row.RarityCode);
        Assert.Null(row.PresentationBrushKey);
        Assert.Null(row.IconSource);
        Assert.False(row.HasIcon);
    }

    [Fact]
    public void Duplicate_display_name_resolves_deterministically_to_first_catalog_record()
    {
        Assert.True(_catalog.TryResolve("Happy Anniversary!", out var resolution));
        Assert.Equal("INS-00076", resolution.Item.CatalogItemId);

        var row = CreateInspirationRow("Happy Anniversary!");

        Assert.Equal("Happy Anniversary!", row.DisplayName);
        Assert.Equal(InspirationPresentation.SpecialBrushKey, row.PresentationBrushKey);
    }

    private LiveSessionItemRowViewModel CreateRow(string observedName, ReferenceItemFamily family) =>
        LiveSessionLootPresentationSupport.CreateRow(
            _catalog,
            new RecordingEnhancementIconCompositor(),
            null,
            null,
            observedName,
            "1",
            family);

    private LiveSessionItemRowViewModel CreateInspirationRow(string observedName) =>
        LiveSessionLootPresentationSupport.CreateRow(
            _catalog,
            null,
            null,
            new RecordingInstalledGameAssetProvider(),
            observedName,
            "1",
            ReferenceItemFamily.Inspiration);

    private sealed class RecordingEnhancementIconCompositor : IEnhancementIconCompositor
    {
        public bool ReturnNull { get; init; }

        public List<EnhancementIconCompositionRequest> Requests { get; } = [];

        public ImageSource? TryCompose(EnhancementIconCompositionRequest request)
        {
            Requests.Add(request);
            if (ReturnNull)
            {
                return null;
            }

            var pixels = new byte[] { 40, 80, 120, 200 };
            var source = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, pixels, 4);
            source.Freeze();
            return source;
        }
    }

    private sealed class RecordingInstalledGameAssetProvider : IInstalledGameAssetProvider
    {
        public bool ReturnNull { get; init; }

        public string? LastResolvedIdentity { get; private set; }

        public ImageSource? TryResolve(string? iconIdentity)
        {
            LastResolvedIdentity = iconIdentity;
            if (ReturnNull || string.IsNullOrWhiteSpace(iconIdentity))
            {
                return null;
            }

            var pixels = new byte[] { 40, 80, 120, 200 };
            var source = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, pixels, 4);
            source.Freeze();
            return source;
        }
    }
}
