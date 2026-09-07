using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CoHAnalytics.ReferenceData;
using CoHAnalytics.ReferenceDataGenerator;
using CoHAnalytics.ViewModels.Workspaces;

namespace CoHAnalytics.Tests.ReferenceData;

public sealed class AccoladeDisplayTextTests
{
    private static readonly JsonSerializerOptions CatalogJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly IItemReferenceCatalog ProductionCatalog =
        ItemReferenceCatalogFactory.LoadEmbeddedProduction();

    [Fact]
    public void Production_day_job_accolade_rewards_have_no_unresolved_formula_placeholders()
    {
        var dayJobAccoladeIds = LoadAccoladeCategorySnapshot()
            .BadgeCategoryById
            .Where(pair => pair.Value == "day-jobs")
            .Select(pair => pair.Key)
            .ToHashSet(StringComparer.Ordinal);

        var dayJobAccolades = ProductionCatalog.GetBadges()
            .Where(badge => dayJobAccoladeIds.Contains(badge.CatalogItemId))
            .ToArray();

        Assert.Equal(19, dayJobAccolades.Length);
        Assert.All(dayJobAccolades, badge =>
            Assert.False(
                BadgeRewardTextSupport.ContainsUnresolvedDayJobChargePlaceholder(badge.RewardText),
                badge.CatalogItemId));
    }

    [Fact]
    public void Web_weaver_reward_uses_safe_non_numeric_charge_wording()
    {
        var normalized = BadgeRewardTextSupport.NormalizeAccoladeRewardPower(
            "Ranged, Target Immobilize, -Recharge, -Fly; For every X hours logged out, a character gains 1 charge, up to a maximum of Z charges.",
            heroDescription: null,
            villainDescription:
                "While logged out in an Arachnos controlled area or in the Arachnos building in Marconeville, you will earn additional charges for your Web Grenade power.");

        Assert.Equal(
            "Web Grenade — Ranged, Target Immobilize, -Recharge, -Fly. Additional charges are earned while logged out in the appropriate Day Job location.",
            normalized);
    }

    [Fact]
    public void Security_chief_reward_preserves_known_maximum_charge_count()
    {
        Assert.True(ProductionCatalog.TryGetBadgeById("BAD-02135", out var badge));

        var normalized = BadgeRewardTextSupport.NormalizeAccoladeRewardPower(
            "Ranged (Targeted AoE), Foe Hold; For every X hours logged out, a character gains 1 charge, up to a maximum of 15 charges.",
            badge.HeroDescription,
            badge.VillainDescription);

        Assert.Contains("up to a maximum of 15 charges", normalized, StringComparison.Ordinal);
        Assert.DoesNotContain(" every X ", normalized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Production_requirement_text_has_no_internal_markdown_references()
    {
        var offending = ProductionCatalog.GetBadges()
            .Where(badge => BadgeRewardTextSupport.ContainsInternalResearchDocumentReference(badge.RequirementText))
            .Select(badge => badge.CatalogItemId)
            .ToArray();

        Assert.Empty(offending);
    }

    [Fact]
    public void Partial_accolade_requirement_intro_still_surfaces_uncertainty_notice()
    {
        var tree = ReferenceBadgeBrowseSupport.BuildBrowseTree(ProductionCatalog);
        var webWeaverNode = tree.NodesByKey.Values.Single(node =>
            node.Kind == ReferenceEnhancementBrowseNodeKind.BadgeAccolade
            && node.BadgeId == "BAD-02145");
        var detail = ReferenceBadgeBrowseSupport.BuildDetail(
            ProductionCatalog,
            webWeaverNode,
            acquiredBadgeIds: null,
            installedGameAssetProvider: null);

        Assert.Equal("Requirement logic is not fully verified in promoted data.", detail.RequirementLogicNote);
        Assert.Equal("Earn a source-defined set of 2 linked prerequisite badges", detail.RequirementIntroText);
        Assert.DoesNotContain(".md", detail.RequirementIntroText ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RefreshAccoladeDisplayText_normalizes_catalog()
    {
        var document = LoadProductionCatalogDocument();
        HomecomingBadgePromotionSupport.RefreshAccoladeDisplayText(document);

        Assert.All(document.Badges, badge =>
            Assert.False(BadgeRewardTextSupport.ContainsInternalResearchDocumentReference(badge.RequirementText)));
        Assert.All(
            document.Badges.Where(badge => badge.RewardText?.Contains("logged out", StringComparison.OrdinalIgnoreCase) == true),
            badge => Assert.False(BadgeRewardTextSupport.ContainsUnresolvedDayJobChargePlaceholder(badge.RewardText)));

        var serialized = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(document, CatalogJsonOptions) + "\n");
        using var stream = new MemoryStream(serialized);
        var load = ItemReferenceCatalogLoader.Load(stream);

        Assert.True(load.Succeeded, load.FailureReason);
    }

    private static AccoladeCategoryReferenceSnapshot LoadAccoladeCategorySnapshot() =>
        AccoladeCategoryReferenceSupport.LoadEmbeddedProduction();

    private static ItemReferenceCatalogDocument LoadProductionCatalogDocument()
    {
        var json = File.ReadAllText(GetProductionCatalogPath());
        return JsonSerializer.Deserialize<ItemReferenceCatalogDocument>(json, CatalogJsonOptions)
            ?? throw new InvalidOperationException("Failed to deserialize production catalog.");
    }

    private static string GetProductionCatalogPath() =>
        Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "CoHAnalytics", "ReferenceData", "item-catalog.v1.json"));
}
