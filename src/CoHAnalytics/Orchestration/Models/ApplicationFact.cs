namespace CoHAnalytics.Orchestration.Models;

public abstract record ApplicationFactValue
{
    private protected ApplicationFactValue()
    {
    }

    public sealed record Boolean(bool Value) : ApplicationFactValue;

    public sealed record Integer(long Value) : ApplicationFactValue;

    public sealed record Decimal(decimal Value) : ApplicationFactValue;

    public sealed record Text(string Value) : ApplicationFactValue;

    public sealed record Timestamp(DateTimeOffset Value) : ApplicationFactValue;

    public sealed record Duration(TimeSpan Value) : ApplicationFactValue;

    public sealed record Identifier(string Value) : ApplicationFactValue;
}

public sealed record ApplicationFactDisplay(
    string Label,
    string? Unit = null,
    string? FormatHint = null,
    bool IsDiagnosticOnly = false);

public sealed record ApplicationFact(
    string Key,
    ApplicationFactValue Value,
    ApplicationFactScope Scope,
    DateTimeOffset ObservedAt)
{
    public ApplicationFactConfidence Confidence { get; init; } = ApplicationFactConfidence.Observed;

    public string? RelatedEntityId { get; init; }

    public ApplicationFactDisplay? Display { get; init; }
}
