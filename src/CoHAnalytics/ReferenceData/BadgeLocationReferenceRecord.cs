namespace CoHAnalytics.ReferenceData;

/// <summary>Physical or surveyed badge/plaque location in the reference catalog.</summary>
public sealed record BadgeLocationReferenceRecord
{
    public required string BadgeCatalogItemId { get; init; }

    public required string ZoneId { get; init; }

    public double? CoordinateX { get; init; }

    public double? CoordinateY { get; init; }

    public double? CoordinateZ { get; init; }

    public string? ThumbtackCommand { get; init; }

    public string? MarkerType { get; init; }

    public string? LocationRole { get; init; }

    public string? CoordinateSemantics { get; init; }

    public required ReferenceVerificationStatus VerificationStatus { get; init; }

    public required int LocationIndex { get; init; }

    public string? TriggerDescription { get; init; }

    public int? ExplorationRouteOrder { get; init; }

    public string? RouteSourceProject { get; init; }

    public string? RouteSourceVersion { get; init; }

    public string? RouteSourceUrl { get; init; }

    public string? RouteMappingConfidence { get; init; }
}
