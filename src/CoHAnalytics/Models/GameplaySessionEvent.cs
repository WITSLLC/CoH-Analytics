namespace CoHAnalytics.Models;

/// <summary>One parser event committed to a resolved gameplay session in order.</summary>
public sealed record GameplaySessionEvent
{
    public required GameplaySessionId SessionId { get; init; }

    public required MonitoringContextId ContextId { get; init; }

    public CharacterRecordId? CharacterRecordId { get; init; }

    public required ParserEvent ParserEvent { get; init; }

    public required DateTimeOffset CommittedAt { get; init; }

    public required long SessionSequence { get; init; }
}
