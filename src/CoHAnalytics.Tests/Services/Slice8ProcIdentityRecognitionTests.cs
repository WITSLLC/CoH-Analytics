using CoHAnalytics.Models;
using CoHAnalytics.ReferenceData;
using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Services;

/// <summary>
/// Slice 8 proc telemetry identity recognition. Identities come from catalogued damage-proc
/// effect facts; parent powers come only from frozen build slot occurrences.
/// </summary>
public sealed class Slice8ProcIdentityRecognitionTests
{
    private const string HotFeetPowerId = "Controller_Control.Fire_Control.Hot_Feet";

    /// <summary>Damage procs slotted once each under Hot Feet in the real captured build.</summary>
    public static TheoryData<string, string> HotFeetDamageProcs() => new()
    {
        { "Armageddon: Chance for Fire Damage", "ENH-01287" },
        { "Eradication: Chance for Energy Damage", "ENH-00094" },
        { "Scirocco's Dervish: Chance for Lethal Damage", "ENH-00793" },
        { "Obliteration: Chance for Smashing Damage", "ENH-00111" }
    };

    private static readonly Lazy<IItemReferenceCatalog> Catalog =
        new(ItemReferenceCatalogFactory.LoadEmbeddedProduction);

    private static IItemReferenceCatalog ProductionCatalog
    {
        get
        {
            Assert.True(Catalog.Value.IsLoaded, Catalog.Value.LoadFailureReason);
            return Catalog.Value;
        }
    }

    [Theory]
    [MemberData(nameof(HotFeetDamageProcs))]
    public void Catalogued_damage_proc_resolves_to_its_exact_identity(string logName, string catalogItemId)
    {
        var index = ProcLogNameIndex.FromCatalog(ProductionCatalog);
        Assert.True(index.IsExactProcIdentity(logName));
        Assert.False(index.IsAmbiguous(logName));
        Assert.Equal(catalogItemId, Assert.Single(index.Identities, i => i.LogName == logName).CatalogItemId);
    }

    [Theory]
    [MemberData(nameof(HotFeetDamageProcs))]
    public void Real_build_yields_one_frozen_slot_per_recognized_damage_proc(string logName, string catalogItemId)
    {
        var manifest = RealBuildManifest();
        var slot = Assert.Single(manifest.ProcSlots, s => s.ExactProcIdentity == logName);
        Assert.Equal(HotFeetPowerId, slot.SlottedInPowerId);
        Assert.Equal(catalogItemId, slot.CanonicalEnhancementId);
        Assert.Equal(ProcMappingStatus.Resolved, slot.MappingStatus);
    }

    [Theory]
    [MemberData(nameof(HotFeetDamageProcs))]
    public void Real_build_attributes_each_damage_proc_to_its_frozen_parent(string logName, string catalogItemId)
    {
        var result = ProcAttributionClassifier.Classify(
            logName, isOwnedPet: false, RealBuildManifest(), ProcLogNameIndex.Empty);
        Assert.Equal(ProcAttributionMode.BuildConfirmed, result.Mode);
        Assert.Equal(ProcAttributionPath.ProcParent, result.Path);
        Assert.Equal(HotFeetPowerId, result.ParentPowerId);
        Assert.Equal("Hot Feet", result.ParentPowerName);
        Assert.Equal(logName, result.ExactProcIdentity);
        Assert.Equal(MetricEvidence.DerivedFromObserved, result.Evidence);
        Assert.NotEqual(string.Empty, catalogItemId);
    }

    [Fact]
    public void Fury_of_the_Gladiator_is_slotted_but_is_not_a_damage_proc_identity()
    {
        var fury = ProductionCatalog.GetEnhancements().Single(i => i.CatalogItemId == "ENH-00794");
        Assert.Equal("Fury of the Gladiator: Chance for -Res", fury.CurrentDisplayName);
        var index = ProcLogNameIndex.FromCatalog(ProductionCatalog);
        Assert.False(index.IsExactProcIdentity(fury.CurrentDisplayName));

        // Present in the build, yet it never becomes a damage attribution without damage telemetry.
        var manifest = RealBuildManifest();
        Assert.Contains(manifest.ProcSlots, slot => slot.CanonicalEnhancementId == fury.CatalogItemId);
        Assert.DoesNotContain(manifest.ProcSlots, slot => slot.ExactProcIdentity == fury.CurrentDisplayName);
        var result = ProcAttributionClassifier.Classify(
            fury.CurrentDisplayName, isOwnedPet: false, manifest, ProcLogNameIndex.Empty);
        Assert.Equal(ProcAttributionMode.Unattributed, result.Mode);
        Assert.Null(result.ParentPowerId);
    }

    [Theory]
    [MemberData(nameof(HotFeetDamageProcs))]
    public void Same_proc_in_two_powers_stays_ambiguous(string logName, string catalogItemId)
    {
        var token = ProductionCatalog.GetEnhancements()
            .Single(i => i.CatalogItemId == catalogItemId)
            .SourceVariants.Select(v => TokenOf(v.HomecomingSourceId))
            .First(t => t.StartsWith("Crafted_", StringComparison.Ordinal));
        var manifest = ManifestFromLayout($"""
            Level 10: Controller_Control Fire_Control Hot_Feet
                {token} (50)
            Level 8: Controller_Control Fire_Control Fire_Cages
                {token} (50)
            """);
        Assert.Equal(2, manifest.ProcSlots.Count(slot => slot.ExactProcIdentity == logName));
        var result = ProcAttributionClassifier.Classify(
            logName, isOwnedPet: false, manifest, ProcLogNameIndex.Empty);
        Assert.Equal(ProcAttributionMode.Unattributed, result.Mode);
        Assert.Equal(2, result.Candidates.Count);
        Assert.Null(result.ParentPowerId);
    }

    [Theory]
    [MemberData(nameof(HotFeetDamageProcs))]
    public void No_matching_frozen_slot_stays_unattributed(string logName, string catalogItemId)
    {
        var manifest = ManifestFromLayout("""
            Level 10: Controller_Control Fire_Control Hot_Feet
                Crafted_Accuracy (50+5)
            """);
        var result = ProcAttributionClassifier.Classify(
            logName, isOwnedPet: false, manifest, ProcLogNameIndex.Empty);
        Assert.Equal(ProcAttributionMode.Unattributed, result.Mode);
        Assert.Empty(result.Candidates);
        Assert.NotEqual(string.Empty, catalogItemId);
    }

    [Theory]
    [MemberData(nameof(HotFeetDamageProcs))]
    public void Missing_build_context_stays_unattributed(string logName, string catalogItemId)
    {
        var result = ProcAttributionClassifier.Classify(
            logName, isOwnedPet: false, manifest: null, ProcLogNameIndex.FromCatalog(ProductionCatalog));
        Assert.Equal(ProcAttributionMode.Unattributed, result.Mode);
        Assert.Equal(logName, result.ExactProcIdentity);
        Assert.Null(result.ParentPowerId);
        Assert.NotEqual(string.Empty, catalogItemId);
    }

    [Theory]
    [MemberData(nameof(HotFeetDamageProcs))]
    public void Pet_events_do_not_consume_player_frozen_slots(string logName, string catalogItemId)
    {
        var result = ProcAttributionClassifier.Classify(
            logName, isOwnedPet: true, RealBuildManifest(), ProcLogNameIndex.Empty);
        Assert.Equal(ProcAttributionMode.Unattributed, result.Mode);
        Assert.Equal(ProcAttributionPath.OwnedPet, result.Path);
        Assert.Null(result.ParentPowerId);
        Assert.NotEqual(string.Empty, catalogItemId);
    }

    [Fact]
    public void Unrelated_enhancement_and_power_display_names_do_not_match()
    {
        var index = ProcLogNameIndex.FromCatalog(ProductionCatalog);
        foreach (var name in new[]
                 {
                     "Invention: Accuracy", "Invention: Damage Increase", "Hot Feet", "Fire Cages",
                     "Fire Ball", "Reactive Interface", "Armageddon", "Chance for Fire Damage",
                     "armageddon: chance for fire damage", "Armageddon: Chance for Fire Damage "
                 })
        {
            Assert.False(index.IsExactProcIdentity(name), name);
        }

        // Every recognized identity is an Enhancement display name carrying proc-damage effects.
        var byName = ProductionCatalog.GetEnhancements()
            .ToDictionary(i => i.CatalogItemId, StringComparer.Ordinal);
        Assert.All(index.Identities, identity =>
        {
            var item = byName[identity.CatalogItemId];
            Assert.Equal(item.CurrentDisplayName, identity.LogName);
            Assert.Contains(item.SourceVariants.SelectMany(v => v.Effects), e => e.Table == "Melee_ProcDamage");
        });
    }

    [Fact]
    public void Identity_recognition_is_generic_rather_than_a_single_hardcoded_proc()
    {
        var index = ProcLogNameIndex.FromCatalog(ProductionCatalog);
        Assert.True(index.CanIdentifyProcs);
        Assert.True(index.Identities.Count > 1);
        Assert.All(HotFeetDamageProcs().Select(row => (string)row[0]),
            name => Assert.True(index.IsExactProcIdentity(name), name));

        // A record stripped of its proc-damage effects loses its identity, proving the rule is
        // driven by effect facts rather than by any particular enhancement or set name.
        var armageddon = ProductionCatalog.GetEnhancements().Single(i => i.CatalogItemId == "ENH-01287");
        var stripped = armageddon with
        {
            SourceVariants = armageddon.SourceVariants
                .Select(v => v with { Effects = Array.Empty<EnhancementSourceVariantEffectReferenceRecord>() })
                .ToArray()
        };
        Assert.False(ProcLogNameIndex.FromItems([stripped]).IsExactProcIdentity(armageddon.CurrentDisplayName));
        Assert.True(ProcLogNameIndex.FromItems([armageddon]).IsExactProcIdentity(armageddon.CurrentDisplayName));
    }

    [Fact]
    public void Identity_index_is_deterministic_and_order_independent()
    {
        var items = ProductionCatalog.GetEnhancements().ToArray();
        var forward = ProcLogNameIndex.FromItems(items).Identities;
        var reversed = ProcLogNameIndex.FromItems(items.Reverse().ToArray()).Identities;
        Assert.Equal(forward, reversed);
        Assert.Equal(forward, ProcLogNameIndex.FromItems(items).Identities);
        Assert.Equal(
            forward.OrderBy(i => i.LogName, StringComparer.Ordinal)
                .ThenBy(i => i.CatalogItemId, StringComparer.Ordinal),
            forward);
        Assert.Equal(forward.Select(i => i.LogName).Distinct(StringComparer.Ordinal).Count(), forward.Count);
    }

    [Fact]
    public void Duplicate_catalog_identity_remains_ambiguous_and_never_confirms_a_parent()
    {
        var item = ProductionCatalog.GetEnhancements().Single(i => i.CatalogItemId == "ENH-00094");
        var index = ProcLogNameIndex.FromItems([item, item with { CatalogItemId = "ENH-CLONE" }]);
        Assert.True(index.IsExactProcIdentity(item.CurrentDisplayName));
        Assert.True(index.IsAmbiguous(item.CurrentDisplayName));
        var result = ProcAttributionClassifier.Classify(
            item.CurrentDisplayName, isOwnedPet: false, manifest: null, index);
        Assert.Equal(ProcAttributionMode.Unattributed, result.Mode);
        Assert.Null(result.ExactProcIdentity);
    }

    [Fact]
    public void Policy_version_is_two_and_analytics_semantic_version_is_unchanged()
    {
        Assert.Equal(2, AttributionPolicyVersion.Current);
        Assert.Equal(3, AnalyticsSemanticVersion.Current);
        Assert.Equal(2, RealBuildManifest().AttributionPolicyVersion);
    }

    private static string TokenOf(string homecomingSourceId)
    {
        var separator = homecomingSourceId.LastIndexOf('.');
        return separator < 0 ? homecomingSourceId : homecomingSourceId[(separator + 1)..];
    }

    private static FrozenBuildManifest RealBuildManifest() =>
        ManifestFromLayout(File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "Services", "Fixtures", "hells-vengence-build-layout.txt")));

    private static FrozenBuildManifest ManifestFromLayout(string layoutText)
    {
        Assert.True(HomecomingBuildLayoutParser.TryParse(layoutText, out var layout));
        return FrozenBuildManifestFactory.Create(
            new CharacterBuildSnapshot
            {
                CharacterRecordId = CharacterRecordId.CreateNew(),
                SyncedAtUtc = DateTimeOffset.UnixEpoch,
                Layout = layout
            },
            EnhancementTokenResolver.FromCatalog(ProductionCatalog),
            ProductionCatalog.Manifest!.CatalogVersion);
    }
}
