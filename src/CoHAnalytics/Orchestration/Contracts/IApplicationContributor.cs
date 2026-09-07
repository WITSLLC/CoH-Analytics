namespace CoHAnalytics.Orchestration.Contracts;

public interface IApplicationContributor
{
    Models.ApplicationContributorDescriptor Descriptor { get; }

    Task<Models.ApplicationContribution> GetContributionAsync(
        CancellationToken cancellationToken = default);

    event EventHandler? ContributionChanged;
}
