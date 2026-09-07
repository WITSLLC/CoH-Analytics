namespace CoHAnalytics.Models;

public sealed class ParserManagerChangedEventArgs(ParserManagerSnapshot snapshot) : EventArgs
{
    public ParserManagerSnapshot Snapshot { get; } = snapshot;
}
