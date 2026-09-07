namespace CoHAnalytics.Models;

/// <summary>Stable runtime identity for one parser worker.</summary>
public sealed record ParserWorkerId
{
    private ParserWorkerId(Guid value) => Value = value;

    public Guid Value { get; }

    public static ParserWorkerId CreateNew() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString("n");
}
