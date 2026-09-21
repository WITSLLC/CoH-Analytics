using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using CoHAnalytics.ReferenceData;

namespace CoHAnalytics.ReferenceDataGenerator;

internal static class RouteOrderingPromotionCommand
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    internal static int Run(string[] args, TextWriter output, TextWriter error)
    {
        if (!TryParseArgs(args, out var catalogPath, out var routeOrderingPath, out var failureReason))
        {
            error.WriteLine(failureReason);
            error.WriteLine(
                "Usage: promote-route-ordering --catalog <item-catalog.v1.json> --route-ordering <route-ordering.json>");
            return 1;
        }

        try
        {
            var package = RouteOrderingPackageLoader.Load(routeOrderingPath);
            using var stream = File.OpenRead(catalogPath);
            var document = JsonSerializer.Deserialize<ItemReferenceCatalogDocument>(stream, ReadOptions)
                ?? throw new InvalidOperationException("Catalog document is empty.");

            var result = RouteOrderingPromotionSupport.Apply(document, package);
            var json = JsonSerializer.Serialize(document, WriteOptions);
            var tempPath = catalogPath + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, catalogPath, overwrite: true);

            output.WriteLine(
                $"Promoted route ordering into '{catalogPath}'. "
                + $"Exploration zones={result.ExplorationZonesApplied}, stops={result.ExplorationStopsApplied}; "
                + $"History collections={result.HistoryPlaqueCollectionsApplied}, stops={result.HistoryPlaqueStopsApplied}.");
            return 0;
        }
        catch (Exception exception) when (
            exception is RouteOrderingPackageLoader.RouteOrderingPackageException
                or IOException
                or JsonException
                or InvalidOperationException)
        {
            error.WriteLine($"Route ordering promotion: FAIL — {exception.Message}");
            return 1;
        }
    }

    private static bool TryParseArgs(
        string[] args,
        out string catalogPath,
        out string routeOrderingPath,
        out string failureReason)
    {
        catalogPath = string.Empty;
        routeOrderingPath = string.Empty;
        failureReason = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--catalog" when index + 1 < args.Length:
                    catalogPath = args[++index];
                    break;
                case "--route-ordering" when index + 1 < args.Length:
                    routeOrderingPath = args[++index];
                    break;
                default:
                    failureReason = $"Unknown or incomplete argument '{args[index]}'.";
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(catalogPath) || string.IsNullOrWhiteSpace(routeOrderingPath))
        {
            failureReason = "Both --catalog and --route-ordering are required.";
            return false;
        }

        if (!File.Exists(catalogPath))
        {
            failureReason = $"Catalog file '{catalogPath}' was not found.";
            return false;
        }

        if (!File.Exists(routeOrderingPath))
        {
            failureReason = $"Route ordering file '{routeOrderingPath}' was not found.";
            return false;
        }

        return true;
    }
}
