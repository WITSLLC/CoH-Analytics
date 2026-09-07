namespace CoHAnalytics.Models;

public sealed record ApplicationActivityEntry(
    DateTimeOffset Timestamp,
    string EventType,
    string DisplayMessage);
