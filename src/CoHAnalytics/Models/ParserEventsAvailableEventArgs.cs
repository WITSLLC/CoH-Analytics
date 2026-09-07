namespace CoHAnalytics.Models;

public sealed class ParserEventsAvailableEventArgs(IReadOnlyList<ParserRawEvent> events) : EventArgs
{
    public IReadOnlyList<ParserRawEvent> Events { get; } = [.. events];
}
