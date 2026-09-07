using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace CoHAnalytics.ReferenceData;

/// <summary>
/// Imports the authoritative JSON catalog into a derived SQLite reference database.
/// The JSON remains the source of truth; the database is a generated artifact.
/// </summary>
public static class ItemReferenceCatalogImporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static void ImportFromJsonStream(Stream jsonStream, string databasePath)
    {
        using var memory = new MemoryStream();
        jsonStream.CopyTo(memory);
        memory.Position = 0;

        var result = ItemReferenceCatalogLoader.Load(memory);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(result.FailureReason ?? "Catalog JSON load failed.");
        }

        memory.Position = 0;
        var document = JsonSerializer.Deserialize<ItemReferenceCatalogDocument>(memory, JsonOptions);
        if (document is null)
        {
            throw new InvalidOperationException("Catalog document is empty.");
        }

        ImportFromLoadResult(result, databasePath, document);
    }

    /// <summary>
    /// Imports a catalog JSON file from disk into a derived SQLite database.
    /// Preferred for production generation so the authored JSON is used directly
    /// rather than a possibly stale embedded assembly resource.
    /// </summary>
    public static void ImportFromJsonFile(string catalogJsonPath, string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogJsonPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        var fullPath = Path.GetFullPath(catalogJsonPath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Catalog JSON path '{fullPath}' was not found.", fullPath);
        }

        using var stream = File.OpenRead(fullPath);
        ImportFromJsonStream(stream, databasePath);
    }

    public static void ImportEmbeddedProduction(string databasePath)
    {
        var assembly = typeof(ItemReferenceCatalogImporter).Assembly;
        using var stream = assembly.GetManifestResourceStream(ItemReferenceCatalogFactory.ProductionCatalogResourceName);
        if (stream is null)
        {
            throw new InvalidOperationException(
                $"Embedded catalog resource '{ItemReferenceCatalogFactory.ProductionCatalogResourceName}' was not found.");
        }

        ImportFromJsonStream(stream, databasePath);
    }

    internal static void ImportFromLoadResult(
        ItemReferenceCatalogLoadResult result,
        string databasePath,
        ItemReferenceCatalogDocument? document = null)
    {
        if (!result.Succeeded ||
            result.Manifest is null ||
            result.Items is null ||
            result.EnhancementSets is null ||
            result.Badges is null ||
            result.Zones is null ||
            result.AliasIndex is null)
        {
            throw new InvalidOperationException(result.FailureReason ?? "Catalog load result is incomplete.");
        }

        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (File.Exists(databasePath))
        {
            File.Delete(databasePath);
        }

        using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        connection.Open();
        CreateSchema(connection);

        using var transaction = connection.BeginTransaction();
        InsertEnhancementSets(connection, transaction, result.EnhancementSets.Values);
        InsertItems(connection, transaction, result.Items.Values);
        InsertEnhancementSetBonuses(connection, transaction, result.EnhancementSets.Values);
        InsertEnhancementResolverNamedTables(connection, transaction, result.EnhancementResolverNamedTables);
        InsertEnhancementSourceVariants(connection, transaction, result.Items.Values);
        InsertEnhancementSourceVariantEffects(connection, transaction, result.Items.Values);
        InsertEnhancementSetBonusPowerEffects(connection, transaction, result.EnhancementSets.Values);
        InsertRecipeLevels(connection, transaction, result.Items.Values);
        InsertServerAvailability(connection, transaction, result.Items.Values, result.EnhancementSets.Values);
        InsertZones(connection, transaction, result.Zones.Values);
        InsertBadges(connection, transaction, result.Badges.Values);
        InsertZoneCompletionBadges(connection, transaction, result.Zones.Values);
        InsertBadgeLocations(connection, transaction, result.BadgeLocations ?? []);
        InsertBadgeAccoladeRequirements(connection, transaction, result.BadgeAccoladeRequirements ?? []);
        InsertRouteOrderingProvenance(connection, transaction, result.RouteOrderingProvenance);
        InsertHistoryPlaqueRouteCollections(connection, transaction, result.HistoryPlaqueRouteCollections ?? []);
        InsertHistoryPlaqueRouteStops(connection, transaction, result.HistoryPlaqueRouteStops ?? []);
        if (document is not null)
        {
            InsertAliases(connection, transaction, document.Aliases);
        }
        else
        {
            InsertAliasesFromIndex(connection, transaction, result.AliasIndex);
        }

        InsertManifest(connection, transaction, result.Manifest);
        transaction.Commit();
    }

    private static void CreateSchema(SqliteConnection connection)
    {
        foreach (var statement in ReferenceDatabaseSchema.CreateTableStatements)
        {
            using var command = connection.CreateCommand();
            command.CommandText = statement;
            command.ExecuteNonQuery();
        }
    }

    private static void InsertEnhancementSets(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IEnumerable<EnhancementSetReferenceRecord> sets)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO EnhancementSet (
                catalog_item_id,
                current_display_name,
                active_status,
                verification_status,
                homecoming_set_id,
                category_code,
                category_display_text,
                rarity_code,
                rarity_display_text,
                minimum_level,
                maximum_level)
            VALUES (
                $id,
                $displayName,
                $activeStatus,
                $verificationStatus,
                $homecomingSetId,
                $categoryCode,
                $categoryDisplayText,
                $rarityCode,
                $rarityDisplayText,
                $minimumLevel,
                $maximumLevel);
            """;

        var id = command.CreateParameter();
        id.ParameterName = "$id";
        command.Parameters.Add(id);

        var displayName = command.CreateParameter();
        displayName.ParameterName = "$displayName";
        command.Parameters.Add(displayName);

        var activeStatus = command.CreateParameter();
        activeStatus.ParameterName = "$activeStatus";
        command.Parameters.Add(activeStatus);

        var verificationStatus = command.CreateParameter();
        verificationStatus.ParameterName = "$verificationStatus";
        command.Parameters.Add(verificationStatus);

        var homecomingSetId = command.CreateParameter();
        homecomingSetId.ParameterName = "$homecomingSetId";
        command.Parameters.Add(homecomingSetId);

        var categoryCode = command.CreateParameter();
        categoryCode.ParameterName = "$categoryCode";
        command.Parameters.Add(categoryCode);

        var categoryDisplayText = command.CreateParameter();
        categoryDisplayText.ParameterName = "$categoryDisplayText";
        command.Parameters.Add(categoryDisplayText);

        var rarityCode = command.CreateParameter();
        rarityCode.ParameterName = "$rarityCode";
        command.Parameters.Add(rarityCode);

        var rarityDisplayText = command.CreateParameter();
        rarityDisplayText.ParameterName = "$rarityDisplayText";
        command.Parameters.Add(rarityDisplayText);

        var minimumLevel = command.CreateParameter();
        minimumLevel.ParameterName = "$minimumLevel";
        command.Parameters.Add(minimumLevel);

        var maximumLevel = command.CreateParameter();
        maximumLevel.ParameterName = "$maximumLevel";
        command.Parameters.Add(maximumLevel);

        foreach (var set in sets)
        {
            id.Value = set.CatalogItemId;
            displayName.Value = set.CurrentDisplayName;
            activeStatus.Value = set.ActiveStatus.ToString();
            verificationStatus.Value = set.VerificationStatus.ToString();
            homecomingSetId.Value = (object?)set.HomecomingSetId ?? DBNull.Value;
            categoryCode.Value = (object?)set.CategoryCode ?? DBNull.Value;
            categoryDisplayText.Value = (object?)set.CategoryDisplayText ?? DBNull.Value;
            rarityCode.Value = (object?)set.RarityCode ?? DBNull.Value;
            rarityDisplayText.Value = (object?)set.RarityDisplayText ?? DBNull.Value;
            minimumLevel.Value = (object?)set.MinimumLevel ?? DBNull.Value;
            maximumLevel.Value = (object?)set.MaximumLevel ?? DBNull.Value;
            command.ExecuteNonQuery();
        }
    }

    private static void InsertEnhancementSetBonuses(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IEnumerable<EnhancementSetReferenceRecord> sets)
    {
        using var bonusCommand = connection.CreateCommand();
        bonusCommand.Transaction = transaction;
        bonusCommand.CommandText =
            """
            INSERT INTO EnhancementSetBonus (
                set_catalog_item_id,
                bonus_index,
                minimum_boosts,
                maximum_boosts,
                requires_pattern)
            VALUES ($setId, $bonusIndex, $minimumBoosts, $maximumBoosts, $requiresPattern);
            """;

        var setId = bonusCommand.CreateParameter();
        setId.ParameterName = "$setId";
        bonusCommand.Parameters.Add(setId);
        var bonusIndex = bonusCommand.CreateParameter();
        bonusIndex.ParameterName = "$bonusIndex";
        bonusCommand.Parameters.Add(bonusIndex);
        var minimumBoosts = bonusCommand.CreateParameter();
        minimumBoosts.ParameterName = "$minimumBoosts";
        bonusCommand.Parameters.Add(minimumBoosts);
        var maximumBoosts = bonusCommand.CreateParameter();
        maximumBoosts.ParameterName = "$maximumBoosts";
        bonusCommand.Parameters.Add(maximumBoosts);
        var requiresPattern = bonusCommand.CreateParameter();
        requiresPattern.ParameterName = "$requiresPattern";
        bonusCommand.Parameters.Add(requiresPattern);

        using var tokenCommand = connection.CreateCommand();
        tokenCommand.Transaction = transaction;
        tokenCommand.CommandText =
            """
            INSERT INTO EnhancementSetBonusRequiresToken (
                set_catalog_item_id,
                bonus_index,
                token_index,
                token)
            VALUES ($setId, $bonusIndex, $tokenIndex, $token);
            """;
        var tokenSetId = tokenCommand.CreateParameter();
        tokenSetId.ParameterName = "$setId";
        tokenCommand.Parameters.Add(tokenSetId);
        var tokenBonusIndex = tokenCommand.CreateParameter();
        tokenBonusIndex.ParameterName = "$bonusIndex";
        tokenCommand.Parameters.Add(tokenBonusIndex);
        var tokenIndex = tokenCommand.CreateParameter();
        tokenIndex.ParameterName = "$tokenIndex";
        tokenCommand.Parameters.Add(tokenIndex);
        var token = tokenCommand.CreateParameter();
        token.ParameterName = "$token";
        tokenCommand.Parameters.Add(token);

        using var requiredCommand = connection.CreateCommand();
        requiredCommand.Transaction = transaction;
        requiredCommand.CommandText =
            """
            INSERT INTO EnhancementSetBonusRequiredEnhancement (
                set_catalog_item_id,
                bonus_index,
                enhancement_catalog_item_id)
            VALUES ($setId, $bonusIndex, $enhancementId);
            """;
        var requiredSetId = requiredCommand.CreateParameter();
        requiredSetId.ParameterName = "$setId";
        requiredCommand.Parameters.Add(requiredSetId);
        var requiredBonusIndex = requiredCommand.CreateParameter();
        requiredBonusIndex.ParameterName = "$bonusIndex";
        requiredCommand.Parameters.Add(requiredBonusIndex);
        var enhancementId = requiredCommand.CreateParameter();
        enhancementId.ParameterName = "$enhancementId";
        requiredCommand.Parameters.Add(enhancementId);

        using var powerCommand = connection.CreateCommand();
        powerCommand.Transaction = transaction;
        powerCommand.CommandText =
            """
            INSERT INTO EnhancementSetBonusPower (
                set_catalog_item_id,
                bonus_index,
                power_index,
                homecoming_source_id,
                display_help,
                boost_use_player_level,
                max_boost_level,
                boost_boostable)
            VALUES ($setId, $bonusIndex, $powerIndex, $homecomingSourceId, $displayHelp, $boostUsePlayerLevel, $maxBoostLevel, $boostBoostable);
            """;

        var powerSetId = powerCommand.CreateParameter();
        powerSetId.ParameterName = "$setId";
        powerCommand.Parameters.Add(powerSetId);
        var powerBonusIndex = powerCommand.CreateParameter();
        powerBonusIndex.ParameterName = "$bonusIndex";
        powerCommand.Parameters.Add(powerBonusIndex);
        var powerIndex = powerCommand.CreateParameter();
        powerIndex.ParameterName = "$powerIndex";
        powerCommand.Parameters.Add(powerIndex);
        var homecomingSourceId = powerCommand.CreateParameter();
        homecomingSourceId.ParameterName = "$homecomingSourceId";
        powerCommand.Parameters.Add(homecomingSourceId);
        var displayHelp = powerCommand.CreateParameter();
        displayHelp.ParameterName = "$displayHelp";
        powerCommand.Parameters.Add(displayHelp);
        var boostUsePlayerLevel = powerCommand.CreateParameter();
        boostUsePlayerLevel.ParameterName = "$boostUsePlayerLevel";
        powerCommand.Parameters.Add(boostUsePlayerLevel);
        var maxBoostLevel = powerCommand.CreateParameter();
        maxBoostLevel.ParameterName = "$maxBoostLevel";
        powerCommand.Parameters.Add(maxBoostLevel);
        var boostBoostable = powerCommand.CreateParameter();
        boostBoostable.ParameterName = "$boostBoostable";
        powerCommand.Parameters.Add(boostBoostable);

        foreach (var set in sets)
        {
            for (var index = 0; index < set.Bonuses.Count; index++)
            {
                var bonus = set.Bonuses[index];
                setId.Value = set.CatalogItemId;
                bonusIndex.Value = index;
                minimumBoosts.Value = bonus.MinimumBoosts;
                maximumBoosts.Value = bonus.MaximumBoosts;
                requiresPattern.Value = bonus.RequiresPattern.ToString();
                bonusCommand.ExecuteNonQuery();

                for (var tokenPosition = 0; tokenPosition < bonus.RequiresTokens.Count; tokenPosition++)
                {
                    tokenSetId.Value = set.CatalogItemId;
                    tokenBonusIndex.Value = index;
                    tokenIndex.Value = tokenPosition;
                    token.Value = bonus.RequiresTokens[tokenPosition];
                    tokenCommand.ExecuteNonQuery();
                }

                foreach (var requiredEnhancementId in bonus.RequiredEnhancementIds)
                {
                    requiredSetId.Value = set.CatalogItemId;
                    requiredBonusIndex.Value = index;
                    enhancementId.Value = requiredEnhancementId;
                    requiredCommand.ExecuteNonQuery();
                }

                for (var autoPowerIndex = 0; autoPowerIndex < bonus.AutoPowers.Count; autoPowerIndex++)
                {
                    var autoPower = bonus.AutoPowers[autoPowerIndex];
                    powerSetId.Value = set.CatalogItemId;
                    powerBonusIndex.Value = index;
                    powerIndex.Value = autoPowerIndex;
                    homecomingSourceId.Value = autoPower.HomecomingSourceId;
                    displayHelp.Value = (object?)autoPower.DisplayHelp ?? DBNull.Value;
                    boostUsePlayerLevel.Value = autoPower.BoostUsePlayerLevel ? 1 : 0;
                    maxBoostLevel.Value = autoPower.MaxBoostLevel;
                    boostBoostable.Value = autoPower.BoostBoostable ? 1 : 0;
                    powerCommand.ExecuteNonQuery();
                }
            }
        }
    }

    private static void InsertItems(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IEnumerable<ItemReferenceRecord> items)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO Item (
                catalog_item_id,
                family,
                subtype,
                enhancement_family,
                current_display_name,
                active_status,
                verification_status,
                rarity,
                origin,
                tier,
                enhancement_set_id,
                produced_item_id,
                variant,
                common_io_boost_type,
                common_io_boost_type_display_text,
                icon,
                display_help,
                short_help,
                homecoming_source_id,
                homecoming_category,
                inspiration_standard_tier,
                inspiration_form,
                display_name_message_key,
                display_help_message_key,
                short_help_message_key)
            VALUES (
                $id,
                $family,
                $subtype,
                $enhancementFamily,
                $displayName,
                $activeStatus,
                $verificationStatus,
                $rarity,
                $origin,
                $tier,
                $enhancementSetId,
                $producedItemId,
                $variant,
                $commonIoBoostType,
                $commonIoBoostTypeDisplayText,
                $icon,
                $displayHelp,
                $shortHelp,
                $homecomingSourceId,
                $homecomingCategory,
                $inspirationStandardTier,
                $inspirationForm,
                $displayNameMessageKey,
                $displayHelpMessageKey,
                $shortHelpMessageKey);
            """;

        var id = command.CreateParameter();
        id.ParameterName = "$id";
        command.Parameters.Add(id);

        var family = command.CreateParameter();
        family.ParameterName = "$family";
        command.Parameters.Add(family);

        var subtype = command.CreateParameter();
        subtype.ParameterName = "$subtype";
        command.Parameters.Add(subtype);

        var enhancementFamily = command.CreateParameter();
        enhancementFamily.ParameterName = "$enhancementFamily";
        command.Parameters.Add(enhancementFamily);

        var displayName = command.CreateParameter();
        displayName.ParameterName = "$displayName";
        command.Parameters.Add(displayName);

        var activeStatus = command.CreateParameter();
        activeStatus.ParameterName = "$activeStatus";
        command.Parameters.Add(activeStatus);

        var verificationStatus = command.CreateParameter();
        verificationStatus.ParameterName = "$verificationStatus";
        command.Parameters.Add(verificationStatus);

        var rarity = command.CreateParameter();
        rarity.ParameterName = "$rarity";
        command.Parameters.Add(rarity);

        var origin = command.CreateParameter();
        origin.ParameterName = "$origin";
        command.Parameters.Add(origin);

        var tier = command.CreateParameter();
        tier.ParameterName = "$tier";
        command.Parameters.Add(tier);

        var enhancementSetId = command.CreateParameter();
        enhancementSetId.ParameterName = "$enhancementSetId";
        command.Parameters.Add(enhancementSetId);

        var producedItemId = command.CreateParameter();
        producedItemId.ParameterName = "$producedItemId";
        command.Parameters.Add(producedItemId);

        var variant = command.CreateParameter();
        variant.ParameterName = "$variant";
        command.Parameters.Add(variant);

        var commonIoBoostType = command.CreateParameter();
        commonIoBoostType.ParameterName = "$commonIoBoostType";
        command.Parameters.Add(commonIoBoostType);

        var commonIoBoostTypeDisplayText = command.CreateParameter();
        commonIoBoostTypeDisplayText.ParameterName = "$commonIoBoostTypeDisplayText";
        command.Parameters.Add(commonIoBoostTypeDisplayText);

        var icon = command.CreateParameter();
        icon.ParameterName = "$icon";
        command.Parameters.Add(icon);

        var displayHelp = command.CreateParameter();
        displayHelp.ParameterName = "$displayHelp";
        command.Parameters.Add(displayHelp);

        var shortHelp = command.CreateParameter();
        shortHelp.ParameterName = "$shortHelp";
        command.Parameters.Add(shortHelp);

        var homecomingSourceId = command.CreateParameter();
        homecomingSourceId.ParameterName = "$homecomingSourceId";
        command.Parameters.Add(homecomingSourceId);

        var homecomingCategory = command.CreateParameter();
        homecomingCategory.ParameterName = "$homecomingCategory";
        command.Parameters.Add(homecomingCategory);

        var inspirationStandardTier = command.CreateParameter();
        inspirationStandardTier.ParameterName = "$inspirationStandardTier";
        command.Parameters.Add(inspirationStandardTier);

        var inspirationForm = command.CreateParameter();
        inspirationForm.ParameterName = "$inspirationForm";
        command.Parameters.Add(inspirationForm);

        var displayNameMessageKey = command.CreateParameter();
        displayNameMessageKey.ParameterName = "$displayNameMessageKey";
        command.Parameters.Add(displayNameMessageKey);

        var displayHelpMessageKey = command.CreateParameter();
        displayHelpMessageKey.ParameterName = "$displayHelpMessageKey";
        command.Parameters.Add(displayHelpMessageKey);

        var shortHelpMessageKey = command.CreateParameter();
        shortHelpMessageKey.ParameterName = "$shortHelpMessageKey";
        command.Parameters.Add(shortHelpMessageKey);

        foreach (var item in items)
        {
            id.Value = item.CatalogItemId;
            family.Value = item.Family.ToString();
            subtype.Value = item.Subtype;
            enhancementFamily.Value = item.EnhancementFamily?.ToString() ?? (object)DBNull.Value;
            displayName.Value = item.CurrentDisplayName;
            activeStatus.Value = item.ActiveStatus.ToString();
            verificationStatus.Value = item.VerificationStatus.ToString();
            rarity.Value = (object?)item.Rarity ?? DBNull.Value;
            origin.Value = (object?)item.Origin ?? DBNull.Value;
            tier.Value = (object?)item.Tier ?? DBNull.Value;
            enhancementSetId.Value = (object?)item.EnhancementSetId ?? DBNull.Value;
            producedItemId.Value = (object?)item.ProducedItemId ?? DBNull.Value;
            variant.Value = (object?)item.Variant ?? DBNull.Value;
            commonIoBoostType.Value = (object?)item.CommonIoBoostType ?? DBNull.Value;
            commonIoBoostTypeDisplayText.Value = (object?)item.CommonIoBoostTypeDisplayText ?? DBNull.Value;
            icon.Value = (object?)item.Icon ?? DBNull.Value;
            displayHelp.Value = (object?)item.DisplayHelp ?? DBNull.Value;
            shortHelp.Value = (object?)item.ShortHelp ?? DBNull.Value;
            homecomingSourceId.Value = (object?)item.HomecomingSourceId ?? DBNull.Value;
            homecomingCategory.Value = (object?)item.HomecomingCategory ?? DBNull.Value;
            inspirationStandardTier.Value = (object?)item.InspirationStandardTier ?? DBNull.Value;
            inspirationForm.Value = (object?)item.InspirationForm ?? DBNull.Value;
            displayNameMessageKey.Value = (object?)item.DisplayNameMessageKey ?? DBNull.Value;
            displayHelpMessageKey.Value = (object?)item.DisplayHelpMessageKey ?? DBNull.Value;
            shortHelpMessageKey.Value = (object?)item.ShortHelpMessageKey ?? DBNull.Value;
            command.ExecuteNonQuery();
        }
    }

    private static void InsertRecipeLevels(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IEnumerable<ItemReferenceRecord> items)
    {
        using var levelCommand = connection.CreateCommand();
        levelCommand.Transaction = transaction;
        levelCommand.CommandText =
            """
            INSERT INTO RecipeLevel (
                catalog_item_id,
                level,
                homecoming_source_id,
                crafting_cost)
            VALUES ($catalogItemId, $level, $homecomingSourceId, $craftingCost);
            """;
        var levelCatalogItemId = levelCommand.CreateParameter();
        levelCatalogItemId.ParameterName = "$catalogItemId";
        levelCommand.Parameters.Add(levelCatalogItemId);
        var levelValue = levelCommand.CreateParameter();
        levelValue.ParameterName = "$level";
        levelCommand.Parameters.Add(levelValue);
        var homecomingSourceId = levelCommand.CreateParameter();
        homecomingSourceId.ParameterName = "$homecomingSourceId";
        levelCommand.Parameters.Add(homecomingSourceId);
        var craftingCost = levelCommand.CreateParameter();
        craftingCost.ParameterName = "$craftingCost";
        levelCommand.Parameters.Add(craftingCost);

        using var requirementCommand = connection.CreateCommand();
        requirementCommand.Transaction = transaction;
        requirementCommand.CommandText =
            """
            INSERT INTO RecipeLevelRequirement (
                catalog_item_id,
                level,
                salvage_catalog_item_id,
                quantity)
            VALUES ($catalogItemId, $level, $salvageItemId, $quantity);
            """;
        var requirementCatalogItemId = requirementCommand.CreateParameter();
        requirementCatalogItemId.ParameterName = "$catalogItemId";
        requirementCommand.Parameters.Add(requirementCatalogItemId);
        var requirementLevel = requirementCommand.CreateParameter();
        requirementLevel.ParameterName = "$level";
        requirementCommand.Parameters.Add(requirementLevel);
        var salvageItemId = requirementCommand.CreateParameter();
        salvageItemId.ParameterName = "$salvageItemId";
        requirementCommand.Parameters.Add(salvageItemId);
        var quantity = requirementCommand.CreateParameter();
        quantity.ParameterName = "$quantity";
        requirementCommand.Parameters.Add(quantity);

        using var excludedCommand = connection.CreateCommand();
        excludedCommand.Transaction = transaction;
        excludedCommand.CommandText =
            """
            INSERT INTO RecipeExcludedSourceLevel (
                catalog_item_id,
                level,
                homecoming_source_id,
                crafting_cost)
            VALUES ($catalogItemId, $level, $homecomingSourceId, $craftingCost);
            """;
        var excludedCatalogItemId = excludedCommand.CreateParameter();
        excludedCatalogItemId.ParameterName = "$catalogItemId";
        excludedCommand.Parameters.Add(excludedCatalogItemId);
        var excludedLevel = excludedCommand.CreateParameter();
        excludedLevel.ParameterName = "$level";
        excludedCommand.Parameters.Add(excludedLevel);
        var excludedSourceId = excludedCommand.CreateParameter();
        excludedSourceId.ParameterName = "$homecomingSourceId";
        excludedCommand.Parameters.Add(excludedSourceId);
        var excludedCost = excludedCommand.CreateParameter();
        excludedCost.ParameterName = "$craftingCost";
        excludedCommand.Parameters.Add(excludedCost);

        foreach (var item in items)
        {
            foreach (var level in item.RecipeLevels)
            {
                levelCatalogItemId.Value = item.CatalogItemId;
                levelValue.Value = level.Level;
                homecomingSourceId.Value = level.HomecomingSourceId;
                craftingCost.Value = checked((long)level.CraftingCost);
                levelCommand.ExecuteNonQuery();

                foreach (var requirement in level.Requirements)
                {
                    requirementCatalogItemId.Value = item.CatalogItemId;
                    requirementLevel.Value = level.Level;
                    salvageItemId.Value = requirement.SalvageItemId;
                    quantity.Value = checked((long)requirement.Quantity);
                    requirementCommand.ExecuteNonQuery();
                }
            }

            foreach (var excluded in item.ExcludedHomecomingSourceLevels)
            {
                excludedCatalogItemId.Value = item.CatalogItemId;
                excludedLevel.Value = excluded.Level;
                excludedSourceId.Value = excluded.HomecomingSourceId;
                excludedCost.Value = checked((long)excluded.CraftingCost);
                excludedCommand.ExecuteNonQuery();
            }
        }
    }

    private static void InsertEnhancementSourceVariants(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IEnumerable<ItemReferenceRecord> items)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO EnhancementSourceVariant (
                catalog_item_id,
                variant_index,
                homecoming_source_id,
                source_form,
                icon,
                display_help,
                short_help,
                boost_use_player_level,
                max_boost_level,
                boost_boostable)
            VALUES ($catalogItemId, $variantIndex, $homecomingSourceId, $sourceForm, $icon, $displayHelp, $shortHelp, $boostUsePlayerLevel, $maxBoostLevel, $boostBoostable);
            """;

        var catalogItemId = command.CreateParameter();
        catalogItemId.ParameterName = "$catalogItemId";
        command.Parameters.Add(catalogItemId);
        var variantIndex = command.CreateParameter();
        variantIndex.ParameterName = "$variantIndex";
        command.Parameters.Add(variantIndex);
        var homecomingSourceId = command.CreateParameter();
        homecomingSourceId.ParameterName = "$homecomingSourceId";
        command.Parameters.Add(homecomingSourceId);
        var sourceForm = command.CreateParameter();
        sourceForm.ParameterName = "$sourceForm";
        command.Parameters.Add(sourceForm);
        var icon = command.CreateParameter();
        icon.ParameterName = "$icon";
        command.Parameters.Add(icon);
        var displayHelp = command.CreateParameter();
        displayHelp.ParameterName = "$displayHelp";
        command.Parameters.Add(displayHelp);
        var shortHelp = command.CreateParameter();
        shortHelp.ParameterName = "$shortHelp";
        command.Parameters.Add(shortHelp);
        var boostUsePlayerLevel = command.CreateParameter();
        boostUsePlayerLevel.ParameterName = "$boostUsePlayerLevel";
        command.Parameters.Add(boostUsePlayerLevel);
        var maxBoostLevel = command.CreateParameter();
        maxBoostLevel.ParameterName = "$maxBoostLevel";
        command.Parameters.Add(maxBoostLevel);
        var boostBoostable = command.CreateParameter();
        boostBoostable.ParameterName = "$boostBoostable";
        command.Parameters.Add(boostBoostable);

        foreach (var item in items)
        {
            for (var index = 0; index < item.SourceVariants.Count; index++)
            {
                var variant = item.SourceVariants[index];
                catalogItemId.Value = item.CatalogItemId;
                variantIndex.Value = index;
                homecomingSourceId.Value = variant.HomecomingSourceId;
                sourceForm.Value = variant.SourceForm;
                icon.Value = (object?)variant.Icon ?? DBNull.Value;
                displayHelp.Value = (object?)variant.DisplayHelp ?? DBNull.Value;
                shortHelp.Value = (object?)variant.ShortHelp ?? DBNull.Value;
                boostUsePlayerLevel.Value = variant.BoostUsePlayerLevel ? 1 : 0;
                maxBoostLevel.Value = variant.MaxBoostLevel;
                boostBoostable.Value = variant.BoostBoostable ? 1 : 0;
                command.ExecuteNonQuery();
            }
        }
    }

    private static void InsertEnhancementSourceVariantEffects(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IEnumerable<ItemReferenceRecord> items)
    {
        using var effectCommand = connection.CreateCommand();
        effectCommand.Transaction = transaction;
        effectCommand.CommandText =
            """
            INSERT INTO EnhancementSourceVariantEffect (
                catalog_item_id,
                variant_index,
                effect_index,
                tag,
                table_name,
                scale)
            VALUES ($catalogItemId, $variantIndex, $effectIndex, $tag, $tableName, $scale);
            """;
        using var attribCommand = connection.CreateCommand();
        attribCommand.Transaction = transaction;
        attribCommand.CommandText =
            """
            INSERT INTO EnhancementSourceVariantEffectAttrib (
                catalog_item_id,
                variant_index,
                effect_index,
                attrib_index,
                attrib_id)
            VALUES ($catalogItemId, $variantIndex, $effectIndex, $attribIndex, $attribId);
            """;

        var catalogItemId = CreateParameter(effectCommand, "$catalogItemId");
        var variantIndex = CreateParameter(effectCommand, "$variantIndex");
        var effectIndex = CreateParameter(effectCommand, "$effectIndex");
        var tag = CreateParameter(effectCommand, "$tag");
        var tableName = CreateParameter(effectCommand, "$tableName");
        var scale = CreateParameter(effectCommand, "$scale");
        var attribCatalogItemId = CreateParameter(attribCommand, "$catalogItemId");
        var attribVariantIndex = CreateParameter(attribCommand, "$variantIndex");
        var attribEffectIndex = CreateParameter(attribCommand, "$effectIndex");
        var attribIndex = CreateParameter(attribCommand, "$attribIndex");
        var attribId = CreateParameter(attribCommand, "$attribId");

        foreach (var item in items)
        {
            for (var variantPosition = 0; variantPosition < item.SourceVariants.Count; variantPosition++)
            {
                var variant = item.SourceVariants[variantPosition];
                for (var effectPosition = 0; effectPosition < variant.Effects.Count; effectPosition++)
                {
                    var effect = variant.Effects[effectPosition];
                    catalogItemId.Value = item.CatalogItemId;
                    variantIndex.Value = variantPosition;
                    effectIndex.Value = effectPosition;
                    tag.Value = effect.Tag;
                    tableName.Value = effect.Table;
                    scale.Value = effect.Scale;
                    effectCommand.ExecuteNonQuery();

                    for (var attribPosition = 0; attribPosition < effect.AttribIds.Count; attribPosition++)
                    {
                        attribCatalogItemId.Value = item.CatalogItemId;
                        attribVariantIndex.Value = variantPosition;
                        attribEffectIndex.Value = effectPosition;
                        attribIndex.Value = attribPosition;
                        attribId.Value = effect.AttribIds[attribPosition];
                        attribCommand.ExecuteNonQuery();
                    }
                }
            }
        }
    }

    private static void InsertEnhancementSetBonusPowerEffects(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IEnumerable<EnhancementSetReferenceRecord> sets)
    {
        using var effectCommand = connection.CreateCommand();
        effectCommand.Transaction = transaction;
        effectCommand.CommandText =
            """
            INSERT INTO EnhancementSetBonusPowerEffect (
                set_catalog_item_id,
                bonus_index,
                power_index,
                effect_index,
                tag,
                table_name,
                scale)
            VALUES ($setId, $bonusIndex, $powerIndex, $effectIndex, $tag, $tableName, $scale);
            """;
        using var attribCommand = connection.CreateCommand();
        attribCommand.Transaction = transaction;
        attribCommand.CommandText =
            """
            INSERT INTO EnhancementSetBonusPowerEffectAttrib (
                set_catalog_item_id,
                bonus_index,
                power_index,
                effect_index,
                attrib_index,
                attrib_id)
            VALUES ($setId, $bonusIndex, $powerIndex, $effectIndex, $attribIndex, $attribId);
            """;

        var setId = CreateParameter(effectCommand, "$setId");
        var bonusIndex = CreateParameter(effectCommand, "$bonusIndex");
        var powerIndex = CreateParameter(effectCommand, "$powerIndex");
        var effectIndex = CreateParameter(effectCommand, "$effectIndex");
        var tag = CreateParameter(effectCommand, "$tag");
        var tableName = CreateParameter(effectCommand, "$tableName");
        var scale = CreateParameter(effectCommand, "$scale");
        var attribSetId = CreateParameter(attribCommand, "$setId");
        var attribBonusIndex = CreateParameter(attribCommand, "$bonusIndex");
        var attribPowerIndex = CreateParameter(attribCommand, "$powerIndex");
        var attribEffectIndex = CreateParameter(attribCommand, "$effectIndex");
        var attribIndex = CreateParameter(attribCommand, "$attribIndex");
        var attribId = CreateParameter(attribCommand, "$attribId");

        foreach (var set in sets)
        {
            for (var bonusPosition = 0; bonusPosition < set.Bonuses.Count; bonusPosition++)
            {
                var bonus = set.Bonuses[bonusPosition];
                for (var powerPosition = 0; powerPosition < bonus.AutoPowers.Count; powerPosition++)
                {
                    var power = bonus.AutoPowers[powerPosition];
                    for (var effectPosition = 0; effectPosition < power.Effects.Count; effectPosition++)
                    {
                        var effect = power.Effects[effectPosition];
                        setId.Value = set.CatalogItemId;
                        bonusIndex.Value = bonusPosition;
                        powerIndex.Value = powerPosition;
                        effectIndex.Value = effectPosition;
                        tag.Value = effect.Tag;
                        tableName.Value = effect.Table;
                        scale.Value = effect.Scale;
                        effectCommand.ExecuteNonQuery();

                        for (var attribPosition = 0; attribPosition < effect.AttribIds.Count; attribPosition++)
                        {
                            attribSetId.Value = set.CatalogItemId;
                            attribBonusIndex.Value = bonusPosition;
                            attribPowerIndex.Value = powerPosition;
                            attribEffectIndex.Value = effectPosition;
                            attribIndex.Value = attribPosition;
                            attribId.Value = effect.AttribIds[attribPosition];
                            attribCommand.ExecuteNonQuery();
                        }
                    }
                }
            }
        }
    }

    private static void InsertEnhancementResolverNamedTables(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyDictionary<string, IReadOnlyList<float>>? namedTables)
    {
        if (namedTables is null || namedTables.Count == 0)
        {
            return;
        }

        using var tableCommand = connection.CreateCommand();
        tableCommand.Transaction = transaction;
        tableCommand.CommandText =
            """
            INSERT INTO EnhancementResolverNamedTable (table_name, value_count)
            VALUES ($tableName, $valueCount);
            """;
        using var valueCommand = connection.CreateCommand();
        valueCommand.Transaction = transaction;
        valueCommand.CommandText =
            """
            INSERT INTO EnhancementResolverNamedTableValue (table_name, value_index, value)
            VALUES ($tableName, $valueIndex, $value);
            """;

        var tableName = CreateParameter(tableCommand, "$tableName");
        var valueCount = CreateParameter(tableCommand, "$valueCount");
        var valueTableName = CreateParameter(valueCommand, "$tableName");
        var valueIndex = CreateParameter(valueCommand, "$valueIndex");
        var value = CreateParameter(valueCommand, "$value");

        foreach (var pair in namedTables.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            tableName.Value = pair.Key;
            valueCount.Value = pair.Value.Count;
            tableCommand.ExecuteNonQuery();

            for (var index = 0; index < pair.Value.Count; index++)
            {
                valueTableName.Value = pair.Key;
                valueIndex.Value = index;
                value.Value = pair.Value[index];
                valueCommand.ExecuteNonQuery();
            }
        }
    }

    private static SqliteParameter CreateParameter(SqliteCommand command, string name)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        command.Parameters.Add(parameter);
        return parameter;
    }

    private static void InsertAliases(
        SqliteConnection connection,
        SqliteTransaction transaction,
        List<ItemReferenceAliasRecordDocument> aliases)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO ItemAlias (
                lookup_key,
                catalog_item_id,
                text,
                locale,
                name_kind,
                is_preferred)
            VALUES ($lookupKey, $catalogItemId, $text, $locale, $nameKind, $isPreferred);
            """;

        var lookupKey = command.CreateParameter();
        lookupKey.ParameterName = "$lookupKey";
        command.Parameters.Add(lookupKey);

        var catalogItemId = command.CreateParameter();
        catalogItemId.ParameterName = "$catalogItemId";
        command.Parameters.Add(catalogItemId);

        var text = command.CreateParameter();
        text.ParameterName = "$text";
        command.Parameters.Add(text);

        var locale = command.CreateParameter();
        locale.ParameterName = "$locale";
        command.Parameters.Add(locale);

        var nameKind = command.CreateParameter();
        nameKind.ParameterName = "$nameKind";
        command.Parameters.Add(nameKind);

        var isPreferred = command.CreateParameter();
        isPreferred.ParameterName = "$isPreferred";
        command.Parameters.Add(isPreferred);

        foreach (var aliasDocument in aliases)
        {
            if (string.IsNullOrWhiteSpace(aliasDocument.CatalogItemId)
                || string.IsNullOrWhiteSpace(aliasDocument.Text)
                || string.IsNullOrWhiteSpace(aliasDocument.Locale)
                || string.IsNullOrWhiteSpace(aliasDocument.NameKind))
            {
                continue;
            }

            var normalizedLookupKey = ItemReferenceLookup.NormalizeLookupKey(aliasDocument.Text);
            if (string.IsNullOrEmpty(normalizedLookupKey))
            {
                continue;
            }

            lookupKey.Value = normalizedLookupKey;
            catalogItemId.Value = aliasDocument.CatalogItemId.Trim();
            text.Value = aliasDocument.Text;
            locale.Value = aliasDocument.Locale.Trim();
            nameKind.Value = aliasDocument.NameKind.Trim();
            isPreferred.Value = aliasDocument.IsPreferred ? 1 : 0;
            command.ExecuteNonQuery();
        }
    }

    private static void InsertAliasesFromIndex(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Dictionary<string, (string CatalogItemId, string MatchedAliasText)> aliasIndex)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO ItemAlias (
                lookup_key,
                catalog_item_id,
                text,
                locale,
                name_kind,
                is_preferred)
            VALUES ($lookupKey, $catalogItemId, $text, $locale, $nameKind, $isPreferred);
            """;

        var lookupKey = command.CreateParameter();
        lookupKey.ParameterName = "$lookupKey";
        command.Parameters.Add(lookupKey);

        var catalogItemId = command.CreateParameter();
        catalogItemId.ParameterName = "$catalogItemId";
        command.Parameters.Add(catalogItemId);

        var text = command.CreateParameter();
        text.ParameterName = "$text";
        command.Parameters.Add(text);

        var locale = command.CreateParameter();
        locale.ParameterName = "$locale";
        command.Parameters.Add(locale);

        var nameKind = command.CreateParameter();
        nameKind.ParameterName = "$nameKind";
        command.Parameters.Add(nameKind);

        var isPreferred = command.CreateParameter();
        isPreferred.ParameterName = "$isPreferred";
        command.Parameters.Add(isPreferred);

        foreach (var (normalizedLookupKey, aliasMatch) in aliasIndex)
        {
            lookupKey.Value = normalizedLookupKey;
            catalogItemId.Value = aliasMatch.CatalogItemId;
            text.Value = aliasMatch.MatchedAliasText;
            locale.Value = "en";
            nameKind.Value = ReferenceAliasNameKind.Display.ToString();
            isPreferred.Value = 0;
            command.ExecuteNonQuery();
        }
    }

    private static void InsertServerAvailability(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IEnumerable<ItemReferenceRecord> items,
        IEnumerable<EnhancementSetReferenceRecord> sets)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO ReferenceServerAvailability (
                catalog_item_id,
                entity_kind,
                server_key,
                availability_status)
            VALUES ($catalogItemId, $entityKind, $serverKey, $availabilityStatus);
            """;

        var catalogItemId = command.CreateParameter();
        catalogItemId.ParameterName = "$catalogItemId";
        command.Parameters.Add(catalogItemId);

        var entityKind = command.CreateParameter();
        entityKind.ParameterName = "$entityKind";
        command.Parameters.Add(entityKind);

        var serverKey = command.CreateParameter();
        serverKey.ParameterName = "$serverKey";
        command.Parameters.Add(serverKey);

        var availabilityStatus = command.CreateParameter();
        availabilityStatus.ParameterName = "$availabilityStatus";
        command.Parameters.Add(availabilityStatus);

        foreach (var item in items)
        {
            foreach (var availability in item.ServerAvailability)
            {
                catalogItemId.Value = item.CatalogItemId;
                entityKind.Value = "Item";
                serverKey.Value = availability.ServerKey;
                availabilityStatus.Value = availability.Status.ToString();
                command.ExecuteNonQuery();
            }
        }

        foreach (var set in sets)
        {
            foreach (var availability in set.ServerAvailability)
            {
                catalogItemId.Value = set.CatalogItemId;
                entityKind.Value = "EnhancementSet";
                serverKey.Value = availability.ServerKey;
                availabilityStatus.Value = availability.Status.ToString();
                command.ExecuteNonQuery();
            }
        }
    }

    private static void InsertManifest(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ItemReferenceManifest manifest)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO CatalogManifest (
                schema_version,
                catalog_version,
                homecoming_build_min,
                homecoming_build_max,
                source_revision,
                source_notes,
                generated_at_utc)
            VALUES ($schemaVersion, $catalogVersion, $buildMin, $buildMax, $sourceRevision, $sourceNotes, $generatedAtUtc);
            """;

        command.Parameters.AddWithValue("$schemaVersion", ReferenceDatabaseSchema.SchemaVersion);
        command.Parameters.AddWithValue("$catalogVersion", manifest.CatalogVersion);
        command.Parameters.AddWithValue("$buildMin", (object?)manifest.HomecomingCompatibility.BuildMin ?? DBNull.Value);
        command.Parameters.AddWithValue("$buildMax", (object?)manifest.HomecomingCompatibility.BuildMax ?? DBNull.Value);
        command.Parameters.AddWithValue("$sourceRevision", (object?)manifest.SourceRevision ?? DBNull.Value);
        command.Parameters.AddWithValue("$sourceNotes", (object?)manifest.SourceNotes ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$generatedAtUtc",
            DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
    }

    private static void InsertZones(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IEnumerable<ZoneReferenceRecord> zones)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO Zone (
                zone_id,
                display_name,
                alignment_notes,
                level_range,
                zone_type)
            VALUES ($zoneId, $displayName, $alignmentNotes, $levelRange, $zoneType);
            """;

        var zoneId = command.CreateParameter();
        zoneId.ParameterName = "$zoneId";
        command.Parameters.Add(zoneId);
        var displayName = command.CreateParameter();
        displayName.ParameterName = "$displayName";
        command.Parameters.Add(displayName);
        var alignmentNotes = command.CreateParameter();
        alignmentNotes.ParameterName = "$alignmentNotes";
        command.Parameters.Add(alignmentNotes);
        var levelRange = command.CreateParameter();
        levelRange.ParameterName = "$levelRange";
        command.Parameters.Add(levelRange);
        var zoneType = command.CreateParameter();
        zoneType.ParameterName = "$zoneType";
        command.Parameters.Add(zoneType);

        foreach (var zone in zones)
        {
            zoneId.Value = zone.ZoneId;
            displayName.Value = zone.DisplayName;
            alignmentNotes.Value = (object?)zone.AlignmentNotes ?? DBNull.Value;
            levelRange.Value = (object?)zone.LevelRange ?? DBNull.Value;
            zoneType.Value = (object?)zone.ZoneType ?? DBNull.Value;
            command.ExecuteNonQuery();
        }
    }

    private static void InsertBadges(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IEnumerable<BadgeReferenceRecord> badges)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO Badge (
                catalog_item_id,
                homecoming_source_id,
                set_title_id,
                canonical_category,
                badge_type,
                reference_kind,
                hero_name,
                villain_name,
                hero_description,
                villain_description,
                hero_icon,
                villain_icon,
                zone_id,
                completion_badge_id,
                is_zone_completion_badge,
                verification_status,
                requirement_text,
                reward_text,
                requirement_logic_status,
                requirement_logic_pattern)
            VALUES (
                $catalogItemId,
                $homecomingSourceId,
                $setTitleId,
                $canonicalCategory,
                $badgeType,
                $referenceKind,
                $heroName,
                $villainName,
                $heroDescription,
                $villainDescription,
                $heroIcon,
                $villainIcon,
                $zoneId,
                $completionBadgeId,
                $isZoneCompletionBadge,
                $verificationStatus,
                $requirementText,
                $rewardText,
                $requirementLogicStatus,
                $requirementLogicPattern);
            """;

        void AddParam(string name)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            command.Parameters.Add(parameter);
        }

        AddParam("$catalogItemId");
        AddParam("$homecomingSourceId");
        AddParam("$setTitleId");
        AddParam("$canonicalCategory");
        AddParam("$badgeType");
        AddParam("$referenceKind");
        AddParam("$heroName");
        AddParam("$villainName");
        AddParam("$heroDescription");
        AddParam("$villainDescription");
        AddParam("$heroIcon");
        AddParam("$villainIcon");
        AddParam("$zoneId");
        AddParam("$completionBadgeId");
        AddParam("$isZoneCompletionBadge");
        AddParam("$verificationStatus");
        AddParam("$requirementText");
        AddParam("$rewardText");
        AddParam("$requirementLogicStatus");
        AddParam("$requirementLogicPattern");

        foreach (var badge in badges)
        {
            command.Parameters["$catalogItemId"].Value = badge.CatalogItemId;
            command.Parameters["$homecomingSourceId"].Value = badge.HomecomingSourceId;
            command.Parameters["$setTitleId"].Value = badge.SetTitleId is null ? DBNull.Value : badge.SetTitleId.Value;
            command.Parameters["$canonicalCategory"].Value = badge.CanonicalCategory;
            command.Parameters["$badgeType"].Value = badge.BadgeType;
            command.Parameters["$referenceKind"].Value = badge.ReferenceKind.ToString();
            command.Parameters["$heroName"].Value = badge.HeroName;
            command.Parameters["$villainName"].Value = badge.VillainName;
            command.Parameters["$heroDescription"].Value = (object?)badge.HeroDescription ?? DBNull.Value;
            command.Parameters["$villainDescription"].Value = (object?)badge.VillainDescription ?? DBNull.Value;
            command.Parameters["$heroIcon"].Value = (object?)badge.HeroIcon ?? DBNull.Value;
            command.Parameters["$villainIcon"].Value = (object?)badge.VillainIcon ?? DBNull.Value;
            command.Parameters["$zoneId"].Value = (object?)badge.ZoneId ?? DBNull.Value;
            command.Parameters["$completionBadgeId"].Value = DBNull.Value;
            command.Parameters["$isZoneCompletionBadge"].Value = badge.IsZoneCompletionBadge ? 1 : 0;
            command.Parameters["$verificationStatus"].Value = badge.VerificationStatus.ToString();
            command.Parameters["$requirementText"].Value = (object?)badge.RequirementText ?? DBNull.Value;
            command.Parameters["$rewardText"].Value = (object?)badge.RewardText ?? DBNull.Value;
            command.Parameters["$requirementLogicStatus"].Value =
                badge.RequirementLogicStatus is null ? DBNull.Value : badge.RequirementLogicStatus.Value.ToString();
            command.Parameters["$requirementLogicPattern"].Value =
                badge.RequirementLogicPattern is null ? DBNull.Value : badge.RequirementLogicPattern.Value.ToString();
            command.ExecuteNonQuery();
        }

        using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText =
            """
            UPDATE Badge
            SET completion_badge_id = $completionBadgeId
            WHERE catalog_item_id = $catalogItemId;
            """;
        update.Parameters.Add(new SqliteParameter("$catalogItemId", null));
        update.Parameters.Add(new SqliteParameter("$completionBadgeId", null));

        foreach (var badge in badges.Where(badge => !string.IsNullOrWhiteSpace(badge.CompletionBadgeId)))
        {
            update.Parameters["$catalogItemId"].Value = badge.CatalogItemId;
            update.Parameters["$completionBadgeId"].Value = badge.CompletionBadgeId;
            update.ExecuteNonQuery();
        }
    }

    private static void InsertZoneCompletionBadges(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IEnumerable<ZoneReferenceRecord> zones)
    {
        using var exploration = connection.CreateCommand();
        exploration.Transaction = transaction;
        exploration.CommandText =
            """
            INSERT INTO ZoneExplorationCompletionBadge (zone_id, badge_catalog_item_id, badge_index)
            VALUES ($zoneId, $badgeId, $badgeIndex);
            """;
        exploration.Parameters.Add(new SqliteParameter("$zoneId", null));
        exploration.Parameters.Add(new SqliteParameter("$badgeId", null));
        exploration.Parameters.Add(new SqliteParameter("$badgeIndex", null));

        using var history = connection.CreateCommand();
        history.Transaction = transaction;
        history.CommandText =
            """
            INSERT INTO ZoneHistoryCompletionBadge (zone_id, badge_catalog_item_id, badge_index)
            VALUES ($zoneId, $badgeId, $badgeIndex);
            """;
        history.Parameters.Add(new SqliteParameter("$zoneId", null));
        history.Parameters.Add(new SqliteParameter("$badgeId", null));
        history.Parameters.Add(new SqliteParameter("$badgeIndex", null));

        foreach (var zone in zones)
        {
            for (var index = 0; index < zone.ExplorationCompletionBadgeIds.Count; index++)
            {
                exploration.Parameters["$zoneId"].Value = zone.ZoneId;
                exploration.Parameters["$badgeId"].Value = zone.ExplorationCompletionBadgeIds[index];
                exploration.Parameters["$badgeIndex"].Value = index;
                exploration.ExecuteNonQuery();
            }

            for (var index = 0; index < zone.HistoryCompletionBadgeIds.Count; index++)
            {
                history.Parameters["$zoneId"].Value = zone.ZoneId;
                history.Parameters["$badgeId"].Value = zone.HistoryCompletionBadgeIds[index];
                history.Parameters["$badgeIndex"].Value = index;
                history.ExecuteNonQuery();
            }
        }
    }

    private static void InsertBadgeLocations(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IEnumerable<BadgeLocationReferenceRecord> locations)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO BadgeLocation (
                badge_catalog_item_id,
                location_index,
                zone_id,
                coordinate_x,
                coordinate_y,
                coordinate_z,
                thumbtack_command,
                marker_type,
                location_role,
                coordinate_semantics,
                verification_status,
                trigger_description,
                exploration_route_order,
                route_source_project,
                route_source_version,
                route_source_url,
                route_mapping_confidence)
            VALUES (
                $badgeId,
                $locationIndex,
                $zoneId,
                $coordinateX,
                $coordinateY,
                $coordinateZ,
                $thumbtackCommand,
                $markerType,
                $locationRole,
                $coordinateSemantics,
                $verificationStatus,
                $triggerDescription,
                $explorationRouteOrder,
                $routeSourceProject,
                $routeSourceVersion,
                $routeSourceUrl,
                $routeMappingConfidence);
            """;

        void AddParam(string name)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            command.Parameters.Add(parameter);
        }

        AddParam("$badgeId");
        AddParam("$locationIndex");
        AddParam("$zoneId");
        AddParam("$coordinateX");
        AddParam("$coordinateY");
        AddParam("$coordinateZ");
        AddParam("$thumbtackCommand");
        AddParam("$markerType");
        AddParam("$locationRole");
        AddParam("$coordinateSemantics");
        AddParam("$verificationStatus");
        AddParam("$triggerDescription");
        AddParam("$explorationRouteOrder");
        AddParam("$routeSourceProject");
        AddParam("$routeSourceVersion");
        AddParam("$routeSourceUrl");
        AddParam("$routeMappingConfidence");

        foreach (var location in locations)
        {
            command.Parameters["$badgeId"].Value = location.BadgeCatalogItemId;
            command.Parameters["$locationIndex"].Value = location.LocationIndex;
            command.Parameters["$zoneId"].Value = location.ZoneId;
            command.Parameters["$coordinateX"].Value = location.CoordinateX is null ? DBNull.Value : location.CoordinateX.Value;
            command.Parameters["$coordinateY"].Value = location.CoordinateY is null ? DBNull.Value : location.CoordinateY.Value;
            command.Parameters["$coordinateZ"].Value = location.CoordinateZ is null ? DBNull.Value : location.CoordinateZ.Value;
            command.Parameters["$thumbtackCommand"].Value = (object?)location.ThumbtackCommand ?? DBNull.Value;
            command.Parameters["$markerType"].Value = (object?)location.MarkerType ?? DBNull.Value;
            command.Parameters["$locationRole"].Value = (object?)location.LocationRole ?? DBNull.Value;
            command.Parameters["$coordinateSemantics"].Value = (object?)location.CoordinateSemantics ?? DBNull.Value;
            command.Parameters["$verificationStatus"].Value = location.VerificationStatus.ToString();
            command.Parameters["$triggerDescription"].Value = (object?)location.TriggerDescription ?? DBNull.Value;
            command.Parameters["$explorationRouteOrder"].Value = location.ExplorationRouteOrder is null ? DBNull.Value : location.ExplorationRouteOrder.Value;
            command.Parameters["$routeSourceProject"].Value = (object?)location.RouteSourceProject ?? DBNull.Value;
            command.Parameters["$routeSourceVersion"].Value = (object?)location.RouteSourceVersion ?? DBNull.Value;
            command.Parameters["$routeSourceUrl"].Value = (object?)location.RouteSourceUrl ?? DBNull.Value;
            command.Parameters["$routeMappingConfidence"].Value = (object?)location.RouteMappingConfidence ?? DBNull.Value;
            command.ExecuteNonQuery();
        }
    }

    private static void InsertRouteOrderingProvenance(
        SqliteConnection connection,
        SqliteTransaction transaction,
        RouteOrderingProvenanceRecord? provenance)
    {
        if (provenance is null)
        {
            return;
        }

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO RouteOrderingProvenance (
                source_name,
                author,
                version,
                maps_thread_url,
                popmenu_thread_url,
                ordering_semantics,
                retrieved)
            VALUES ($sourceName, $author, $version, $mapsUrl, $popmenuUrl, $semantics, $retrieved);
            """;
        command.Parameters.AddWithValue("$sourceName", provenance.SourceName);
        command.Parameters.AddWithValue("$author", provenance.Author);
        command.Parameters.AddWithValue("$version", provenance.Version);
        command.Parameters.AddWithValue("$mapsUrl", (object?)provenance.MapsThreadUrl ?? DBNull.Value);
        command.Parameters.AddWithValue("$popmenuUrl", (object?)provenance.PopmenuThreadUrl ?? DBNull.Value);
        command.Parameters.AddWithValue("$semantics", (object?)provenance.OrderingSemantics ?? DBNull.Value);
        command.Parameters.AddWithValue("$retrieved", (object?)provenance.Retrieved ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    private static void InsertHistoryPlaqueRouteCollections(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IEnumerable<HistoryPlaqueRouteCollectionRecord> collections)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO HistoryPlaqueRouteCollection (
                collection_name,
                completion_badge_id,
                published_collection_order_available,
                ordering_status,
                ordering_notes)
            VALUES ($collectionName, $completionBadgeId, $publishedOrder, $orderingStatus, $orderingNotes);
            """;

        command.Parameters.Add(new SqliteParameter("$collectionName", null));
        command.Parameters.Add(new SqliteParameter("$completionBadgeId", null));
        command.Parameters.Add(new SqliteParameter("$publishedOrder", null));
        command.Parameters.Add(new SqliteParameter("$orderingStatus", null));
        command.Parameters.Add(new SqliteParameter("$orderingNotes", null));

        foreach (var collection in collections)
        {
            command.Parameters["$collectionName"].Value = collection.CollectionName;
            command.Parameters["$completionBadgeId"].Value = collection.CompletionBadgeId;
            command.Parameters["$publishedOrder"].Value = collection.PublishedCollectionOrderAvailable ? 1 : 0;
            command.Parameters["$orderingStatus"].Value = collection.OrderingStatus;
            command.Parameters["$orderingNotes"].Value = (object?)collection.OrderingNotes ?? DBNull.Value;
            command.ExecuteNonQuery();
        }
    }

    private static void InsertHistoryPlaqueRouteStops(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IEnumerable<HistoryPlaqueRouteStopRecord> stops)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO HistoryPlaqueRouteStop (
                collection_name,
                inventory_order,
                route_order,
                completion_badge_id,
                zone_id,
                location_index,
                plaque_name,
                source_zone_route_order,
                route_mapping_confidence,
                route_source_project,
                route_source_version,
                route_source_url)
            VALUES (
                $collectionName,
                $inventoryOrder,
                $routeOrder,
                $completionBadgeId,
                $zoneId,
                $locationIndex,
                $plaqueName,
                $sourceZoneRouteOrder,
                $mappingConfidence,
                $routeSourceProject,
                $routeSourceVersion,
                $routeSourceUrl);
            """;

        command.Parameters.Add(new SqliteParameter("$collectionName", null));
        command.Parameters.Add(new SqliteParameter("$inventoryOrder", null));
        command.Parameters.Add(new SqliteParameter("$routeOrder", null));
        command.Parameters.Add(new SqliteParameter("$completionBadgeId", null));
        command.Parameters.Add(new SqliteParameter("$zoneId", null));
        command.Parameters.Add(new SqliteParameter("$locationIndex", null));
        command.Parameters.Add(new SqliteParameter("$plaqueName", null));
        command.Parameters.Add(new SqliteParameter("$sourceZoneRouteOrder", null));
        command.Parameters.Add(new SqliteParameter("$mappingConfidence", null));
        command.Parameters.Add(new SqliteParameter("$routeSourceProject", null));
        command.Parameters.Add(new SqliteParameter("$routeSourceVersion", null));
        command.Parameters.Add(new SqliteParameter("$routeSourceUrl", null));

        foreach (var stop in stops)
        {
            command.Parameters["$collectionName"].Value = stop.CollectionName;
            command.Parameters["$inventoryOrder"].Value = stop.InventoryOrder;
            command.Parameters["$routeOrder"].Value = stop.RouteOrder is null ? DBNull.Value : stop.RouteOrder.Value;
            command.Parameters["$completionBadgeId"].Value = stop.CompletionBadgeId;
            command.Parameters["$zoneId"].Value = stop.ZoneId;
            command.Parameters["$locationIndex"].Value = stop.LocationIndex;
            command.Parameters["$plaqueName"].Value = (object?)stop.PlaqueName ?? DBNull.Value;
            command.Parameters["$sourceZoneRouteOrder"].Value = stop.SourceZoneRouteOrder is null ? DBNull.Value : stop.SourceZoneRouteOrder.Value;
            command.Parameters["$mappingConfidence"].Value = (object?)stop.RouteMappingConfidence ?? DBNull.Value;
            command.Parameters["$routeSourceProject"].Value = (object?)stop.RouteSourceProject ?? DBNull.Value;
            command.Parameters["$routeSourceVersion"].Value = (object?)stop.RouteSourceVersion ?? DBNull.Value;
            command.Parameters["$routeSourceUrl"].Value = (object?)stop.RouteSourceUrl ?? DBNull.Value;
            command.ExecuteNonQuery();
        }
    }

    private static void InsertBadgeAccoladeRequirements(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IEnumerable<BadgeAccoladeRequirementRecord> requirements)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO BadgeAccoladeRequirement (
                accolade_badge_id,
                prerequisite_badge_id,
                prerequisite_index,
                logic_group,
                requirement_logic_status)
            VALUES ($accoladeId, $prerequisiteId, $prerequisiteIndex, $logicGroup, $status);
            """;

        command.Parameters.Add(new SqliteParameter("$accoladeId", null));
        command.Parameters.Add(new SqliteParameter("$prerequisiteId", null));
        command.Parameters.Add(new SqliteParameter("$prerequisiteIndex", null));
        command.Parameters.Add(new SqliteParameter("$logicGroup", null));
        command.Parameters.Add(new SqliteParameter("$status", null));

        foreach (var requirement in requirements)
        {
            command.Parameters["$accoladeId"].Value = requirement.AccoladeBadgeId;
            command.Parameters["$prerequisiteId"].Value = requirement.PrerequisiteBadgeId;
            command.Parameters["$prerequisiteIndex"].Value = requirement.PrerequisiteIndex;
            command.Parameters["$logicGroup"].Value = requirement.LogicGroup;
            command.Parameters["$status"].Value = requirement.RequirementLogicStatus.ToString();
            command.ExecuteNonQuery();
        }
    }
}
