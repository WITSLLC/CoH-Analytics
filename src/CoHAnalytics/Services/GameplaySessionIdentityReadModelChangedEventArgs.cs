using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

public sealed class GameplaySessionIdentityReadModelChangedEventArgs : EventArgs
{
    public required GameplaySessionIdentityReadModelSnapshot Snapshot { get; init; }
}
