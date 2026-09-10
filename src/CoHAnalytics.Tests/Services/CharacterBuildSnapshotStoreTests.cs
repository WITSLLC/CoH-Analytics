using System.Text.Json.Nodes;
using CoHAnalytics.Models;
using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Services;

public sealed class CharacterBuildSnapshotStoreTests
{
    [Fact]
    public void Save_and_load_round_trip_preserves_layout_metadata_order_duplicates_and_empty_slots()
    {
        var dataDirectory = CreateDataDirectory();
        var recordId = CharacterRecordId.FromGuid(Guid.Parse("b41a7552-19b0-4c8b-bfcb-6ac6dcd75246"));
        var store = CreateStore(dataDirectory);
        var expected = CreateSnapshot(recordId);

        var save = store.Save(expected);
        var load = store.TryLoad(recordId);

        Assert.True(save.IsSuccess);
        Assert.Equal(
            Path.Combine(dataDirectory, "builds", $"{recordId}.json"),
            save.Path);
        Assert.True(load.IsSuccess);
        Assert.Equal(recordId, load.Snapshot!.CharacterRecordId);
        Assert.Equal("DAS4MNTU", load.Snapshot.CharacterShortId);
        Assert.Equal("BIue Devil", load.Snapshot.CharacterName);
        Assert.Equal(expected.SyncedAtUtc, load.Snapshot.SyncedAtUtc);
        Assert.Equal("DAS4MNTU.txt", load.Snapshot.SourceBuildFile);
        Assert.Equal(expected.SourceLastWriteUtc, load.Snapshot.SourceLastWriteUtc);
        Assert.Equal("Class_Brute", load.Snapshot.Layout.RawClassToken);

        var powers = load.Snapshot.Layout.Powers;
        Assert.Equal([0, 1, 2], powers.Select(power => power.SourceOrder));
        Assert.Equal(
            ["Scorch", "Blazing_Aura", "Long_Jump"],
            powers.Select(power => power.RawPowerToken));
        var slots = powers[0].Slots;
        Assert.Equal([0, 1, 2, 3], slots.Select(slot => slot.SlotOrder));
        Assert.Equal(2, slots.Count(slot => slot.RawEnhancementToken == "Crafted_Damage"));
        Assert.True(slots[2].IsEmpty);
        Assert.Null(slots[2].RawEnhancementToken);
        Assert.True(slots[1].IsAttuned);
        Assert.Equal(50, slots[3].BaseEnhancementLevel);
        Assert.Equal(5, slots[3].BoostValue);

        var json = JsonNode.Parse(File.ReadAllText(save.Path))!.AsObject();
        Assert.Equal(CharacterBuildSnapshotStore.PersistenceSchemaVersion, json["schemaVersion"]!.GetValue<int>());
        Assert.Equal(recordId.ToString(), json["characterRecordId"]!.GetValue<string>());
        Assert.NotNull(json["snapshot"]);
    }

    [Fact]
    public void Missing_corrupt_and_unsupported_files_fail_independently_and_gracefully()
    {
        var dataDirectory = CreateDataDirectory();
        var goodId = CharacterRecordId.CreateNew();
        var corruptId = CharacterRecordId.CreateNew();
        var unsupportedId = CharacterRecordId.CreateNew();
        var missingId = CharacterRecordId.CreateNew();
        var store = CreateStore(dataDirectory);
        Assert.True(store.Save(CreateSnapshot(goodId)).IsSuccess);
        Directory.CreateDirectory(Path.Combine(dataDirectory, "builds"));
        File.WriteAllText(store.GetSnapshotPath(corruptId), "{not-json");
        File.WriteAllText(
            store.GetSnapshotPath(unsupportedId),
            $$"""{"schemaVersion": {{CharacterBuildSnapshotStore.PersistenceSchemaVersion + 1}}}""");

        var corrupt = store.TryLoad(corruptId);
        var unsupported = store.TryLoad(unsupportedId);
        var missing = store.TryLoad(missingId);
        var good = store.TryLoad(goodId);

        Assert.Equal(CharacterBuildSnapshotLoadOutcome.Corrupt, corrupt.Outcome);
        Assert.Equal(CharacterBuildSnapshotLoadOutcome.UnsupportedSchema, unsupported.Outcome);
        Assert.Equal(CharacterBuildSnapshotLoadOutcome.NotFound, missing.Outcome);
        Assert.True(good.IsSuccess);
        Assert.Equal("Scorch", good.Snapshot!.Layout.Powers[0].RawPowerToken);
    }

    [Fact]
    public void Snapshot_with_different_embedded_record_id_is_rejected_without_rekeying()
    {
        var dataDirectory = CreateDataDirectory();
        var requestedId = CharacterRecordId.CreateNew();
        var differentId = CharacterRecordId.CreateNew();
        var store = CreateStore(dataDirectory);
        Assert.True(store.Save(CreateSnapshot(differentId)).IsSuccess);
        Directory.CreateDirectory(Path.Combine(dataDirectory, "builds"));
        File.Copy(store.GetSnapshotPath(differentId), store.GetSnapshotPath(requestedId));

        var result = store.TryLoad(requestedId);

        Assert.Equal(CharacterBuildSnapshotLoadOutcome.Corrupt, result.Outcome);
        Assert.Null(result.Snapshot);
    }

    [Fact]
    public void Failed_atomic_replacement_retains_the_previous_good_snapshot()
    {
        var dataDirectory = CreateDataDirectory();
        var recordId = CharacterRecordId.CreateNew();
        var workingStore = CreateStore(dataDirectory);
        var original = CreateSnapshot(recordId);
        Assert.True(workingStore.Save(original).IsSuccess);
        var originalJson = File.ReadAllText(workingStore.GetSnapshotPath(recordId));
        var failingStore = new CharacterBuildSnapshotStore(new CharacterBuildSnapshotStoreOptions
        {
            DataDirectory = dataDirectory,
            AtomicReplace = (_, _) => throw new IOException("simulated replacement failure")
        });
        var replacement = original with
        {
            SyncedAtUtc = original.SyncedAtUtc.AddDays(1),
            CharacterName = "Different informational header"
        };

        var result = failingStore.Save(replacement);

        Assert.False(result.IsSuccess);
        Assert.Equal(originalJson, File.ReadAllText(workingStore.GetSnapshotPath(recordId)));
        Assert.Equal("BIue Devil", workingStore.TryLoad(recordId).Snapshot!.CharacterName);
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(dataDirectory, "builds"), "*.tmp"));
    }

    private static CharacterBuildSnapshotStore CreateStore(string dataDirectory) =>
        new(new CharacterBuildSnapshotStoreOptions { DataDirectory = dataDirectory });

    private static CharacterBuildSnapshot CreateSnapshot(CharacterRecordId recordId) =>
        new()
        {
            CharacterRecordId = recordId,
            CharacterShortId = "DAS4MNTU",
            CharacterName = "BIue Devil",
            SyncedAtUtc = new DateTimeOffset(2026, 9, 10, 14, 15, 16, TimeSpan.Zero),
            SourceBuildFile = "DAS4MNTU.txt",
            SourceLastWriteUtc = new DateTimeOffset(2026, 9, 5, 10, 20, 30, TimeSpan.Zero),
            Layout = new HomecomingBuildLayoutSnapshot(
                "BIue Devil",
                38,
                "Class_Brute",
                [
                    new HomecomingBuildPowerSnapshot(
                        1,
                        "Brute_Melee",
                        "Fiery_Melee",
                        "Scorch",
                        0,
                        [
                            new HomecomingBuildSlotSnapshot(false, "Crafted_Damage", false, 35, null, 0),
                            new HomecomingBuildSlotSnapshot(false, "Crafted_Damage", true, null, null, 1),
                            new HomecomingBuildSlotSnapshot(true, null, false, null, null, 2),
                            new HomecomingBuildSlotSnapshot(false, "Crafted_Recharge", false, 50, 5, 3)
                        ]),
                    new HomecomingBuildPowerSnapshot(
                        1,
                        "Brute_Defense",
                        "Fiery_Aura",
                        "Blazing_Aura",
                        1,
                        [new HomecomingBuildSlotSnapshot(true, null, false, null, null, 0)]),
                    new HomecomingBuildPowerSnapshot(
                        8,
                        "Pool",
                        "Leaping",
                        "Long_Jump",
                        2,
                        [new HomecomingBuildSlotSnapshot(true, null, false, null, null, 0)])
                ])
        };

    private static string CreateDataDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "coh-analytics-build-snapshot-tests",
            Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(path);
        return path;
    }
}
