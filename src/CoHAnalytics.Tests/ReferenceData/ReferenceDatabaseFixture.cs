using CoHAnalytics.ReferenceData;

namespace CoHAnalytics.Tests.ReferenceData;

public static class ReferenceDatabasePaths
{
    public static string GeneratedProductionDatabasePath =>
        Path.Combine(
            AppContext.BaseDirectory,
            ItemReferenceCatalogFactory.ProductionDatabaseRelativePath.Replace('/', Path.DirectorySeparatorChar));
}

public sealed class ReferenceDatabaseFixture
{
    public string DatabasePath { get; }

    public ReferenceDatabaseFixture()
    {
        DatabasePath = ReferenceDatabasePaths.GeneratedProductionDatabasePath;
    }
}

[CollectionDefinition(nameof(ReferenceDatabaseCollection))]
public sealed class ReferenceDatabaseCollection : ICollectionFixture<ReferenceDatabaseFixture>
{
}
