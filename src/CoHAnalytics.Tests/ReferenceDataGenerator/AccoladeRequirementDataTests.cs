using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CoHAnalytics.ReferenceData;
using CoHAnalytics.ReferenceDataGenerator;

namespace CoHAnalytics.Tests.ReferenceDataGenerator;

[Trait("Category", PrivateResearchTestEnvironment.Category)]
public sealed class AccoladeRequirementDataTests
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

    [Fact]
    public void RefreshAccoladeRequirementData_corrects_exploration_accolade_and_logic_patterns()
    {
        var document = LoadProductionCatalogDocument();
        var research = BadgeResearchPackageLoader.Load(GetResearchRoot());

        HomecomingBadgePromotionSupport.RefreshAccoladeRequirementData(document, research);

        var accolades = document.Badges
            .Where(badge => string.Equals(badge.ReferenceKind, nameof(ReferenceBadgeKind.Accolade), StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(145, accolades.Length);

        var atlasTourGuide = accolades.Single(badge => badge.CatalogItemId == "BAD-01917");
        Assert.Equal(nameof(ReferenceRequirementLogicPattern.And), atlasTourGuide.RequirementLogicPattern);
        Assert.Equal(8, document.BadgeAccoladeRequirements.Count(requirement =>
            requirement.AccoladeBadgeId == "BAD-01917"));

        var explorerAccolades = accolades
            .Where(badge => badge.HomecomingSourceId?.EndsWith("Explorer", StringComparison.Ordinal) == true)
            .ToArray();
        Assert.NotEmpty(explorerAccolades);
        Assert.All(explorerAccolades, badge =>
            Assert.Equal(nameof(ReferenceRequirementLogicPattern.And), badge.RequirementLogicPattern));
    }

    [Fact]
    public void RefreshAccoladeRequirementData_deduplicates_alchemist_prerequisites()
    {
        var document = LoadProductionCatalogDocument();
        var research = BadgeResearchPackageLoader.Load(GetResearchRoot());

        HomecomingBadgePromotionSupport.RefreshAccoladeRequirementData(document, research);

        var alchemistRequirements = document.BadgeAccoladeRequirements
            .Where(requirement => requirement.AccoladeBadgeId == "BAD-02094")
            .ToArray();

        Assert.Equal(2, alchemistRequirements.Length);
        Assert.Single(alchemistRequirements, requirement => requirement.PrerequisiteBadgeId == "BAD-02104");
        Assert.Single(alchemistRequirements, requirement => requirement.PrerequisiteBadgeId == "BAD-02126");
    }

    [Fact]
    public void RefreshAccoladeRequirementData_has_no_duplicate_accolade_prerequisite_edges()
    {
        var document = LoadProductionCatalogDocument();
        var research = BadgeResearchPackageLoader.Load(GetResearchRoot());

        HomecomingBadgePromotionSupport.RefreshAccoladeRequirementData(document, research);

        var duplicateGroups = document.BadgeAccoladeRequirements
            .GroupBy(requirement => $"{requirement.AccoladeBadgeId}|{requirement.PrerequisiteBadgeId}", StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .ToArray();

        Assert.Empty(duplicateGroups);
        Assert.Equal(740, document.BadgeAccoladeRequirements.Count);
    }

    [Fact]
    public void RefreshAccoladeRequirementData_preserves_text_only_requirements_without_invented_logic()
    {
        var document = LoadProductionCatalogDocument();
        var research = BadgeResearchPackageLoader.Load(GetResearchRoot());

        HomecomingBadgePromotionSupport.RefreshAccoladeRequirementData(document, research);

        var taskForceCommander = document.Badges.Single(badge => badge.CatalogItemId == "BAD-03304");
        Assert.Equal(nameof(ReferenceRequirementLogicPattern.TextOnly), taskForceCommander.RequirementLogicPattern);
        Assert.Contains("either of two methods", taskForceCommander.RequirementText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            document.BadgeAccoladeRequirements,
            requirement => requirement.AccoladeBadgeId == "BAD-03304");
    }

    [Fact]
    public void RefreshAccoladeRequirementData_produces_valid_catalog()
    {
        var document = LoadProductionCatalogDocument();
        var research = BadgeResearchPackageLoader.Load(GetResearchRoot());

        HomecomingBadgePromotionSupport.RefreshAccoladeRequirementData(document, research);

        var serialized = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(document, CatalogJsonOptions) + "\n");
        using var stream = new MemoryStream(serialized);
        var load = ItemReferenceCatalogLoader.Load(stream);

        Assert.True(load.Succeeded, load.FailureReason);
    }

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

    private static string GetResearchRoot() => PrivateResearchTestEnvironment.RequireResearchRoot();
}
