using CoHAnalytics.Orchestration.Models;

namespace CoHAnalytics.Orchestration.Contracts;

public interface IApplicationActionHandler
{
    Task<ApplicationActionResult> HandleAsync(
        ApplicationAction action,
        CancellationToken cancellationToken = default);
}
