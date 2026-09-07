namespace CoHAnalytics.ReferenceData;

/// <summary>Server-specific content availability for a reference catalog record.</summary>
public sealed record ReferenceServerAvailability
{
    public required string ServerKey { get; init; }

    public required ReferenceServerAvailabilityStatus Status { get; init; }
}
