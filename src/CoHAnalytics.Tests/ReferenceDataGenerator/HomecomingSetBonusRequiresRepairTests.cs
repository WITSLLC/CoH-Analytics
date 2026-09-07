using System.Text;
using CoHAnalytics.ReferenceData;
using CoHAnalytics.ReferenceDataGenerator;
using Microsoft.Data.Sqlite;

namespace CoHAnalytics.Tests.ReferenceDataGenerator;

[Collection(LiveInstallPromotionCollection.Name)]
public sealed class HomecomingSetBonusRequiresRepairTests
{
    private readonly LiveInstallPromotionFixture _promotion;

    public HomecomingSetBonusRequiresRepairTests(LiveInstallPromotionFixture promotion)
    {
        _promotion = promotion;
    }

    private static string LiveInstallRoot => LiveInstallTestEnvironment.InstallRoot;

    private static readonly string[] PreviouslyMissingSetIds =
    [
        "Aegis",
        "Blessing_of_the_Zephyr",
        "Brutes_Fury",
        "Call_to_Arms",
        "Command_of_the_Mastermind",
        "Commanding_Presence",
        "Edict_of_the_Master",
        "Expedient_Reinforcement",
        "Experienced_Marksman",
        "Fury_of_the_Gladiator",
        "Gift_of_the_Ancients",
        "Gladiators_Armor",
        "Gladiators_Javelin",
        "Gladiators_Net",
        "Gladiators_Strike",
        "Impervious_Skin",
        "Impervium_Armor",
        "Javelin_Volley",
        "Karma",
        "Kheldians_Grace",
        "Luck_of_the_Gambler",
        "Mark_of_Supremacy",
        "Panacea",
        "Preventive_Medicine",
        "Reactive_Defenses",
        "Rectified_Reticle",
        "Scrappers_Strike",
        "Shield_Wall",
        "Sovereign_Right",
        "Spiders_Bite",
        "Steadfast_Protection",
        "Superior_Brutes_Fury",
        "Superior_Command_of_the_Mastermind",
        "Superior_Kheldians_Grace",
        "Superior_Mark_of_Supremacy",
        "Superior_Scrappers_Strike",
        "Superior_Spiders_Bite",
        "Synapses_Shock",
        "Unbreakable_Guard",
        "Winters_Gift"
    ];

    [Fact]
    public void DiscoveryReader_PreservesEmptyRequiresBonus()
    {
        var data = CreateSetWithBonuses(
            new SyntheticBoostSetBonusRecord(2, 6, [], ["Set_Bonus.Set_Bonus.Improved_Recovery_7"]));

        var bonus = Assert.Single(HomecomingBoostSetsDiscoveryReader.Read(data).Single().Bonuses);
        Assert.Equal(2u, bonus.MinimumBoosts);
        Assert.Equal(6u, bonus.MaximumBoosts);
        Assert.Empty(bonus.RequiresTokens);
        Assert.Equal(["Set_Bonus.Set_Bonus.Improved_Recovery_7"], bonus.AutoPowerSourceIds);
    }

    [Fact]
    public void DiscoveryReader_PreservesPvPRequiresBonus()
    {
        var data = CreateSetWithBonuses(
            new SyntheticBoostSetBonusRecord(2, 6, ["isPVPMap?"], ["Set_Bonus.Set_Bonus.PvP_Only"]));

        var bonus = Assert.Single(HomecomingBoostSetsDiscoveryReader.Read(data).Single().Bonuses);
        Assert.Equal(["isPVPMap?"], bonus.RequiresTokens);
    }

    [Fact]
    public void DiscoveryReader_PreservesMultiTokenPieceGate()
    {
        var tokens =
            new[]
            {
                "Crafted_Aegis_F",
                "PowerBoostsSlotted>",
                "1",
                ">=",
                "Attuned_Aegis_F",
                "PowerBoostsSlotted>",
                "1",
                ">=",
                "||"
            };
        var data = CreateSetWithBonuses(
            new SyntheticBoostSetBonusRecord(1, 6, tokens, ["Set_Bonus.Global_Bonus.Aegis"]));

        var bonus = Assert.Single(HomecomingBoostSetsDiscoveryReader.Read(data).Single().Bonuses);
        Assert.Equal(tokens, bonus.RequiresTokens);
    }

    [Fact]
    public void DiscoveryReader_PreservesMultipleAutoPowers()
    {
        var data = CreateSetWithBonuses(
            new SyntheticBoostSetBonusRecord(
                6,
                6,
                [],
                [
                    "Set_Bonus.Set_Bonus.First",
                    "Set_Bonus.Set_Bonus.Second",
                    "Set_Bonus.Set_Bonus.Third"
                ]));

        var bonus = Assert.Single(HomecomingBoostSetsDiscoveryReader.Read(data).Single().Bonuses);
        Assert.Equal(3, bonus.AutoPowerSourceIds.Count);
    }

    [Fact]
    public void DiscoveryReader_PreservesUnusualMinMaxRange()
    {
        var data = CreateSetWithBonuses(
            new SyntheticBoostSetBonusRecord(1, 3, [], ["Set_Bonus.Set_Bonus.Unusual"]));

        var bonus = Assert.Single(HomecomingBoostSetsDiscoveryReader.Read(data).Single().Bonuses);
        Assert.Equal(1u, bonus.MinimumBoosts);
        Assert.Equal(3u, bonus.MaximumBoosts);
    }

    [Fact]
    public void DiscoveryReader_PreservesUnknownRequiresExpression()
    {
        var tokens = new[] { "FutureToken?", "CustomGate>", "2", "==" };
        var data = CreateSetWithBonuses(
            new SyntheticBoostSetBonusRecord(2, 4, tokens, ["Set_Bonus.Set_Bonus.Unknown"]));

        var bonus = Assert.Single(HomecomingBoostSetsDiscoveryReader.Read(data).Single().Bonuses);
        Assert.Equal(tokens, bonus.RequiresTokens);
    }

    [Fact]
    public void DiscoveryReader_TruncatedRequiresTokenList_FailsExplicitly()
    {
        var data = CreateCorruptBonusData(requiresCount: 2, requiresTokensWritten: 1);
        var exception = Assert.Throws<HomecomingBoostSetsDiscoveryException>(() =>
            HomecomingBoostSetsDiscoveryReader.Read(data));
        Assert.Contains("truncated", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DiscoveryReader_MalformedRequiresCount_FailsExplicitly()
    {
        var data = CreateCorruptBonusData(requiresCount: 512, requiresTokensWritten: 0);
        var exception = Assert.Throws<HomecomingBoostSetsDiscoveryException>(() =>
            HomecomingBoostSetsDiscoveryReader.Read(data));
        Assert.Contains("implausible", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DiscoveryReader_MalformedTrailingUnknown_FailsExplicitly()
    {
        var data = CreateCorruptBonusData(trailingUnknown: 1);
        var exception = Assert.Throws<HomecomingBoostSetsDiscoveryException>(() =>
            HomecomingBoostSetsDiscoveryReader.Read(data));
        Assert.Contains("trailing unknown", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ClassifyPattern_None_ForEmptyRequires()
    {
        Assert.Equal(
            ReferenceEnhancementSetBonusRequiresPattern.None,
            HomecomingSetBonusRequiresSupport.ClassifyPattern([]));
    }

    [Fact]
    public void ClassifyPattern_PvPMap_ForIsPvpMapToken()
    {
        Assert.Equal(
            ReferenceEnhancementSetBonusRequiresPattern.PvPMap,
            HomecomingSetBonusRequiresSupport.ClassifyPattern(["isPVPMap?"]));
    }

    [Fact]
    public void ClassifyPattern_PieceGate_DetectsPowerBoostsSlotted()
    {
        var tokens = new[] { "Crafted_Aegis_F", "PowerBoostsSlotted>", "1", ">=" };
        Assert.Equal(
            ReferenceEnhancementSetBonusRequiresPattern.PieceGate,
            HomecomingSetBonusRequiresSupport.ClassifyPattern(tokens));
    }

    [Fact]
    public void ClassifyPattern_UnknownExpression_ReturnsOther()
    {
        var tokens = new[] { "FutureToken?", "CustomGate>", "2", "==" };
        Assert.Equal(
            ReferenceEnhancementSetBonusRequiresPattern.Other,
            HomecomingSetBonusRequiresSupport.ClassifyPattern(tokens));
    }

    [Fact]
    public void ResolveRequiredEnhancementIds_MapsPieceTokenToLogicalEnhancement()
    {
        var memberGroups = new IReadOnlyList<string>[]
        {
            [
                "Boosts.Crafted_Aegis_F.Crafted_Aegis_F",
                "Boosts.Attuned_Aegis_F.Attuned_Aegis_F"
            ]
        };
        var setEnhancements = new[]
        {
            new HomecomingEnhancementCandidateRecord(
                "ENH-00099",
                "Aegis: Defense/Resistance",
                "Aegis",
                "SET-00099",
                nameof(HomecomingEnhancementMatchStatus.MatchedExisting),
                ["ENH-00099"],
                [
                    new HomecomingEnhancementSourceVariant(
                        "Boosts.Crafted_Aegis_F.Crafted_Aegis_F",
                        "P_AEGIS",
                        "Crafted"),
                    new HomecomingEnhancementSourceVariant(
                        "Boosts.Attuned_Aegis_F.Attuned_Aegis_F",
                        "P_AEGIS",
                        "Attuned")
                ])
        };

        var tokens = new[] { "Crafted_Aegis_F", "PowerBoostsSlotted>", "1", ">=" };
        var resolved = HomecomingSetBonusRequiresSupport.ResolveRequiredEnhancementIds(
            "Aegis",
            "SET-00099",
            tokens,
            memberGroups,
            setEnhancements);

        Assert.Equal(["ENH-00099"], resolved);
    }

    [Fact]
    public void RequiresTokens_PreserveExactOrderAndText()
    {
        var tokens = new[] { "Attuned_Aegis_F", "PowerBoostsSlotted>", "1", ">=" };
        Assert.Equal(tokens, tokens.ToArray());
        Assert.Equal("Attuned_Aegis_F", tokens[0]);
    }

    [Fact]
    public void JsonAndSqlite_Roundtrip_AllRequiresPatterns()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"coh-bonus-repair-{Guid.NewGuid():N}.db");
        try
        {
            var json = """
                {
                  "manifest": {
                    "catalogVersion": "item-ref-test",
                    "homecomingCompatibility": { "buildMin": "1", "buildMax": "1" }
                  },
                  "items": [
                    {
                      "catalogItemId": "ENH-90001",
                      "family": "Enhancement",
                      "subtype": "SetIO",
                      "currentDisplayName": "Fixture: Piece Gate",
                      "activeStatus": "Active",
                      "serverAvailability": [
                        { "serverKey": "Homecoming", "status": "Current" }
                      ],
                      "verificationStatus": "VerifiedMultiSource",
                      "enhancementSetId": "SET-90001"
                    }
                  ],
                  "aliases": [],
                  "enhancementSets": [
                    {
                      "catalogItemId": "SET-90001",
                      "currentDisplayName": "Fixture Set",
                      "activeStatus": "Active",
                      "serverAvailability": [
                        { "serverKey": "Homecoming", "status": "Current" }
                      ],
                      "verificationStatus": "VerifiedMultiSource",
                      "bonuses": [
                        {
                          "minimumBoosts": 2,
                          "maximumBoosts": 6,
                          "requiresPattern": "None",
                          "requiresTokens": [],
                          "requiredEnhancementIds": [],
                          "autoPowers": [
                            { "homecomingSourceId": "Set_Bonus.Set_Bonus.None", "displayHelp": "None bonus." }
                          ]
                        },
                        {
                          "minimumBoosts": 1,
                          "maximumBoosts": 6,
                          "requiresPattern": "PieceGate",
                          "requiresTokens": ["Crafted_Fixture_F", "PowerBoostsSlotted>", "1", ">="],
                          "requiredEnhancementIds": ["ENH-90001"],
                          "autoPowers": [
                            { "homecomingSourceId": "Set_Bonus.Global_Bonus.Fixture", "displayHelp": "Piece gate." }
                          ]
                        },
                        {
                          "minimumBoosts": 2,
                          "maximumBoosts": 6,
                          "requiresPattern": "PvPMap",
                          "requiresTokens": ["isPVPMap?"],
                          "requiredEnhancementIds": [],
                          "autoPowers": [
                            { "homecomingSourceId": "Set_Bonus.Set_Bonus.PvP", "displayHelp": "PvP bonus." }
                          ]
                        },
                        {
                          "minimumBoosts": 3,
                          "maximumBoosts": 5,
                          "requiresPattern": "Other",
                          "requiresTokens": ["FutureToken?", "CustomGate>", "2", "=="],
                          "requiredEnhancementIds": [],
                          "autoPowers": [
                            { "homecomingSourceId": "Set_Bonus.Set_Bonus.Other", "displayHelp": "Other bonus." }
                          ]
                        }
                      ]
                    }
                  ]
                }
                """;

            ItemReferenceCatalogImporter.ImportFromJsonStream(
                new MemoryStream(Encoding.UTF8.GetBytes(json)),
                databasePath);

            using (var connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadOnly;Pooling=False"))
            {
                connection.Open();
                Assert.Equal(4, QueryCount(connection, "SELECT COUNT(*) FROM EnhancementSetBonus;"));
                Assert.Equal(9, QueryCount(connection, "SELECT COUNT(*) FROM EnhancementSetBonusRequiresToken;"));
                Assert.Equal(1, QueryCount(connection, "SELECT COUNT(*) FROM EnhancementSetBonusRequiredEnhancement;"));
                Assert.Equal(4, QueryCount(connection, "SELECT COUNT(*) FROM EnhancementSetBonusPower;"));
            }

            var loadResult = SqliteReferenceCatalogLoader.Load(databasePath);
            Assert.True(loadResult.Succeeded, loadResult.FailureReason);
            var catalog = (ItemReferenceCatalog)ItemReferenceCatalog.FromLoadResult(loadResult);
            var set = catalog.EnhancementSets["SET-90001"];
            Assert.Equal(ReferenceEnhancementSetBonusRequiresPattern.None, set.Bonuses[0].RequiresPattern);
            Assert.Empty(set.Bonuses[0].RequiresTokens);
            Assert.Equal(ReferenceEnhancementSetBonusRequiresPattern.PieceGate, set.Bonuses[1].RequiresPattern);
            Assert.Equal(
                ["Crafted_Fixture_F", "PowerBoostsSlotted>", "1", ">="],
                set.Bonuses[1].RequiresTokens);
            Assert.Equal(["ENH-90001"], set.Bonuses[1].RequiredEnhancementIds);
            Assert.Equal(ReferenceEnhancementSetBonusRequiresPattern.PvPMap, set.Bonuses[2].RequiresPattern);
            Assert.Equal(["isPVPMap?"], set.Bonuses[2].RequiresTokens);
            Assert.Equal(ReferenceEnhancementSetBonusRequiresPattern.Other, set.Bonuses[3].RequiresPattern);
            Assert.Equal(
                ["FutureToken?", "CustomGate>", "2", "=="],
                set.Bonuses[3].RequiresTokens);
            Assert.Single(set.Bonuses[3].AutoPowers);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }

    [Fact]
    [Trait("Category", "LiveInstall")]
    public void LiveInstall_DiscoveryReader_NoLongerClearsRequiresSets()
    {
        var source = HomecomingStaticDataSourceDiscovery.Discover(LiveInstallRoot);
        var bytes = HomecomingPiggMemberReader.ReadMember(
            source.BinPiggPath,
            HomecomingBinaryFixtureBuilder.BoostSetsMemberName);
        var requiresSetIds = HomecomingBoostSetBonusRequiresDiscoveryReader.Read(bytes)
            .Where(set => set.Bonuses.Any(tier => tier.RequiresTokens.Count > 0))
            .Select(set => set.HomecomingSetId)
            .ToHashSet(StringComparer.Ordinal);
        var repaired = HomecomingBoostSetsDiscoveryReader.Read(bytes);

        Assert.Equal(40, requiresSetIds.Count);
        foreach (var setId in requiresSetIds)
        {
            var set = repaired.First(value => value.HomecomingSetId == setId);
            Assert.NotEmpty(set.Bonuses);
            Assert.Contains(set.Bonuses, tier => tier.RequiresTokens.Count > 0);
        }
    }

    [Fact]
    [Trait("Category", "LiveInstall")]
    public void LiveInstall_ProductionCatalog_RetainsPreviouslyMissingSetBonuses()
    {
        var catalogPath = Path.Combine(Path.GetTempPath(), $"coh-bonus-repair-live-{Guid.NewGuid():N}.json");
        try
        {
            using (var embedded = typeof(ItemReferenceCatalogFactory).Assembly.GetManifestResourceStream(
                       ItemReferenceCatalogFactory.ProductionCatalogResourceName)
                   ?? throw new InvalidOperationException("Embedded production catalog missing."))
            using (var output = File.Create(catalogPath))
            {
                embedded.CopyTo(output);
            }

            var result = _promotion.Promote(catalogPath);
            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(catalogPath));
            var sets = document.RootElement.GetProperty("enhancementSets")
                .EnumerateArray()
                .Where(set => set.TryGetProperty("homecomingSetId", out _))
                .ToDictionary(
                    set => set.GetProperty("homecomingSetId").GetString()!,
                    set => set,
                    StringComparer.Ordinal);

            var complete = 0;
            var incomplete = new List<string>();
            foreach (var setId in PreviouslyMissingSetIds)
            {
                if (!sets.TryGetValue(setId, out var setElement)
                    || !setElement.TryGetProperty("bonuses", out var bonuses)
                    || bonuses.GetArrayLength() == 0)
                {
                    incomplete.Add(setId);
                    continue;
                }

                complete++;
            }

            Assert.Equal(40, complete);
            Assert.Empty(incomplete);
            Assert.Equal(1086, result.Stats.BonusTiers);
            Assert.Equal(1138, result.Stats.BonusAutoPowers);
            Assert.Equal(1004, result.Stats.NoneRequiresTiers);
            Assert.Equal(37, result.Stats.PieceGateRequiresTiers);
            Assert.Equal(45, result.Stats.PvPMapRequiresTiers);
            Assert.Equal(0, result.Stats.OtherRequiresTiers);
            Assert.Equal(0, result.Stats.UnresolvedBonusHelp);
        }
        finally
        {
            if (File.Exists(catalogPath))
            {
                File.Delete(catalogPath);
            }
        }
    }

    private static byte[] CreateSetWithBonuses(params SyntheticBoostSetBonusRecord[] bonuses) =>
        HomecomingBinaryFixtureBuilder.CreateBoostSets(
            new SyntheticBoostSetRecord(
                "Fixture_Set",
                "P_SET",
                "ECUncommon",
                "ECMelee",
                10,
                50,
                [["Boosts.Crafted_Fixture_A.Crafted_Fixture_A"]],
                bonuses));

    private static byte[] CreateCorruptBonusData(
        uint requiresCount = 0,
        int requiresTokensWritten = 0,
        uint trailingUnknown = 0)
    {
        var stringPool = new SyntheticParse7StringPoolShim();
        using var bonusBody = new MemoryStream();
        using (var bonusWriter = new BinaryWriter(bonusBody, Encoding.UTF8, leaveOpen: true))
        {
            bonusWriter.Write(0u);
            bonusWriter.Write(2u);
            bonusWriter.Write(6u);
            bonusWriter.Write(requiresCount);
            for (var index = 0; index < requiresTokensWritten; index++)
            {
                bonusWriter.Write(stringPool.Add($"Token_{index}"));
            }

            bonusWriter.Write(1u);
            WriteInlineString(bonusWriter, "Set_Bonus.Set_Bonus.Corrupt");
            bonusWriter.Write(trailingUnknown);
        }

        using var setBody = new MemoryStream();
        using (var setWriter = new BinaryWriter(setBody, Encoding.UTF8, leaveOpen: true))
        {
            setWriter.Write(stringPool.Add("Corrupt_Set"));
            setWriter.Write(stringPool.Add("P_SET"));
            setWriter.Write(stringPool.Add("P_DESCRIPTION"));
            setWriter.Write(1u);
            setWriter.Write(stringPool.Add("ECUncommon"));
            WritePackedStringArray(setWriter, []);
            setWriter.Write(1u);
            using var groupBody = new MemoryStream();
            using (var groupWriter = new BinaryWriter(groupBody, Encoding.UTF8, leaveOpen: true))
            {
                WritePackedStringArray(
                    groupWriter,
                    ["Boosts.Crafted_Fixture_A.Crafted_Fixture_A"]);
            }

            setWriter.Write(checked((uint)groupBody.Length));
            setWriter.Write(groupBody.ToArray());
            setWriter.Write(1u);
            setWriter.Write(checked((uint)bonusBody.Length));
            setWriter.Write(bonusBody.ToArray());
            setWriter.Write(10u);
            setWriter.Write(50u);
            setWriter.Write(stringPool.Add(string.Empty));
        }

        using var definitionBlock = new MemoryStream();
        using (var writer = new BinaryWriter(definitionBlock, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(1u);
            writer.Write(checked((uint)setBody.Length));
            writer.Write(setBody.ToArray());
        }

        return CreateParse7(stringPool.ToArray(), definitionBlock.ToArray());
    }

    private static void WriteInlineString(BinaryWriter writer, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        writer.Write(checked((ushort)bytes.Length));
        writer.Write(bytes);
        while (writer.BaseStream.Position % 4 != 0)
        {
            writer.Write((byte)0);
        }
    }

    private static void WritePackedStringArray(BinaryWriter writer, IReadOnlyCollection<string> values)
    {
        writer.Write(checked((uint)values.Count));
        foreach (var value in values)
        {
            WriteInlineString(writer, value);
        }
    }

    private static byte[] CreateParse7(byte[] stringPool, byte[] definitionBlock)
    {
        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true);
        writer.Write("CrypticS"u8);
        writer.Write(0x12345678u);
        writer.Write((ushort)6);
        writer.Write("Parse7"u8);
        writer.Write(checked((uint)stringPool.Length));
        writer.Write(stringPool);
        while (output.Position % 4 != 0)
        {
            writer.Write((byte)0);
        }

        writer.Write(checked((uint)definitionBlock.Length));
        writer.Write(definitionBlock);
        writer.Flush();
        return output.ToArray();
    }

    private static int QueryCount(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private sealed class SyntheticParse7StringPoolShim
    {
        private readonly MemoryStream _stream = new();
        private readonly Dictionary<string, uint> _offsets = new(StringComparer.Ordinal);

        internal SyntheticParse7StringPoolShim()
        {
            _ = Add(string.Empty);
        }

        internal uint Add(string value)
        {
            if (_offsets.TryGetValue(value, out var existing))
            {
                return existing;
            }

            var offset = checked((uint)_stream.Position);
            _stream.Write(Encoding.UTF8.GetBytes(value));
            _stream.WriteByte(0);
            _offsets.Add(value, offset);
            return offset;
        }

        internal byte[] ToArray() => _stream.ToArray();
    }
}
