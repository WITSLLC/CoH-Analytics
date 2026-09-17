using System.Text.Json.Nodes;
using CoHAnalytics.Models;
using CoHAnalytics.ReferenceData;
using CoHAnalytics.Services;
using CoHAnalytics.Tests.Orchestration;
using Xunit.Abstractions;

namespace CoHAnalytics.Tests.Services;

/// <summary>Slice 9 durable Segment hybrid persistence. Test-only roundtrips; no historical UI.</summary>
public sealed class Slice9DurableSegmentTests
{
    private readonly ITestOutputHelper _output;

    public Slice9DurableSegmentTests(ITestOutputHelper output) => _output = output;
    private const string HotFeet =
        "2026-08-04 12:00:00 You hit Lusca with your Hot Feet for 13.88 points of Fire damage.";
    private const string FireCagesTick =
        "2026-09-12 05:36:30 You hit Cleaner with your Fire Cages for 2.57 points of Fire damage over time.";
    private const string IncomingDamage =
        "2026-08-04 12:00:00 Crey Thorn Mook hits you with Bone Shard for 22.15 points of Lethal damage.";
    private const string PetBrawl =
        "2026-09-12 05:38:47 Imp:  You hit Cleaner with your Brawl for 15.61 points of Fire damage over time.";
    private const string Armageddon =
        "2026-09-12 05:38:44 You hit Dismantler with your Armageddon: Chance for Fire Damage for 19.71 points of Fire damage.";
    private const string HastenActivated =
        "2026-09-12 05:25:01 You activated the Hasten power.";
    private const string HastenRecharged =
        "2026-09-12 05:25:08 Hasten is recharged.";
    private const string AttackResolution =
        "2026-08-06 12:00:00 HIT Rikti Pylon! Your Flashfire power had a 95.00% chance to hit, you rolled a 51.51.";
    private const string ZeroHotFeet =
        "2026-08-04 12:00:00 You hit Lusca with your Hot Feet for 0.00 points of Fire damage.";

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

    [Fact]
    public void Finalized_segment_roundtrips_identity_versions_and_session_totals()
    {
        using var root = new TempRoot();
        var engine = Apply(HotFeet, IncomingDamage, PetBrawl, FireCagesTick, HastenActivated, HastenRecharged, AttackResolution);
        var live = engine.Project();
        var store = new SegmentStore(root.Path);
        var draft = Draft(engine);
        Assert.Equal(SegmentPersistOutcome.Persisted, store.Persist(draft).Outcome);
        Assert.Equal(SegmentPersistOutcome.Duplicate, store.Persist(draft).Outcome);
        Assert.Single(store.ListPublishedDirectories());

        var loaded = AssertLoaded(store, draft.GameplaySessionId);
        Assert.Equal(draft.GameplaySessionId, loaded.Metadata.GameplaySessionId);
        Assert.Equal(0, loaded.Metadata.SegmentOrdinal);
        Assert.Equal(draft.CharacterRecordId, loaded.Metadata.CharacterRecordId);
        Assert.Equal("acct-1", loaded.Metadata.AccountStableId);
        Assert.Equal(SegmentSchemaVersion.Current, loaded.Metadata.SegmentSchemaVersion);
        Assert.Equal(1, loaded.Metadata.SegmentSchemaVersion);
        Assert.Equal(AnalyticsSemanticVersion.Current, loaded.Metadata.AnalyticsSemanticVersion);
        Assert.Equal(3, loaded.Metadata.AnalyticsSemanticVersion);
        Assert.Equal(EventProvenance.CurrentGrammarSetVersion, loaded.Metadata.GrammarSetVersion);
        Assert.Equal(4, loaded.Metadata.GrammarSetVersion);
        Assert.Equal(DedupPolicyVersion.Current, loaded.Metadata.DedupPolicyVersion);
        Assert.Equal(1, loaded.Metadata.DedupPolicyVersion);
        Assert.Equal(AttributionPolicyVersion.Current, loaded.Metadata.AttributionPolicyVersion);
        Assert.Equal(2, loaded.Metadata.AttributionPolicyVersion);
        Assert.Equal(SpineSchemaVersion.Current, loaded.Metadata.SpineSchemaVersion);
        Assert.Equal(live.Session.DamageDealt, loaded.Aggregates.Session.DamageDealt);
        Assert.Equal(live.Session.DamageDealtSelf, loaded.Aggregates.Session.DamageDealtSelf);
        Assert.Equal(live.Session.DamageDealtOwnedPets, loaded.Aggregates.Session.DamageDealtOwnedPets);
        Assert.Equal(live.Session.DamageReceived, loaded.Aggregates.Session.DamageReceived);
        Assert.Equal(live.Session.HealingDealt, loaded.Aggregates.Session.HealingDealt);
        Assert.Equal(live.Session.EnduranceGranted, loaded.Aggregates.Session.EnduranceGranted);
        Assert.Equal(live.Session.ActivationCount, loaded.Aggregates.Session.ActivationCount);
        Assert.Equal(live.Session.Accuracy, loaded.Aggregates.Session.Accuracy);
        Assert.Equal(MetricAvailability.Available, loaded.Aggregates.Session.Metrics.Accuracy.Availability);
        Assert.Equal(live.LogicalEventsApplied, loaded.Aggregates.LogicalEventsApplied);
        Assert.Equal(engine.LogicalEventsApplied, loaded.Coverage.LogicalEventCount);
        Assert.False(loaded.Coverage.SpineTruncated);
        Assert.Equal(ReplayCoverageKind.Lossless, loaded.Coverage.Replay.SessionTotals.Replay);
        Assert.True(loaded.Coverage.Replay.SessionTotals.AggregateAuthoritative);
        Assert.Equal(ReplayCoverageKind.Lossless, loaded.Coverage.Replay.EventTimeline.Replay);
        Assert.DoesNotContain(
            Directory.EnumerateFiles(loaded.PublishedDirectory!, "*", SearchOption.AllDirectories),
            path => File.ReadAllText(path).Contains("You hit Lusca", StringComparison.Ordinal));
    }

    [Fact]
    public void Availability_zero_not_captured_incomplete_and_unsupported_survive_roundtrip()
    {
        using var root = new TempRoot();
        var engine = new CombatEngine();
        engine.Apply(ParseOne(ZeroHotFeet));
        for (var index = 0; index < CombatEngine.MaxTrackedTargets + 1; index++)
        {
            engine.Apply(ParseOne(HotFeet) with
            {
                Target = ActorRef.UnknownNamed($"Mob{index}"),
                Provenance = WithSequence(ParseOne(HotFeet).Provenance, index + 2),
                Sequence = index + 2
            });
        }

        var live = engine.Project();
        Assert.Equal(MetricAvailability.Available, live.Session.Metrics.DamageDealt.Availability);
        Assert.Equal(MetricAvailability.NotCaptured, live.Session.Metrics.HealingDealt.Availability);
        Assert.Equal(MetricAvailability.Incomplete, live.Session.Metrics.DistinctTargetCount.Availability);
        Assert.Equal(MetricAvailability.Unsupported, live.Clock.ActiveDuration.Availability);
        Assert.True(live.Session.Metrics.DistinctTargetCount.Coverage is { LowerBound: true, Overflow: true });
        Assert.Equal(MetricEvidence.CoverageLimited, live.Session.Metrics.DistinctTargetCount.Evidence);

        var loaded = Roundtrip(root.Path, engine);
        Assert.Equal(MetricAvailability.Available, loaded.Aggregates.Session.Metrics.DamageDealt.Availability);
        Assert.Equal(MetricAvailability.NotCaptured, loaded.Aggregates.Session.Metrics.HealingDealt.Availability);
        Assert.Null(loaded.Aggregates.Session.Metrics.HealingDealt.Value);
        Assert.Equal(MetricAvailability.Incomplete, loaded.Aggregates.Session.Metrics.DistinctTargetCount.Availability);
        Assert.True(loaded.Aggregates.Session.Metrics.DistinctTargetCount.Coverage is { LowerBound: true, Overflow: true });
        Assert.Equal(MetricEvidence.CoverageLimited, loaded.Aggregates.Session.Metrics.DistinctTargetCount.Evidence);
        Assert.Equal(MetricAvailability.Unsupported, loaded.Aggregates.Clock.ActiveDuration.Availability);
        Assert.Null(loaded.Aggregates.Clock.ActiveDuration.Value);
        Assert.Equal(MetricAvailability.Unsupported, loaded.Aggregates.Session.Metrics.TheoreticalRecharge.Availability);
        Assert.Contains(loaded.Aggregates.Targets, row => row.IsOverflow);
        Assert.Equal(MetricAvailability.NotCaptured, loaded.Aggregates.Session.Metrics.Accuracy.Availability);

        var zero = Roundtrip(System.IO.Path.Combine(root.Path, "zero"), Apply(ZeroHotFeet));
        Assert.Equal(MetricAvailability.Available, zero.Aggregates.Session.Metrics.DamageDealt.Availability);
        Assert.Equal(CombatScaledAmount.Zero, zero.Aggregates.Session.Metrics.DamageDealtSelf.Value);
    }

    [Fact]
    public void Per_power_actor_type_dot_and_lifecycle_rows_roundtrip()
    {
        using var root = new TempRoot();
        var engine = Apply(HotFeet, FireCagesTick, IncomingDamage, PetBrawl, HastenActivated, HastenRecharged);
        var live = engine.Project();
        var loaded = Roundtrip(root.Path, engine);
        Assert.Equal(live.Powers.Count, loaded.Aggregates.Powers.Count);
        foreach (var expected in live.Powers)
        {
            var actual = Assert.Single(
                loaded.Aggregates.Powers,
                row => row.Scope == expected.Scope
                    && row.Direction == expected.Direction
                    && row.PowerName == expected.PowerName
                    && row.PetNormalizedName == expected.PetNormalizedName);
            Assert.Equal(expected.DamageMagnitude, actual.DamageMagnitude);
            Assert.Equal(expected.DirectAmount, actual.DirectAmount);
            Assert.Equal(expected.DotAmount, actual.DotAmount);
            Assert.Equal(expected.ActivationCount, actual.ActivationCount);
            Assert.Equal(expected.ConfirmedRechargeCompletedCount, actual.ConfirmedRechargeCompletedCount);
            Assert.Equal(expected.TotalMagnitudeMetric.Availability, actual.TotalMagnitudeMetric.Availability);
        }

        Assert.Equal(live.Actors.Count, loaded.Aggregates.Actors.Count);
        Assert.Contains(loaded.Aggregates.Actors, row => row.Scope == CombatAnalyticsScope.Self);
        Assert.Contains(loaded.Aggregates.Actors, row => row.Scope == CombatAnalyticsScope.PerPet);
        Assert.Equal(live.DamageTypes.Count, loaded.Aggregates.DamageTypes.Count);
        Assert.Equal(live.IncomingDamageTypes.Count, loaded.Aggregates.IncomingDamageTypes.Count);
        Assert.Equal(
            live.Session.Metrics.ConfirmedRechargeCompletedCount.Availability,
            loaded.Aggregates.Session.Metrics.ConfirmedRechargeCompletedCount.Availability);
        Assert.Equal(1, live.Session.ConfirmedRechargeCompletedCount);
        Assert.Equal(1, loaded.Aggregates.Session.ConfirmedRechargeCompletedCount);
    }

    [Fact]
    public void Frozen_build_and_proc_attribution_roundtrip_without_current_catalog()
    {
        using var root = new TempRoot();
        var manifest = ManifestFromLayout(SingleArmageddonLayout());
        var engine = new CombatEngine(ProcLogNameIndex.FromCatalog(ProductionCatalog));
        Assert.True(engine.AttachBuildContext(manifest, AvailableContext(manifest)));
        engine.Apply(ParseOne(Armageddon));
        var live = engine.Project();
        Assert.Equal(1, live.Attribution.BuildConfirmedCount);

        var loaded = Roundtrip(root.Path, engine);
        Assert.Equal(manifest.ManifestHash, loaded.Metadata.BuildManifestHash);
        Assert.Equal(manifest.BuildCatalogFingerprint, loaded.Metadata.BuildCatalogFingerprint);
        Assert.Equal(manifest.ManifestHash, loaded.FrozenManifest!.ManifestHash);
        Assert.Equal(manifest.BuildCatalogFingerprint, loaded.FrozenManifest.BuildCatalogFingerprint);
        Assert.Equal(AttributionPolicyVersion.Current, loaded.FrozenManifest.AttributionPolicyVersion);
        Assert.Equal("Armageddon: Chance for Fire Damage", Assert.Single(loaded.FrozenManifest.ProcSlots).ExactProcIdentity);
        Assert.Equal(1, loaded.Aggregates.Attribution.BuildConfirmedCount);
        Assert.Equal(live.Attribution.BuildConfirmedProcDamage, loaded.Aggregates.Attribution.BuildConfirmedProcDamage);
        Assert.Equal(2, loaded.Aggregates.Attribution.AttributionPolicyVersion);
        Assert.Equal(ReplayCoverageKind.NotRecomputable, loaded.Coverage.Replay.ProcAttribution.Replay);
        Assert.Equal(ReplayCoverageKind.NotRecomputable, loaded.Coverage.Replay.FrozenBuildContext.Replay);
        Assert.True(loaded.Coverage.Replay.FrozenBuildContext.AggregateAuthoritative);
        var attributed = Assert.Single(loaded.Spine, item => item.PowerName == "Armageddon: Chance for Fire Damage");
        Assert.Equal(ProcAttributionMode.BuildConfirmed, attributed.Attribution!.Mode);
        Assert.Equal(2, attributed.Attribution.PolicyVersion);
    }

    [Fact]
    public void Later_build_does_not_rewrite_an_earlier_segment_manifest()
    {
        using var root = new TempRoot();
        var store = new SegmentStore(root.Path);
        var firstManifest = ManifestFromLayout(SingleArmageddonLayout());
        var secondManifest = ManifestFromLayout("""
            Level 10: Controller_Control Fire_Control Fire_Cages
                Crafted_Armageddon_F (50)
            """);
        Assert.NotEqual(firstManifest.ManifestHash, secondManifest.ManifestHash);

        var firstEngine = new CombatEngine(ProcLogNameIndex.FromCatalog(ProductionCatalog));
        Assert.True(firstEngine.AttachBuildContext(firstManifest, AvailableContext(firstManifest)));
        firstEngine.Apply(ParseOne(Armageddon));
        var firstDraft = Draft(firstEngine);
        Assert.Equal(SegmentPersistOutcome.Persisted, store.Persist(firstDraft).Outcome);

        var secondEngine = new CombatEngine(ProcLogNameIndex.FromCatalog(ProductionCatalog));
        Assert.True(secondEngine.AttachBuildContext(secondManifest, AvailableContext(secondManifest)));
        secondEngine.Apply(ParseOne(FireCagesTick));
        Assert.Equal(SegmentPersistOutcome.Persisted, store.Persist(Draft(secondEngine)).Outcome);

        var reloaded = AssertLoaded(store, firstDraft.GameplaySessionId);
        Assert.Equal(firstManifest.ManifestHash, reloaded.FrozenManifest!.ManifestHash);
        Assert.Equal("Hot_Feet", Assert.Single(reloaded.FrozenManifest.Powers).RawPowerToken);
    }

    [Fact]
    public void Event_spine_stores_logical_events_only_and_preserves_order_fields()
    {
        using var root = new TempRoot();
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
        var sameTimestamp = events[0] with
        {
            ObservedAt = events[0].ObservedAt,
            Sequence = 3,
            Provenance = events[0].Provenance with
            {
                ParserSequence = 3,
                ByteStart = 900,
                ByteEnd = 990
            }
        };
        var engine = new CombatEngine();
        engine.Apply(events[0]);
        engine.Apply(duplicate);
        engine.Apply(sameTimestamp);
        Assert.Equal(2, engine.LogicalEventsApplied);
        Assert.Equal(1, engine.DuplicateOccurrencesIgnored);
        Assert.Equal(2, engine.RetainedSpine.Count);

        var loaded = Roundtrip(root.Path, engine);
        Assert.Equal(2, loaded.Spine.Count);
        Assert.Equal(1, loaded.Coverage.DuplicateOccurrencesIgnored);
        Assert.Equal(events[0].Provenance.ParserSequence, loaded.Spine[0].Provenance.ParserSequence);
        Assert.Equal(3, loaded.Spine[1].Provenance.ParserSequence);
        Assert.Equal(events[0].ObservedAt, loaded.Spine[1].ObservedAt);
        Assert.Equal(900, loaded.Spine[1].Provenance.ByteStart);
        Assert.Equal(events[0].Provenance.SourceId, loaded.Spine[1].Provenance.SourceId);
        Assert.Equal(events[0].Provenance.SourceSegmentId, loaded.Spine[1].Provenance.SourceSegmentId);
        Assert.All(loaded.Spine, item => Assert.Null(item.GetType().GetProperty("RawLine")));
    }

    [Fact]
    public void Spine_bound_truncates_detail_but_keeps_complete_aggregates()
    {
        using var root = new TempRoot();
        var template = ParseOne(HotFeet);
        var engine = new CombatEngine();
        for (var index = 0; index < SegmentSpineLimits.MaxRetainedLogicalEvents + 5; index++)
        {
            engine.Apply(template with
            {
                Sequence = index + 1,
                Provenance = WithSequence(template.Provenance, index + 1)
            });
        }

        Assert.True(engine.SpineTruncated);
        Assert.Equal(SegmentSpineLimits.MaxRetainedLogicalEvents, engine.RetainedSpine.Count);
        Assert.Equal(SegmentSpineLimits.MaxRetainedLogicalEvents + 5, engine.LogicalEventsApplied);
        var expectedDamage = new CombatScaledAmount(1388L * (SegmentSpineLimits.MaxRetainedLogicalEvents + 5));
        Assert.Equal(expectedDamage, engine.Project().Session.DamageDealt);

        var loaded = Roundtrip(root.Path, engine);
        Assert.Equal(SegmentSpineLimits.MaxRetainedLogicalEvents, loaded.Spine.Count);
        Assert.True(loaded.Coverage.SpineTruncated);
        Assert.Equal(engine.LogicalEventsApplied, loaded.Coverage.LogicalEventCount);
        Assert.Equal(expectedDamage, loaded.Aggregates.Session.DamageDealt);
        Assert.True(loaded.Coverage.Replay.SessionTotals.AggregateAuthoritative);
        Assert.Equal(ReplayCoverageKind.NotRecomputable, loaded.Coverage.Replay.SessionTotals.Replay);
        Assert.Equal(ReplayCoverageKind.PartialReplay, loaded.Coverage.Replay.EventTimeline.Replay);
        Assert.False(loaded.Coverage.Replay.EventTimeline.AggregateAuthoritative);
        Assert.True(loaded.Coverage.Replay.Clock.AggregateAuthoritative);
        Assert.Equal(ReplayCoverageKind.NotRecomputable, loaded.Coverage.Replay.Clock.Replay);
        Assert.Equal(1, loaded.Spine[0].Provenance.ParserSequence);
        Assert.Equal(SegmentSpineLimits.MaxRetainedLogicalEvents, loaded.Spine[^1].Provenance.ParserSequence);
    }

    [Fact]
    public void Failed_commit_leaves_no_partial_segment_and_does_not_poison_later_saves()
    {
        using var root = new TempRoot();
        var failing = new SegmentStore(root.Path, (_, _) => throw new IOException("commit failed"));
        var failed = failing.Persist(Draft(Apply(HotFeet)));
        Assert.Equal(SegmentPersistOutcome.PersistenceFailed, failed.Outcome);
        Assert.Empty(failing.ListPublishedDirectories());
        Assert.False(Directory.Exists(ApplicationDataPaths.GetSegmentsDirectory(root.Path))
            && Directory.GetDirectories(ApplicationDataPaths.GetSegmentsDirectory(root.Path))
                .Any(path => Path.GetFileName(path).Contains(".staging-", StringComparison.Ordinal)));

        var store = new SegmentStore(root.Path);
        Assert.Equal(SegmentPersistOutcome.Persisted, store.Persist(Draft(Apply(HotFeet))).Outcome);
        Assert.Single(store.ListPublishedDirectories());
    }

    [Fact]
    public void Concurrent_distinct_segment_saves_both_succeed()
    {
        using var root = new TempRoot();
        var store = new SegmentStore(root.Path);
        var left = Draft(Apply(HotFeet));
        var right = Draft(Apply(FireCagesTick));
        var results = new SegmentPersistResult[2];
        Parallel.Invoke(
            () => results[0] = store.Persist(left),
            () => results[1] = store.Persist(right));
        Assert.All(results, result => Assert.Equal(SegmentPersistOutcome.Persisted, result.Outcome));
        Assert.Equal(2, store.ListPublishedDirectories().Count);
        AssertLoaded(store, left.GameplaySessionId);
        AssertLoaded(store, right.GameplaySessionId);
    }

    [Fact]
    public void Fresh_and_existing_application_data_create_segment_schema_non_destructively()
    {
        using var root = new TempRoot();
        var observation = new CharacterPerformanceObservation
        {
            GameplaySessionId = GameplaySessionId.CreateNew(),
            SegmentOrdinal = 0,
            CharacterRecordId = CharacterRecordId.CreateNew(),
            StartedAtUtc = new DateTimeOffset(2026, 8, 4, 12, 0, 0, TimeSpan.Zero),
            EndedAtUtc = new DateTimeOffset(2026, 8, 4, 12, 30, 0, TimeSpan.Zero),
            DamageDealt = new CombatScaledAmount(100),
            Attempts = 1,
            Hits = 1
        };
        var observations = new CharacterPerformanceObservationRepository(root.Path);
        Assert.Equal(CharacterPerformanceObservationWriteOutcome.Persisted, observations.Persist(observation).Outcome);

        var store = new SegmentStore(root.Path);
        Assert.Equal(SegmentPersistOutcome.Persisted, store.Persist(Draft(Apply(HotFeet))).Outcome);
        Assert.Equal(observation, Assert.Single(new CharacterPerformanceObservationRepository(root.Path).GetAll()));
        Assert.True(Directory.Exists(ApplicationDataPaths.GetSegmentsDirectory(root.Path)));
        Assert.True(Directory.Exists(ApplicationDataPaths.GetBuildManifestsDirectory(root.Path)));
        Assert.DoesNotContain(
            Directory.EnumerateFiles(root.Path, "*", SearchOption.AllDirectories),
            path => path.Contains("reference.db", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Staging_directories_are_ignored_and_corrupt_sidecars_degrade_without_rewriting_the_cube()
    {
        using var root = new TempRoot();
        var store = new SegmentStore(root.Path);
        var engine = new CombatEngine(ProcLogNameIndex.FromCatalog(ProductionCatalog));
        var manifest = ManifestFromLayout(SingleArmageddonLayout());
        Assert.True(engine.AttachBuildContext(manifest, AvailableContext(manifest)));
        engine.Apply(ParseOne(HotFeet));
        var draft = Draft(engine);
        var persisted = store.Persist(draft);
        Assert.Equal(SegmentPersistOutcome.Persisted, persisted.Outcome);
        var published = persisted.DirectoryPath!;
        var hashes = AssertLoaded(store, draft.GameplaySessionId).Metadata.FileHashes;

        Directory.CreateDirectory(published + ".staging-" + Guid.NewGuid().ToString("n"));
        Assert.Single(store.ListPublishedDirectories());

        File.WriteAllText(Path.Combine(published, SegmentStore.AnnotationsFileName), "{not-json");
        var degradedAnnotations = store.TryLoad(draft.GameplaySessionId, 0);
        Assert.Equal(SegmentLoadOutcome.Loaded, degradedAnnotations.Outcome);
        Assert.Null(degradedAnnotations.Segment!.Annotations.UserDisplayName);
        Assert.Contains("Annotations", degradedAnnotations.Detail, StringComparison.Ordinal);

        File.WriteAllBytes(Path.Combine(published, SegmentStore.SpineFileName), [1, 2, 3, 4]);
        var degradedSpine = store.TryLoad(draft.GameplaySessionId, 0);
        Assert.Equal(SegmentLoadOutcome.Loaded, degradedSpine.Outcome);
        Assert.Empty(degradedSpine.Segment!.Spine);
        Assert.Equal(new CombatScaledAmount(1388), degradedSpine.Segment.Aggregates.Session.DamageDealt);
        Assert.Equal(ReplayCoverageKind.NotRecomputable, degradedSpine.Segment.Coverage.Replay.EventTimeline.Replay);

        File.Delete(Path.Combine(store.ManifestsDirectory, draft.FrozenManifest!.ManifestHash + ".json"));
        var degradedManifest = store.TryLoad(draft.GameplaySessionId, 0);
        Assert.Equal(SegmentLoadOutcome.Loaded, degradedManifest.Outcome);
        Assert.Null(degradedManifest.Segment!.FrozenManifest);
        Assert.Equal(new CombatScaledAmount(1388), degradedManifest.Segment.Aggregates.Session.DamageDealt);

        Assert.Equal(SegmentPersistOutcome.Persisted, store.TryUpdateAnnotations(
            draft.GameplaySessionId,
            0,
            new SegmentAnnotations { UserDisplayName = "Farm A" }).Outcome);
        var annotated = AssertLoaded(store, draft.GameplaySessionId);
        Assert.Equal("Farm A", annotated.Annotations.UserDisplayName);
        Assert.Equal(hashes.Aggregates, annotated.Metadata.FileHashes.Aggregates);
        Assert.Equal(hashes.Coverage, annotated.Metadata.FileHashes.Coverage);
        Assert.Equal(hashes.Spine, annotated.Metadata.FileHashes.Spine);
    }

    [Fact]
    public void Approximate_segment_storage_size_is_bounded()
    {
        using var root = new TempRoot();
        var template = ParseOne(HotFeet);
        var engine = new CombatEngine();
        for (var index = 0; index < SegmentSpineLimits.MaxRetainedLogicalEvents; index++)
        {
            engine.Apply(template with
            {
                Sequence = index + 1,
                Provenance = WithSequence(template.Provenance, index + 1)
            });
        }

        var manifest = ManifestFromLayout(SingleArmageddonLayout());
        engine.AttachBuildContext(manifest, AvailableContext(manifest));
        var store = new SegmentStore(root.Path);
        var result = store.Persist(Draft(engine));
        var directory = result.DirectoryPath!;
        var aggregateBytes = new FileInfo(Path.Combine(directory, SegmentStore.AggregatesFileName)).Length;
        var spineBytes = new FileInfo(Path.Combine(directory, SegmentStore.SpineFileName)).Length;
        var manifestBytes = new FileInfo(Path.Combine(store.ManifestsDirectory, manifest.ManifestHash + ".json")).Length;
        var total = Directory.GetFiles(directory).Sum(path => new FileInfo(path).Length) + manifestBytes;
        Assert.True(aggregateBytes > 0);
        Assert.True(spineBytes > 0);
        Assert.True(manifestBytes > 0);
        _output.WriteLine(
            $"Slice 9 size sample: aggregates={aggregateBytes} spine={spineBytes} manifest={manifestBytes} total={total}");
        Assert.True(total < 2_000_000, $"Unexpected Segment size {total} bytes (aggregates {aggregateBytes}, spine {spineBytes}, manifest {manifestBytes}).");
    }

    [Fact]
    public void Catalog_json_is_unchanged_by_segment_persistence()
    {
        var catalogPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "CoHAnalytics",
            "ReferenceData",
            "item-catalog.v1.json"));
        Assert.True(File.Exists(catalogPath), catalogPath);
        var before = File.ReadAllBytes(catalogPath);
        using var root = new TempRoot();
        new SegmentStore(root.Path).Persist(Draft(Apply(HotFeet), ManifestFromLayout(SingleArmageddonLayout())));
        Assert.Equal(before, File.ReadAllBytes(catalogPath));
    }

    [Fact]
    public async Task Welcome_boundaries_persist_distinct_segments_and_recovery_does_not_duplicate()
    {
        var monitoring = new FakeMonitoringSessionManager();
        var parser = new GameplaySessionTestInfrastructure.FakeGameplayParserManager();
        var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dir);
        var store = new SegmentStore(dir);
        var context = MonitoringContextId.CreateNew();
        var source = GameplaySessionTestInfrastructure.DefaultSource();
        var segment = ParserSourceSegmentId.CreateNew();
        var buildStore = new MemoryBuildStore();
        var a = repository.EstablishTrustedFromWelcome("acct-1", "Hero A").RecordId!;
        var b = repository.EstablishTrustedFromWelcome("acct-1", "Hero B").RecordId!;
        buildStore.Save(Snapshot(a, SingleArmageddonLayout()));
        buildStore.Save(Snapshot(b, """
            Level 10: Controller_Control Fire_Control Fire_Cages
                Crafted_Armageddon_F (50)
            """));
        monitoring.SetInitial(ParserTestSnapshots.Snapshot(1, GameplaySessionTestInfrastructure.ReadyContext(context, source)));
        using var manager = await GameplaySessionTestInfrastructure.CreateStartedManager(
            monitoring,
            parser,
            repository,
            new GameplaySessionOptions { CombatSnapshotPublishInterval = TimeSpan.Zero },
            characterBuildSnapshotStore: buildStore,
            itemReferenceCatalog: ProductionCatalog,
            segmentStore: store);
        try
        {
            ParserEvent Line(string body, long seq) => GameplaySessionTestInfrastructure.Classify(
                "2026-08-04 12:00:00 " + body, context, source, seq) with
            {
                SourceSegmentId = segment,
                SourceByteStart = seq * 300,
                SourceByteEnd = seq * 300 + 200
            };

            async Task Send(ParserEvent item)
            {
                parser.PublishClassified([item]);
                await GameplaySessionTestInfrastructure.WaitForWorkQueueToDrainAsync(manager);
            }

            var welcomeA = Line("Welcome to City of Heroes, Hero A!", 1);
            await Send(welcomeA);
            var firstId = Assert.Single(manager.Current.Sessions).SessionId;
            var liveBefore = Assert.Single(manager.Current.Sessions).CombatAnalytics;
            await Send(Line("You hit Lusca with your Hot Feet for 13.88 points of Fire damage.", 2));
            var afterDamage = Assert.Single(manager.Current.Sessions).CombatAnalytics;
            Assert.Equal(new CombatScaledAmount(1388), afterDamage.Session.DamageDealt);
            Assert.Equal(liveBefore.AnalyticsSemanticVersion, afterDamage.AnalyticsSemanticVersion);

            await Send(welcomeA with { IsRecoveredWelcome = true });
            Assert.Equal(firstId, Assert.Single(manager.Current.Sessions).SessionId);
            Assert.Empty(store.ListPublishedDirectories());

            await Send(Line("Welcome to City of Heroes, Hero A!", 3));
            var secondAId = Assert.Single(manager.Current.Sessions).SessionId;
            Assert.NotEqual(firstId, secondAId);
            Assert.Single(store.ListPublishedDirectories());

            await Send(Line("Welcome to City of Heroes, Hero B!", 4));
            var secondId = Assert.Single(manager.Current.Sessions).SessionId;
            Assert.NotEqual(firstId, secondId);
            Assert.NotEqual(secondAId, secondId);
            await Send(Line("You hit Cleaner with your Fire Cages for 2.57 points of Fire damage over time.", 5));
            await Send(Line("Welcome to City of Heroes, Hero A!", 6));
            var thirdId = Assert.Single(manager.Current.Sessions).SessionId;
            Assert.NotEqual(firstId, thirdId);
            Assert.NotEqual(secondAId, thirdId);
            Assert.NotEqual(secondId, thirdId);
            await manager.StopAsync();

            Assert.Equal(4, store.ListPublishedDirectories().Count);
            var first = AssertLoaded(store, firstId);
            var secondA = AssertLoaded(store, secondAId);
            var second = AssertLoaded(store, secondId);
            var third = AssertLoaded(store, thirdId);
            Assert.Equal(new CombatScaledAmount(1388), first.Aggregates.Session.DamageDealt);
            Assert.Equal(CombatScaledAmount.Zero, secondA.Aggregates.Session.DamageDealt);
            Assert.Equal(new CombatScaledAmount(257), second.Aggregates.Session.DamageDealt);
            Assert.Equal(first.Metadata.BuildManifestHash, first.FrozenManifest!.ManifestHash);
            Assert.NotEqual(first.Metadata.BuildManifestHash, second.Metadata.BuildManifestHash);
            Assert.Equal(first.Metadata.BuildManifestHash, secondA.Metadata.BuildManifestHash);
            Assert.Equal(first.Metadata.BuildManifestHash, third.Metadata.BuildManifestHash);
            Assert.Equal(SegmentPersistOutcome.Duplicate, store.Persist(new SegmentDraft
            {
                GameplaySessionId = firstId,
                CharacterRecordId = first.Metadata.CharacterRecordId,
                AccountStableId = first.Metadata.AccountStableId,
                CaptureStartUtc = first.Metadata.CaptureStartUtc,
                CaptureEndUtc = first.Metadata.CaptureEndUtc,
                FinalizedAtUtc = first.Metadata.FinalizedAtUtc,
                Aggregates = first.Aggregates,
                Spine = first.Spine,
                FrozenManifest = first.FrozenManifest,
                Coverage = first.Coverage
            }).Outcome);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Unsupported_segment_schema_is_rejected_cleanly()
    {
        using var root = new TempRoot();
        var store = new SegmentStore(root.Path);
        var draft = Draft(Apply(HotFeet));
        Assert.Equal(SegmentPersistOutcome.Persisted, store.Persist(draft).Outcome);
        var directory = store.ListPublishedDirectories()[0];
        var metadataPath = Path.Combine(directory, SegmentStore.MetadataFileName);
        var node = JsonNode.Parse(File.ReadAllText(metadataPath))!.AsObject();
        node["segmentSchemaVersion"] = 99;
        File.WriteAllText(metadataPath, node.ToJsonString());
        var loaded = store.TryLoad(draft.GameplaySessionId, 0);
        Assert.Equal(SegmentLoadOutcome.UnsupportedSchema, loaded.Outcome);
    }

    [Fact]
    public void Clock_final_semantics_roundtrip_without_live_as_of()
    {
        using var root = new TempRoot();
        var start = new DateTimeOffset(2026, 8, 4, 12, 0, 0, TimeSpan.Zero);
        var end = start.AddMinutes(2);
        var engine = Apply(HotFeet);
        engine.Freeze();
        var projection = engine.Project(new SegmentClockCapture
        {
            CaptureStartUtc = start,
            CaptureEndUtc = end,
            AsOfUtc = end,
            TrackedPauseAdjustedDuration = TimeSpan.FromMinutes(1)
        });
        var loaded = Roundtrip(root.Path, engine, start, end);
        Assert.Equal(start, loaded.Metadata.CaptureStartUtc);
        Assert.Equal(end, loaded.Metadata.CaptureEndUtc);
        Assert.Equal(end, loaded.Aggregates.Clock.CaptureEndUtc);
        Assert.Equal(end, loaded.Aggregates.Clock.AsOfUtc);
        Assert.Equal(projection.Clock.WallClockDuration.Availability, loaded.Aggregates.Clock.WallClockDuration.Availability);
        Assert.Equal(MetricAvailability.Unsupported, loaded.Aggregates.Clock.ActiveDuration.Availability);
        Assert.Equal(RateDenominatorKind.WallClock, loaded.Aggregates.Clock.WallClockDamagePerSecondHundredths.Denominator);
        Assert.NotNull(loaded.Aggregates.Clock.FirstObservation);
        Assert.Equal(loaded.Aggregates.Clock.FirstObservation!.ParserSequence, loaded.Spine[0].Provenance.ParserSequence);
    }

    [Fact]
    public async Task Unresolved_provisional_session_combat_is_not_persisted()
    {
        var monitoring = new FakeMonitoringSessionManager();
        var parser = new GameplaySessionTestInfrastructure.FakeGameplayParserManager();
        var repository = GameplaySessionTestInfrastructure.CreateRepository(out var dir);
        var store = new SegmentStore(dir);
        var context = MonitoringContextId.CreateNew();
        var source = GameplaySessionTestInfrastructure.DefaultSource();
        var segment = ParserSourceSegmentId.CreateNew();
        monitoring.SetInitial(ParserTestSnapshots.Snapshot(1, GameplaySessionTestInfrastructure.ReadyContext(context, source)));
        using var manager = await GameplaySessionTestInfrastructure.CreateStartedManager(
            monitoring,
            parser,
            repository,
            new GameplaySessionOptions { CombatSnapshotPublishInterval = TimeSpan.Zero },
            itemReferenceCatalog: ProductionCatalog,
            segmentStore: store);
        try
        {
            // Combat with no preceding Welcome: identity never resolves, so finalization must skip
            // durable persistence rather than emit an anonymous Segment.
            var combat = GameplaySessionTestInfrastructure.Classify(
                "2026-08-04 12:00:00 You hit Lusca with your Hot Feet for 13.88 points of Fire damage.",
                context,
                source,
                1) with { SourceSegmentId = segment, SourceByteStart = 0, SourceByteEnd = 200 };
            parser.PublishClassified([combat]);
            await GameplaySessionTestInfrastructure.WaitForWorkQueueToDrainAsync(manager);
            await manager.StopAsync();

            Assert.Empty(store.ListPublishedDirectories());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(SegmentSpineLimits.MaxRetainedLogicalEvents - 1, false)]
    [InlineData(SegmentSpineLimits.MaxRetainedLogicalEvents, false)]
    [InlineData(SegmentSpineLimits.MaxRetainedLogicalEvents + 1, true)]
    public void Spine_retention_honors_first_n_boundaries(int logicalEvents, bool expectTruncated)
    {
        var template = ParseOne(HotFeet);
        var engine = new CombatEngine();
        for (var index = 0; index < logicalEvents; index++)
        {
            engine.Apply(template with
            {
                Sequence = index + 1,
                Provenance = WithSequence(template.Provenance, index + 1)
            });
        }

        var expectedRetained = Math.Min(logicalEvents, SegmentSpineLimits.MaxRetainedLogicalEvents);
        Assert.Equal(logicalEvents, engine.LogicalEventsApplied);
        Assert.Equal(expectedRetained, engine.RetainedSpine.Count);
        Assert.Equal(expectTruncated, engine.SpineTruncated);
        if (logicalEvents > 0)
        {
            // First-N: the retained window always starts at parser sequence 1.
            Assert.Equal(1, engine.RetainedSpine[0].Provenance.ParserSequence);
            Assert.Equal(expectedRetained, engine.RetainedSpine[^1].Provenance.ParserSequence);
        }
    }

    [Fact]
    public void Spine_codec_is_deterministic_and_rejects_corrupt_bytes()
    {
        var engine = Apply(HotFeet, IncomingDamage, PetBrawl);
        var spine = engine.RetainedSpine;

        var first = SegmentSpineCodec.Encode(spine);
        var second = SegmentSpineCodec.Encode(spine);
        Assert.Equal(first, second);

        var decoded = SegmentSpineCodec.Decode(first);
        Assert.Equal(spine.Count, decoded.Count);
        Assert.Equal(spine[0].Provenance.ParserSequence, decoded[0].Provenance.ParserSequence);

        Assert.Equal([], SegmentSpineCodec.Decode(SegmentSpineCodec.Encode([])));
        Assert.ThrowsAny<InvalidDataException>(() => SegmentSpineCodec.Decode([1, 2, 3, 4, 5]));
    }

    private static AnalyticalSegment Roundtrip(
        string root,
        CombatEngine engine,
        DateTimeOffset? start = null,
        DateTimeOffset? end = null)
    {
        var store = new SegmentStore(root);
        var draft = Draft(engine, start: start, end: end);
        Assert.Equal(SegmentPersistOutcome.Persisted, store.Persist(draft).Outcome);
        return AssertLoaded(store, draft.GameplaySessionId);
    }

    private static AnalyticalSegment AssertLoaded(SegmentStore store, GameplaySessionId sessionId)
    {
        var loaded = store.TryLoad(sessionId, 0);
        Assert.Equal(SegmentLoadOutcome.Loaded, loaded.Outcome);
        Assert.NotNull(loaded.Segment);
        return loaded.Segment!;
    }

    private static SegmentDraft Draft(
        CombatEngine engine,
        FrozenBuildManifest? manifest = null,
        DateTimeOffset? start = null,
        DateTimeOffset? end = null)
    {
        var captureStart = start ?? new DateTimeOffset(2026, 8, 4, 12, 0, 0, TimeSpan.Zero);
        var captureEnd = end ?? captureStart.AddMinutes(5);
        if (manifest is not null && engine.FrozenManifest is null)
        {
            engine.AttachBuildContext(manifest, AvailableContext(manifest));
        }

        engine.Freeze();
        var projection = engine.Project(new SegmentClockCapture
        {
            CaptureStartUtc = captureStart,
            CaptureEndUtc = captureEnd,
            AsOfUtc = captureEnd
        });
        var spine = engine.RetainedSpine;
        return new SegmentDraft
        {
            GameplaySessionId = GameplaySessionId.CreateNew(),
            CharacterRecordId = CharacterRecordId.CreateNew(),
            AccountStableId = "acct-1",
            CaptureStartUtc = captureStart,
            CaptureEndUtc = captureEnd,
            FinalizedAtUtc = captureEnd,
            Aggregates = projection,
            Spine = spine,
            FrozenManifest = engine.FrozenManifest ?? manifest,
            Coverage = new SegmentCoverageDescriptor
            {
                LogicalEventCount = engine.LogicalEventsApplied,
                DuplicateOccurrencesIgnored = engine.DuplicateOccurrencesIgnored,
                RetainedSpineEventCount = spine.Count,
                SpineRetentionLimit = SegmentSpineLimits.MaxRetainedLogicalEvents,
                SpineTruncated = engine.SpineTruncated,
                CoverageLimited = projection.CoverageLimited,
                Replay = LosslessReplayCoverageMatrix.ForCapture(
                    engine.SpineTruncated,
                    !engine.SpineTruncated && spine.Count == engine.LogicalEventsApplied,
                    (engine.FrozenManifest ?? manifest) is not null)
            }
        };
    }

    private static CombatEngine Apply(params string[] lines)
    {
        var engine = new CombatEngine();
        foreach (var canonical in ParseCanonical(lines))
        {
            engine.Apply(canonical);
        }

        return engine;
    }

    private static CanonicalCombatEvent ParseOne(string line) => Assert.Single(ParseCanonical(line));

    private static IReadOnlyList<CanonicalCombatEvent> ParseCanonical(params string[] lines)
    {
        var events = new List<CanonicalCombatEvent>(lines.Length);
        var contextId = MonitoringContextId.CreateNew();
        var segment = ParserSourceSegmentId.CreateNew();
        long sequence = 1;
        long byteStart = 0;
        foreach (var line in lines)
        {
            var logDate = line.StartsWith("2026-09-12 ", StringComparison.Ordinal)
                ? new DateOnly(2026, 9, 12)
                : line.StartsWith("2026-08-06 ", StringComparison.Ordinal)
                    ? new DateOnly(2026, 8, 6)
                    : new DateOnly(2026, 8, 4);
            var classified = CombatEventParserTestSupport.Classifier.Classify(new ParserRawEvent
            {
                ContextId = contextId,
                SourceId = LogSourceId.Create("acct-1", "acct-1", @"C:\fake\chatlog.txt", logDate),
                SourceSegmentId = segment,
                BindingGeneration = 1,
                SourceTransitionKind = MonitoringSourceTransitionKind.SourceAssigned,
                Sequence = sequence,
                ObservedAt = new DateTimeOffset(2026, 8, 4, 12, 0, 0, TimeSpan.Zero).AddSeconds(sequence - 1),
                RawLine = line,
                SourceByteStart = byteStart,
                SourceByteEnd = byteStart + line.Length + 2,
                LineStatus = ParserLineStatus.Complete
            });
            Assert.True(CombatEventParserTestSupport.Parser.TryParseCanonical(classified, out var canonical));
            events.Add(canonical);
            byteStart = classified.SourceByteEnd;
            sequence++;
        }

        return events;
    }

    private static EventProvenance WithSequence(EventProvenance provenance, long sequence) =>
        provenance with
        {
            ParserSequence = sequence,
            ByteStart = sequence * 100,
            ByteEnd = sequence * 100 + 50
        };

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

    private static CharacterBuildSnapshot Snapshot(CharacterRecordId recordId, string layoutText)
    {
        Assert.True(HomecomingBuildLayoutParser.TryParse(layoutText, out var layout));
        return new CharacterBuildSnapshot
        {
            CharacterRecordId = recordId,
            SyncedAtUtc = DateTimeOffset.UnixEpoch,
            Layout = layout
        };
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

    private static string SingleArmageddonLayout() => """
        Level 10: Controller_Control Fire_Control Hot_Feet
            Crafted_Armageddon_F (50)
        """;

    private sealed class MemoryBuildStore : ICharacterBuildSnapshotStore
    {
        private readonly Dictionary<Guid, CharacterBuildSnapshot> _items = [];

        public CharacterBuildSnapshotLoadResult TryLoad(CharacterRecordId characterRecordId) =>
            _items.TryGetValue(characterRecordId.Value, out var snapshot)
                ? CharacterBuildSnapshotLoadResult.Loaded("memory", snapshot)
                : CharacterBuildSnapshotLoadResult.NotFound("memory");

        public CharacterBuildSnapshotSaveResult Save(CharacterBuildSnapshot snapshot)
        {
            _items[snapshot.CharacterRecordId.Value] = snapshot;
            return CharacterBuildSnapshotSaveResult.Saved("memory");
        }
    }

    private sealed class TempRoot : IDisposable
    {
        public TempRoot()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "coh-analytics-segments",
                Guid.NewGuid().ToString("n"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { }
        }
    }
}
