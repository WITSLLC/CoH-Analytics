using System.IO;
using CoHAnalytics.Models;
using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Services;

public sealed class CharacterBuildLayoutSyncServiceTests
{
    [Fact]
    public void Sync_reads_expected_character_build_path_and_invokes_layout_parser()
    {
        var (service, repository, accountFolder) = CreateService();
        var character = repository.EstablishTrustedFromWelcome("acct-a", "Alpha Hero");
        var record = repository.TryGetRecord(character.RecordId!)!;
        Assert.True(CharacterShortId.TryParse(record.CharacterShortId, out var shortId));
        var buildPath = CharacterBuildImportService.GetBuildSaveFilePath(
            accountFolder,
            shortId);
        Directory.CreateDirectory(Path.GetDirectoryName(buildPath)!);
        File.WriteAllText(buildPath, """
            Alpha Hero: Level 12 Magic Class_Blaster
            Level 1: Blaster_Ranged Fire_Blast Flares
                EMPTY
            """);
        var contentBefore = File.ReadAllBytes(buildPath);
        var lastWriteBefore = File.GetLastWriteTimeUtc(buildPath);

        var result = service.SyncFromBuild("acct-a", accountFolder, character.RecordId!);

        Assert.True(result.IsSuccess);
        Assert.Equal(buildPath, result.BuildFilePath);
        Assert.Equal(new DateTimeOffset(lastWriteBefore), result.SourceLastWriteUtc);
        Assert.Equal(contentBefore, File.ReadAllBytes(buildPath));
        Assert.Equal(lastWriteBefore, File.GetLastWriteTimeUtc(buildPath));
        var power = Assert.Single(result.Snapshot!.Powers);
        Assert.Equal("Fire_Blast", power.RawPowerSetToken);
        Assert.True(Assert.Single(power.Slots).IsEmpty);
    }

    [Fact]
    public void Sync_reports_missing_build_file_without_throwing()
    {
        var (service, repository, accountFolder) = CreateService();
        var character = repository.EstablishTrustedFromWelcome("acct-a", "Alpha Hero");

        var result = service.SyncFromBuild("acct-a", accountFolder, character.RecordId!);

        Assert.Equal(CharacterBuildLayoutSyncStatus.MissingFile, result.Status);
        Assert.NotNull(result.BuildFilePath);
        Assert.Null(result.Snapshot);
    }

    [Fact]
    public void Sync_reports_malformed_build_without_discarding_the_failure_reason()
    {
        var (service, repository, accountFolder) = CreateService();
        var character = repository.EstablishTrustedFromWelcome("acct-a", "Alpha Hero");
        var record = repository.TryGetRecord(character.RecordId!)!;
        Assert.True(CharacterShortId.TryParse(record.CharacterShortId, out var shortId));
        var buildPath = CharacterBuildImportService.GetBuildSaveFilePath(
            accountFolder,
            shortId);
        Directory.CreateDirectory(Path.GetDirectoryName(buildPath)!);
        File.WriteAllText(buildPath, "Level nope: malformed");

        var result = service.SyncFromBuild("acct-a", accountFolder, character.RecordId!);

        Assert.Equal(CharacterBuildLayoutSyncStatus.Failed, result.Status);
        Assert.Contains("malformed or unsupported", result.Detail, StringComparison.Ordinal);
        Assert.Null(result.Snapshot);
    }

    private static (CharacterBuildLayoutSyncService Service, CharacterRepository Repository, string AccountFolder)
        CreateService()
    {
        var dataDirectory = Path.Combine(
            Path.GetTempPath(),
            "coh-analytics-build-layout-sync",
            Guid.NewGuid().ToString("n"));
        var accountFolder = Path.Combine(dataDirectory, "account");
        Directory.CreateDirectory(accountFolder);
        var repository = new CharacterRepository(new CharacterRepositoryOptions
        {
            DataDirectory = Path.Combine(dataDirectory, "repository")
        });
        return (new CharacterBuildLayoutSyncService(repository), repository, accountFolder);
    }
}
