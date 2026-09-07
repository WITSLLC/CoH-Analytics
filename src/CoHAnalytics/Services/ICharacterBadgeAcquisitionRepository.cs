using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

public interface ICharacterBadgeAcquisitionRepository
{
    CharacterBadgeAcquisitionSnapshot GetSnapshot(CharacterRecordId characterRecordId);

    bool IsBadgeAcquired(CharacterRecordId characterRecordId, string catalogItemId);

    IReadOnlyCollection<string> GetAcquiredBadgeIds(CharacterRecordId characterRecordId);

    CharacterBadgeAcquisitionOperationResult RecordAcquisition(
        CharacterRecordId characterRecordId,
        string accountStableId,
        string catalogItemId,
        string observedTitle,
        DateTimeOffset observedAt);
}

public enum CharacterBadgeAcquisitionOutcome
{
    Success,
    InvalidArgument,
    PersistenceFailed
}

public sealed record CharacterBadgeAcquisitionOperationResult
{
    public CharacterBadgeAcquisitionOutcome Outcome { get; init; }

    public string? FailureReason { get; init; }

    public bool IsSuccess => Outcome == CharacterBadgeAcquisitionOutcome.Success;

    public static CharacterBadgeAcquisitionOperationResult Success() =>
        new() { Outcome = CharacterBadgeAcquisitionOutcome.Success };

    public static CharacterBadgeAcquisitionOperationResult Failure(
        CharacterBadgeAcquisitionOutcome outcome,
        string reason) =>
        new()
        {
            Outcome = outcome,
            FailureReason = reason
        };
}
