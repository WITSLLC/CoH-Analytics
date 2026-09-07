namespace CoHAnalytics.Models;

public sealed class ParserWorkerChangedEventArgs(ParserWorkerSnapshot snapshot) : EventArgs
{
    public ParserWorkerSnapshot Snapshot { get; } = snapshot;
}
