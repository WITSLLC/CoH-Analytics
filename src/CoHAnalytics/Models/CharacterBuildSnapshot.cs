namespace CoHAnalytics.Models;

/// <summary>
/// Last successfully parsed build layout owned by one stable character record. Display metadata
/// is informational only and must never be used to reconcile character identity.
/// </summary>
public sealed record CharacterBuildSnapshot
{
    public required CharacterRecordId CharacterRecordId { get; init; }

    public string? CharacterShortId { get; init; }

    public string? CharacterName { get; init; }

    public required DateTimeOffset SyncedAtUtc { get; init; }

    public string? SourceBuildFile { get; init; }

    public DateTimeOffset? SourceLastWriteUtc { get; init; }

    public required HomecomingBuildLayoutSnapshot Layout { get; init; }
}
