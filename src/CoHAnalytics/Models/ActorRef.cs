namespace CoHAnalytics.Models;

/// <summary>Small immutable actor identity. Pet keys are coverage-limited name rollups.</summary>
public sealed record ActorRef
{
    public required ActorType Type { get; init; }

    /// <summary>Raw surfaced name from the log, when one exists.</summary>
    public string? DisplayName { get; init; }

    public PetInstanceKey? PetKey { get; init; }

    public static ActorRef Self { get; } = new() { Type = ActorType.Self };

    public static ActorRef UnknownNamed(string displayName) =>
        new() { Type = ActorType.Unknown, DisplayName = displayName };

    public static ActorRef SelfNamed(string displayName) =>
        new() { Type = ActorType.Self, DisplayName = displayName };

    public static ActorRef OwnPet(string displayName, PetInstanceKey? petKey = null) =>
        new() { Type = ActorType.OwnPet, DisplayName = displayName, PetKey = petKey };

    public static ActorRef OtherPet(string displayName, PetInstanceKey? petKey = null) =>
        new() { Type = ActorType.OtherPet, DisplayName = displayName, PetKey = petKey };
}
