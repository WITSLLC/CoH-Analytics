namespace CoHAnalytics.ReferenceDataGenerator;

using CoHAnalytics.ReferenceData;

internal sealed record BadgeResearchPackage(
    IReadOnlyList<BadgeResearchZoneSetRow> ExplorationZoneSets,
    IReadOnlyList<BadgeResearchExplorationLocationRow> ExplorationLocations,
    IReadOnlyList<BadgeResearchPlaqueRow> HistoryPlaques,
    IReadOnlyList<BadgeResearchZoneIndexEntry> Zones,
    IReadOnlyList<BadgeResearchMasterCatalogRow> MasterCatalog,
    IReadOnlyList<BadgeResearchAccoladeIndexRow> AccoladeIndex,
    IReadOnlyDictionary<uint, BadgeResearchAccoladeDetail> AccoladeDetails,
    IReadOnlyList<BadgeResearchTitleCrosswalkRow> TitleCrosswalk,
    IReadOnlyList<BadgeResearchDependencyCrosswalkRow> DependencyCrosswalk);

internal sealed record BadgeResearchZoneSetRow(
    string ZoneName,
    string CompletionBadges,
    int DistinctMemberBadges,
    string AlignmentAccess,
    string Source);

internal sealed record BadgeResearchExplorationLocationRow(
    string ZoneName,
    string BadgeDisplayName,
    string AlignmentRestrictions,
    double? CoordinateX,
    double? CoordinateY,
    double? CoordinateZ,
    string ThumbtackCommand,
    string NearbyLandmark,
    string VariantInstance,
    string Notes,
    string Verification,
    string Sources);

internal sealed record BadgeResearchPlaqueRow(
    string CollectionName,
    string PlaqueName,
    string ZoneName,
    double? CoordinateX,
    double? CoordinateY,
    double? CoordinateZ,
    string ThumbtackCommand,
    string HistoryBadgeSet,
    int Sequence,
    string CompletionBadge,
    string NearbyLandmark,
    string Verification,
    string Sources);

internal sealed record BadgeResearchZoneIndexEntry(
    string DisplayName,
    string? AlignmentNotes,
    string? LevelRange,
    string? ZoneType,
    int BadgeCount,
    int PlaqueCount,
    IReadOnlyList<string> ExplorationCompletionBadges,
    IReadOnlyList<string> HistoryCompletionBadges,
    IReadOnlyList<string> ExplorationBadges,
    IReadOnlyList<string> Plaques);

internal sealed record BadgeResearchMasterCatalogRow(
    string ReferenceId,
    uint SetTitleId,
    string InternalName,
    string DisplayTitles,
    string Requirement,
    string RequiredByAccolades,
    string Verification,
    string Sources,
    string Category);

internal sealed record BadgeResearchAccoladeIndexRow(
    uint SetTitleId,
    string AccoladeTitles,
    int PrerequisiteCount,
    string RewardPower,
    string Verification);

internal sealed record BadgeResearchAccoladeDetail(
    uint SetTitleId,
    string InternalName,
    string DisplayTitles,
    string? RewardPower,
    string Verification,
    string? RequirementLogic,
    string? RequirementSynopsis,
    string? RequirementText,
    IReadOnlyList<string> LinkedPrerequisiteDisplayNames,
    ReferenceRequirementLogicPattern RequirementLogicPattern);

internal sealed record BadgeResearchTitleCrosswalkRow(
    uint SetTitleId,
    string InternalName,
    string Category,
    string IssueStatus,
    string HeroMale,
    string HeroFemale,
    string VillainMale,
    string VillainFemale,
    string PraetorianMale,
    string PraetorianFemale);

internal sealed record BadgeResearchDependencyCrosswalkRow(
    uint SetTitleId,
    string BadgeTitle,
    string Category,
    IReadOnlyList<string> AccoladeTitles);

internal sealed class BadgeResearchPackageException : Exception
{
    internal BadgeResearchPackageException(string message)
        : base(message)
    {
    }
}
