using CoHAnalytics.Models;
using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Services;

public sealed class CharacterBadgeAcquisitionRepositoryTests
{
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
}
