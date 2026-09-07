namespace CoHAnalytics.Models;

public sealed class ParserRawEventAvailableEventArgs(ParserRawEvent parserEvent) : EventArgs
{
    public ParserRawEvent ParserEvent { get; } = parserEvent;
}
