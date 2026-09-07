using System.Diagnostics;
using System.IO;
using System.Text.Json;
using CoHAnalytics.ReferenceData;

namespace CoHAnalytics.ReferenceDataGenerator;

public static class Program
{
    private const int LoadIterations = 30;
    private const int LookupPasses = 500;

    public static int Main(string[] args)
    {
        try
        {
            return args.FirstOrDefault()?.ToLowerInvariant() switch
            {
                "generate" when args.Length >= 2 => Generate(args[1..]),
                "benchmark" when args.Length == 2 => Benchmark(args[1]),
                "import-homecoming" => HomecomingImportCommand.Run(args[1..], Console.Out, Console.Error),
                "promote-homecoming-enhancements" => HomecomingEnhancementPromotionCommand.Run(
                    args[1..],
                    Console.Out,
                    Console.Error),
                "promote-homecoming-recipes" => HomecomingRecipePromotionCommand.Run(
                    args[1..],
                    Console.Out,
                    Console.Error),
                "promote-homecoming-badges" => HomecomingBadgePromotionCommand.Run(
                    args[1..],
                    Console.Out,
                    Console.Error),
                "sync-badge-log-receipt-aliases" => HomecomingBadgeLogReceiptAliasSyncCommand.Run(
                    args[1..],
                    Console.Out,
                    Console.Error),
                "promote-homecoming-inspirations" => HomecomingInspirationPromotionCommand.Run(
                    args[1..],
                    Console.Out,
                    Console.Error),
                "enrich-badge-rewards" => BadgeCatalogRewardEnrichmentCommand.Run(
                    args[1..],
                    Console.Out,
                    Console.Error),
                "promote-route-ordering" => RouteOrderingPromotionCommand.Run(
                    args[1..],
                    Console.Out,
                    Console.Error),
                "discover-enhancement-repair-facts" => HomecomingEnhancementRepairDiscoveryCommand.Run(
                    args[1..],
                    Console.Out,
                    Console.Error),
                _ => Usage()
            };
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static int Generate(string[] args)
    {
        if (args.Length < 1 || string.IsNullOrWhiteSpace(args[0]))
        {
            Console.Error.WriteLine(
                "Usage: generate <reference.db> [--catalog <item-catalog.v1.json>]");
            return 2;
        }

        var databasePath = args[0];
        string? catalogPath = null;
        for (var index = 1; index < args.Length; index++)
        {
            var option = args[index];
            if (option is "--catalog" && index + 1 < args.Length)
            {
                catalogPath = args[++index];
                continue;
            }

            Console.Error.WriteLine($"Unknown generate option '{option}'.");
            Console.Error.WriteLine(
                "Usage: generate <reference.db> [--catalog <item-catalog.v1.json>]");
            return 2;
        }

        if (!string.IsNullOrWhiteSpace(catalogPath))
        {
            ItemReferenceCatalogImporter.ImportFromJsonFile(catalogPath, databasePath);
            Console.WriteLine(
                $"Generated SQLite reference catalog from '{Path.GetFullPath(catalogPath)}': {databasePath}");
        }
        else
        {
            ItemReferenceCatalogImporter.ImportEmbeddedProduction(databasePath);
            Console.WriteLine($"Generated SQLite reference catalog: {databasePath}");
        }

        return 0;
    }

    private static int Benchmark(string databasePath)
    {
        if (!File.Exists(databasePath))
        {
            throw new FileNotFoundException("Generated reference database was not found.", databasePath);
        }

        var aliases = ReadProductionAliases();

        _ = ItemReferenceCatalogFactory.LoadEmbeddedProduction();
        _ = ItemReferenceCatalogFactory.LoadFromDatabaseFile(databasePath);

        var jsonLoads = MeasureLoads(ItemReferenceCatalogFactory.LoadEmbeddedProduction);
        var sqliteLoads = MeasureLoads(() => ItemReferenceCatalogFactory.LoadFromDatabaseFile(databasePath));

        var jsonCatalog = ItemReferenceCatalogFactory.LoadEmbeddedProduction();
        var sqliteCatalog = ItemReferenceCatalogFactory.LoadFromDatabaseFile(databasePath);
        EnsureCatalogLoaded(jsonCatalog);
        EnsureCatalogLoaded(sqliteCatalog);

        var jsonLookup = MeasureLookups(jsonCatalog, aliases);
        var sqliteLookup = MeasureLookups(sqliteCatalog, aliases);
        var itemCount = aliases
            .Select(alias => ResolveId(jsonCatalog, alias))
            .Distinct(StringComparer.Ordinal)
            .Count();

        var result = new
        {
            loadIterations = LoadIterations,
            aliasCount = aliases.Count,
            itemCount,
            jsonLoad = Summarize(jsonLoads),
            sqliteLoad = Summarize(sqliteLoads),
            lookupPasses = LookupPasses,
            lookupsPerformed = aliases.Count * LookupPasses,
            jsonLookup,
            sqliteLookup
        };

        Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }

    private static List<LoadSample> MeasureLoads(Func<IItemReferenceCatalog> load)
    {
        var samples = new List<LoadSample>(LoadIterations);
        for (var iteration = 0; iteration < LoadIterations; iteration++)
        {
            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var stopwatch = Stopwatch.StartNew();
            var catalog = load();
            stopwatch.Stop();
            var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            EnsureCatalogLoaded(catalog);
            samples.Add(new LoadSample(stopwatch.Elapsed.TotalMilliseconds, allocatedBytes));
        }

        return samples;
    }

    private static LookupResult MeasureLookups(IItemReferenceCatalog catalog, IReadOnlyList<string> aliases)
    {
        foreach (var alias in aliases)
        {
            if (!catalog.TryResolve(alias, out _))
            {
                throw new InvalidOperationException($"Warm-up lookup failed for '{alias}'.");
            }
        }

        var resolved = 0;
        var stopwatch = Stopwatch.StartNew();
        for (var pass = 0; pass < LookupPasses; pass++)
        {
            foreach (var alias in aliases)
            {
                if (catalog.TryResolve(alias, out _))
                {
                    resolved++;
                }
            }
        }

        stopwatch.Stop();
        var seconds = stopwatch.Elapsed.TotalSeconds;
        return new LookupResult(
            resolved,
            stopwatch.Elapsed.TotalMilliseconds,
            resolved / seconds);
    }

    private static IReadOnlyList<string> ReadProductionAliases()
    {
        var assembly = typeof(ItemReferenceCatalogFactory).Assembly;
        using var stream = assembly.GetManifestResourceStream(ItemReferenceCatalogFactory.ProductionCatalogResourceName)
            ?? throw new InvalidOperationException("Embedded production JSON was not found.");
        using var document = JsonDocument.Parse(stream);
        return document.RootElement
            .GetProperty("aliases")
            .EnumerateArray()
            .Select(alias => alias.GetProperty("text").GetString())
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Select(text => text!)
            .ToArray();
    }

    private static string ResolveId(IItemReferenceCatalog catalog, string alias)
    {
        if (!catalog.TryResolve(alias, out var resolution))
        {
            throw new InvalidOperationException($"Lookup failed for '{alias}'.");
        }

        return resolution.Item.CatalogItemId;
    }

    private static void EnsureCatalogLoaded(IItemReferenceCatalog catalog)
    {
        if (!catalog.IsLoaded)
        {
            throw new InvalidOperationException(catalog.LoadFailureReason ?? "Reference catalog failed to load.");
        }
    }

    private static LoadSummary Summarize(IReadOnlyList<LoadSample> samples)
    {
        var times = samples.Select(sample => sample.ElapsedMilliseconds).Order().ToArray();
        var allocations = samples.Select(sample => sample.AllocatedBytes).Order().ToArray();
        return new LoadSummary(
            Median(times),
            samples.Average(sample => sample.ElapsedMilliseconds),
            samples.Max(sample => sample.ElapsedMilliseconds),
            Median(allocations));
    }

    private static double Median(IReadOnlyList<double> sorted)
    {
        var middle = sorted.Count / 2;
        return sorted.Count % 2 == 0
            ? (sorted[middle - 1] + sorted[middle]) / 2
            : sorted[middle];
    }

    private static double Median(IReadOnlyList<long> sorted)
    {
        var middle = sorted.Count / 2;
        return sorted.Count % 2 == 0
            ? (sorted[middle - 1] + sorted[middle]) / 2d
            : sorted[middle];
    }

    private static int Usage()
    {
        Console.Error.WriteLine(
            "Usage: generate <reference.db> [--catalog <item-catalog.v1.json>] | benchmark <reference.db> | import-homecoming ... | promote-homecoming-enhancements ... | promote-homecoming-recipes ... | promote-homecoming-badges ... | promote-homecoming-inspirations ... | discover-enhancement-repair-facts ...");
        HomecomingImportCommand.WriteUsage(Console.Error);
        return 2;
    }

    private sealed record LoadSample(double ElapsedMilliseconds, long AllocatedBytes);

    private sealed record LoadSummary(
        double MedianMilliseconds,
        double AverageMilliseconds,
        double WorstMilliseconds,
        double MedianManagedAllocatedBytes);

    private sealed record LookupResult(int Resolved, double ElapsedMilliseconds, double LookupsPerSecond);
}
