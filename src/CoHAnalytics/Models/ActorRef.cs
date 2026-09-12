namespace CoHAnalytics.Models;

/// <summary>Small immutable actor identity. Pet instance keys belong to a later slice.</summary>
public sealed record ActorRef
{
    public required ActorType Type { get; init; }

    /// <summary>Raw surfaced name from the log, when one exists.</summary>
    public string? DisplayName { get; init; }

    public static ActorRef Self { get; } = new() { Type = ActorType.Self };

    public static ActorRef UnknownNamed(string displayName) =>
        new() { Type = ActorType.Unknown, DisplayName = displayName };

    public static ActorRef SelfNamed(string displayName) =>
        new() { Type = ActorType.Self, DisplayName = displayName };
}
