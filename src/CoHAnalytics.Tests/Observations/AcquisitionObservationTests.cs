using System.Text.Json;
using CoHAnalytics.Models;
using CoHAnalytics.Observations;
using CoHAnalytics.ReferenceData;
using CoHAnalytics.Services;
using CoHAnalytics.Tests.ReferenceData;
using CoHAnalytics.ViewModels.Workspaces;

namespace CoHAnalytics.Tests.Observations;

public sealed class AcquisitionObservationTests
{
    [Fact]
    public void Observing_classifier_family_known_without_catalog_identity_records_presented_unresolved()
    {
        var sink = new RecordingObservationSink();
        var inner = new GameplayReceivedItemClassifier(CreateTaxonomyCatalog());
        var classifier = new ObservingReceivedItemClassifier(
            inner,
            sink,
            () => "catalog-v1");

        Assert.True(classifier.TryClassify("Luck Charm", out var category, out _));
        Assert.Equal(GameplaySessionRewardCategory.Salvage, category);
        var observation = Assert.Single(sink.Records);
        Assert.Equal(ReferenceItemFamily.Salvage, observation.FamilyHint);
        Assert.Equal(AcquisitionIdentityResolutionState.PresentedUnresolved, observation.ResolutionStateAtCapture);
        Assert.Equal(1, sink.Service.GetStatus().AcquisitionsSeen);
        Assert.Equal(0, sink.Service.GetStatus().AcquisitionsResolved);
        Assert.Equal(1, sink.Service.GetStatus().AcquisitionsPresentedUnresolved);
    }

    [Fact]
    public void Observing_classifier_unknown_item_records_once_and_returns_false()
    {
        var sink = new RecordingObservationSink();
        var inner = new GameplayReceivedItemClassifier(CreateTaxonomyCatalog());
        var classifier = new ObservingReceivedItemClassifier(
            inner,
            sink,
            () => "catalog-v1");

        Assert.False(classifier.TryClassify("Mystery Thing", out var category, out var metadata));
        Assert.Equal(GameplaySessionRewardCategory.ReceivedItem, category);
        Assert.Null(metadata);
        Assert.Single(sink.Records);
        Assert.Equal("Mystery Thing", sink.Records[0].ObservedText);
    }

    [Fact]
    public void Recipe_presentation_without_identity_is_presented_unresolved()
    {
        var result = new GameplayReceivedItemClassifier(CreateTaxonomyCatalog())
            .Classify("Unknown Enhancement (Recipe)");

        Assert.Equal(GameplaySessionRewardCategory.Recipe, result.PresentationCategory);
        Assert.Equal(ReferenceItemFamily.Recipe, result.FamilyHint);
        Assert.Equal(AcquisitionIdentityResolutionState.PresentedUnresolved, result.ResolutionState);
        Assert.Null(result.CatalogItemId);
    }

    [Fact]
    public void Catalog_backed_recipe_is_resolved()
    {
        var catalog = ItemReferenceCatalogFactory.LoadEmbeddedBootstrap();
        var result = new GameplayReceivedItemClassifier(
            new ItemReferenceReceivedItemTaxonomyCatalog(catalog),
            catalog).Classify("Fixture Recipe Sample (Recipe)");

        Assert.Equal(GameplaySessionRewardCategory.Recipe, result.PresentationCategory);
        Assert.Equal(AcquisitionIdentityResolutionState.Resolved, result.ResolutionState);
        Assert.Equal("REC-00001", result.CatalogItemId);
    }

    [Fact]
    public void Presented_unresolved_uses_human_readable_queue_labels()
    {
        var row = new AcquisitionObservationRowViewModel(new AcquisitionObservationListItem
        {
            NormalizedKey = "FIXTURE (RECIPE)",
            ObservedText = "Fixture (Recipe)",
            FamilyHint = ReferenceItemFamily.Recipe,
            ResolutionState = AcquisitionIdentityResolutionState.PresentedUnresolved,
            Disposition = ObservationDispositionStatus.New,
            OccurrenceCount = 1,
            FirstSeenUtc = DateTimeOffset.UtcNow,
            LastSeenUtc = DateTimeOffset.UtcNow,
            EvidenceSampleCount = 1
        });

        Assert.Equal("Recipe", row.FamilyLabel);
        Assert.Equal("Presentation Known", row.PresentationStatusLabel);
        Assert.Equal("Catalog Missing", row.CatalogStatusLabel);
        Assert.DoesNotContain("PresentedUnresolved", row.PresentationStatusLabel, StringComparison.Ordinal);
    }

    [Fact]
    public void Observing_classifier_sink_failure_still_returns_classification_result()
    {
        var inner = new GameplayReceivedItemClassifier(CreateTaxonomyCatalog());
        var classifier = new ObservingReceivedItemClassifier(
            inner,
            new ThrowingObservationSink(),
            () => "catalog-v1");

        Assert.False(classifier.TryClassify("Mystery Thing", out _, out _));
        Assert.True(classifier.TryClassify("Luck Charm", out var category, out _));
        Assert.Equal(GameplaySessionRewardCategory.Salvage, category);
    }

    [Fact]
    public async Task Counters_track_seen_resolved_unresolved_and_dropped()
    {
        var directory = CreateTempDirectory();
        var catalog = ItemReferenceCatalogFactory.LoadEmbeddedBootstrap();
        using var service = new AcquisitionObservationService(
            catalog,
            directory,
            options: new AcquisitionObservationServiceOptions
            {
                QueueCapacity = 1,
                EnableBackgroundWriter = false
            });

        service.RecordClassificationAttempt(true);
        service.RecordClassificationAttempt(false);

        var observation = CreateObservation("Unknown A", "catalog-v1");
        Assert.True(service.TryRecordUnresolvedAcquisition(observation));
        Assert.False(service.TryRecordUnresolvedAcquisition(CreateObservation("Unknown B", "catalog-v1")));

        var status = service.GetStatus();
        Assert.Equal(2, status.AcquisitionsSeen);
        Assert.Equal(1, status.AcquisitionsResolved);
        Assert.Equal(1, status.AcquisitionsUnresolved);
        Assert.Equal(1, status.DroppedObservations);

        await Task.Delay(50);
    }

    [Fact]
    public async Task Unresolved_item_writes_evidence_and_updates_journal()
    {
        var directory = CreateTempDirectory();
        var catalog = ItemReferenceCatalogFactory.LoadEmbeddedBootstrap();
        using var service = new AcquisitionObservationService(catalog, directory);

        service.TryRecordUnresolvedAcquisition(CreateObservation("Mystery Thing", catalog.Manifest!.CatalogVersion));

        await WaitForCaptureAsync(service);

        var files = Directory.GetFiles(ApplicationDataPaths.GetObservationsRoot(directory), "evidence-*.jsonl");
        Assert.NotEmpty(files);

        var line = File.ReadAllLines(files[0]).Single(line => !string.IsNullOrWhiteSpace(line));
        using var document = JsonDocument.Parse(line);
        Assert.Equal("Mystery Thing", document.RootElement.GetProperty("observedText").GetString());
        Assert.False(document.RootElement.TryGetProperty("accountName", out _));
        Assert.False(document.RootElement.TryGetProperty("characterName", out _));
        Assert.False(document.RootElement.TryGetProperty("sessionId", out _));
        Assert.False(document.RootElement.TryGetProperty("rawLine", out _));

        var entry = service.DebugJournalEntries.Single();
        Assert.Equal("Mystery Thing", entry.ObservedText);
        Assert.Equal(1, entry.OccurrenceCount);
        Assert.Equal(ObservationDispositionStatus.New, entry.Status);
    }

    [Fact]
    public void Concurrent_detail_read_waits_for_evidence_append_and_returns_coherent_evidence()
    {
        var directory = CreateTempDirectory();
        var catalog = ItemReferenceCatalogFactory.LoadEmbeddedBootstrap();
        using var appendReached = new ManualResetEventSlim();
        using var allowAppend = new ManualResetEventSlim();
        var service = new AcquisitionObservationService(
            catalog,
            directory,
            options: new AcquisitionObservationServiceOptions
            {
                BeforeEvidenceAppend = () =>
                {
                    appendReached.Set();
                    if (!allowAppend.Wait(TimeSpan.FromSeconds(10)))
                    {
                        throw new TimeoutException("Evidence append was not released by the test.");
                    }
                }
            });
        Thread? reader = null;
        AcquisitionObservationDetail? detail = null;
        Exception? readFailure = null;

        try
        {
            var observation = CreateObservation("Concurrent Mystery", catalog.Manifest!.CatalogVersion);
            Assert.True(service.TryRecordUnresolvedAcquisition(observation));
            Assert.True(
                appendReached.Wait(TimeSpan.FromSeconds(10)),
                "The background writer did not reach the evidence append boundary.");

            reader = new Thread(() =>
            {
                try
                {
                    detail = service.GetObservationDetail(observation.NormalizedLookupKey);
                }
                catch (Exception ex)
                {
                    readFailure = ex;
                }
            })
            {
                IsBackground = true
            };
            reader.Start();

            Assert.True(
                SpinWait.SpinUntil(
                    () => (reader.ThreadState & ThreadState.WaitSleepJoin) != 0 || !reader.IsAlive,
                    TimeSpan.FromSeconds(10)),
                "The detail reader did not reach the publication boundary.");
            Assert.True(reader.IsAlive, "The detail read completed before evidence persistence finished.");

            allowAppend.Set();
            Assert.True(
                reader.Join(TimeSpan.FromSeconds(10)),
                "The detail reader did not complete after evidence persistence finished.");

            Assert.Null(readFailure);
            Assert.NotNull(detail);
            var evidence = Assert.Single(detail.Evidence);
            Assert.Equal("Concurrent Mystery", evidence.ObservedText);
        }
        finally
        {
            allowAppend.Set();
            if (reader?.IsAlive == true)
            {
                reader.Join(TimeSpan.FromSeconds(10));
            }

            service.Dispose();
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Duplicate_observations_aggregate_and_cap_evidence_samples()
    {
        var directory = CreateTempDirectory();
        var catalog = ItemReferenceCatalogFactory.LoadEmbeddedBootstrap();
        using var service = new AcquisitionObservationService(catalog, directory);

        for (var i = 0; i < 7; i++)
        {
            service.TryRecordUnresolvedAcquisition(CreateObservation("Mystery Thing", catalog.Manifest!.CatalogVersion));
        }

        await WaitForCaptureAsync(service, expectedCount: 7);

        var entry = service.DebugJournalEntries.Single();
        Assert.Equal(7, entry.OccurrenceCount);
        Assert.Equal(AcquisitionObservationConstants.MaxEvidenceSamplesPerKey, entry.EvidenceSampleCount);

        var files = Directory.GetFiles(ApplicationDataPaths.GetObservationsRoot(directory), "evidence-*.jsonl");
        var lines = File.ReadAllLines(files[0]).Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
        Assert.Equal(AcquisitionObservationConstants.MaxEvidenceSamplesPerKey, lines.Length);
    }

    [Fact]
    public async Task Lookup_equivalent_observed_strings_share_one_normalized_journal_identity()
    {
        var directory = CreateTempDirectory();
        var catalog = ItemReferenceCatalogFactory.LoadEmbeddedProduction();
        Assert.True(catalog.TryResolve("Luck Charm", out var canonical));
        Assert.True(catalog.TryResolve("  luck charm  ", out var equivalent));
        Assert.Equal(canonical.Item.CatalogItemId, equivalent.Item.CatalogItemId);
        using var service = new AcquisitionObservationService(catalog, directory);
        var classifier = new ObservingReceivedItemClassifier(
            new GameplayReceivedItemClassifier(CreateTaxonomyCatalog()),
            service,
            () => catalog.Manifest!.CatalogVersion);

        var variants = new[]
        {
            "  Mystery: Thing  ",
            "mystery: thing",
            "MYSTERY: THING"
        };

        Assert.Single(variants.Select(ItemReferenceLookup.NormalizeLookupKey).Distinct(StringComparer.Ordinal));
        foreach (var variant in variants)
        {
            Assert.False(classifier.TryClassify(variant, out _, out _));
        }

        await WaitForCaptureAsync(service, expectedCount: variants.Length);

        var entry = Assert.Single(service.DebugJournalEntries);
        Assert.Equal(ItemReferenceLookup.NormalizeLookupKey(variants[0]), entry.NormalizedKey);
        Assert.Equal(variants.Length, entry.OccurrenceCount);
    }

    [Fact]
    public void Existing_case_variant_journal_entries_are_normalized_and_merged_on_startup()
    {
        var directory = CreateTempDirectory();
        var catalog = ItemReferenceCatalogFactory.LoadEmbeddedBootstrap();
        var observationsRoot = ApplicationDataPaths.GetObservationsRoot(directory);
        Directory.CreateDirectory(observationsRoot);
        var now = DateTimeOffset.UtcNow;
        var journal = new AcquisitionObservationJournalDocument
        {
            LastReconciledCatalogVersion = catalog.Manifest!.CatalogVersion,
            Entries =
            [
                new AcquisitionObservationJournalEntry
                {
                    NormalizedKey = "Mystery: Thing",
                    ObservedText = "Mystery: Thing",
                    ProducerId = AcquisitionObservationConstants.ProducerId,
                    ObservationKind = AcquisitionObservationConstants.ObservationKind,
                    OccurrenceCount = 3,
                    EvidenceSampleCount = 3,
                    FirstSeenUtc = now.AddMinutes(-2),
                    LastSeenUtc = now.AddMinutes(-1)
                },
                new AcquisitionObservationJournalEntry
                {
                    NormalizedKey = "mystery: thing",
                    ObservedText = "mystery: thing",
                    ProducerId = AcquisitionObservationConstants.ProducerId,
                    ObservationKind = AcquisitionObservationConstants.ObservationKind,
                    OccurrenceCount = 4,
                    EvidenceSampleCount = 4,
                    FirstSeenUtc = now.AddMinutes(-1),
                    LastSeenUtc = now
                }
            ]
        };
        new AcquisitionObservationJournalStore(observationsRoot).SaveNoThrow(journal);

        using var service = new AcquisitionObservationService(catalog, directory);

        var entry = Assert.Single(service.DebugJournalEntries);
        Assert.Equal(ItemReferenceLookup.NormalizeLookupKey("Mystery: Thing"), entry.NormalizedKey);
        Assert.Equal(7, entry.OccurrenceCount);
        Assert.Equal(AcquisitionObservationConstants.MaxEvidenceSamplesPerKey, entry.EvidenceSampleCount);

        var persisted = new AcquisitionObservationJournalStore(observationsRoot).LoadOrEmpty();
        Assert.Single(persisted.Entries);
    }

    [Fact]
    public void Lookup_normalization_preserves_existing_punctuation_semantics()
    {
        var withPunctuation = ItemReferenceLookup.NormalizeLookupKey(" Mystery: Thing ");
        var withoutPunctuation = ItemReferenceLookup.NormalizeLookupKey("mystery thing");

        Assert.NotEqual(withPunctuation, withoutPunctuation);
    }

    [Fact]
    public void Reconciliation_resolves_entries_when_catalog_version_changes()
    {
        var directory = CreateTempDirectory();
        var catalog = ItemReferenceCatalogFactory.LoadEmbeddedProduction();
        Assert.True(catalog.IsLoaded);

        var observationsRoot = ApplicationDataPaths.GetObservationsRoot(directory);
        Directory.CreateDirectory(observationsRoot);
        var journal = new AcquisitionObservationJournalDocument
        {
            LastReconciledCatalogVersion = "stale-catalog-version",
            Entries =
            [
                new AcquisitionObservationJournalEntry
                {
                    NormalizedKey = "Essence of the Earth",
                    ObservedText = "Essence of the Earth",
                    ProducerId = AcquisitionObservationConstants.ProducerId,
                    ObservationKind = AcquisitionObservationConstants.ObservationKind,
                    OccurrenceCount = 2,
                    FirstSeenUtc = DateTimeOffset.UtcNow.AddHours(-1),
                    LastSeenUtc = DateTimeOffset.UtcNow,
                    Status = ObservationDispositionStatus.New
                }
            ]
        };
        new AcquisitionObservationJournalStore(observationsRoot).SaveNoThrow(journal);

        using var service = new AcquisitionObservationService(catalog, directory);
        var entry = service.DebugJournalEntries.Single();
        Assert.Equal(ObservationDispositionStatus.Resolved, entry.Status);
        Assert.NotNull(entry.ResolvedCatalogItemId);
        Assert.Equal(catalog.Manifest!.CatalogVersion, entry.ResolvingCatalogVersion);
    }

    [Fact]
    public void Malformed_journal_rebuilds_from_evidence()
    {
        var directory = CreateTempDirectory();
        var observationsRoot = ApplicationDataPaths.GetObservationsRoot(directory);
        Directory.CreateDirectory(observationsRoot);
        File.WriteAllText(Path.Combine(observationsRoot, "journal.json"), "{ not valid json");

        var evidencePath = Path.Combine(observationsRoot, "evidence-2026-08-09.jsonl");
        var payload = JsonSerializer.Serialize(new
        {
            producerId = AcquisitionObservationConstants.ProducerId,
            observationKind = AcquisitionObservationConstants.ObservationKind,
            observedText = "Mystery Thing",
            normalizedLookupKey = "Mystery Thing",
            capturedAtUtc = DateTimeOffset.UtcNow,
            failedCatalogVersion = "catalog-v1",
            grammarSource = AcquisitionGrammarSource.ReceivedSimple
        });
        File.WriteAllText(evidencePath, payload + Environment.NewLine);

        var catalog = ItemReferenceCatalogFactory.LoadEmbeddedBootstrap();
        using var service = new AcquisitionObservationService(catalog, directory);
        var entry = service.DebugJournalEntries.Single();
        Assert.Equal("Mystery Thing", entry.ObservedText);
        Assert.Equal(1, entry.OccurrenceCount);
        Assert.Equal(ObservationDispositionStatus.New, entry.Status);
    }

    [Fact]
    public void Truncated_evidence_line_is_ignored_without_crashing()
    {
        var directory = CreateTempDirectory();
        var observationsRoot = ApplicationDataPaths.GetObservationsRoot(directory);
        Directory.CreateDirectory(observationsRoot);
        var evidencePath = Path.Combine(observationsRoot, "evidence-2026-08-09.jsonl");
        File.WriteAllText(evidencePath, "{\"observedText\":\"Good Item\",\"normalizedLookupKey\":\"Good Item\",\"capturedAtUtc\":\"2026-08-09T10:00:00Z\"}\n{\"truncated");

        var catalog = ItemReferenceCatalogFactory.LoadEmbeddedBootstrap();
        using var service = new AcquisitionObservationService(catalog, directory);
        Assert.Single(service.DebugJournalEntries);
        Assert.Equal("Good Item", service.DebugJournalEntries[0].ObservedText);
    }

  private static InMemoryReceivedItemTaxonomyCatalog CreateTaxonomyCatalog()
  {
    var catalog = new InMemoryReceivedItemTaxonomyCatalog();
    catalog.AddSalvage(
        "Luck Charm",
        new ReceivedItemClassificationMetadata { SalvageRarity = "Common" });
    return catalog;
  }

  private static AcquisitionObservationRecord CreateObservation(string text, string catalogVersion) =>
      new()
      {
          ProducerId = AcquisitionObservationConstants.ProducerId,
          ObservationKind = AcquisitionObservationConstants.ObservationKind,
          ObservedText = text,
          NormalizedLookupKey = ItemReferenceLookup.NormalizeLookupKey(text),
          CapturedAtUtc = DateTimeOffset.UtcNow,
          FailedCatalogVersion = catalogVersion,
          GrammarSource = AcquisitionGrammarSource.ReceivedSimple
      };

  private static string CreateTempDirectory()
  {
    var path = Path.Combine(Path.GetTempPath(), "coh-obs-tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(path);
    return path;
  }

  private static async Task WaitForCaptureAsync(IAcquisitionObservationService service, int expectedCount = 1)
  {
    for (var attempt = 0; attempt < 100; attempt++)
    {
      if (service is AcquisitionObservationService concrete
          && concrete.DebugJournalEntries.Sum(entry => entry.OccurrenceCount) >= expectedCount)
      {
        return;
      }

      await Task.Delay(20);
    }

    throw new TimeoutException("Observation capture did not complete.");
  }

  private sealed class RecordingObservationSink : IAcquisitionObservationService
  {
    public RecordingObservationSink()
    {
      Service = new AcquisitionObservationService(
          ItemReferenceCatalogFactory.LoadEmbeddedBootstrap(),
          CreateTempDirectory(),
          options: new AcquisitionObservationServiceOptions { EnableBackgroundWriter = false });
    }

    public List<AcquisitionObservationRecord> Records { get; } = [];

    public AcquisitionObservationService Service { get; }

    public event EventHandler? Changed { add { } remove { } }

    public AcquisitionObservationStatus GetStatus() => Service.GetStatus();

        public void RecordClassificationAttempt(bool resolved) => Service.RecordClassificationAttempt(resolved);

        public void RecordClassificationAttempt(AcquisitionIdentityResolutionState resolutionState) =>
            Service.RecordClassificationAttempt(resolutionState);

    public bool TryRecordUnresolvedAcquisition(AcquisitionObservationRecord observation)
    {
      Records.Add(observation);
      return Service.TryRecordUnresolvedAcquisition(observation);
    }
  }

  private sealed class ThrowingObservationSink : IAcquisitionObservationService
  {
    public event EventHandler? Changed { add { } remove { } }

    public AcquisitionObservationStatus GetStatus() => new();

    public void RecordClassificationAttempt(bool resolved)
    {
    }

    public bool TryRecordUnresolvedAcquisition(AcquisitionObservationRecord observation) =>
        throw new InvalidOperationException("sink failure");
  }
}
