using CoHAnalytics.ReferenceData;

namespace CoHAnalytics.Tests.ReferenceData;

public sealed class InspirationPresentationTests
{
    private readonly ItemReferenceCatalog _catalog =
        (ItemReferenceCatalog)ItemReferenceCatalogFactory.LoadEmbeddedProduction();

    [Theory]
    [InlineData("Enrage", InspirationSemanticFamily.Damage)]
    [InlineData("Insight", InspirationSemanticFamily.Accuracy)]
    [InlineData("Luck", InspirationSemanticFamily.Defense)]
    [InlineData("Rugged", InspirationSemanticFamily.Resistance)]
    [InlineData("Respite", InspirationSemanticFamily.Health)]
    [InlineData("Catch a Breath", InspirationSemanticFamily.Endurance)]
    [InlineData("Awaken", InspirationSemanticFamily.Resurrection)]
    [InlineData("Sturdy", InspirationSemanticFamily.Resistance)]
    public void Single_inspiration_maps_to_expected_family(string displayName, InspirationSemanticFamily expected)
    {
        var presentation = InspirationPresentation.Resolve(RequireInspiration(displayName));

        Assert.Equal(expected, presentation.PrimaryFamily);
        Assert.Null(presentation.SecondaryFamily);
        Assert.Equal(InspirationPresentation.ResolveBrushResourceKey(expected), presentation.PresentationKey);
        Assert.Null(presentation.UnmappedIconStem);
    }

    [Theory]
    [InlineData("Iron Will", InspirationSemanticFamily.StatusProtection)]
    [InlineData("Discipline", InspirationSemanticFamily.StatusProtection)]
    public void Status_protection_variants_normalize(string displayName, InspirationSemanticFamily expected)
    {
        var presentation = InspirationPresentation.Resolve(RequireInspiration(displayName));

        Assert.Equal(expected, presentation.PrimaryFamily);
        Assert.Equal(InspirationPresentation.StatusProtectionBrushKey, presentation.PresentationKey);
    }

    [Fact]
    public void Resist_sleep_hold_maps_to_status_protection()
    {
        var presentation = InspirationPresentation.Resolve(RequireInspiration("Emerge"));

        Assert.Equal(InspirationSemanticFamily.StatusProtection, presentation.PrimaryFamily);
        Assert.Equal(InspirationPresentation.StatusProtectionBrushKey, presentation.PresentationKey);
    }

    [Fact]
    public void Protected_maps_to_defense_and_resistance_with_defense_primary()
    {
        var presentation = InspirationPresentation.Resolve(RequireInspiration("Protected"));

        Assert.Equal(InspirationSemanticFamily.Defense, presentation.PrimaryFamily);
        Assert.Equal(InspirationSemanticFamily.Resistance, presentation.SecondaryFamily);
        Assert.Equal(InspirationPresentation.DefenseBrushKey, presentation.PresentationKey);
    }

    [Fact]
    public void Revitalize_maps_to_health_and_endurance_with_health_primary()
    {
        var presentation = InspirationPresentation.Resolve(RequireInspiration("Revitalize"));

        Assert.Equal(InspirationSemanticFamily.Health, presentation.PrimaryFamily);
        Assert.Equal(InspirationSemanticFamily.Endurance, presentation.SecondaryFamily);
        Assert.Equal(InspirationPresentation.HealthBrushKey, presentation.PresentationKey);
    }

    [Fact]
    public void Tactical_dual_maps_to_damage_and_accuracy_with_damage_primary()
    {
        var presentation = InspirationPresentation.Resolve(RequireInspiration("Tactical"));

        Assert.Equal(InspirationSemanticFamily.Damage, presentation.PrimaryFamily);
        Assert.Equal(InspirationSemanticFamily.Accuracy, presentation.SecondaryFamily);
        Assert.Equal(InspirationPresentation.DamageBrushKey, presentation.PresentationKey);
    }

    [Fact]
    public void Team_damage_maps_to_damage()
    {
        var presentation = InspirationPresentation.Resolve(RequireInspiration("Rage Imbuement"));

        Assert.Equal(InspirationSemanticFamily.Damage, presentation.PrimaryFamily);
        Assert.Null(presentation.SecondaryFamily);
    }

    [Fact]
    public void Team_dual_damage_accuracy_maps_to_damage_and_accuracy()
    {
        var presentation = InspirationPresentation.Resolve(RequireInspiration("Tactical Imbuement"));

        Assert.Equal(InspirationSemanticFamily.Damage, presentation.PrimaryFamily);
        Assert.Equal(InspirationSemanticFamily.Accuracy, presentation.SecondaryFamily);
    }

    [Fact]
    public void Ultimate_maps_to_level_shift()
    {
        var presentation = InspirationPresentation.Resolve(RequireInspiration("Ultimate"));

        Assert.Equal(InspirationSemanticFamily.LevelShift, presentation.PrimaryFamily);
        Assert.Null(presentation.SecondaryFamily);
        Assert.Equal(InspirationPresentation.LevelShiftBrushKey, presentation.PresentationKey);
    }

    [Theory]
    [InlineData("Present")]
    [InlineData("Happy Anniversary!")]
    [InlineData("Ambrosia")]
    public void Special_event_records_use_special_presentation(string displayName)
    {
        var presentation = InspirationPresentation.Resolve(RequireInspiration(displayName));

        Assert.Equal(InspirationSemanticFamily.Special, presentation.PrimaryFamily);
        Assert.Null(presentation.SecondaryFamily);
        Assert.Equal(InspirationPresentation.SpecialBrushKey, presentation.PresentationKey);
    }

    [Fact]
    public void Tier_does_not_change_semantic_family()
    {
        var smallLuck = InspirationPresentation.Resolve(RequireInspiration("Luck"));
        var largeLuck = InspirationPresentation.Resolve(RequireInspiration("Phenomenal Luck"));

        Assert.Equal(smallLuck.PrimaryFamily, largeLuck.PrimaryFamily);
        Assert.Equal(smallLuck.PresentationKey, largeLuck.PresentationKey);
    }

    [Fact]
    public void Unknown_icon_token_falls_back_to_special_with_diagnostics()
    {
        var item = RequireInspiration("Luck") with { Icon = "Inspiration_Unknown_Family.tga" };
        var presentation = InspirationPresentation.Resolve(item);

        Assert.Equal(InspirationSemanticFamily.Special, presentation.PrimaryFamily);
        Assert.Equal(InspirationPresentation.SpecialBrushKey, presentation.PresentationKey);
        Assert.Equal("Unknown_Family", presentation.UnmappedIconStem);
    }

    [Fact]
    public void Production_catalog_all_inspirations_return_valid_presentation()
    {
        var inspirations = _catalog.DebugItems.Values
            .Where(item => item.Family == ReferenceItemFamily.Inspiration)
            .ToList();

        Assert.Equal(96, inspirations.Count);
        Assert.All(
            inspirations,
            item =>
            {
                var presentation = InspirationPresentation.Resolve(item);
                Assert.False(string.IsNullOrWhiteSpace(presentation.PresentationKey));
            });
    }

    [Fact]
    public void Production_catalog_semantic_audit_has_no_unmapped_records()
    {
        var inspirations = _catalog.DebugItems.Values
            .Where(item => item.Family == ReferenceItemFamily.Inspiration)
            .ToList();

        var familyCounts = new Dictionary<InspirationSemanticFamily, int>();
        var dualCount = 0;
        var specialCount = 0;
        var unmapped = new List<string>();

        foreach (var item in inspirations)
        {
            var presentation = InspirationPresentation.Resolve(item);
            familyCounts[presentation.PrimaryFamily] = familyCounts.GetValueOrDefault(presentation.PrimaryFamily) + 1;
            if (presentation.SecondaryFamily is not null)
            {
                dualCount++;
            }

            if (presentation.PrimaryFamily == InspirationSemanticFamily.Special)
            {
                specialCount++;
            }

            if (presentation.UnmappedIconStem is not null)
            {
                unmapped.Add($"{item.CatalogItemId} {item.CurrentDisplayName} {presentation.UnmappedIconStem}");
            }
        }

        Assert.Equal(96, inspirations.Count);
        Assert.Equal(21, dualCount);
        Assert.Equal(18, specialCount);
        Assert.Empty(unmapped);
        Assert.Equal(96, familyCounts.Values.Sum());

        Assert.Equal(14, familyCounts.GetValueOrDefault(InspirationSemanticFamily.Damage));
        Assert.Equal(7, familyCounts.GetValueOrDefault(InspirationSemanticFamily.Accuracy));
        Assert.Equal(14, familyCounts.GetValueOrDefault(InspirationSemanticFamily.Defense));
        Assert.Equal(7, familyCounts.GetValueOrDefault(InspirationSemanticFamily.Resistance));
        Assert.Equal(14, familyCounts.GetValueOrDefault(InspirationSemanticFamily.Health));
        Assert.Equal(7, familyCounts.GetValueOrDefault(InspirationSemanticFamily.Endurance));
        Assert.Equal(4, familyCounts.GetValueOrDefault(InspirationSemanticFamily.Resurrection));
        Assert.Equal(10, familyCounts.GetValueOrDefault(InspirationSemanticFamily.StatusProtection));
        Assert.Equal(1, familyCounts.GetValueOrDefault(InspirationSemanticFamily.LevelShift));
        Assert.Equal(18, familyCounts.GetValueOrDefault(InspirationSemanticFamily.Special));

        var teamCount = inspirations.Count(item => item.InspirationForm == "Team");
        var teamDualCount = inspirations.Count(item => item.InspirationForm == "TeamDual");
        Assert.Equal(21, teamCount);
        Assert.Equal(9, teamDualCount);
    }

    [Theory]
    [InlineData("Inspiration_Dual_Def_Res_Lvl_3.tga", "Dual_Def_Res")]
    [InlineData("Inspiration_resist_sleep_hold_Lvl_2.tga", "resist_sleep_hold")]
  [InlineData("Inspiration_Team_Dual_Dmg_Acc_Lvl_1.tga", "Team_Dual_Dmg_Acc")]
    public void ExtractIconStem_normalizes_canonical_identities(string iconIdentity, string expectedStem)
    {
        Assert.Equal(expectedStem, InspirationPresentation.ExtractIconStem(iconIdentity));
    }

    private ItemReferenceRecord RequireInspiration(string displayName)
    {
        Assert.True(_catalog.TryResolve(displayName, out var resolution));
        Assert.Equal(ReferenceItemFamily.Inspiration, resolution.Item.Family);
        return resolution.Item;
    }
}
