using CoHAnalytics.Orchestration.Models;

namespace CoHAnalytics.Orchestration.Contracts;

public interface IApplicationActionRouter
{
    bool CanInvoke(ApplicationAction action);

    Task<ApplicationActionResult> InvokeAsync(
        ApplicationAction action,
        CancellationToken cancellationToken = default);
}
