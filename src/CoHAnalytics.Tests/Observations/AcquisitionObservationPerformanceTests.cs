using System.Diagnostics;
using System.Text.Json;
using CoHAnalytics.Observations;
using CoHAnalytics.ReferenceData;
using CoHAnalytics.Services;
using Xunit.Abstractions;

namespace CoHAnalytics.Tests.Observations;

[Collection(nameof(AcquisitionObservationPerformanceCollection))]
public sealed class AcquisitionObservationPerformanceTests
{
    private const int ClassificationAttempts = 20_000;
    private const int UnresolvedInterval = 100;

    private readonly ITestOutputHelper _output;

    public AcquisitionObservationPerformanceTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task Production_observation_path_remains_non_blocking_and_persists_real_workload()
    {
        var workload = BuildMixedWorkload();
        var catalog = ItemReferenceCatalogFactory.LoadProductionDatabase();
        Assert.True(catalog.IsLoaded, catalog.LoadFailureReason);
        var baseline = CreateProductionClassifier(catalog);
        WarmUp(baseline);
        var baselineElapsed = Measure(baseline, workload);

        var directory = CreateTempDirectory();
        var service = new AcquisitionObservationService(catalog, directory);
        var observedInner = CreateProductionClassifier(catalog);
        WarmUp(observedInner);
        var observed = new ObservingReceivedItemClassifier(
            observedInner,
            service,
            () => catalog.Manifest!.CatalogVersion);

        var observationElapsed = Measure(observed, workload);
        var expectedUnresolved = ClassificationAttempts / UnresolvedInterval;
        await WaitForJournalCountAsync(service, expectedUnresolved);
        var statusBeforeDispose = service.GetStatus();
        var entriesBeforeDispose = service.DebugJournalEntries;
        service.Dispose();

        var observationsRoot = ApplicationDataPaths.GetObservationsRoot(directory);
        var evidencePaths = Directory.GetFiles(observationsRoot, "evidence-*.jsonl");
        var evidenceRows = evidencePaths
            .SelectMany(path => File.ReadLines(path))
            .Count(line => !string.IsNullOrWhiteSpace(line));
        var journalPath = Path.Combine(observationsRoot, "journal.json");
        using (JsonDocument.Parse(File.ReadAllText(journalPath)))
        {
        }

        Assert.Equal(ClassificationAttempts, statusBeforeDispose.AcquisitionsSeen);
        Assert.Equal(expectedUnresolved, statusBeforeDispose.AcquisitionsUnresolved);
        Assert.Equal(0, statusBeforeDispose.DroppedObservations);
        Assert.Null(statusBeforeDispose.HealthDetail);
        Assert.Equal(AcquisitionCaptureHealth.Active, statusBeforeDispose.Health);
        Assert.Equal(6, entriesBeforeDispose.Count);
        Assert.Equal(expectedUnresolved, entriesBeforeDispose.Sum(entry => entry.OccurrenceCount));
        Assert.All(
            entriesBeforeDispose,
            entry => Assert.InRange(
                entry.EvidenceSampleCount,
                1,
                AcquisitionObservationConstants.MaxEvidenceSamplesPerKey));

        var repeated = entriesBeforeDispose.Single(
            entry => entry.NormalizedKey == ItemReferenceLookup.NormalizeLookupKey("Mystery Repeated"));
        Assert.Equal(expectedUnresolved / 2, repeated.OccurrenceCount);
        Assert.Equal(AcquisitionObservationConstants.MaxEvidenceSamplesPerKey, repeated.EvidenceSampleCount);
        Assert.Equal(
            entriesBeforeDispose.Sum(entry => entry.EvidenceSampleCount),
            evidenceRows);

        // Successful exclusive opens after disposal prove the writer released both resources.
        foreach (var path in evidencePaths.Append(journalPath))
        {
            using var stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            Assert.True(stream.Length > 0);
        }

        var baselineRate = ClassificationAttempts / baselineElapsed.TotalSeconds;
        var observationRate = ClassificationAttempts / observationElapsed.TotalSeconds;
        var deltaPercent = ((observationElapsed.TotalSeconds / baselineElapsed.TotalSeconds) - 1d) * 100d;

        _output.WriteLine($"Classification attempts: {ClassificationAttempts:N0}");
        _output.WriteLine($"Baseline elapsed: {baselineElapsed.TotalMilliseconds:F3} ms");
        _output.WriteLine($"Baseline throughput: {baselineRate:F0} attempts/sec");
        _output.WriteLine($"Observation elapsed: {observationElapsed.TotalMilliseconds:F3} ms");
        _output.WriteLine($"Observation throughput: {observationRate:F0} attempts/sec");
        _output.WriteLine($"Elapsed delta: {deltaPercent:+0.00;-0.00;0.00}%");
        _output.WriteLine($"Observation events enqueued: {expectedUnresolved - statusBeforeDispose.DroppedObservations}");
        _output.WriteLine($"Evidence rows written: {evidenceRows}");
        _output.WriteLine($"Journal identities: {entriesBeforeDispose.Count}");
        _output.WriteLine($"Dropped observations: {statusBeforeDispose.DroppedObservations}");
        _output.WriteLine("Writer failures: 0");

        // The absolute budget avoids a noisy ratio against the deliberately tiny in-memory baseline.
        Assert.True(
            observationElapsed <= baselineElapsed + TimeSpan.FromSeconds(1),
            $"Observation classifier path exceeded its one-second overhead budget: {observationElapsed} vs {baselineElapsed}.");

        Directory.Delete(directory, recursive: true);
    }

    [Fact]
    public void Saturated_queue_drops_without_blocking_or_throwing_into_classifier()
    {
        var directory = CreateTempDirectory();
        var catalog = ItemReferenceCatalogFactory.LoadProductionDatabase();
        Assert.True(catalog.IsLoaded, catalog.LoadFailureReason);
        using var service = new AcquisitionObservationService(
            catalog,
            directory,
            options: new AcquisitionObservationServiceOptions
            {
                QueueCapacity = 1,
                EnableBackgroundWriter = false
            });
        var classifier = new ObservingReceivedItemClassifier(
            CreateProductionClassifier(catalog),
            service,
            () => catalog.Manifest!.CatalogVersion);

        Assert.False(classifier.TryClassify("Unknown First", out _, out _));
        var stopwatch = Stopwatch.StartNew();
        var exception = Record.Exception(() => classifier.TryClassify("Unknown Dropped", out _, out _));
        stopwatch.Stop();

        Assert.Null(exception);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromMilliseconds(250));
        Assert.Equal(1, service.GetStatus().DroppedObservations);
    }

    private static string[] BuildMixedWorkload()
    {
        var workload = new string[ClassificationAttempts];
        var unresolvedIndex = 0;
        for (var index = 0; index < workload.Length; index++)
        {
            if ((index + 1) % UnresolvedInterval != 0)
            {
                workload[index] = "Luck Charm";
                continue;
            }

            workload[index] = unresolvedIndex % 2 == 0
                ? "Mystery Repeated"
                : $"Unknown Distinct {unresolvedIndex % 10}";
            unresolvedIndex++;
        }

        return workload;
    }

    private static TimeSpan Measure(IGameplayReceivedItemClassifier classifier, IReadOnlyList<string> workload)
    {
        var stopwatch = Stopwatch.StartNew();
        foreach (var item in workload)
        {
            classifier.TryClassify(item, out _, out _);
        }

        stopwatch.Stop();
        return stopwatch.Elapsed;
    }

    private static void WarmUp(IGameplayReceivedItemClassifier classifier)
    {
        for (var index = 0; index < 100; index++)
        {
            classifier.TryClassify("Luck Charm", out _, out _);
        }
    }

    private static GameplayReceivedItemClassifier CreateProductionClassifier(IItemReferenceCatalog catalog) =>
        new(new ItemReferenceReceivedItemTaxonomyCatalog(catalog));

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "coh-obs-performance", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static async Task WaitForJournalCountAsync(AcquisitionObservationService service, int expectedCount)
    {
        for (var attempt = 0; attempt < 500; attempt++)
        {
            if (service.DebugJournalEntries.Sum(entry => entry.OccurrenceCount) >= expectedCount)
            {
                return;
            }

            await Task.Delay(20);
        }

        throw new TimeoutException("The production observation writer did not drain the realistic benchmark workload.");
    }
}

[CollectionDefinition(nameof(AcquisitionObservationPerformanceCollection), DisableParallelization = true)]
public sealed class AcquisitionObservationPerformanceCollection;
