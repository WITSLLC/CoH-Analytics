using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

public sealed class ViewedContextChangedEventArgs : EventArgs
{
    public required ViewedContextState State { get; init; }
}
