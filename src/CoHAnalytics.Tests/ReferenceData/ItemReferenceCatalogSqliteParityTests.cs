using CoHAnalytics.ReferenceData;

namespace CoHAnalytics.Tests.ReferenceData;

[Collection(nameof(ReferenceDatabaseCollection))]
public sealed class ItemReferenceCatalogSqliteParityTests
{
    private readonly ReferenceDatabaseFixture _fixture;

    public ItemReferenceCatalogSqliteParityTests(ReferenceDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void Full_production_alias_parity_between_json_and_sqlite()
    {
        var jsonCatalog = ItemReferenceCatalogFactory.LoadEmbeddedProduction();
        var sqliteCatalog = ItemReferenceCatalogFactory.LoadFromDatabaseFile(_fixture.DatabasePath);

        Assert.True(jsonCatalog.IsLoaded, jsonCatalog.LoadFailureReason);
        Assert.True(sqliteCatalog.IsLoaded, sqliteCatalog.LoadFailureReason);

        var assembly = typeof(ItemReferenceCatalogFactory).Assembly;
        using var stream = assembly.GetManifestResourceStream(ItemReferenceCatalogFactory.ProductionCatalogResourceName);
        Assert.NotNull(stream);

        var loadResult = ItemReferenceCatalogLoader.Load(stream);
        Assert.True(loadResult.Succeeded);
        Assert.NotNull(loadResult.AliasIndex);

        var parityFailures = 0;
        foreach (var (_, aliasMatch) in loadResult.AliasIndex!)
        {
            if (!AssertAliasParity(jsonCatalog, sqliteCatalog, aliasMatch.MatchedAliasText))
            {
                parityFailures++;
            }
        }

        Assert.Equal(0, parityFailures);
        Assert.Equal(5087, loadResult.AllAliasIndex!.Count);
        Assert.Equal(4908, loadResult.AliasIndex!.Count);
    }

    [Fact]
    public void Production_factory_loads_generated_sqlite_database()
    {
        var catalog = ItemReferenceCatalogFactory.LoadProductionDatabase();

        Assert.True(catalog.IsLoaded, catalog.LoadFailureReason);
        Assert.True(catalog.TryResolve("Power of Grey (Endurance Reduction)", out var resolution));
        Assert.True(ReferenceServerAvailabilitySupport.IsCurrentHomecoming(resolution.Item.ServerAvailability));
        Assert.NotEqual("ENH-00847", resolution.Item.CatalogItemId);
    }

    [Fact]
    public void Unknown_lookup_text_matches_between_json_and_sqlite()
    {
        var jsonCatalog = ItemReferenceCatalogFactory.LoadEmbeddedProduction();
        var sqliteCatalog = ItemReferenceCatalogFactory.LoadFromDatabaseFile(_fixture.DatabasePath);

        foreach (var unknown in new[] { "Mystery Thing", string.Empty, "   " })
        {
            var jsonResolved = jsonCatalog.TryResolve(unknown, out _);
            var sqliteResolved = sqliteCatalog.TryResolve(unknown, out _);
            Assert.Equal(jsonResolved, sqliteResolved);
            Assert.False(jsonResolved);
        }
    }

    [Fact]
    public void Native_sqlite_provider_opens_generated_database()
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection(
            $"Data Source={_fixture.DatabasePath};Mode=ReadOnly");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Item;";
        var count = Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(4659, count);
    }

    private static bool AssertAliasParity(
        IItemReferenceCatalog jsonCatalog,
        IItemReferenceCatalog sqliteCatalog,
        string observedText)
    {
        var jsonResolved = jsonCatalog.TryResolve(observedText, out var jsonResolution);
        var sqliteResolved = sqliteCatalog.TryResolve(observedText, out var sqliteResolution);

        if (jsonResolved != sqliteResolved)
        {
            return false;
        }

        if (!jsonResolved)
        {
            return true;
        }

        return string.Equals(jsonResolution.Item.CatalogItemId, sqliteResolution.Item.CatalogItemId, StringComparison.Ordinal)
            && jsonResolution.Item.Family == sqliteResolution.Item.Family
            && string.Equals(jsonResolution.Item.Subtype, sqliteResolution.Item.Subtype, StringComparison.Ordinal)
            && string.Equals(jsonResolution.Item.CurrentDisplayName, sqliteResolution.Item.CurrentDisplayName, StringComparison.Ordinal)
            && string.Equals(jsonResolution.Item.Rarity, sqliteResolution.Item.Rarity, StringComparison.Ordinal)
            && string.Equals(jsonResolution.Item.Origin, sqliteResolution.Item.Origin, StringComparison.Ordinal)
            && string.Equals(jsonResolution.Item.Tier, sqliteResolution.Item.Tier, StringComparison.Ordinal)
            && string.Equals(jsonResolution.Item.EnhancementSetId, sqliteResolution.Item.EnhancementSetId, StringComparison.Ordinal)
            && string.Equals(jsonResolution.Item.Variant, sqliteResolution.Item.Variant, StringComparison.Ordinal)
            && jsonResolution.Item.ActiveStatus == sqliteResolution.Item.ActiveStatus
            && jsonResolution.Item.VerificationStatus == sqliteResolution.Item.VerificationStatus
            && string.Equals(jsonResolution.CatalogVersion, sqliteResolution.CatalogVersion, StringComparison.Ordinal)
            && string.Equals(jsonResolution.MatchedAliasText, sqliteResolution.MatchedAliasText, StringComparison.Ordinal);
    }
}
