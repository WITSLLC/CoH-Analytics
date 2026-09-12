namespace CoHAnalytics.Models;

/// <summary>Durable actor classification for a <see cref="ActorRef"/>.</summary>
public enum ActorType
{
    Self = 0,
    OwnPet,
    OtherPlayer,
    OtherPet,
    Enemy,
    Environment,
    Unknown
}
