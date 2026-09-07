using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CoHAnalytics.Models;
using CoHAnalytics.ReferenceData;
using CoHAnalytics.Services;

namespace CoHAnalytics.Observations;

internal sealed class AcquisitionObservationEvidenceStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    private readonly string _observationsRoot;
    private readonly Action? _beforeAppend;

    public AcquisitionObservationEvidenceStore(
        string observationsRoot,
        Action? beforeAppend = null)
    {
        _observationsRoot = observationsRoot;
        _beforeAppend = beforeAppend;
    }

    public string ObservationsRoot => _observationsRoot;

    public void EnsureDirectory()
    {
        Directory.CreateDirectory(_observationsRoot);
    }

    public void AppendEvidence(AcquisitionObservationRecord observation)
    {
        EnsureDirectory();
        var evidencePath = GetEvidencePathForDate(observation.CapturedAtUtc);
        var line = JsonSerializer.Serialize(ToEvidencePayload(observation), SerializerOptions);
        _beforeAppend?.Invoke();
        File.AppendAllText(evidencePath, line + Environment.NewLine, Encoding.UTF8);
    }

    public List<AcquisitionObservationRecord> ReadAllEvidence(out int unreadableLineCount)
    {
        unreadableLineCount = 0;
        if (!Directory.Exists(_observationsRoot))
        {
            return [];
        }

        var records = new List<AcquisitionObservationRecord>();
        foreach (var path in Directory.EnumerateFiles(_observationsRoot, "evidence-*.jsonl"))
        {
            foreach (var line in File.ReadLines(path, Encoding.UTF8))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                try
                {
                    var payload = JsonSerializer.Deserialize<EvidencePayload>(line, SerializerOptions);
                    if (payload is null
                        || string.IsNullOrWhiteSpace(payload.ObservedText)
                        || string.IsNullOrWhiteSpace(payload.NormalizedLookupKey))
                    {
                        unreadableLineCount++;
                        continue;
                    }

                    records.Add(new AcquisitionObservationRecord
                    {
                        ProducerId = payload.ProducerId ?? AcquisitionObservationConstants.ProducerId,
                        ObservationKind = payload.ObservationKind ?? AcquisitionObservationConstants.ObservationKind,
                        ObservedText = payload.ObservedText,
                        // Re-normalize persisted evidence so journal recovery uses the current
                        // authoritative reference-lookup identity, including for older captures.
                        NormalizedLookupKey = ItemReferenceLookup.NormalizeLookupKey(payload.ObservedText),
                        CapturedAtUtc = payload.CapturedAtUtc,
                        FailedCatalogVersion = payload.FailedCatalogVersion ?? string.Empty,
                        GrammarSource = payload.GrammarSource ?? AcquisitionGrammarSource.ReceivedSimple,
                        FamilyHint = payload.FamilyHint,
                        ResolutionStateAtCapture = payload.ResolutionStateAtCapture
                    });
                }
                catch
                {
                    unreadableLineCount++;
                }
            }
        }

        return records;
    }

    public string GetEvidencePathForDate(DateTimeOffset capturedAtUtc) =>
        Path.Combine(
            _observationsRoot,
            $"evidence-{capturedAtUtc.UtcDateTime:yyyy-MM-dd}.jsonl");

    private static EvidencePayload ToEvidencePayload(AcquisitionObservationRecord observation) =>
        new()
        {
            ProducerId = observation.ProducerId,
            ObservationKind = observation.ObservationKind,
            ObservedText = observation.ObservedText,
            NormalizedLookupKey = observation.NormalizedLookupKey,
            CapturedAtUtc = observation.CapturedAtUtc,
            FailedCatalogVersion = observation.FailedCatalogVersion,
            GrammarSource = observation.GrammarSource,
            FamilyHint = observation.FamilyHint,
            ResolutionStateAtCapture = observation.ResolutionStateAtCapture
        };

    private sealed class EvidencePayload
    {
        public string? ProducerId { get; set; }

        public string? ObservationKind { get; set; }

        public required string ObservedText { get; set; }

        public required string NormalizedLookupKey { get; set; }

        public DateTimeOffset CapturedAtUtc { get; set; }

        public string? FailedCatalogVersion { get; set; }

        public string? GrammarSource { get; set; }

        public ReferenceItemFamily? FamilyHint { get; set; }

        public AcquisitionIdentityResolutionState ResolutionStateAtCapture { get; set; } =
            AcquisitionIdentityResolutionState.Unresolved;
    }

    public IReadOnlyList<AcquisitionObservationRecord> ReadEvidenceForKey(string normalizedKey)
    {
        var records = ReadAllEvidence(out _);
        return records
            .Where(record => string.Equals(
                record.NormalizedLookupKey,
                normalizedKey,
                StringComparison.Ordinal))
            .OrderBy(record => record.CapturedAtUtc)
            .Take(AcquisitionObservationConstants.MaxEvidenceSamplesPerKey)
            .ToArray();
    }
}
