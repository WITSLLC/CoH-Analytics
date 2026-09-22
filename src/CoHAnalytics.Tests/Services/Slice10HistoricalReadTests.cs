using System.Reflection;
using System.Text.Json.Nodes;
using CoHAnalytics.Models;
using CoHAnalytics.ReferenceData;
using CoHAnalytics.Services;
using CoHAnalytics.Tests.Orchestration;

namespace CoHAnalytics.Tests.Services;

/// <summary>Slice 10 historical read + legacy adapter. No UI, compare, or rebake.</summary>
public sealed class Slice10HistoricalReadTests
{
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
    public void Enumerate_zero_segments()
    {
        using var root = new TempRoot();
        var reader = new HistoricalSegmentReadService(new SegmentStore(root.Path));
        Assert.Empty(reader.ListHeaders());
    }

    [Fact]
    public void Enumerate_one_valid_segment_without_decoding_spine()
    {
        using var root = new TempRoot();
        var store = new SegmentStore(root.Path);
        var draft = Draft(Apply(HotFeet));
        Assert.Equal(SegmentPersistOutcome.Persisted, store.Persist(draft).Outcome);
        File.WriteAllBytes(
            Path.Combine(store.ListPublishedDirectories()[0], SegmentStore.SpineFileName),
            [1, 2, 3, 4]);

        var reader = new HistoricalSegmentReadService(store);
        var header = Assert.Single(reader.ListHeaders());
        Assert.Equal(SegmentCaptureKey.Format(draft.GameplaySessionId, 0), header.SegmentId);
        Assert.Equal(HistoricalCaptureKind.DurableSegment, header.CaptureKind);
        Assert.Equal(HistoricalCompatibility.AuthoritativeAggregate, header.Compatibility);
        Assert.Equal(draft.CharacterRecordId, header.CharacterRecordId);
        Assert.Equal(1, header.SegmentSchemaVersion);
        Assert.Equal(3, header.AnalyticsSemanticVersion);
        Assert.Equal(4, header.GrammarSetVersion);
        Assert.Equal(1, header.DedupPolicyVersion);
        Assert.Equal(2, header.AttributionPolicyVersion);
        Assert.Equal(1, header.SpineSchemaVersion);
        Assert.False(header.SpineTruncated);
        Assert.Equal(ReplayCoverageKind.Lossless, header.EventTimelineReplay);

        var loaded = reader.TryLoad(header.SegmentId);
        Assert.Equal(HistoricalLoadOutcome.Loaded, loaded.Outcome);
        Assert.Equal(new CombatScaledAmount(1388), loaded.Segment!.Aggregates!.Session.DamageDealt);
        Assert.Equal(HistoricalDetailStatus.NotRequested, loaded.Segment.DetailStatus);
        Assert.Empty(loaded.Segment.Spine);
    }

    [Fact]
    public void Enumerate_many_newest_first_with_stable_segment_id_tiebreak()
    {
        using var root = new TempRoot();
        var store = new SegmentStore(root.Path);
        var start = new DateTimeOffset(2026, 8, 4, 12, 0, 0, TimeSpan.Zero);
        var sameEnd = start.AddHours(2);
        PersistAt(store, start, start.AddHours(3), out var newest);
        PersistAt(store, start, sameEnd, out var left);
        PersistAt(store, start, sameEnd, out var right);
        var expectedTie = string.CompareOrdinal(
            SegmentCaptureKey.Format(left, 0),
            SegmentCaptureKey.Format(right, 0)) < 0
            ? left
            : right;
        var otherTie = expectedTie == left ? right : left;

        var headers = new HistoricalSegmentReadService(store).ListHeaders();
        Assert.Equal(3, headers.Count);
        Assert.Equal(newest, headers[0].GameplaySessionId);
        Assert.Equal(expectedTie, headers[1].GameplaySessionId);
        Assert.Equal(otherTie, headers[2].GameplaySessionId);
        Assert.True(string.CompareOrdinal(headers[1].SegmentId, headers[2].SegmentId) < 0);
    }

    [Fact]
    public void Filter_by_character_date_and_segment_id()
    {
        using var root = new TempRoot();
        var store = new SegmentStore(root.Path);
        var characterA = CharacterRecordId.CreateNew();
        var characterB = CharacterRecordId.CreateNew();
        var early = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var late = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var draftA = Draft(Apply(HotFeet), start: early, end: early.AddHours(1), character: characterA);
        var draftB = Draft(Apply(FireCagesTick), start: late, end: late.AddHours(1), character: characterB);
        Assert.Equal(SegmentPersistOutcome.Persisted, store.Persist(draftA).Outcome);
        Assert.Equal(SegmentPersistOutcome.Persisted, store.Persist(draftB).Outcome);
        var reader = new HistoricalSegmentReadService(store);

        Assert.Equal(draftA.GameplaySessionId, Assert.Single(reader.ListHeaders(new HistoricalSegmentQuery
        {
            CharacterRecordId = characterA
        })).GameplaySessionId);
        Assert.Equal(draftB.GameplaySessionId, Assert.Single(reader.ListHeaders(new HistoricalSegmentQuery
        {
            RangeStartUtc = late.AddMinutes(-1)
        })).GameplaySessionId);
        Assert.Equal(draftA.GameplaySessionId, Assert.Single(reader.ListHeaders(new HistoricalSegmentQuery
        {
            RangeEndUtc = early.AddHours(2)
        })).GameplaySessionId);
        var id = SegmentCaptureKey.Format(draftB.GameplaySessionId, 0);
        Assert.Equal(id, Assert.Single(reader.ListHeaders(new HistoricalSegmentQuery { SegmentId = id })).SegmentId);
    }

    [Fact]
    public void Listing_skips_staging_unrelated_paths_and_survives_malformed_segments()
    {
        using var root = new TempRoot();
        var store = new SegmentStore(root.Path);
        var draft = Draft(Apply(HotFeet));
        Assert.Equal(SegmentPersistOutcome.Persisted, store.Persist(draft).Outcome);
        var published = store.ListPublishedDirectories()[0];
        Directory.CreateDirectory(published + ".staging-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(Path.Combine(store.SegmentsDirectory, "not-a-segment"));
        File.WriteAllText(Path.Combine(store.SegmentsDirectory, "readme.txt"), "ignore");
        var broken = Path.Combine(
            store.SegmentsDirectory,
            SegmentCaptureKey.Format(GameplaySessionId.CreateNew(), 0));
        Directory.CreateDirectory(broken);
        File.WriteAllText(Path.Combine(broken, SegmentStore.MetadataFileName), "{not-json");

        var headers = new HistoricalSegmentReadService(store).ListHeaders();
        Assert.Single(headers);
        Assert.Equal(draft.GameplaySessionId, headers[0].GameplaySessionId);
        Assert.Equal(HistoricalCompatibility.AuthoritativeAggregate, headers[0].Compatibility);
        Assert.DoesNotContain(headers, header => header.Compatibility == HistoricalCompatibility.Corrupt);
    }

    [Fact]
    public void Unsupported_schema_is_explicit_and_does_not_use_v1_mapping()
    {
        using var root = new TempRoot();
        var store = new SegmentStore(root.Path);
        var draft = Draft(Apply(HotFeet));
        Assert.Equal(SegmentPersistOutcome.Persisted, store.Persist(draft).Outcome);
        RewriteMetadata(store, draft.GameplaySessionId, node => node["segmentSchemaVersion"] = 99);

        var reader = new HistoricalSegmentReadService(store);
        var header = Assert.Single(reader.ListHeaders());
        Assert.Equal(HistoricalCompatibility.UnsupportedSchema, header.Compatibility);
        var loaded = reader.TryLoad(draft.GameplaySessionId, 0);
        Assert.Equal(HistoricalLoadOutcome.UnsupportedSchema, loaded.Outcome);
        Assert.Null(loaded.Segment!.Aggregates);
    }

    [Fact]
    public void Future_semantic_version_is_not_treated_as_current_meaning()
    {
        using var root = new TempRoot();
        var store = new SegmentStore(root.Path);
        var draft = Draft(Apply(HotFeet));
        Assert.Equal(SegmentPersistOutcome.Persisted, store.Persist(draft).Outcome);
        RewriteMetadata(store, draft.GameplaySessionId, node => node["analyticsSemanticVersion"] = 99);

        var loaded = new HistoricalSegmentReadService(store).TryLoad(draft.GameplaySessionId, 0);
        Assert.Equal(HistoricalLoadOutcome.UnsupportedSemanticVersion, loaded.Outcome);
        Assert.Null(loaded.Segment!.Aggregates);
        Assert.Equal(99, loaded.Segment.Header.AnalyticsSemanticVersion);
    }

    [Fact]
    public void Semantic_version_3_loads_authoritative_projection_and_availability()
    {
        using var root = new TempRoot();
        var start = new DateTimeOffset(2026, 8, 4, 12, 0, 0, TimeSpan.Zero);
        var end = start.AddMinutes(5);
        var engine = Apply(
            HotFeet,
            ZeroHotFeet,
            IncomingDamage,
            PetBrawl,
            FireCagesTick,
            HastenActivated,
            HastenRecharged,
            AttackResolution);
        engine.Freeze();
        var live = engine.Project(new SegmentClockCapture
        {
            CaptureStartUtc = start,
            CaptureEndUtc = end,
            AsOfUtc = end
        });
        var incomplete = live with
        {
            Session = live.Session with
            {
                Metrics = live.Session.Metrics with
                {
                    DistinctTargetCount = Metric<long>.Incomplete(
                        1,
                        coverage: new CoverageInfo { Overflow = true, LowerBound = true }),
                    ActivationCount = Metric<long>.Available(
                        live.Session.ActivationCount,
                        MetricEvidence.DerivedFromObserved,
                        MetricConfidence.Medium)
                }
            }
        };
        var store = new SegmentStore(root.Path);
        var draft = Draft(engine, start: start, end: end);
        draft = draft with { Aggregates = incomplete };
        Assert.Equal(SegmentPersistOutcome.Persisted, store.Persist(draft).Outcome);

        var loaded = new HistoricalSegmentReadService(store).TryLoad(draft.GameplaySessionId, 0);
        Assert.Equal(HistoricalLoadOutcome.Loaded, loaded.Outcome);
        var projection = loaded.Segment!.Aggregates!;
        Assert.Equal(3, projection.AnalyticsSemanticVersion);
        Assert.Equal(incomplete.Session.DamageDealt, projection.Session.DamageDealt);
        Assert.Equal(MetricAvailability.Available, projection.Session.Metrics.DamageDealt.Availability);
        Assert.Equal(incomplete.Session.DamageDealt, projection.Session.Metrics.DamageDealt.Value);
        Assert.Equal(MetricAvailability.NotCaptured, CombatAnalyticsProjection.Empty.Session.Metrics.HealingDealt.Availability);
        Assert.Equal(incomplete.Session.Metrics.HealingDealt.Availability, projection.Session.Metrics.HealingDealt.Availability);
        Assert.Equal(MetricAvailability.Unsupported, projection.Session.Metrics.Overkill.Availability);
        Assert.Equal(MetricAvailability.Incomplete, projection.Session.Metrics.DistinctTargetCount.Availability);
        Assert.Equal(MetricConfidence.Medium, projection.Session.Metrics.ActivationCount.Confidence);
        Assert.Equal(incomplete.Session.Metrics.Accuracy.Availability, projection.Session.Metrics.Accuracy.Availability);
        Assert.Equal(incomplete.Powers.Count, projection.Powers.Count);
        Assert.Equal(incomplete.Actors.Count, projection.Actors.Count);
        Assert.Equal(incomplete.DamageTypes.Count, projection.DamageTypes.Count);
        Assert.Equal(incomplete.Targets.Count, projection.Targets.Count);
        Assert.Equal(incomplete.Clock.CaptureEndUtc, projection.Clock.CaptureEndUtc);
        Assert.Equal(incomplete.Clock.WallClockDuration.Availability, projection.Clock.WallClockDuration.Availability);
        Assert.Equal(incomplete.BuildContext.Availability, projection.BuildContext.Availability);
        Assert.Equal(incomplete.Attribution.ProcDamage.Availability, projection.Attribution.ProcDamage.Availability);
        var view = loaded.Segment.TryAsProjectionView();
        Assert.Equal(AnalyticalProjectionSourceKind.HistoricalDurable, view!.SourceKind);
        Assert.Equal(projection, view.Projection);
    }

    [Fact]
    public void Available_zero_is_preserved_from_live_projection()
    {
        using var root = new TempRoot();
        var engine = Apply(ZeroHotFeet);
        var live = engine.Project();
        Assert.Equal(CombatScaledAmount.Zero, live.Session.DamageDealt);
        Assert.Equal(MetricAvailability.Available, live.Session.Metrics.DamageDealt.Availability);
        var loaded = new HistoricalSegmentReadService(new SegmentStore(root.Path))
            .TryLoad(Persist(root.Path, engine).GameplaySessionId, 0);
        Assert.Equal(CombatScaledAmount.Zero, loaded.Segment!.Aggregates!.Session.DamageDealt);
        Assert.Equal(MetricAvailability.Available, loaded.Segment.Aggregates.Session.Metrics.DamageDealt.Availability);
    }

    [Fact]
    public void Spine_complete_truncated_and_corrupt_detail_semantics()
    {
        using var root = new TempRoot();
        var store = new SegmentStore(root.Path);
        var reader = new HistoricalSegmentReadService(store);
        var complete = Draft(Apply(HotFeet, IncomingDamage));
        Assert.Equal(SegmentPersistOutcome.Persisted, store.Persist(complete).Outcome);
        var completeLoad = reader.TryLoad(complete.GameplaySessionId, 0, new HistoricalLoadOptions { IncludeSpine = true });
        Assert.Equal(HistoricalLoadOutcome.Loaded, completeLoad.Outcome);
        Assert.Equal(HistoricalDetailStatus.AvailableComplete, completeLoad.Segment!.DetailStatus);
        Assert.Equal(2, completeLoad.Segment.Spine.Count);
        Assert.Equal(1, completeLoad.Segment.Spine[0].Provenance.ParserSequence);
        Assert.Equal(ReplayCoverageKind.Lossless, completeLoad.Segment.Coverage!.Replay.EventTimeline.Replay);

        var template = ParseOne(HotFeet);
        var truncatedEngine = new CombatEngine();
        for (var index = 0; index < SegmentSpineLimits.MaxRetainedLogicalEvents + 1; index++)
        {
            truncatedEngine.Apply(template with
            {
                Sequence = index + 1,
                Provenance = WithSequence(template.Provenance, index + 1)
            });
        }

        var truncated = Draft(truncatedEngine);
        Assert.Equal(SegmentPersistOutcome.Persisted, store.Persist(truncated).Outcome);
        var truncatedLoad = reader.TryLoad(truncated.GameplaySessionId, 0, new HistoricalLoadOptions { IncludeSpine = true });
        Assert.True(truncatedLoad.Segment!.Coverage!.SpineTruncated);
        Assert.Equal(HistoricalDetailStatus.AvailablePartial, truncatedLoad.Segment.DetailStatus);
        Assert.Equal(ReplayCoverageKind.PartialReplay, truncatedLoad.Segment.Coverage.Replay.EventTimeline.Replay);
        Assert.Equal(ReplayCoverageKind.NotRecomputable, truncatedLoad.Segment.Coverage.Replay.SessionTotals.Replay);
        Assert.True(truncatedLoad.Segment.Coverage.Replay.SessionTotals.AggregateAuthoritative);
        Assert.Equal(
            new CombatScaledAmount(1388L * (SegmentSpineLimits.MaxRetainedLogicalEvents + 1)),
            truncatedLoad.Segment.Aggregates!.Session.DamageDealt);

        var truncatedDirectory = store.ListPublishedDirectories()
            .First(path => path.Contains(truncated.GameplaySessionId.ToString(), StringComparison.Ordinal));
        File.WriteAllBytes(Path.Combine(truncatedDirectory, SegmentStore.SpineFileName), [9, 9, 9, 9]);
        var corrupt = reader.TryLoad(truncated.GameplaySessionId, 0, new HistoricalLoadOptions { IncludeSpine = true });
        Assert.Equal(HistoricalLoadOutcome.LoadedDetailDegraded, corrupt.Outcome);
        Assert.Equal(HistoricalDetailStatus.Corrupt, corrupt.Segment!.DetailStatus);
        Assert.Equal(
            new CombatScaledAmount(1388L * (SegmentSpineLimits.MaxRetainedLogicalEvents + 1)),
            corrupt.Segment.Aggregates!.Session.DamageDealt);
        Assert.Empty(corrupt.Segment.Spine);
    }

    [Fact]
    public void Spine_hash_mismatch_and_unsupported_spine_schema_degrade_detail_only()
    {
        using var root = new TempRoot();
        var store = new SegmentStore(root.Path);
        var draft = Draft(Apply(HotFeet));
        Assert.Equal(SegmentPersistOutcome.Persisted, store.Persist(draft).Outcome);
        var directory = store.ListPublishedDirectories()[0];
        var valid = File.ReadAllBytes(Path.Combine(directory, SegmentStore.SpineFileName));
        valid[valid.Length - 1] ^= 0xff;
        File.WriteAllBytes(Path.Combine(directory, SegmentStore.SpineFileName), valid);
        var mismatch = new HistoricalSegmentReadService(store).TryLoad(
            draft.GameplaySessionId,
            0,
            new HistoricalLoadOptions { IncludeSpine = true });
        Assert.Equal(HistoricalLoadOutcome.LoadedDetailDegraded, mismatch.Outcome);
        Assert.Equal(HistoricalDetailStatus.Corrupt, mismatch.Segment!.DetailStatus);
        Assert.Equal(new CombatScaledAmount(1388), mismatch.Segment.Aggregates!.Session.DamageDealt);

        using var root2 = new TempRoot();
        var store2 = new SegmentStore(root2.Path);
        var draft2 = Draft(Apply(HotFeet));
        Assert.Equal(SegmentPersistOutcome.Persisted, store2.Persist(draft2).Outcome);
        RewriteMetadata(store2, draft2.GameplaySessionId, node => node["spineSchemaVersion"] = 99);
        var unsupported = new HistoricalSegmentReadService(store2).TryLoad(
            draft2.GameplaySessionId,
            0,
            new HistoricalLoadOptions { IncludeSpine = true });
        Assert.Equal(HistoricalLoadOutcome.LoadedDetailDegraded, unsupported.Outcome);
        Assert.Equal(HistoricalDetailStatus.UnsupportedSchema, unsupported.Segment!.DetailStatus);
        Assert.Equal(new CombatScaledAmount(1388), unsupported.Segment.Aggregates!.Session.DamageDealt);
    }

    [Fact]
    public void Frozen_manifest_loads_without_current_catalog_and_degrades_independently()
    {
        using var root = new TempRoot();
        var store = new SegmentStore(root.Path);
        var engine = new CombatEngine(ProcLogNameIndex.FromCatalog(ProductionCatalog));
        var manifest = ManifestFromLayout(SingleArmageddonLayout());
        Assert.True(engine.AttachBuildContext(manifest, AvailableContext(manifest)));
        engine.Apply(ParseOne(Armageddon));
        var draft = Draft(engine);
        Assert.Equal(SegmentPersistOutcome.Persisted, store.Persist(draft).Outcome);
        var loaded = new HistoricalSegmentReadService(store).TryLoad(
            draft.GameplaySessionId,
            0,
            new HistoricalLoadOptions { IncludeManifest = true });
        Assert.Equal(HistoricalBuildContextStatus.Present, loaded.Segment!.BuildContextStatus);
        Assert.Equal(manifest.ManifestHash, loaded.Segment.FrozenManifest!.ManifestHash);
        Assert.Equal(loaded.Segment.Aggregates!.BuildContext.ManifestHash, loaded.Segment.FrozenManifest.ManifestHash);
        Assert.Equal(ProcAttributionMode.BuildConfirmed, loaded.Segment.Aggregates.Attribution.ByParent[0].Mode);
        Assert.Equal(ReplayCoverageKind.NotRecomputable, loaded.Segment.Coverage!.Replay.ProcAttribution.Replay);

        File.Delete(Path.Combine(store.ManifestsDirectory, manifest.ManifestHash + ".json"));
        var missing = new HistoricalSegmentReadService(store).TryLoad(draft.GameplaySessionId, 0);
        Assert.Equal(HistoricalLoadOutcome.LoadedDetailDegraded, missing.Outcome);
        Assert.True(missing.HasAuthoritativeAggregates);
        Assert.Equal(new CombatScaledAmount(1971), missing.Segment!.Aggregates!.Session.DamageDealt);
        // The persisted cube build-context summary is untouched; only manifest detail degrades.
        Assert.Equal(manifest.ManifestHash, missing.Segment.Aggregates.BuildContext.ManifestHash);
        Assert.Equal(MetricAvailability.Available, missing.Segment.Aggregates.BuildContext.Availability);
        Assert.Equal(HistoricalBuildContextStatus.Missing, missing.Segment.BuildContextStatus);
        Assert.Null(missing.Segment.FrozenManifest);

        using var root2 = new TempRoot();
        var store2 = new SegmentStore(root2.Path);
        var engine2 = new CombatEngine(ProcLogNameIndex.FromCatalog(ProductionCatalog));
        Assert.True(engine2.AttachBuildContext(manifest, AvailableContext(manifest)));
        engine2.Apply(ParseOne(Armageddon));
        var draft2 = Draft(engine2);
        Assert.Equal(SegmentPersistOutcome.Persisted, store2.Persist(draft2).Outcome);
        File.WriteAllText(
            Path.Combine(store2.ManifestsDirectory, manifest.ManifestHash + ".json"),
            "{not-json");
        var corrupt = new HistoricalSegmentReadService(store2).TryLoad(draft2.GameplaySessionId, 0);
        Assert.True(corrupt.HasAuthoritativeAggregates);
        Assert.Null(corrupt.Segment!.FrozenManifest);
        Assert.Equal(new CombatScaledAmount(1971), corrupt.Segment.Aggregates!.Session.DamageDealt);
    }

    [Fact]
    public void Aggregate_hash_mismatch_makes_authoritative_analytics_unavailable()
    {
        using var root = new TempRoot();
        var store = new SegmentStore(root.Path);
        var draft = Draft(Apply(HotFeet));
        Assert.Equal(SegmentPersistOutcome.Persisted, store.Persist(draft).Outcome);
        var path = Path.Combine(store.ListPublishedDirectories()[0], SegmentStore.AggregatesFileName);
        File.WriteAllText(path, File.ReadAllText(path).Replace("1388", "1", StringComparison.Ordinal));
        var loaded = new HistoricalSegmentReadService(store).TryLoad(draft.GameplaySessionId, 0);
        Assert.Equal(HistoricalLoadOutcome.Corrupt, loaded.Outcome);
        Assert.Null(loaded.Segment!.Aggregates);
    }

    [Fact]
    public void Corrupt_annotations_do_not_invalidate_immutable_segment()
    {
        using var root = new TempRoot();
        var store = new SegmentStore(root.Path);
        var draft = Draft(Apply(HotFeet));
        Assert.Equal(SegmentPersistOutcome.Persisted, store.Persist(draft).Outcome);
        File.WriteAllText(
            Path.Combine(store.ListPublishedDirectories()[0], SegmentStore.AnnotationsFileName),
            "{not-json");
        var loaded = new HistoricalSegmentReadService(store).TryLoad(draft.GameplaySessionId, 0);
        // Corrupt non-authoritative annotations degrade the summary but never invalidate the cube.
        Assert.Equal(HistoricalLoadOutcome.LoadedDetailDegraded, loaded.Outcome);
        Assert.True(loaded.HasAuthoritativeAggregates);
        Assert.Equal(new CombatScaledAmount(1388), loaded.Segment!.Aggregates!.Session.DamageDealt);
        Assert.Equal(HistoricalAnnotationStatus.Corrupt, loaded.Segment.AnnotationStatus);
    }

    [Fact]
    public void Historical_load_does_not_use_catalog_engine_or_live_session_manager()
    {
        using var root = new TempRoot();
        var catalogPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "CoHAnalytics",
            "ReferenceData",
            "item-catalog.v1.json"));
        var before = File.ReadAllBytes(catalogPath);
        var store = new SegmentStore(root.Path);
        var draft = Draft(Apply(HotFeet));
        Assert.Equal(SegmentPersistOutcome.Persisted, store.Persist(draft).Outcome);
        var reader = new HistoricalSegmentReadService(store);
        var loaded = reader.TryLoad(draft.GameplaySessionId, 0);
        Assert.Equal(new CombatScaledAmount(1388), loaded.Segment!.Aggregates!.Session.DamageDealt);
        Assert.Equal(before, File.ReadAllBytes(catalogPath));
        Assert.DoesNotContain(
            typeof(HistoricalSegmentReadService).GetFields(BindingFlags.Instance | BindingFlags.NonPublic),
            field => field.FieldType.Name.Contains("CombatEngine", StringComparison.Ordinal)
                || field.FieldType.Name.Contains("IItemReferenceCatalog", StringComparison.Ordinal)
                || field.FieldType.Name.Contains("ICharacterBuildSnapshotStore", StringComparison.Ordinal));
        Assert.DoesNotContain(
            typeof(GameplaySessionManager).GetFields(BindingFlags.Instance | BindingFlags.NonPublic),
            field => typeof(IHistoricalSegmentReader).IsAssignableFrom(field.FieldType));
    }

    [Fact]
    public void Concurrent_publish_does_not_disturb_existing_historical_read()
    {
        using var root = new TempRoot();
        var store = new SegmentStore(root.Path);
        var first = Draft(Apply(HotFeet));
        Assert.Equal(SegmentPersistOutcome.Persisted, store.Persist(first).Outcome);
        var reader = new HistoricalSegmentReadService(store);
        Exception? readError = null;
        var second = Draft(Apply(FireCagesTick));
        Parallel.Invoke(
            () =>
            {
                for (var i = 0; i < 20; i++)
                {
                    try
                    {
                        var loaded = reader.TryLoad(first.GameplaySessionId, 0);
                        Assert.Equal(new CombatScaledAmount(1388), loaded.Segment!.Aggregates!.Session.DamageDealt);
                        Assert.Contains(
                            reader.ListHeaders(),
                            header => header.GameplaySessionId == first.GameplaySessionId);
                    }
                    catch (Exception exception)
                    {
                        readError = exception;
                        throw;
                    }
                }
            },
            () => Assert.Equal(SegmentPersistOutcome.Persisted, store.Persist(second).Outcome));
        Assert.Null(readError);
        Assert.Equal(2, reader.ListHeaders().Count);
    }

    [Fact]
    public void Legacy_adapter_maps_available_axes_and_not_captured_gaps_without_current_formulas()
    {
        using var root = new TempRoot();
        var observation = new CharacterPerformanceObservation
        {
            SchemaVersion = 2,
            GameplaySessionId = GameplaySessionId.CreateNew(),
            SegmentOrdinal = 0,
            CharacterRecordId = CharacterRecordId.CreateNew(),
            StartedAtUtc = new DateTimeOffset(2026, 8, 4, 12, 0, 0, TimeSpan.Zero),
            EndedAtUtc = new DateTimeOffset(2026, 8, 4, 12, 30, 0, TimeSpan.Zero),
            DamageDealt = new CombatScaledAmount(1250),
            Attempts = 4,
            Hits = 3,
            RolledAttempts = 4,
            DisplayedChanceSumHundredths = 40000,
            RollSumHundredths = 20000,
            TotalDefeated = 2,
            MyDefeats = 1,
            ExperienceGained = 100,
            GameplayInfluenceGained = 50
        };
        var observations = new CharacterPerformanceObservationRepository(root.Path);
        Assert.True(observations.Persist(observation).IsSuccess);
        var loaded = new HistoricalSegmentReadService(new SegmentStore(root.Path), observations)
            .TryLoad(observation.GameplaySessionId, 0);
        Assert.Equal(HistoricalLoadOutcome.LoadedLegacyAdapted, loaded.Outcome);
        var projection = loaded.Segment!.Aggregates!;
        Assert.Equal(observation.DamageDealt, projection.Session.DamageDealt);
        Assert.Equal(MetricAvailability.Available, projection.Session.Metrics.DamageDealt.Availability);
        Assert.Equal(MetricAvailability.Available, projection.Session.Metrics.Accuracy.Availability);
        Assert.Equal(3, projection.Session.Accuracy.Hits);
        Assert.Equal(2, projection.Session.DefeatCount);
        Assert.Equal(MetricAvailability.NotCaptured, projection.Session.Metrics.HealingDealt.Availability);
        Assert.Equal(MetricAvailability.NotCaptured, projection.Session.Metrics.DamageReceived.Availability);
        Assert.Empty(projection.Powers);
        Assert.Empty(projection.Actors);
        Assert.Equal(MetricAvailability.NotCaptured, projection.DamageTypeBreakdown.Availability);
        Assert.Equal(MetricAvailability.NotCaptured, projection.Attribution.ProcDamage.Availability);
        Assert.Equal(MetricAvailability.NotCaptured, projection.BuildContext.Availability);
        Assert.Equal(MetricAvailability.Unsupported, projection.Session.Metrics.PermaHasten.Availability);
        Assert.Equal(ReplayCoverageKind.NotRecomputable, loaded.Segment.Coverage!.Replay.EventTimeline.Replay);
        Assert.False(loaded.Segment.Coverage.Replay.PerPowerTotals.AggregateAuthoritative);
        Assert.Equal(100, loaded.Segment.ExperienceGained.Value);
        Assert.Equal(HistoricalDetailStatus.Unavailable, loaded.Segment.DetailStatus);
        Assert.Equal(AnalyticalProjectionSourceKind.HistoricalLegacy, loaded.Segment.TryAsProjectionView()!.SourceKind);
    }

    [Fact]
    public void Durable_segment_wins_over_legacy_observation_for_the_same_capture_key()
    {
        using var root = new TempRoot();
        var store = new SegmentStore(root.Path);
        var draft = Draft(Apply(HotFeet));
        Assert.Equal(SegmentPersistOutcome.Persisted, store.Persist(draft).Outcome);
        var observations = new CharacterPerformanceObservationRepository(root.Path);
        Assert.True(observations.Persist(new CharacterPerformanceObservation
        {
            GameplaySessionId = draft.GameplaySessionId,
            SegmentOrdinal = 0,
            CharacterRecordId = draft.CharacterRecordId!,
            StartedAtUtc = draft.CaptureStartUtc,
            EndedAtUtc = draft.CaptureEndUtc,
            DamageDealt = new CombatScaledAmount(1)
        }).IsSuccess);
        var reader = new HistoricalSegmentReadService(store, observations);
        var header = Assert.Single(reader.ListHeaders());
        Assert.Equal(HistoricalCaptureKind.DurableSegment, header.CaptureKind);
        Assert.True(header.HasLegacyObservationCounterpart);
        Assert.False(header.UsedLegacyFallback);
        Assert.Equal(new CombatScaledAmount(1388), reader.TryLoad(draft.GameplaySessionId, 0).Segment!.Aggregates!.Session.DamageDealt);

        RewriteMetadata(store, draft.GameplaySessionId, node => node["segmentSchemaVersion"] = 99);
        var fallback = Assert.Single(reader.ListHeaders());
        Assert.Equal(HistoricalCaptureKind.LegacyObservation, fallback.CaptureKind);
        Assert.True(fallback.UsedLegacyFallback);
        Assert.Equal(
            new CombatScaledAmount(1),
            reader.TryLoad(draft.GameplaySessionId, 0).Segment!.Aggregates!.Session.DamageDealt);
    }

    [Fact]
    public void Delete_removes_durable_directory_and_matching_observation_and_does_not_restore_on_reopen()
    {
        using var root = new TempRoot();
        var store = new SegmentStore(root.Path);
        var observations = new CharacterPerformanceObservationRepository(root.Path);
        var kept = Draft(Apply(HotFeet));
        var deleted = Draft(Apply(FireCagesTick));
        Assert.Equal(SegmentPersistOutcome.Persisted, store.Persist(kept).Outcome);
        Assert.Equal(SegmentPersistOutcome.Persisted, store.Persist(deleted).Outcome);
        Assert.True(observations.Persist(ObservationFor(kept, experience: 100)).IsSuccess);
        Assert.True(observations.Persist(ObservationFor(deleted, experience: 900)).IsSuccess);
        var chatLog = Path.Combine(root.Path, "HomecomingAccount", "Logs", "chatlog.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(chatLog)!);
        File.WriteAllText(chatLog, "2026-08-04 12:00:00 You hit Lusca with your Hot Feet for 13.88 points of Fire damage.");
        var chatBytes = File.ReadAllBytes(chatLog);

        var reader = new HistoricalSegmentReadService(store, observations);
        Assert.Equal(SegmentDeleteOutcome.Deleted, reader.Delete(deleted.GameplaySessionId, 0).Outcome);
        Assert.Equal(SegmentDeleteOutcome.NotFound, store.Delete(deleted.GameplaySessionId, 0).Outcome);
        Assert.DoesNotContain(
            store.ListPublishedDirectories(),
            path => path.Contains(deleted.GameplaySessionId.ToString(), StringComparison.Ordinal));
        Assert.DoesNotContain(
            observations.GetAll(),
            observation => observation.GameplaySessionId == deleted.GameplaySessionId);
        Assert.Contains(
            store.ListPublishedDirectories(),
            path => path.Contains(kept.GameplaySessionId.ToString(), StringComparison.Ordinal));
        Assert.Contains(observations.GetAll(), observation => observation.GameplaySessionId == kept.GameplaySessionId);
        Assert.Equal(kept.GameplaySessionId, Assert.Single(reader.ListHeaders()).GameplaySessionId);
        Assert.Equal(chatBytes, File.ReadAllBytes(chatLog));

        var reopenedObservations = new CharacterPerformanceObservationRepository(root.Path);
        var reopened = new HistoricalSegmentReadService(new SegmentStore(root.Path), reopenedObservations);
        Assert.Equal(kept.GameplaySessionId, Assert.Single(reopened.ListHeaders()).GameplaySessionId);
        Assert.DoesNotContain(
            reopenedObservations.GetAll(),
            observation => observation.GameplaySessionId == deleted.GameplaySessionId);
        Assert.Equal(chatBytes, File.ReadAllBytes(chatLog));
    }

    [Fact]
    public void Incomplete_or_orphaned_durable_directories_are_not_listed_without_a_legacy_observation()
    {
        using var root = new TempRoot();
        var store = new SegmentStore(root.Path);
        var valid = Draft(Apply(HotFeet));
        Assert.Equal(SegmentPersistOutcome.Persisted, store.Persist(valid).Outcome);

        var emptyId = GameplaySessionId.CreateNew();
        Directory.CreateDirectory(Path.Combine(
            store.SegmentsDirectory,
            SegmentCaptureKey.Format(emptyId, 0)));

        var metadataOnlyId = GameplaySessionId.CreateNew();
        var metadataOnly = Path.Combine(
            store.SegmentsDirectory,
            SegmentCaptureKey.Format(metadataOnlyId, 0));
        Directory.CreateDirectory(metadataOnly);
        File.WriteAllText(Path.Combine(metadataOnly, SegmentStore.MetadataFileName), "{}");

        var reader = new HistoricalSegmentReadService(store);
        var header = Assert.Single(reader.ListHeaders());
        Assert.Equal(valid.GameplaySessionId, header.GameplaySessionId);
        Assert.Equal(HistoricalCompatibility.AuthoritativeAggregate, header.Compatibility);
    }

    [Fact]
    public void Incomplete_durable_directory_still_lists_matching_legacy_observation()
    {
        using var root = new TempRoot();
        var store = new SegmentStore(root.Path);
        var observations = new CharacterPerformanceObservationRepository(root.Path);
        var sessionId = GameplaySessionId.CreateNew();
        var character = CharacterRecordId.CreateNew();
        Directory.CreateDirectory(Path.Combine(
            store.SegmentsDirectory,
            SegmentCaptureKey.Format(sessionId, 0)));
        Assert.True(observations.Persist(new CharacterPerformanceObservation
        {
            GameplaySessionId = sessionId,
            SegmentOrdinal = 0,
            CharacterRecordId = character,
            StartedAtUtc = new DateTimeOffset(2026, 8, 4, 12, 0, 0, TimeSpan.Zero),
            EndedAtUtc = new DateTimeOffset(2026, 8, 4, 12, 30, 0, TimeSpan.Zero),
            ExperienceGained = 50
        }).IsSuccess);

        var header = Assert.Single(new HistoricalSegmentReadService(store, observations).ListHeaders());
        Assert.Equal(sessionId, header.GameplaySessionId);
        Assert.Equal(HistoricalCaptureKind.LegacyObservation, header.CaptureKind);
    }

    [Fact]
    public void Live_path_and_historical_path_share_projection_type_without_a_second_engine()
    {
        using var root = new TempRoot();
        var engine = Apply(HotFeet);
        var live = engine.Project();
        var loaded = new HistoricalSegmentReadService(new SegmentStore(root.Path))
            .TryLoad(Persist(root.Path, engine).GameplaySessionId, 0)
            .Segment!.TryAsProjectionView()!;
        Assert.Equal(live.Session.DamageDealt, loaded.Projection.Session.DamageDealt);
        Assert.Equal(typeof(CombatAnalyticsProjection), live.GetType());
        Assert.Equal(typeof(CombatAnalyticsProjection), loaded.Projection.GetType());
    }

    private static SegmentDraft Persist(string root, CombatEngine engine)
    {
        var store = new SegmentStore(root);
        var draft = Draft(engine);
        Assert.Equal(SegmentPersistOutcome.Persisted, store.Persist(draft).Outcome);
        return draft;
    }

    private static void PersistAt(
        SegmentStore store,
        DateTimeOffset start,
        DateTimeOffset end,
        out GameplaySessionId sessionId)
    {
        var draft = Draft(Apply(HotFeet), start: start, end: end);
        Assert.Equal(SegmentPersistOutcome.Persisted, store.Persist(draft).Outcome);
        sessionId = draft.GameplaySessionId;
    }

    private static CharacterPerformanceObservation ObservationFor(SegmentDraft draft, long experience) =>
        new()
        {
            GameplaySessionId = draft.GameplaySessionId,
            SegmentOrdinal = draft.SegmentOrdinal,
            CharacterRecordId = draft.CharacterRecordId!,
            StartedAtUtc = draft.CaptureStartUtc,
            EndedAtUtc = draft.CaptureEndUtc,
            ExperienceGained = experience
        };

    private static void RewriteMetadata(
        SegmentStore store,
        GameplaySessionId sessionId,
        Action<JsonObject> mutate)
    {
        var directory = store.ListPublishedDirectories()
            .Single(path => path.Contains(sessionId.ToString(), StringComparison.Ordinal));
        var metadataPath = Path.Combine(directory, SegmentStore.MetadataFileName);
        var node = JsonNode.Parse(File.ReadAllText(metadataPath))!.AsObject();
        mutate(node);
        File.WriteAllText(metadataPath, node.ToJsonString());
    }

    private static SegmentDraft Draft(
        CombatEngine engine,
        FrozenBuildManifest? manifest = null,
        DateTimeOffset? start = null,
        DateTimeOffset? end = null,
        CharacterRecordId? character = null)
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
            CharacterRecordId = character ?? CharacterRecordId.CreateNew(),
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

    [Fact]
    public void Spine_codec_rejects_more_than_retention_bound_events()
    {
        var template = PersistedSpineEvent.FromLogical(ParseOne(HotFeet), attribution: null);
        var events = Enumerable.Range(0, SegmentSpineLimits.MaxRetainedLogicalEvents + 1)
            .Select(i => template with { Sequence = i })
            .ToArray();
        var encoded = SegmentSpineCodec.Encode(events);
        var ex = Assert.Throws<InvalidDataException>(() => SegmentSpineCodec.Decode(encoded));
        Assert.Contains("retention bound", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Spine_codec_enforces_decompression_bound_during_decode()
    {
        // A valid gzip stream that decompresses far past 8 MB must be stopped mid-stream,
        // not fully allocated. 24 MB of a single repeated byte compresses to a tiny payload.
        using var buffer = new MemoryStream();
        using (var gzip = new System.IO.Compression.GZipStream(
            buffer, System.IO.Compression.CompressionLevel.SmallestSize, leaveOpen: true))
        {
            var chunk = new byte[64 * 1024];
            for (var written = 0; written < 24 * 1024 * 1024; written += chunk.Length)
            {
                gzip.Write(chunk, 0, chunk.Length);
            }
        }

        var ex = Assert.Throws<InvalidDataException>(() => SegmentSpineCodec.Decode(buffer.ToArray()));
        Assert.Contains("decompression bound", ex.Message, StringComparison.Ordinal);
    }

    private sealed class TempRoot : IDisposable
    {
        public TempRoot()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "coh-analytics-historical",
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
