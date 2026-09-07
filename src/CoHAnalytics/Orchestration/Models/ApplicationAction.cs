namespace CoHAnalytics.Orchestration.Models;

public sealed record ApplicationAction(
    string ActionId,
    string Kind,
    string DisplayLabel,
    ApplicationActionTarget Target)
{
    public string? ProviderId { get; init; }

    public string? RelatedEntityId { get; init; }

    public bool IsEnabled { get; init; } = true;

    public string? DisabledReason { get; init; }

    public int Priority { get; init; }

    public bool IsPrimary { get; init; }
}

public sealed record ApplicationActionResult(bool Succeeded, string? Message = null)
{
    public static ApplicationActionResult Success(string? message = null) => new(true, message);

    public static ApplicationActionResult Failed(string message) => new(false, message);
}
