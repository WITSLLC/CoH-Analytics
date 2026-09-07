namespace CoHAnalytics.Homecoming;

/// <summary>
/// Resolves canonical Homecoming boost metadata needed for Enhancement icon composition.
/// </summary>
public interface IHomecomingBoostMetadataProvider
{
    IReadOnlyList<string>? TryGetBoostsAllowed(string? homecomingSourceId);
}
