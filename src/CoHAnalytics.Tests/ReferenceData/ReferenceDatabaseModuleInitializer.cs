using System.Runtime.CompilerServices;
using CoHAnalytics.ReferenceData;

namespace CoHAnalytics.Tests.ReferenceData;

/// <summary>
/// Generates the derived SQLite reference database before SQLite catalog tests run.
/// Uses the authored production JSON file — the same import path as application generate —
/// rather than a possibly richer or divergent fixture.
/// </summary>
internal static class ReferenceDatabaseModuleInitializer
{
    [ModuleInitializer]
    internal static void EnsureGeneratedReferenceDatabase()
    {
        var databasePath = ReferenceDatabasePaths.GeneratedProductionDatabasePath;
        var directory = Path.GetDirectoryName(databasePath)!;
        Directory.CreateDirectory(directory);

        var catalogJsonPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "CoHAnalytics", "ReferenceData", "item-catalog.v1.json"));
        ItemReferenceCatalogImporter.ImportFromJsonFile(catalogJsonPath, databasePath);
    }
}
