namespace CoHAnalytics.Models;

/// <summary>Stable opaque identity for one gameplay session within a monitoring context.</summary>
public sealed record GameplaySessionId
{
    private GameplaySessionId(Guid value)
    {
        Value = value;
    }

    public Guid Value { get; }

    public static GameplaySessionId CreateNew() => new(Guid.NewGuid());

    public static GameplaySessionId FromGuid(Guid value) => new(value);

    public override string ToString() => Value.ToString("n");
}
