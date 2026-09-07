namespace CoHAnalytics.ReferenceData;

/// <summary>Loads shipped item reference catalogs from production and embedded test resources.</summary>
public static class ItemReferenceCatalogFactory
{
    public const string ProductionDatabaseRelativePath = "ReferenceData/reference.db";

    public const string ProductionCatalogResourceName = "CoHAnalytics.ReferenceData.item-catalog.v1.json";

    public const string BootstrapCatalogResourceName = "CoHAnalytics.ReferenceData.item-catalog-bootstrap.v1.json";

    public const int ReferenceDatabaseSchemaVersion = ReferenceDatabaseSchema.SchemaVersion;

    /// <summary>Loads the production reconciled catalog embedded with the application.</summary>
    public static IItemReferenceCatalog LoadEmbeddedProduction()
    {
        return LoadEmbedded(ProductionCatalogResourceName);
    }

    /// <summary>
    /// Loads the embedded production catalog plus canonical Enhancement resolver NamedTables.
    /// </summary>
    public static EnhancementHelpResolverProductionInputs LoadEmbeddedProductionResolverInputs()
    {
        var assembly = typeof(ItemReferenceCatalogFactory).Assembly;
        using var stream = assembly.GetManifestResourceStream(ProductionCatalogResourceName);
        if (stream is null)
        {
            return new EnhancementHelpResolverProductionInputs(
                new FailedItemReferenceCatalog(
                    $"Embedded catalog resource '{ProductionCatalogResourceName}' was not found."),
                null);
        }

        var result = ItemReferenceCatalogLoader.Load(stream);
        return new EnhancementHelpResolverProductionInputs(
            ItemReferenceCatalog.FromLoadResult(result),
            result.EnhancementResolverNamedTables);
    }

    /// <summary>Creates a static Enhancement help resolver backed by embedded production NamedTables.</summary>
    public static IEnhancementHelpResolver CreateEmbeddedProductionResolver()
    {
        var inputs = LoadEmbeddedProductionResolverInputs();
        if (!inputs.Catalog.IsLoaded || inputs.NamedTables is null)
        {
            throw new InvalidOperationException(
                inputs.Catalog.LoadFailureReason ?? "Production resolver inputs failed to load.");
        }

        return new EnhancementHelpResolver(inputs.NamedTables);
    }

    /// <summary>Loads the small Reference 1 bootstrap fixture catalog used by loader unit tests.</summary>
    public static IItemReferenceCatalog LoadEmbeddedBootstrap()
    {
        return LoadEmbedded(BootstrapCatalogResourceName);
    }

    private static IItemReferenceCatalog LoadEmbedded(string resourceName)
    {
        var assembly = typeof(ItemReferenceCatalogFactory).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            return new FailedItemReferenceCatalog(
                $"Embedded catalog resource '{resourceName}' was not found.");
        }

        return Load(stream);
    }

    internal static IItemReferenceCatalog Load(Stream stream)
    {
        var result = ItemReferenceCatalogLoader.Load(stream);
        return ItemReferenceCatalog.FromLoadResult(result);
    }

    internal static IItemReferenceCatalog LoadFromString(string json)
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
        return Load(stream);
    }

    /// <summary>Loads the generated production SQLite database from the application directory.</summary>
    public static IItemReferenceCatalog LoadProductionDatabase() =>
        LoadFromDatabaseFile(Path.Combine(
            AppContext.BaseDirectory,
            ProductionDatabaseRelativePath.Replace('/', Path.DirectorySeparatorChar)));

    /// <summary>Loads a generated SQLite reference database from disk.</summary>
    public static IItemReferenceCatalog LoadFromDatabaseFile(string databasePath) =>
        new SqliteReferenceStore(databasePath);

    /// <summary>
    /// Loads a generated SQLite reference database copied from an embedded resource when present.
    /// Not used by production runtime in Slice 0.
    /// </summary>
    public static IItemReferenceCatalog LoadEmbeddedSqlite(string resourceName)
    {
        var assembly = typeof(ItemReferenceCatalogFactory).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            return new FailedItemReferenceCatalog($"Embedded database resource '{resourceName}' was not found.");
        }

        var tempPath = Path.Combine(Path.GetTempPath(), $"coh-reference-{Guid.NewGuid():N}.db");
        try
        {
            using (var fileStream = File.Create(tempPath))
            {
                stream.CopyTo(fileStream);
            }

            return LoadFromDatabaseFile(tempPath);
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch
            {
            }
        }
    }
}
