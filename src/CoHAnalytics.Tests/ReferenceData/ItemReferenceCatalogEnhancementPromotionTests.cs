using CoHAnalytics.ReferenceData;
using Microsoft.Data.Sqlite;

namespace CoHAnalytics.Tests.ReferenceData;

public sealed class ItemReferenceCatalogEnhancementPromotionTests
{
    [Fact]
    public void Production_catalog_preserves_stable_enhancement_and_set_ids()
    {
        var catalog = (ItemReferenceCatalog)ItemReferenceCatalogFactory.LoadEmbeddedProduction();

        Assert.True(catalog.TryResolve("Invention: Accuracy", out var accuracy));
        Assert.Equal("ENH-00001", accuracy.Item.CatalogItemId);
        Assert.Equal("CraftedInvention", accuracy.Item.Subtype);
        Assert.Equal(ReferenceEnhancementFamily.CraftedInvention, accuracy.Item.EnhancementFamily);

        Assert.True(catalog.TryResolve("Bonesnap: Accuracy/Damage", out var bonesnapPiece));
        Assert.Equal("SET-00001", bonesnapPiece.Item.EnhancementSetId);
        Assert.Equal(
            "Bonesnap",
            catalog.EnhancementSets[bonesnapPiece.Item.EnhancementSetId!].CurrentDisplayName);

        Assert.True(catalog.TryResolve("Hecatomb: Damage", out var hecatomb));
        Assert.Equal("ENH-00067", hecatomb.Item.CatalogItemId);
        Assert.Equal("SET-00011", hecatomb.Item.EnhancementSetId);
    }

    [Fact]
    public void Common_IO_classification_uses_structural_boost_type_not_display_name()
    {
        var catalog = (ItemReferenceCatalog)ItemReferenceCatalogFactory.LoadEmbeddedProduction();

        Assert.True(catalog.TryResolve("Invention: Recharge Reduction", out var recharge));
        Assert.Equal("Recharge", recharge.Item.CommonIoBoostType);
        Assert.Equal("Recharge Reduction", recharge.Item.CommonIoBoostTypeDisplayText);

        Assert.True(catalog.TryResolve("Invention: Endurance Reduction", out var endurance));
        Assert.Equal("EnduranceDiscount", endurance.Item.CommonIoBoostType);
        Assert.Equal("Endurance Reduction", endurance.Item.CommonIoBoostTypeDisplayText);

        Assert.True(catalog.TryResolve("Invention: Resist Damage", out var resistance));
        Assert.Equal("Res_Damage", resistance.Item.CommonIoBoostType);
        Assert.Equal("Damage Resistance", resistance.Item.CommonIoBoostTypeDisplayText);

        Assert.All(
            catalog.DebugItems.Values.Where(item =>
                item.Family == ReferenceItemFamily.Enhancement
                && item.EnhancementFamily == ReferenceEnhancementFamily.CraftedInvention
                && item.CommonIoBoostType is not null),
            item => Assert.False(
                string.Equals(item.CommonIoBoostType, item.CurrentDisplayName, StringComparison.Ordinal)));
    }

    [Fact]
    public void Set_category_preserves_canonical_key_with_independent_presentation_label()
    {
        var catalog = (ItemReferenceCatalog)ItemReferenceCatalogFactory.LoadEmbeddedProduction();
        var bonesnap = catalog.EnhancementSets["SET-00001"];
        Assert.Equal("Bonesnap", bonesnap.HomecomingSetId);
        Assert.Equal("ECMelee", bonesnap.CategoryCode);
        Assert.Equal("Melee", bonesnap.CategoryDisplayText);
        Assert.Equal(10, bonesnap.MinimumLevel);
        Assert.Equal(25, bonesnap.MaximumLevel);

        var toHitSets = catalog.EnhancementSets.Values
            .Where(set => string.Equals(set.CategoryCode, "ECToHitDeBuff", StringComparison.Ordinal))
            .OrderBy(set => set.CatalogItemId, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(4, toHitSets.Length);
        Assert.All(toHitSets, set => Assert.Equal("To-Hit Debuff", set.CategoryDisplayText));
        Assert.Contains(toHitSets, set => set.CurrentDisplayName == "Dampened Spirits");
        Assert.DoesNotContain(
            toHitSets,
            set => string.Equals(set.CategoryCode, set.CategoryDisplayText, StringComparison.Ordinal));
    }

    [Fact]
    public void Enhancement_icon_identifiers_are_promoted_without_image_bytes()
    {
        var catalog = (ItemReferenceCatalog)ItemReferenceCatalogFactory.LoadEmbeddedProduction();
        Assert.True(catalog.TryResolve("Invention: Accuracy", out var accuracy));
        Assert.Equal("E_ICON_GEN_ACCURACY_01.tga", accuracy.Item.Icon);
        Assert.EndsWith(".tga", accuracy.Item.Icon, StringComparison.OrdinalIgnoreCase);

        Assert.True(catalog.TryResolve("Hecatomb: Damage", out var hecatomb));
        Assert.Equal("E_ICON_Hecatomb.tga", hecatomb.Item.Icon);
        Assert.Equal(2, hecatomb.Item.SourceVariants.Count);
        Assert.Contains(
            hecatomb.Item.SourceVariants,
            variant => variant.SourceForm == "Crafted");
        Assert.Contains(
            hecatomb.Item.SourceVariants,
            variant => variant.SourceForm == "Superior_Attuned");
        Assert.All(
            hecatomb.Item.SourceVariants,
            variant => Assert.Equal(hecatomb.Item.Icon, variant.Icon));

        Assert.Equal(
            0,
            catalog.DebugItems.Values.Count(item =>
                item.Family == ReferenceItemFamily.Enhancement
                && item.SourceVariants.Select(variant => variant.Icon)
                    .Where(icon => !string.IsNullOrWhiteSpace(icon))
                    .Distinct(StringComparer.Ordinal)
                    .Count() > 1));
    }

    [Fact]
    public void Set_bonuses_preserve_piece_thresholds_and_resolved_help()
    {
        var catalog = (ItemReferenceCatalog)ItemReferenceCatalogFactory.LoadEmbeddedProduction();
        var hecatomb = catalog.EnhancementSets["SET-00011"];
        Assert.Equal("ECVeryRare", hecatomb.RarityCode);
        Assert.Equal("Very Rare", hecatomb.RarityDisplayText);
        Assert.Equal(5, hecatomb.Bonuses.Count);

        var first = hecatomb.Bonuses[0];
        Assert.Equal(2, first.MinimumBoosts);
        Assert.Equal(6, first.MaximumBoosts);
        Assert.Equal(ReferenceEnhancementSetBonusRequiresPattern.None, first.RequiresPattern);
        Assert.Empty(first.RequiresTokens);
        Assert.Equal(
            "Set_Bonus.Set_Bonus.Improved_Recovery_7",
            Assert.Single(first.AutoPowers).HomecomingSourceId);
        Assert.Equal(
            "Improves your Recovery by 4%.",
            Assert.Single(first.AutoPowers).DisplayHelp);

        var catalogAfterPromotion = (ItemReferenceCatalog)ItemReferenceCatalogFactory.LoadEmbeddedProduction();
        var aegis = catalogAfterPromotion.EnhancementSets.Values
            .First(set => string.Equals(set.HomecomingSetId, "Aegis", StringComparison.Ordinal));
        var pieceGate = Assert.Single(
            aegis.Bonuses,
            bonus => bonus.RequiresPattern == ReferenceEnhancementSetBonusRequiresPattern.PieceGate);
        Assert.Contains("Crafted_Aegis_F", pieceGate.RequiresTokens);
        Assert.NotEmpty(pieceGate.RequiredEnhancementIds);
        Assert.NotEmpty(pieceGate.AutoPowers);

        Assert.All(
            catalogAfterPromotion.EnhancementSets.Values.SelectMany(set => set.Bonuses)
                .SelectMany(bonus => bonus.AutoPowers),
            power => Assert.False(string.IsNullOrWhiteSpace(power.DisplayHelp)));
    }

    [Fact]
    public void Level_data_is_set_slottable_range_not_recipe_craft_level()
    {
        var catalog = (ItemReferenceCatalog)ItemReferenceCatalogFactory.LoadEmbeddedProduction();
        var hecatomb = catalog.EnhancementSets["SET-00011"];
        Assert.Equal(50, hecatomb.MinimumLevel);
        Assert.Equal(50, hecatomb.MaximumLevel);

        Assert.True(catalog.TryResolve("Hecatomb: Damage", out var piece));
        Assert.NotNull(piece.Item.EnhancementSetId);
        Assert.All(
            catalog.EnhancementSets.Values.Where(set => set.HomecomingSetId is not null),
            set =>
            {
                Assert.NotNull(set.MinimumLevel);
                Assert.NotNull(set.MaximumLevel);
                Assert.True(set.MinimumLevel <= set.MaximumLevel);
            });
    }
}

[Collection(nameof(ReferenceDatabaseCollection))]
public sealed class ItemReferenceCatalogEnhancementPromotionSqliteTests
{
    private readonly ReferenceDatabaseFixture _fixture;

    public ItemReferenceCatalogEnhancementPromotionSqliteTests(ReferenceDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void Generated_database_stores_enhancement_enrichment_without_artwork_blobs()
    {
        using var connection = new SqliteConnection($"Data Source={_fixture.DatabasePath};Mode=ReadOnly");
        connection.Open();

        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                SELECT common_io_boost_type, common_io_boost_type_display_text, icon, display_help
                FROM Item
                WHERE catalog_item_id = 'ENH-00001';
                """;
            using var reader = command.ExecuteReader();
            Assert.True(reader.Read());
            Assert.Equal("Accuracy", reader.GetString(0));
            Assert.Equal("Accuracy", reader.GetString(1));
            Assert.Equal("E_ICON_GEN_ACCURACY_01.tga", reader.GetString(2));
            Assert.False(reader.IsDBNull(3));
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                SELECT COUNT(*)
                FROM EnhancementSetBonusPower
                WHERE set_catalog_item_id = 'SET-00011';
                """;
            Assert.Equal(5, Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture));
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                SELECT requires_pattern
                FROM EnhancementSetBonus
                WHERE set_catalog_item_id = 'SET-00011'
                ORDER BY bonus_index;
                """;
            using var reader = command.ExecuteReader();
            var patterns = new List<string>();
            while (reader.Read())
            {
                patterns.Add(reader.GetString(0));
            }

            Assert.Equal(5, patterns.Count);
            Assert.All(patterns, pattern => Assert.Equal("None", pattern));
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                SELECT name FROM sqlite_master
                WHERE type = 'table'
                  AND name IN (
                    'Item',
                    'EnhancementSet',
                    'EnhancementSetBonus',
                    'EnhancementSetBonusRequiresToken',
                    'EnhancementSetBonusRequiredEnhancement',
                    'EnhancementSetBonusPower',
                    'EnhancementSourceVariant');
                """;
            var tables = new HashSet<string>(StringComparer.Ordinal);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                tables.Add(reader.GetString(0));
            }

            Assert.Contains("EnhancementSetBonus", tables);
            Assert.Contains("EnhancementSetBonusRequiresToken", tables);
            Assert.Contains("EnhancementSetBonusRequiredEnhancement", tables);
            Assert.Contains("EnhancementSetBonusPower", tables);
            Assert.Contains("EnhancementSourceVariant", tables);
        }

        Assert.True(new FileInfo(_fixture.DatabasePath).Length > 0);
        Assert.DoesNotContain(
            ".png",
            Path.GetExtension(_fixture.DatabasePath),
            StringComparison.OrdinalIgnoreCase);
    }
}
