using System.IO;
using System.Text;
using CoHAnalytics.Models;
using CoHAnalytics.Services;
using CoHAnalytics.Tests.Orchestration;

namespace CoHAnalytics.Tests.Services;

public sealed class CharacterRepositoryBuildImportTests
{
    private static CharacterRepository CreateRepository(out string dataDirectory) =>
        CreateRepository(out dataDirectory, out _);

    private static CharacterRepository CreateRepository(out string dataDirectory, out ManualTimeProvider timeProvider)
    {
        dataDirectory = Path.Combine(Path.GetTempPath(), "coh-analytics-character-repo", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dataDirectory);
        timeProvider = new ManualTimeProvider();
        return new CharacterRepository(new CharacterRepositoryOptions
        {
            DataDirectory = dataDirectory,
            TimeProvider = timeProvider
        });
    }

    [Fact]
    public void New_character_receives_unique_eight_character_short_id()
    {
        var repository = CreateRepository(out _);
        var first = repository.EstablishTrustedFromWelcome("acct-1", "Alpha Hero");
        var second = repository.EstablishTrustedFromWelcome("acct-1", "Beta Hero");

        var firstRecord = repository.TryGetRecord(first.RecordId!)!;
        var secondRecord = repository.TryGetRecord(second.RecordId!)!;

        Assert.True(CharacterShortId.IsValid(firstRecord.CharacterShortId));
        Assert.True(CharacterShortId.IsValid(secondRecord.CharacterShortId));
        Assert.NotEqual(firstRecord.CharacterShortId, secondRecord.CharacterShortId);
        Assert.Equal(CharacterShortId.Length, firstRecord.CharacterShortId!.Length);
    }

    [Fact]
    public void Existing_character_without_short_id_is_backfilled_on_load()
    {
        var dataDirectory = Path.Combine(Path.GetTempPath(), "coh-analytics-character-repo", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dataDirectory);
        var repositoryPath = Path.Combine(dataDirectory, "characters.json");
        File.WriteAllText(
            repositoryPath,
            """
            {
              "schemaVersion": 1,
              "records": [
                {
                  "recordId": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                  "accountStableId": "acct-1",
                  "normalizedCharacterName": "alpha hero",
                  "currentDisplayName": "Alpha Hero",
                  "firstObservedAt": "2026-01-01T00:00:00Z",
                  "lastObservedAt": "2026-01-02T00:00:00Z",
                  "trustState": 1,
                  "provenance": {
                    "trustState": 1,
                    "establishedAt": "2026-01-01T00:00:00Z",
                    "inferredObservationCount": 0
                  },
                  "recordSchemaVersion": 1
                }
              ]
            }
            """);

        var repository = new CharacterRepository(new CharacterRepositoryOptions
        {
            DataDirectory = dataDirectory,
            TimeProvider = new ManualTimeProvider()
        });

        var record = Assert.Single(repository.Current.Records);
        Assert.True(CharacterShortId.IsValid(record.CharacterShortId));
    }

    [Fact]
    public void Short_id_survives_rename()
    {
        var repository = CreateRepository(out _);
        var established = repository.EstablishTrustedFromWelcome("acct-1", "Shadow Vanguard");
        var shortId = repository.TryGetRecord(established.RecordId!)!.CharacterShortId;

        repository.RecordTrustedObservedDisplayName(
            established.RecordId!,
            "D4wn's Vanguard",
            CharacterTrustState.TrustedFromWelcome);

        var renamed = repository.TryGetRecord(established.RecordId!)!;
        Assert.Equal(shortId, renamed.CharacterShortId);
        Assert.Equal("D4wn's Vanguard", renamed.CurrentDisplayName);
    }

    [Fact]
    public void Short_id_resolves_to_correct_character_record_id()
    {
        var repository = CreateRepository(out _);
        var established = repository.EstablishTrustedFromWelcome("acct-1", "Alpha Hero");
        var record = repository.TryGetRecord(established.RecordId!)!;
        var resolved = repository.TryGetRecordByShortId("acct-1", record.CharacterShortId!);

        Assert.NotNull(resolved);
        Assert.Equal(established.RecordId, resolved!.RecordId);
    }

    [Fact]
    public void Collision_detection_regenerates_short_id()
    {
        var repository = CreateRepository(out _);
        var shortIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < 32; index++)
        {
            var established = repository.EstablishTrustedFromWelcome("acct-1", $"Hero {index}");
            var shortId = repository.TryGetRecord(established.RecordId!)!.CharacterShortId!;
            Assert.True(CharacterShortId.IsValid(shortId));
            Assert.True(shortIds.Add(shortId));
        }
    }

    [Fact]
    public void Build_save_command_is_generated_correctly()
    {
        var shortId = CharacterShortId.FromCanonical("H7K2M9Q4");
        Assert.Equal("/buildsavefile H7K2M9Q4.txt", CharacterShortId.FormatBuildSaveCommand(shortId));
    }

    [Fact]
    public void Build_save_parser_reads_game_format_metadata()
    {
        const string build = """
            D4wn's Vanguard: Level 50 Magic Class_brute
            header
            header
            header
            Level 1: brute_fighting Punch
            Level 2: brute_armor Enrage
            """;

        Assert.True(HomecomingBuildSaveMetadataParser.TryParse(build, out var metadata));
        Assert.Equal("Brute", metadata.Archetype);
        Assert.Equal("Fighting", metadata.PrimaryPowerSet);
        Assert.Equal("Armor", metadata.SecondaryPowerSet);
        Assert.Null(metadata.CurrentBuildNumber);
    }

    [Fact]
    public void Build_save_parser_reads_explicit_primary_secondary_lines()
    {
        const string build = """
            Hero Plan by Mids' Reborn 3.9
            Shadow Vanguard: Level 50 Magic Brute
            Primary Power Set: Dark Melee
            Secondary Power Set: Invulnerability
            """;

        Assert.True(HomecomingBuildSaveMetadataParser.TryParse(build, out var metadata));
        Assert.Equal("Brute", metadata.Archetype);
        Assert.Equal("Dark Melee", metadata.PrimaryPowerSet);
        Assert.Equal("Invulnerability", metadata.SecondaryPowerSet);
    }

    [Fact]
    public void Import_uses_account_builds_path()
    {
        var accountDirectory = CreateAccountDirectory(out var buildsDirectory);
        var repository = CreateRepository(out _);
        var established = repository.EstablishTrustedFromWelcome("acct-1", "Alpha Hero");
        var shortId = repository.TryGetRecord(established.RecordId!)!.CharacterShortId!;
        WriteSampleBuild(Path.Combine(buildsDirectory, $"{shortId}.txt"));

        var importService = new CharacterBuildImportService(repository);
        var expectedPath = CharacterBuildImportService.GetBuildSaveFilePath(accountDirectory, CharacterShortId.FromCanonical(shortId));
        var result = importService.TryImportIfPresent("acct-1", accountDirectory, established.RecordId!);

        Assert.True(result.IsSuccess);
        Assert.Equal(expectedPath, result.BuildFilePath);
    }

    [Fact]
    public void Wrong_account_log_cannot_rename_character()
    {
        var accountADirectory = CreateAccountDirectory(out var buildsDirectoryA, "acct-a");
        var accountBDirectory = CreateAccountDirectory(out _, "acct-b");
        var logsB = Path.Combine(accountBDirectory, "Logs");
        Directory.CreateDirectory(logsB);
        File.WriteAllText(
            Path.Combine(logsB, "chatlog 2026-08-15.txt"),
            "[03:57] Welcome to City of Heroes, Wrong Name!\r\n");

        var repository = CreateRepository(out _, out var timeProvider);
        var established = repository.EstablishTrustedFromWelcome("acct-a", "Alpha Hero");
        var shortId = repository.TryGetRecord(established.RecordId!)!.CharacterShortId!;
        WriteSampleBuild(Path.Combine(buildsDirectoryA, $"{shortId}.txt"), archetypeToken: "Class_blaster");

        var importService = new CharacterBuildImportService(repository, timeProvider: timeProvider);
        var result = importService.TryImportIfPresent(
            "acct-a",
            accountADirectory,
            established.RecordId!,
            Path.Combine(logsB, "chatlog 2026-08-15.txt"));

        Assert.True(result.IsSuccess);
        var record = repository.TryGetRecord(established.RecordId!)!;
        Assert.Equal("Alpha Hero", record.CurrentDisplayName);
    }

    [Fact]
    public void Explicit_short_id_import_with_trusted_welcome_updates_current_name()
    {
        var accountDirectory = CreateAccountDirectory(out var buildsDirectory);
        var logsDirectory = Path.Combine(accountDirectory, "Logs");
        Directory.CreateDirectory(logsDirectory);
        var logPath = Path.Combine(logsDirectory, "chatlog 2026-08-15.txt");
        File.WriteAllText(logPath, "[03:57] Welcome to City of Heroes, D4wn's Vanguard!\r\n");

        var repository = CreateRepository(out _, out var timeProvider);
        var established = repository.EstablishTrustedFromWelcome("acct-1", "Shadow Vanguard");
        var shortId = repository.TryGetRecord(established.RecordId!)!.CharacterShortId!;
        WriteSampleBuild(Path.Combine(buildsDirectory, $"{shortId}.txt"));

        var importService = new CharacterBuildImportService(repository, timeProvider: timeProvider);
        var result = importService.TryImportIfPresent("acct-1", accountDirectory, established.RecordId!, logPath);

        Assert.True(result.IsSuccess);
        var record = repository.TryGetRecord(established.RecordId!)!;
        Assert.Equal("D4wn's Vanguard", record.CurrentDisplayName);
        Assert.Contains(record.Aliases, alias => alias.DisplayName == "Shadow Vanguard");
        Assert.Equal(established.RecordId, record.RecordId);
    }

    [Fact]
    public void Import_preserves_badges_and_history_after_rename()
    {
        var accountDirectory = CreateAccountDirectory(out var buildsDirectory);
        var logsDirectory = Path.Combine(accountDirectory, "Logs");
        Directory.CreateDirectory(logsDirectory);
        var logPath = Path.Combine(logsDirectory, "chatlog 2026-08-15.txt");
        File.WriteAllText(logPath, "[03:57] Welcome to City of Heroes, D4wn's Vanguard!\r\n");

        var repository = CreateRepository(out _, out var timeProvider);
        timeProvider.Set(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        var established = repository.EstablishTrustedFromWelcome("acct-1", "Shadow Vanguard");
        repository.RecordObservedLevel(established.RecordId!, 50, timeProvider.GetUtcNow());
        repository.RecordTrustedActivity(established.RecordId!, timeProvider.GetUtcNow());

        var shortId = repository.TryGetRecord(established.RecordId!)!.CharacterShortId!;
        WriteSampleBuild(Path.Combine(buildsDirectory, $"{shortId}.txt"));

        var importService = new CharacterBuildImportService(repository, timeProvider: timeProvider);
        importService.TryImportIfPresent("acct-1", accountDirectory, established.RecordId!, logPath);

        var record = repository.TryGetRecord(established.RecordId!)!;
        Assert.Equal(50, record.ObservedLevel);
        Assert.Equal("D4wn's Vanguard", record.CurrentDisplayName);
        Assert.Equal("Brute", record.Archetype);
    }

    [Fact]
    public void Import_does_not_replace_canonical_powerset_with_generic_category()
    {
        var repository = CreateRepository(out _);
        var established = repository.EstablishTrustedFromWelcome("acct-1", "Scout Primus");
        repository.ImportBuildMetadata(
            established.RecordId!,
            "Robotics",
            "Kinetics",
            "Mastermind",
            null,
            DateTimeOffset.Parse("2026-08-15T00:00:00Z"));

        repository.ImportBuildMetadata(
            established.RecordId!,
            "Summon",
            "Buff",
            "Mastermind",
            null,
            DateTimeOffset.Parse("2026-08-15T01:00:00Z"));

        var record = repository.TryGetRecord(established.RecordId!)!;
        Assert.Equal("Robotics", record.PrimaryPowerSet);
        Assert.Equal("Kinetics", record.SecondaryPowerSet);
    }

    [Fact]
    public void Missing_build_file_leaves_metadata_unimported_without_failure()
    {
        var accountDirectory = CreateAccountDirectory(out _);
        var repository = CreateRepository(out _);
        var established = repository.EstablishTrustedFromWelcome("acct-1", "Alpha Hero");

        var importService = new CharacterBuildImportService(repository);
        var result = importService.TryImportIfPresent("acct-1", accountDirectory, established.RecordId!);

        Assert.False(result.IsSuccess);
        Assert.Equal(CharacterBuildImportStatus.MissingFile, result.Status);
        var record = repository.TryGetRecord(established.RecordId!)!;
        Assert.Null(record.Archetype);
        Assert.Null(record.PrimaryPowerSet);
    }

    private static string CreateAccountDirectory(out string buildsDirectory, string accountStableId = "acct-1")
    {
        var accountDirectory = Path.Combine(Path.GetTempPath(), "coh-analytics-account", Guid.NewGuid().ToString("n"));
        buildsDirectory = Path.Combine(accountDirectory, "Builds");
        Directory.CreateDirectory(buildsDirectory);
        return accountDirectory;
    }

    private static void WriteSampleBuild(string path, string archetypeToken = "Class_brute")
    {
        var content = $"""
            Alpha Hero: Level 50 Magic {archetypeToken}
            header
            header
            header
            Level 1: brute_fighting Punch
            Level 2: brute_armor Enrage
            """;
        File.WriteAllText(path, content);
    }
}
