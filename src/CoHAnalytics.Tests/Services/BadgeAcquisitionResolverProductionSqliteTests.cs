using CoHAnalytics.Models;
using CoHAnalytics.ReferenceData;
using CoHAnalytics.Services;
using CoHAnalytics.Tests.ReferenceData;

namespace CoHAnalytics.Tests.Services;

/// <summary>
/// Resolves live badge award titles against the generated production SQLite database,
/// not the embedded JSON catalog alone.
/// </summary>
[Collection(nameof(ReferenceDatabaseCollection))]
public sealed class BadgeAcquisitionResolverProductionSqliteTests
{
    private readonly ReferenceDatabaseFixture _fixture;

    public BadgeAcquisitionResolverProductionSqliteTests(ReferenceDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Theory]
    [InlineData("Hero Corps Insider", "BAD-01921")]
    [InlineData("Patriot", "BAD-01922")]
    [InlineData("Freedom", "BAD-01924")]
    [InlineData("Collector", "BAD-02061")]
    [InlineData("Salesman", "BAD-01936")]
    [InlineData("Saleswoman", "BAD-01936")]
    public void Live_slash_and_gender_receipt_titles_resolve_via_generated_production_sqlite(
        string observedTitle,
        string expectedCatalogItemId)
    {
        var catalog = ItemReferenceCatalogFactory.LoadFromDatabaseFile(_fixture.DatabasePath);
        Assert.True(catalog.IsLoaded, catalog.LoadFailureReason);

        var resolver = new BadgeAcquisitionResolver(catalog);
        var result = resolver.Resolve(observedTitle);

        Assert.Equal(AcquisitionIdentityResolutionState.Resolved, result.ResolutionState);
        Assert.Equal(expectedCatalogItemId, result.ResolvedCatalogItemId);
        Assert.True(catalog.TryGetById(expectedCatalogItemId, out var item));
        Assert.Equal(ReferenceItemFamily.Badge, item.Family);
    }

    [Fact]
    public void Previously_working_exact_display_receipts_still_resolve_via_generated_sqlite()
    {
        var catalog = ItemReferenceCatalogFactory.LoadFromDatabaseFile(_fixture.DatabasePath);
        Assert.True(catalog.IsLoaded, catalog.LoadFailureReason);

        var resolver = new BadgeAcquisitionResolver(catalog);

        Assert.Equal("BAD-01917", resolver.Resolve("Atlas Tour Guide").ResolvedCatalogItemId);
        Assert.Equal("BAD-02574", resolver.Resolve("Passport").ResolvedCatalogItemId);
        Assert.Equal("BAD-01576", resolver.Resolve("Home Sweet Home").ResolvedCatalogItemId);
        Assert.Equal("BAD-01930", resolver.Resolve("Broker").ResolvedCatalogItemId);
    }

    [Fact]
    public void Generated_production_sqlite_contains_single_name_log_receipt_alias_rows()
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection(
            $"Data Source={_fixture.DatabasePath};Mode=ReadOnly");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT lookup_key, catalog_item_id, text, name_kind
            FROM ItemAlias
            WHERE lookup_key IN (
                'HERO CORPS INSIDER',
                'PATRIOT',
                'FREEDOM',
                'COLLECTOR',
                'SALESMAN',
                'SALESWOMAN')
            ORDER BY lookup_key;
            """;

        using var reader = command.ExecuteReader();
        var rows = new Dictionary<string, (string CatalogItemId, string Text, string NameKind)>(
            StringComparer.Ordinal);
        while (reader.Read())
        {
            rows[reader.GetString(0)] = (reader.GetString(1), reader.GetString(2), reader.GetString(3));
        }

        Assert.Equal(6, rows.Count);
        Assert.Equal(("BAD-01921", "Hero Corps Insider", "LogReceipt"), rows["HERO CORPS INSIDER"]);
        Assert.Equal(("BAD-01922", "Patriot", "LogReceipt"), rows["PATRIOT"]);
        Assert.Equal(("BAD-01924", "Freedom", "LogReceipt"), rows["FREEDOM"]);
        Assert.Equal(("BAD-02061", "Collector", "LogReceipt"), rows["COLLECTOR"]);
        Assert.Equal(("BAD-01936", "Salesman", "LogReceipt"), rows["SALESMAN"]);
        Assert.Equal(("BAD-01936", "Saleswoman", "LogReceipt"), rows["SALESWOMAN"]);
    }
}
