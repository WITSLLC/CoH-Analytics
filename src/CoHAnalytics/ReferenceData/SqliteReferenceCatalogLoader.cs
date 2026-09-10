using Microsoft.Data.Sqlite;

namespace CoHAnalytics.ReferenceData;

internal static class SqliteReferenceCatalogLoader
{
    public static ItemReferenceCatalogLoadResult Load(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            return ItemReferenceCatalogLoadResult.Failed("Database path is required.");
        }

        if (!File.Exists(databasePath))
        {
            return ItemReferenceCatalogLoadResult.Failed($"Reference database '{databasePath}' was not found.");
        }

        try
        {
            using var connection = new SqliteConnection(
                $"Data Source={databasePath};Mode=ReadOnly;Pooling=False");
            connection.Open();

            if (!TryReadSchemaVersion(connection, out var schemaVersion, out var schemaFailure))
            {
                return ItemReferenceCatalogLoadResult.Failed(schemaFailure);
            }

            if (schemaVersion != ReferenceDatabaseSchema.SchemaVersion)
            {
                return ItemReferenceCatalogLoadResult.Failed(
                    $"Unsupported reference database schema version {schemaVersion}; expected {ReferenceDatabaseSchema.SchemaVersion}.");
            }

            if (!TryReadManifest(connection, out var manifest, out var manifestFailure))
            {
                return ItemReferenceCatalogLoadResult.Failed(manifestFailure);
            }

            var enhancementSets = ReadEnhancementSets(connection);
            var items = ReadItems(connection);
            var namedTables = ReadEnhancementResolverNamedTables(connection);
            var zones = ReadZones(connection);
            var badges = ReadBadges(connection);
            var badgeLocations = ReadBadgeLocations(connection);
            var badgeAccoladeRequirements = ReadBadgeAccoladeRequirements(connection);
            var routeOrderingProvenance = ReadRouteOrderingProvenance(connection);
            var historyPlaqueRouteCollections = ReadHistoryPlaqueRouteCollections(connection);
            var historyPlaqueRouteStops = ReadHistoryPlaqueRouteStops(connection);
            var availabilityByItem = ReadServerAvailability(connection, "Item");
            ApplyServerAvailability(items, availabilityByItem);
            ApplyServerAvailability(enhancementSets, ReadServerAvailability(connection, "EnhancementSet"));
            var (aliasIndex, allAliasIndex, aliasFailure) = ReadAliasIndexes(connection, items);
            if (aliasFailure is not null)
            {
                return ItemReferenceCatalogLoadResult.Failed(aliasFailure);
            }

            foreach (var item in items.Values)
            {
                if (!string.IsNullOrWhiteSpace(item.EnhancementSetId)
                    && !enhancementSets.ContainsKey(item.EnhancementSetId))
                {
                    return ItemReferenceCatalogLoadResult.Failed(
                        $"Item '{item.CatalogItemId}' references unknown EnhancementSetId '{item.EnhancementSetId}'.");
                }

                if (item.Family is not (ReferenceItemFamily.Enhancement or ReferenceItemFamily.Recipe)
                    && !string.IsNullOrWhiteSpace(item.EnhancementSetId))
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
        catch (SqliteException ex)
        {
            return ItemReferenceCatalogLoadResult.Failed($"Reference database is invalid: {ex.Message}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ItemReferenceCatalogLoadResult.Failed($"Reference database load failed: {ex.Message}");
        }
    }

    private static bool TryReadSchemaVersion(
        SqliteConnection connection,
        out int schemaVersion,
        out string failureReason)
    {
        failureReason = string.Empty;
        schemaVersion = 0;

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT schema_version FROM CatalogManifest LIMIT 1;";

        try
        {
            var value = command.ExecuteScalar();
            if (value is null || value is DBNull)
            {
                failureReason = "CatalogManifest is missing or empty.";
                return false;
            }

            schemaVersion = Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
            return true;
        }
        catch (SqliteException ex)
        {
            failureReason = $"Reference database is invalid: {ex.Message}";
            return false;
        }
    }

    private static bool TryReadManifest(
        SqliteConnection connection,
        out ItemReferenceManifest manifest,
        out string failureReason)
    {
        manifest = null!;
        failureReason = string.Empty;

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                catalog_version,
                homecoming_build_min,
                homecoming_build_max,
                source_revision,
                source_notes
            FROM CatalogManifest
            LIMIT 1;
            """;

        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            failureReason = "CatalogManifest is missing or empty.";
            return false;
        }

        var catalogVersion = reader.GetString(0);
        if (string.IsNullOrWhiteSpace(catalogVersion))
        {
            failureReason = "Manifest.CatalogVersion is required.";
            return false;
        }

        manifest = new ItemReferenceManifest
        {
            CatalogVersion = catalogVersion,
            HomecomingCompatibility = new ItemReferenceHomecomingCompatibility
            {
                BuildMin = ReadNullableString(reader, 1),
                BuildMax = ReadNullableString(reader, 2)
            },
            SourceRevision = ReadNullableString(reader, 3),
            SourceNotes = ReadNullableString(reader, 4)
        };

        return true;
    }

    private static Dictionary<string, EnhancementSetReferenceRecord> ReadEnhancementSets(SqliteConnection connection)
    {
        var sets = new Dictionary<string, EnhancementSetReferenceRecord>(StringComparer.Ordinal);
        var bonusesBySet = ReadEnhancementSetBonuses(connection);

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
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
                maximum_level
            FROM EnhancementSet;
            """;

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var catalogItemId = reader.GetString(0);
            bonusesBySet.TryGetValue(catalogItemId, out var bonuses);
            var set = new EnhancementSetReferenceRecord
            {
                CatalogItemId = catalogItemId,
                CurrentDisplayName = reader.GetString(1),
                ActiveStatus = Enum.Parse<ReferenceActiveStatus>(reader.GetString(2), ignoreCase: true),
                VerificationStatus = Enum.Parse<ReferenceVerificationStatus>(reader.GetString(3), ignoreCase: true),
                HomecomingSetId = ReadNullableString(reader, 4),
                CategoryCode = ReadNullableString(reader, 5),
                CategoryDisplayText = ReadNullableString(reader, 6),
                RarityCode = ReadNullableString(reader, 7),
                RarityDisplayText = ReadNullableString(reader, 8),
                MinimumLevel = ReadNullableInt32(reader, 9),
                MaximumLevel = ReadNullableInt32(reader, 10),
                Bonuses = bonuses ?? Array.Empty<EnhancementSetBonusReferenceRecord>(),
                ServerAvailability = Array.Empty<ReferenceServerAvailability>()
            };

            sets[set.CatalogItemId] = set;
        }

        return sets;
    }

    private static Dictionary<string, IReadOnlyList<EnhancementSetBonusReferenceRecord>> ReadEnhancementSetBonuses(
        SqliteConnection connection)
    {
        var tokensByKey = new Dictionary<(string SetId, int BonusIndex), List<string>>();
        using (var tokenCommand = connection.CreateCommand())
        {
            tokenCommand.CommandText =
                """
                SELECT set_catalog_item_id, bonus_index, token
                FROM EnhancementSetBonusRequiresToken
                ORDER BY set_catalog_item_id, bonus_index, token_index;
                """;
            using var tokenReader = tokenCommand.ExecuteReader();
            while (tokenReader.Read())
            {
                var key = (tokenReader.GetString(0), tokenReader.GetInt32(1));
                if (!tokensByKey.TryGetValue(key, out var tokens))
                {
                    tokens = [];
                    tokensByKey[key] = tokens;
                }

                tokens.Add(tokenReader.GetString(2));
            }
        }

        var requiredEnhancementsByKey =
            new Dictionary<(string SetId, int BonusIndex), List<string>>();
        using (var requiredCommand = connection.CreateCommand())
        {
            requiredCommand.CommandText =
                """
                SELECT set_catalog_item_id, bonus_index, enhancement_catalog_item_id
                FROM EnhancementSetBonusRequiredEnhancement
                ORDER BY set_catalog_item_id, bonus_index, enhancement_catalog_item_id;
                """;
            using var requiredReader = requiredCommand.ExecuteReader();
            while (requiredReader.Read())
            {
                var key = (requiredReader.GetString(0), requiredReader.GetInt32(1));
                if (!requiredEnhancementsByKey.TryGetValue(key, out var required))
                {
                    required = [];
                    requiredEnhancementsByKey[key] = required;
                }

                required.Add(requiredReader.GetString(2));
            }
        }

        var powersByKey = new Dictionary<(string SetId, int BonusIndex), List<EnhancementSetBonusPowerReferenceRecord>>();
        using (var powerCommand = connection.CreateCommand())
        {
            powerCommand.CommandText =
                """
                SELECT set_catalog_item_id, bonus_index, power_index, homecoming_source_id, display_name, display_help,
                       boost_use_player_level, max_boost_level, boost_boostable
                FROM EnhancementSetBonusPower
                ORDER BY set_catalog_item_id, bonus_index, power_index;
                """;
            using var powerReader = powerCommand.ExecuteReader();
            var bonusPowerEffects = ReadEnhancementSetBonusPowerEffects(connection);
            while (powerReader.Read())
            {
                var key = (powerReader.GetString(0), powerReader.GetInt32(1));
                if (!powersByKey.TryGetValue(key, out var powers))
                {
                    powers = [];
                    powersByKey[key] = powers;
                }

                var powerIndex = powerReader.GetInt32(2);
                bonusPowerEffects.TryGetValue((key.Item1, key.Item2, powerIndex), out var effects);
                powers.Add(new EnhancementSetBonusPowerReferenceRecord
                {
                    HomecomingSourceId = powerReader.GetString(3),
                    DisplayName = ReadNullableString(powerReader, 4),
                    DisplayHelp = ReadNullableString(powerReader, 5),
                    BoostUsePlayerLevel = powerReader.GetInt32(6) != 0,
                    MaxBoostLevel = powerReader.GetInt32(7),
                    BoostBoostable = powerReader.GetInt32(8) != 0,
                    Effects = effects ?? Array.Empty<EnhancementSourceVariantEffectReferenceRecord>()
                });
            }
        }

        var bonusesBySet = new Dictionary<string, List<EnhancementSetBonusReferenceRecord>>(StringComparer.Ordinal);
        using var bonusCommand = connection.CreateCommand();
        bonusCommand.CommandText =
            """
            SELECT set_catalog_item_id, bonus_index, minimum_boosts, maximum_boosts, requires_pattern
            FROM EnhancementSetBonus
            ORDER BY set_catalog_item_id, bonus_index;
            """;
        using var bonusReader = bonusCommand.ExecuteReader();
        while (bonusReader.Read())
        {
            var setId = bonusReader.GetString(0);
            var bonusIndex = bonusReader.GetInt32(1);
            var key = (setId, bonusIndex);
            powersByKey.TryGetValue(key, out var powers);
            tokensByKey.TryGetValue(key, out var tokens);
            requiredEnhancementsByKey.TryGetValue(key, out var requiredEnhancements);
            if (!bonusesBySet.TryGetValue(setId, out var bonuses))
            {
                bonuses = [];
                bonusesBySet[setId] = bonuses;
            }

            bonuses.Add(new EnhancementSetBonusReferenceRecord
            {
                MinimumBoosts = bonusReader.GetInt32(2),
                MaximumBoosts = bonusReader.GetInt32(3),
                RequiresPattern = Enum.Parse<ReferenceEnhancementSetBonusRequiresPattern>(
                    bonusReader.GetString(4),
                    ignoreCase: true),
                RequiresTokens = tokens ?? [],
                RequiredEnhancementIds = requiredEnhancements ?? [],
                AutoPowers = powers ?? []
            });
        }

        return bonusesBySet.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<EnhancementSetBonusReferenceRecord>)pair.Value,
            StringComparer.Ordinal);
    }

    private static Dictionary<string, ItemReferenceRecord> ReadItems(SqliteConnection connection)
    {
        var items = new Dictionary<string, ItemReferenceRecord>(StringComparer.Ordinal);
        var variantsByItem = ReadEnhancementSourceVariants(connection);
        var recipeLevelsByItem = ReadRecipeLevels(connection);
        var excludedLevelsByItem = ReadRecipeExcludedSourceLevels(connection);

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
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
                short_help_message_key
            FROM Item;
            """;

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var catalogItemId = reader.GetString(0);
            variantsByItem.TryGetValue(catalogItemId, out var variants);
            recipeLevelsByItem.TryGetValue(catalogItemId, out var recipeLevels);
            excludedLevelsByItem.TryGetValue(catalogItemId, out var excludedLevels);
            ReferenceEnhancementFamily? enhancementFamily = reader.IsDBNull(3)
                ? null
                : Enum.Parse<ReferenceEnhancementFamily>(reader.GetString(3), ignoreCase: true);
            var item = new ItemReferenceRecord
            {
                CatalogItemId = catalogItemId,
                Family = Enum.Parse<ReferenceItemFamily>(reader.GetString(1), ignoreCase: true),
                Subtype = reader.GetString(2),
                EnhancementFamily = enhancementFamily,
                CurrentDisplayName = reader.GetString(4),
                ActiveStatus = Enum.Parse<ReferenceActiveStatus>(reader.GetString(5), ignoreCase: true),
                VerificationStatus = Enum.Parse<ReferenceVerificationStatus>(reader.GetString(6), ignoreCase: true),
                Rarity = ReadNullableString(reader, 7),
                Origin = ReadNullableString(reader, 8),
                Tier = ReadNullableString(reader, 9),
                EnhancementSetId = ReadNullableString(reader, 10),
                ProducedItemId = ReadNullableString(reader, 11),
                Variant = ReadNullableString(reader, 12),
                CommonIoBoostType = ReadNullableString(reader, 13),
                CommonIoBoostTypeDisplayText = ReadNullableString(reader, 14),
                Icon = ReadNullableString(reader, 15),
                DisplayHelp = ReadNullableString(reader, 16),
                ShortHelp = ReadNullableString(reader, 17),
                HomecomingSourceId = ReadNullableString(reader, 18),
                HomecomingCategory = ReadNullableString(reader, 19),
                InspirationStandardTier = ReadNullableString(reader, 20),
                InspirationForm = ReadNullableString(reader, 21),
                DisplayNameMessageKey = ReadNullableString(reader, 22),
                DisplayHelpMessageKey = ReadNullableString(reader, 23),
                ShortHelpMessageKey = ReadNullableString(reader, 24),
                SourceVariants = variants ?? Array.Empty<EnhancementSourceVariantReferenceRecord>(),
                RecipeLevels = recipeLevels ?? Array.Empty<RecipeLevelReferenceRecord>(),
                ExcludedHomecomingSourceLevels =
                    excludedLevels ?? Array.Empty<RecipeExcludedSourceLevelReferenceRecord>(),
                ServerAvailability = Array.Empty<ReferenceServerAvailability>()
            };

            items[item.CatalogItemId] = item;
        }

        return items;
    }

    private static Dictionary<string, IReadOnlyList<RecipeLevelReferenceRecord>> ReadRecipeLevels(
        SqliteConnection connection)
    {
        var requirementsByLevel = ReadRecipeLevelRequirements(connection);
        var levelsByItem = new Dictionary<string, List<RecipeLevelReferenceRecord>>(StringComparer.Ordinal);
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT catalog_item_id, level, homecoming_source_id, crafting_cost
            FROM RecipeLevel
            ORDER BY catalog_item_id, level;
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var catalogItemId = reader.GetString(0);
            var level = reader.GetInt32(1);
            if (!levelsByItem.TryGetValue(catalogItemId, out var levels))
            {
                levels = [];
                levelsByItem[catalogItemId] = levels;
            }

            requirementsByLevel.TryGetValue((catalogItemId, level), out var requirements);
            levels.Add(new RecipeLevelReferenceRecord
            {
                Level = level,
                HomecomingSourceId = reader.GetString(2),
                CraftingCost = checked((uint)reader.GetInt64(3)),
                Requirements = requirements ?? Array.Empty<RecipeRequirementReferenceRecord>()
            });
        }

        return levelsByItem.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<RecipeLevelReferenceRecord>)pair.Value,
            StringComparer.Ordinal);
    }

    private static Dictionary<(string CatalogItemId, int Level), IReadOnlyList<RecipeRequirementReferenceRecord>>
        ReadRecipeLevelRequirements(SqliteConnection connection)
    {
        var requirementsByLevel =
            new Dictionary<(string CatalogItemId, int Level), List<RecipeRequirementReferenceRecord>>();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT catalog_item_id, level, salvage_catalog_item_id, quantity
            FROM RecipeLevelRequirement
            ORDER BY catalog_item_id, level, salvage_catalog_item_id;
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var key = (reader.GetString(0), reader.GetInt32(1));
            if (!requirementsByLevel.TryGetValue(key, out var requirements))
            {
                requirements = [];
                requirementsByLevel[key] = requirements;
            }

            requirements.Add(new RecipeRequirementReferenceRecord
            {
                SalvageItemId = reader.GetString(2),
                Quantity = checked((uint)reader.GetInt64(3))
            });
        }

        return requirementsByLevel.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<RecipeRequirementReferenceRecord>)pair.Value);
    }

    private static Dictionary<string, IReadOnlyList<RecipeExcludedSourceLevelReferenceRecord>>
        ReadRecipeExcludedSourceLevels(SqliteConnection connection)
    {
        var excludedByItem =
            new Dictionary<string, List<RecipeExcludedSourceLevelReferenceRecord>>(StringComparer.Ordinal);
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT catalog_item_id, level, homecoming_source_id, crafting_cost
            FROM RecipeExcludedSourceLevel
            ORDER BY catalog_item_id, level;
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var catalogItemId = reader.GetString(0);
            if (!excludedByItem.TryGetValue(catalogItemId, out var excluded))
            {
                excluded = [];
                excludedByItem[catalogItemId] = excluded;
            }

            excluded.Add(new RecipeExcludedSourceLevelReferenceRecord
            {
                Level = reader.GetInt32(1),
                HomecomingSourceId = reader.GetString(2),
                CraftingCost = checked((uint)reader.GetInt64(3))
            });
        }

        return excludedByItem.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<RecipeExcludedSourceLevelReferenceRecord>)pair.Value,
            StringComparer.Ordinal);
    }

    private static Dictionary<string, IReadOnlyList<EnhancementSourceVariantReferenceRecord>> ReadEnhancementSourceVariants(
        SqliteConnection connection)
    {
        var effectsByVariant = ReadEnhancementSourceVariantEffects(connection);
        var variantsByItem = new Dictionary<string, List<EnhancementSourceVariantReferenceRecord>>(StringComparer.Ordinal);
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT catalog_item_id, variant_index, homecoming_source_id, source_form, icon, display_help, short_help,
                   boost_use_player_level, max_boost_level, boost_boostable
            FROM EnhancementSourceVariant
            ORDER BY catalog_item_id, variant_index;
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var catalogItemId = reader.GetString(0);
            var variantIndex = reader.GetInt32(1);
            if (!variantsByItem.TryGetValue(catalogItemId, out var variants))
            {
                variants = [];
                variantsByItem[catalogItemId] = variants;
            }

            effectsByVariant.TryGetValue((catalogItemId, variantIndex), out var effects);
            variants.Add(new EnhancementSourceVariantReferenceRecord
            {
                HomecomingSourceId = reader.GetString(2),
                SourceForm = reader.GetString(3),
                Icon = ReadNullableString(reader, 4),
                DisplayHelp = ReadNullableString(reader, 5),
                ShortHelp = ReadNullableString(reader, 6),
                BoostUsePlayerLevel = reader.GetInt32(7) != 0,
                MaxBoostLevel = reader.GetInt32(8),
                BoostBoostable = reader.GetInt32(9) != 0,
                Effects = effects ?? Array.Empty<EnhancementSourceVariantEffectReferenceRecord>()
            });
        }

        return variantsByItem.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<EnhancementSourceVariantReferenceRecord>)pair.Value,
            StringComparer.Ordinal);
    }

    private static Dictionary<(string CatalogItemId, int VariantIndex), IReadOnlyList<EnhancementSourceVariantEffectReferenceRecord>>
        ReadEnhancementSourceVariantEffects(SqliteConnection connection)
    {
        var effectsByVariant =
            new Dictionary<(string, int), List<EnhancementSourceVariantEffectReferenceRecord>>();
        var attribIdsByEffect =
            new Dictionary<(string CatalogItemId, int VariantIndex, int EffectIndex), List<uint>>();

        using (var attribCommand = connection.CreateCommand())
        {
            attribCommand.CommandText =
                """
                SELECT catalog_item_id, variant_index, effect_index, attrib_id
                FROM EnhancementSourceVariantEffectAttrib
                ORDER BY catalog_item_id, variant_index, effect_index, attrib_index;
                """;
            using var attribReader = attribCommand.ExecuteReader();
            while (attribReader.Read())
            {
                var key = (attribReader.GetString(0), attribReader.GetInt32(1), attribReader.GetInt32(2));
                if (!attribIdsByEffect.TryGetValue(key, out var attribIds))
                {
                    attribIds = [];
                    attribIdsByEffect[key] = attribIds;
                }

                attribIds.Add(Convert.ToUInt32(attribReader.GetInt64(3)));
            }
        }

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT catalog_item_id, variant_index, effect_index, tag, table_name, scale
            FROM EnhancementSourceVariantEffect
            ORDER BY catalog_item_id, variant_index, effect_index;
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var variantKey = (reader.GetString(0), reader.GetInt32(1));
            var effectIndex = reader.GetInt32(2);
            if (!effectsByVariant.TryGetValue(variantKey, out var effects))
            {
                effects = [];
                effectsByVariant[variantKey] = effects;
            }

            attribIdsByEffect.TryGetValue((variantKey.Item1, variantKey.Item2, effectIndex), out var attribIds);
            effects.Add(new EnhancementSourceVariantEffectReferenceRecord
            {
                Tag = reader.GetString(3),
                Table = reader.GetString(4),
                Scale = reader.GetFloat(5),
                AttribIds = attribIds?.ToArray() ?? Array.Empty<uint>()
            });
        }

        return effectsByVariant.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<EnhancementSourceVariantEffectReferenceRecord>)pair.Value);
    }

    private static Dictionary<(string SetId, int BonusIndex, int PowerIndex), IReadOnlyList<EnhancementSourceVariantEffectReferenceRecord>>
        ReadEnhancementSetBonusPowerEffects(SqliteConnection connection)
    {
        var effectsByPower =
            new Dictionary<(string, int, int), List<EnhancementSourceVariantEffectReferenceRecord>>();
        var attribIdsByEffect =
            new Dictionary<(string SetId, int BonusIndex, int PowerIndex, int EffectIndex), List<uint>>();

        using (var attribCommand = connection.CreateCommand())
        {
            attribCommand.CommandText =
                """
                SELECT set_catalog_item_id, bonus_index, power_index, effect_index, attrib_id
                FROM EnhancementSetBonusPowerEffectAttrib
                ORDER BY set_catalog_item_id, bonus_index, power_index, effect_index, attrib_index;
                """;
            using var attribReader = attribCommand.ExecuteReader();
            while (attribReader.Read())
            {
                var key = (
                    attribReader.GetString(0),
                    attribReader.GetInt32(1),
                    attribReader.GetInt32(2),
                    attribReader.GetInt32(3));
                if (!attribIdsByEffect.TryGetValue(key, out var attribIds))
                {
                    attribIds = [];
                    attribIdsByEffect[key] = attribIds;
                }

                attribIds.Add(Convert.ToUInt32(attribReader.GetInt64(4)));
            }
        }

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT set_catalog_item_id, bonus_index, power_index, effect_index, tag, table_name, scale
            FROM EnhancementSetBonusPowerEffect
            ORDER BY set_catalog_item_id, bonus_index, power_index, effect_index;
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var powerKey = (reader.GetString(0), reader.GetInt32(1), reader.GetInt32(2));
            var effectIndex = reader.GetInt32(3);
            if (!effectsByPower.TryGetValue(powerKey, out var effects))
            {
                effects = [];
                effectsByPower[powerKey] = effects;
            }

            attribIdsByEffect.TryGetValue((powerKey.Item1, powerKey.Item2, powerKey.Item3, effectIndex), out var attribIds);
            effects.Add(new EnhancementSourceVariantEffectReferenceRecord
            {
                Tag = reader.GetString(4),
                Table = reader.GetString(5),
                Scale = reader.GetFloat(6),
                AttribIds = attribIds?.ToArray() ?? Array.Empty<uint>()
            });
        }

        return effectsByPower.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<EnhancementSourceVariantEffectReferenceRecord>)pair.Value);
    }

    private static Dictionary<string, IReadOnlyList<float>> ReadEnhancementResolverNamedTables(SqliteConnection connection)
    {
        var tables = new Dictionary<string, List<float>>(StringComparer.Ordinal);
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT table_name, value_index, value
            FROM EnhancementResolverNamedTableValue
            ORDER BY table_name, value_index;
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var tableName = reader.GetString(0);
            if (!tables.TryGetValue(tableName, out var values))
            {
                values = [];
                tables[tableName] = values;
            }

            values.Add(reader.GetFloat(2));
        }

        return tables.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<float>)pair.Value,
            StringComparer.Ordinal);
    }

    private static Dictionary<string, IReadOnlyList<ReferenceServerAvailability>> ReadServerAvailability(
        SqliteConnection connection,
        string entityKind)
    {
        var availabilityByItem = new Dictionary<string, List<ReferenceServerAvailability>>(StringComparer.Ordinal);
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT catalog_item_id, server_key, availability_status
            FROM ReferenceServerAvailability
            WHERE entity_kind = $entityKind
            ORDER BY catalog_item_id, server_key;
            """;
        command.Parameters.AddWithValue("$entityKind", entityKind);

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var catalogItemId = reader.GetString(0);
            if (!availabilityByItem.TryGetValue(catalogItemId, out var entries))
            {
                entries = [];
                availabilityByItem[catalogItemId] = entries;
            }

            entries.Add(new ReferenceServerAvailability
            {
                ServerKey = reader.GetString(1),
                Status = Enum.Parse<ReferenceServerAvailabilityStatus>(
                    reader.GetString(2),
                    ignoreCase: true)
            });
        }

        return availabilityByItem.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<ReferenceServerAvailability>)pair.Value,
            StringComparer.Ordinal);
    }

    private static void ApplyServerAvailability(
        Dictionary<string, ItemReferenceRecord> items,
        IReadOnlyDictionary<string, IReadOnlyList<ReferenceServerAvailability>> availabilityByItem)
    {
        foreach (var (catalogItemId, item) in items)
        {
            availabilityByItem.TryGetValue(catalogItemId, out var availability);
            items[catalogItemId] = item with { ServerAvailability = availability ?? Array.Empty<ReferenceServerAvailability>() };
        }
    }

    private static void ApplyServerAvailability(
        Dictionary<string, EnhancementSetReferenceRecord> sets,
        IReadOnlyDictionary<string, IReadOnlyList<ReferenceServerAvailability>> availabilityByItem)
    {
        foreach (var (catalogItemId, set) in sets)
        {
            availabilityByItem.TryGetValue(catalogItemId, out var availability);
            sets[catalogItemId] = set with
            {
                ServerAvailability = availability ?? Array.Empty<ReferenceServerAvailability>()
            };
        }
    }

    private static (
        Dictionary<string, (string CatalogItemId, string MatchedAliasText)> AliasIndex,
        Dictionary<string, (string CatalogItemId, string MatchedAliasText)> AllAliasIndex,
        string? FailureReason) ReadAliasIndexes(
        SqliteConnection connection,
        IReadOnlyDictionary<string, ItemReferenceRecord> items)
    {
        var aliasIndex = new Dictionary<string, (string CatalogItemId, string MatchedAliasText)>(
            StringComparer.OrdinalIgnoreCase);
        var allAliasIndex = new Dictionary<string, (string CatalogItemId, string MatchedAliasText)>(
            StringComparer.OrdinalIgnoreCase);

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT lookup_key, catalog_item_id, text
            FROM ItemAlias;
            """;

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var lookupKey = reader.GetString(0);
            var catalogItemId = reader.GetString(1);
            var text = reader.GetString(2);

            if (!items.TryGetValue(catalogItemId, out var item))
            {
                return (aliasIndex, allAliasIndex, $"Alias references unknown CatalogItemId '{catalogItemId}'.");
            }

            if (allAliasIndex.TryGetValue(lookupKey, out var existingAll)
                && !string.Equals(existingAll.CatalogItemId, catalogItemId, StringComparison.Ordinal))
            {
                return (
                    aliasIndex,
                    allAliasIndex,
                    $"Alias lookup key '{lookupKey}' is assigned to both '{existingAll.CatalogItemId}' and '{catalogItemId}'.");
            }

            allAliasIndex[lookupKey] = (catalogItemId, text);

            var includeInCurrentIndex = item.Family is not ReferenceItemFamily.Enhancement
                || ReferenceServerAvailabilitySupport.IsCurrentHomecoming(item.ServerAvailability);
            if (!includeInCurrentIndex)
            {
                continue;
            }

            if (aliasIndex.TryGetValue(lookupKey, out var existingCurrent)
                && !string.Equals(existingCurrent.CatalogItemId, catalogItemId, StringComparison.Ordinal))
            {
                return (
                    aliasIndex,
                    allAliasIndex,
                    $"Alias lookup key '{lookupKey}' is assigned to both '{existingCurrent.CatalogItemId}' and '{catalogItemId}'.");
            }

            aliasIndex[lookupKey] = (catalogItemId, text);
        }

        return (aliasIndex, allAliasIndex, null);
    }

    private static string? ReadNullableString(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static int? ReadNullableInt32(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);

    private static uint? ReadNullableUInt32(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : Convert.ToUInt32(reader.GetInt64(ordinal));

    private static double? ReadNullableDouble(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetDouble(ordinal);

    private static Dictionary<string, ZoneReferenceRecord> ReadZones(SqliteConnection connection)
    {
        var zones = new Dictionary<string, ZoneReferenceRecord>(StringComparer.Ordinal);
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT zone_id, display_name, alignment_notes, level_range, zone_type
            FROM Zone;
            """;

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var zoneId = reader.GetString(0);
            zones[zoneId] = new ZoneReferenceRecord
            {
                ZoneId = zoneId,
                DisplayName = reader.GetString(1),
                AlignmentNotes = ReadNullableString(reader, 2),
                LevelRange = ReadNullableString(reader, 3),
                ZoneType = ReadNullableString(reader, 4),
                ExplorationCompletionBadgeIds = ReadZoneCompletionBadges(connection, zoneId, "ZoneExplorationCompletionBadge"),
                HistoryCompletionBadgeIds = ReadZoneCompletionBadges(connection, zoneId, "ZoneHistoryCompletionBadge")
            };
        }

        return zones;
    }

    private static IReadOnlyList<string> ReadZoneCompletionBadges(
        SqliteConnection connection,
        string zoneId,
        string tableName)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            SELECT badge_catalog_item_id
            FROM {tableName}
            WHERE zone_id = $zoneId
            ORDER BY badge_index;
            """;
        command.Parameters.AddWithValue("$zoneId", zoneId);

        var badges = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            badges.Add(reader.GetString(0));
        }

        return badges;
    }

    private static Dictionary<string, BadgeReferenceRecord> ReadBadges(SqliteConnection connection)
    {
        var badges = new Dictionary<string, BadgeReferenceRecord>(StringComparer.Ordinal);
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
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
                requirement_logic_pattern
            FROM Badge;
            """;

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var catalogItemId = reader.GetString(0);
            badges[catalogItemId] = new BadgeReferenceRecord
            {
                CatalogItemId = catalogItemId,
                HomecomingSourceId = reader.GetString(1),
                SetTitleId = ReadNullableUInt32(reader, 2),
                CanonicalCategory = reader.GetString(3),
                BadgeType = Convert.ToUInt32(reader.GetInt64(4)),
                ReferenceKind = Enum.Parse<ReferenceBadgeKind>(reader.GetString(5), ignoreCase: true),
                HeroName = reader.GetString(6),
                VillainName = reader.GetString(7),
                HeroDescription = ReadNullableString(reader, 8),
                VillainDescription = ReadNullableString(reader, 9),
                HeroIcon = ReadNullableString(reader, 10),
                VillainIcon = ReadNullableString(reader, 11),
                ZoneId = ReadNullableString(reader, 12),
                CompletionBadgeId = ReadNullableString(reader, 13),
                IsZoneCompletionBadge = reader.GetInt32(14) == 1,
                VerificationStatus = Enum.Parse<ReferenceVerificationStatus>(reader.GetString(15), ignoreCase: true),
                RequirementText = ReadNullableString(reader, 16),
                RewardText = ReadNullableString(reader, 17),
                RequirementLogicStatus = ReadNullableEnum<ReferenceRequirementLogicStatus>(reader, 18),
                RequirementLogicPattern = ReadNullableEnum<ReferenceRequirementLogicPattern>(reader, 19)
            };
        }

        return badges;
    }

    private static IReadOnlyList<BadgeLocationReferenceRecord> ReadBadgeLocations(SqliteConnection connection)
    {
        var locations = new List<BadgeLocationReferenceRecord>();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
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
                route_mapping_confidence
            FROM BadgeLocation
            ORDER BY badge_catalog_item_id, location_index;
            """;

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            locations.Add(new BadgeLocationReferenceRecord
            {
                BadgeCatalogItemId = reader.GetString(0),
                LocationIndex = reader.GetInt32(1),
                ZoneId = reader.GetString(2),
                CoordinateX = ReadNullableDouble(reader, 3),
                CoordinateY = ReadNullableDouble(reader, 4),
                CoordinateZ = ReadNullableDouble(reader, 5),
                ThumbtackCommand = ReadNullableString(reader, 6),
                MarkerType = ReadNullableString(reader, 7),
                LocationRole = ReadNullableString(reader, 8),
                CoordinateSemantics = ReadNullableString(reader, 9),
                VerificationStatus = Enum.Parse<ReferenceVerificationStatus>(reader.GetString(10), ignoreCase: true),
                TriggerDescription = ReadNullableString(reader, 11),
                ExplorationRouteOrder = ReadNullableInt32(reader, 12),
                RouteSourceProject = ReadNullableString(reader, 13),
                RouteSourceVersion = ReadNullableString(reader, 14),
                RouteSourceUrl = ReadNullableString(reader, 15),
                RouteMappingConfidence = ReadNullableString(reader, 16)
            });
        }

        return locations;
    }

    private static RouteOrderingProvenanceRecord? ReadRouteOrderingProvenance(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                source_name,
                author,
                version,
                maps_thread_url,
                popmenu_thread_url,
                ordering_semantics,
                retrieved
            FROM RouteOrderingProvenance
            LIMIT 1;
            """;

        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return new RouteOrderingProvenanceRecord
        {
            SourceName = reader.GetString(0),
            Author = reader.GetString(1),
            Version = reader.GetString(2),
            MapsThreadUrl = ReadNullableString(reader, 3),
            PopmenuThreadUrl = ReadNullableString(reader, 4),
            OrderingSemantics = ReadNullableString(reader, 5),
            Retrieved = ReadNullableString(reader, 6)
        };
    }

    private static IReadOnlyList<HistoryPlaqueRouteCollectionRecord> ReadHistoryPlaqueRouteCollections(
        SqliteConnection connection)
    {
        var collections = new List<HistoryPlaqueRouteCollectionRecord>();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                collection_name,
                completion_badge_id,
                published_collection_order_available,
                ordering_status,
                ordering_notes
            FROM HistoryPlaqueRouteCollection
            ORDER BY collection_name;
            """;

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            collections.Add(new HistoryPlaqueRouteCollectionRecord
            {
                CollectionName = reader.GetString(0),
                CompletionBadgeId = reader.GetString(1),
                PublishedCollectionOrderAvailable = reader.GetInt32(2) == 1,
                OrderingStatus = reader.GetString(3),
                OrderingNotes = ReadNullableString(reader, 4)
            });
        }

        return collections;
    }

    private static IReadOnlyList<HistoryPlaqueRouteStopRecord> ReadHistoryPlaqueRouteStops(SqliteConnection connection)
    {
        var stops = new List<HistoryPlaqueRouteStopRecord>();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
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
                route_source_url
            FROM HistoryPlaqueRouteStop
            ORDER BY collection_name, inventory_order;
            """;

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            stops.Add(new HistoryPlaqueRouteStopRecord
            {
                CollectionName = reader.GetString(0),
                InventoryOrder = reader.GetInt32(1),
                RouteOrder = ReadNullableInt32(reader, 2),
                CompletionBadgeId = reader.GetString(3),
                ZoneId = reader.GetString(4),
                LocationIndex = reader.GetInt32(5),
                PlaqueName = ReadNullableString(reader, 6),
                SourceZoneRouteOrder = ReadNullableInt32(reader, 7),
                RouteMappingConfidence = ReadNullableString(reader, 8),
                RouteSourceProject = ReadNullableString(reader, 9),
                RouteSourceVersion = ReadNullableString(reader, 10),
                RouteSourceUrl = ReadNullableString(reader, 11)
            });
        }

        return stops;
    }

    private static IReadOnlyList<BadgeAccoladeRequirementRecord> ReadBadgeAccoladeRequirements(SqliteConnection connection)
    {
        var requirements = new List<BadgeAccoladeRequirementRecord>();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                accolade_badge_id,
                prerequisite_badge_id,
                prerequisite_index,
                logic_group,
                requirement_logic_status
            FROM BadgeAccoladeRequirement
            ORDER BY accolade_badge_id, prerequisite_index;
            """;

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            requirements.Add(new BadgeAccoladeRequirementRecord
            {
                AccoladeBadgeId = reader.GetString(0),
                PrerequisiteBadgeId = reader.GetString(1),
                PrerequisiteIndex = reader.GetInt32(2),
                LogicGroup = reader.GetInt32(3),
                RequirementLogicStatus = Enum.Parse<ReferenceRequirementLogicStatus>(
                    reader.GetString(4),
                    ignoreCase: true)
            });
        }

        return requirements;
    }

    private static TEnum? ReadNullableEnum<TEnum>(SqliteDataReader reader, int ordinal)
        where TEnum : struct, Enum =>
        reader.IsDBNull(ordinal)
            ? null
            : Enum.Parse<TEnum>(reader.GetString(ordinal), ignoreCase: true);
}
