namespace CoHAnalytics.Models;

/// <summary>
/// Shell-level navigation context: which account and character the user is browsing.
/// </summary>
public sealed record ViewedContextState
{
    public static ViewedContextState Empty { get; } = new();

    public string? AccountStableId { get; init; }

    public CharacterRecordId? CharacterRecordId { get; init; }

    /// <summary>
    /// When true, viewed context follows the established live-follow monitoring context.
    /// </summary>
    public bool IsFollowingLive { get; init; }

    /// <summary>
    /// Monitoring context tracked for live-follow when multiple clients are live.
    /// </summary>
    public MonitoringContextId? LiveFollowContextId { get; init; }

    public bool HasCharacter =>
        !string.IsNullOrWhiteSpace(AccountStableId) && CharacterRecordId is not null;

    public bool HasAccount => !string.IsNullOrWhiteSpace(AccountStableId);
}
