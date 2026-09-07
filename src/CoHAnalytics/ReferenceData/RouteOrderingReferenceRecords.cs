namespace CoHAnalytics.ReferenceData;

/// <summary>Provenance for supplemental route-ordering research integrated into the catalog.</summary>
public sealed record RouteOrderingProvenanceRecord
{
    public required string SourceName { get; init; }

    public required string Author { get; init; }

    public required string Version { get; init; }

    public string? MapsThreadUrl { get; init; }

    public string? PopmenuThreadUrl { get; init; }

    public string? OrderingSemantics { get; init; }

    public string? Retrieved { get; init; }
}

/// <summary>History Plaque collection route metadata from published zone segments.</summary>
public sealed record HistoryPlaqueRouteCollectionRecord
{
    public required string CollectionName { get; init; }

    public required string CompletionBadgeId { get; init; }

    public required bool PublishedCollectionOrderAvailable { get; init; }

    public required string OrderingStatus { get; init; }

    public string? OrderingNotes { get; init; }
}

/// <summary>Individual History Plaque stop within a collection route segment.</summary>
public sealed record HistoryPlaqueRouteStopRecord
{
    public required string CollectionName { get; init; }

    public required int InventoryOrder { get; init; }

    public int? RouteOrder { get; init; }

    public required string CompletionBadgeId { get; init; }

    public required string ZoneId { get; init; }

    public required int LocationIndex { get; init; }

    public string? PlaqueName { get; init; }

    public int? SourceZoneRouteOrder { get; init; }

    public string? RouteMappingConfidence { get; init; }

    public string? RouteSourceProject { get; init; }

    public string? RouteSourceVersion { get; init; }

    public string? RouteSourceUrl { get; init; }
}
