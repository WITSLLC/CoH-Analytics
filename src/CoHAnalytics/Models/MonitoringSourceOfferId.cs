namespace CoHAnalytics.Models;

/// <summary>
/// Stable runtime identity for one pending additional-session offer.
/// </summary>
public sealed record MonitoringSourceOfferId
{
    private MonitoringSourceOfferId(Guid value)
    {
        Value = value;
    }

    public Guid Value { get; }

    public static MonitoringSourceOfferId CreateNew() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString("n");
}
