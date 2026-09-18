using System.Security.Cryptography;
using System.Text;
using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

/// <summary>
/// Multi-file Segment publication under Characters/Segments plus content-addressed
/// builds/manifests. Reuses observation-repository atomic file writes and directory rename.
/// </summary>
public sealed class SegmentStore : ISegmentStore, ISegmentAnnotationWriter
{
    public const string MetadataFileName = "metadata.json";
    public const string CoverageFileName = "coverage.json";
    public const string AggregatesFileName = "aggregates.json";
    public const string SpineFileName = "spine.bin";
    public const string AnnotationsFileName = "annotations.json";

    private readonly object _sync = new();
    private readonly string _dataDirectory;
    private readonly Action<string, string> _commitDirectory;
    private readonly Action<string> _deleteDirectory;

    public SegmentStore(string? dataDirectory = null)
        : this(dataDirectory, CommitDirectory, DeleteDirectory)
    {
    }

    internal SegmentStore(
        string? dataDirectory,
        Action<string, string> commitDirectory,
        Action<string>? deleteDirectory = null)
    {
        _dataDirectory = dataDirectory ?? ApplicationDataPaths.GetApplicationRoot();
        _commitDirectory = commitDirectory ?? throw new ArgumentNullException(nameof(commitDirectory));
        _deleteDirectory = deleteDirectory ?? DeleteDirectory;
        SegmentsDirectory = ApplicationDataPaths.GetSegmentsDirectory(_dataDirectory);
        ManifestsDirectory = ApplicationDataPaths.GetBuildManifestsDirectory(_dataDirectory);
    }

    public string SegmentsDirectory { get; }

    public string ManifestsDirectory { get; }

    public SegmentPersistResult Persist(SegmentDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (!TryValidate(draft, out var validationError))
        {
            return new SegmentPersistResult
            {
                Outcome = SegmentPersistOutcome.InvalidSegment,
                Detail = validationError
            };
        }

        var keyDirectory = GetSegmentDirectory(draft.GameplaySessionId, draft.SegmentOrdinal);
        lock (_sync)
        {
            Directory.CreateDirectory(SegmentsDirectory);
            Directory.CreateDirectory(ManifestsDirectory);

            if (Directory.Exists(keyDirectory))
            {
                return ResolveExisting(keyDirectory, draft);
            }

            var staging = keyDirectory + ".staging-" + Guid.NewGuid().ToString("n");
            try
            {
                Directory.CreateDirectory(staging);
                var manifestHash = PublishManifestLocked(draft.FrozenManifest);
                var coverageJson = SegmentJson.Serialize(draft.Coverage);
                var aggregates = FinalizeClock(draft.Aggregates, draft.CaptureStartUtc, draft.CaptureEndUtc);
                var aggregatesJson = SegmentJson.Serialize(aggregates);
                var spineBytes = SegmentSpineCodec.Encode(draft.Spine);
                var annotationsJson = SegmentJson.Serialize(draft.Annotations);
                WriteUtf8(Path.Combine(staging, CoverageFileName), coverageJson);
                WriteUtf8(Path.Combine(staging, AggregatesFileName), aggregatesJson);
                WriteBytes(Path.Combine(staging, SpineFileName), spineBytes);
                WriteUtf8(Path.Combine(staging, AnnotationsFileName), annotationsJson);
                var metadata = BuildMetadata(draft, manifestHash, new SegmentFileHashes
                {
                    Coverage = Sha256(Encoding.UTF8.GetBytes(coverageJson)),
                    Aggregates = Sha256(Encoding.UTF8.GetBytes(aggregatesJson)),
                    Spine = Sha256(spineBytes),
                    Manifest = manifestHash is null ? null : Sha256(File.ReadAllBytes(GetManifestPath(manifestHash)))
                });
                WriteUtf8(Path.Combine(staging, MetadataFileName), SegmentJson.Serialize(metadata));
                _commitDirectory(staging, keyDirectory);
                return new SegmentPersistResult
                {
                    Outcome = SegmentPersistOutcome.Persisted,
                    DirectoryPath = keyDirectory
                };
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                return new SegmentPersistResult
                {
                    Outcome = SegmentPersistOutcome.PersistenceFailed,
                    DirectoryPath = keyDirectory,
                    Detail = exception.Message
                };
            }
            finally
            {
                if (Directory.Exists(staging))
                {
                    _deleteDirectory(staging);
                }
            }
        }
    }

    public SegmentLoadResult TryLoad(GameplaySessionId gameplaySessionId, int segmentOrdinal) =>
        TryLoad(gameplaySessionId, segmentOrdinal, SegmentLoadOptions.Complete);

    public SegmentLoadResult TryLoad(
        GameplaySessionId gameplaySessionId,
        int segmentOrdinal,
        SegmentLoadOptions options)
    {
        ArgumentNullException.ThrowIfNull(gameplaySessionId);
        ArgumentNullException.ThrowIfNull(options);
        var directory = GetSegmentDirectory(gameplaySessionId, segmentOrdinal);
        lock (_sync)
        {
            return LoadPublished(directory, options);
        }
    }

    public SegmentPublishedHeader ReadHeader(GameplaySessionId gameplaySessionId, int segmentOrdinal)
    {
        ArgumentNullException.ThrowIfNull(gameplaySessionId);
        var directory = GetSegmentDirectory(gameplaySessionId, segmentOrdinal);
        return ReadPublishedHeader(directory, SegmentCaptureKey.Format(gameplaySessionId, segmentOrdinal));
    }

    public IReadOnlyList<SegmentPublishedHeader> ListHeaders()
    {
        if (!Directory.Exists(SegmentsDirectory))
        {
            return [];
        }

        var headers = new List<SegmentPublishedHeader>();
        foreach (var directory in Directory.GetDirectories(SegmentsDirectory))
        {
            var name = Path.GetFileName(directory);
            if (name.Contains(".staging-", StringComparison.Ordinal))
            {
                continue;
            }

            if (!SegmentCaptureKey.TryParse(name, out _, out _))
            {
                continue;
            }

            headers.Add(ReadPublishedHeader(directory, name));
        }

        return headers;
    }

    public SegmentPersistResult TryUpdateAnnotations(
        GameplaySessionId gameplaySessionId,
        int segmentOrdinal,
        SegmentAnnotations annotations)
    {
        ArgumentNullException.ThrowIfNull(gameplaySessionId);
        ArgumentNullException.ThrowIfNull(annotations);
        var directory = GetSegmentDirectory(gameplaySessionId, segmentOrdinal);
        lock (_sync)
        {
            var loaded = LoadPublished(directory, SegmentLoadOptions.WithoutSpine);
            if (!loaded.IsSuccess || loaded.Segment is null)
            {
                return new SegmentPersistResult
                {
                    Outcome = SegmentPersistOutcome.InvalidSegment,
                    DirectoryPath = directory,
                    Detail = loaded.Detail ?? "Segment is not published."
                };
            }

            try
            {
                WriteUtf8Replace(Path.Combine(directory, AnnotationsFileName), SegmentJson.Serialize(annotations));
                return new SegmentPersistResult
                {
                    Outcome = SegmentPersistOutcome.Persisted,
                    DirectoryPath = directory
                };
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                return new SegmentPersistResult
                {
                    Outcome = SegmentPersistOutcome.PersistenceFailed,
                    DirectoryPath = directory,
                    Detail = exception.Message
                };
            }
        }
    }

    internal IReadOnlyList<string> ListPublishedDirectories()
    {
        lock (_sync)
        {
            if (!Directory.Exists(SegmentsDirectory))
            {
                return [];
            }

            return Directory.GetDirectories(SegmentsDirectory)
                .Where(path => !Path.GetFileName(path).Contains(".staging-", StringComparison.Ordinal))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
        }
    }

    private SegmentLoadResult LoadPublished(string directory, SegmentLoadOptions options)
    {
        if (!Directory.Exists(directory) || Path.GetFileName(directory).Contains(".staging-", StringComparison.Ordinal))
        {
            return new SegmentLoadResult { Outcome = SegmentLoadOutcome.NotFound, DirectoryPath = directory };
        }

        try
        {
            var metadataPath = Path.Combine(directory, MetadataFileName);
            var coveragePath = Path.Combine(directory, CoverageFileName);
            var aggregatesPath = Path.Combine(directory, AggregatesFileName);
            var spinePath = Path.Combine(directory, SpineFileName);
            var annotationsPath = Path.Combine(directory, AnnotationsFileName);
            if (!File.Exists(metadataPath) || !File.Exists(coveragePath)
                || !File.Exists(aggregatesPath) || !File.Exists(spinePath))
            {
                return new SegmentLoadResult
                {
                    Outcome = SegmentLoadOutcome.IncompletePublication,
                    DirectoryPath = directory,
                    Detail = "Required immutable Segment files are missing."
                };
            }

            var metadata = SegmentJson.Deserialize<SegmentCaptureMetadata>(File.ReadAllText(metadataPath));
            if (metadata.SegmentSchemaVersion != SegmentSchemaVersion.Current)
            {
                return new SegmentLoadResult
                {
                    Outcome = SegmentLoadOutcome.UnsupportedSchema,
                    DirectoryPath = directory,
                    Detail = $"Segment schema version {metadata.SegmentSchemaVersion} is not supported."
                };
            }

            var coverageJson = File.ReadAllText(coveragePath);
            var aggregatesJson = File.ReadAllText(aggregatesPath);
            if (Sha256(Encoding.UTF8.GetBytes(coverageJson)) != metadata.FileHashes.Coverage
                || Sha256(Encoding.UTF8.GetBytes(aggregatesJson)) != metadata.FileHashes.Aggregates)
            {
                return new SegmentLoadResult
                {
                    Outcome = SegmentLoadOutcome.HashMismatch,
                    DirectoryPath = directory,
                    Detail = "Immutable Segment file hash mismatch."
                };
            }

            var coverage = SegmentJson.Deserialize<SegmentCoverageDescriptor>(coverageJson);
            var aggregates = SegmentJson.Deserialize<CombatAnalyticsProjection>(aggregatesJson);
            string? detail = null;
            IReadOnlyList<PersistedSpineEvent> spine = [];
            if (options.IncludeSpine)
            {
                var spineBytes = File.ReadAllBytes(spinePath);
                if (Sha256(spineBytes) != metadata.FileHashes.Spine)
                {
                    coverage = coverage with
                    {
                        Replay = coverage.Replay with
                        {
                            EventTimeline = new ReplayCoverageEntry
                            {
                                AggregateAuthoritative = false,
                                Replay = ReplayCoverageKind.NotRecomputable
                            }
                        }
                    };
                    detail = "Spine hash mismatch; aggregate cube remains readable.";
                }
                else
                {
                    try
                    {
                        spine = SegmentSpineCodec.Decode(spineBytes);
                    }
                    catch (Exception exception) when (exception is InvalidDataException or System.Text.Json.JsonException)
                    {
                        coverage = coverage with
                        {
                            Replay = coverage.Replay with
                            {
                                EventTimeline = new ReplayCoverageEntry
                                {
                                    AggregateAuthoritative = false,
                                    Replay = ReplayCoverageKind.NotRecomputable
                                }
                            }
                        };
                        detail = "Spine is corrupt; aggregate cube remains readable.";
                    }
                }
            }

            FrozenBuildManifest? manifest = null;
            if (options.IncludeManifest && metadata.BuildManifestHash is { } hash)
            {
                var manifestPath = GetManifestPath(hash);
                if (!File.Exists(manifestPath))
                {
                    detail = ConcatDetail(detail, "Frozen build manifest is missing or hash-mismatched.");
                }
                else
                {
                    try
                    {
                        var manifestBytes = File.ReadAllBytes(manifestPath);
                        if (metadata.FileHashes.Manifest is not null
                            && Sha256(manifestBytes) != metadata.FileHashes.Manifest)
                        {
                            detail = ConcatDetail(detail, "Frozen build manifest is missing or hash-mismatched.");
                        }
                        else
                        {
                            manifest = SegmentJson.Deserialize<FrozenBuildManifest>(
                                Encoding.UTF8.GetString(manifestBytes));
                        }
                    }
                    catch (Exception exception) when (exception is IOException or InvalidDataException
                        or System.Text.Json.JsonException or UnauthorizedAccessException)
                    {
                        detail = ConcatDetail(detail, "Frozen build manifest is missing or hash-mismatched.");
                    }
                }
            }

            SegmentAnnotations annotations = new();
            var annotationStatusDetail = (string?)null;
            if (options.IncludeAnnotations)
            {
                try
                {
                    annotations = File.Exists(annotationsPath)
                        ? SegmentJson.Deserialize<SegmentAnnotations>(File.ReadAllText(annotationsPath))
                        : new SegmentAnnotations();
                    if (!File.Exists(annotationsPath))
                    {
                        annotationStatusDetail = null;
                    }
                }
                catch (System.Text.Json.JsonException)
                {
                    annotations = new SegmentAnnotations();
                    annotationStatusDetail = "Annotations sidecar is corrupt; defaults were used.";
                }
            }

            if (annotationStatusDetail is not null)
            {
                detail = ConcatDetail(detail, annotationStatusDetail);
            }

            return new SegmentLoadResult
            {
                Outcome = SegmentLoadOutcome.Loaded,
                DirectoryPath = directory,
                Detail = detail,
                Segment = new AnalyticalSegment
                {
                    Metadata = metadata,
                    Coverage = coverage,
                    Aggregates = aggregates,
                    Spine = spine,
                    FrozenManifest = manifest,
                    Annotations = annotations,
                    PublishedDirectory = directory
                }
            };
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException
            or System.Text.Json.JsonException or UnauthorizedAccessException)
        {
            return new SegmentLoadResult
            {
                Outcome = SegmentLoadOutcome.Corrupt,
                DirectoryPath = directory,
                Detail = exception.Message
            };
        }
    }

    private SegmentPersistResult ResolveExisting(string directory, SegmentDraft draft)
    {
        var loaded = LoadPublished(directory, SegmentLoadOptions.WithoutSpine);
        if (!loaded.IsSuccess || loaded.Segment is null)
        {
            return new SegmentPersistResult
            {
                Outcome = SegmentPersistOutcome.Conflict,
                DirectoryPath = directory,
                Detail = loaded.Detail ?? "Existing Segment directory is not a valid publication."
            };
        }

        var existing = loaded.Segment;
        var incomingCoverage = Sha256(Encoding.UTF8.GetBytes(SegmentJson.Serialize(draft.Coverage)));
        var incomingAggregates = Sha256(Encoding.UTF8.GetBytes(SegmentJson.Serialize(
            FinalizeClock(draft.Aggregates, draft.CaptureStartUtc, draft.CaptureEndUtc))));
        var incomingSpine = Sha256(SegmentSpineCodec.Encode(draft.Spine));
        if (existing.Metadata.GameplaySessionId == draft.GameplaySessionId
            && existing.Metadata.SegmentOrdinal == draft.SegmentOrdinal
            && existing.Metadata.FileHashes.Coverage == incomingCoverage
            && existing.Metadata.FileHashes.Aggregates == incomingAggregates
            && existing.Metadata.FileHashes.Spine == incomingSpine
            && existing.Metadata.BuildManifestHash == draft.FrozenManifest?.ManifestHash)
        {
            return new SegmentPersistResult
            {
                Outcome = SegmentPersistOutcome.Duplicate,
                DirectoryPath = directory
            };
        }

        return new SegmentPersistResult
        {
            Outcome = SegmentPersistOutcome.Conflict,
            DirectoryPath = directory,
            Detail = "A different Segment is already published for this capture key."
        };
    }

    private string? PublishManifestLocked(FrozenBuildManifest? manifest)
    {
        if (manifest is null || string.IsNullOrWhiteSpace(manifest.ManifestHash))
        {
            return null;
        }

        var path = GetManifestPath(manifest.ManifestHash);
        var json = SegmentJson.Serialize(manifest);
        if (File.Exists(path))
        {
            var existing = File.ReadAllBytes(path);
            if (Sha256(existing) != Sha256(Encoding.UTF8.GetBytes(json)))
            {
                throw new InvalidOperationException(
                    $"Frozen manifest {manifest.ManifestHash} already exists with different content.");
            }

            return manifest.ManifestHash;
        }

        var temp = path + "." + Guid.NewGuid().ToString("n") + ".tmp";
        try
        {
            WriteUtf8(temp, json);
            File.Move(temp, path, overwrite: false);
        }
        finally
        {
            if (File.Exists(temp))
            {
                try { File.Delete(temp); } catch { }
            }
        }

        return manifest.ManifestHash;
    }

    private static SegmentCaptureMetadata BuildMetadata(
        SegmentDraft draft,
        string? manifestHash,
        SegmentFileHashes hashes) =>
        new()
        {
            SegmentSchemaVersion = SegmentSchemaVersion.Current,
            AnalyticsSemanticVersion = AnalyticsSemanticVersion.Current,
            GrammarSetVersion = EventProvenance.CurrentGrammarSetVersion,
            DedupPolicyVersion = DedupPolicyVersion.Current,
            AttributionPolicyVersion = AttributionPolicyVersion.Current,
            SpineSchemaVersion = SpineSchemaVersion.Current,
            GameplaySessionId = draft.GameplaySessionId,
            SegmentOrdinal = draft.SegmentOrdinal,
            CharacterRecordId = draft.CharacterRecordId,
            AccountStableId = draft.AccountStableId,
            AccountDisplayNameAtCapture = draft.AccountDisplayNameAtCapture,
            CharacterDisplayNameAtCapture = draft.CharacterDisplayNameAtCapture,
            LevelAtCapture = draft.LevelAtCapture,
            Archetype = draft.Archetype,
            PrimaryPowerSet = draft.PrimaryPowerSet,
            SecondaryPowerSet = draft.SecondaryPowerSet,
            CaptureStartUtc = draft.CaptureStartUtc,
            CaptureEndUtc = draft.CaptureEndUtc,
            FinalizedAtUtc = draft.FinalizedAtUtc,
            AppVersion = draft.AppVersion,
            BuildManifestHash = manifestHash,
            BuildCatalogFingerprint = draft.FrozenManifest?.BuildCatalogFingerprint,
            FileHashes = hashes
        };

    private static CombatAnalyticsProjection FinalizeClock(
        CombatAnalyticsProjection projection,
        DateTimeOffset captureStart,
        DateTimeOffset captureEnd)
    {
        var clock = projection.Clock with
        {
            CaptureStartUtc = captureStart,
            CaptureEndUtc = captureEnd,
            AsOfUtc = captureEnd
        };
        return projection with { Clock = clock, AnalyticsSemanticVersion = AnalyticsSemanticVersion.Current };
    }

    private static bool TryValidate(SegmentDraft draft, out string error)
    {
        if (draft.SegmentOrdinal < 0)
        {
            error = "Segment ordinal must be non-negative.";
            return false;
        }

        if (draft.CaptureEndUtc < draft.CaptureStartUtc)
        {
            error = "Capture end precedes capture start.";
            return false;
        }

        if (draft.Spine.Count > SegmentSpineLimits.MaxRetainedLogicalEvents)
        {
            error = "Spine exceeds the retention bound.";
            return false;
        }

        if (draft.Spine.Any(item => item is null))
        {
            error = "Spine contains a null event.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private SegmentPublishedHeader ReadPublishedHeader(string directory, string segmentId)
    {
        if (!Directory.Exists(directory) || Path.GetFileName(directory).Contains(".staging-", StringComparison.Ordinal))
        {
            SegmentCaptureKey.TryParse(segmentId, out var sessionId, out var ordinal);
            return new SegmentPublishedHeader
            {
                Status = SegmentHeaderReadStatus.NotFound,
                SegmentId = segmentId,
                GameplaySessionId = sessionId,
                SegmentOrdinal = ordinal,
                DirectoryPath = directory
            };
        }

        try
        {
            SegmentCaptureKey.TryParse(segmentId, out var parsedSession, out var parsedOrdinal);
            var metadataPath = Path.Combine(directory, MetadataFileName);
            if (!File.Exists(metadataPath))
            {
                return new SegmentPublishedHeader
                {
                    Status = SegmentHeaderReadStatus.IncompletePublication,
                    SegmentId = segmentId,
                    GameplaySessionId = parsedSession,
                    SegmentOrdinal = parsedOrdinal,
                    DirectoryPath = directory,
                    Detail = "metadata.json is missing."
                };
            }

            SegmentCaptureMetadata metadata;
            try
            {
                metadata = SegmentJson.Deserialize<SegmentCaptureMetadata>(File.ReadAllText(metadataPath));
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException
                or System.Text.Json.JsonException or UnauthorizedAccessException)
            {
                return new SegmentPublishedHeader
                {
                    Status = SegmentHeaderReadStatus.Corrupt,
                    SegmentId = segmentId,
                    GameplaySessionId = parsedSession,
                    SegmentOrdinal = parsedOrdinal,
                    DirectoryPath = directory,
                    Detail = exception.Message
                };
            }

            var status = metadata.SegmentSchemaVersion == SegmentSchemaVersion.Current
                ? SegmentHeaderReadStatus.Readable
                : SegmentHeaderReadStatus.UnsupportedSchema;
            var detail = status == SegmentHeaderReadStatus.UnsupportedSchema
                ? $"Segment schema version {metadata.SegmentSchemaVersion} is not supported."
                : null;

            SegmentCoverageDescriptor? coverage = null;
            var coveragePath = Path.Combine(directory, CoverageFileName);
            if (File.Exists(coveragePath))
            {
                try
                {
                    var coverageJson = File.ReadAllText(coveragePath);
                    if (Sha256(Encoding.UTF8.GetBytes(coverageJson)) != metadata.FileHashes.Coverage)
                    {
                        status = status == SegmentHeaderReadStatus.UnsupportedSchema
                            ? status
                            : SegmentHeaderReadStatus.CoverageDegraded;
                        detail = ConcatDetail(detail, "Coverage hash mismatch; identity metadata remains readable.");
                    }
                    else
                    {
                        coverage = SegmentJson.Deserialize<SegmentCoverageDescriptor>(coverageJson);
                    }
                }
                catch (Exception exception) when (exception is IOException or InvalidDataException
                    or System.Text.Json.JsonException or UnauthorizedAccessException)
                {
                    status = status == SegmentHeaderReadStatus.UnsupportedSchema
                        ? status
                        : SegmentHeaderReadStatus.CoverageDegraded;
                    detail = ConcatDetail(detail, exception.Message);
                }
            }
            else
            {
                status = status == SegmentHeaderReadStatus.UnsupportedSchema
                    ? status
                    : SegmentHeaderReadStatus.CoverageDegraded;
                detail = ConcatDetail(detail, "coverage.json is missing.");
            }

            return new SegmentPublishedHeader
            {
                Status = status,
                SegmentId = SegmentCaptureKey.Format(metadata.GameplaySessionId, metadata.SegmentOrdinal),
                GameplaySessionId = metadata.GameplaySessionId,
                SegmentOrdinal = metadata.SegmentOrdinal,
                DirectoryPath = directory,
                Metadata = metadata,
                Coverage = coverage,
                Detail = detail
            };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            SegmentCaptureKey.TryParse(segmentId, out var sessionId, out var ordinal);
            return new SegmentPublishedHeader
            {
                Status = SegmentHeaderReadStatus.Corrupt,
                SegmentId = segmentId,
                GameplaySessionId = sessionId,
                SegmentOrdinal = ordinal,
                DirectoryPath = directory,
                Detail = exception.Message
            };
        }
    }

    private string GetSegmentDirectory(GameplaySessionId sessionId, int ordinal) =>
        Path.Combine(SegmentsDirectory, SegmentCaptureKey.Format(sessionId, ordinal));

    private string GetManifestPath(string hash) =>
        Path.Combine(ManifestsDirectory, hash + ".json");

    private static void WriteUtf8(string path, string json)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(json);
        writer.Flush();
        stream.Flush(flushToDisk: true);
    }

    private static void WriteUtf8Replace(string path, string json)
    {
        var temp = path + "." + Guid.NewGuid().ToString("n") + ".tmp";
        try
        {
            WriteUtf8(temp, json);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                try { File.Delete(temp); } catch { }
            }
        }
    }

    private static void WriteBytes(string path, byte[] bytes)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush(flushToDisk: true);
    }

    internal static string Sha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static string ConcatDetail(string? left, string right) =>
        string.IsNullOrWhiteSpace(left) ? right : left + " " + right;

    private static void CommitDirectory(string staging, string final) =>
        Directory.Move(staging, final);

    private static void DeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
        }
    }
}
