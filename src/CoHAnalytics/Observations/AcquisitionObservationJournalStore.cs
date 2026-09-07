using System.Text.Json;
using System.Text.Json.Serialization;
using CoHAnalytics.Models;

namespace CoHAnalytics.Observations;

internal sealed class AcquisitionObservationJournalStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _journalPath;

    public AcquisitionObservationJournalStore(string observationsRoot)
    {
        _journalPath = Path.Combine(observationsRoot, "journal.json");
    }

    public string JournalPath => _journalPath;

    public AcquisitionObservationJournalDocument LoadOrEmpty()
    {
        if (!File.Exists(_journalPath))
        {
            return new AcquisitionObservationJournalDocument();
        }

        try
        {
            var json = File.ReadAllText(_journalPath);
            var document = JsonSerializer.Deserialize<AcquisitionObservationJournalDocument>(json, SerializerOptions);
            if (document is null || document.SchemaVersion != AcquisitionObservationJournalDocument.CurrentSchemaVersion)
            {
                return new AcquisitionObservationJournalDocument();
            }

            return document;
        }
        catch
        {
            return new AcquisitionObservationJournalDocument();
        }
    }

    public void SaveNoThrow(AcquisitionObservationJournalDocument document)
    {
        TrySave(document, out _);
    }

    public bool TrySave(AcquisitionObservationJournalDocument document, out string? failureReason)
    {
        var tempPath = _journalPath + ".tmp";
        failureReason = null;

        try
        {
            var directory = Path.GetDirectoryName(_journalPath)!;
            Directory.CreateDirectory(directory);

            document.SchemaVersion = AcquisitionObservationJournalDocument.CurrentSchemaVersion;
            var json = JsonSerializer.Serialize(document, SerializerOptions);
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, _journalPath, overwrite: true);
            return true;
        }
        catch (Exception exception)
        {
            failureReason = exception.Message;
            try
            {
                File.Delete(tempPath);
            }
            catch
            {
            }

            return false;
        }
    }

    public AcquisitionObservationJournalDocument RebuildFromEvidence(
        List<AcquisitionObservationRecord> evidenceRecords,
        AcquisitionObservationJournalDocument? previousJournal)
    {
        var dispositionByKey = previousJournal?.Entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.NormalizedKey))
            .ToDictionary(entry => entry.NormalizedKey, entry => entry, StringComparer.Ordinal)
            ?? new Dictionary<string, AcquisitionObservationJournalEntry>(StringComparer.Ordinal);

        var rebuilt = new AcquisitionObservationJournalDocument
        {
            LastReconciledCatalogVersion = previousJournal?.LastReconciledCatalogVersion
        };

        var aggregated = new Dictionary<string, AcquisitionObservationJournalEntry>(StringComparer.Ordinal);
        foreach (var record in evidenceRecords.OrderBy(record => record.CapturedAtUtc))
        {
            if (!aggregated.TryGetValue(record.NormalizedLookupKey, out var entry))
            {
                var prior = dispositionByKey.GetValueOrDefault(record.NormalizedLookupKey);
                entry = new AcquisitionObservationJournalEntry
                {
                    NormalizedKey = record.NormalizedLookupKey,
                    ObservedText = record.ObservedText,
                    ProducerId = record.ProducerId,
                    ObservationKind = record.ObservationKind,
                    OccurrenceCount = 0,
                    FirstSeenUtc = record.CapturedAtUtc,
                    LastSeenUtc = record.CapturedAtUtc,
                    Status = prior?.Status ?? ObservationDispositionStatus.New,
                    DeveloperNotes = prior?.DeveloperNotes,
                    ResolvedCatalogItemId = prior?.ResolvedCatalogItemId,
                    ResolvingCatalogVersion = prior?.ResolvingCatalogVersion,
                    FamilyHint = record.FamilyHint ?? prior?.FamilyHint,
                    ResolutionState = prior?.ResolutionState ?? record.ResolutionStateAtCapture,
                    EvidenceSampleCount = 0
                };
                aggregated[record.NormalizedLookupKey] = entry;
            }

            entry.OccurrenceCount++;
            if (record.ResolutionStateAtCapture is AcquisitionIdentityResolutionState.PresentedUnresolved)
            {
                entry.ResolutionState = AcquisitionIdentityResolutionState.PresentedUnresolved;
                entry.FamilyHint = record.FamilyHint ?? entry.FamilyHint;
            }
            entry.LastSeenUtc = record.CapturedAtUtc;
            if (record.CapturedAtUtc < entry.FirstSeenUtc)
            {
                entry.FirstSeenUtc = record.CapturedAtUtc;
            }

            if (!string.IsNullOrWhiteSpace(record.FailedCatalogVersion)
                && !entry.FailedCatalogVersions.Contains(record.FailedCatalogVersion, StringComparer.Ordinal))
            {
                entry.FailedCatalogVersions.Add(record.FailedCatalogVersion);
            }

            if (entry.EvidenceSampleCount < AcquisitionObservationConstants.MaxEvidenceSamplesPerKey)
            {
                entry.EvidenceSampleCount++;
            }
        }

        rebuilt.Entries = aggregated.Values
            .OrderByDescending(entry => entry.LastSeenUtc)
            .ToList();

        return rebuilt;
    }
}
