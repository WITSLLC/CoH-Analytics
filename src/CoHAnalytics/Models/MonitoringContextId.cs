namespace CoHAnalytics.Models;

/// <summary>
/// Stable runtime identity for one monitoring context.
/// </summary>
/// <remarks>
/// This identity is deliberately opaque and generated at creation time. It is never derived
/// from an account ID, a character name, a process ID, a log source ID, or a file path, so it
/// remains valid across account rebinding, source rollover, and source replacement.
/// </remarks>
public sealed record MonitoringContextId
{
    private MonitoringContextId(Guid value)
    {
        Value = value;
    }

    public Guid Value { get; }

    public static MonitoringContextId CreateNew() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString("n");
}
