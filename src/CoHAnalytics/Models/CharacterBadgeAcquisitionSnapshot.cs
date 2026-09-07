namespace CoHAnalytics.Models;

/// <summary>Immutable read snapshot of badge acquisitions for one character.</summary>
public sealed record CharacterBadgeAcquisitionSnapshot
{
    public static CharacterBadgeAcquisitionSnapshot Empty { get; } = new()
    {
        CharacterRecordId = CharacterRecordId.FromGuid(Guid.Empty),
        AccountStableId = string.Empty,
        AcquiredBadgeIds = Array.Empty<string>()
    };

    public required CharacterRecordId CharacterRecordId { get; init; }

    public required string AccountStableId { get; init; }

    public required IReadOnlyList<string> AcquiredBadgeIds { get; init; }
}
