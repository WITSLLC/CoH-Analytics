namespace CoHAnalytics.Models;

/// <summary>
/// Best-effort pet identity. Slice 4 rolls up by normalized name; instance ordinal stays
/// unresolved unless later evidence justifies a split.
/// </summary>
public sealed record PetInstanceKey
{
    public required string NormalizedPetName { get; init; }

    public string? OwnerRecordId { get; init; }

    public int? InstanceOrdinal { get; init; }

    public bool CoverageLimited { get; init; }
}
