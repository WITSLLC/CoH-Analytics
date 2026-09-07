using CommunityToolkit.Mvvm.ComponentModel;

namespace CoHAnalytics.ViewModels.Workspaces;

public abstract partial class WorkspaceViewModelBase : ObservableObject
{
    public abstract string Title { get; }
}
