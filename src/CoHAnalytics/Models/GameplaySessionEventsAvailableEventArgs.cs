namespace CoHAnalytics.Models;

public sealed class GameplaySessionEventsAvailableEventArgs(IReadOnlyList<GameplaySessionEvent> events) : EventArgs
{
    public IReadOnlyList<GameplaySessionEvent> Events { get; } = [.. events];
}
