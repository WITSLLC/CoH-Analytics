using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

/// <summary>
/// Enumerates and loads historical captures from published Segments plus legacy observations.
/// One visible capture per <c>(GameplaySessionId, SegmentOrdinal)</c>: a validated durable
/// Segment wins; an unpublished or invalid Segment falls back to the matching observation.
/// </summary>
public sealed class HistoricalSegmentReadService : IHistoricalSegmentReader
{
    private readonly ISegmentStore _segmentStore;
    private readonly ICharacterPerformanceObservationRepository? _observationRepository;
    private readonly ICharacterRepository? _characterRepository;

    public HistoricalSegmentReadService(
        ISegmentStore segmentStore,
        ICharacterPerformanceObservationRepository? observationRepository = null,
        ICharacterRepository? characterRepository = null)
    {
        _segmentStore = segmentStore ?? throw new ArgumentNullException(nameof(segmentStore));
        _observationRepository = observationRepository;
        _characterRepository = characterRepository;
    }

    public IReadOnlyList<HistoricalSegmentHeader> ListHeaders(HistoricalSegmentQuery? query = null)
    {
        var headers = EnumerateVisibleHeaders();
        var allowedCharacters = query?.CharacterRecordId is { } character
            ? (_characterRepository?.GetRecordIdsResolvingTo(character) ?? [character]).ToHashSet()
            : null;
        if (query is not null)
        {
            headers = headers.Where(header => Matches(header, query, allowedCharacters));
        }

        return headers
            .OrderByDescending(header => header.CaptureEndUtc)
            .ThenByDescending(header => header.FinalizedAtUtc ?? header.CaptureEndUtc)
            .ThenBy(header => header.SegmentId, StringComparer.Ordinal)
            .ToArray();
    }

    public HistoricalLoadResult TryLoad(string segmentId, HistoricalLoadOptions? options = null)
    {
        if (!SegmentCaptureKey.TryParse(segmentId, out var sessionId, out var ordinal))
        {
            return new HistoricalLoadResult
            {
                Outcome = HistoricalLoadOutcome.NotFound,
                Detail = "SegmentId is not a valid capture key."
            };
        }

        return TryLoad(sessionId, ordinal, options);
    }

    public HistoricalLoadResult TryLoad(
        GameplaySessionId gameplaySessionId,
        int segmentOrdinal,
        HistoricalLoadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(gameplaySessionId);
        options ??= HistoricalLoadOptions.Default;
        var segmentId = SegmentCaptureKey.Format(gameplaySessionId, segmentOrdinal);
        var published = _segmentStore.ReadHeader(gameplaySessionId, segmentOrdinal);
        var legacy = TryGetLegacy(gameplaySessionId, segmentOrdinal);
        var canonical = ResolveCanonical(published.Metadata?.CharacterRecordId ?? legacy?.CharacterRecordId);

        if (published.Status != SegmentHeaderReadStatus.NotFound)
        {
            var durableResult = LoadDurable(published, options, canonical, legacy is not null);
            if (durableResult.HasAuthoritativeAggregates
                || durableResult.Outcome is HistoricalLoadOutcome.UnsupportedSemanticVersion)
            {
                return durableResult;
            }

            if (legacy is not null)
            {
                return LoadLegacy(
                    legacy,
                    canonical,
                    hasDurableCounterpart: true,
                    usedLegacyFallback: true,
                    detail: Concat(
                        $"Durable Segment {segmentId} is not a validated capture ({durableResult.Outcome}).",
                        durableResult.Detail));
            }

            return durableResult;
        }

        if (legacy is not null)
        {
            return LoadLegacy(legacy, canonical, hasDurableCounterpart: false, usedLegacyFallback: false);
        }

        return new HistoricalLoadResult
        {
            Outcome = HistoricalLoadOutcome.NotFound,
            Detail = $"No published Segment or legacy observation for {segmentId}."
        };
    }

    private IEnumerable<HistoricalSegmentHeader> EnumerateVisibleHeaders()
    {
        var published = new Dictionary<string, SegmentPublishedHeader>(StringComparer.Ordinal);
        foreach (var header in _segmentStore.ListHeaders())
        {
            published[header.SegmentId] = header;
        }

        var observations = new Dictionary<string, CharacterPerformanceObservation>(StringComparer.Ordinal);
        if (_observationRepository is not null)
        {
            foreach (var observation in _observationRepository.GetAll())
            {
                observations[SegmentCaptureKey.Format(observation.GameplaySessionId, observation.SegmentOrdinal)] =
                    observation;
            }
        }

        var keys = published.Keys.Union(observations.Keys, StringComparer.Ordinal);
        foreach (var key in keys)
        {
            published.TryGetValue(key, out var durable);
            observations.TryGetValue(key, out var legacy);
            var canonical = ResolveCanonical(
                durable?.Metadata?.CharacterRecordId ?? legacy?.CharacterRecordId);
            if (durable is not null && IsValidatedDurableHeader(durable))
            {
                yield return ToDurableHeader(durable, canonical, legacy is not null, usedLegacyFallback: false);
                continue;
            }

            if (legacy is not null)
            {
                yield return LegacyObservationAdapter.ToHeader(
                    legacy,
                    canonical,
                    hasDurableCounterpart: durable is not null,
                    usedLegacyFallback: durable is not null,
                    detail: durable is null
                        ? null
                        : $"Durable Segment {key} is not a validated capture ({durable.Status}).");
                continue;
            }

            if (durable is not null)
            {
                yield return ToDurableHeader(durable, canonical, hasLegacyCounterpart: false, usedLegacyFallback: false);
            }
        }
    }

    private HistoricalLoadResult LoadDurable(
        SegmentPublishedHeader published,
        HistoricalLoadOptions options,
        CharacterRecordId? canonical,
        bool hasLegacyCounterpart)
    {
        var header = ToDurableHeader(published, canonical, hasLegacyCounterpart, usedLegacyFallback: false);
        if (published.Status is SegmentHeaderReadStatus.NotFound)
        {
            return new HistoricalLoadResult { Outcome = HistoricalLoadOutcome.NotFound };
        }

        if (published.Status is SegmentHeaderReadStatus.Corrupt)
        {
            return Fail(HistoricalLoadOutcome.Corrupt, header, published.Detail);
        }

        if (published.Status is SegmentHeaderReadStatus.IncompletePublication
            || published.Metadata is null)
        {
            return Fail(HistoricalLoadOutcome.IncompletePublication, header, published.Detail);
        }

        if (published.Metadata.SegmentSchemaVersion != SegmentSchemaVersion.Current)
        {
            header = header with { Compatibility = HistoricalCompatibility.UnsupportedSchema };
            return Fail(HistoricalLoadOutcome.UnsupportedSchema, header, published.Detail);
        }

        if (published.Metadata.AnalyticsSemanticVersion != AnalyticsSemanticVersion.Current)
        {
            header = header with { Compatibility = HistoricalCompatibility.UnsupportedSemanticVersion };
            return new HistoricalLoadResult
            {
                Outcome = HistoricalLoadOutcome.UnsupportedSemanticVersion,
                Detail = $"Analytics semantic version {published.Metadata.AnalyticsSemanticVersion} is not understood.",
                Segment = new HistoricalSegment
                {
                    Header = header,
                    DetailStatus = HistoricalDetailStatus.Unavailable,
                    BuildContextStatus = HistoricalBuildContextStatus.NotApplicable,
                    AnnotationStatus = HistoricalAnnotationStatus.NotApplicable
                }
            };
        }

        if (published.GameplaySessionId is null)
        {
            return Fail(HistoricalLoadOutcome.Corrupt, header, "Published header is missing a session identity.");
        }

        var loaded = _segmentStore.TryLoad(
            published.GameplaySessionId,
            published.SegmentOrdinal,
            new SegmentLoadOptions
            {
                IncludeSpine = options.IncludeSpine,
                IncludeManifest = options.IncludeManifest,
                IncludeAnnotations = options.IncludeAnnotations
            });

        if (loaded.Outcome == SegmentLoadOutcome.HashMismatch)
        {
            header = header with { Compatibility = HistoricalCompatibility.Corrupt };
            return Fail(HistoricalLoadOutcome.Corrupt, header, loaded.Detail);
        }

        if (loaded.Outcome == SegmentLoadOutcome.IncompletePublication)
        {
            return Fail(HistoricalLoadOutcome.IncompletePublication, header, loaded.Detail);
        }

        if (loaded.Outcome == SegmentLoadOutcome.UnsupportedSchema)
        {
            header = header with { Compatibility = HistoricalCompatibility.UnsupportedSchema };
            return Fail(HistoricalLoadOutcome.UnsupportedSchema, header, loaded.Detail);
        }

        if (loaded.Outcome is SegmentLoadOutcome.Corrupt or SegmentLoadOutcome.NotFound || loaded.Segment is null)
        {
            return Fail(HistoricalLoadOutcome.Corrupt, header, loaded.Detail);
        }

        var segment = loaded.Segment;
        var coverage = segment.Coverage;
        var detailStatus = ResolveDetailStatus(options, segment, coverage, loaded.Detail);
        var spine = options.IncludeSpine ? segment.Spine : [];
        if (options.IncludeSpine
            && detailStatus is HistoricalDetailStatus.AvailableComplete or HistoricalDetailStatus.AvailablePartial
            && coverage.RetainedSpineEventCount != spine.Count)
        {
            detailStatus = HistoricalDetailStatus.Corrupt;
            spine = [];
        }

        var (buildStatus, manifest) = ResolveBuildContext(options, segment, loaded.Detail);
        var (annotationStatus, annotations) = ResolveAnnotations(options, segment, loaded.Detail);

        // The cube is authoritative in every branch here (aggregate hash already validated).
        // A requested dependency degrades the summary while the component status stays specific.
        // Truncation (AvailablePartial) is coverage-declared, not degradation.
        var detailDegraded = options.IncludeSpine
            && detailStatus is HistoricalDetailStatus.Corrupt
                or HistoricalDetailStatus.UnsupportedSchema
                or HistoricalDetailStatus.Unavailable;
        var buildDegraded = buildStatus is HistoricalBuildContextStatus.Missing
            or HistoricalBuildContextStatus.HashMismatch;
        var annotationDegraded = annotationStatus is HistoricalAnnotationStatus.Corrupt;
        var compatibility = detailDegraded || buildDegraded || annotationDegraded
            ? HistoricalCompatibility.AuthoritativeAggregateDetailDegraded
            : HistoricalCompatibility.AuthoritativeAggregate;

        header = header with
        {
            Compatibility = compatibility,
            LogicalEventCount = coverage.LogicalEventCount,
            RetainedSpineEventCount = coverage.RetainedSpineEventCount,
            SpineTruncated = coverage.SpineTruncated,
            CoverageLimited = coverage.CoverageLimited,
            EventTimelineReplay = coverage.Replay.EventTimeline.Replay,
            Detail = loaded.Detail
        };

        return new HistoricalLoadResult
        {
            Outcome = compatibility == HistoricalCompatibility.AuthoritativeAggregateDetailDegraded
                ? HistoricalLoadOutcome.LoadedDetailDegraded
                : HistoricalLoadOutcome.Loaded,
            Detail = loaded.Detail,
            Segment = new HistoricalSegment
            {
                Header = header,
                Aggregates = segment.Aggregates,
                Coverage = coverage,
                DetailStatus = detailStatus,
                Spine = spine,
                FrozenManifest = manifest,
                BuildContextStatus = buildStatus,
                Annotations = annotations,
                AnnotationStatus = annotationStatus
            }
        };
    }

    private HistoricalLoadResult LoadLegacy(
        CharacterPerformanceObservation observation,
        CharacterRecordId? canonical,
        bool hasDurableCounterpart,
        bool usedLegacyFallback,
        string? detail = null)
    {
        var header = LegacyObservationAdapter.ToHeader(
            observation,
            canonical,
            hasDurableCounterpart,
            usedLegacyFallback,
            detail);
        var coverage = LegacyObservationAdapter.ToCoverage(observation);
        return new HistoricalLoadResult
        {
            Outcome = HistoricalLoadOutcome.LoadedLegacyAdapted,
            Detail = detail,
            Segment = new HistoricalSegment
            {
                Header = header,
                Aggregates = LegacyObservationAdapter.ToProjection(observation),
                Coverage = coverage,
                DetailStatus = HistoricalDetailStatus.Unavailable,
                BuildContextStatus = HistoricalBuildContextStatus.NotApplicable,
                AnnotationStatus = HistoricalAnnotationStatus.NotApplicable,
                LegacyObservation = observation,
                ExperienceGained = Metric<long>.Available(observation.ExperienceGained),
                GameplayInfluenceGained = Metric<long>.Available(observation.GameplayInfluenceGained)
            }
        };
    }

    private static HistoricalSegmentHeader ToDurableHeader(
        SegmentPublishedHeader published,
        CharacterRecordId? canonical,
        bool hasLegacyCounterpart,
        bool usedLegacyFallback)
    {
        var metadata = published.Metadata;
        var coverage = published.Coverage;
        var compatibility = published.Status switch
        {
            SegmentHeaderReadStatus.UnsupportedSchema => HistoricalCompatibility.UnsupportedSchema,
            SegmentHeaderReadStatus.Corrupt => HistoricalCompatibility.Corrupt,
            SegmentHeaderReadStatus.IncompletePublication => HistoricalCompatibility.IncompletePublication,
            SegmentHeaderReadStatus.NotFound => HistoricalCompatibility.NotFound,
            _ when metadata is { SegmentSchemaVersion: var schema }
                && schema != SegmentSchemaVersion.Current => HistoricalCompatibility.UnsupportedSchema,
            _ when metadata is { AnalyticsSemanticVersion: var semantic }
                && semantic != AnalyticsSemanticVersion.Current => HistoricalCompatibility.UnsupportedSemanticVersion,
            _ => HistoricalCompatibility.AuthoritativeAggregate
        };

        return new HistoricalSegmentHeader
        {
            SegmentId = published.SegmentId,
            CaptureKind = HistoricalCaptureKind.DurableSegment,
            Compatibility = compatibility,
            GameplaySessionId = published.GameplaySessionId
                ?? metadata?.GameplaySessionId
                ?? GameplaySessionId.FromGuid(Guid.Empty),
            SegmentOrdinal = published.SegmentOrdinal,
            CharacterRecordId = metadata?.CharacterRecordId,
            CanonicalCharacterRecordId = canonical ?? metadata?.CharacterRecordId,
            AccountStableId = metadata?.AccountStableId,
            CharacterDisplayNameAtCapture = metadata?.CharacterDisplayNameAtCapture,
            AccountDisplayNameAtCapture = metadata?.AccountDisplayNameAtCapture,
            LevelAtCapture = metadata?.LevelAtCapture,
            Archetype = metadata?.Archetype,
            PrimaryPowerSet = metadata?.PrimaryPowerSet,
            SecondaryPowerSet = metadata?.SecondaryPowerSet,
            CaptureStartUtc = metadata?.CaptureStartUtc ?? DateTimeOffset.MinValue,
            CaptureEndUtc = metadata?.CaptureEndUtc ?? DateTimeOffset.MinValue,
            FinalizedAtUtc = metadata?.FinalizedAtUtc,
            SegmentSchemaVersion = metadata?.SegmentSchemaVersion,
            SpineSchemaVersion = metadata?.SpineSchemaVersion,
            AnalyticsSemanticVersion = metadata?.AnalyticsSemanticVersion,
            GrammarSetVersion = metadata?.GrammarSetVersion,
            DedupPolicyVersion = metadata?.DedupPolicyVersion,
            AttributionPolicyVersion = metadata?.AttributionPolicyVersion,
            BuildManifestHash = metadata?.BuildManifestHash,
            BuildCatalogFingerprint = metadata?.BuildCatalogFingerprint,
            AppVersion = metadata?.AppVersion,
            LogicalEventCount = coverage?.LogicalEventCount ?? 0,
            RetainedSpineEventCount = coverage?.RetainedSpineEventCount ?? 0,
            SpineTruncated = coverage?.SpineTruncated ?? false,
            CoverageLimited = coverage?.CoverageLimited ?? false,
            EventTimelineReplay = coverage?.Replay.EventTimeline.Replay,
            HasLegacyObservationCounterpart = hasLegacyCounterpart,
            UsedLegacyFallback = usedLegacyFallback,
            Detail = published.Detail
        };
    }

    private static bool IsValidatedDurableHeader(SegmentPublishedHeader published) =>
        published.Metadata is not null
        && published.Metadata.SegmentSchemaVersion == SegmentSchemaVersion.Current
        && published.Status is SegmentHeaderReadStatus.Readable
            or SegmentHeaderReadStatus.CoverageDegraded;

    private static bool Matches(
        HistoricalSegmentHeader header,
        HistoricalSegmentQuery query,
        HashSet<CharacterRecordId>? allowedCharacters)
    {
        if (query.SegmentId is { } segmentId
            && !string.Equals(header.SegmentId, segmentId, StringComparison.Ordinal))
        {
            return false;
        }

        if (query.GameplaySessionId is { } sessionId && header.GameplaySessionId != sessionId)
        {
            return false;
        }

        if (query.SegmentOrdinal is { } ordinal && header.SegmentOrdinal != ordinal)
        {
            return false;
        }

        if (allowedCharacters is not null)
        {
            var captured = header.CharacterRecordId;
            var canonical = header.CanonicalCharacterRecordId;
            if ((captured is null || !allowedCharacters.Contains(captured))
                && (canonical is null || !allowedCharacters.Contains(canonical)))
            {
                return false;
            }
        }

        var captureStart = header.CaptureStartUtc;
        var captureEnd = header.FinalizedAtUtc ?? header.CaptureEndUtc;
        if (query.RangeStartUtc is { } rangeStart && captureEnd < rangeStart)
        {
            return false;
        }

        if (query.RangeEndUtc is { } rangeEnd && captureStart > rangeEnd)
        {
            return false;
        }

        return true;
    }

    private CharacterRecordId? ResolveCanonical(CharacterRecordId? captured)
    {
        if (captured is null)
        {
            return null;
        }

        return _characterRepository?.ResolveCanonicalRecordId(captured) ?? captured;
    }

    private CharacterPerformanceObservation? TryGetLegacy(GameplaySessionId sessionId, int ordinal)
    {
        if (_observationRepository is null)
        {
            return null;
        }

        var key = SegmentCaptureKey.Format(sessionId, ordinal);
        return _observationRepository.GetAll()
            .FirstOrDefault(observation =>
                SegmentCaptureKey.Format(observation.GameplaySessionId, observation.SegmentOrdinal) == key);
    }

    private static HistoricalDetailStatus ResolveDetailStatus(
        HistoricalLoadOptions options,
        AnalyticalSegment segment,
        SegmentCoverageDescriptor coverage,
        string? detail)
    {
        if (!options.IncludeSpine)
        {
            return HistoricalDetailStatus.NotRequested;
        }

        if (segment.Metadata.SpineSchemaVersion != SpineSchemaVersion.Current)
        {
            return HistoricalDetailStatus.UnsupportedSchema;
        }

        if (detail is not null
            && (detail.Contains("Spine hash mismatch", StringComparison.Ordinal)
                || detail.Contains("Spine is corrupt", StringComparison.Ordinal)))
        {
            return HistoricalDetailStatus.Corrupt;
        }

        var timeline = coverage.Replay.EventTimeline.Replay;
        if (timeline == ReplayCoverageKind.Lossless && !coverage.SpineTruncated)
        {
            return HistoricalDetailStatus.AvailableComplete;
        }

        if (timeline == ReplayCoverageKind.PartialReplay || coverage.SpineTruncated)
        {
            return HistoricalDetailStatus.AvailablePartial;
        }

        return HistoricalDetailStatus.Unavailable;
    }

    private static (HistoricalBuildContextStatus Status, FrozenBuildManifest? Manifest) ResolveBuildContext(
        HistoricalLoadOptions options,
        AnalyticalSegment segment,
        string? detail)
    {
        if (!options.IncludeManifest)
        {
            return (segment.Metadata.BuildManifestHash is null
                ? HistoricalBuildContextStatus.NotCaptured
                : HistoricalBuildContextStatus.NotApplicable, null);
        }

        if (segment.Metadata.BuildManifestHash is null)
        {
            return (HistoricalBuildContextStatus.NotCaptured, null);
        }

        if (segment.FrozenManifest is not null)
        {
            return (HistoricalBuildContextStatus.Present, segment.FrozenManifest);
        }

        if (detail is not null && detail.Contains("manifest", StringComparison.OrdinalIgnoreCase))
        {
            return (HistoricalBuildContextStatus.Missing, null);
        }

        return (HistoricalBuildContextStatus.HashMismatch, null);
    }

    private static (HistoricalAnnotationStatus Status, SegmentAnnotations? Annotations) ResolveAnnotations(
        HistoricalLoadOptions options,
        AnalyticalSegment segment,
        string? detail)
    {
        if (!options.IncludeAnnotations)
        {
            return (HistoricalAnnotationStatus.NotApplicable, null);
        }

        if (detail is not null && detail.Contains("Annotations sidecar is corrupt", StringComparison.Ordinal))
        {
            return (HistoricalAnnotationStatus.Corrupt, segment.Annotations);
        }

        return (HistoricalAnnotationStatus.Present, segment.Annotations);
    }

    private static HistoricalLoadResult Fail(
        HistoricalLoadOutcome outcome,
        HistoricalSegmentHeader header,
        string? detail) =>
        new()
        {
            Outcome = outcome,
            Detail = detail,
            Segment = new HistoricalSegment
            {
                Header = header with { Compatibility = outcome switch
                {
                    HistoricalLoadOutcome.UnsupportedSchema => HistoricalCompatibility.UnsupportedSchema,
                    HistoricalLoadOutcome.UnsupportedSemanticVersion => HistoricalCompatibility.UnsupportedSemanticVersion,
                    HistoricalLoadOutcome.IncompletePublication => HistoricalCompatibility.IncompletePublication,
                    HistoricalLoadOutcome.NotFound => HistoricalCompatibility.NotFound,
                    _ => HistoricalCompatibility.Corrupt
                }},
                DetailStatus = HistoricalDetailStatus.Unavailable,
                BuildContextStatus = HistoricalBuildContextStatus.NotApplicable,
                AnnotationStatus = HistoricalAnnotationStatus.NotApplicable
            }
        };

    private static string Concat(string left, string? right) =>
        string.IsNullOrWhiteSpace(right) ? left : left + " " + right;
}
