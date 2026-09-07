using CoHAnalytics.ReferenceData;

namespace CoHAnalytics.Tests.ReferenceData;

public sealed class ItemReferenceCatalogProductionTests
{
    [Fact]
    public void Production_catalog_loads_successfully()
    {
        var catalog = ItemReferenceCatalogFactory.LoadEmbeddedProduction();

        Assert.True(catalog.IsLoaded, catalog.LoadFailureReason);
        Assert.Null(catalog.LoadFailureReason);
        Assert.Equal("item-ref-3.1.0", catalog.Manifest!.CatalogVersion);
        Assert.Equal("homecoming-inspiration-promotion-2026-08-16", catalog.Manifest.SourceRevision);
    }

    [Fact]
    public void Production_catalog_contains_no_fixture_placeholders()
    {
        var catalog = ItemReferenceCatalogFactory.LoadEmbeddedProduction();
        Assert.False(catalog.TryResolve("Fixture Salvage Sample", out _));
        Assert.False(catalog.TryResolve("Fixture Enhancement Sample", out _));
        Assert.False(catalog.TryResolve("Unstable Aether", out _));
    }

    [Fact]
    public void Ordinary_inspiration_gate_is_78()
    {
        var catalog = (ItemReferenceCatalog)ItemReferenceCatalogFactory.LoadEmbeddedProduction();
        var ordinaryInspirations = catalog.DebugItems.Values
            .Where(i => i.Family == ReferenceItemFamily.Inspiration
                && i.Subtype is "Single" or "Dual" or "Team")
            .ToList();
        Assert.Equal(78, ordinaryInspirations.Count);
        Assert.All(
            ordinaryInspirations,
            i => Assert.Equal(ReferenceVerificationStatus.VerifiedMultiSource, i.VerificationStatus));
    }

    [Fact]
    public void Known_inspiration_names_resolve()
    {
        var catalog = ItemReferenceCatalogFactory.LoadEmbeddedProduction();

        Assert.True(catalog.TryResolve("Luck", out var luck));
        Assert.Equal(ReferenceItemFamily.Inspiration, luck.Item.Family);
        Assert.Equal("Single", luck.Item.Subtype);

        Assert.True(catalog.TryResolve("Respite", out var respite));
        Assert.Equal(ReferenceItemFamily.Inspiration, respite.Item.Family);

        Assert.True(catalog.TryResolve("Luck Imbuement", out var team));
        Assert.Equal(ReferenceItemFamily.Inspiration, team.Item.Family);
        Assert.Equal("Team", team.Item.Subtype);
    }

    [Fact]
    public void Ordinary_invention_salvage_gate_is_108()
    {
        var catalog = (ItemReferenceCatalog)ItemReferenceCatalogFactory.LoadEmbeddedProduction();
        var salvage = catalog.DebugItems.Values
            .Count(i => i.Family == ReferenceItemFamily.Salvage && i.Subtype == "Invention");
        Assert.Equal(108, salvage);
        Assert.All(
            catalog.DebugItems.Values.Where(i => i.Family == ReferenceItemFamily.Salvage),
            i => Assert.Equal(ReferenceVerificationStatus.VerifiedMultiSource, i.VerificationStatus));
    }

    [Fact]
    public void Known_salvage_and_enhancement_names_resolve()
    {
        var catalog = ItemReferenceCatalogFactory.LoadEmbeddedProduction();

        Assert.True(catalog.TryResolve("Luck Charm", out var salvage));
        Assert.Equal(ReferenceItemFamily.Salvage, salvage.Item.Family);
        Assert.Equal("Common", salvage.Item.Rarity);
        Assert.Equal("Low", salvage.Item.Tier);
        Assert.Equal("Arcane", salvage.Item.Origin);

        Assert.True(catalog.TryResolve("Invention: Accuracy", out var io));
        Assert.Equal(ReferenceItemFamily.Enhancement, io.Item.Family);
        Assert.Equal("CraftedInvention", io.Item.Subtype);
        Assert.Equal(ReferenceEnhancementFamily.CraftedInvention, io.Item.EnhancementFamily);

        Assert.True(catalog.TryResolve("Bonesnap: Accuracy/Damage", out var setPiece));
        Assert.Equal("SetIO", setPiece.Item.Subtype);
        Assert.False(string.IsNullOrWhiteSpace(setPiece.Item.EnhancementSetId));
    }

    [Fact]
    public void Enhancement_set_references_resolve()
    {
        var catalog = (ItemReferenceCatalog)ItemReferenceCatalogFactory.LoadEmbeddedProduction();
        Assert.True(catalog.TryResolve("Bonesnap: Accuracy/Damage", out var piece));
        Assert.True(catalog.EnhancementSets.ContainsKey(piece.Item.EnhancementSetId!));
        Assert.Equal(
            "Bonesnap",
            catalog.EnhancementSets[piece.Item.EnhancementSetId!].CurrentDisplayName);
    }

    [Fact]
    public void Telemetry_fixture_names_resolution_audit()
    {
        var catalog = ItemReferenceCatalogFactory.LoadEmbeddedProduction();
        var cases = new (string Name, bool ExpectResolved)[]
        {
            ("Luck Charm", true),
            ("Invention: Accuracy", true),
            ("Armageddon: Damage (Recipe)", false), // Homecoming recipe identity is the Superior name
            ("Accuracy SO", false), // SO multi-source authorship deferred
            ("Mystery Thing", false),
            ("Impervium Armor", false), // not ordinary invention salvage / not authored enhancement
            ("Hecatomb: Damage", true),
        };

        var unresolvedCritical = new List<string>();
        foreach (var (name, expectResolved) in cases)
        {
            var resolved = catalog.TryResolve(name, out _);
            if (expectResolved && !resolved)
            {
                unresolvedCritical.Add(name);
            }

            if (!expectResolved)
            {
                Assert.False(resolved, $"Unexpected resolve for '{name}'");
            }
        }

        Assert.True(
            unresolvedCritical.Count == 0,
            "Unresolved cutover-sample names: " + string.Join(", ", unresolvedCritical));
    }

    [Fact]
    public void Essence_of_the_Earth_resolves_as_special_inspiration()
    {
        var catalog = ItemReferenceCatalogFactory.LoadEmbeddedProduction();

        Assert.True(catalog.TryResolve("Essence of the Earth", out var resolution));
        Assert.Equal("INS-00075", resolution.Item.CatalogItemId);
        Assert.Equal(ReferenceItemFamily.Inspiration, resolution.Item.Family);
        Assert.Equal("Special", resolution.Item.Subtype);
        Assert.Equal("Essence of the Earth", resolution.Item.CurrentDisplayName);
        Assert.Equal("Essence of the Earth", resolution.MatchedAliasText);
        Assert.Equal(ReferenceVerificationStatus.VerifiedDirect, resolution.Item.VerificationStatus);
    }

    [Fact]
    public void Unknown_item_still_returns_false()
    {
        var catalog = ItemReferenceCatalogFactory.LoadEmbeddedProduction();
        Assert.False(catalog.TryResolve("Totally Unknown Drop Name XYZ", out _));
    }
}
