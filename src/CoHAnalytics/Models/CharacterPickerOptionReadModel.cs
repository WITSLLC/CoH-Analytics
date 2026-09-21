namespace CoHAnalytics.Models;

/// <summary>
/// One account-scoped character available for manual runtime identity confirmation.
/// </summary>
public sealed record CharacterPickerOptionReadModel
{
    public required CharacterRecordId RecordId { get; init; }

    public required string DisplayName { get; init; }

    public required DateTimeOffset LastObservedAt { get; init; }
}
