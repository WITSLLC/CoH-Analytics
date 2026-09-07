namespace CoHAnalytics.ReferenceData;

/// <summary>
/// Whether a record is present in a server's current canonical dataset or retained
/// only as a stable historical identity.
/// </summary>
public enum ReferenceServerAvailabilityStatus
{
    Current,
    Historical
}
