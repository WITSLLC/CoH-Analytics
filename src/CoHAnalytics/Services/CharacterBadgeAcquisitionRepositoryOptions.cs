namespace CoHAnalytics.Services;

public sealed class CharacterBadgeAcquisitionRepositoryOptions
{
    /// <summary>
    /// Optional override for <see cref="ApplicationDataPaths.GetApplicationRoot"/>.
    /// Badge acquisitions persist under <see cref="ApplicationDataPaths.GetCharactersRoot"/>.
    /// </summary>
    public string? DataDirectory { get; init; }

    public TimeProvider? TimeProvider { get; init; }

    public bool SimulatePersistenceFailure { get; init; }
}
