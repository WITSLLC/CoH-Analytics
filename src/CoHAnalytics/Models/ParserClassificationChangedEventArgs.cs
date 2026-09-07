namespace CoHAnalytics.Models;

public sealed class ParserClassificationChangedEventArgs(ParserClassificationSnapshot snapshot) : EventArgs
{
    public ParserClassificationSnapshot Snapshot { get; } = snapshot;
}
