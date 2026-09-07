using System.Text.Json;

namespace CoHAnalytics.ReferenceData;

internal static class ItemReferenceCatalogLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static ItemReferenceCatalogLoadResult Load(Stream stream)
    {
        try
        {
            var document = JsonSerializer.Deserialize<ItemReferenceCatalogDocument>(stream, JsonOptions);
            if (document is null)
            {
                return ItemReferenceCatalogLoadResult.Failed("Catalog document is empty.");
            }

            return ValidateAndBuild(document);
        }
        catch (JsonException ex)
        {
            return ItemReferenceCatalogLoadResult.Failed($"Catalog JSON is invalid: {ex.Message}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ItemReferenceCatalogLoadResult.Failed($"Catalog load failed: {ex.Message}");
        }
    }

    private static ItemReferenceCatalogLoadResult ValidateAndBuild(ItemReferenceCatalogDocument document)
    {
        if (document.Manifest is null)
        {
            return ItemReferenceCatalogLoadResult.Failed("Manifest is required.");
        }

        if (string.IsNullOrWhiteSpace(document.Manifest.CatalogVersion))
        {
            return ItemReferenceCatalogLoadResult.Failed("Manifest.CatalogVersion is required.");
        }

        if (document.Manifest.HomecomingCompatibility is null)
        {
            return ItemReferenceCatalogLoadResult.Failed("Manifest.HomecomingCompatibility is required.");
        }

        var manifest = new ItemReferenceManifest
        {
            CatalogVersion = document.Manifest.CatalogVersion.Trim(),
            HomecomingCompatibility = new ItemReferenceHomecomingCompatibility
            {
                BuildMin = NormalizeOptional(document.Manifest.HomecomingCompatibility.BuildMin),
                BuildMax = NormalizeOptional(document.Manifest.HomecomingCompatibility.BuildMax)
            },
            SourceRevision = NormalizeOptional(document.Manifest.SourceRevision),
            SourceNotes = NormalizeOptional(document.Manifest.SourceNotes)
        };

        var items = new Dictionary<string, ItemReferenceRecord>(StringComparer.Ordinal);
        foreach (var (index, itemDocument) in document.Items.Select((value, index) => (index, value)))
        {
            if (!TryParseItem(itemDocument, index, out var item, out var failureReason))
            {
                return ItemReferenceCatalogLoadResult.Failed(failureReason);
            }

            if (items.ContainsKey(item.CatalogItemId))
            {
                return ItemReferenceCatalogLoadResult.Failed(
                    $"Duplicate CatalogItemId '{item.CatalogItemId}' in items.");
            }

            items[item.CatalogItemId] = item;
        }

        var enhancementSets = new Dictionary<string, EnhancementSetReferenceRecord>(StringComparer.Ordinal);
        foreach (var (index, setDocument) in document.EnhancementSets.Select((value, index) => (index, value)))
        {
            if (!TryParseEnhancementSet(setDocument, index, out var setRecord, out var failureReason))
            {
                return ItemReferenceCatalogLoadResult.Failed(failureReason);
            }

            if (items.ContainsKey(setRecord.CatalogItemId))
            {
                return ItemReferenceCatalogLoadResult.Failed(
                    $"CatalogItemId '{setRecord.CatalogItemId}' is used by both items and enhancementSets.");
            }

            if (enhancementSets.ContainsKey(setRecord.CatalogItemId))
            {
                return ItemReferenceCatalogLoadResult.Failed(
                    $"Duplicate CatalogItemId '{setRecord.CatalogItemId}' in enhancementSets.");
            }

            enhancementSets[setRecord.CatalogItemId] = setRecord;
        }

        foreach (var item in items.Values)
        {
            if (string.IsNullOrWhiteSpace(item.EnhancementSetId))
            {
                continue;
            }

            if (!enhancementSets.ContainsKey(item.EnhancementSetId))
            {
                return ItemReferenceCatalogLoadResult.Failed(
                    $"Item '{item.CatalogItemId}' references unknown EnhancementSetId '{item.EnhancementSetId}'.");
            }

            if (item.Family is not (ReferenceItemFamily.Enhancement or ReferenceItemFamily.Recipe))
            {
                return ItemReferenceCatalogLoadResult.Failed(
                    $"Item '{item.CatalogItemId}' has EnhancementSetId but family is '{item.Family}'.");
            }
        }

        foreach (var item in items.Values)
        {
            if (item.Family is not ReferenceItemFamily.Recipe)
            {
                if (!string.IsNullOrWhiteSpace(item.ProducedItemId))
                {
                    return ItemReferenceCatalogLoadResult.Failed(
                        $"Item '{item.CatalogItemId}' has ProducedItemId but family is '{item.Family}'.");
                }

                continue;
            }

            if (string.IsNullOrWhiteSpace(item.ProducedItemId)
                || !items.TryGetValue(item.ProducedItemId, out var producedItem)
                || producedItem.Family is not ReferenceItemFamily.Enhancement)
            {
                return ItemReferenceCatalogLoadResult.Failed(
                    $"Recipe '{item.CatalogItemId}' must reference an existing Enhancement ProducedItemId.");
            }

            if (string.IsNullOrWhiteSpace(item.Rarity))
            {
                return ItemReferenceCatalogLoadResult.Failed(
                    $"Recipe '{item.CatalogItemId}' is missing Rarity.");
            }

            if (!string.IsNullOrWhiteSpace(item.EnhancementSetId)
                && !string.Equals(
                    item.EnhancementSetId,
                    producedItem.EnhancementSetId,
                    StringComparison.Ordinal))
            {
                return ItemReferenceCatalogLoadResult.Failed(
                    $"Recipe '{item.CatalogItemId}' and produced Enhancement '{producedItem.CatalogItemId}' must reference the same EnhancementSetId.");
            }

            foreach (var level in item.RecipeLevels)
            {
                foreach (var requirement in level.Requirements)
                {
                    if (!items.TryGetValue(requirement.SalvageItemId, out var salvage)
                        || salvage.Family is not ReferenceItemFamily.Salvage)
                    {
                        return ItemReferenceCatalogLoadResult.Failed(
                            $"Recipe '{item.CatalogItemId}' level {level.Level} salvage '{requirement.SalvageItemId}' is not an existing Salvage identity.");
                    }
                }
            }
        }

        var duplicateProduced = items.Values
            .Where(item => item.Family is ReferenceItemFamily.Recipe
                && !string.IsNullOrWhiteSpace(item.ProducedItemId))
            .GroupBy(item => item.ProducedItemId!, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateProduced is not null)
        {
            return ItemReferenceCatalogLoadResult.Failed(
                $"Multiple Recipes produce Enhancement '{duplicateProduced.Key}'.");
        }

        var aliasIndex = new Dictionary<string, (string CatalogItemId, string MatchedAliasText)>(
            StringComparer.OrdinalIgnoreCase);
        var allAliasIndex = new Dictionary<string, (string CatalogItemId, string MatchedAliasText)>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var (index, aliasDocument) in document.Aliases.Select((value, index) => (index, value)))
        {
            if (!TryParseAlias(aliasDocument, index, out var alias, out var failureReason))
            {
                return ItemReferenceCatalogLoadResult.Failed(failureReason);
            }

            if (!items.ContainsKey(alias.CatalogItemId))
            {
                return ItemReferenceCatalogLoadResult.Failed(
                    $"Alias at index {index} references unknown CatalogItemId '{alias.CatalogItemId}'.");
            }

            var lookupKey = ItemReferenceLookup.NormalizeLookupKey(alias.Text);
            if (string.IsNullOrEmpty(lookupKey))
            {
                return ItemReferenceCatalogLoadResult.Failed($"Alias at index {index} has empty Text.");
            }

            var item = items[alias.CatalogItemId];
            var includeInCurrentIndex = item.Family is not ReferenceItemFamily.Enhancement
                || ReferenceServerAvailabilitySupport.IsCurrentHomecoming(item.ServerAvailability);

            if (!TryAssignAliasIndex(
                    allAliasIndex,
                    lookupKey,
                    alias.CatalogItemId,
                    alias.Text,
                    index,
                    out failureReason))
            {
                return ItemReferenceCatalogLoadResult.Failed(failureReason);
            }

            if (includeInCurrentIndex
                && !TryAssignAliasIndex(
                    aliasIndex,
                    lookupKey,
                    alias.CatalogItemId,
                    alias.Text,
                    index,
                    out failureReason))
            {
                return ItemReferenceCatalogLoadResult.Failed(failureReason);
            }
        }

        if (!TryParseNamedTables(
                document.EnhancementResolverNamedTables,
                out var namedTables,
                out var namedTableFailure))
        {
            return ItemReferenceCatalogLoadResult.Failed(namedTableFailure);
        }

        if (!TryParseBadges(document.Badges, items, out var badges, out var badgeFailure)
            || !TryParseZones(document.Zones, out var zones, out badgeFailure)
            || !TryParseBadgeLocations(document.BadgeLocations, badges, zones, out var badgeLocations, out badgeFailure)
            || !TryParseBadgeAccoladeRequirements(
                document.BadgeAccoladeRequirements,
                badges,
                out var badgeAccoladeRequirements,
                out badgeFailure)
            || !TryParseRouteOrderingProvenance(document.RouteOrderingProvenance, out var routeOrderingProvenance, out badgeFailure)
            || !TryParseHistoryPlaqueRouteCollections(
                document.HistoryPlaqueRouteCollections,
                badges,
                out var historyPlaqueRouteCollections,
                out badgeFailure)
            || !TryParseHistoryPlaqueRouteStops(
                document.HistoryPlaqueRouteStops,
                badges,
                zones,
                historyPlaqueRouteCollections,
                out var historyPlaqueRouteStops,
                out badgeFailure))
        {
            return ItemReferenceCatalogLoadResult.Failed(badgeFailure);
        }

        return ItemReferenceCatalogLoadResult.Success(
            manifest,
            items,
            enhancementSets,
            namedTables,
            badges,
            badgeLocations,
            zones,
            badgeAccoladeRequirements,
            routeOrderingProvenance,
            historyPlaqueRouteCollections,
            historyPlaqueRouteStops,
            aliasIndex,
            allAliasIndex);
    }

    private static bool TryAssignAliasIndex(
        Dictionary<string, (string CatalogItemId, string MatchedAliasText)> aliasIndex,
        string lookupKey,
        string catalogItemId,
        string aliasText,
        int index,
        out string failureReason)
    {
        failureReason = string.Empty;
        if (aliasIndex.TryGetValue(lookupKey, out var existing)
            && !string.Equals(existing.CatalogItemId, catalogItemId, StringComparison.Ordinal))
        {
            failureReason =
                $"Alias text '{aliasText}' is assigned to both '{existing.CatalogItemId}' and '{catalogItemId}'.";
            return false;
        }

        aliasIndex[lookupKey] = (catalogItemId, aliasText);
        return true;
    }

    private static bool TryParseItem(
        ItemReferenceRecordDocument document,
        int index,
        out ItemReferenceRecord item,
        out string failureReason)
    {
        item = null!;
        failureReason = string.Empty;

        if (string.IsNullOrWhiteSpace(document.CatalogItemId))
        {
            failureReason = $"Item at index {index} is missing CatalogItemId.";
            return false;
        }

        if (!TryParseFamily(document.Family, out var family, out failureReason))
        {
            failureReason = $"Item at index {index}: {failureReason}";
            return false;
        }

        if (!ItemReferenceIdRules.TryValidateItemId(document.CatalogItemId, family, out var idFailure))
        {
            failureReason = $"Item at index {index}: {idFailure}";
            return false;
        }

        if (string.IsNullOrWhiteSpace(document.Subtype))
        {
            failureReason = $"Item at index {index} is missing Subtype.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(document.CurrentDisplayName))
        {
            failureReason = $"Item at index {index} is missing CurrentDisplayName.";
            return false;
        }

        if (!TryParseActiveStatus(document.ActiveStatus, out var activeStatus, out var activeFailure))
        {
            failureReason = $"Item at index {index}: {activeFailure}";
            return false;
        }

        if (!TryParseVerificationStatus(document.VerificationStatus, out var verificationStatus, out var verificationFailure))
        {
            failureReason = $"Item at index {index}: {verificationFailure}";
            return false;
        }

        if (!TryParseServerAvailability(
                document.ServerAvailability,
                index,
                requiresHomecoming: family == ReferenceItemFamily.Enhancement,
                out var serverAvailability,
                out failureReason))
        {
            return false;
        }

        if (!TryParseSourceVariants(
                document.SourceVariants,
                index,
                out var sourceVariants,
                out failureReason))
        {
            return false;
        }

        if (!TryParseRecipeLevels(
                document.RecipeLevels,
                family,
                index,
                out var recipeLevels,
                out failureReason))
        {
            return false;
        }

        if (!TryParseExcludedHomecomingSourceLevels(
                document.ExcludedHomecomingSourceLevels,
                family,
                index,
                out var excludedSourceLevels,
                out failureReason))
        {
            return false;
        }

        if (!TryParseEnhancementFamily(
                document.EnhancementFamily,
                document.Subtype,
                document.EnhancementSetId,
                family,
                index,
                out var enhancementFamily,
                out failureReason))
        {
            return false;
        }

        item = new ItemReferenceRecord
        {
            CatalogItemId = document.CatalogItemId.Trim(),
            Family = family,
            Subtype = document.Subtype.Trim(),
            EnhancementFamily = enhancementFamily,
            CurrentDisplayName = document.CurrentDisplayName.Trim(),
            ActiveStatus = activeStatus,
            ServerAvailability = serverAvailability,
            VerificationStatus = verificationStatus,
            Rarity = NormalizeOptional(document.Rarity),
            Origin = NormalizeOptional(document.Origin),
            Tier = NormalizeOptional(document.Tier),
            EnhancementSetId = NormalizeOptional(document.EnhancementSetId),
            ProducedItemId = NormalizeOptional(document.ProducedItemId),
            Variant = NormalizeOptional(document.Variant),
            CommonIoBoostType = NormalizeOptional(document.CommonIoBoostType),
            CommonIoBoostTypeDisplayText = NormalizeOptional(document.CommonIoBoostTypeDisplayText),
            Icon = NormalizeOptional(document.Icon),
            DisplayHelp = NormalizeOptional(document.DisplayHelp),
            ShortHelp = NormalizeOptional(document.ShortHelp),
            HomecomingSourceId = NormalizeOptional(document.HomecomingSourceId),
            HomecomingCategory = NormalizeOptional(document.HomecomingCategory),
            InspirationStandardTier = NormalizeOptional(document.InspirationStandardTier),
            InspirationForm = NormalizeOptional(document.InspirationForm),
            DisplayNameMessageKey = NormalizeOptional(document.DisplayNameMessageKey),
            DisplayHelpMessageKey = NormalizeOptional(document.DisplayHelpMessageKey),
            ShortHelpMessageKey = NormalizeOptional(document.ShortHelpMessageKey),
            SourceVariants = sourceVariants,
            RecipeLevels = recipeLevels,
            ExcludedHomecomingSourceLevels = excludedSourceLevels
        };

        if (item.Family is not ReferenceItemFamily.Enhancement)
        {
            if (item.CommonIoBoostType is not null
                || item.CommonIoBoostTypeDisplayText is not null
                || item.SourceVariants.Count > 0)
            {
                failureReason =
                    $"Item at index {index} has Enhancement enrichment fields but family is '{item.Family}'.";
                return false;
            }

            if ((item.DisplayHelp is not null || item.ShortHelp is not null)
                && item.Family is not ReferenceItemFamily.Inspiration)
            {
                failureReason =
                    $"Item at index {index} has help enrichment fields but family is '{item.Family}'.";
                return false;
            }

            if (item.Icon is not null
                && item.Family is not (
                    ReferenceItemFamily.Recipe
                    or ReferenceItemFamily.Badge
                    or ReferenceItemFamily.Inspiration))
            {
                failureReason =
                    $"Item at index {index} has icon enrichment fields but family is '{item.Family}'.";
                return false;
            }

            if (HasInspirationHomecomingFields(item)
                && item.Family is not ReferenceItemFamily.Inspiration)
            {
                failureReason =
                    $"Item at index {index} has Inspiration Homecoming fields but family is '{item.Family}'.";
                return false;
            }
        }

        if (item.Family == ReferenceItemFamily.Inspiration
            && HasInspirationHomecomingFields(item)
            && string.IsNullOrWhiteSpace(item.HomecomingSourceId))
        {
            failureReason =
                $"Item at index {index} has Inspiration Homecoming metadata without HomecomingSourceId.";
            return false;
        }

        if (item.CommonIoBoostTypeDisplayText is not null && item.CommonIoBoostType is null)
        {
            failureReason =
                $"Item at index {index} has CommonIoBoostTypeDisplayText without CommonIoBoostType.";
            return false;
        }

        if (item.Family == ReferenceItemFamily.Enhancement
            && item.EnhancementFamily is ReferenceEnhancementFamily.CraftedInvention
            && item.CommonIoBoostTypeDisplayText is not null
            && item.CommonIoBoostType is not null
            && item.CommonIoBoostType.Contains('+', StringComparison.Ordinal))
        {
            failureReason =
                $"Item at index {index} has composite CommonIoBoostType with a presentation label.";
            return false;
        }

        if (item.Family == ReferenceItemFamily.Enhancement
            && item.EnhancementFamily is not null
            && item.EnhancementFamily is not ReferenceEnhancementFamily.CraftedInvention
            && item.CommonIoBoostTypeDisplayText is not null)
        {
            failureReason =
                $"Item at index {index} has CommonIoBoostTypeDisplayText outside CraftedInvention.";
            return false;
        }

        if (item.Family == ReferenceItemFamily.Enhancement
            && item.EnhancementFamily is not null
            && string.Equals(item.Subtype, "CommonIO", StringComparison.Ordinal))
        {
            failureReason =
                $"Item at index {index} has EnhancementFamily '{item.EnhancementFamily}' with legacy CommonIO subtype.";
            return false;
        }

        if (item.Family is not ReferenceItemFamily.Enhancement && item.ServerAvailability.Count > 0)
        {
            failureReason =
                $"Item at index {index} has ServerAvailability but family is '{item.Family}'.";
            return false;
        }

        if (item.Family == ReferenceItemFamily.Enhancement
            && !EnhancementVariantHelpValidation.TryValidateLogicalHelpConsistency(item, out failureReason))
        {
            return false;
        }

        return true;
    }

    private static bool TryParseRecipeLevels(
        List<RecipeLevelReferenceRecordDocument>? documents,
        ReferenceItemFamily family,
        int index,
        out IReadOnlyList<RecipeLevelReferenceRecord> recipeLevels,
        out string failureReason)
    {
        recipeLevels = Array.Empty<RecipeLevelReferenceRecord>();
        failureReason = string.Empty;
        if (documents is null || documents.Count == 0)
        {
            return true;
        }

        if (family is not ReferenceItemFamily.Recipe)
        {
            failureReason =
                $"Item at index {index} has RecipeLevels but family is '{family}'.";
            return false;
        }

        var parsed = new List<RecipeLevelReferenceRecord>(documents.Count);
        var seenLevels = new HashSet<int>();
        foreach (var document in documents)
        {
            if (document.Level is not int level
                || level is < 1 or > 50)
            {
                failureReason =
                    $"Item at index {index} RecipeLevels contains a level outside 1–50.";
                return false;
            }

            if (!seenLevels.Add(level))
            {
                failureReason =
                    $"Item at index {index} RecipeLevels repeats level {level}.";
                return false;
            }

            var sourceId = NormalizeOptional(document.HomecomingSourceId);
            if (sourceId is null)
            {
                failureReason =
                    $"Item at index {index} RecipeLevels level {level} is missing HomecomingSourceId.";
                return false;
            }

            if (document.CraftingCost is not uint craftingCost || craftingCost == 0)
            {
                failureReason =
                    $"Item at index {index} RecipeLevels level {level} is missing a positive CraftingCost.";
                return false;
            }

            if (!TryParseRecipeRequirements(
                    document.Requirements,
                    index,
                    level,
                    out var requirements,
                    out failureReason))
            {
                return false;
            }

            parsed.Add(new RecipeLevelReferenceRecord
            {
                Level = level,
                HomecomingSourceId = sourceId,
                CraftingCost = craftingCost,
                Requirements = requirements
            });
        }

        recipeLevels = parsed
            .OrderBy(level => level.Level)
            .ToArray();
        return true;
    }

    private static bool TryParseRecipeRequirements(
        List<RecipeRequirementReferenceRecordDocument>? documents,
        int index,
        int level,
        out IReadOnlyList<RecipeRequirementReferenceRecord> requirements,
        out string failureReason)
    {
        requirements = Array.Empty<RecipeRequirementReferenceRecord>();
        failureReason = string.Empty;
        if (documents is null || documents.Count == 0)
        {
            return true;
        }

        var parsed = new List<RecipeRequirementReferenceRecord>(documents.Count);
        var seenSalvage = new HashSet<string>(StringComparer.Ordinal);
        foreach (var document in documents)
        {
            var salvageItemId = NormalizeOptional(document.SalvageItemId);
            if (salvageItemId is null)
            {
                failureReason =
                    $"Item at index {index} RecipeLevels level {level} is missing SalvageItemId.";
                return false;
            }

            if (!seenSalvage.Add(salvageItemId))
            {
                failureReason =
                    $"Item at index {index} RecipeLevels level {level} repeats salvage '{salvageItemId}'.";
                return false;
            }

            if (document.Quantity is not uint quantity || quantity == 0)
            {
                failureReason =
                    $"Item at index {index} RecipeLevels level {level} salvage '{salvageItemId}' is missing a positive Quantity.";
                return false;
            }

            parsed.Add(new RecipeRequirementReferenceRecord
            {
                SalvageItemId = salvageItemId,
                Quantity = quantity
            });
        }

        requirements = parsed
            .OrderBy(requirement => requirement.SalvageItemId, StringComparer.Ordinal)
            .ToArray();
        return true;
    }

    private static bool TryParseExcludedHomecomingSourceLevels(
        List<RecipeExcludedSourceLevelReferenceRecordDocument>? documents,
        ReferenceItemFamily family,
        int index,
        out IReadOnlyList<RecipeExcludedSourceLevelReferenceRecord> excludedLevels,
        out string failureReason)
    {
        excludedLevels = Array.Empty<RecipeExcludedSourceLevelReferenceRecord>();
        failureReason = string.Empty;
        if (documents is null || documents.Count == 0)
        {
            return true;
        }

        if (family is not ReferenceItemFamily.Recipe)
        {
            failureReason =
                $"Item at index {index} has ExcludedHomecomingSourceLevels but family is '{family}'.";
            return false;
        }

        var parsed = new List<RecipeExcludedSourceLevelReferenceRecord>(documents.Count);
        var seenLevels = new HashSet<int>();
        foreach (var document in documents)
        {
            if (document.Level is not int level || level <= 50)
            {
                failureReason =
                    $"Item at index {index} ExcludedHomecomingSourceLevels must contain only levels above 50.";
                return false;
            }

            if (!seenLevels.Add(level))
            {
                failureReason =
                    $"Item at index {index} ExcludedHomecomingSourceLevels repeats level {level}.";
                return false;
            }

            var sourceId = NormalizeOptional(document.HomecomingSourceId);
            if (sourceId is null)
            {
                failureReason =
                    $"Item at index {index} ExcludedHomecomingSourceLevels level {level} is missing HomecomingSourceId.";
                return false;
            }

            if (document.CraftingCost is not uint craftingCost || craftingCost == 0)
            {
                failureReason =
                    $"Item at index {index} ExcludedHomecomingSourceLevels level {level} is missing a positive CraftingCost.";
                return false;
            }

            parsed.Add(new RecipeExcludedSourceLevelReferenceRecord
            {
                Level = level,
                HomecomingSourceId = sourceId,
                CraftingCost = craftingCost
            });
        }

        excludedLevels = parsed
            .OrderBy(level => level.Level)
            .ToArray();
        return true;
    }

    private static bool TryParseEnhancementFamily(
        string? value,
        string? subtype,
        string? enhancementSetId,
        ReferenceItemFamily family,
        int index,
        out ReferenceEnhancementFamily? enhancementFamily,
        out string failureReason)
    {
        enhancementFamily = null;
        failureReason = string.Empty;

        if (family is not ReferenceItemFamily.Enhancement)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                failureReason =
                    $"Item at index {index} has EnhancementFamily but family is '{family}'.";
                return false;
            }

            return true;
        }

        if (!string.IsNullOrWhiteSpace(enhancementSetId))
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                failureReason =
                    $"Item at index {index} is a set piece but has EnhancementFamily.";
                return false;
            }

            if (!string.Equals(subtype, "SetIO", StringComparison.Ordinal))
            {
                failureReason =
                    $"Item at index {index} has EnhancementSetId but subtype is not SetIO.";
                return false;
            }

            return true;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            if (string.Equals(subtype, "CommonIO", StringComparison.Ordinal))
            {
                return true;
            }

            failureReason =
                $"Item at index {index} is a non-set Enhancement but has no EnhancementFamily.";
            return false;
        }

        if (!Enum.TryParse(value, ignoreCase: true, out ReferenceEnhancementFamily parsed)
            || !Enum.IsDefined(parsed))
        {
            failureReason =
                $"Item at index {index} has unknown EnhancementFamily '{value}'.";
            return false;
        }

        if (!string.Equals(subtype, parsed.ToString(), StringComparison.Ordinal))
        {
            failureReason =
                $"Item at index {index} subtype '{subtype}' does not match EnhancementFamily '{parsed}'.";
            return false;
        }

        enhancementFamily = parsed;
        return true;
    }

    private static bool TryParseEnhancementSet(
        EnhancementSetReferenceRecordDocument document,
        int index,
        out EnhancementSetReferenceRecord setRecord,
        out string failureReason)
    {
        setRecord = null!;
        failureReason = string.Empty;

        if (string.IsNullOrWhiteSpace(document.CatalogItemId))
        {
            failureReason = $"EnhancementSet at index {index} is missing CatalogItemId.";
            return false;
        }

        if (!ItemReferenceIdRules.TryValidateEnhancementSetId(document.CatalogItemId, out var idFailure))
        {
            failureReason = $"EnhancementSet at index {index}: {idFailure}";
            return false;
        }

        if (string.IsNullOrWhiteSpace(document.CurrentDisplayName))
        {
            failureReason = $"EnhancementSet at index {index} is missing CurrentDisplayName.";
            return false;
        }

        if (!TryParseActiveStatus(document.ActiveStatus, out var activeStatus, out var activeFailure))
        {
            failureReason = $"EnhancementSet at index {index}: {activeFailure}";
            return false;
        }

        if (!TryParseVerificationStatus(document.VerificationStatus, out var verificationStatus, out var verificationFailure))
        {
            failureReason = $"EnhancementSet at index {index}: {verificationFailure}";
            return false;
        }

        if (!TryParseServerAvailability(
                document.ServerAvailability,
                index,
                requiresHomecoming: true,
                out var serverAvailability,
                out failureReason))
        {
            return false;
        }

        if (!TryParseBonuses(document.Bonuses, index, out var bonuses, out failureReason))
        {
            return false;
        }

        if (document.MinimumLevel is not null
            && document.MaximumLevel is not null
            && document.MinimumLevel > document.MaximumLevel)
        {
            failureReason =
                $"EnhancementSet at index {index} minimum level exceeds its maximum level.";
            return false;
        }

        setRecord = new EnhancementSetReferenceRecord
        {
            CatalogItemId = document.CatalogItemId.Trim(),
            CurrentDisplayName = document.CurrentDisplayName.Trim(),
            ActiveStatus = activeStatus,
            ServerAvailability = serverAvailability,
            VerificationStatus = verificationStatus,
            HomecomingSetId = NormalizeOptional(document.HomecomingSetId),
            CategoryCode = NormalizeOptional(document.CategoryCode),
            CategoryDisplayText = NormalizeOptional(document.CategoryDisplayText),
            RarityCode = NormalizeOptional(document.RarityCode),
            RarityDisplayText = NormalizeOptional(document.RarityDisplayText),
            MinimumLevel = document.MinimumLevel,
            MaximumLevel = document.MaximumLevel,
            Bonuses = bonuses
        };

        if (setRecord.CategoryDisplayText is not null && setRecord.CategoryCode is null)
        {
            failureReason =
                $"EnhancementSet at index {index} has CategoryDisplayText without CategoryCode.";
            return false;
        }

        return true;
    }

    private static bool TryParseSourceVariants(
        List<EnhancementSourceVariantReferenceRecordDocument>? documents,
        int itemIndex,
        out IReadOnlyList<EnhancementSourceVariantReferenceRecord> variants,
        out string failureReason)
    {
        failureReason = string.Empty;
        if (documents is null || documents.Count == 0)
        {
            variants = Array.Empty<EnhancementSourceVariantReferenceRecord>();
            return true;
        }

        var parsed = new List<EnhancementSourceVariantReferenceRecord>(documents.Count);
        var sourceIds = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < documents.Count; index++)
        {
            var document = documents[index];
            var sourceId = NormalizeOptional(document.HomecomingSourceId);
            var sourceForm = NormalizeOptional(document.SourceForm);
            if (sourceId is null || sourceForm is null)
            {
                failureReason =
                    $"Item at index {itemIndex} sourceVariants[{index}] requires HomecomingSourceId and SourceForm.";
                variants = Array.Empty<EnhancementSourceVariantReferenceRecord>();
                return false;
            }

            if (!sourceIds.Add(sourceId))
            {
                failureReason =
                    $"Item at index {itemIndex} sourceVariants contains duplicate HomecomingSourceId '{sourceId}'.";
                variants = Array.Empty<EnhancementSourceVariantReferenceRecord>();
                return false;
            }

            if (!TryParseVariantEffects(
                    document.Effects,
                    itemIndex,
                    index,
                    out var effects,
                    out failureReason))
            {
                variants = Array.Empty<EnhancementSourceVariantReferenceRecord>();
                return false;
            }

            parsed.Add(new EnhancementSourceVariantReferenceRecord
            {
                HomecomingSourceId = sourceId,
                SourceForm = sourceForm,
                Icon = NormalizeOptional(document.Icon),
                DisplayHelp = NormalizeOptional(document.DisplayHelp),
                ShortHelp = NormalizeOptional(document.ShortHelp),
                BoostUsePlayerLevel = document.BoostUsePlayerLevel ?? false,
                MaxBoostLevel = document.MaxBoostLevel ?? 0,
                BoostBoostable = document.BoostBoostable ?? false,
                Effects = effects
            });
        }

        variants = parsed
            .OrderBy(value => value.HomecomingSourceId, StringComparer.Ordinal)
            .ToArray();
        return true;
    }

    private static bool TryParseBonuses(
        List<EnhancementSetBonusReferenceRecordDocument>? documents,
        int setIndex,
        out IReadOnlyList<EnhancementSetBonusReferenceRecord> bonuses,
        out string failureReason)
    {
        failureReason = string.Empty;
        if (documents is null || documents.Count == 0)
        {
            bonuses = Array.Empty<EnhancementSetBonusReferenceRecord>();
            return true;
        }

        var parsed = new List<EnhancementSetBonusReferenceRecord>(documents.Count);
        for (var index = 0; index < documents.Count; index++)
        {
            var document = documents[index];
            if (document.MinimumBoosts < 0
                || document.MaximumBoosts < 0
                || document.MinimumBoosts > document.MaximumBoosts)
            {
                failureReason =
                    $"EnhancementSet at index {setIndex} bonuses[{index}] has an invalid piece-count range.";
                bonuses = Array.Empty<EnhancementSetBonusReferenceRecord>();
                return false;
            }

            if (document.AutoPowers is null || document.AutoPowers.Count == 0)
            {
                failureReason =
                    $"EnhancementSet at index {setIndex} bonuses[{index}] has no autoPowers.";
                bonuses = Array.Empty<EnhancementSetBonusReferenceRecord>();
                return false;
            }

            if (!TryParseRequiresPattern(
                    document.RequiresPattern,
                    setIndex,
                    index,
                    out var requiresPattern,
                    out failureReason))
            {
                bonuses = Array.Empty<EnhancementSetBonusReferenceRecord>();
                return false;
            }

            var requiresTokens = document.RequiresTokens?.ToArray() ?? Array.Empty<string>();
            if (requiresPattern == ReferenceEnhancementSetBonusRequiresPattern.None)
            {
                if (requiresTokens.Length > 0)
                {
                    failureReason =
                        $"EnhancementSet at index {setIndex} bonuses[{index}] has RequiresTokens without a Requires expression.";
                    bonuses = Array.Empty<EnhancementSetBonusReferenceRecord>();
                    return false;
                }
            }
            else if (requiresTokens.Length == 0)
            {
                failureReason =
                    $"EnhancementSet at index {setIndex} bonuses[{index}] requires RequiresTokens.";
                bonuses = Array.Empty<EnhancementSetBonusReferenceRecord>();
                return false;
            }

            var requiredEnhancementIds = document.RequiredEnhancementIds?.ToArray() ?? Array.Empty<string>();
            if (requiresPattern == ReferenceEnhancementSetBonusRequiresPattern.PieceGate
                && requiredEnhancementIds.Length == 0)
            {
                failureReason =
                    $"EnhancementSet at index {setIndex} bonuses[{index}] has PieceGate Requires without RequiredEnhancementIds.";
                bonuses = Array.Empty<EnhancementSetBonusReferenceRecord>();
                return false;
            }

            if (requiresPattern != ReferenceEnhancementSetBonusRequiresPattern.PieceGate
                && requiredEnhancementIds.Length > 0)
            {
                failureReason =
                    $"EnhancementSet at index {setIndex} bonuses[{index}] has RequiredEnhancementIds without a PieceGate Requires expression.";
                bonuses = Array.Empty<EnhancementSetBonusReferenceRecord>();
                return false;
            }

            foreach (var requiredEnhancementId in requiredEnhancementIds)
            {
                if (string.IsNullOrWhiteSpace(requiredEnhancementId))
                {
                    failureReason =
                        $"EnhancementSet at index {setIndex} bonuses[{index}] has an empty RequiredEnhancementId.";
                    bonuses = Array.Empty<EnhancementSetBonusReferenceRecord>();
                    return false;
                }

                if (!ItemReferenceIdRules.TryValidateItemId(
                        requiredEnhancementId,
                        ReferenceItemFamily.Enhancement,
                        out var requiredFailure))
                {
                    failureReason =
                        $"EnhancementSet at index {setIndex} bonuses[{index}] has invalid RequiredEnhancementId '{requiredEnhancementId}': {requiredFailure}";
                    bonuses = Array.Empty<EnhancementSetBonusReferenceRecord>();
                    return false;
                }
            }

            var autoPowers = new List<EnhancementSetBonusPowerReferenceRecord>(document.AutoPowers.Count);
            var powerIds = new HashSet<string>(StringComparer.Ordinal);
            for (var powerIndex = 0; powerIndex < document.AutoPowers.Count; powerIndex++)
            {
                var power = document.AutoPowers[powerIndex];
                var sourceId = NormalizeOptional(power.HomecomingSourceId);
                if (sourceId is null)
                {
                    failureReason =
                        $"EnhancementSet at index {setIndex} bonuses[{index}].autoPowers[{powerIndex}] is missing HomecomingSourceId.";
                    bonuses = Array.Empty<EnhancementSetBonusReferenceRecord>();
                    return false;
                }

                if (!powerIds.Add(sourceId))
                {
                    failureReason =
                        $"EnhancementSet at index {setIndex} bonuses[{index}] has duplicate autoPower '{sourceId}'.";
                    bonuses = Array.Empty<EnhancementSetBonusReferenceRecord>();
                    return false;
                }

                autoPowers.Add(new EnhancementSetBonusPowerReferenceRecord
                {
                    HomecomingSourceId = sourceId,
                    DisplayHelp = NormalizeOptional(power.DisplayHelp),
                    BoostUsePlayerLevel = power.BoostUsePlayerLevel ?? false,
                    MaxBoostLevel = power.MaxBoostLevel ?? 0,
                    BoostBoostable = power.BoostBoostable ?? false,
                    Effects = ParseEffectDocuments(power.Effects)
                });
            }

            parsed.Add(new EnhancementSetBonusReferenceRecord
            {
                MinimumBoosts = document.MinimumBoosts,
                MaximumBoosts = document.MaximumBoosts,
                RequiresPattern = requiresPattern,
                RequiresTokens = requiresTokens,
                RequiredEnhancementIds = requiredEnhancementIds
                    .OrderBy(value => value, StringComparer.Ordinal)
                    .ToArray(),
                AutoPowers = autoPowers
            });
        }

        bonuses = parsed;
        return true;
    }

    private static bool TryParseNamedTables(
        List<EnhancementResolverNamedTableReferenceRecordDocument>? documents,
        out IReadOnlyDictionary<string, IReadOnlyList<float>> namedTables,
        out string failureReason)
    {
        failureReason = string.Empty;
        namedTables = new Dictionary<string, IReadOnlyList<float>>(StringComparer.Ordinal);
        if (documents is null || documents.Count == 0)
        {
            return true;
        }

        var parsed = new Dictionary<string, IReadOnlyList<float>>(StringComparer.Ordinal);
        foreach (var (index, document) in documents.Select((value, index) => (index, value)))
        {
            var name = NormalizeOptional(document.Name);
            if (name is null)
            {
                failureReason = $"EnhancementResolverNamedTables[{index}] is missing Name.";
                return false;
            }

            if (parsed.ContainsKey(name))
            {
                failureReason = $"EnhancementResolverNamedTables contains duplicate Name '{name}'.";
                return false;
            }

            if (document.Values is null || document.Values.Count == 0)
            {
                failureReason = $"EnhancementResolverNamedTables[{index}] '{name}' has no Values.";
                return false;
            }

            for (var valueIndex = 0; valueIndex < document.Values.Count; valueIndex++)
            {
                if (!float.IsFinite(document.Values[valueIndex]))
                {
                    failureReason =
                        $"EnhancementResolverNamedTables[{index}] '{name}' value[{valueIndex}] is not finite.";
                    return false;
                }
            }

            parsed[name] = document.Values.ToArray();
        }

        namedTables = parsed;
        return true;
    }

    private static bool TryParseVariantEffects(
        List<EnhancementSourceVariantEffectReferenceRecordDocument>? documents,
        int itemIndex,
        int variantIndex,
        out IReadOnlyList<EnhancementSourceVariantEffectReferenceRecord> effects,
        out string failureReason)
    {
        failureReason = string.Empty;
        if (documents is null || documents.Count == 0)
        {
            effects = Array.Empty<EnhancementSourceVariantEffectReferenceRecord>();
            return true;
        }

        effects = ParseEffectDocuments(documents, out failureReason, itemIndex, variantIndex);
        return string.IsNullOrEmpty(failureReason);
    }

    private static IReadOnlyList<EnhancementSourceVariantEffectReferenceRecord> ParseEffectDocuments(
        List<EnhancementSourceVariantEffectReferenceRecordDocument>? documents,
        out string failureReason,
        int? itemIndex = null,
        int? variantIndex = null)
    {
        failureReason = string.Empty;
        if (documents is null || documents.Count == 0)
        {
            return Array.Empty<EnhancementSourceVariantEffectReferenceRecord>();
        }

        var parsed = new List<EnhancementSourceVariantEffectReferenceRecord>(documents.Count);
        for (var index = 0; index < documents.Count; index++)
        {
            var document = documents[index];
            var tag = document.Tag ?? string.Empty;
            var table = NormalizeOptional(document.Table);
            if (table is null)
            {
                failureReason = DescribeEffectPath(itemIndex, variantIndex, index)
                    + " requires Table.";
                return Array.Empty<EnhancementSourceVariantEffectReferenceRecord>();
            }

            if (document.Scale is not float scale || !float.IsFinite(scale))
            {
                failureReason = DescribeEffectPath(itemIndex, variantIndex, index)
                    + " requires a finite Scale.";
                return Array.Empty<EnhancementSourceVariantEffectReferenceRecord>();
            }

            parsed.Add(new EnhancementSourceVariantEffectReferenceRecord
            {
                Tag = tag,
                Table = table,
                Scale = scale,
                AttribIds = document.AttribIds?.ToArray() ?? Array.Empty<uint>()
            });
        }

        return parsed;
    }

    private static IReadOnlyList<EnhancementSourceVariantEffectReferenceRecord> ParseEffectDocuments(
        List<EnhancementSourceVariantEffectReferenceRecordDocument>? documents) =>
        ParseEffectDocuments(documents, out _);

    private static string DescribeEffectPath(int? itemIndex, int? variantIndex, int effectIndex)
    {
        if (itemIndex is null)
        {
            return $"Effect[{effectIndex}]";
        }

        return variantIndex is null
            ? $"Item at index {itemIndex} effect[{effectIndex}]"
            : $"Item at index {itemIndex} sourceVariants[{variantIndex}] effect[{effectIndex}]";
    }

    private static bool TryParseRequiresPattern(
        string? value,
        int setIndex,
        int bonusIndex,
        out ReferenceEnhancementSetBonusRequiresPattern pattern,
        out string failureReason)
    {
        failureReason = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            pattern = ReferenceEnhancementSetBonusRequiresPattern.None;
            return true;
        }

        if (!Enum.TryParse(value, ignoreCase: true, out pattern)
            || !Enum.IsDefined(pattern))
        {
            failureReason =
                $"EnhancementSet at index {setIndex} bonuses[{bonusIndex}] has unknown RequiresPattern '{value}'.";
            pattern = ReferenceEnhancementSetBonusRequiresPattern.None;
            return false;
        }

        return true;
    }

    private static bool TryParseAlias(
        ItemReferenceAliasRecordDocument document,
        int index,
        out ItemReferenceAliasRecord alias,
        out string failureReason)
    {
        alias = null!;
        failureReason = string.Empty;

        if (string.IsNullOrWhiteSpace(document.CatalogItemId))
        {
            failureReason = $"Alias at index {index} is missing CatalogItemId.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(document.Locale))
        {
            failureReason = $"Alias at index {index} is missing Locale.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(document.Text))
        {
            failureReason = $"Alias at index {index} is missing Text.";
            return false;
        }

        if (!TryParseNameKind(document.NameKind, out var nameKind, out failureReason))
        {
            failureReason = $"Alias at index {index}: {failureReason}";
            return false;
        }

        alias = new ItemReferenceAliasRecord
        {
            CatalogItemId = document.CatalogItemId.Trim(),
            Locale = document.Locale.Trim(),
            Text = document.Text,
            NameKind = nameKind,
            IsPreferred = document.IsPreferred
        };

        return true;
    }

    private static bool TryParseFamily(string? value, out ReferenceItemFamily family, out string failureReason)
    {
        failureReason = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            failureReason = "Family is required.";
            family = default;
            return false;
        }

        if (!Enum.TryParse(value.Trim(), ignoreCase: true, out family))
        {
            failureReason = $"Unknown family '{value}'.";
            return false;
        }

        return true;
    }

    private static bool TryParseServerAvailability(
        List<ReferenceServerAvailabilityDocument>? documents,
        int index,
        bool requiresHomecoming,
        out IReadOnlyList<ReferenceServerAvailability> availability,
        out string failureReason)
    {
        availability = Array.Empty<ReferenceServerAvailability>();
        failureReason = string.Empty;

        if (documents is null || documents.Count == 0)
        {
            if (requiresHomecoming)
            {
                failureReason = $"Record at index {index} is missing required Homecoming ServerAvailability.";
                return false;
            }

            return true;
        }

        if (documents.Count > 1)
        {
            failureReason = $"Record at index {index} has duplicate ServerAvailability entries.";
            return false;
        }

        var document = documents[0];
        if (string.IsNullOrWhiteSpace(document.ServerKey))
        {
            failureReason = $"Record at index {index} ServerAvailability is missing ServerKey.";
            return false;
        }

        if (!string.Equals(document.ServerKey.Trim(), ReferenceServerKey.Homecoming, StringComparison.Ordinal))
        {
            failureReason =
                $"Record at index {index} has unknown ServerAvailability ServerKey '{document.ServerKey}'.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(document.Status))
        {
            failureReason = $"Record at index {index} ServerAvailability is missing Status.";
            return false;
        }

        if (!Enum.TryParse(document.Status.Trim(), ignoreCase: true, out ReferenceServerAvailabilityStatus status))
        {
            failureReason =
                $"Record at index {index} has unknown ServerAvailability Status '{document.Status}'.";
            return false;
        }

        availability =
        [
            ReferenceServerAvailabilitySupport.CreateHomecoming(status)
        ];
        return true;
    }

    private static bool TryParseActiveStatus(string? value, out ReferenceActiveStatus status, out string failureReason)
    {
        failureReason = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            failureReason = "ActiveStatus is required.";
            status = default;
            return false;
        }

        if (!Enum.TryParse(value.Trim(), ignoreCase: true, out status))
        {
            failureReason = $"Unknown ActiveStatus '{value}'.";
            return false;
        }

        return true;
    }

    private static bool TryParseVerificationStatus(
        string? value,
        out ReferenceVerificationStatus status,
        out string failureReason)
    {
        failureReason = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            failureReason = "VerificationStatus is required.";
            status = default;
            return false;
        }

        if (!Enum.TryParse(value.Trim(), ignoreCase: true, out status))
        {
            failureReason = $"Unknown VerificationStatus '{value}'.";
            return false;
        }

        return true;
    }

    private static bool TryParseNameKind(string? value, out ReferenceAliasNameKind nameKind, out string failureReason)
    {
        failureReason = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            failureReason = "NameKind is required.";
            nameKind = default;
            return false;
        }

        if (!Enum.TryParse(value.Trim(), ignoreCase: true, out nameKind))
        {
            failureReason = $"Unknown NameKind '{value}'.";
            return false;
        }

        return true;
    }

    private static bool HasInspirationHomecomingFields(ItemReferenceRecord item) =>
        item.HomecomingSourceId is not null
        || item.HomecomingCategory is not null
        || item.InspirationStandardTier is not null
        || item.InspirationForm is not null
        || item.DisplayNameMessageKey is not null
        || item.DisplayHelpMessageKey is not null
        || item.ShortHelpMessageKey is not null;

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool TryParseBadges(
        List<BadgeReferenceRecordDocument> documents,
        IReadOnlyDictionary<string, ItemReferenceRecord> items,
        out Dictionary<string, BadgeReferenceRecord> badges,
        out string failureReason)
    {
        badges = new Dictionary<string, BadgeReferenceRecord>(StringComparer.Ordinal);
        failureReason = string.Empty;
        foreach (var (index, document) in documents.Select((value, index) => (index, value)))
        {
            if (!TryParseBadge(document, index, out var badge, out failureReason))
            {
                return false;
            }

            if (!items.TryGetValue(badge.CatalogItemId, out var item)
                || item.Family is not ReferenceItemFamily.Badge)
            {
                failureReason =
                    $"Badge '{badge.CatalogItemId}' does not have a matching items[] Badge identity.";
                return false;
            }

            if (badges.ContainsKey(badge.CatalogItemId))
            {
                failureReason = $"Duplicate Badge CatalogItemId '{badge.CatalogItemId}'.";
                return false;
            }

            badges[badge.CatalogItemId] = badge;
        }

        return true;
    }

    private static bool TryParseBadge(
        BadgeReferenceRecordDocument document,
        int index,
        out BadgeReferenceRecord badge,
        out string failureReason)
    {
        badge = null!;
        failureReason = string.Empty;
        if (string.IsNullOrWhiteSpace(document.CatalogItemId))
        {
            failureReason = $"Badge at index {index} is missing CatalogItemId.";
            return false;
        }

        if (!ItemReferenceIdRules.TryValidateItemId(
                document.CatalogItemId,
                ReferenceItemFamily.Badge,
                out var idFailure))
        {
            failureReason = $"Badge at index {index}: {idFailure}";
            return false;
        }

        if (string.IsNullOrWhiteSpace(document.HomecomingSourceId))
        {
            failureReason = $"Badge at index {index} is missing HomecomingSourceId.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(document.CanonicalCategory))
        {
            failureReason = $"Badge at index {index} is missing CanonicalCategory.";
            return false;
        }

        if (!document.BadgeType.HasValue)
        {
            failureReason = $"Badge at index {index} is missing BadgeType.";
            return false;
        }

        if (!TryParseBadgeKind(document.ReferenceKind, out var referenceKind, out failureReason))
        {
            failureReason = $"Badge at index {index}: {failureReason}";
            return false;
        }

        if (string.IsNullOrWhiteSpace(document.HeroName) || string.IsNullOrWhiteSpace(document.VillainName))
        {
            failureReason = $"Badge at index {index} is missing HeroName or VillainName.";
            return false;
        }

        if (!TryParseVerificationStatus(document.VerificationStatus, out var verificationStatus, out var verificationFailure))
        {
            failureReason = $"Badge at index {index}: {verificationFailure}";
            return false;
        }

        ReferenceRequirementLogicStatus? requirementLogicStatus = null;
        if (!string.IsNullOrWhiteSpace(document.RequirementLogicStatus))
        {
            if (!Enum.TryParse(document.RequirementLogicStatus.Trim(), ignoreCase: true, out ReferenceRequirementLogicStatus parsed))
            {
                failureReason = $"Badge at index {index} has unknown RequirementLogicStatus '{document.RequirementLogicStatus}'.";
                return false;
            }

            requirementLogicStatus = parsed;
        }

        ReferenceRequirementLogicPattern? requirementLogicPattern = null;
        if (!string.IsNullOrWhiteSpace(document.RequirementLogicPattern))
        {
            if (!Enum.TryParse(document.RequirementLogicPattern.Trim(), ignoreCase: true, out ReferenceRequirementLogicPattern parsed))
            {
                failureReason = $"Badge at index {index} has unknown RequirementLogicPattern '{document.RequirementLogicPattern}'.";
                return false;
            }

            requirementLogicPattern = parsed;
        }

        badge = new BadgeReferenceRecord
        {
            CatalogItemId = document.CatalogItemId.Trim(),
            HomecomingSourceId = document.HomecomingSourceId.Trim(),
            SetTitleId = document.SetTitleId,
            CanonicalCategory = document.CanonicalCategory.Trim(),
            BadgeType = document.BadgeType.Value,
            ReferenceKind = referenceKind,
            HeroName = document.HeroName.Trim(),
            VillainName = document.VillainName.Trim(),
            HeroDescription = NormalizeOptional(document.HeroDescription),
            VillainDescription = NormalizeOptional(document.VillainDescription),
            HeroIcon = NormalizeOptional(document.HeroIcon),
            VillainIcon = NormalizeOptional(document.VillainIcon),
            ZoneId = NormalizeOptional(document.ZoneId),
            CompletionBadgeId = NormalizeOptional(document.CompletionBadgeId),
            IsZoneCompletionBadge = document.IsZoneCompletionBadge ?? false,
            VerificationStatus = verificationStatus,
            RequirementText = NormalizeOptional(document.RequirementText),
            RewardText = NormalizeOptional(document.RewardText),
            RequirementLogicStatus = requirementLogicStatus,
            RequirementLogicPattern = requirementLogicPattern
        };

        return true;
    }

    private static bool TryParseZones(
        List<ZoneReferenceRecordDocument> documents,
        out Dictionary<string, ZoneReferenceRecord> zones,
        out string failureReason)
    {
        zones = new Dictionary<string, ZoneReferenceRecord>(StringComparer.Ordinal);
        failureReason = string.Empty;
        foreach (var (index, document) in documents.Select((value, index) => (index, value)))
        {
            if (string.IsNullOrWhiteSpace(document.ZoneId))
            {
                failureReason = $"Zone at index {index} is missing ZoneId.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(document.DisplayName))
            {
                failureReason = $"Zone at index {index} is missing DisplayName.";
                return false;
            }

            if (zones.ContainsKey(document.ZoneId.Trim()))
            {
                failureReason = $"Duplicate ZoneId '{document.ZoneId}'.";
                return false;
            }

            zones[document.ZoneId.Trim()] = new ZoneReferenceRecord
            {
                ZoneId = document.ZoneId.Trim(),
                DisplayName = document.DisplayName.Trim(),
                AlignmentNotes = NormalizeOptional(document.AlignmentNotes),
                LevelRange = NormalizeOptional(document.LevelRange),
                ZoneType = NormalizeOptional(document.ZoneType),
                ExplorationCompletionBadgeIds = document.ExplorationCompletionBadgeIds?
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => value.Trim())
                    .OrderBy(value => value, StringComparer.Ordinal)
                    .ToArray()
                    ?? [],
                HistoryCompletionBadgeIds = document.HistoryCompletionBadgeIds?
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => value.Trim())
                    .OrderBy(value => value, StringComparer.Ordinal)
                    .ToArray()
                    ?? []
            };
        }

        return true;
    }

    private static bool TryParseBadgeLocations(
        List<BadgeLocationReferenceRecordDocument> documents,
        IReadOnlyDictionary<string, BadgeReferenceRecord> badges,
        IReadOnlyDictionary<string, ZoneReferenceRecord> zones,
        out IReadOnlyList<BadgeLocationReferenceRecord> badgeLocations,
        out string failureReason)
    {
        var parsed = new List<BadgeLocationReferenceRecord>();
        badgeLocations = parsed;
        failureReason = string.Empty;
        foreach (var (index, document) in documents.Select((value, index) => (index, value)))
        {
            if (string.IsNullOrWhiteSpace(document.BadgeCatalogItemId)
                || !badges.ContainsKey(document.BadgeCatalogItemId))
            {
                failureReason = $"BadgeLocation at index {index} references unknown badge '{document.BadgeCatalogItemId}'.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(document.ZoneId) || !zones.ContainsKey(document.ZoneId))
            {
                failureReason = $"BadgeLocation at index {index} references unknown zone '{document.ZoneId}'.";
                return false;
            }

            if (!document.LocationIndex.HasValue)
            {
                failureReason = $"BadgeLocation at index {index} is missing LocationIndex.";
                return false;
            }

            if (!TryParseVerificationStatus(document.VerificationStatus, out var verificationStatus, out var verificationFailure))
            {
                failureReason = $"BadgeLocation at index {index}: {verificationFailure}";
                return false;
            }

            parsed.Add(new BadgeLocationReferenceRecord
            {
                BadgeCatalogItemId = document.BadgeCatalogItemId.Trim(),
                ZoneId = document.ZoneId.Trim(),
                CoordinateX = document.CoordinateX,
                CoordinateY = document.CoordinateY,
                CoordinateZ = document.CoordinateZ,
                ThumbtackCommand = NormalizeOptional(document.ThumbtackCommand),
                MarkerType = NormalizeOptional(document.MarkerType),
                LocationRole = NormalizeOptional(document.LocationRole),
                CoordinateSemantics = NormalizeOptional(document.CoordinateSemantics),
                VerificationStatus = verificationStatus,
                LocationIndex = document.LocationIndex.Value,
                TriggerDescription = NormalizeOptional(document.TriggerDescription),
                ExplorationRouteOrder = document.ExplorationRouteOrder,
                RouteSourceProject = NormalizeOptional(document.RouteSourceProject),
                RouteSourceVersion = NormalizeOptional(document.RouteSourceVersion),
                RouteSourceUrl = NormalizeOptional(document.RouteSourceUrl),
                RouteMappingConfidence = NormalizeOptional(document.RouteMappingConfidence)
            });
        }

        badgeLocations = parsed;
        return true;
    }

    private static bool TryParseBadgeAccoladeRequirements(
        List<BadgeAccoladeRequirementRecordDocument> documents,
        IReadOnlyDictionary<string, BadgeReferenceRecord> badges,
        out IReadOnlyList<BadgeAccoladeRequirementRecord> requirements,
        out string failureReason)
    {
        var parsed = new List<BadgeAccoladeRequirementRecord>();
        requirements = parsed;
        failureReason = string.Empty;
        foreach (var (index, document) in documents.Select((value, index) => (index, value)))
        {
            if (string.IsNullOrWhiteSpace(document.AccoladeBadgeId)
                || !badges.ContainsKey(document.AccoladeBadgeId))
            {
                failureReason =
                    $"BadgeAccoladeRequirement at index {index} references unknown accolade '{document.AccoladeBadgeId}'.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(document.PrerequisiteBadgeId)
                || !badges.ContainsKey(document.PrerequisiteBadgeId))
            {
                failureReason =
                    $"BadgeAccoladeRequirement at index {index} references unknown prerequisite '{document.PrerequisiteBadgeId}'.";
                return false;
            }

            if (!document.PrerequisiteIndex.HasValue)
            {
                failureReason = $"BadgeAccoladeRequirement at index {index} is missing PrerequisiteIndex.";
                return false;
            }

            if (!TryParseRequirementLogicStatus(
                    document.RequirementLogicStatus,
                    out var requirementLogicStatus,
                    out failureReason))
            {
                failureReason = $"BadgeAccoladeRequirement at index {index}: {failureReason}";
                return false;
            }

            parsed.Add(new BadgeAccoladeRequirementRecord
            {
                AccoladeBadgeId = document.AccoladeBadgeId.Trim(),
                PrerequisiteBadgeId = document.PrerequisiteBadgeId.Trim(),
                PrerequisiteIndex = document.PrerequisiteIndex.Value,
                LogicGroup = document.LogicGroup ?? 0,
                RequirementLogicStatus = requirementLogicStatus
            });
        }

        requirements = parsed;
        return true;
    }

    private static bool TryParseBadgeKind(string? value, out ReferenceBadgeKind kind, out string failureReason)
    {
        failureReason = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            failureReason = "ReferenceKind is required.";
            kind = default;
            return false;
        }

        if (!Enum.TryParse(value.Trim(), ignoreCase: true, out kind))
        {
            failureReason = $"Unknown ReferenceKind '{value}'.";
            return false;
        }

        return true;
    }

    private static bool TryParseRequirementLogicStatus(
        string? value,
        out ReferenceRequirementLogicStatus status,
        out string failureReason)
    {
        failureReason = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            failureReason = "RequirementLogicStatus is required.";
            status = default;
            return false;
        }

        if (!Enum.TryParse(value.Trim(), ignoreCase: true, out status))
        {
            failureReason = $"Unknown RequirementLogicStatus '{value}'.";
            return false;
        }

        return true;
    }

    private static bool TryParseRouteOrderingProvenance(
        RouteOrderingProvenanceRecordDocument? document,
        out RouteOrderingProvenanceRecord? provenance,
        out string failureReason)
    {
        provenance = null;
        failureReason = string.Empty;
        if (document is null)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(document.SourceName)
            || string.IsNullOrWhiteSpace(document.Author)
            || string.IsNullOrWhiteSpace(document.Version))
        {
            failureReason = "RouteOrderingProvenance is missing required source metadata.";
            return false;
        }

        provenance = new RouteOrderingProvenanceRecord
        {
            SourceName = document.SourceName.Trim(),
            Author = document.Author.Trim(),
            Version = document.Version.Trim(),
            MapsThreadUrl = NormalizeOptional(document.MapsThreadUrl),
            PopmenuThreadUrl = NormalizeOptional(document.PopmenuThreadUrl),
            OrderingSemantics = NormalizeOptional(document.OrderingSemantics),
            Retrieved = NormalizeOptional(document.Retrieved)
        };

        return true;
    }

    private static bool TryParseHistoryPlaqueRouteCollections(
        List<HistoryPlaqueRouteCollectionRecordDocument> documents,
        IReadOnlyDictionary<string, BadgeReferenceRecord> badges,
        out IReadOnlyList<HistoryPlaqueRouteCollectionRecord> collections,
        out string failureReason)
    {
        var parsed = new List<HistoryPlaqueRouteCollectionRecord>();
        collections = parsed;
        failureReason = string.Empty;
        foreach (var (index, document) in documents.Select((value, index) => (index, value)))
        {
            if (string.IsNullOrWhiteSpace(document.CollectionName))
            {
                failureReason = $"HistoryPlaqueRouteCollection at index {index} is missing CollectionName.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(document.CompletionBadgeId)
                || !badges.ContainsKey(document.CompletionBadgeId))
            {
                failureReason =
                    $"HistoryPlaqueRouteCollection '{document.CollectionName}' references unknown completion badge '{document.CompletionBadgeId}'.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(document.OrderingStatus))
            {
                failureReason = $"HistoryPlaqueRouteCollection '{document.CollectionName}' is missing OrderingStatus.";
                return false;
            }

            parsed.Add(new HistoryPlaqueRouteCollectionRecord
            {
                CollectionName = document.CollectionName.Trim(),
                CompletionBadgeId = document.CompletionBadgeId.Trim(),
                PublishedCollectionOrderAvailable = document.PublishedCollectionOrderAvailable ?? false,
                OrderingStatus = document.OrderingStatus.Trim(),
                OrderingNotes = NormalizeOptional(document.OrderingNotes)
            });
        }

        collections = parsed;
        return true;
    }

    private static bool TryParseHistoryPlaqueRouteStops(
        List<HistoryPlaqueRouteStopRecordDocument> documents,
        IReadOnlyDictionary<string, BadgeReferenceRecord> badges,
        IReadOnlyDictionary<string, ZoneReferenceRecord> zones,
        IReadOnlyList<HistoryPlaqueRouteCollectionRecord> collections,
        out IReadOnlyList<HistoryPlaqueRouteStopRecord> stops,
        out string failureReason)
    {
        var parsed = new List<HistoryPlaqueRouteStopRecord>();
        stops = parsed;
        failureReason = string.Empty;
        var collectionNames = collections
            .Select(collection => collection.CollectionName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var (index, document) in documents.Select((value, index) => (index, value)))
        {
            if (string.IsNullOrWhiteSpace(document.CollectionName)
                || !collectionNames.Contains(document.CollectionName))
            {
                failureReason =
                    $"HistoryPlaqueRouteStop at index {index} references unknown collection '{document.CollectionName}'.";
                return false;
            }

            if (!document.InventoryOrder.HasValue)
            {
                failureReason = $"HistoryPlaqueRouteStop at index {index} is missing InventoryOrder.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(document.CompletionBadgeId)
                || !badges.ContainsKey(document.CompletionBadgeId))
            {
                failureReason =
                    $"HistoryPlaqueRouteStop at index {index} references unknown completion badge '{document.CompletionBadgeId}'.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(document.ZoneId) || !zones.ContainsKey(document.ZoneId))
            {
                failureReason =
                    $"HistoryPlaqueRouteStop at index {index} references unknown zone '{document.ZoneId}'.";
                return false;
            }

            if (!document.LocationIndex.HasValue)
            {
                failureReason = $"HistoryPlaqueRouteStop at index {index} is missing LocationIndex.";
                return false;
            }

            parsed.Add(new HistoryPlaqueRouteStopRecord
            {
                CollectionName = document.CollectionName.Trim(),
                InventoryOrder = document.InventoryOrder.Value,
                RouteOrder = document.RouteOrder,
                CompletionBadgeId = document.CompletionBadgeId.Trim(),
                ZoneId = document.ZoneId.Trim(),
                LocationIndex = document.LocationIndex.Value,
                PlaqueName = NormalizeOptional(document.PlaqueName),
                SourceZoneRouteOrder = document.SourceZoneRouteOrder,
                RouteMappingConfidence = NormalizeOptional(document.RouteMappingConfidence),
                RouteSourceProject = NormalizeOptional(document.RouteSourceProject),
                RouteSourceVersion = NormalizeOptional(document.RouteSourceVersion),
                RouteSourceUrl = NormalizeOptional(document.RouteSourceUrl)
            });
        }

        stops = parsed;
        return true;
    }
}

internal sealed class ItemReferenceCatalogLoadResult
{
    private ItemReferenceCatalogLoadResult(
        bool succeeded,
        string? failureReason,
        ItemReferenceManifest? manifest,
        Dictionary<string, ItemReferenceRecord>? items,
        Dictionary<string, EnhancementSetReferenceRecord>? enhancementSets,
        IReadOnlyDictionary<string, IReadOnlyList<float>>? enhancementResolverNamedTables,
        Dictionary<string, BadgeReferenceRecord>? badges,
        IReadOnlyList<BadgeLocationReferenceRecord>? badgeLocations,
        Dictionary<string, ZoneReferenceRecord>? zones,
        IReadOnlyList<BadgeAccoladeRequirementRecord>? badgeAccoladeRequirements,
        RouteOrderingProvenanceRecord? routeOrderingProvenance,
        IReadOnlyList<HistoryPlaqueRouteCollectionRecord>? historyPlaqueRouteCollections,
        IReadOnlyList<HistoryPlaqueRouteStopRecord>? historyPlaqueRouteStops,
        Dictionary<string, (string CatalogItemId, string MatchedAliasText)>? aliasIndex,
        Dictionary<string, (string CatalogItemId, string MatchedAliasText)>? allAliasIndex)
    {
        Succeeded = succeeded;
        FailureReason = failureReason;
        Manifest = manifest;
        Items = items;
        EnhancementSets = enhancementSets;
        EnhancementResolverNamedTables = enhancementResolverNamedTables;
        Badges = badges;
        BadgeLocations = badgeLocations;
        Zones = zones;
        BadgeAccoladeRequirements = badgeAccoladeRequirements;
        RouteOrderingProvenance = routeOrderingProvenance;
        HistoryPlaqueRouteCollections = historyPlaqueRouteCollections;
        HistoryPlaqueRouteStops = historyPlaqueRouteStops;
        AliasIndex = aliasIndex;
        AllAliasIndex = allAliasIndex;
    }

    public bool Succeeded { get; }

    public string? FailureReason { get; }

    public ItemReferenceManifest? Manifest { get; }

    public Dictionary<string, ItemReferenceRecord>? Items { get; }

    public Dictionary<string, EnhancementSetReferenceRecord>? EnhancementSets { get; }

    public IReadOnlyDictionary<string, IReadOnlyList<float>>? EnhancementResolverNamedTables { get; }

    public Dictionary<string, BadgeReferenceRecord>? Badges { get; }

    public IReadOnlyList<BadgeLocationReferenceRecord>? BadgeLocations { get; }

    public Dictionary<string, ZoneReferenceRecord>? Zones { get; }

    public IReadOnlyList<BadgeAccoladeRequirementRecord>? BadgeAccoladeRequirements { get; }

    public RouteOrderingProvenanceRecord? RouteOrderingProvenance { get; }

    public IReadOnlyList<HistoryPlaqueRouteCollectionRecord>? HistoryPlaqueRouteCollections { get; }

    public IReadOnlyList<HistoryPlaqueRouteStopRecord>? HistoryPlaqueRouteStops { get; }

    public Dictionary<string, (string CatalogItemId, string MatchedAliasText)>? AliasIndex { get; }

    public Dictionary<string, (string CatalogItemId, string MatchedAliasText)>? AllAliasIndex { get; }

    public static ItemReferenceCatalogLoadResult Failed(string failureReason) =>
        new(false, failureReason, null, null, null, null, null, null, null, null, null, null, null, null, null);

    public static ItemReferenceCatalogLoadResult Success(
        ItemReferenceManifest manifest,
        Dictionary<string, ItemReferenceRecord> items,
        Dictionary<string, EnhancementSetReferenceRecord> enhancementSets,
        IReadOnlyDictionary<string, IReadOnlyList<float>> enhancementResolverNamedTables,
        Dictionary<string, BadgeReferenceRecord> badges,
        IReadOnlyList<BadgeLocationReferenceRecord> badgeLocations,
        Dictionary<string, ZoneReferenceRecord> zones,
        IReadOnlyList<BadgeAccoladeRequirementRecord> badgeAccoladeRequirements,
        RouteOrderingProvenanceRecord? routeOrderingProvenance,
        IReadOnlyList<HistoryPlaqueRouteCollectionRecord> historyPlaqueRouteCollections,
        IReadOnlyList<HistoryPlaqueRouteStopRecord> historyPlaqueRouteStops,
        Dictionary<string, (string CatalogItemId, string MatchedAliasText)> aliasIndex,
        Dictionary<string, (string CatalogItemId, string MatchedAliasText)> allAliasIndex) =>
        new(
            true,
            null,
            manifest,
            items,
            enhancementSets,
            enhancementResolverNamedTables,
            badges,
            badgeLocations,
            zones,
            badgeAccoladeRequirements,
            routeOrderingProvenance,
            historyPlaqueRouteCollections,
            historyPlaqueRouteStops,
            aliasIndex,
            allAliasIndex);
}
