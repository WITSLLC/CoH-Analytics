using System.Collections;
using CoHAnalytics.Models;
using CoHAnalytics.ReferenceData;
using CoHAnalytics.Services;
using CoHAnalytics.Tests.Orchestration;

namespace CoHAnalytics.Tests.Services;

/// <summary>Slice 8 frozen build manifest and conservative four-mode proc attribution.</summary>
public sealed class Slice8FrozenBuildManifestTests
{
    private const string HotFeet =
        "2026-08-04 12:00:00 You hit Lusca with your Hot Feet for 13.88 points of Fire damage.";

    private const string Armageddon =
        "2026-09-12 05:38:44 You hit Dismantler with your Armageddon: Chance for Fire Damage for 19.71 points of Fire damage.";

    private const string PetBrawl =
        "2026-09-12 05:38:47 Imp:  You hit Cleaner with your Brawl for 15.61 points of Fire damage over time.";

    private const string ReactiveInterface =
        "2026-09-12 05:38:44 You hit Dismantler with your Reactive Interface for 10.00 points of Fire damage.";

    private const string FireCagesTick =
        "2026-09-12 05:36:30 You hit Cleaner with your Fire Cages for 2.57 points of Fire damage over time.";

    private static readonly Lazy<IItemReferenceCatalog> Catalog = new(ItemReferenceCatalogFactory.LoadEmbeddedProduction);

    private static IItemReferenceCatalog ProductionCatalog
    {
        get
        {
            Assert.True(Catalog.Value.IsLoaded, Catalog.Value.LoadFailureReason);
            return Catalog.Value;
        }
    }

    [Fact]
    public void Frozen_manifest_lists_are_immutable()
    {
        var manifest = ManifestFromLayout("""
            Level 10: Controller_Control Fire_Control Hot_Feet
                Crafted_Armageddon_F (50)
            """);
        Assert.Throws<NotSupportedException>(() => ((IList)manifest.Powers).Add(null!));
        Assert.Throws<NotSupportedException>(() => ((IList)manifest.ProcSlots).Add(null!));
        var mutated = manifest with { ManifestHash = "other" };
        Assert.NotEqual(manifest.ManifestHash, mutated.ManifestHash);
        Assert.Equal(FrozenBuildManifestHash.Compute(manifest), manifest.ManifestHash);
    }

    [Fact]
    public void Same_effective_content_produces_deterministic_fingerprint()
    {
        var first = ManifestFromLayout(SingleArmageddonLayout(), syncedAt: DateTimeOffset.UnixEpoch);
        var second = ManifestFromLayout(SingleArmageddonLayout(), syncedAt: DateTimeOffset.UtcNow);
        Assert.Equal(first.ManifestHash, second.ManifestHash);
        Assert.Equal(FrozenBuildManifestHash.Compute(first), first.ManifestHash);
        Assert.Equal(64, first.ManifestHash.Length);
        Assert.Equal(first.ManifestHash, first.ManifestHash.ToLowerInvariant());
    }

    [Fact]
    public void Relevant_slot_change_changes_fingerprint()
    {
        var original = ManifestFromLayout(SingleArmageddonLayout());
        var changed = ManifestFromLayout("""
            Level 10: Controller_Control Fire_Control Hot_Feet
                Crafted_Hecatomb_F (50)
            """);
        Assert.NotEqual(original.ManifestHash, changed.ManifestHash);
    }

    [Fact]
    public void Display_only_and_sync_metadata_do_not_change_fingerprint()
    {
        Assert.True(HomecomingBuildLayoutParser.TryParse(SingleArmageddonLayout(), out var layout));
        var record = CharacterRecordId.CreateNew();
        var resolver = EnhancementTokenResolver.FromCatalog(ProductionCatalog);
        var fingerprint = ProductionCatalog.Manifest!.CatalogVersion;
        var left = FrozenBuildManifestFactory.Create(
            new CharacterBuildSnapshot
            {
                CharacterRecordId = record,
                CharacterName = "Alpha",
                CharacterShortId = "A1",
                SyncedAtUtc = DateTimeOffset.UnixEpoch,
                SourceBuildFile = @"C:\builds\alpha.txt",
                SourceLastWriteUtc = DateTimeOffset.UnixEpoch,
                Layout = layout
            },
            resolver,
            fingerprint);
        var right = FrozenBuildManifestFactory.Create(
            new CharacterBuildSnapshot
            {
                CharacterRecordId = CharacterRecordId.CreateNew(),
                CharacterName = "Beta",
                CharacterShortId = "B2",
                SyncedAtUtc = DateTimeOffset.UtcNow,
                SourceBuildFile = @"D:\other\beta.txt",
                SourceLastWriteUtc = DateTimeOffset.UtcNow,
                Layout = layout
            },
            resolver,
            fingerprint);
        Assert.Equal(left.ManifestHash, right.ManifestHash);
        Assert.Equal("Armageddon: Chance for Fire Damage", Assert.Single(left.ProcSlots).ExactProcIdentity);
        Assert.Equal("ENH-01287", Assert.Single(left.ProcSlots).CanonicalEnhancementId);
    }

    [Fact]
    public void Enhancement_level_text_is_not_part_of_manifest_identity()
    {
        var level50 = ManifestFromLayout("""
            Level 10: Controller_Control Fire_Control Hot_Feet
                Crafted_Armageddon_F (50)
            """);
        var level40 = ManifestFromLayout("""
            Level 10: Controller_Control Fire_Control Hot_Feet
                Crafted_Armageddon_F (40)
            """);
        Assert.Equal(level50.ManifestHash, level40.ManifestHash);
    }

    [Fact]
    public void Direct_attribution_uses_the_logged_attack_name()
    {
        var projection = Project(catalog: ProductionCatalog, HotFeet);
        Assert.Equal(1, projection.Attribution.DirectCount);
        Assert.Equal(0, projection.Attribution.BuildConfirmedCount);
        Assert.Equal(0, projection.Attribution.CorrelatedCount);
        Assert.Equal(new CombatScaledAmount(1388), projection.Attribution.DirectDamage);
        Assert.Equal(MetricAvailability.NotCaptured, projection.Attribution.ProcDamage.Availability);
        Assert.Null(projection.Attribution.ProcDamage.Value);
        Assert.Equal(MetricEvidence.None, projection.Attribution.ProcDamage.Evidence);
        Assert.Empty(projection.Attribution.ByParent);
    }

    [Fact]
    public void BuildConfirmed_requires_exactly_one_frozen_slot_occurrence()
    {
        var manifest = ManifestFromLayout(SingleArmageddonLayout());
        var projection = Project(ProductionCatalog, manifest, Armageddon);
        var row = Assert.Single(projection.Attribution.ByParent);
        Assert.Equal(ProcAttributionMode.BuildConfirmed, row.Mode);
        Assert.Equal("Controller_Control.Fire_Control.Hot_Feet", row.ParentPowerId);
        Assert.Equal("Hot Feet", row.ParentPowerName);
        Assert.Equal("Armageddon: Chance for Fire Damage", row.ExactProcIdentity);
        Assert.Equal(MetricEvidence.DerivedFromObserved, row.Evidence);
        Assert.Equal(MetricConfidence.High, row.Confidence);
        Assert.Equal(1, projection.Attribution.BuildConfirmedCount);
        Assert.Equal(new CombatScaledAmount(1971), projection.Attribution.BuildConfirmedProcDamage);
        Assert.Equal(new CombatScaledAmount(1971), projection.Attribution.ProcDamage.Value);
        Assert.Equal(MetricAvailability.Available, row.ProcDamageMetric.Availability);
        Assert.Equal(manifest.ManifestHash, projection.BuildContext.ManifestHash);
    }

    [Fact]
    public void Fixture_layout_maps_Crafted_Armageddon_F_once_under_Hot_Feet()
    {
        var layoutText = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "Services", "Fixtures", "hells-vengence-build-layout.txt"));
        var manifest = ManifestFromLayout(layoutText);
        var matching = manifest.ProcSlots
            .Where(slot => slot.ExactProcIdentity == "Armageddon: Chance for Fire Damage")
            .ToArray();
        var slot = Assert.Single(matching);
        Assert.Equal("Controller_Control.Fire_Control.Hot_Feet", slot.SlottedInPowerId);
        Assert.Equal("ENH-01287", slot.CanonicalEnhancementId);
        var projection = Project(ProductionCatalog, manifest, Armageddon);
        Assert.Equal(ProcAttributionMode.BuildConfirmed, Assert.Single(projection.Attribution.ByParent).Mode);
    }

    [Fact]
    public void Duplicate_slot_occurrences_are_Unattributed_not_BuildConfirmed()
    {
        var manifest = ManifestFromLayout("""
            Level 10: Controller_Control Fire_Control Hot_Feet
                Crafted_Armageddon_F (50)
                Crafted_Armageddon_F (50)
            """);
        var projection = Project(ProductionCatalog, manifest, Armageddon);
        var row = Assert.Single(projection.Attribution.ByParent);
        Assert.Equal(ProcAttributionMode.Unattributed, row.Mode);
        Assert.Equal(2, row.Candidates.Count);
        Assert.Equal(0, projection.Attribution.BuildConfirmedCount);
        Assert.Equal(1, projection.Attribution.UnattributedCount);
        Assert.Equal(0, projection.Attribution.CorrelatedCount);
        Assert.Equal(MetricAvailability.Incomplete, row.ProcDamageMetric.Availability);
        Assert.True(row.ProcDamageMetric.Coverage?.AmbiguousProcParent);
    }

    [Fact]
    public void Superior_attuned_and_crafted_variants_are_two_occurrences_of_the_same_identity()
    {
        var manifest = ManifestFromLayout("""
            Level 10: Controller_Control Fire_Control Hot_Feet
                Crafted_Armageddon_F (50)
                Superior_Attuned_Armageddon_F (1)
            """);
        Assert.Equal(2, manifest.ProcSlots.Count(slot => slot.ExactProcIdentity == "Armageddon: Chance for Fire Damage"));
        var projection = Project(ProductionCatalog, manifest, Armageddon);
        Assert.Equal(ProcAttributionMode.Unattributed, Assert.Single(projection.Attribution.ByParent).Mode);
    }

    [Fact]
    public void Missing_build_leaves_known_proc_Unattributed()
    {
        var projection = Project(catalog: ProductionCatalog, Armageddon);
        var row = Assert.Single(projection.Attribution.ByParent);
        Assert.Equal(ProcAttributionMode.Unattributed, row.Mode);
        Assert.Equal("Armageddon: Chance for Fire Damage", row.ExactProcIdentity);
        Assert.Null(row.ParentPowerId);
        Assert.Equal(MetricAvailability.Available, projection.Attribution.ProcDamage.Availability);
        Assert.Equal(new CombatScaledAmount(1971), projection.Attribution.ProcDamage.Value);
        Assert.Equal(0, projection.Attribution.CorrelatedCount);
    }

    [Fact]
    public void Unmatched_proc_is_Unattributed_when_manifest_has_no_matching_slot()
    {
        var manifest = ManifestFromLayout("""
            Level 10: Controller_Control Fire_Control Hot_Feet
                Crafted_Hecatomb_F (50)
            """);
        var projection = Project(ProductionCatalog, manifest, Armageddon);
        Assert.Equal(ProcAttributionMode.Unattributed, Assert.Single(projection.Attribution.ByParent).Mode);
        Assert.Empty(Assert.Single(projection.Attribution.ByParent).Candidates);
    }

    [Fact]
    public void Unknown_enhancement_token_cannot_be_BuildConfirmed()
    {
        var manifest = ManifestFromLayout("""
            Level 10: Controller_Control Fire_Control Hot_Feet
                Crafted_Uncatalogued_Proc_Z (50)
            """);
        Assert.Equal(ProcMappingStatus.Incomplete, Assert.Single(manifest.ProcSlots).MappingStatus);
        Assert.Null(Assert.Single(manifest.ProcSlots).ExactProcIdentity);
        var projection = Project(ProductionCatalog, manifest, Armageddon);
        Assert.Equal(ProcAttributionMode.Unattributed, Assert.Single(projection.Attribution.ByParent).Mode);
    }

    [Fact]
    public void Correlated_is_never_emitted_including_for_timing_proximity()
    {
        var manifest = ManifestFromLayout("""
            Level 10: Controller_Control Fire_Control Hot_Feet
                Crafted_Armageddon_F (50)
                Crafted_Armageddon_F (50)
            Level 8: Controller_Control Fire_Control Fire_Cages
                Crafted_Hecatomb_F (50)
            """);
        var engine = Engine(ProductionCatalog, manifest);
        foreach (var canonical in ParseCanonical(HotFeet, FireCagesTick, Armageddon))
        {
            engine.Apply(canonical);
        }

        var projection = engine.Project();
        Assert.Equal(0, projection.Attribution.CorrelatedCount);
        Assert.DoesNotContain(projection.Attribution.ByParent, row => row.Mode == ProcAttributionMode.Correlated);
        Assert.Equal(ProcAttributionMode.Unattributed, Assert.Single(projection.Attribution.ByParent).Mode);
    }

    [Fact]
    public void BuildConfirmed_does_not_consult_a_later_mutable_manifest()
    {
        var first = ManifestFromLayout(SingleArmageddonLayout());
        var second = ManifestFromLayout("""
            Level 10: Controller_Control Fire_Control Fire_Cages
                Crafted_Armageddon_F (50)
            """);
        var engine = Engine(ProductionCatalog, first);
        engine.Apply(Assert.Single(ParseCanonical(Armageddon)));
        var before = engine.Project();
        Assert.False(engine.AttachBuildContext(second, AvailableContext(second)));
        var after = engine.Project();
        Assert.Equal(first.ManifestHash, after.BuildContext.ManifestHash);
        Assert.Equal("Controller_Control.Fire_Control.Hot_Feet", Assert.Single(after.Attribution.ByParent).ParentPowerId);
        Assert.Equal(before.Attribution.ByParent[0].ParentPowerId, after.Attribution.ByParent[0].ParentPowerId);
        Assert.NotEqual(first.ManifestHash, second.ManifestHash);
    }

    [Fact]
    public void Same_stream_and_manifest_is_deterministic()
    {
        var manifest = ManifestFromLayout(SingleArmageddonLayout());
        var left = Project(ProductionCatalog, manifest, HotFeet, Armageddon);
        var right = Project(ProductionCatalog, manifest, HotFeet, Armageddon);
        Assert.Equal(left.Attribution.BuildConfirmedCount, right.Attribution.BuildConfirmedCount);
        Assert.Equal(left.Attribution.DirectCount, right.Attribution.DirectCount);
        Assert.Equal(left.Attribution.ByParent[0].ParentPowerId, right.Attribution.ByParent[0].ParentPowerId);
        Assert.Equal(left.Session.DamageDealt, right.Session.DamageDealt);
    }

    [Fact]
    public void Different_manifest_can_change_attribution_without_mutating_a_prior_projection()
    {
        var hotFeet = ManifestFromLayout(SingleArmageddonLayout());
        var fireCages = ManifestFromLayout("""
            Level 8: Controller_Control Fire_Control Fire_Cages
                Crafted_Armageddon_F (50)
            """);
        var firstEngine = Engine(ProductionCatalog, hotFeet);
        firstEngine.Apply(Assert.Single(ParseCanonical(Armageddon)));
        var first = firstEngine.Project();
        var second = Project(ProductionCatalog, fireCages, Armageddon);
        Assert.Equal("Controller_Control.Fire_Control.Hot_Feet", Assert.Single(first.Attribution.ByParent).ParentPowerId);
        Assert.Equal("Controller_Control.Fire_Control.Fire_Cages", Assert.Single(second.Attribution.ByParent).ParentPowerId);
        Assert.Equal("Controller_Control.Fire_Control.Hot_Feet", Assert.Single(firstEngine.Project().Attribution.ByParent).ParentPowerId);
    }

    [Fact]
    public void Proc_magnitude_is_not_added_to_session_totals()
    {
        var manifest = ManifestFromLayout(SingleArmageddonLayout());
        var projection = Project(ProductionCatalog, manifest, HotFeet, Armageddon);
        Assert.Equal(new CombatScaledAmount(1388 + 1971), projection.Session.DamageDealt);
        Assert.Equal(new CombatScaledAmount(1971), projection.Attribution.ProcDamage.Value);
        Assert.Equal(new CombatScaledAmount(1388), projection.Attribution.DirectDamage);
        Assert.Equal(2, projection.LogicalEventsApplied);
        Assert.Equal(MetricAvailability.Available, projection.Attribution.ProcContributionHundredths.Availability);
        Assert.Equal(1971 * 10_000 / (1388 + 1971), projection.Attribution.ProcContributionHundredths.Value);
    }

    [Fact]
    public void Duplicate_physical_event_does_not_create_a_second_attribution()
    {
        var events = ParseCanonical(HotFeet, HotFeet);
        var duplicate = events[1] with
        {
            DuplicateOf = new EventOccurrenceRef
            {
                SourceId = events[0].Provenance.SourceId,
                SourceSegmentId = events[0].Provenance.SourceSegmentId,
                BindingGeneration = events[0].Provenance.BindingGeneration,
                ParserSequence = events[0].Provenance.ParserSequence
            }
        };
        var engine = new CombatEngine(ProcLogNameIndex.FromCatalog(ProductionCatalog));
        engine.Apply(events[0]);
        engine.Apply(duplicate);
        var projection = engine.Project();
        Assert.Equal(1, projection.LogicalEventsApplied);
        Assert.Equal(1, projection.DuplicateOccurrencesIgnored);
        Assert.Equal(1, projection.Attribution.DirectCount);
        Assert.Equal(new CombatScaledAmount(1388), projection.Session.DamageDealt);
    }

    [Fact]
    public void Owned_pet_events_stay_Unattributed_and_do_not_use_the_player_manifest()
    {
        var manifest = ManifestFromLayout("""
            Level 1: Inherent Inherent Brawl
                Crafted_Armageddon_F (50)
            """);
        var projection = Project(ProductionCatalog, manifest, PetBrawl);
        Assert.Equal(1, projection.Attribution.UnattributedCount);
        Assert.Equal(0, projection.Attribution.BuildConfirmedCount);
        Assert.Equal(0, projection.Attribution.DirectCount);
        Assert.Empty(projection.Attribution.ByParent);
        Assert.Equal(new CombatScaledAmount(1561), projection.Session.DamageDealtOwnedPets);
        Assert.Equal(new CombatScaledAmount(1561), projection.Session.DamageDealt);
    }

    [Fact]
    public void Global_Incarnate_names_are_Unattributed_and_excluded_from_parent_share()
    {
        var manifest = ManifestFromLayout(SingleArmageddonLayout());
        var projection = Project(ProductionCatalog, manifest, ReactiveInterface);
        Assert.Equal(1, projection.Attribution.UnattributedCount);
        Assert.Empty(projection.Attribution.ByParent);
        Assert.Equal(new CombatScaledAmount(1000), projection.Session.DamageDealt);
        Assert.Equal(MetricAvailability.NotCaptured, projection.Attribution.ProcDamage.Availability);
        Assert.Null(projection.Attribution.ProcDamage.Value);
    }

    [Fact]
    public void Slice6_numerical_totals_and_Slice7_availability_remain()
    {
        var projection = Project(catalog: ProductionCatalog, HotFeet, PetBrawl);
        Assert.Equal(new CombatScaledAmount(1388 + 1561), projection.Session.DamageDealt);
        Assert.Equal(new CombatScaledAmount(1388), projection.Session.DamageDealtSelf);
        Assert.Equal(new CombatScaledAmount(1561), projection.Session.DamageDealtOwnedPets);
        Assert.Equal(MetricAvailability.Available, projection.Session.Metrics.DamageDealt.Availability);
        Assert.Equal(MetricAvailability.Available, projection.Session.Metrics.DamageDealtOwnedPets.Availability);
        Assert.Equal(3, AnalyticsSemanticVersion.Current);
        Assert.Equal(3, projection.AnalyticsSemanticVersion);
        Assert.Equal(1, AttributionPolicyVersion.Current);
        Assert.Equal(1, DedupPolicyVersion.Current);
        Assert.Equal(4, EventProvenance.CurrentGrammarSetVersion);
        Assert.Equal(MetricAvailability.NotCaptured, projection.BuildContext.Availability);
    }

    [Fact]
    public void Capture_time_stale_build_detection_is_not_implemented()
    {
        var manifest = ManifestFromLayout(SingleArmageddonLayout());
        var projection = Project(ProductionCatalog, manifest, Armageddon);
        Assert.Equal(ProcAttributionMode.BuildConfirmed, Assert.Single(projection.Attribution.ByParent).Mode);
        Assert.DoesNotContain(
            typeof(FrozenBuildManifest).GetProperties(),
            property => property.Name.Contains("Stale", StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains("OutOfDate", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Session_freezes_current_build_once_and_ignores_later_replacement()
    {
        var monitoring = new FakeMonitoringSessionManager();
        var parser = new GameplaySessionTestInfrastructure.FakeGameplayParserManager();
        var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dir);
        var store = new MemoryBuildStore();
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 8, 4, 12, 0, 0, TimeSpan.Zero));
        var established = repository.EstablishTrustedFromWelcome("acct-1", "Hero A");
        var recordId = Assert.IsType<CharacterRecordId>(established.RecordId);
        store.Save(Snapshot(recordId, SingleArmageddonLayout(), DateTimeOffset.UnixEpoch));
        var firstHash = ManifestFromLayout(SingleArmageddonLayout()).ManifestHash;
        var secondHash = ManifestFromLayout("""
            Level 8: Controller_Control Fire_Control Fire_Cages
                Crafted_Armageddon_F (50)
            """).ManifestHash;
        Assert.NotEqual(firstHash, secondHash);

        try
        {
            var contextId = MonitoringContextId.CreateNew();
            var source = GameplaySessionTestInfrastructure.DefaultSource();
            monitoring.SetInitial(ParserTestSnapshots.Snapshot(
                1,
                GameplaySessionTestInfrastructure.ReadyContext(contextId, source)));
            using var manager = await GameplaySessionTestInfrastructure.CreateStartedManager(
                monitoring,
                parser,
                repository,
                new GameplaySessionOptions
                {
                    TimeProvider = time,
                    CombatSnapshotPublishInterval = TimeSpan.Zero
                },
                characterBuildSnapshotStore: store,
                itemReferenceCatalog: ProductionCatalog);

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 12:00:00 Welcome to City of Heroes, Hero A!",
                    contextId,
                    source,
                    sequence: 1)
            ]);
            await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
                manager.Current.Sessions.Any(session =>
                    session.ContextId == contextId
                    && session.CombatAnalytics.BuildContext.Availability == MetricAvailability.Available
                    && session.CombatAnalytics.BuildContext.ManifestHash == firstHash));

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(Armageddon, contextId, source, sequence: 2)
            ]);
            await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
                manager.Current.Sessions.Any(session =>
                    session.CombatAnalytics.Attribution.BuildConfirmedCount == 1));

            var firstSession = Assert.Single(manager.Current.Sessions);
            Assert.Equal(firstHash, firstSession.CombatAnalytics.BuildContext.ManifestHash);
            Assert.Equal("Controller_Control.Fire_Control.Hot_Feet", Assert.Single(firstSession.CombatAnalytics.Attribution.ByParent).ParentPowerId);
            Assert.Equal(MetricAvailability.Available, firstSession.CombatAnalytics.BuildContext.Availability);

            store.Save(Snapshot(recordId, """
                Level 8: Controller_Control Fire_Control Fire_Cages
                    Crafted_Armageddon_F (50)
                """, DateTimeOffset.UtcNow));
            await GameplaySessionTestInfrastructure.WaitForWorkQueueToDrainAsync(manager);
            var stillFirst = Assert.Single(manager.Current.Sessions);
            Assert.Equal(firstSession.SessionId, stillFirst.SessionId);
            Assert.Equal(firstHash, stillFirst.CombatAnalytics.BuildContext.ManifestHash);
            Assert.Equal("Controller_Control.Fire_Control.Hot_Feet", Assert.Single(stillFirst.CombatAnalytics.Attribution.ByParent).ParentPowerId);

            time.Advance(TimeSpan.FromMinutes(1));
            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 12:01:00 Welcome to City of Heroes, Hero A!",
                    contextId,
                    source,
                    sequence: 3,
                    observedAt: time.GetUtcNow())
            ]);
            await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
                manager.Current.Sessions.Any(session =>
                    session.SessionId != firstSession.SessionId
                    && session.CombatAnalytics.BuildContext.ManifestHash == secondHash));

            var secondSession = Assert.Single(manager.Current.Sessions);
            Assert.NotEqual(firstSession.SessionId, secondSession.SessionId);
            Assert.Equal(secondHash, secondSession.CombatAnalytics.BuildContext.ManifestHash);
            Assert.Equal(MetricAvailability.Available, secondSession.CombatAnalytics.BuildContext.Availability);
            await manager.StopAsync();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task No_build_available_is_explicit_NotCaptured()
    {
        var monitoring = new FakeMonitoringSessionManager();
        var parser = new GameplaySessionTestInfrastructure.FakeGameplayParserManager();
        var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dir);
        try
        {
            var contextId = MonitoringContextId.CreateNew();
            var source = GameplaySessionTestInfrastructure.DefaultSource();
            monitoring.SetInitial(ParserTestSnapshots.Snapshot(
                1,
                GameplaySessionTestInfrastructure.ReadyContext(contextId, source)));
            using var manager = await GameplaySessionTestInfrastructure.CreateStartedManager(
                monitoring,
                parser,
                repository,
                new GameplaySessionOptions { CombatSnapshotPublishInterval = TimeSpan.Zero },
                itemReferenceCatalog: ProductionCatalog);

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 12:00:00 Welcome to City of Heroes, Hero A!",
                    contextId,
                    source,
                    sequence: 1)
            ]);
            await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
                manager.Current.Sessions.Any(session =>
                    session.CharacterIdentityResolutionState == CharacterIdentityResolutionState.Resolved
                    && session.CombatAnalytics.BuildContext.Availability == MetricAvailability.NotCaptured));

            var session = Assert.Single(manager.Current.Sessions);
            Assert.Null(session.CombatAnalytics.BuildContext.ManifestHash);
            Assert.Equal(MetricEvidence.None, session.CombatAnalytics.BuildContext.Evidence);
            await manager.StopAsync();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Corrupt_build_without_retained_content_is_NotCaptured()
    {
        var monitoring = new FakeMonitoringSessionManager();
        var parser = new GameplaySessionTestInfrastructure.FakeGameplayParserManager();
        var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dir);
        var established = repository.EstablishTrustedFromWelcome("acct-1", "Hero A");
        Assert.NotNull(established.RecordId);
        try
        {
            var contextId = MonitoringContextId.CreateNew();
            var source = GameplaySessionTestInfrastructure.DefaultSource();
            monitoring.SetInitial(ParserTestSnapshots.Snapshot(
                1,
                GameplaySessionTestInfrastructure.ReadyContext(contextId, source)));
            using var manager = await GameplaySessionTestInfrastructure.CreateStartedManager(
                monitoring,
                parser,
                repository,
                new GameplaySessionOptions { CombatSnapshotPublishInterval = TimeSpan.Zero },
                characterBuildSnapshotStore: new CorruptBuildStore(),
                itemReferenceCatalog: ProductionCatalog);

            parser.PublishClassified([
                GameplaySessionTestInfrastructure.Classify(
                    "2026-08-04 12:00:00 Welcome to City of Heroes, Hero A!",
                    contextId,
                    source,
                    sequence: 1)
            ]);
            await GameplaySessionTestInfrastructure.WaitUntilAsync(() =>
                manager.Current.Sessions.Any(session =>
                    session.CharacterIdentityResolutionState == CharacterIdentityResolutionState.Resolved
                    && session.CombatAnalytics.BuildContext.Availability == MetricAvailability.NotCaptured));

            var session = Assert.Single(manager.Current.Sessions);
            Assert.Equal(MetricAvailability.NotCaptured, session.CombatAnalytics.BuildContext.Availability);
            Assert.Null(session.CombatAnalytics.BuildContext.ManifestHash);
            Assert.Equal(MetricEvidence.None, session.CombatAnalytics.BuildContext.Evidence);
            await manager.StopAsync();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Unknown_outgoing_name_is_not_positive_Direct_evidence()
    {
        var result = Project(ProductionCatalog, HotFeet.Replace("Hot Feet", "Uncatalogued secondary effect"));
        Assert.Equal(0, result.Attribution.DirectCount);
        Assert.Equal(1, result.Attribution.UnattributedCount);
        Assert.Equal(result.Session.DamageDealt, result.Attribution.UnattributedDamage);
        Assert.Null(result.Attribution.ProcDamage.Value);
        var direct = ProcAttributionClassifier.Classify("Fire Ball", false, null, ProcLogNameIndex.Empty, directDamageForm: true);
        Assert.Equal(ProcAttributionMode.Direct, direct.Mode);
        Assert.Equal(MetricEvidence.DirectObserved, direct.Evidence);
        Assert.Equal(MetricConfidence.High, direct.Confidence);
    }

    [Fact]
    public void Late_attachment_does_not_reclassify_previous_events()
    {
        var engine = Engine(ProductionCatalog, null);
        var item = Assert.Single(ParseCanonical(Armageddon));
        engine.Apply(item);
        var before = engine.Project();
        var manifest = ManifestFromLayout(SingleArmageddonLayout());
        engine.AttachBuildContext(manifest, AvailableContext(manifest));
        Assert.Equal(0, engine.Project().Attribution.BuildConfirmedCount);
        Assert.Equal(1, engine.Project().Attribution.UnattributedCount);
        Assert.Equal(1, engine.Project().BuildContext.AppliedLogicalEventCountAtFreeze);
        engine.Apply(item with { Sequence = 2 });
        var after = engine.Project();
        Assert.Equal(1, after.Attribution.BuildConfirmedCount);
        Assert.Equal(1, after.Attribution.UnattributedCount);
        Assert.Equal(item.Amount, before.Attribution.UnattributedProcDamage);
        Assert.Equal(item.Amount, after.Attribution.BuildConfirmedProcDamage);
        Assert.Equal(item.Amount, after.Attribution.UnattributedProcDamage);
    }

    [Fact]
    public void Partial_build_retains_content_but_never_confirms_a_parent()
    {
        var manifest = ManifestFromLayout(SingleArmageddonLayout() + "\n    Crafted_Unknown_Thing (50)");
        var result = Project(ProductionCatalog, manifest, Armageddon);
        Assert.Equal(MetricAvailability.Incomplete, result.BuildContext.Availability);
        Assert.NotNull(result.BuildContext.ManifestHash);
        Assert.Equal(2, result.BuildContext.ProcSlotCount);
        Assert.Equal(0, result.Attribution.BuildConfirmedCount);
        Assert.Equal(1, result.Attribution.UnattributedCount);
    }

    [Fact]
    public void Frozen_lists_copy_caller_owned_backing_arrays_and_candidates()
    {
        var original = ManifestFromLayout(SingleArmageddonLayout());
        var powers = original.Powers.ToArray();
        var slots = original.ProcSlots.ToArray();
        var identities = original.ProcIdentities.ToArray();
        var frozen = original with { Powers = powers, ProcSlots = slots, ProcIdentities = identities };
        var hash = FrozenBuildManifestHash.Compute(frozen);
        powers[0] = powers[0] with { RawPowerToken = "Changed" };
        slots[0] = slots[0] with { ExactProcIdentity = "Changed" };
        identities[0] = new FrozenProcIdentity("Changed", "Changed");
        Assert.Equal(hash, FrozenBuildManifestHash.Compute(frozen));
        var candidates = new[] { new ProcAttributionCandidate { ParentPowerId = "A", ParentPowerName = "A", Occurrence = original.ProcSlots[0].Occurrence } };
        var attribution = PowerAttribution.Unattributed with { Candidates = candidates };
        candidates[0] = candidates[0] with { ParentPowerId = "B" };
        Assert.Equal("A", attribution.Candidates[0].ParentPowerId);
        Assert.Throws<NotSupportedException>(() => ((IList)attribution.Candidates).Clear());
    }

    [Fact]
    public void Fingerprint_is_culture_and_enumeration_independent_but_not_slot_order_independent()
    {
        var manifest = ManifestFromLayout(SingleArmageddonLayout() + "\n    Crafted_Hecatomb_F (50)");
        var reversed = manifest with { Powers = manifest.Powers.Reverse().ToArray(), ProcSlots = manifest.ProcSlots.Reverse().ToArray() };
        Assert.Equal(manifest.ManifestHash, FrozenBuildManifestHash.Compute(reversed));
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("ar-SA");
            Assert.Equal(manifest.ManifestHash, FrozenBuildManifestHash.Compute(manifest));
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = previous; }
        Assert.Equal(manifest.ManifestHash, FrozenBuildManifestHash.Compute(manifest with { AttributionPolicyVersion = 99 }));
        var slots = manifest.ProcSlots.Select(slot => slot with { Occurrence = slot.Occurrence with { SlotOrder = 1 - slot.Occurrence.SlotOrder } }).ToArray();
        Assert.NotEqual(manifest.ManifestHash, FrozenBuildManifestHash.Compute(manifest with { ProcSlots = slots }));
    }

    [Fact]
    public void Frozen_catalog_facts_survive_changed_current_catalog()
    {
        var manifest = ManifestFromLayout(SingleArmageddonLayout());
        var emptyNames = ProcLogNameIndex.Empty;
        var result = ProcAttributionClassifier.Classify("Armageddon: Chance for Fire Damage", false, manifest, emptyNames);
        Assert.Equal(ProcAttributionMode.BuildConfirmed, result.Mode);
        var item = ProductionCatalog.GetEnhancements().Single(i => i.CatalogItemId == "ENH-01287");
        var changed = EnhancementTokenResolver.FromItems([item with { CurrentDisplayName = "Different catalog name" }], ProductionCatalog.Manifest!.CatalogVersion);
        Assert.NotEqual(EnhancementTokenResolver.FromItems([item], ProductionCatalog.Manifest!.CatalogVersion).CatalogFingerprint, changed.CatalogFingerprint);
        Assert.Equal(ProcAttributionMode.BuildConfirmed,
            ProcAttributionClassifier.Classify("Armageddon: Chance for Fire Damage", false, manifest, changed.ProcLogNames).Mode);
    }

    [Fact]
    public void Token_and_log_name_collisions_never_choose_a_winner()
    {
        var item = ProductionCatalog.GetEnhancements().Single(i => i.CatalogItemId == "ENH-01287");
        var resolver = EnhancementTokenResolver.FromItems([item, item with { CatalogItemId = "OTHER" }], "same-version");
        Assert.Equal(ProcMappingStatus.Collision, resolver.TryResolve("Crafted_Armageddon_F", out var resolved));
        Assert.Null(resolved);
        Assert.True(resolver.ProcLogNames.IsAmbiguous(item.CurrentDisplayName));
        var result = ProcAttributionClassifier.Classify(item.CurrentDisplayName, false, null, resolver.ProcLogNames);
        Assert.Equal(ProcAttributionMode.Unattributed, result.Mode);
        Assert.Null(result.ExactProcIdentity);
        var ordinary = ProductionCatalog.GetEnhancements().First(i => i.CatalogItemId != item.CatalogItemId);
        Assert.False(ProcLogNameIndex.FromCatalog(ProductionCatalog).IsExactProcIdentity(ordinary.CurrentDisplayName));
    }

    [Fact]
    public void Power_name_collision_and_two_parent_powers_are_unattributed()
    {
        var original = ManifestFromLayout(SingleArmageddonLayout());
        var colliding = original with { Powers = original.Powers.Append(new FrozenBuildPower
        {
            RawCategoryToken = "Other", RawPowerSetToken = "Other", RawPowerToken = "Armageddon:_Chance_for_Fire_Damage",
            AcquisitionLevel = 1, SourceOrder = 1
        }).ToArray() };
        var collision = Project(ProductionCatalog, colliding, Armageddon);
        Assert.Equal(1, collision.Attribution.UnattributedCount);
        Assert.Equal(0, collision.Attribution.BuildConfirmedCount);
        Assert.Null(collision.Attribution.ProcDamage.Value);
        var multiple = ManifestFromLayout(SingleArmageddonLayout() + "\nLevel 8: Controller_Control Fire_Control Fire_Cages\n    Crafted_Armageddon_F (50)");
        var result = Project(ProductionCatalog, multiple, Armageddon);
        Assert.Equal(2, Assert.Single(result.Attribution.ByParent).Candidates.Count);
        Assert.Equal(0, result.Attribution.BuildConfirmedCount);
    }

    [Fact]
    public void Attribution_partitions_damage_only_without_double_counting_ticks_or_mixed_units()
    {
        var manifest = ManifestFromLayout(SingleArmageddonLayout());
        var engine = Engine(ProductionCatalog, manifest);
        var lines = new[] { HotFeet, Armageddon, Armageddon, PetBrawl, ReactiveInterface,
            HotFeet.Replace("Hot Feet", "Unknown"),
            "2026-09-12 05:36:59 You heal Hero_A with Transfusion for 421.34 health points.",
            "2026-09-12 05:24:59 You hit Hero_A with your Panacea: Chance for +Hit Points/Endurance granting them 7.5 points of endurance." };
        foreach (var item in ParseCanonical(lines)) engine.Apply(item);
        var result = engine.Project();
        var a = result.Attribution;
        Assert.Equal(6, a.DirectCount + a.BuildConfirmedCount + a.UnattributedCount);
        Assert.Equal(result.Session.DamageDealt.Hundredths,
            a.DirectDamage.Hundredths + a.BuildConfirmedProcDamage.Hundredths + a.UnattributedDamage.Hundredths);
        Assert.Equal(2, a.BuildConfirmedCount);
        Assert.Equal(MetricAvailability.Incomplete, a.ProcDamage.Availability);
        Assert.True(a.ProcDamage.Coverage?.UnidentifiedProcSource);
        Assert.Equal(0, a.CorrelatedCount);
        Assert.Equal(new CombatScaledAmount(42134), result.Session.HealingDealt);
        Assert.Equal(new CombatScaledAmount(750), result.Session.EnduranceGranted);
    }

    [Fact]
    public void Contribution_uses_wide_arithmetic_and_observed_zero_needs_real_evidence()
    {
        var engine = Engine(ProductionCatalog, ManifestFromLayout(SingleArmageddonLayout()));
        var item = Assert.Single(ParseCanonical(Armageddon));
        engine.Apply(item with { Amount = new CombatScaledAmount(long.MaxValue) });
        Assert.Equal(10000, engine.Project().Attribution.ProcContributionHundredths.Value);
        var zero = Engine(ProductionCatalog, ManifestFromLayout(SingleArmageddonLayout()));
        Assert.Null(zero.Project().Attribution.ProcDamage.Value);
        zero.Apply(item with { Amount = CombatScaledAmount.Zero });
        Assert.Equal(MetricAvailability.Available, zero.Project().Attribution.ProcDamage.Availability);
        Assert.Equal(CombatScaledAmount.Zero, zero.Project().Attribution.ProcDamage.Value);
    }

    [Fact]
    public void Unknown_name_stream_does_not_retain_unbounded_attribution_history()
    {
        var engine = new CombatEngine();
        var item = Assert.Single(ParseCanonical(HotFeet));
        for (int i = 0; i < 2000; i++) engine.Apply(item with { PowerName = $"Unknown{i}" });
        Assert.Equal(2000, engine.Project().Attribution.UnattributedCount);
        Assert.DoesNotContain(typeof(CombatEngine).GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance),
            field => field.Name == "_attributionObservations");
        var cache = Assert.IsAssignableFrom<IDictionary>(typeof(CombatEngine).GetField("_attributionCache",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(engine));
        Assert.True(cache.Count <= CombatEngine.MaxAttributionBuckets);
    }

    [Fact]
    public async Task Manual_resolution_freezes_once_but_buffered_combat_remains_unattributed()
    {
        var monitoring = new FakeMonitoringSessionManager();
        var parser = new GameplaySessionTestInfrastructure.FakeGameplayParserManager();
        var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dir);
        var store = new MemoryBuildStore();
        var context = MonitoringContextId.CreateNew();
        var source = GameplaySessionTestInfrastructure.DefaultSource();
        var segment = ParserSourceSegmentId.CreateNew();
        monitoring.SetInitial(ParserTestSnapshots.Snapshot(1, GameplaySessionTestInfrastructure.ReadyContext(context, source)));
        using var manager = await GameplaySessionTestInfrastructure.CreateStartedManager(monitoring, parser, repository,
            new GameplaySessionOptions { CombatSnapshotPublishInterval = TimeSpan.Zero },
            characterBuildSnapshotStore: store, itemReferenceCatalog: ProductionCatalog);
        try
        {
            ParserEvent Line(long sequence) => GameplaySessionTestInfrastructure.Classify(Armageddon, context, source, sequence) with
                { SourceSegmentId = segment, SourceByteStart = sequence * 300, SourceByteEnd = sequence * 300 + 200 };
            parser.PublishClassified([Line(1)]);
            await GameplaySessionTestInfrastructure.WaitForWorkQueueToDrainAsync(manager);
            var before = Assert.Single(manager.Current.Sessions);
            Assert.NotEqual(CharacterIdentityResolutionState.Resolved, before.CharacterIdentityResolutionState);
            Assert.Equal(0, store.LoadCount);
            var recordId = repository.EstablishTrustedFromManualConfirmation("acct-1", "Hero A").RecordId!;
            store.Save(Snapshot(recordId, SingleArmageddonLayout(), DateTimeOffset.UnixEpoch));
            manager.ConfirmCharacter(context, recordId);
            await GameplaySessionTestInfrastructure.WaitForWorkQueueToDrainAsync(manager);
            var confirmed = Assert.Single(manager.Current.Sessions);
            Assert.Equal(before.SessionId, confirmed.SessionId);
            Assert.Equal(1, store.LoadCount);
            Assert.Equal(1, confirmed.CombatAnalytics.Attribution.UnattributedCount);
            Assert.Equal(0, confirmed.CombatAnalytics.Attribution.BuildConfirmedCount);
            Assert.Single(confirmed.CombatAnalytics.BuildContext.PreFreezeSourceBoundaries);
            parser.PublishClassified([Line(2)]);
            await GameplaySessionTestInfrastructure.WaitForWorkQueueToDrainAsync(manager);
            Assert.Equal(1, Assert.Single(manager.Current.Sessions).CombatAnalytics.Attribution.BuildConfirmedCount);
            manager.ConfirmCharacter(context, recordId);
            await GameplaySessionTestInfrastructure.WaitForWorkQueueToDrainAsync(manager);
            Assert.Equal(1, store.LoadCount);
            await manager.StopAsync();
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task A_B_A_freezes_independently_and_exact_Welcome_recovery_does_not_refreeze()
    {
        var monitoring = new FakeMonitoringSessionManager();
        var parser = new GameplaySessionTestInfrastructure.FakeGameplayParserManager();
        var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dir);
        var store = new MemoryBuildStore();
        var context = MonitoringContextId.CreateNew();
        var source = GameplaySessionTestInfrastructure.DefaultSource();
        var segment = ParserSourceSegmentId.CreateNew();
        var a = repository.EstablishTrustedFromWelcome("acct-1", "Hero A").RecordId!;
        var b = repository.EstablishTrustedFromWelcome("acct-1", "Hero B").RecordId!;
        store.Save(Snapshot(a, SingleArmageddonLayout(), DateTimeOffset.UnixEpoch));
        store.Save(Snapshot(b, SingleArmageddonLayout().Replace("Hot_Feet", "Fire_Cages"), DateTimeOffset.UnixEpoch));
        monitoring.SetInitial(ParserTestSnapshots.Snapshot(1, GameplaySessionTestInfrastructure.ReadyContext(context, source)));
        using var manager = await GameplaySessionTestInfrastructure.CreateStartedManager(monitoring, parser, repository,
            new GameplaySessionOptions { CombatSnapshotPublishInterval = TimeSpan.Zero },
            characterBuildSnapshotStore: store, itemReferenceCatalog: ProductionCatalog);
        try
        {
            var ids = new HashSet<GameplaySessionId>();
            long sequence = 0;
            foreach (var name in new[] { "Hero A", "Hero B", "Hero A" })
            {
                var seq = ++sequence;
                var welcome = GameplaySessionTestInfrastructure.Classify($"2026-08-04 12:0{seq}:00 Welcome to City of Heroes, {name}!", context, source, seq) with
                    { SourceSegmentId = segment, SourceByteStart = seq * 300, SourceByteEnd = seq * 300 + 200 };
                parser.PublishClassified([welcome]);
                await GameplaySessionTestInfrastructure.WaitForWorkQueueToDrainAsync(manager);
                var current = Assert.Single(manager.Current.Sessions);
                Assert.True(ids.Add(current.SessionId));
                Assert.Equal(seq, store.LoadCount);
                parser.PublishClassified([welcome with { IsRecoveredWelcome = true }]);
                await GameplaySessionTestInfrastructure.WaitForWorkQueueToDrainAsync(manager);
                Assert.Equal(current.SessionId, Assert.Single(manager.Current.Sessions).SessionId);
                Assert.Equal(seq, store.LoadCount);
            }
            await manager.StopAsync();
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Duplicate_power_identity_is_partial_rather_than_throwing_or_selecting_a_parent()
    {
        var manifest = ManifestFromLayout(SingleArmageddonLayout());
        var duplicate = manifest with { Powers = manifest.Powers.Append(manifest.Powers[0] with { SourceOrder = 1 }).ToArray() };
        var result = Project(ProductionCatalog, duplicate, Armageddon);
        Assert.Equal(MetricAvailability.Incomplete, result.BuildContext.Availability);
        Assert.Equal(0, result.Attribution.BuildConfirmedCount);
        Assert.Equal(1, result.Attribution.UnattributedCount);
    }

    [Fact]
    public void Enhancement_source_aliases_are_exact_and_case_sensitive()
    {
        var resolver = EnhancementTokenResolver.FromCatalog(ProductionCatalog);
        Assert.Equal(ProcMappingStatus.Resolved, resolver.TryResolve("Crafted_Armageddon_F", out var token));
        Assert.Equal(ProcMappingStatus.Resolved, resolver.TryResolve("Boosts.Crafted_Armageddon_F.Crafted_Armageddon_F", out var full));
        Assert.Equal(token!.CatalogItemId, full!.CatalogItemId);
        Assert.Equal(token.EnhancementSetId, full.EnhancementSetId);
        Assert.Equal(ProcMappingStatus.Incomplete, resolver.TryResolve("crafted_armageddon_f", out _));
        Assert.Equal(ProcMappingStatus.Incomplete, resolver.TryResolve("Prefix_Crafted_Armageddon_F", out _));
    }

    private static FrozenBuildManifest ManifestFromLayout(
        string layoutText,
        DateTimeOffset? syncedAt = null)
    {
        Assert.True(HomecomingBuildLayoutParser.TryParse(layoutText, out var layout));
        return FrozenBuildManifestFactory.Create(
            new CharacterBuildSnapshot
            {
                CharacterRecordId = CharacterRecordId.CreateNew(),
                SyncedAtUtc = syncedAt ?? DateTimeOffset.UnixEpoch,
                Layout = layout
            },
            EnhancementTokenResolver.FromCatalog(ProductionCatalog),
            ProductionCatalog.Manifest!.CatalogVersion);
    }

    private static CharacterBuildSnapshot Snapshot(
        CharacterRecordId recordId,
        string layoutText,
        DateTimeOffset syncedAt)
    {
        Assert.True(HomecomingBuildLayoutParser.TryParse(layoutText, out var layout));
        return new CharacterBuildSnapshot
        {
            CharacterRecordId = recordId,
            SyncedAtUtc = syncedAt,
            Layout = layout
        };
    }

    private static CombatEngine Engine(IItemReferenceCatalog catalog, FrozenBuildManifest? manifest)
    {
        var engine = new CombatEngine(ProcLogNameIndex.FromCatalog(catalog));
        if (manifest is not null)
        {
            Assert.True(engine.AttachBuildContext(manifest, AvailableContext(manifest)));
        }

        return engine;
    }

    private static CombatBuildContextSummary AvailableContext(FrozenBuildManifest manifest) =>
        new()
        {
            Availability = MetricAvailability.Available,
            Evidence = MetricEvidence.DerivedFromObserved,
            ManifestHash = manifest.ManifestHash,
            BuildCatalogFingerprint = manifest.BuildCatalogFingerprint,
            AttributionPolicyVersion = AttributionPolicyVersion.Current,
            PowerCount = manifest.Powers.Count,
            ProcSlotCount = manifest.ProcSlots.Count,
            ResolvedProcIdentityCount = manifest.ProcSlots.Count(slot => slot.ExactProcIdentity is not null)
        };

    private static CombatAnalyticsProjection Project(
        IItemReferenceCatalog catalog,
        FrozenBuildManifest? manifest,
        params string[] lines)
    {
        var engine = Engine(catalog, manifest);
        foreach (var canonical in ParseCanonical(lines))
        {
            engine.Apply(canonical);
        }

        return engine.Project();
    }

    private static CombatAnalyticsProjection Project(IItemReferenceCatalog catalog, params string[] lines) =>
        Project(catalog, manifest: null, lines);

    private static IReadOnlyList<CanonicalCombatEvent> ParseCanonical(params string[] lines)
    {
        var events = new List<CanonicalCombatEvent>(lines.Length);
        for (var index = 0; index < lines.Length; index++)
        {
            var classified = CombatEventParserTestSupport.Classify(lines[index], sequence: index + 1);
            Assert.True(CombatEventParserTestSupport.Parser.TryParseCanonical(classified, out var canonical));
            events.Add(canonical);
        }

        return events;
    }

    private static string SingleArmageddonLayout() => """
        Level 10: Controller_Control Fire_Control Hot_Feet
            Crafted_Armageddon_F (50)
        """;

    private sealed class MemoryBuildStore : ICharacterBuildSnapshotStore
    {
        private readonly Dictionary<Guid, CharacterBuildSnapshot> _items = [];
        public int LoadCount { get; private set; }

        public CharacterBuildSnapshotLoadResult TryLoad(CharacterRecordId characterRecordId)
        {
            LoadCount++;
            return _items.TryGetValue(characterRecordId.Value, out var snapshot)
                ? CharacterBuildSnapshotLoadResult.Loaded("memory", snapshot)
                : CharacterBuildSnapshotLoadResult.NotFound("memory");
        }

        public CharacterBuildSnapshotSaveResult Save(CharacterBuildSnapshot snapshot)
        {
            _items[snapshot.CharacterRecordId.Value] = snapshot;
            return CharacterBuildSnapshotSaveResult.Saved("memory");
        }
    }

    private sealed class CorruptBuildStore : ICharacterBuildSnapshotStore
    {
        public CharacterBuildSnapshotLoadResult TryLoad(CharacterRecordId characterRecordId) =>
            CharacterBuildSnapshotLoadResult.Corrupt("memory", "corrupt fixture");

        public CharacterBuildSnapshotSaveResult Save(CharacterBuildSnapshot snapshot) =>
            CharacterBuildSnapshotSaveResult.Saved("memory");
    }
}
