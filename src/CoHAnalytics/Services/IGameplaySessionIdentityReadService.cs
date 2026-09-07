using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

/// <summary>
/// Publishes a single UI-facing read model for runtime character identity across monitoring contexts.
/// </summary>
public interface IGameplaySessionIdentityReadService
{
    GameplaySessionIdentityReadModelSnapshot Current { get; }

    event EventHandler<GameplaySessionIdentityReadModelChangedEventArgs>? Changed;
}
