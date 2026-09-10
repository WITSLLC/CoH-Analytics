namespace CoHAnalytics.Homecoming;

/// <summary>Canonical presentation metadata for powers in the installed Homecoming client.</summary>
public interface IHomecomingPowerReferenceCatalog
{
    bool IsLoaded { get; }

    bool TryResolve(
        string categoryId,
        string powersetId,
        string powerId,
        out HomecomingPowerReference power);
}

public readonly record struct HomecomingPowerReference(
    string CategoryId,
    string PowersetId,
    string PowerId,
    string PowersetDisplayName,
    string PowerDisplayName,
    string? DisplayHelp,
    string? IconIdentity,
    bool IsAutoIssued,
    bool IsFree,
    HomecomingPowerType PowerType)
{
    public HomecomingPowerReference(
        string categoryId,
        string powersetId,
        string powerId,
        string powersetDisplayName,
        string powerDisplayName,
        string? iconIdentity,
        bool isAutoIssued,
        bool isFree,
        HomecomingPowerType powerType)
        : this(
            categoryId,
            powersetId,
            powerId,
            powersetDisplayName,
            powerDisplayName,
            null,
            iconIdentity,
            isAutoIssued,
            isFree,
            powerType)
    {
    }
}

public enum HomecomingPowerType
{
    Unknown = -1,
    Click = 0,
    Auto = 1,
    Toggle = 2,
    Boost = 3,
    Inspiration = 4,
    GlobalBoost = 5
}
