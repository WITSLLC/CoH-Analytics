namespace CoHAnalytics.Tests.Integration;

/// <summary>
/// AppServices composition tests use process-global OS discovery and the shared application-data
/// root, so they must not compete with the parallel suite for lifecycle startup resources.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AppServicesCompositionCollection
{
    public const string Name = "AppServices composition";
}
