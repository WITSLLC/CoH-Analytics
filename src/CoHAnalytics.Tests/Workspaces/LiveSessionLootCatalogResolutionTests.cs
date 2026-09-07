using System.Windows.Media;
using System.Windows.Media.Imaging;
using CoHAnalytics.Homecoming;
using CoHAnalytics.ReferenceData;
using CoHAnalytics.Tests.ReferenceData;
using CoHAnalytics.ViewModels.Workspaces;

namespace CoHAnalytics.Tests.Workspaces;

[Collection(nameof(ReferenceDatabaseCollection))]
public sealed class LiveSessionLootCatalogResolutionTests
{
    private readonly IItemReferenceCatalog _catalog;

    public LiveSessionLootCatalogResolutionTests(ReferenceDatabaseFixture fixture)
    {
        _catalog = ItemReferenceCatalogFactory.LoadFromDatabaseFile(fixture.DatabasePath);
        Assert.True(_catalog.IsLoaded, _catalog.LoadFailureReason);
    }

    [Theory]
    [InlineData("Fortune", "SAL-00094", "Common", null)]
    [InlineData("Mathematic Proof", "SAL-00088", "Common", null)]
    [InlineData("Nevermelting Ice", "SAL-00091", "Common", null)]
    [InlineData("Ruby", "SAL-00097", "Common", null)]
    public void Observed_salvage_names_resolve_through_production_catalog(
        string observedName,
        string expectedCatalogItemId,
        string expectedRarityLabel,
        string? expectedRarityCode)
    {
        Assert.True(_catalog.TryResolve(observedName, out var resolution));
        Assert.Equal(ReferenceItemFamily.Salvage, resolution.Item.Family);
        Assert.Equal(expectedCatalogItemId, resolution.Item.CatalogItemId);
        Assert.Equal(expectedRarityLabel, resolution.Item.Rarity);

        var row = CreateSalvageRow(observedName);
        Assert.Equal(expectedRarityCode, row.RarityCode);
        Assert.Null(row.IconSource);
        Assert.False(row.HasIcon);
    }

    [Fact]
    public void Membrane_exposure_resolves_and_composes_canonical_icon()
    {
        const string observedName = "Membrane Exposure (Rech/ToHit/Def)";
        Assert.True(_catalog.TryResolve(observedName, out var resolution));
        Assert.Equal(ReferenceItemFamily.Enhancement, resolution.Item.Family);
        Assert.Equal("ENH-01376", resolution.Item.CatalogItemId);
        Assert.Null(resolution.Item.EnhancementSetId);

        var compositor = new RecordingEnhancementIconCompositor();
        var boostMetadata = new FakeBoostMetadataProvider()
            .With(
                "Boosts.Hamidon_Buff_Recharge.Hamidon_Buff_Recharge",
                "Defense",
                "ToHit",
                "Recharge",
                "Natural",
                "Technology",
                "Magic",
                "Mutation",
                "Science");

        var row = LiveSessionLootPresentationSupport.CreateRow(
            _catalog,
            compositor,
            boostMetadata,
            null,
            observedName,
            "1",
            ReferenceItemFamily.Enhancement);

        Assert.Null(row.RarityCode);
        Assert.NotNull(row.IconSource);
        Assert.True(row.HasIcon);
        Assert.NotEmpty(compositor.Requests);
        Assert.Equal("E_ICON_HAMIDON_01.tga", compositor.Requests[0].IconIdentity);
        Assert.Equal("Defense", compositor.Requests[0].PogBoostType);
        Assert.Equal(EnhancementFrameClass.Rare, compositor.Requests[0].FrameClass);
    }

    [Fact]
    public void Realistic_recipe_observation_resolves_rarity_and_artwork()
    {
        const string observedName = "Adjusted Targeting: To Hit Buff (Recipe)";
        Assert.True(_catalog.TryResolve(observedName, out var resolution));
        Assert.Equal(ReferenceItemFamily.Recipe, resolution.Item.Family);
        Assert.Equal("Uncommon", resolution.Item.Rarity);

        var compositor = new RecordingEnhancementIconCompositor();
        var boostMetadata = new FakeBoostMetadataProvider()
            .With(
                "Boosts.Crafted_Adjusted_Targeting_A.Crafted_Adjusted_Targeting_A",
                "ToHit",
                "Natural",
                "Technology",
                "Magic",
                "Mutation",
                "Science")
            .With(
                "Boosts.Attuned_Adjusted_Targeting_A.Attuned_Adjusted_Targeting_A",
                "ToHit",
                "Natural",
                "Technology",
                "Magic",
                "Mutation",
                "Science");

        var row = LiveSessionLootPresentationSupport.CreateRow(
            _catalog,
            compositor,
            boostMetadata,
            null,
            observedName,
            "1",
            ReferenceItemFamily.Recipe);

        Assert.Equal("ECUncommon", row.RarityCode);
        Assert.NotNull(row.IconSource);
        Assert.NotEmpty(compositor.Requests);
    }

    [Fact]
    public void Unresolved_salvage_and_enhancement_remain_default_without_fabricated_presentation()
    {
        var salvageRow = CreateSalvageRow("Totally Unknown Salvage Drop");
        Assert.Null(salvageRow.RarityCode);
        Assert.Null(salvageRow.IconSource);
        Assert.False(salvageRow.HasIcon);

        var enhancementRow = LiveSessionLootPresentationSupport.CreateRow(
            _catalog,
            new RecordingEnhancementIconCompositor(),
            null,
            null,
            "Totally Unknown Enhancement Drop",
            "1",
            ReferenceItemFamily.Enhancement);
        Assert.Null(enhancementRow.RarityCode);
        Assert.Null(enhancementRow.IconSource);
        Assert.False(enhancementRow.HasIcon);
    }

  [Fact]
    public void Uncommon_salvage_observation_receives_canonical_rarity_code()
    {
        const string observedName = "Mutant Blood Sample";
        Assert.True(_catalog.TryResolve(observedName, out var resolution));
        Assert.Equal("Uncommon", resolution.Item.Rarity);

        var row = CreateSalvageRow(observedName);
        Assert.Equal("ECUncommon", row.RarityCode);
    }

    private LiveSessionItemRowViewModel CreateSalvageRow(string observedName) =>
        LiveSessionLootPresentationSupport.CreateRow(
            _catalog,
            null,
            null,
            null,
            observedName,
            "1",
            ReferenceItemFamily.Salvage);

    private sealed class RecordingEnhancementIconCompositor : IEnhancementIconCompositor
    {
        public List<EnhancementIconCompositionRequest> Requests { get; } = [];

        public ImageSource? TryCompose(EnhancementIconCompositionRequest request)
        {
            Requests.Add(request);
            var pixels = new byte[] { 40, 80, 120, 200 };
            var source = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, pixels, 4);
            source.Freeze();
            return source;
        }
    }

    private sealed class FakeBoostMetadataProvider : IHomecomingBoostMetadataProvider
    {
        private readonly Dictionary<string, string[]> _boostsAllowed = new(StringComparer.Ordinal);

        public FakeBoostMetadataProvider With(string sourceId, params string[] boostsAllowed)
        {
            _boostsAllowed[sourceId] = boostsAllowed;
            return this;
        }

        public IReadOnlyList<string>? TryGetBoostsAllowed(string? homecomingSourceId)
        {
            if (string.IsNullOrWhiteSpace(homecomingSourceId))
            {
                return null;
            }

            return _boostsAllowed.TryGetValue(homecomingSourceId.Trim(), out var boostsAllowed)
                ? boostsAllowed
                : null;
        }
    }
}
