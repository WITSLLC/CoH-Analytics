using CoHAnalytics.ReferenceData;
using Microsoft.Data.Sqlite;

namespace CoHAnalytics.Tests.ReferenceData;

public sealed class ItemReferenceCatalogSqliteFailureTests
{
    [Fact]
    public void Missing_database_returns_failed_catalog_without_throwing()
    {
        var catalog = ItemReferenceCatalogFactory.LoadFromDatabaseFile(
            Path.Combine(Path.GetTempPath(), $"missing-reference-{Guid.NewGuid():N}.db"));

        Assert.False(catalog.IsLoaded);
        Assert.NotNull(catalog.LoadFailureReason);
        Assert.Contains("not found", catalog.LoadFailureReason!, StringComparison.OrdinalIgnoreCase);
        Assert.False(catalog.TryResolve("Anything", out _));
    }

    [Fact]
    public void Corrupt_database_returns_failed_catalog_without_throwing()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"corrupt-reference-{Guid.NewGuid():N}.db");
        try
        {
            File.WriteAllText(databasePath, "not a sqlite database");

            var catalog = ItemReferenceCatalogFactory.LoadFromDatabaseFile(databasePath);

            Assert.False(catalog.IsLoaded);
            Assert.NotNull(catalog.LoadFailureReason);
            Assert.False(catalog.TryResolve("Anything", out _));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }

    [Fact]
    public void Unsupported_schema_version_returns_failed_catalog_without_throwing()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"schema-reference-{Guid.NewGuid():N}.db");
        try
        {
            using (var connection = new SqliteConnection($"Data Source={databasePath}"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText =
                    """
                    CREATE TABLE CatalogManifest (
                        schema_version INTEGER NOT NULL PRIMARY KEY,
                        catalog_version TEXT NOT NULL,
                        homecoming_build_min TEXT,
                        homecoming_build_max TEXT,
                        source_revision TEXT,
                        source_notes TEXT,
                        generated_at_utc TEXT NOT NULL);
                    INSERT INTO CatalogManifest (
                        schema_version,
                        catalog_version,
                        generated_at_utc)
                    VALUES (99, 'unsupported', '2026-08-09T00:00:00Z');
                    """;
                command.ExecuteNonQuery();
            }

            var catalog = ItemReferenceCatalogFactory.LoadFromDatabaseFile(databasePath);

            Assert.False(catalog.IsLoaded);
            Assert.Contains("Unsupported reference database schema version", catalog.LoadFailureReason!, StringComparison.Ordinal);
            Assert.False(catalog.TryResolve("Luck Charm", out _));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }
}
