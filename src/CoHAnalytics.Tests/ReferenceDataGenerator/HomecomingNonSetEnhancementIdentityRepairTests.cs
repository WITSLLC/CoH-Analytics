using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CoHAnalytics.ReferenceData;
using CoHAnalytics.ReferenceDataGenerator;

namespace CoHAnalytics.Tests.ReferenceDataGenerator;

[Collection(LiveInstallPromotionCollection.Name)]
public sealed class HomecomingNonSetEnhancementIdentityRepairTests
{
    private readonly LiveInstallPromotionFixture _promotion;

    public HomecomingNonSetEnhancementIdentityRepairTests(LiveInstallPromotionFixture promotion)
    {
        _promotion = promotion;
    }

    private static string LiveInstallRoot => LiveInstallTestEnvironment.InstallRoot;

    private static readonly string[] OriginBoostTypes =
    [
        "Science", "Mutation", "Magic", "Technology", "Natural"
    ];

    private static readonly string[] TrainingRangeBoostsAllowed =
    [
        "Range", "Natural", "Technology", "Magic", "Mutation", "Science"
    ];

    private static readonly string[] TrainingRecoveryBoostsAllowed =
    [
        "Recovery", "Natural", "Technology", "Magic", "Mutation", "Science"
    ];

    [Theory]
    [InlineData("Boosts.Crafted_Accuracy", "Accuracy", "ENH-00001", 10)]
    [InlineData("Boosts.Crafted_Damage", "Damage", "ENH-01295", 10)]
    [InlineData("Boosts.Crafted_Recharge", "Recharge", "ENH-00017", 10)]
    [InlineData("Boosts.Crafted_Endurance_Discount", "EnduranceDiscount", "ENH-00007", 10)]
    [InlineData("Boosts.Crafted_Hold", "Hold", "ENH-00011", 10)]
    [InlineData("Boosts.Crafted_Res_Damage", "Res_Damage", "ENH-00018", 10)]
    public void Create_CraftedSeries_GroupsByStructuralSeriesIdentity(
        string seriesPrefix,
        string aspectType,
        string expectedEnhId,
        int expectedSourceCount)
    {
        var boosts = BuildCraftedSeriesBoosts(seriesPrefix, aspectType);
        var discovery = BuildCraftedSeriesDiscovery(boosts, aspectType);
        var messages = boosts
            .Select((boost, index) => (boost.DisplayNameMessageKey, $"Invention: {aspectType} {index}"))
            .ToArray();

        var candidate = CreateCandidate(
            boosts,
            [],
            messages,
            [BuildCurrentIdentity(expectedEnhId, boosts)],
            discoveryBoosts: discovery);

        var enhancement = Assert.Single(candidate.Enhancements);
        Assert.Equal(expectedEnhId, enhancement.AppOwnedId);
        Assert.Equal(expectedSourceCount, enhancement.SourceVariants.Count);
        Assert.Null(enhancement.EnhancementSetAppOwnedId);
    }

    [Fact]
    public void Create_CraftedSeries_RegenerationDecreaseEdgeCase_HasNineSources()
    {
        var boosts = new[]
        {
            Boost("Boosts.Crafted_Decreased_Regeneration.Crafted_Decreased_Regeneration", "P0"),
            Boost("Boosts.Crafted_Decreased_Regeneration_1.Crafted_Decreased_Regeneration_1", "P1"),
            Boost("Boosts.Crafted_Decreased_Regeneration_2.Crafted_Decreased_Regeneration_2", "P2"),
            Boost("Boosts.Crafted_Decreased_Regeneration_3.Crafted_Decreased_Regeneration_3", "P3"),
            Boost("Boosts.Crafted_Decreased_Regeneration_4.Crafted_Decreased_Regeneration_4", "P4"),
            Boost("Boosts.Crafted_Decreased_Regeneration_5.Crafted_Decreased_Regeneration_5", "P5"),
            Boost("Boosts.Crafted_Decreased_Regeneration_6.Crafted_Decreased_Regeneration_6", "P6"),
            Boost("Boosts.Crafted_Decreased_Regeneration_7.Crafted_Decreased_Regeneration_7", "P7"),
            Boost("Boosts.Crafted_Decreased_Regeneration_8.Crafted_Decreased_Regeneration_8", "P8")
        };
        var allowed = OriginBoostTypes.ToArray();
        var discovery = boosts.ToDictionary(
            boost => boost.HomecomingSourceId,
            boost => Discovery(
                boost.HomecomingSourceId,
                ["Regeneration"],
                allowed,
                icon: "E_ICON_GEN_DAMAGE_01.tga"),
            StringComparer.Ordinal);
        var messages = boosts.Select(boost => (boost.DisplayNameMessageKey, "Regeneration Decrease")).ToArray();

        var candidate = CreateCandidate(
            boosts,
            [],
            messages,
            [BuildCurrentIdentity("ENH-01296", boosts)],
            discoveryBoosts: discovery);

        var enhancement = Assert.Single(candidate.Enhancements);
        Assert.Equal("ENH-01296", enhancement.AppOwnedId);
        Assert.Equal(9, enhancement.SourceVariants.Count);
    }

    [Fact]
    public void Create_OriginStructuralPair_WithIdenticalHelp_MergesByApplicability()
    {
        var cone = Boost("Boosts.Generic_Cone.Generic_Cone", "P_CONE", "Generic");
        var range = Boost("Boosts.Generic_Range.Generic_Range", "P_RANGE", "Generic");
        var discovery = new Dictionary<string, HomecomingBoostDiscoveryRecord>(StringComparer.Ordinal)
        {
            [cone.HomecomingSourceId] = Discovery(
                cone.HomecomingSourceId,
                ["Range"],
                TrainingRangeBoostsAllowed,
                helpKey: "P_HELP_A",
                icon: "E_ICON_GEN_RANGE_01.tga"),
            [range.HomecomingSourceId] = Discovery(
                range.HomecomingSourceId,
                ["Range"],
                TrainingRangeBoostsAllowed,
                helpKey: "P_HELP_A",
                icon: "E_ICON_GEN_RANGE_01.tga")
        };

        var candidate = CreateCandidate(
            [cone, range],
            [],
            [
                ("P_CONE", "Training: Range Increase"),
                ("P_RANGE", "Training: Range Increase"),
                ("P_HELP_A", "Increases the range of a power.")
            ],
            [BuildCurrentIdentity("ENH-01344", [cone, range])],
            discoveryBoosts: discovery);

        var enhancement = Assert.Single(candidate.Enhancements);
        Assert.Equal("ENH-01344", enhancement.AppOwnedId);
        Assert.Equal(2, enhancement.SourceVariants.Count);
    }

    [Fact]
    public void Create_OriginStructuralPair_WithHelpVariance_StillMerges()
    {
        var drain = Boost("Boosts.Generic_Drain_Endurance.Generic_Drain_Endurance", "P_DRAIN", "Generic");
        var recovery = Boost("Boosts.Generic_Recovery.Generic_Recovery", "P_RECOVERY", "Generic");
        var discovery = new Dictionary<string, HomecomingBoostDiscoveryRecord>(StringComparer.Ordinal)
        {
            [drain.HomecomingSourceId] = Discovery(
                drain.HomecomingSourceId,
                ["Recovery"],
                TrainingRecoveryBoostsAllowed,
                helpKey: "P_HELP_DRAIN",
                icon: "E_ICON_GEN_ENDMOD_01.tga"),
            [recovery.HomecomingSourceId] = Discovery(
                recovery.HomecomingSourceId,
                ["Recovery"],
                TrainingRecoveryBoostsAllowed,
                helpKey: "P_HELP_RECOVERY",
                icon: "E_ICON_GEN_ENDMOD_01.tga")
        };

        var candidate = CreateCandidate(
            [drain, recovery],
            [],
            [
                ("P_DRAIN", "Training: Endurance Modification"),
                ("P_RECOVERY", "Training: Endurance Modification"),
                ("P_HELP_DRAIN", "Modify endurance recovery."),
                ("P_HELP_RECOVERY", "Manipulate endurance recovery.")
            ],
            discoveryBoosts: discovery);

        var enhancement = Assert.Single(candidate.Enhancements);
        Assert.Equal(2, enhancement.SourceVariants.Count);
    }

    [Fact]
    public void Create_DifferentDisplayNameSameStructure_KeepsOneLogicalIdentity()
    {
        var first = Boost("Boosts.Crafted_Accuracy.Crafted_Accuracy", "P1");
        var second = Boost("Boosts.Crafted_Accuracy_10.Crafted_Accuracy_10", "P2");
        var discovery = BuildCraftedSeriesDiscovery([first, second], "Accuracy");

        var candidate = CreateCandidate(
            [first, second],
            [],
            [("P1", "Renamed Accuracy A"), ("P2", "Renamed Accuracy B")],
            [BuildCurrentIdentity("ENH-00001", [first, second])],
            discoveryBoosts: discovery);

        var enhancement = Assert.Single(candidate.Enhancements);
        Assert.Equal("ENH-00001", enhancement.AppOwnedId);
        Assert.Equal(2, enhancement.SourceVariants.Count);
    }

    [Fact]
    public void Create_SameDisplayNameDifferentStructure_DoesNotMerge()
    {
        var first = Boost("Boosts.Science_Accuracy.Science_Accuracy", "P1", "Science");
        var second = Boost("Boosts.Magic_Accuracy.Magic_Accuracy", "P2", "Magic");
        var discovery = new Dictionary<string, HomecomingBoostDiscoveryRecord>(StringComparer.Ordinal)
        {
            [first.HomecomingSourceId] = Discovery(
                first.HomecomingSourceId,
                ["Accuracy"],
                ["Science", "Accuracy"]),
            [second.HomecomingSourceId] = Discovery(
                second.HomecomingSourceId,
                ["Accuracy"],
                ["Magic", "Accuracy"])
        };

        var candidate = CreateCandidate(
            [first, second],
            [],
            [("P1", "Training: Accuracy"), ("P2", "Training: Accuracy")],
            discoveryBoosts: discovery);

        Assert.Equal(2, candidate.Enhancements.Count);
        Assert.All(candidate.Enhancements, value => Assert.Single(value.SourceVariants));
    }

    [Fact]
    public void Create_OriginStructuralCollision_DoesNotMergeMoreThanPair()
    {
        var first = Boost("Boosts.Generic_Cone.Generic_Cone", "P1", "Generic");
        var second = Boost("Boosts.Generic_Range.Generic_Range", "P2", "Generic");
        var third = Boost("Boosts.Generic_Extra.Generic_Extra", "P3", "Generic");
        var discovery = new Dictionary<string, HomecomingBoostDiscoveryRecord>(StringComparer.Ordinal)
        {
            [first.HomecomingSourceId] = Discovery(first.HomecomingSourceId, ["Range"], TrainingRangeBoostsAllowed),
            [second.HomecomingSourceId] = Discovery(second.HomecomingSourceId, ["Range"], TrainingRangeBoostsAllowed),
            [third.HomecomingSourceId] = Discovery(third.HomecomingSourceId, ["Range"], TrainingRangeBoostsAllowed)
        };

        var candidate = CreateCandidate(
            [first, second, third],
            [],
            [("P1", "Training: Range Increase"), ("P2", "Training: Range Increase"), ("P3", "Training: Range Increase")],
            discoveryBoosts: discovery);

        Assert.Equal(3, candidate.Enhancements.Count);
        Assert.All(candidate.Enhancements, value => Assert.Single(value.SourceVariants));
    }

    [Fact]
    public void Create_MissingStructuralDiscovery_FailsInsteadOfDisplayNameFallback()
    {
        var boost = Boost("Boosts.Science_Accuracy.Science_Accuracy", "P1", "Science");

        var exception = Assert.Throws<HomecomingEnhancementCandidateException>(() =>
            CreateCandidate(
                [boost],
                [],
                [("P1", "Training: Accuracy")],
                discoveryBoosts: new Dictionary<string, HomecomingBoostDiscoveryRecord>(StringComparer.Ordinal)));

        Assert.Contains("no structural discovery record", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Create_CraftedSeriesInvariantViolation_FailsExplicitly()
    {
        var first = Boost("Boosts.Crafted_Accuracy.Crafted_Accuracy", "P1");
        var second = Boost("Boosts.Crafted_Accuracy_10.Crafted_Accuracy_10", "P2");
        var discovery = new Dictionary<string, HomecomingBoostDiscoveryRecord>(StringComparer.Ordinal)
        {
            [first.HomecomingSourceId] = Discovery(
                first.HomecomingSourceId,
                ["Accuracy"],
                [.. OriginBoostTypes, "Accuracy"]),
            [second.HomecomingSourceId] = Discovery(
                second.HomecomingSourceId,
                ["Damage"],
                [.. OriginBoostTypes, "Damage"])
        };

        var exception = Assert.Throws<HomecomingEnhancementCandidateException>(() =>
            CreateCandidate(
                [first, second],
                [],
                [("P1", "Invention: Accuracy"), ("P2", "Invention: Accuracy")],
                discoveryBoosts: discovery));

        Assert.Contains("failed structural corroboration", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Create_SetPieces_AreUnaffected()
    {
        var crafted = Boost("Boosts.Crafted_Bonesnap_A.Crafted_Bonesnap_A", "P_PIECE", "Crafted");
        var attuned = Boost("Boosts.Attuned_Bonesnap_A.Attuned_Bonesnap_A", "P_PIECE", "Attuned");
        var discovery = BuildDefaultDiscovery([crafted, attuned]);

        var candidate = CreateCandidate(
            [crafted, attuned],
            [Set("Bonesnap", "P_SET", [[crafted.HomecomingSourceId, attuned.HomecomingSourceId]])],
            [
                ("P_PIECE", "Bonesnap: Accuracy/Damage"),
                ("P_SET", "Bonesnap"),
                ("ECUncommon", "Rarity: Uncommon"),
                ("ECMelee", "Category: Melee")
            ],
            discoveryBoosts: discovery);

        var enhancement = Assert.Single(candidate.Enhancements);
        Assert.Equal("Bonesnap", enhancement.HomecomingEnhancementSetId);
        Assert.Equal(2, enhancement.SourceVariants.Count);
    }

    [Fact]
    [Trait("Category", "LiveInstall")]
    public void LiveInstall_FullNonSetIdentityCensus_MatchesProvenPopulation()
    {
        var report = HomecomingEnhancementRepairDiscoveryCommand.Discover(LiveInstallRoot);
        var powersBytes = HomecomingPiggMemberReader.ReadMember(
            Path.Combine(LiveInstallRoot, HomecomingEnhancementCandidateGenerator.PowersArchive),
            HomecomingEnhancementCandidateGenerator.PowersMember);
        var discoveryBoosts = HomecomingPowersBoostDiscoveryReader.ReadBoosts(powersBytes)
            .Where(value => value.SourceId.StartsWith("Boosts.", StringComparison.Ordinal))
            .ToDictionary(value => value.SourceId, StringComparer.Ordinal);

        var multiSourceGroups = report.NonSetIdentity.Groups;
        Assert.Equal(49, report.NonSetIdentity.MultiSourceGroupCount);
        Assert.Equal(313, report.NonSetIdentity.ConcreteSourceCount);
        Assert.Equal(27, report.NonSetIdentity.CraftedLevelSeriesCount);
        Assert.Equal(22, report.NonSetIdentity.OriginSynonymPairCount);
        Assert.Equal(0, report.NonSetIdentity.OtherCount);

        var accounting = new StringBuilder();
        accounting.AppendLine("Non-set multi-source identity census:");
        foreach (var group in multiSourceGroups)
        {
            var sourceIds = group.Sources.Select(value => value.HomecomingSourceId).ToArray();
            var identity = HomecomingNonSetEnhancementIdentitySupport.ResolveLogicalGroupIdentity(
                sourceIds,
                discoveryBoosts);
            Assert.NotEqual("DisplayNameOnly", group.EvidenceClass);
            Assert.True(group.CurrentGroupingProvenCorrect);
            Assert.False(group.ShouldSplit);
            Assert.Equal(
                identity.EvidenceClass.ToString(),
                group.Kind switch
                {
                    "CraftedLevelSeries" => nameof(NonSetEnhancementIdentityEvidenceClass.CraftedSeries),
                    "OriginSynonymPair" => nameof(NonSetEnhancementIdentityEvidenceClass.OriginStructuralPair),
                    _ => group.Kind
                },
                StringComparer.Ordinal);

            accounting.AppendLine(
                HomecomingNonSetEnhancementIdentitySupport.FormatGroupDiagnostic(
                    group.AppOwnedId,
                    identity,
                    sourceIds));
        }

        Assert.DoesNotContain("DisplayNameMatch", accounting.ToString(), StringComparison.Ordinal);
        Assert.Equal(49, multiSourceGroups.Length);
    }

    [Fact]
    [Trait("Category", "LiveInstall")]
    public void LiveInstall_Promotion_PreservesSlice2EnhancementItems()
    {
        var catalogPath = Path.Combine(Path.GetTempPath(), $"coh-slice3-live-{Guid.NewGuid():N}.json");
        try
        {
            using (var embedded = typeof(ItemReferenceCatalogFactory).Assembly.GetManifestResourceStream(
                       ItemReferenceCatalogFactory.ProductionCatalogResourceName)
                   ?? throw new InvalidOperationException("Embedded production catalog missing."))
            using (var output = File.Create(catalogPath))
            {
                embedded.CopyTo(output);
            }

            var beforeJson = JsonDocument.Parse(File.ReadAllText(catalogPath));
            var beforeItems = beforeJson.RootElement.GetProperty("items");

            _promotion.Promote(catalogPath);

            var afterJson = JsonDocument.Parse(File.ReadAllText(catalogPath));
            var afterItems = afterJson.RootElement.GetProperty("items");
            Assert.Equal(beforeItems.GetArrayLength(), afterItems.GetArrayLength());

            var identityFieldsMatch = beforeItems.EnumerateArray().Zip(afterItems.EnumerateArray())
                .All(pair => IdentityFieldsEqual(pair.First, pair.Second));
            Assert.True(identityFieldsMatch);

            var multiSourceNonSet = afterItems.EnumerateArray()
                .Count(item =>
                    string.Equals(item.GetProperty("family").GetString(), nameof(ReferenceItemFamily.Enhancement), StringComparison.Ordinal)
                    && (!item.TryGetProperty("enhancementSetId", out var setId)
                        || setId.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                    && item.TryGetProperty("sourceVariants", out var variants)
                    && variants.GetArrayLength() > 1);
            var concreteSources = afterItems.EnumerateArray()
                .Where(item =>
                    string.Equals(item.GetProperty("family").GetString(), nameof(ReferenceItemFamily.Enhancement), StringComparison.Ordinal)
                    && (!item.TryGetProperty("enhancementSetId", out var setId)
                        || setId.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                    && item.TryGetProperty("sourceVariants", out var variants)
                    && variants.GetArrayLength() > 1)
                .Sum(item => item.GetProperty("sourceVariants").GetArrayLength());
            Assert.Equal(49, multiSourceNonSet);
            Assert.Equal(313, concreteSources);
        }
        finally
        {
            if (File.Exists(catalogPath))
            {
                File.Delete(catalogPath);
            }
        }
    }

    [Fact]
    [Trait("Category", "LiveInstall")]
    public void LiveInstall_Promotion_IsDeterministic()
    {
        var firstPath = Path.Combine(Path.GetTempPath(), $"coh-slice3-det-1-{Guid.NewGuid():N}.json");
        var secondPath = Path.Combine(Path.GetTempPath(), $"coh-slice3-det-2-{Guid.NewGuid():N}.json");
        try
        {
            foreach (var path in new[] { firstPath, secondPath })
            {
                using var embedded = typeof(ItemReferenceCatalogFactory).Assembly.GetManifestResourceStream(
                                           ItemReferenceCatalogFactory.ProductionCatalogResourceName)
                                       ?? throw new InvalidOperationException("Embedded production catalog missing.");
                using var output = File.Create(path);
                embedded.CopyTo(output);
            }

            _promotion.Promote(firstPath);
            _promotion.Promote(secondPath);

            var firstHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(firstPath)));
            var secondHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(secondPath)));
            Assert.Equal(firstHash, secondHash);
        }
        finally
        {
            foreach (var path in new[] { firstPath, secondPath })
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }

    private static bool IdentityFieldsEqual(JsonElement before, JsonElement after)
    {
        string? Read(JsonElement item, string name) =>
            item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

        if (!string.Equals(
                before.GetProperty("catalogItemId").GetString(),
                after.GetProperty("catalogItemId").GetString(),
                StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.Equals(Read(before, "enhancementSetId"), Read(after, "enhancementSetId"), StringComparison.Ordinal))
        {
            return false;
        }

        if (!before.TryGetProperty("sourceVariants", out var beforeVariants)
            || !after.TryGetProperty("sourceVariants", out var afterVariants))
        {
            return before.TryGetProperty("sourceVariants", out _)
                == after.TryGetProperty("sourceVariants", out _);
        }

        var beforeIds = beforeVariants.EnumerateArray()
            .Select(variant => variant.GetProperty("homecomingSourceId").GetString())
            .Order(StringComparer.Ordinal)
            .ToArray();
        var afterIds = afterVariants.EnumerateArray()
            .Select(variant => variant.GetProperty("homecomingSourceId").GetString())
            .Order(StringComparer.Ordinal)
            .ToArray();
        return beforeIds.SequenceEqual(afterIds, StringComparer.Ordinal);
    }

    private static HomecomingEnhancementCandidateDocument CreateCandidate(
        IReadOnlyList<HomecomingConcreteBoostRecord> boosts,
        IReadOnlyList<HomecomingBoostSetRecord> sets,
        IReadOnlyList<(string Key, string Value)> messages,
        IReadOnlyList<CurrentEnhancementIdentity>? currentEnhancements = null,
        IReadOnlyDictionary<string, HomecomingBoostDiscoveryRecord>? discoveryBoosts = null) =>
        HomecomingEnhancementCandidateGenerator.Create(
            boosts,
            sets,
            HomecomingMessageStoreReader.Read(
                HomecomingBinaryFixtureBuilder.CreateMessageStore([.. messages])),
            discoveryBoosts ?? BuildDefaultDiscovery(boosts),
            currentEnhancements ?? [],
            [],
            "Issue 28, Page 3 - 28.3.7927",
            "1.20260707.124731.7927");

    private static CurrentEnhancementIdentity BuildCurrentIdentity(
        string appOwnedId,
        IReadOnlyList<HomecomingConcreteBoostRecord> boosts) =>
        new(
            appOwnedId,
            "Current",
            null,
            boosts.Select(boost => boost.HomecomingSourceId).Order(StringComparer.Ordinal).ToArray());

    private static IReadOnlyList<HomecomingConcreteBoostRecord> BuildCraftedSeriesBoosts(
        string seriesPrefix,
        string aspectType)
    {
        var levels = new[] { string.Empty, "_10", "_15", "_20", "_25", "_30", "_35", "_40", "_45", "_50" };
        var shortName = seriesPrefix.Replace("Boosts.", "", StringComparison.Ordinal);
        return levels
            .Select(level => Boost(
                $"Boosts.{shortName}{level}.{shortName}{level}",
                $"P{level.Replace("_", "", StringComparison.Ordinal)}"))
            .ToArray();
    }

    private static Dictionary<string, HomecomingBoostDiscoveryRecord> BuildCraftedSeriesDiscovery(
        IReadOnlyList<HomecomingConcreteBoostRecord> boosts,
        string aspectType)
    {
        var allowed = OriginBoostTypes.Append(aspectType).ToArray();
        return boosts.ToDictionary(
            boost => boost.HomecomingSourceId,
            boost => Discovery(
                boost.HomecomingSourceId,
                [aspectType],
                allowed,
                icon: "E_ICON_GEN_ACCURACY_01.tga"),
            StringComparer.Ordinal);
    }

    private static IReadOnlyDictionary<string, HomecomingBoostDiscoveryRecord> BuildDefaultDiscovery(
        IReadOnlyList<HomecomingConcreteBoostRecord> boosts) =>
        boosts.ToDictionary(
            boost => boost.HomecomingSourceId,
            boost => Discovery(boost.HomecomingSourceId, [], OriginBoostTypes),
            StringComparer.Ordinal);

    private static HomecomingConcreteBoostRecord Boost(
        string sourceId,
        string displayKey,
        string sourceForm = "Crafted") =>
        new(sourceId, displayKey, sourceForm);

    private static HomecomingBoostSetRecord Set(
        string sourceId,
        string displayKey,
        IReadOnlyList<IReadOnlyList<string>> memberGroups) =>
        new(sourceId, displayKey, "P_DESCRIPTION", ["ECUncommon", "ECMelee"], 10, 50, memberGroups);

    private static HomecomingBoostDiscoveryRecord Discovery(
        string sourceId,
        IReadOnlyList<string> nonOriginBoostTypes,
        IReadOnlyList<string> boostsAllowed,
        string helpKey = "P_HELP",
        string icon = "icon.tga") =>
        new(
            sourceId,
            "P_DISPLAY",
            helpKey,
            string.Empty,
            icon,
            boostsAllowed,
            nonOriginBoostTypes,
            0f,
            0f,
            0f,
            0f);
}
