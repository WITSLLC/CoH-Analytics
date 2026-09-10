namespace CoHAnalytics.ReferenceData;

internal sealed class ItemReferenceCatalogDocument
{
    public ItemReferenceManifestDocument? Manifest { get; set; }

    public List<ItemReferenceRecordDocument> Items { get; set; } = [];

    public List<ItemReferenceAliasRecordDocument> Aliases { get; set; } = [];

    public List<EnhancementSetReferenceRecordDocument> EnhancementSets { get; set; } = [];

    public List<EnhancementResolverNamedTableReferenceRecordDocument>? EnhancementResolverNamedTables { get; set; }

    public List<BadgeReferenceRecordDocument> Badges { get; set; } = [];

    public List<BadgeLocationReferenceRecordDocument> BadgeLocations { get; set; } = [];

    public List<ZoneReferenceRecordDocument> Zones { get; set; } = [];

    public List<BadgeAccoladeRequirementRecordDocument> BadgeAccoladeRequirements { get; set; } = [];

    public RouteOrderingProvenanceRecordDocument? RouteOrderingProvenance { get; set; }

    public List<HistoryPlaqueRouteCollectionRecordDocument> HistoryPlaqueRouteCollections { get; set; } = [];

    public List<HistoryPlaqueRouteStopRecordDocument> HistoryPlaqueRouteStops { get; set; } = [];
}

internal sealed class ItemReferenceManifestDocument
{
    public string? CatalogVersion { get; set; }

    public ItemReferenceHomecomingCompatibilityDocument? HomecomingCompatibility { get; set; }

    public string? SourceRevision { get; set; }

    public string? SourceNotes { get; set; }
}

internal sealed class ItemReferenceHomecomingCompatibilityDocument
{
    public string? BuildMin { get; set; }

    public string? BuildMax { get; set; }
}

internal sealed class ItemReferenceRecordDocument
{
    public string? CatalogItemId { get; set; }

    public string? Family { get; set; }

    public string? Subtype { get; set; }

    public string? EnhancementFamily { get; set; }

    public string? CurrentDisplayName { get; set; }

    public string? ActiveStatus { get; set; }

    public List<ReferenceServerAvailabilityDocument>? ServerAvailability { get; set; }

    public string? VerificationStatus { get; set; }

    public string? Rarity { get; set; }

    public string? Origin { get; set; }

    public string? Tier { get; set; }

    public string? EnhancementSetId { get; set; }

    public string? ProducedItemId { get; set; }

    public string? Variant { get; set; }

    public string? CommonIoBoostType { get; set; }

    public string? CommonIoBoostTypeDisplayText { get; set; }

    public string? Icon { get; set; }

    public string? DisplayHelp { get; set; }

    public string? ShortHelp { get; set; }

    public string? HomecomingSourceId { get; set; }

    public string? HomecomingCategory { get; set; }

    public string? InspirationStandardTier { get; set; }

    public string? InspirationForm { get; set; }

    public string? DisplayNameMessageKey { get; set; }

    public string? DisplayHelpMessageKey { get; set; }

    public string? ShortHelpMessageKey { get; set; }

    public List<EnhancementSourceVariantReferenceRecordDocument>? SourceVariants { get; set; }

    public List<RecipeLevelReferenceRecordDocument>? RecipeLevels { get; set; }

    public List<RecipeExcludedSourceLevelReferenceRecordDocument>? ExcludedHomecomingSourceLevels { get; set; }
}

internal sealed class RecipeLevelReferenceRecordDocument
{
    public int? Level { get; set; }

    public string? HomecomingSourceId { get; set; }

    public uint? CraftingCost { get; set; }

    public List<RecipeRequirementReferenceRecordDocument>? Requirements { get; set; }
}

internal sealed class RecipeRequirementReferenceRecordDocument
{
    public string? SalvageItemId { get; set; }

    public uint? Quantity { get; set; }
}

internal sealed class RecipeExcludedSourceLevelReferenceRecordDocument
{
    public int? Level { get; set; }

    public string? HomecomingSourceId { get; set; }

    public uint? CraftingCost { get; set; }
}

internal sealed class EnhancementSourceVariantReferenceRecordDocument
{
    public string? HomecomingSourceId { get; set; }

    public string? SourceForm { get; set; }

    public string? Icon { get; set; }

    public string? DisplayHelp { get; set; }

    public string? ShortHelp { get; set; }

    public bool? BoostUsePlayerLevel { get; set; }

    public int? MaxBoostLevel { get; set; }

    public bool? BoostBoostable { get; set; }

    public List<EnhancementSourceVariantEffectReferenceRecordDocument>? Effects { get; set; }
}

internal sealed class EnhancementSourceVariantEffectReferenceRecordDocument
{
    public string? Tag { get; set; }

    public string? Table { get; set; }

    public float? Scale { get; set; }

    public List<uint>? AttribIds { get; set; }
}

internal sealed class EnhancementResolverNamedTableReferenceRecordDocument
{
    public string? Name { get; set; }

    public List<float>? Values { get; set; }
}

internal sealed class ItemReferenceAliasRecordDocument
{
    public string? CatalogItemId { get; set; }

    public string? Locale { get; set; }

    public string? Text { get; set; }

    public string? NameKind { get; set; }

    public bool IsPreferred { get; set; }
}

internal sealed class EnhancementSetReferenceRecordDocument
{
    public string? CatalogItemId { get; set; }

    public string? CurrentDisplayName { get; set; }

    public string? ActiveStatus { get; set; }

    public List<ReferenceServerAvailabilityDocument>? ServerAvailability { get; set; }

    public string? VerificationStatus { get; set; }

    public string? HomecomingSetId { get; set; }

    public string? CategoryCode { get; set; }

    public string? CategoryDisplayText { get; set; }

    public string? RarityCode { get; set; }

    public string? RarityDisplayText { get; set; }

    public int? MinimumLevel { get; set; }

    public int? MaximumLevel { get; set; }

    public List<EnhancementSetBonusReferenceRecordDocument>? Bonuses { get; set; }
}

internal sealed class EnhancementSetBonusReferenceRecordDocument
{
    public int MinimumBoosts { get; set; }

    public int MaximumBoosts { get; set; }

    public string? RequiresPattern { get; set; }

    public List<string>? RequiresTokens { get; set; }

    public List<string>? RequiredEnhancementIds { get; set; }

    public List<EnhancementSetBonusPowerReferenceRecordDocument>? AutoPowers { get; set; }
}

internal sealed class EnhancementSetBonusPowerReferenceRecordDocument
{
    public string? HomecomingSourceId { get; set; }

    public string? DisplayName { get; set; }

    public string? DisplayHelp { get; set; }

    public bool? BoostUsePlayerLevel { get; set; }

    public int? MaxBoostLevel { get; set; }

    public bool? BoostBoostable { get; set; }

    public List<EnhancementSourceVariantEffectReferenceRecordDocument>? Effects { get; set; }
}

internal sealed class ReferenceServerAvailabilityDocument
{
    public string? ServerKey { get; set; }

    public string? Status { get; set; }
}

internal sealed class BadgeReferenceRecordDocument
{
    public string? CatalogItemId { get; set; }

    public string? HomecomingSourceId { get; set; }

    public uint? SetTitleId { get; set; }

    public string? CanonicalCategory { get; set; }

    public uint? BadgeType { get; set; }

    public string? ReferenceKind { get; set; }

    public string? HeroName { get; set; }

    public string? VillainName { get; set; }

    public string? HeroDescription { get; set; }

    public string? VillainDescription { get; set; }

    public string? HeroIcon { get; set; }

    public string? VillainIcon { get; set; }

    public string? ZoneId { get; set; }

    public string? CompletionBadgeId { get; set; }

    public bool? IsZoneCompletionBadge { get; set; }

    public string? VerificationStatus { get; set; }

    public string? RequirementText { get; set; }

    public string? RewardText { get; set; }

    public string? RequirementLogicStatus { get; set; }

    public string? RequirementLogicPattern { get; set; }
}

internal sealed class RouteOrderingProvenanceRecordDocument
{
    public string? SourceName { get; set; }

    public string? Author { get; set; }

    public string? Version { get; set; }

    public string? MapsThreadUrl { get; set; }

    public string? PopmenuThreadUrl { get; set; }

    public string? OrderingSemantics { get; set; }

    public string? Retrieved { get; set; }
}

internal sealed class HistoryPlaqueRouteCollectionRecordDocument
{
    public string? CollectionName { get; set; }

    public string? CompletionBadgeId { get; set; }

    public bool? PublishedCollectionOrderAvailable { get; set; }

    public string? OrderingStatus { get; set; }

    public string? OrderingNotes { get; set; }
}

internal sealed class HistoryPlaqueRouteStopRecordDocument
{
    public string? CollectionName { get; set; }

    public int? InventoryOrder { get; set; }

    public int? RouteOrder { get; set; }

    public string? CompletionBadgeId { get; set; }

    public string? ZoneId { get; set; }

    public int? LocationIndex { get; set; }

    public string? PlaqueName { get; set; }

    public int? SourceZoneRouteOrder { get; set; }

    public string? RouteMappingConfidence { get; set; }

    public string? RouteSourceProject { get; set; }

    public string? RouteSourceVersion { get; set; }

    public string? RouteSourceUrl { get; set; }
}

internal sealed class BadgeLocationReferenceRecordDocument
{
    public string? BadgeCatalogItemId { get; set; }

    public string? ZoneId { get; set; }

    public double? CoordinateX { get; set; }

    public double? CoordinateY { get; set; }

    public double? CoordinateZ { get; set; }

    public string? ThumbtackCommand { get; set; }

    public string? MarkerType { get; set; }

    public string? LocationRole { get; set; }

    public string? CoordinateSemantics { get; set; }

    public string? VerificationStatus { get; set; }

    public int? LocationIndex { get; set; }

    public string? TriggerDescription { get; set; }

    public int? ExplorationRouteOrder { get; set; }

    public string? RouteSourceProject { get; set; }

    public string? RouteSourceVersion { get; set; }

    public string? RouteSourceUrl { get; set; }

    public string? RouteMappingConfidence { get; set; }
}

internal sealed class ZoneReferenceRecordDocument
{
    public string? ZoneId { get; set; }

    public string? DisplayName { get; set; }

    public string? AlignmentNotes { get; set; }

    public string? LevelRange { get; set; }

    public string? ZoneType { get; set; }

    public List<string>? ExplorationCompletionBadgeIds { get; set; }

    public List<string>? HistoryCompletionBadgeIds { get; set; }
}

internal sealed class BadgeAccoladeRequirementRecordDocument
{
    public string? AccoladeBadgeId { get; set; }

    public string? PrerequisiteBadgeId { get; set; }

    public int? PrerequisiteIndex { get; set; }

    public int? LogicGroup { get; set; }

    public string? RequirementLogicStatus { get; set; }
}
