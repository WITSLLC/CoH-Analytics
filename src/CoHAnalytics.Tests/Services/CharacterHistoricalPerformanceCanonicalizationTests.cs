using System.Text.Json.Nodes;
using CoHAnalytics.Models;
using CoHAnalytics.Services;
using CoHAnalytics.Tests.Orchestration;

namespace CoHAnalytics.Tests.Services;

public sealed class CharacterHistoricalPerformanceCanonicalizationTests
{
    private static readonly DateTimeOffset Start =
        new(2026, 8, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Consolidated_character_history_resolves_through_survivor_and_retired_ids_after_restart()
    {
        var directory = CreateDataDirectory();
        try
        {
            var characters = CreateCharacterRepository(directory);
            var survivor = Assert.IsType<CharacterRecordId>(characters
                .EstablishTrustedFromWelcome("acct", "Survivor Hero", Start)
                .RecordId);
            var retired = Assert.IsType<CharacterRecordId>(characters
                .EstablishTrustedFromWelcome("acct", "Retired Hero", Start.AddMinutes(1))
                .RecordId);
            var observations = new CharacterPerformanceObservationRepository(directory);
            Assert.True(observations.Persist(CreateObservation(survivor, 100)).IsSuccess);
            Assert.True(observations.Persist(CreateObservation(
                retired,
                250,
                GameplaySessionId.CreateNew())).IsSuccess);

            Assert.True(characters.RecordTrustedObservedDisplayName(
                survivor,
                "Retired Hero",
                CharacterTrustState.TrustedFromWelcome).IsSuccess);
            Assert.Null(characters.TryGetRecord(retired));

            AssertCombinedHistory(characters, observations, survivor, retired);

            var reloadedCharacters = CreateCharacterRepository(directory);
            var reloadedObservations = new CharacterPerformanceObservationRepository(directory);
            AssertCombinedHistory(reloadedCharacters, reloadedObservations, survivor, retired);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Transitive_redirects_include_all_retired_ids_without_rewriting_observations()
    {
        var directory = CreateDataDirectory();
        try
        {
            var characters = CreateCharacterRepository(directory);
            var first = Assert.IsType<CharacterRecordId>(characters
                .EstablishTrustedFromWelcome("acct", "First Hero", Start)
                .RecordId);
            var second = Assert.IsType<CharacterRecordId>(characters
                .EstablishTrustedFromWelcome("acct", "Second Hero", Start.AddMinutes(1))
                .RecordId);
            var survivor = Assert.IsType<CharacterRecordId>(characters
                .EstablishTrustedFromWelcome("acct", "Final Hero", Start.AddMinutes(2))
                .RecordId);
            var observations = new CharacterPerformanceObservationRepository(directory);
            Assert.True(observations.Persist(CreateObservation(first, 100)).IsSuccess);
            Assert.True(observations.Persist(CreateObservation(
                second,
                200,
                GameplaySessionId.CreateNew())).IsSuccess);
            Assert.True(observations.Persist(CreateObservation(
                survivor,
                300,
                GameplaySessionId.CreateNew())).IsSuccess);

            Assert.True(characters.RecordTrustedObservedDisplayName(
                second,
                "First Hero",
                CharacterTrustState.TrustedFromWelcome).IsSuccess);
            Assert.True(characters.RecordTrustedObservedDisplayName(
                survivor,
                "First Hero",
                CharacterTrustState.TrustedFromWelcome).IsSuccess);

            Assert.Equal(survivor, characters.ResolveCanonicalRecordId(first));
            Assert.Equal(survivor, characters.ResolveCanonicalRecordId(second));
            Assert.Equal(
                new HashSet<CharacterRecordId> { first, second, survivor },
                characters.GetRecordIdsResolvingTo(first).ToHashSet());

            var service = new CharacterHistoricalPerformanceReadService(observations, characters);
            var lifetime = service.GetLifetime(first);
            Assert.Equal(survivor, lifetime.CharacterRecordId);
            Assert.Equal(3, lifetime.ObservationCount);
            Assert.Equal(600, lifetime.ExperienceGained);
            Assert.Equal(3, observations.GetAll().Select(item => item.CharacterRecordId).Distinct().Count());
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Persisted_redirect_cycle_is_rejected_without_infinite_resolution()
    {
        var directory = CreateDataDirectory();
        try
        {
            var characters = CreateCharacterRepository(directory);
            Assert.True(characters.EstablishTrustedFromWelcome("acct", "Cycle Guard", Start).IsSuccess);
            var first = CharacterRecordId.CreateNew();
            var second = CharacterRecordId.CreateNew();
            var path = characters.GetDiagnostics().PersistencePath;
            var document = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            document["retiredRecordRedirects"] = new JsonArray(
                new JsonObject
                {
                    ["retiredRecordId"] = first.ToString(),
                    ["survivingRecordId"] = second.ToString()
                },
                new JsonObject
                {
                    ["retiredRecordId"] = second.ToString(),
                    ["survivingRecordId"] = first.ToString()
                });
            File.WriteAllText(path, document.ToJsonString());

            var reloaded = CreateCharacterRepository(directory);

            Assert.Equal(second, reloaded.ResolveCanonicalRecordId(first));
            Assert.Equal(second, reloaded.ResolveCanonicalRecordId(second));
            Assert.True(reloaded.GetDiagnostics().SkippedCorruptRecordCount >= 1);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void Ordinary_rename_and_same_name_on_another_account_do_not_create_redirects()
    {
        var directory = CreateDataDirectory();
        try
        {
            var characters = CreateCharacterRepository(directory);
            var first = Assert.IsType<CharacterRecordId>(characters
                .EstablishTrustedFromWelcome("acct-a", "Shared Hero", Start)
                .RecordId);
            var otherAccount = Assert.IsType<CharacterRecordId>(characters
                .EstablishTrustedFromWelcome("acct-b", "Shared Hero", Start)
                .RecordId);

            Assert.True(characters.RecordTrustedObservedDisplayName(
                first,
                "Renamed Hero",
                CharacterTrustState.TrustedFromWelcome).IsSuccess);

            Assert.Equal(first, characters.ResolveCanonicalRecordId(first));
            Assert.Equal([first], characters.GetRecordIdsResolvingTo(first));
            Assert.Equal(otherAccount, characters.ResolveCanonicalRecordId(otherAccount));
            Assert.Equal([otherAccount], characters.GetRecordIdsResolvingTo(otherAccount));
            Assert.Equal(2, characters.Current.Records.Count);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    private static void AssertCombinedHistory(
        CharacterRepository characters,
        CharacterPerformanceObservationRepository observations,
        CharacterRecordId survivor,
        CharacterRecordId retired)
    {
        var service = new CharacterHistoricalPerformanceReadService(observations, characters);
        var canonical = service.GetLifetime(survivor);
        var retiredQuery = service.GetLifetime(retired);

        Assert.Equal(survivor, canonical.CharacterRecordId);
        Assert.Equal(2, canonical.ObservationCount);
        Assert.Equal(350, canonical.ExperienceGained);
        Assert.Equal(canonical, retiredQuery);
        var canonicalSegments = service.GetSegments(survivor);
        var retiredSegments = service.GetSegments(retired);
        Assert.Equal(2, canonicalSegments.Count);
        Assert.Equal(canonicalSegments, retiredSegments);
        Assert.All(canonicalSegments, segment =>
            Assert.Equal(survivor, segment.CanonicalCharacterRecordId));
        Assert.Contains(canonicalSegments, segment => segment.Observation.CharacterRecordId == survivor);
        Assert.Contains(canonicalSegments, segment => segment.Observation.CharacterRecordId == retired);
        Assert.Equal(2, observations.GetAll().Count);
        Assert.Contains(observations.GetAll(), item => item.CharacterRecordId == survivor);
        Assert.Contains(observations.GetAll(), item => item.CharacterRecordId == retired);
    }

    private static CharacterRepository CreateCharacterRepository(string directory) =>
        new(new CharacterRepositoryOptions
        {
            DataDirectory = directory,
            TimeProvider = new ManualTimeProvider(Start)
        });

    private static CharacterPerformanceObservation CreateObservation(
        CharacterRecordId character,
        long experience,
        GameplaySessionId? session = null) =>
        new()
        {
            GameplaySessionId = session ?? GameplaySessionId.CreateNew(),
            SegmentOrdinal = 0,
            CharacterRecordId = character,
            StartedAtUtc = Start,
            EndedAtUtc = Start.AddMinutes(30),
            ExperienceGained = experience
        };

    private static string CreateDataDirectory() =>
        Path.Combine(Path.GetTempPath(), "CoHAnalyticsTests", Guid.NewGuid().ToString("N"));

    private static void DeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch
        {
        }
    }
}
