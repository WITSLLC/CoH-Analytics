using System.Reflection;
using System.Text.Json;
using CoHAnalytics.Models;
using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Services;

/// <summary>Slice 12 diagnostics and fixture hardening. Observation only; no UI or rebake.</summary>
public sealed class Slice12PipelineDiagnosticsTests
{
    private const string HotFeet =
        "2026-08-04 12:00:00 You hit Lusca with your Hot Feet for 13.88 points of Fire damage.";
    private const string PetBrawl =
        "2026-09-12 05:38:47 Imp:  You hit Cleaner with your Brawl for 15.61 points of Fire damage over time.";
    private const string YouTake =
        "2026-08-04 12:00:00 You take 12 points of Toxic damage from Poison Gas.";
    private const string Armageddon =
        "2026-09-12 05:38:44 You hit Dismantler with your Armageddon: Chance for Fire Damage for 19.71 points of Fire damage.";

    [Fact]
    public void Independent_goldens_still_cover_every_CombatGrammarId_exactly_once()
    {
        var covered = Slice2CanonicalFieldGoldenTests.CoveredGrammarIds
            .Concat(Slice3PlayerCombatGrammarTests.CoveredGrammarIds)
            .Concat(Slice4PlayerPetGrammarTests.CoveredGrammarIds)
            .Concat(Slice5APowerLifecycleTests.Goldens.Select(row => row.GrammarId))
            .ToArray();
        Assert.Equal(covered.Length, covered.Distinct().Count());
        Assert.Equal(Enum.GetValues<CombatGrammarId>().Order().ToArray(), covered.Order().ToArray());
    }

    [Fact]
    public void Pet_canonical_success_is_not_legacy_combat_shaped_and_not_canonical_unparsed()
    {
        var pet = CombatEventParserTestSupport.Classify(PetBrawl);
        Assert.True(CombatEventParserTestSupport.Parser.TryParseCanonical(pet, out var canonical));
        Assert.Equal(ActorType.OwnPet, canonical.Actor.Type);
        Assert.False(CombatEventParserTestSupport.Parser.TryParse(pet, out _));
        Assert.False(CombatEventParser.IsCombatShapedUnparsed(pet));
        Assert.False(CombatEventParser.IsCanonicalCombatUnparsed(pet));
    }

    [Fact]
    public void Same_surfaced_pet_name_does_not_become_self()
    {
        var player = ParseOne("2026-09-12 05:38:47 You hit Cleaner with your Brawl for 15.61 points of Fire damage over time.");
        var pet = ParseOne(PetBrawl);
        Assert.Equal(ActorType.Self, player.Actor.Type);
        Assert.Equal(ActorType.OwnPet, pet.Actor.Type);
        Assert.Equal("Imp", pet.Actor.DisplayName);
        Assert.NotEqual(player.Actor.Type, pet.Actor.Type);
        var engine = new CombatEngine();
        engine.Apply(pet);
        Assert.True(CombatPipelineDiagnosticsFactory.Observe([], projection: engine.Project()).PetNameRollup);
    }

    [Fact]
    public void You_take_remains_canonical_unparsed_and_is_not_zero_damage()
    {
        var input = CombatEventParserTestSupport.Classify(YouTake);
        Assert.True(CombatEventParser.IsCanonicalCombatUnparsed(input));
        Assert.True(CombatEventParser.IsCombatShapedUnparsed(input));
        Assert.False(CombatEventParserTestSupport.Parser.TryParseCanonical(input, out _));
        var empty = new CombatEngine().Project();
        Assert.Equal(MetricAvailability.NotCaptured, empty.Session.Metrics.DamageReceived.Availability);
        Assert.Null(empty.Session.Metrics.DamageReceived.Value);
    }

    [Fact]
    public void Negative_fixture_lines_do_not_broaden_grammar_acceptance()
    {
        var lines = ReadFixture("slice12-negative-2026-08-04.tsv");
        Assert.Equal(6, lines.Count);
        var classified = lines.Select(line => CombatEventParserTestSupport.Classify(line.RawLine, sequence: line.SourceLine)).ToArray();
        var expectedCanonicalUnparsed = new[] { false, true, false, false, false, false };
        for (var index = 0; index < classified.Length; index++)
        {
            Assert.False(CombatEventParserTestSupport.Parser.TryParseCanonical(classified[index], out _));
            Assert.Equal(expectedCanonicalUnparsed[index], CombatEventParser.IsCanonicalCombatUnparsed(classified[index]));
        }

        Assert.False(GrammarMatcher.TryMatch("The Hot Feet scorches you for 12 points of Fire damage!", out _));
        var diagnostics = CombatPipelineDiagnosticsFactory.Observe(classified);
        Assert.True(diagnostics.CanonicalUnparsedCount >= 1);
        Assert.DoesNotContain(diagnostics.CanonicalUnparsedSamples, sample => sample.SanitizedBody.Contains("C:\\", StringComparison.Ordinal));
        Assert.DoesNotContain(diagnostics.CanonicalUnparsedSamples, sample => sample.SanitizedBody.Contains("accounts", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Sanitize_replaces_paths_and_bounds_sample_length()
    {
        var sanitized = CombatPipelineDiagnosticsFactory.SanitizeBody(
            @"You hit Lusca C:\CoH\accounts\secret\chatlog.txt " + new string('x', 200));
        Assert.DoesNotContain(@"C:\", sanitized, StringComparison.Ordinal);
        Assert.Contains("[path]", sanitized, StringComparison.Ordinal);
        Assert.True(sanitized.Length <= CombatPipelineDiagnostics.MaxSampleBodyChars);
    }

    [Theory]
    [InlineData("You take a walk after patrol.")]
    [InlineData("You healed the relationship.")]
    [InlineData("You missed the meeting.")]
    public void Ordinary_prose_is_not_a_canonical_candidate_or_event(string body)
    {
        var input = CombatEventParserTestSupport.Classify("2026-08-04 12:00:00 " + body);
        Assert.False(CombatEventParserTestSupport.Parser.TryParseCanonical(input, out _));
        Assert.False(CombatEventParser.IsCanonicalCombatUnparsed(input));
    }

    [Theory]
    [InlineData(@"C:\Program Files\CoH\accounts\secret user\chatlog.txt")]
    [InlineData(@"c:/Program Files/CoH/accounts/secret user/chatlog.txt")]
    [InlineData(@"\\server\private share\accounts\secret user\chatlog.txt")]
    [InlineData("\"C:\\Program Files\\CoH\\accounts\\secret user\\chatlog.txt\"")]
    [InlineData(@"C:\")]
    public void Sanitize_removes_supported_path_forms_before_truncation(string path)
    {
        var sanitized = CombatPipelineDiagnosticsFactory.SanitizeBody(
            "You take 12 points from " + path + " " + new string('x', 200));
        Assert.Contains("[path]", sanitized, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Program Files", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("server", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.True(sanitized.Length <= CombatPipelineDiagnostics.MaxSampleBodyChars);
    }

    [Fact]
    public void Samples_and_observed_channels_are_bounded_before_retention()
    {
        var inputs = Enumerable.Range(0, 24)
            .Select(index => CombatEventParserTestSupport.Classify(
                $"2026-08-04 12:00:00 You take {index} points of Toxic damage from malformed source {index}.",
                sequence: index + 1) with
            {
                SourceChannel = "Channel-" + index + new string('x', 100)
            })
            .ToArray();
        var diagnostics = CombatPipelineDiagnosticsFactory.Observe(inputs);
        Assert.Equal(CombatPipelineDiagnostics.MaxUnparsedSamples, diagnostics.CanonicalUnparsedSamples.Count);
        Assert.All(diagnostics.CanonicalUnparsedSamples,
            sample => Assert.True(sample.SanitizedBody.Length <= CombatPipelineDiagnostics.MaxSampleBodyChars));
        Assert.Equal(CombatPipelineDiagnostics.MaxObservedSourceChannels, diagnostics.ObservedSourceChannels.Count);
        Assert.All(diagnostics.ObservedSourceChannels,
            channel => Assert.True(channel.Length <= CombatPipelineDiagnostics.MaxSourceChannelChars));
        Assert.Equal(diagnostics.ObservedSourceChannels.Order(StringComparer.Ordinal), diagnostics.ObservedSourceChannels);
    }

    [Fact]
    public void Max_channel_fixture_canary_is_deterministic_and_ignores_runtime_observation_time()
    {
        var events = ParseFixtureCanonical("max-channel-2026-09-12.tsv");
        Assert.NotEmpty(events);
        var first = CanonicalSemanticDigest.Hash(events);
        Assert.Equal("14f90002df4304ca118c5aa312ce53883670c4fd0b241c4fa6a77e386f201869", first);
        var shifted = events.Select(item => item with
        {
            Sequence = item.Sequence + 1000,
            ObservedAt = item.ObservedAt.AddHours(3),
            Provenance = item.Provenance with
            {
                ParserSequence = item.Provenance.ParserSequence + 1000,
                ByteStart = item.Provenance.ByteStart + 5000,
                ByteEnd = item.Provenance.ByteEnd + 5000,
                ObservedAt = item.Provenance.ObservedAt.AddHours(3),
                ContextId = MonitoringContextId.CreateNew()
            }
        }).ToArray();
        Assert.Equal(first, CanonicalSemanticDigest.Hash(shifted));
        Assert.Equal(64, first.Length);
        Assert.Equal(first, CanonicalSemanticDigest.Hash(ParseFixtureCanonical("max-channel-2026-09-12.tsv")));
    }

    [Fact]
    public void Semantic_digest_changes_for_semantics_and_length_prefixes_delimiter_content()
    {
        var baseline = ParseOne(HotFeet);
        Assert.NotEqual(
            CanonicalSemanticDigest.Hash([baseline]),
            CanonicalSemanticDigest.Hash([baseline with { PowerName = baseline.PowerName + " changed" }]));

        var first = baseline with
        {
            Actor = ActorRef.OwnPet("A|B", new PetInstanceKey
            {
                NormalizedPetName = "C",
                CoverageLimited = true
            })
        };
        var second = baseline with
        {
            Actor = ActorRef.OwnPet("A", new PetInstanceKey
            {
                NormalizedPetName = "B|C",
                CoverageLimited = true
            })
        };
        Assert.NotEqual(CanonicalSemanticDigest.Hash([first]), CanonicalSemanticDigest.Hash([second]));
    }

    [Fact]
    public void Max_channel_pipeline_diagnostics_report_channels_held_mirrors_and_unparsed_rate()
    {
        var classified = ClassifyFixture("max-channel-2026-09-12.tsv");
        var canonical = new List<CanonicalCombatEvent>();
        foreach (var line in classified)
        {
            if (CombatEventParserTestSupport.Parser.TryParseCanonical(line, out var parsed))
            {
                canonical.Add(parsed);
            }
        }

        var dedup = new Deduplicator(MirrorCompatibilityPolicy.Version1).Deduplicate(canonical);
        var engine = new CombatEngine();
        foreach (var item in dedup.LogicalEvents)
        {
            engine.Apply(item);
        }

        var projection = engine.Project();
        var beforeDamage = projection.Session.DamageDealt;
        var diagnostics = CombatPipelineDiagnosticsFactory.Observe(classified, dedup.Diagnostics, projection);
        CombatPipelineDiagnosticsFactory.Observe(classified, dedup.Diagnostics, projection);
        Assert.Equal(beforeDamage, projection.Session.DamageDealt);
        Assert.Equal(classified.Count, diagnostics.ClassifiedLineCount);
        Assert.Equal(canonical.Count, diagnostics.CanonicalParsedCount);
        Assert.Equal(0, diagnostics.Dedup.MirrorCollapsedCount);
        Assert.Equal(0, diagnostics.Dedup.MirrorCandidatesHeldCount);
        Assert.Contains("NPC", diagnostics.ObservedSourceChannels);
        Assert.Contains("Team", diagnostics.ObservedSourceChannels);
        Assert.Equal(0, diagnostics.Attribution.Correlated);
        Assert.True(diagnostics.CanonicalUnparsedCount < diagnostics.ClassifiedLineCount);
        Assert.True(diagnostics.CanonicalUnparsedSamples.Count <= CombatPipelineDiagnostics.MaxUnparsedSamples);
        Assert.Equal(AnalyticsSemanticVersion.Current, diagnostics.AnalyticsSemanticVersion);
        Assert.Equal(EventProvenance.CurrentGrammarSetVersion, diagnostics.GrammarSetVersion);
        Assert.Equal(DedupPolicyVersion.Current, diagnostics.DedupPolicyVersion);
        Assert.Equal(AttributionPolicyVersion.Current, diagnostics.AttributionPolicyVersion);
        Assert.Equal(SegmentSchemaVersion.Current, diagnostics.SegmentSchemaVersion);
        Assert.Equal(SpineSchemaVersion.Current, diagnostics.SpineSchemaVersion);
        Assert.Equal(3, diagnostics.AnalyticsSemanticVersion);
        Assert.Equal(4, diagnostics.GrammarSetVersion);
        Assert.Equal(1, diagnostics.DedupPolicyVersion);
        Assert.Equal(1, diagnostics.AttributionPolicyVersion);
        Assert.Equal(1, diagnostics.SegmentSchemaVersion);
        Assert.Equal(1, diagnostics.SpineSchemaVersion);
    }

    [Fact]
    public void Held_mirror_diagnostics_are_copied_without_collapsing()
    {
        var healDealt = ParseOne("2026-09-12 05:36:59 You heal Hero_A with Transfusion for 421.34 health points.");
        var healReceived = ParseOne("2026-09-12 05:36:59 Hero_A heals you with their Transfusion for 421.34 health points.");
        healReceived = healReceived with
        {
            Sequence = 2,
            Provenance = healDealt.Provenance with
            {
                ParserSequence = 2,
                ByteStart = 80,
                ByteEnd = 160
            }
        };
        var rule = new MirrorCompatibilityRule
        {
            RuleId = "slice12-heal-held",
            LeftFamily = CombatEventFamily.HealDealt,
            RightFamily = CombatEventFamily.HealReceived,
            RequiredDiscriminator = MirrorDiscriminatorKind.ActualSourceChannel,
            LeftChannel = "TestDelivery",
            RightChannel = "TestReceipt",
            MaxSequenceDistance = 2,
            FacetUnion = EventFacets.HealDelivered | EventFacets.HealReceived
        };
        var result = new Deduplicator(MirrorCompatibilityPolicy.ForTests(rule)).Deduplicate([healDealt, healReceived]);
        Assert.Equal(2, result.LogicalEvents.Count());
        Assert.Equal(0, result.Diagnostics.MirrorCollapsedCount);
        Assert.True(result.Diagnostics.MirrorCandidatesHeldCount >= 1);
        Assert.All(result.LogicalEvents, item => Assert.Null(item.DuplicateOf));
        var diagnostics = CombatPipelineDiagnosticsFactory.Observe([], result.Diagnostics);
        Assert.Equal(result.Diagnostics.MirrorCollapsedCount, diagnostics.Dedup.MirrorCollapsedCount);
        Assert.Equal(result.Diagnostics.MirrorCandidatesHeldCount, diagnostics.Dedup.MirrorCandidatesHeldCount);
        Assert.Equal(result.Diagnostics.HeldByFamily.Values.Sum(), diagnostics.Dedup.HeldByFamily.Values.Sum());
    }

    [Fact]
    public void Proven_mirror_collapse_diagnostics_are_copied_without_invoking_dedup()
    {
        var dealt = WithChannel(
            ParseOne("2026-09-12 05:36:59 You heal Hero_A with Transfusion for 421.34 health points."),
            "TestDelivery");
        var receivedBase = WithChannel(
            ParseOne("2026-09-12 05:36:59 Hero_A heals you with their Transfusion for 421.34 health points."),
            "TestReceipt");
        var received = receivedBase with
        {
            Sequence = 2,
            Provenance = dealt.Provenance with
            {
                ParserSequence = 2,
                ByteStart = 80,
                ByteEnd = 160,
                SourceChannel = "TestReceipt"
            }
        };
        var rule = new MirrorCompatibilityRule
        {
            RuleId = "slice12-heal-collapse",
            LeftFamily = CombatEventFamily.HealDealt,
            RightFamily = CombatEventFamily.HealReceived,
            RequiredDiscriminator = MirrorDiscriminatorKind.ActualSourceChannel,
            LeftChannel = "TestDelivery",
            RightChannel = "TestReceipt",
            MaxSequenceDistance = 2,
            FacetUnion = EventFacets.HealDelivered | EventFacets.HealReceived
        };
        var result = new Deduplicator(MirrorCompatibilityPolicy.ForTests(rule)).Deduplicate([dealt, received]);
        Assert.Single(result.LogicalEvents);
        Assert.Equal(1, result.Diagnostics.MirrorCollapsedCount);

        var diagnostics = CombatPipelineDiagnosticsFactory.Observe([], result.Diagnostics);
        Assert.Equal(1, diagnostics.Dedup.MirrorCollapsedCount);
        Assert.Equal(result.Diagnostics.CollapsedByRule, diagnostics.Dedup.CollapsedByRule);
    }

    [Fact]
    public void Recharge_without_activation_stays_unmatched_and_intervals_stay_uncaptured()
    {
        var engine = new CombatEngine();
        engine.Apply(ParseOne("2026-09-12 05:25:08 Hasten is recharged."));
        var projection = engine.Project();
        Assert.Equal(1, projection.Session.UnmatchedRechargeCandidateCount);
        Assert.Equal(0, projection.Session.ConfirmedRechargeCompletedCount);
        Assert.Equal(MetricAvailability.NotCaptured, projection.Session.Metrics.ObservedActivationToRechargeInterval.Availability);
        Assert.Equal(MetricAvailability.Unsupported, projection.Session.Metrics.TheoreticalRecharge.Availability);
        var coverage = new SegmentCoverageDescriptor
        {
            LogicalEventCount = 1,
            DuplicateOccurrencesIgnored = 0,
            RetainedSpineEventCount = 1,
            SpineRetentionLimit = 1024,
            SpineTruncated = true,
            Replay = LosslessReplayCoverageMatrix.ForCapture(
                spineTruncated: true,
                allLogicalEventsRetained: false,
                frozenBuildPersisted: false)
        };
        var diagnostics = CombatPipelineDiagnosticsFactory.Observe([], projection: projection, coverage: coverage);
        var replay = diagnostics.Replay;
        Assert.NotNull(replay);
        Assert.Equal(coverage.Replay, replay);
        Assert.Equal(ReplayCoverageKind.PartialReplay, replay.EventTimeline.Replay);
        Assert.Equal(0, diagnostics.CanonicalParsedCount);
    }

    [Theory]
    [InlineData("The battery is recharged.")]
    [InlineData("The battery is still recharging.")]
    public void Recharge_shaped_prose_remains_an_unconfirmed_candidate(string body)
    {
        var input = CombatEventParserTestSupport.Classify("2026-09-12 05:25:08 " + body);
        Assert.True(CombatEventParserTestSupport.Parser.TryParseCanonical(input, out var candidate));
        Assert.False(candidate.IsPlayerPowerActivationEvidence);
        var engine = new CombatEngine();
        engine.Apply(candidate);
        var projection = engine.Project();
        Assert.Equal(1, projection.Session.UnmatchedRechargeCandidateCount);
        Assert.Equal(0, projection.Session.ConfirmedRechargeCompletedCount);
        Assert.Equal(MetricAvailability.NotCaptured, projection.Session.Metrics.ObservedActivationToRechargeInterval.Availability);
    }

    [Fact]
    public void Mez_is_canonical_only_and_is_not_counted_as_combat_shaped_unparsed()
    {
        var mez = CombatEventParserTestSupport.Classify(
            "2026-09-12 05:36:34 You Stun Sweeper with your Pyronic Radial Final Judgement.");
        Assert.True(CombatEventParserTestSupport.Parser.TryParseCanonical(mez, out var canonical));
        Assert.Equal(CombatGrammarId.Mez01YouStatusTargetWithPower, canonical.GrammarId);
        Assert.False(CombatEventParserTestSupport.Parser.TryParse(mez, out _));
        Assert.False(CombatEventParser.IsCombatShapedUnparsed(mez));
        Assert.False(CombatEventParser.IsCanonicalCombatUnparsed(mez));
    }

    [Fact]
    public void Known_catalog_proc_without_frozen_build_is_unattributed_and_never_correlated()
    {
        var engine = new CombatEngine();
        engine.Apply(ParseOne(Armageddon));
        var projection = engine.Project();
        var diagnostics = CombatPipelineDiagnosticsFactory.Observe(
            [CombatEventParserTestSupport.Classify(Armageddon)],
            projection: projection);
        Assert.Equal(0, diagnostics.Attribution.Direct);
        Assert.Equal(0, diagnostics.Attribution.BuildConfirmed);
        Assert.Equal(0, diagnostics.Attribution.Correlated);
        Assert.Equal(1, diagnostics.Attribution.Unattributed);
        Assert.Equal(MetricAvailability.NotCaptured, projection.BuildContext.Availability);
        Assert.DoesNotContain(projection.Attribution.ByParent, row => row.Mode == ProcAttributionMode.Correlated);
        Assert.DoesNotContain(projection.Attribution.ByParent, row => row.Mode == ProcAttributionMode.Direct);
    }

    [Fact]
    public void Target_overflow_uses_IsOverflow_not_literal_Other_name()
    {
        var template = ParseOne(HotFeet);
        var engine = new CombatEngine();
        engine.Apply(template with { Target = ActorRef.UnknownNamed("Other") });
        for (var index = 0; index < CombatEngine.MaxTrackedTargets; index++)
        {
            engine.Apply(template with { Target = ActorRef.UnknownNamed("Target " + index) });
        }

        var projection = engine.Project();
        var diagnostics = CombatPipelineDiagnosticsFactory.Observe(
            [CombatEventParserTestSupport.Classify(HotFeet)],
            projection: projection);
        Assert.True(diagnostics.TargetOverflow);
        Assert.True(diagnostics.TargetLowerBound);
        Assert.Contains(projection.Targets, row => row.NormalizedTargetName == "Other" && !row.IsOverflow);
        Assert.Contains(projection.Targets, row => row.IsOverflow);
        Assert.NotEqual(
            projection.Targets.Count(row => row.NormalizedTargetName == "Other"),
            projection.Targets.Count(row => row.IsOverflow));
    }

    [Fact]
    public void Missing_damage_type_is_incomplete_not_zero_and_is_diagnosed()
    {
        var typed = ParseOne(HotFeet);
        var engine = new CombatEngine();
        engine.Apply(typed with { DamageType = null });
        var projection = engine.Project();
        var diagnostics = CombatPipelineDiagnosticsFactory.Observe(
            [CombatEventParserTestSupport.Classify(HotFeet)],
            projection: projection);
        Assert.True(diagnostics.MissingOutgoingDamageType);
        Assert.Equal(MetricAvailability.Incomplete, projection.DamageTypeBreakdown.Availability);
        Assert.Empty(projection.DamageTypes);
        Assert.Equal(MetricAvailability.Available, projection.Session.Metrics.DamageDealt.Availability);
    }

    [Fact]
    public void Incoming_missing_damage_type_is_copied_independently()
    {
        var incoming = ParseOne("2026-09-12 05:36:35 Ally_B hits you with their Particle Burst for 31.64 points of Energy damage.");
        var engine = new CombatEngine();
        engine.Apply(incoming with { DamageType = null });
        var projection = engine.Project();
        var diagnostics = CombatPipelineDiagnosticsFactory.Observe([], projection: projection);
        Assert.True(diagnostics.MissingIncomingDamageType);
        Assert.False(diagnostics.MissingOutgoingDamageType);
        Assert.Equal(MetricAvailability.Incomplete, projection.IncomingDamageTypeBreakdown.Availability);
    }

    [Fact]
    public void Observation_does_not_mutate_parser_results_or_projection()
    {
        var classified = CombatEventParserTestSupport.Classify(HotFeet);
        Assert.True(CombatEventParserTestSupport.Parser.TryParseCanonical(classified, out var beforeEvent));
        var engine = new CombatEngine();
        engine.Apply(beforeEvent);
        var projection = engine.Project();
        var beforeProjection = JsonSerializer.Serialize(projection);

        CombatPipelineDiagnosticsFactory.Observe([classified], projection: projection);

        Assert.True(CombatEventParserTestSupport.Parser.TryParseCanonical(classified, out var afterEvent));
        Assert.Equal(beforeEvent, afterEvent);
        Assert.Equal(beforeProjection, JsonSerializer.Serialize(projection));
    }

    [Fact]
    public void Semantic_mismatch_comparison_still_has_no_numeric_delta()
    {
        var left = new CombatEngine();
        left.Apply(ParseOne(HotFeet));
        var right = left.Project() with { AnalyticsSemanticVersion = 2 };
        var comparison = new ComparisonEngine().Compare(left.Project(), right);
        Assert.Equal(AnalyticalComparisonCompatibility.IncompatibleSemanticVersion, comparison.Compatibility);
        Assert.Null(comparison.Session.DamageDealt.AbsoluteDelta.Value);
    }

    [Fact]
    public void Factory_has_no_catalog_engine_or_store_fields()
    {
        var fields = typeof(CombatPipelineDiagnosticsFactory)
            .GetFields(BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.DoesNotContain(fields, field =>
            field.FieldType.Name.Contains("CombatEngine", StringComparison.Ordinal)
            || field.FieldType.Name.Contains("IItemReferenceCatalog", StringComparison.Ordinal)
            || field.FieldType.Name.Contains("ISegmentStore", StringComparison.Ordinal));
    }

    [Fact]
    public void Catalog_bytes_are_unchanged_by_observation()
    {
        var catalogPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..",
            "CoHAnalytics", "ReferenceData", "item-catalog.v1.json"));
        var before = File.ReadAllBytes(catalogPath);
        CombatPipelineDiagnosticsFactory.Observe([CombatEventParserTestSupport.Classify(HotFeet)]);
        Assert.Equal(before, File.ReadAllBytes(catalogPath));
    }

    private static CanonicalCombatEvent ParseOne(string line)
    {
        var input = CombatEventParserTestSupport.Classify(line);
        Assert.True(CombatEventParserTestSupport.Parser.TryParseCanonical(input, out var canonical));
        return canonical;
    }

    private static CanonicalCombatEvent WithChannel(CanonicalCombatEvent item, string channel) => item with
    {
        SourceChannel = channel,
        MirrorClass = item.MirrorClass with { SourceChannel = channel },
        Provenance = item.Provenance with { SourceChannel = channel }
    };

    private static IReadOnlyList<CanonicalCombatEvent> ParseFixtureCanonical(string fileName)
    {
        var events = new List<CanonicalCombatEvent>();
        foreach (var line in ClassifyFixture(fileName))
        {
            if (CombatEventParserTestSupport.Parser.TryParseCanonical(line, out var canonical))
            {
                events.Add(canonical);
            }
        }

        return events;
    }

    private static IReadOnlyList<ParserEvent> ClassifyFixture(string fileName) =>
        ReadFixture(fileName)
            .Select(line => CombatEventParserTestSupport.Classify(line.RawLine, sequence: line.SourceLine))
            .ToArray();

    private static IReadOnlyList<(int SourceLine, string RawLine)> ReadFixture(string fileName) =>
        File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Combat", fileName))
            .Select(line =>
            {
                var tab = line.IndexOf('\t');
                return (int.Parse(line[..tab]), line[(tab + 1)..]);
            })
            .ToArray();
}
