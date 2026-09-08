using CoHAnalytics.Models;
using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Services;

public sealed class CharacterBadgeAcquisitionRepositoryTests
{
    [Fact]
    public void Log_receipt_round_trip_exposes_title_provenance_and_first_observed_time()
    {
        var dir = CreateDataDirectory();

        try
        {
            var recordId = CharacterRecordId.CreateNew();
            var observedAt = new DateTimeOffset(2026, 9, 8, 12, 34, 56, TimeSpan.Zero);
            var repository = CreateRepository(dir);

            Assert.True(repository.RecordAcquisition(
                recordId,
                "acct-1",
                "BAD-03191",
                "Defiler",
                observedAt,
                CharacterBadgeAcquisitionProvenance.LogReceipt).IsSuccess);

            var reloaded = CreateRepository(dir);
            var entry = Assert.Single(reloaded.GetSnapshot(recordId).Acquisitions);
            Assert.Equal("BAD-03191", entry.CatalogItemId);
            Assert.Equal("Defiler", entry.ObservedTitle);
            Assert.Equal(observedAt, entry.FirstObservedAt);
            Assert.Equal(CharacterBadgeAcquisitionProvenance.LogReceipt, entry.Provenance);
        }
        finally
        {
            TryDeleteDirectory(dir);
        }
    }

    [Fact]
    public void Explicit_log_receipt_upgrades_build_metadata_without_changing_ownership_or_time()
    {
        var dir = CreateDataDirectory();

        try
        {
            var recordId = CharacterRecordId.CreateNew();
            var firstObservedAt = new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);
            var repository = CreateRepository(dir);
            Assert.True(repository.RecordAcquisition(
                recordId,
                "acct-1",
                "BAD-03191",
                "Purifier / Defiler",
                firstObservedAt,
                CharacterBadgeAcquisitionProvenance.BuildSourceId).IsSuccess);

            Assert.True(repository.RecordAcquisition(
                recordId,
                "acct-1",
                "BAD-03191",
                "Defiler",
                firstObservedAt.AddHours(1),
                CharacterBadgeAcquisitionProvenance.LogReceipt).IsSuccess);

            var snapshot = repository.GetSnapshot(recordId);
            Assert.Single(snapshot.AcquiredBadgeIds);
            var entry = Assert.Single(snapshot.Acquisitions);
            Assert.Equal("Defiler", entry.ObservedTitle);
            Assert.Equal(firstObservedAt, entry.FirstObservedAt);
            Assert.Equal(CharacterBadgeAcquisitionProvenance.LogReceipt, entry.Provenance);
        }
        finally
        {
            TryDeleteDirectory(dir);
        }
    }

    [Fact]
    public void Build_and_second_log_receipt_do_not_replace_first_explicit_log_title()
    {
        var dir = CreateDataDirectory();

        try
        {
            var recordId = CharacterRecordId.CreateNew();
            var firstObservedAt = new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);
            var repository = CreateRepository(dir);
            Assert.True(repository.RecordAcquisition(
                recordId,
                "acct-1",
                "BAD-03191",
                "Defiler",
                firstObservedAt,
                CharacterBadgeAcquisitionProvenance.LogReceipt).IsSuccess);
            Assert.True(repository.RecordAcquisition(
                recordId,
                "acct-1",
                "BAD-03191",
                "Purifier / Defiler",
                firstObservedAt.AddMinutes(1),
                CharacterBadgeAcquisitionProvenance.BuildSourceId).IsSuccess);
            Assert.True(repository.RecordAcquisition(
                recordId,
                "acct-1",
                "BAD-03191",
                "Purifier",
                firstObservedAt.AddMinutes(2),
                CharacterBadgeAcquisitionProvenance.LogReceipt).IsSuccess);

            var entry = Assert.Single(repository.GetSnapshot(recordId).Acquisitions);
            Assert.Equal("Defiler", entry.ObservedTitle);
            Assert.Equal(firstObservedAt, entry.FirstObservedAt);
            Assert.Equal(CharacterBadgeAcquisitionProvenance.LogReceipt, entry.Provenance);
            Assert.Single(repository.GetAcquiredBadgeIds(recordId));
        }
        finally
        {
            TryDeleteDirectory(dir);
        }
    }

    [Fact]
    public void Missing_persisted_provenance_loads_as_legacy_unknown()
    {
        var dir = CreateDataDirectory();

        try
        {
            var recordId = CharacterRecordId.CreateNew();
            var repositoryPath = ApplicationDataPaths.GetBadgeAcquisitionsPath(dir);
            Directory.CreateDirectory(Path.GetDirectoryName(repositoryPath)!);
            File.WriteAllText(repositoryPath, $$"""
                {
                  "schemaVersion": 1,
                  "characters": [{
                    "characterRecordId": "{{recordId}}",
                    "accountStableId": "acct-1",
                    "acquisitions": [{
                      "catalogItemId": "BAD-03191",
                      "firstObservedAt": "2026-09-08T12:00:00+00:00",
                      "observedTitle": "Purifier"
                    }]
                  }]
                }
                """);

            var repository = CreateRepository(dir);
            var snapshot = repository.GetSnapshot(recordId);
            Assert.Equal(["BAD-03191"], snapshot.AcquiredBadgeIds);
            var entry = Assert.Single(snapshot.Acquisitions);
            Assert.Equal("Purifier", entry.ObservedTitle);
            Assert.Equal(CharacterBadgeAcquisitionProvenance.LegacyUnknown, entry.Provenance);
        }
        finally
        {
            TryDeleteDirectory(dir);
        }
    }

    [Fact]
    public void RecordAcquisition_is_idempotent_and_persists_across_restart()
    {
        var dir = Path.Combine(Path.GetTempPath(), "coh-badge-repo", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);

        try
        {
            var recordId = CharacterRecordId.CreateNew();
            var observedAt = new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero);

            CharacterBadgeAcquisitionRepository repository;
            repository = new CharacterBadgeAcquisitionRepository(new CharacterBadgeAcquisitionRepositoryOptions
            {
                DataDirectory = dir
            });
            Assert.True(repository.RecordAcquisition(
                    recordId,
                    "acct-1",
                    "BAD-00001",
                    "Atlas Tour Guide",
                    observedAt).IsSuccess);
                Assert.True(repository.RecordAcquisition(
                    recordId,
                    "acct-1",
                    "BAD-00001",
                    "Atlas Tour Guide",
                    observedAt.AddMinutes(5)).IsSuccess);
                Assert.True(repository.IsBadgeAcquired(recordId, "BAD-00001"));
            Assert.Equal(["BAD-00001"], repository.GetAcquiredBadgeIds(recordId));

            var reloaded = new CharacterBadgeAcquisitionRepository(new CharacterBadgeAcquisitionRepositoryOptions
            {
                DataDirectory = dir
            });
            Assert.True(reloaded.IsBadgeAcquired(recordId, "BAD-00001"));
            Assert.Equal(["BAD-00001"], reloaded.GetAcquiredBadgeIds(recordId));
        }
        finally
        {
            TryDeleteDirectory(dir);
        }
    }

    [Fact]
    public void RecordAcquisition_rejects_account_mismatch()
    {
        var dir = Path.Combine(Path.GetTempPath(), "coh-badge-repo", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);

        try
        {
            var repository = new CharacterBadgeAcquisitionRepository(new CharacterBadgeAcquisitionRepositoryOptions
            {
                DataDirectory = dir
            });
            var recordId = CharacterRecordId.CreateNew();
            Assert.True(repository.RecordAcquisition(
                recordId,
                "acct-1",
                "BAD-00001",
                "Atlas Tour Guide",
                DateTimeOffset.UtcNow).IsSuccess);

            var result = repository.RecordAcquisition(
                recordId,
                "acct-2",
                "BAD-00002",
                "Other Badge",
                DateTimeOffset.UtcNow);

            Assert.False(result.IsSuccess);
            Assert.Equal(["BAD-00001"], repository.GetAcquiredBadgeIds(recordId));
        }
        finally
        {
            TryDeleteDirectory(dir);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch
        {
        }
    }

    private static string CreateDataDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "coh-badge-repo", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static CharacterBadgeAcquisitionRepository CreateRepository(string dataDirectory) =>
        new(new CharacterBadgeAcquisitionRepositoryOptions { DataDirectory = dataDirectory });
}
