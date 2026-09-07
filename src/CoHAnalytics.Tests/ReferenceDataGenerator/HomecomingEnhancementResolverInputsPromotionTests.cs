using System.Text.Json;
using CoHAnalytics.ReferenceData;
using CoHAnalytics.ReferenceDataGenerator;
using CoHAnalytics.Tests.ReferenceData;

namespace CoHAnalytics.Tests.ReferenceDataGenerator;

[Collection(LiveInstallPromotionCollection.Name)]
public sealed class HomecomingEnhancementResolverInputsPromotionTests
{
    private readonly LiveInstallPromotionFixture _promotion;

    public HomecomingEnhancementResolverInputsPromotionTests(LiveInstallPromotionFixture promotion)
    {
        _promotion = promotion;
    }

    private static string LiveInstallRoot => LiveInstallTestEnvironment.InstallRoot;

    private static EnhancementSourceVariantReferenceRecord RequireVariant(
        ItemReferenceRecord item,
        string homecomingSourceId) =>
        item.SourceVariants.First(variant =>
            string.Equals(variant.HomecomingSourceId, homecomingSourceId, StringComparison.Ordinal));

    [Fact]
    public void Production_catalog_A_Crafted_Accuracy_exposes_resolver_facts()
    {
        var catalog = (ItemReferenceCatalog)ItemReferenceCatalogFactory.LoadEmbeddedProduction();
        Assert.True(catalog.TryResolve("Invention: Accuracy", out var resolution));
        var variant = RequireVariant(
            resolution.Item,
            "Boosts.Crafted_Accuracy.Crafted_Accuracy");
        Assert.False(variant.BoostUsePlayerLevel);
        Assert.Equal(50, variant.MaxBoostLevel);
        Assert.True(variant.BoostBoostable);

        var effect = Assert.Single(variant.Effects);
        Assert.Equal("Accuracy", effect.Tag);
        Assert.Equal("Melee_Boosts_33", effect.Table);
        Assert.Equal(1.0f, effect.Scale, 5);
    }

    [Fact]
    public void Production_catalog_B_dual_aspect_set_piece_retains_damage_and_accuracy()
    {
        var catalog = (ItemReferenceCatalog)ItemReferenceCatalogFactory.LoadEmbeddedProduction();
        Assert.True(catalog.TryResolve("Bonesnap: Accuracy/Damage", out var resolution));
        var variant = resolution.Item.SourceVariants
            .First(value => string.Equals(value.SourceForm, "Crafted", StringComparison.Ordinal));
        var tags = variant.Effects.Select(effect => effect.Tag).OrderBy(value => value, StringComparer.Ordinal).ToArray();
        Assert.Equal(["Accuracy", "Damage"], tags);
        Assert.All(variant.Effects, effect => Assert.Equal("Melee_Boosts_33", effect.Table));
        Assert.All(variant.Effects, effect => Assert.Equal(0.625f, effect.Scale, 5));
    }

    [Fact]
    public void Production_catalog_C_attuned_variant_uses_player_level_flags()
    {
        var catalog = (ItemReferenceCatalog)ItemReferenceCatalogFactory.LoadEmbeddedProduction();
        Assert.True(catalog.TryResolve("Bonesnap: Accuracy/Damage", out var resolution));
        var variant = resolution.Item.SourceVariants
            .First(value => string.Equals(value.SourceForm, "Attuned", StringComparison.Ordinal));
        Assert.True(variant.BoostUsePlayerLevel);
        Assert.Equal(25, variant.MaxBoostLevel);
        Assert.False(variant.BoostBoostable);
    }

    [Fact]
    public void Production_catalog_D_superior_attuned_preserves_table_and_scale()
    {
        var catalog = (ItemReferenceCatalog)ItemReferenceCatalogFactory.LoadEmbeddedProduction();
        Assert.True(catalog.TryResolve("Absolute Amazement: Stun Duration", out var resolution));
        var variant = RequireVariant(
            resolution.Item,
            "Boosts.Superior_Attuned_Absolute_Amazement_A.Superior_Attuned_Absolute_Amazement_A");
        Assert.True(variant.BoostUsePlayerLevel);
        var effect = Assert.Single(variant.Effects);
        Assert.Equal("Mez", effect.Tag);
        Assert.Equal("Melee_Boosts_33", effect.Table);
        Assert.Equal(1.25f, effect.Scale, 5);
    }

    [Fact]
    public void Production_catalog_E_ones_family_uses_melee_ones_table()
    {
        var catalog = (ItemReferenceCatalog)ItemReferenceCatalogFactory.LoadEmbeddedProduction();
        Assert.True(catalog.TryResolve("Training: Accuracy", out var training));
        Assert.Equal(
            "Melee_Ones",
            Assert.Single(RequireVariant(training.Item, "Boosts.Generic_Accuracy.Generic_Accuracy").Effects).Table);

        Assert.True(catalog.TryResolve("Invention: Accuracy", out var crafted));
        Assert.Equal(
            "Melee_Boosts_33",
            RequireVariant(crafted.Item, "Boosts.Crafted_Accuracy.Crafted_Accuracy").Effects[0].Table);
    }

    [Fact]
    public void Production_catalog_F_regen_and_regeneration_tags_remain_distinct()
    {
        var catalog = (ItemReferenceCatalog)ItemReferenceCatalogFactory.LoadEmbeddedProduction();
        Assert.True(catalog.TryResolve("Numina's Convalescence: Regeneration/Recovery", out var numina));
        var tags = RequireVariant(
                numina.Item,
                "Boosts.Crafted_Numinas_Convalesence_F.Crafted_Numinas_Convalesence_F")
            .Effects
            .Select(effect => effect.Tag)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(["Endurance", "Regen"], tags);
        Assert.DoesNotContain("Regeneration", tags);
    }

    [Fact]
    public void Production_catalog_G_token_coverage_is_case_insensitive_with_canonical_tags()
    {
        var catalog = (ItemReferenceCatalog)ItemReferenceCatalogFactory.LoadEmbeddedProduction();
        Assert.True(catalog.TryResolve("Invention: Recharge Reduction", out var recharge));
        var variant = RequireVariant(recharge.Item, "Boosts.Crafted_Recharge.Crafted_Recharge");
        Assert.Contains("{Boost.Attrib.RechargeTime.Scale}", variant.DisplayHelp!, StringComparison.Ordinal);
        var effect = Assert.Single(variant.Effects);
        Assert.Equal("rechargetime", effect.Tag);
    }

    [Fact]
    public void PromoteNamedTables_H_identical_cross_class_tables_are_accepted()
    {
        var values = Enumerable.Range(0, 105).Select(index => index * 0.01f).ToArray();
        var tables = new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<float>>>(StringComparer.Ordinal)
        {
            ["Class_Blaster"] = new Dictionary<string, IReadOnlyList<float>>(StringComparer.Ordinal)
            {
                ["Melee_Boosts_33"] = values
            },
            ["Class_Scrapper"] = new Dictionary<string, IReadOnlyList<float>>(StringComparer.Ordinal)
            {
                ["Melee_Boosts_33"] = values
            }
        };

        var promoted = HomecomingEnhancementResolverPromotionSupport.PromoteNamedTables(
            [],
            new HomecomingClassModTableDiscoveryResult(tables),
            ["Melee_Boosts_33"]);

        Assert.Equal(values.Length, promoted["Melee_Boosts_33"].Count);
    }

    [Fact]
    public void PromoteNamedTables_H_conflicting_cross_class_tables_fail()
    {
        var tables = new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<float>>>(StringComparer.Ordinal)
        {
            ["Class_Blaster"] = new Dictionary<string, IReadOnlyList<float>>(StringComparer.Ordinal)
            {
                ["Melee_Boosts_33"] = Enumerable.Repeat(1.0f, 105).ToArray()
            },
            ["Class_Scrapper"] = new Dictionary<string, IReadOnlyList<float>>(StringComparer.Ordinal)
            {
                ["Melee_Boosts_33"] = Enumerable.Repeat(2.0f, 105).ToArray()
            }
        };

        var exception = Assert.Throws<HomecomingEnhancementPromotionException>(() =>
            HomecomingEnhancementResolverPromotionSupport.PromoteNamedTables(
                [],
                new HomecomingClassModTableDiscoveryResult(tables),
                ["Melee_Boosts_33"]));

        Assert.Contains("disagrees across player classes", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "LiveInstall")]
    public void PromoteNamedTables_I_missing_referenced_table_fails()
    {
        var source = HomecomingStaticDataSourceDiscovery.Discover(LiveInstallRoot);
        var classesBytes = HomecomingPiggMemberReader.ReadMember(source.BinPiggPath, "bin/classes.bin");
        var classTables = HomecomingClassModTableDiscoveryReader.Read(classesBytes);

        var exception = Assert.Throws<HomecomingClassModTableDiscoveryException>(() =>
            HomecomingEnhancementResolverPromotionSupport.PromoteNamedTables(
                classesBytes,
                classTables,
                ["Not_A_Real_NamedTable"]));

        Assert.Contains("Not_A_Real_NamedTable", exception.Message, StringComparison.Ordinal);
    }

    [Collection(nameof(ReferenceDatabaseCollection))]
    public sealed class SqliteParity
    {
        private readonly ReferenceDatabaseFixture _fixture;

        public SqliteParity(ReferenceDatabaseFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public void Production_catalog_J_json_and_sqlite_resolver_facts_match()
        {
            using var stream = typeof(ItemReferenceCatalogFactory).Assembly
                .GetManifestResourceStream(ItemReferenceCatalogFactory.ProductionCatalogResourceName);
            Assert.NotNull(stream);
            var json = ItemReferenceCatalogLoader.Load(stream);
            Assert.True(json.Succeeded);

            var sqlite = SqliteReferenceCatalogLoader.Load(_fixture.DatabasePath);
            Assert.True(sqlite.Succeeded);

            Assert.Equal(
                json.EnhancementResolverNamedTables!.Count,
                sqlite.EnhancementResolverNamedTables!.Count);
            foreach (var (name, jsonValues) in json.EnhancementResolverNamedTables!)
            {
                Assert.True(sqlite.EnhancementResolverNamedTables.TryGetValue(name, out var sqliteValues));
                Assert.Equal(jsonValues.Count, sqliteValues.Count);
                for (var index = 0; index < jsonValues.Count; index++)
                {
                    Assert.Equal(jsonValues[index], sqliteValues[index], 6);
                }
            }

            var jsonAccuracy = json.Items!["ENH-00001"].SourceVariants![0];
            Assert.True(sqlite.Items!.TryGetValue("ENH-00001", out var sqliteAccuracy));
            var sqliteVariant = sqliteAccuracy.SourceVariants[0];
            Assert.Equal(jsonAccuracy.BoostUsePlayerLevel, sqliteVariant.BoostUsePlayerLevel);
            Assert.Equal(jsonAccuracy.MaxBoostLevel, sqliteVariant.MaxBoostLevel);
            Assert.Equal(jsonAccuracy.BoostBoostable, sqliteVariant.BoostBoostable);
            Assert.Equal(jsonAccuracy.Effects!.Count, sqliteVariant.Effects.Count);
            Assert.Equal(jsonAccuracy.Effects[0].Tag, sqliteVariant.Effects[0].Tag);
            Assert.Equal(jsonAccuracy.Effects[0].Table, sqliteVariant.Effects[0].Table);
            Assert.Equal(jsonAccuracy.Effects[0].Scale, sqliteVariant.Effects[0].Scale, 6);
        }
    }

    [Fact]
    public void Production_catalog_K_historical_variants_do_not_fabricate_resolver_facts()
    {
        var empty = HomecomingEnhancementResolverPromotionSupport.CreateEmptyResolverVariantDocument(
            new EnhancementSourceVariantReferenceRecordDocument
            {
                HomecomingSourceId = "Boosts.Crafted_Historical_Example.Crafted_Historical_Example",
                SourceForm = "Crafted",
                DisplayHelp = "Historical help {Boost.Attrib.Accuracy.Scale}%.",
                ShortHelp = "Historical",
            });

        Assert.Equal("Boosts.Crafted_Historical_Example.Crafted_Historical_Example", empty.HomecomingSourceId);
        Assert.Equal("Historical help {Boost.Attrib.Accuracy.Scale}%.", empty.DisplayHelp);
        Assert.False(empty.BoostUsePlayerLevel);
        Assert.Equal(0, empty.MaxBoostLevel);
        Assert.False(empty.BoostBoostable);
        Assert.Empty(empty.Effects);
    }

    [Fact]
    [Trait("Category", "LiveInstall")]
    public void LiveInstall_Crafted_and_Attuned_level_flags_match_research()
    {
        var source = HomecomingStaticDataSourceDiscovery.Discover(LiveInstallRoot);
        var powers = HomecomingPiggMemberReader.ReadMember(
            source.BinPowersPiggPath,
            HomecomingEnhancementCandidateGenerator.PowersMember);

        var crafted = HomecomingBoostResolverFactsDiscoveryReader.ReadForSourceId(
            powers,
            "Boosts.Crafted_Accuracy.Crafted_Accuracy");
        Assert.False(crafted.LevelFlags.BoostUsePlayerLevel);
        Assert.Equal(50, crafted.LevelFlags.MaxBoostLevel);
        Assert.True(crafted.LevelFlags.BoostBoostable);

        var attuned = HomecomingBoostResolverFactsDiscoveryReader.ReadForSourceId(
            powers,
            "Boosts.Attuned_Bonesnap_A.Attuned_Bonesnap_A");
        Assert.True(
            HomecomingEnhancementResolverPromotionSupport.ResolveBoostUsePlayerLevel(
                "Boosts.Attuned_Bonesnap_A.Attuned_Bonesnap_A",
                attuned.LevelFlags.BoostUsePlayerLevel));
        Assert.Equal(25, attuned.LevelFlags.MaxBoostLevel);
        Assert.False(attuned.LevelFlags.BoostBoostable);
    }

    [Fact]
    [Trait("Category", "LiveInstall")]
    public void LiveInstall_promotion_covers_all_current_scale_tokens()
    {
        var catalogPath = Path.Combine(Path.GetTempPath(), $"coh-i1-{Guid.NewGuid():N}.json");
        try
        {
            using (var embedded = typeof(ItemReferenceCatalogFactory).Assembly.GetManifestResourceStream(
                       ItemReferenceCatalogFactory.ProductionCatalogResourceName)
                   ?? throw new InvalidOperationException("Embedded production catalog missing."))
            using (var output = File.Create(catalogPath))
            {
                embedded.CopyTo(output);
            }

            using var startingDocument = JsonDocument.Parse(File.ReadAllBytes(catalogPath));
            var startingManifest = startingDocument.RootElement.GetProperty("manifest").Clone();
            Assert.Equal("item-ref-3.1.0", startingManifest.GetProperty("catalogVersion").GetString());

            var result = _promotion.Promote(catalogPath);
            Assert.Equal(result.Stats.TokenBearingVariants, result.Stats.TokenBearingVariantsCovered);
            Assert.True(result.Stats.PromotedNamedTables > 0);

            using var document = JsonDocument.Parse(File.ReadAllBytes(catalogPath));
            var promotedManifest = document.RootElement.GetProperty("manifest");
            Assert.Equal(startingManifest.GetRawText(), promotedManifest.GetRawText());
            Assert.True(document.RootElement.TryGetProperty("enhancementResolverNamedTables", out var tables));
            Assert.True(tables.GetArrayLength() > 0);
        }
        finally
        {
            if (File.Exists(catalogPath))
            {
                File.Delete(catalogPath);
            }
        }
    }
}
