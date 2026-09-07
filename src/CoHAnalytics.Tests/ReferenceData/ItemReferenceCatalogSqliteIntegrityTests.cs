using CoHAnalytics.ReferenceData;
using Microsoft.Data.Sqlite;

namespace CoHAnalytics.Tests.ReferenceData;

[Collection(nameof(ReferenceDatabaseCollection))]
public sealed class ItemReferenceCatalogSqliteIntegrityTests
{
    private readonly ReferenceDatabaseFixture _fixture;

    public ItemReferenceCatalogSqliteIntegrityTests(ReferenceDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void Generated_database_matches_expected_production_counts()
    {
        using var connection = new SqliteConnection($"Data Source={_fixture.DatabasePath};Mode=ReadOnly");
        connection.Open();

        Assert.Equal(4659, QueryCount(connection, "SELECT COUNT(*) FROM Item;"));
        Assert.Equal(1797, QueryCount(connection, "SELECT COUNT(*) FROM Item WHERE family = 'Enhancement';"));
        Assert.Equal(873, QueryCount(connection, "SELECT COUNT(*) FROM Item WHERE family = 'Recipe';"));
        Assert.Equal(108, QueryCount(connection, "SELECT COUNT(*) FROM Item WHERE family = 'Salvage';"));
        Assert.Equal(96, QueryCount(connection, "SELECT COUNT(*) FROM Item WHERE family = 'Inspiration';"));
        Assert.Equal(1785, QueryCount(connection, "SELECT COUNT(*) FROM Item WHERE family = 'Badge';"));
        Assert.Equal(1785, QueryCount(connection, "SELECT COUNT(*) FROM Badge;"));
        Assert.Equal(84, QueryCount(connection, "SELECT COUNT(*) FROM Zone;"));
        Assert.Equal(712, QueryCount(connection, "SELECT COUNT(*) FROM BadgeLocation;"));
        Assert.Equal(740, QueryCount(connection, "SELECT COUNT(*) FROM BadgeAccoladeRequirement;"));
        Assert.Equal(230, QueryCount(connection, "SELECT COUNT(*) FROM EnhancementSet;"));
        Assert.Equal(5087, QueryCount(connection, "SELECT COUNT(*) FROM ItemAlias;"));
        Assert.Equal(20835, QueryCount(connection, "SELECT COUNT(*) FROM RecipeLevel;"));
        Assert.Equal(0, QueryCount(connection, "SELECT COUNT(*) FROM RecipeLevel WHERE level > 50;"));
        Assert.Equal(1062, QueryCount(connection, "SELECT COUNT(*) FROM RecipeExcludedSourceLevel;"));
        Assert.Equal(0, QueryCount(connection, "SELECT COUNT(*) FROM RecipeExcludedSourceLevel WHERE level <= 50;"));
        Assert.Equal(1086, QueryCount(connection, "SELECT COUNT(*) FROM EnhancementSetBonus;"));
        Assert.Equal(1138, QueryCount(connection, "SELECT COUNT(*) FROM EnhancementSetBonusPower;"));
        Assert.Equal(ReferenceDatabaseSchema.SchemaVersion, QueryCount(connection, "SELECT schema_version FROM CatalogManifest;"));
        Assert.Equal(2027, QueryCount(connection, "SELECT COUNT(*) FROM ReferenceServerAvailability;"));
        Assert.Equal(1628, QueryCount(connection, """
            SELECT COUNT(*)
            FROM ReferenceServerAvailability
            WHERE entity_kind = 'Item' AND availability_status = 'Current';
            """));
        Assert.Equal(169, QueryCount(connection, """
            SELECT COUNT(*)
            FROM ReferenceServerAvailability
            WHERE entity_kind = 'Item' AND availability_status = 'Historical';
            """));
        Assert.Equal(227, QueryCount(connection, """
            SELECT COUNT(*)
            FROM ReferenceServerAvailability
            WHERE entity_kind = 'EnhancementSet' AND availability_status = 'Current';
            """));
        Assert.Equal(3, QueryCount(connection, """
            SELECT COUNT(*)
            FROM ReferenceServerAvailability
            WHERE entity_kind = 'EnhancementSet' AND availability_status = 'Historical';
            """));
    }

    [Fact]
    public void Generated_database_contains_promoted_recipes()
    {
        var catalog = ItemReferenceCatalogFactory.LoadFromDatabaseFile(_fixture.DatabasePath);
        Assert.True(catalog.IsLoaded, catalog.LoadFailureReason);
        Assert.Equal("item-ref-3.1.0", catalog.Manifest!.CatalogVersion);
        Assert.Equal(873, catalog.GetRecipes().Count);
        Assert.True(catalog.TryResolve("Invention: Accuracy (Recipe)", out var recipe));
        Assert.Equal(ReferenceItemFamily.Recipe, recipe.Item.Family);
        Assert.Equal("ENH-00001", recipe.Item.ProducedItemId);
        Assert.All(catalog.GetRecipes(), item => Assert.All(item.ValidLevels, level => Assert.InRange(level, 1, 50)));
    }

    [Fact]
    public void Generated_database_has_no_orphan_aliases_or_set_references()
    {
        using var connection = new SqliteConnection($"Data Source={_fixture.DatabasePath};Mode=ReadOnly");
        connection.Open();

        Assert.Equal(
            0,
            QueryCount(
                connection,
                """
                SELECT COUNT(*)
                FROM ItemAlias alias
                WHERE NOT EXISTS (
                    SELECT 1 FROM Item item WHERE item.catalog_item_id = alias.catalog_item_id);
                """));

        Assert.Equal(
            0,
            QueryCount(
                connection,
                """
                SELECT COUNT(*)
                FROM Item item
                WHERE item.enhancement_set_id IS NOT NULL
                  AND NOT EXISTS (
                    SELECT 1 FROM EnhancementSet setRow
                    WHERE setRow.catalog_item_id = item.enhancement_set_id);
                """));
    }

    [Fact]
    public void All_item_ids_satisfy_ItemReferenceIdRules()
    {
        using var connection = new SqliteConnection($"Data Source={_fixture.DatabasePath};Mode=ReadOnly");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT catalog_item_id, family FROM Item;";

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var id = reader.GetString(0);
            var family = Enum.Parse<ReferenceItemFamily>(reader.GetString(1), ignoreCase: true);
            Assert.True(
                ItemReferenceIdRules.TryValidateItemId(id, family, out var failure),
                failure);
        }
    }

    [Fact]
    public void Duplicate_normalized_lookup_keys_fail_generation()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"coh-reference-duplicate-{Guid.NewGuid():N}.db");
        try
        {
            var exception = Assert.Throws<InvalidOperationException>(() =>
                ItemReferenceCatalogImporter.ImportFromJsonStream(
                    new MemoryStream(System.Text.Encoding.UTF8.GetBytes("""
                        {
                          "manifest": {
                            "catalogVersion": "item-ref-test",
                            "homecomingCompatibility": { "buildMin": "1", "buildMax": "1" }
                          },
                          "items": [
                            {
                              "catalogItemId": "SAL-00002",
                              "family": "Salvage",
                              "subtype": "Invention",
                              "currentDisplayName": "First",
                              "activeStatus": "Active",
                              "verificationStatus": "VerifiedMultiSource"
                            },
                            {
                              "catalogItemId": "SAL-00003",
                              "family": "Salvage",
                              "subtype": "Invention",
                              "currentDisplayName": "Second",
                              "activeStatus": "Active",
                              "verificationStatus": "VerifiedMultiSource"
                            }
                          ],
                          "aliases": [
                            {
                              "catalogItemId": "SAL-00002",
                              "locale": "en",
                              "text": "Shared Alias",
                              "nameKind": "Display",
                              "isPreferred": true
                            },
                            {
                              "catalogItemId": "SAL-00003",
                              "locale": "en",
                              "text": "Shared Alias",
                              "nameKind": "Display",
                              "isPreferred": true
                            }
                          ],
                          "enhancementSets": []
                        }
                        """)),
                    databasePath));

            Assert.Contains("Shared Alias", exception.Message, StringComparison.Ordinal);
            Assert.False(File.Exists(databasePath));
        }
        finally
        {
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }

    private static int QueryCount(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }
}
