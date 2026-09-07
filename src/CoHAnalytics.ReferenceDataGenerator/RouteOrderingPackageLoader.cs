using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using CoHAnalytics.ReferenceData;

namespace CoHAnalytics.ReferenceDataGenerator;

internal static class RouteOrderingPackageLoader
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    internal static RouteOrderingPackage Load(string routeOrderingPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(routeOrderingPath);
        var fullPath = Path.GetFullPath(routeOrderingPath);
        if (!File.Exists(fullPath))
        {
            throw new RouteOrderingPackageException($"Route ordering file '{fullPath}' was not found.");
        }

        using var stream = File.OpenRead(fullPath);
        var document = JsonSerializer.Deserialize<RouteOrderingPackageDocument>(stream, ReadOptions)
            ?? throw new RouteOrderingPackageException("Route ordering document is empty.");

        if (document.Source is null)
        {
            throw new RouteOrderingPackageException("Route ordering document is missing source metadata.");
        }

        return new RouteOrderingPackage(
            document.Source,
            document.Exploration ?? [],
            document.HistoryPlaques ?? []);
    }

    internal sealed class RouteOrderingPackageException(string message) : Exception(message);
}

internal sealed record RouteOrderingPackage(
    RouteOrderingSourceDocument Source,
    IReadOnlyList<RouteOrderingExplorationZoneDocument> Exploration,
    IReadOnlyList<RouteOrderingHistoryCollectionDocument> HistoryPlaques);

internal sealed class RouteOrderingPackageDocument
{
    public RouteOrderingSourceDocument? Source { get; set; }

    public List<RouteOrderingExplorationZoneDocument>? Exploration { get; set; }

    public List<RouteOrderingHistoryCollectionDocument>? HistoryPlaques { get; set; }
}

internal sealed class RouteOrderingSourceDocument
{
    public string? Name { get; set; }

    public string? Author { get; set; }

    public string? Version { get; set; }

    public string? MapsThreadUrl { get; set; }

    public string? PopmenuThreadUrl { get; set; }

    public string? OrderingSemantics { get; set; }

    public string? Retrieved { get; set; }
}

internal sealed class RouteOrderingExplorationZoneDocument
{
    public string? ZoneId { get; set; }

    public List<RouteOrderingExplorationStopDocument>? Stops { get; set; }
}

internal sealed class RouteOrderingExplorationStopDocument
{
    public int RouteOrder { get; set; }

    public string? BadgeId { get; set; }

    public string? BadgeName { get; set; }

    public string? HomecomingSourceId { get; set; }

    public string? LocationIdentity { get; set; }

    public int LocationIndex { get; set; }

    public string? SourceProject { get; set; }

    public string? SourceUrl { get; set; }

    public string? SourceVersion { get; set; }

    public string? MappingConfidence { get; set; }
}

internal sealed class RouteOrderingHistoryCollectionDocument
{
    public string? Collection { get; set; }

    public string? CompletionBadgeId { get; set; }

    public bool PublishedCollectionOrderAvailable { get; set; }

    public string? OrderingStatus { get; set; }

    public string? OrderingNotes { get; set; }

    public List<RouteOrderingHistoryStopDocument>? Stops { get; set; }
}

internal sealed class RouteOrderingHistoryStopDocument
{
    public int InventoryOrder { get; set; }

    public int? RouteOrder { get; set; }

    public string? PlaqueName { get; set; }

    public string? PlaqueIdentity { get; set; }

    public string? CompletionBadgeId { get; set; }

    public string? ZoneId { get; set; }

    public int? SourceZoneRouteOrder { get; set; }

    public string? SourceProject { get; set; }

    public string? SourceUrl { get; set; }

    public string? SourceVersion { get; set; }

    public string? MappingConfidence { get; set; }
}
