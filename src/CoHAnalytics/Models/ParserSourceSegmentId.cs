namespace CoHAnalytics.Models;

/// <summary>Parser-owned identity for one continuous byte range of a logical source binding.</summary>
public sealed record ParserSourceSegmentId
{
    private ParserSourceSegmentId(Guid value) => Value = value;

    public Guid Value { get; }

    public static ParserSourceSegmentId CreateNew() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString("n");
}
