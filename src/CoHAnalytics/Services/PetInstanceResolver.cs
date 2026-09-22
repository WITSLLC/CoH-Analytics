using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

/// <summary>
/// Conservative pet-name rollup. Session-scoped instance splitting is not justified yet;
/// collisions on the same normalized name remain unresolved and coverage-limited.
/// </summary>
internal static class PetInstanceResolver
{
    public static PetInstanceKey ForSurfacedName(string displayName) =>
        new()
        {
            NormalizedPetName = CharacterNameNormalizer.Normalize(displayName),
            OwnerRecordId = null,
            InstanceOrdinal = null,
            CoverageLimited = true
        };
}
