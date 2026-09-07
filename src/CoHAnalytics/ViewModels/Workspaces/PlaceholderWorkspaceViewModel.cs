using CoHAnalytics.Orchestration.Contracts;
using CoHAnalytics.Services;

namespace CoHAnalytics.ViewModels.Workspaces;

public sealed class PlaceholderWorkspaceViewModel : WorkspaceEnvironmentStatusViewModelBase, IDisposable
{
    private bool _disposed;

    public PlaceholderWorkspaceViewModel(
        string title,
        string description,
        IApplicationOrchestrator orchestrator,
        IGameRuntimeService gameRuntimeService)
        : base(orchestrator, gameRuntimeService)
    {
        Title = title;
        Description = description;
        WireEnvironmentStatus();
    }

    public override string Title { get; }

    public string Description { get; }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        UnwireEnvironmentStatus();
    }
}
