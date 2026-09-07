namespace CoHAnalytics.Models;

public sealed class GameplaySessionManagerChangedEventArgs(GameplaySessionManagerSnapshot snapshot) : EventArgs
{
    public GameplaySessionManagerSnapshot Snapshot { get; } = snapshot;
}
