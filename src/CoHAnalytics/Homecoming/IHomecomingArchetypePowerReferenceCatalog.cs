namespace CoHAnalytics.Homecoming;

/// <summary>
/// Canonical Homecoming archetype primary/secondary power ownership from live <c>powers.bin</c>.
/// </summary>
public interface IHomecomingArchetypePowerReferenceCatalog
{
    bool IsLoaded { get; }

    bool TryResolvePower(
        string categoryGroup,
        string? powersetToken,
        string powerName,
        out HomecomingArchetypePowerResolution resolution);
}

public readonly record struct HomecomingArchetypePowerResolution(
    string CategoryGroup,
    string PowersetId,
    string PowersetDisplayName,
    string PowerName);
