namespace CoHAnalytics.ReferenceData;

/// <summary>SQLite schema constants for the generated item reference database.</summary>
internal static class ReferenceDatabaseSchema
{
    public const int SchemaVersion = 12;

    public const string CreateCatalogManifestSql =
        """
        CREATE TABLE CatalogManifest (
            schema_version INTEGER NOT NULL PRIMARY KEY CHECK (schema_version = 12),
            catalog_version TEXT NOT NULL,
            homecoming_build_min TEXT,
            homecoming_build_max TEXT,
            source_revision TEXT,
            source_notes TEXT,
            generated_at_utc TEXT NOT NULL
        );
        """;

    public const string CreateEnhancementSetSql =
        """
        CREATE TABLE EnhancementSet (
            catalog_item_id TEXT PRIMARY KEY,
            current_display_name TEXT NOT NULL,
            active_status TEXT NOT NULL,
            verification_status TEXT NOT NULL,
            homecoming_set_id TEXT,
            category_code TEXT,
            category_display_text TEXT,
            rarity_code TEXT,
            rarity_display_text TEXT,
            minimum_level INTEGER,
            maximum_level INTEGER,
            CHECK (catalog_item_id GLOB 'SET-[0-9][0-9][0-9][0-9][0-9]')
        );
        """;

    public const string CreateEnhancementSetBonusSql =
        """
        CREATE TABLE EnhancementSetBonus (
            set_catalog_item_id TEXT NOT NULL REFERENCES EnhancementSet(catalog_item_id),
            bonus_index INTEGER NOT NULL,
            minimum_boosts INTEGER NOT NULL,
            maximum_boosts INTEGER NOT NULL,
            requires_pattern TEXT NOT NULL,
            PRIMARY KEY (set_catalog_item_id, bonus_index)
        );
        """;

    public const string CreateEnhancementSetBonusRequiresTokenSql =
        """
        CREATE TABLE EnhancementSetBonusRequiresToken (
            set_catalog_item_id TEXT NOT NULL,
            bonus_index INTEGER NOT NULL,
            token_index INTEGER NOT NULL,
            token TEXT NOT NULL,
            PRIMARY KEY (set_catalog_item_id, bonus_index, token_index),
            FOREIGN KEY (set_catalog_item_id, bonus_index)
                REFERENCES EnhancementSetBonus(set_catalog_item_id, bonus_index)
        );
        """;

    public const string CreateEnhancementSetBonusRequiredEnhancementSql =
        """
        CREATE TABLE EnhancementSetBonusRequiredEnhancement (
            set_catalog_item_id TEXT NOT NULL,
            bonus_index INTEGER NOT NULL,
            enhancement_catalog_item_id TEXT NOT NULL REFERENCES Item(catalog_item_id),
            PRIMARY KEY (set_catalog_item_id, bonus_index, enhancement_catalog_item_id),
            FOREIGN KEY (set_catalog_item_id, bonus_index)
                REFERENCES EnhancementSetBonus(set_catalog_item_id, bonus_index)
        );
        """;

    public const string CreateEnhancementSetBonusPowerSql =
        """
        CREATE TABLE EnhancementSetBonusPower (
            set_catalog_item_id TEXT NOT NULL,
            bonus_index INTEGER NOT NULL,
            power_index INTEGER NOT NULL,
            homecoming_source_id TEXT NOT NULL,
            display_help TEXT,
            boost_use_player_level INTEGER NOT NULL CHECK (boost_use_player_level IN (0, 1)),
            max_boost_level INTEGER NOT NULL,
            boost_boostable INTEGER NOT NULL CHECK (boost_boostable IN (0, 1)),
            PRIMARY KEY (set_catalog_item_id, bonus_index, power_index),
            FOREIGN KEY (set_catalog_item_id, bonus_index)
                REFERENCES EnhancementSetBonus(set_catalog_item_id, bonus_index)
        );
        """;

    public const string CreateEnhancementSetBonusPowerEffectSql =
        """
        CREATE TABLE EnhancementSetBonusPowerEffect (
            set_catalog_item_id TEXT NOT NULL,
            bonus_index INTEGER NOT NULL,
            power_index INTEGER NOT NULL,
            effect_index INTEGER NOT NULL,
            tag TEXT NOT NULL,
            table_name TEXT NOT NULL,
            scale REAL NOT NULL,
            PRIMARY KEY (set_catalog_item_id, bonus_index, power_index, effect_index),
            FOREIGN KEY (set_catalog_item_id, bonus_index, power_index)
                REFERENCES EnhancementSetBonusPower(set_catalog_item_id, bonus_index, power_index)
        );
        """;

    public const string CreateEnhancementSetBonusPowerEffectAttribSql =
        """
        CREATE TABLE EnhancementSetBonusPowerEffectAttrib (
            set_catalog_item_id TEXT NOT NULL,
            bonus_index INTEGER NOT NULL,
            power_index INTEGER NOT NULL,
            effect_index INTEGER NOT NULL,
            attrib_index INTEGER NOT NULL,
            attrib_id INTEGER NOT NULL,
            PRIMARY KEY (set_catalog_item_id, bonus_index, power_index, effect_index, attrib_index),
            FOREIGN KEY (set_catalog_item_id, bonus_index, power_index, effect_index)
                REFERENCES EnhancementSetBonusPowerEffect(
                    set_catalog_item_id, bonus_index, power_index, effect_index)
        );
        """;

    public const string CreateItemSql =
        """
        CREATE TABLE Item (
            catalog_item_id TEXT PRIMARY KEY,
            family TEXT NOT NULL,
            subtype TEXT NOT NULL,
            enhancement_family TEXT,
            current_display_name TEXT NOT NULL,
            active_status TEXT NOT NULL,
            verification_status TEXT NOT NULL,
            rarity TEXT,
            origin TEXT,
            tier TEXT,
            enhancement_set_id TEXT REFERENCES EnhancementSet(catalog_item_id),
            produced_item_id TEXT REFERENCES Item(catalog_item_id),
            variant TEXT,
            common_io_boost_type TEXT,
            common_io_boost_type_display_text TEXT,
            icon TEXT,
            display_help TEXT,
            short_help TEXT,
            homecoming_source_id TEXT,
            homecoming_category TEXT,
            inspiration_standard_tier TEXT,
            inspiration_form TEXT,
            display_name_message_key TEXT,
            display_help_message_key TEXT,
            short_help_message_key TEXT,
            CHECK (catalog_item_id GLOB '[A-Z][A-Z][A-Z]-[0-9][0-9][0-9][0-9][0-9]')
        );
        """;

    public const string CreateEnhancementSourceVariantSql =
        """
        CREATE TABLE EnhancementSourceVariant (
            catalog_item_id TEXT NOT NULL REFERENCES Item(catalog_item_id),
            variant_index INTEGER NOT NULL,
            homecoming_source_id TEXT NOT NULL,
            source_form TEXT NOT NULL,
            icon TEXT,
            display_help TEXT,
            short_help TEXT,
            boost_use_player_level INTEGER NOT NULL CHECK (boost_use_player_level IN (0, 1)),
            max_boost_level INTEGER NOT NULL,
            boost_boostable INTEGER NOT NULL CHECK (boost_boostable IN (0, 1)),
            PRIMARY KEY (catalog_item_id, variant_index)
        );
        """;

    public const string CreateEnhancementSourceVariantEffectSql =
        """
        CREATE TABLE EnhancementSourceVariantEffect (
            catalog_item_id TEXT NOT NULL,
            variant_index INTEGER NOT NULL,
            effect_index INTEGER NOT NULL,
            tag TEXT NOT NULL,
            table_name TEXT NOT NULL,
            scale REAL NOT NULL,
            PRIMARY KEY (catalog_item_id, variant_index, effect_index),
            FOREIGN KEY (catalog_item_id, variant_index)
                REFERENCES EnhancementSourceVariant(catalog_item_id, variant_index)
        );
        """;

    public const string CreateEnhancementSourceVariantEffectAttribSql =
        """
        CREATE TABLE EnhancementSourceVariantEffectAttrib (
            catalog_item_id TEXT NOT NULL,
            variant_index INTEGER NOT NULL,
            effect_index INTEGER NOT NULL,
            attrib_index INTEGER NOT NULL,
            attrib_id INTEGER NOT NULL,
            PRIMARY KEY (catalog_item_id, variant_index, effect_index, attrib_index),
            FOREIGN KEY (catalog_item_id, variant_index, effect_index)
                REFERENCES EnhancementSourceVariantEffect(catalog_item_id, variant_index, effect_index)
        );
        """;

    public const string CreateEnhancementResolverNamedTableSql =
        """
        CREATE TABLE EnhancementResolverNamedTable (
            table_name TEXT NOT NULL PRIMARY KEY,
            value_count INTEGER NOT NULL
        );
        """;

    public const string CreateEnhancementResolverNamedTableValueSql =
        """
        CREATE TABLE EnhancementResolverNamedTableValue (
            table_name TEXT NOT NULL REFERENCES EnhancementResolverNamedTable(table_name),
            value_index INTEGER NOT NULL,
            value REAL NOT NULL,
            PRIMARY KEY (table_name, value_index)
        );
        """;

    public const string CreateItemAliasSql =
        """
        CREATE TABLE ItemAlias (
            lookup_key TEXT NOT NULL PRIMARY KEY,
            catalog_item_id TEXT NOT NULL REFERENCES Item(catalog_item_id),
            text TEXT NOT NULL,
            locale TEXT NOT NULL,
            name_kind TEXT NOT NULL,
            is_preferred INTEGER NOT NULL CHECK (is_preferred IN (0, 1))
        );
        """;

    public const string CreateItemAliasLookupIndexSql =
        "CREATE UNIQUE INDEX IF NOT EXISTS UX_ItemAlias_LookupKey ON ItemAlias(lookup_key);";

    public const string CreateRecipeLevelSql =
        """
        CREATE TABLE RecipeLevel (
            catalog_item_id TEXT NOT NULL REFERENCES Item(catalog_item_id),
            level INTEGER NOT NULL CHECK (level >= 1 AND level <= 50),
            homecoming_source_id TEXT NOT NULL,
            crafting_cost INTEGER NOT NULL CHECK (crafting_cost > 0),
            PRIMARY KEY (catalog_item_id, level)
        );
        """;

    public const string CreateRecipeLevelRequirementSql =
        """
        CREATE TABLE RecipeLevelRequirement (
            catalog_item_id TEXT NOT NULL,
            level INTEGER NOT NULL,
            salvage_catalog_item_id TEXT NOT NULL REFERENCES Item(catalog_item_id),
            quantity INTEGER NOT NULL CHECK (quantity > 0),
            PRIMARY KEY (catalog_item_id, level, salvage_catalog_item_id),
            FOREIGN KEY (catalog_item_id, level)
                REFERENCES RecipeLevel(catalog_item_id, level)
        );
        """;

    public const string CreateRecipeExcludedSourceLevelSql =
        """
        CREATE TABLE RecipeExcludedSourceLevel (
            catalog_item_id TEXT NOT NULL REFERENCES Item(catalog_item_id),
            level INTEGER NOT NULL CHECK (level > 50),
            homecoming_source_id TEXT NOT NULL,
            crafting_cost INTEGER NOT NULL CHECK (crafting_cost > 0),
            PRIMARY KEY (catalog_item_id, level)
        );
        """;

    public const string CreateReferenceServerAvailabilitySql =
        """
        CREATE TABLE ReferenceServerAvailability (
            catalog_item_id TEXT NOT NULL,
            entity_kind TEXT NOT NULL CHECK (entity_kind IN ('Item', 'EnhancementSet')),
            server_key TEXT NOT NULL,
            availability_status TEXT NOT NULL CHECK (availability_status IN ('Current', 'Historical')),
            PRIMARY KEY (catalog_item_id, server_key)
        );
        """;

    public const string CreateZoneSql =
        """
        CREATE TABLE Zone (
            zone_id TEXT PRIMARY KEY,
            display_name TEXT NOT NULL,
            alignment_notes TEXT,
            level_range TEXT,
            zone_type TEXT
        );
        """;

    public const string CreateZoneExplorationCompletionBadgeSql =
        """
        CREATE TABLE ZoneExplorationCompletionBadge (
            zone_id TEXT NOT NULL REFERENCES Zone(zone_id),
            badge_catalog_item_id TEXT NOT NULL,
            badge_index INTEGER NOT NULL,
            PRIMARY KEY (zone_id, badge_catalog_item_id),
            FOREIGN KEY (badge_catalog_item_id) REFERENCES Badge(catalog_item_id)
        );
        """;

    public const string CreateZoneHistoryCompletionBadgeSql =
        """
        CREATE TABLE ZoneHistoryCompletionBadge (
            zone_id TEXT NOT NULL REFERENCES Zone(zone_id),
            badge_catalog_item_id TEXT NOT NULL,
            badge_index INTEGER NOT NULL,
            PRIMARY KEY (zone_id, badge_catalog_item_id),
            FOREIGN KEY (badge_catalog_item_id) REFERENCES Badge(catalog_item_id)
        );
        """;

    public const string CreateBadgeSql =
        """
        CREATE TABLE Badge (
            catalog_item_id TEXT PRIMARY KEY REFERENCES Item(catalog_item_id),
            homecoming_source_id TEXT NOT NULL,
            set_title_id INTEGER,
            canonical_category TEXT NOT NULL,
            badge_type INTEGER NOT NULL,
            reference_kind TEXT NOT NULL,
            hero_name TEXT NOT NULL,
            villain_name TEXT NOT NULL,
            hero_description TEXT,
            villain_description TEXT,
            hero_icon TEXT,
            villain_icon TEXT,
            zone_id TEXT REFERENCES Zone(zone_id),
            completion_badge_id TEXT REFERENCES Badge(catalog_item_id),
            is_zone_completion_badge INTEGER NOT NULL CHECK (is_zone_completion_badge IN (0, 1)),
            verification_status TEXT NOT NULL,
            requirement_text TEXT,
            reward_text TEXT,
            requirement_logic_status TEXT,
            requirement_logic_pattern TEXT,
            CHECK (catalog_item_id GLOB 'BAD-[0-9][0-9][0-9][0-9][0-9]')
        );
        """;

    public const string CreateBadgeLocationSql =
        """
        CREATE TABLE BadgeLocation (
            badge_catalog_item_id TEXT NOT NULL REFERENCES Badge(catalog_item_id),
            location_index INTEGER NOT NULL,
            zone_id TEXT NOT NULL REFERENCES Zone(zone_id),
            coordinate_x REAL,
            coordinate_y REAL,
            coordinate_z REAL,
            thumbtack_command TEXT,
            marker_type TEXT,
            location_role TEXT,
            coordinate_semantics TEXT,
            verification_status TEXT NOT NULL,
            trigger_description TEXT,
            exploration_route_order INTEGER,
            route_source_project TEXT,
            route_source_version TEXT,
            route_source_url TEXT,
            route_mapping_confidence TEXT,
            PRIMARY KEY (badge_catalog_item_id, location_index)
        );
        """;

    public const string CreateRouteOrderingProvenanceSql =
        """
        CREATE TABLE RouteOrderingProvenance (
            source_name TEXT NOT NULL,
            author TEXT NOT NULL,
            version TEXT NOT NULL,
            maps_thread_url TEXT,
            popmenu_thread_url TEXT,
            ordering_semantics TEXT,
            retrieved TEXT
        );
        """;

    public const string CreateHistoryPlaqueRouteCollectionSql =
        """
        CREATE TABLE HistoryPlaqueRouteCollection (
            collection_name TEXT NOT NULL PRIMARY KEY,
            completion_badge_id TEXT NOT NULL REFERENCES Badge(catalog_item_id),
            published_collection_order_available INTEGER NOT NULL CHECK (published_collection_order_available IN (0, 1)),
            ordering_status TEXT NOT NULL,
            ordering_notes TEXT
        );
        """;

    public const string CreateHistoryPlaqueRouteStopSql =
        """
        CREATE TABLE HistoryPlaqueRouteStop (
            collection_name TEXT NOT NULL REFERENCES HistoryPlaqueRouteCollection(collection_name),
            inventory_order INTEGER NOT NULL,
            route_order INTEGER,
            completion_badge_id TEXT NOT NULL REFERENCES Badge(catalog_item_id),
            zone_id TEXT NOT NULL REFERENCES Zone(zone_id),
            location_index INTEGER NOT NULL,
            plaque_name TEXT,
            source_zone_route_order INTEGER,
            route_mapping_confidence TEXT,
            route_source_project TEXT,
            route_source_version TEXT,
            route_source_url TEXT,
            PRIMARY KEY (collection_name, inventory_order)
        );
        """;

    public const string CreateBadgeAccoladeRequirementSql =
        """
        CREATE TABLE BadgeAccoladeRequirement (
            accolade_badge_id TEXT NOT NULL REFERENCES Badge(catalog_item_id),
            prerequisite_badge_id TEXT NOT NULL REFERENCES Badge(catalog_item_id),
            prerequisite_index INTEGER NOT NULL,
            logic_group INTEGER NOT NULL DEFAULT 0,
            requirement_logic_status TEXT NOT NULL,
            PRIMARY KEY (accolade_badge_id, prerequisite_index)
        );
        """;

    public static IReadOnlyList<string> CreateTableStatements { get; } =
        [
            CreateCatalogManifestSql,
            CreateEnhancementSetSql,
            CreateEnhancementSetBonusSql,
            CreateEnhancementSetBonusRequiresTokenSql,
            CreateEnhancementSetBonusRequiredEnhancementSql,
            CreateEnhancementSetBonusPowerSql,
            CreateEnhancementSetBonusPowerEffectSql,
            CreateEnhancementSetBonusPowerEffectAttribSql,
            CreateItemSql,
            CreateEnhancementSourceVariantSql,
            CreateEnhancementSourceVariantEffectSql,
            CreateEnhancementSourceVariantEffectAttribSql,
            CreateEnhancementResolverNamedTableSql,
            CreateEnhancementResolverNamedTableValueSql,
            CreateItemAliasSql,
            CreateItemAliasLookupIndexSql,
            CreateRecipeLevelSql,
            CreateRecipeLevelRequirementSql,
            CreateRecipeExcludedSourceLevelSql,
            CreateReferenceServerAvailabilitySql,
            CreateZoneSql,
            CreateBadgeSql,
            CreateZoneExplorationCompletionBadgeSql,
            CreateZoneHistoryCompletionBadgeSql,
            CreateBadgeLocationSql,
            CreateBadgeAccoladeRequirementSql,
            CreateRouteOrderingProvenanceSql,
            CreateHistoryPlaqueRouteCollectionSql,
            CreateHistoryPlaqueRouteStopSql
        ];
}
