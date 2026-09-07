namespace CoHAnalytics.Models;

public sealed class ParserEventsClassifiedEventArgs(IReadOnlyList<ParserEvent> events) : EventArgs
{
    public IReadOnlyList<ParserEvent> Events { get; } = [.. events];
}
