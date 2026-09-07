using System.Text;
using CoHAnalytics.ReferenceDataGenerator;

namespace CoHAnalytics.Tests.ReferenceDataGenerator;

public sealed class HomecomingBaseRecipesReaderTests
{
    [Fact]
    public void Read_ValidRecord_RecoversExactRequiredFields()
    {
        var data = HomecomingBinaryFixtureBuilder.CreateBaseRecipes(
            new SyntheticBaseRecipeRecord(
                "Recipe_Invention_Fixture_10",
                "P_RECIPE",
                "Boosts.Crafted_Fixture_A.Crafted_Fixture_A",
                rarity: 3,
                level: 10,
                requirements:
                [
                    new("S_Rare", 2),
                    new("S_Common", 1)
                ],
                craftingCosts: ["3400"]));

        var record = Assert.Single(HomecomingBaseRecipesReader.Read(data));

        Assert.Equal("Recipe_Invention_Fixture_10", record.HomecomingSourceId);
        Assert.Equal("P_RECIPE", record.DisplayNameMessageKey);
        Assert.Equal("recipe_fixture.tga", record.Icon);
        Assert.Equal(["Worktable_Invention"], record.WorktableIds);
        Assert.Equal("Boosts.Crafted_Fixture_A.Crafted_Fixture_A", record.ProductSourceId);
        Assert.Equal(3u, record.Rarity);
        Assert.Equal(10u, record.Level);
        Assert.Equal(["3400"], record.CraftingCosts);
        Assert.Equal(
            [new HomecomingBaseRecipeRequirement("S_Rare", 2), new("S_Common", 1)],
            record.Requirements);
    }

    [Fact]
    public void Read_DuplicateSourceId_FailsClearly()
    {
        var data = HomecomingBinaryFixtureBuilder.CreateBaseRecipes(
            Recipe("Recipe_Duplicate", "P_ONE"),
            Recipe("Recipe_Duplicate", "P_TWO"));

        var exception = Assert.Throws<HomecomingBaseRecipesException>(() =>
            HomecomingBaseRecipesReader.Read(data));

        Assert.Contains("duplicated", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Read_TruncatedRecord_FailsClearly()
    {
        var data = HomecomingBinaryFixtureBuilder.CreateBaseRecipes(
            Recipe("Recipe_Truncated", "P_RECIPE"));

        var exception = Assert.Throws<HomecomingBaseRecipesException>(() =>
            HomecomingBaseRecipesReader.Read(data[..^1]));

        Assert.Contains("beyond", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static SyntheticBaseRecipeRecord Recipe(string sourceId, string messageKey) =>
        new(sourceId, messageKey, "Boosts.Crafted_Fixture_A.Crafted_Fixture_A");
}

public sealed class HomecomingRecipeCandidateGeneratorTests
{
    [Fact]
    public void Create_ExactAuthoritativeJoins_ProducesCompleteCandidate()
    {
        var result = Create(
            [Recipe("Recipe_B", "P_RECIPE", Product, requirements: [new("S_B", 2), new("S_A", 1)])],
            [Enhancement("ENH-00007", "Fixture Enhancement", Product)],
            [Salvage("SAL-00002", "S_B"), Salvage("SAL-00001", "S_A")]);

        var record = Assert.Single(result.Records);
        Assert.Equal("REC-00001", record.AppOwnedId);
        Assert.Equal("Fixture Recipe", record.DisplayName);
        Assert.Equal("recipe_fixture.tga", record.Icon);
        Assert.Equal("ENH-00007", record.ProducedEnhancementAppOwnedId);
        Assert.False(record.UsedCaseInsensitiveProductJoin);
        Assert.Equal("Rare", record.Rarity);
        Assert.Equal(3400u, record.CraftingCost);
        Assert.Equal(
            [
                new HomecomingRecipeRequirementCandidate("SAL-00001", "S_A", 1),
                new HomecomingRecipeRequirementCandidate("SAL-00002", "S_B", 2)
            ],
            record.Requirements);
        Assert.Empty(result.AssignedEnhancementIds);
        Assert.Equal(1, result.Summary.CompletedProductJoins);
        Assert.Equal(1, result.Summary.CompleteSalvageMatrices);
        Assert.Equal(0, result.Summary.InvalidOrUnresolved);
    }

    [Fact]
    public void Create_ProductCaseDifference_UsesApprovedHomecomingIdJoinAndPreservesSpellings()
    {
        const string recipeSpelling = "Boosts.Crafted_Exploit_Weakness_C.Crafted_Exploit_Weakness_C";
        const string enhancementSpelling = "Boosts.Crafted_Exploit_Weakness_c.Crafted_Exploit_Weakness_c";

        var result = Create(
            [Recipe("Recipe_Case", "P_RECIPE", recipeSpelling)],
            [Enhancement("ENH-00007", "Exploit Weakness", enhancementSpelling)],
            [Salvage("SAL-00001", "S_Fixture")]);

        var record = Assert.Single(result.Records);
        Assert.True(record.UsedCaseInsensitiveProductJoin);
        Assert.Equal(recipeSpelling, record.HomecomingProducedBoostSourceId);
        Assert.Equal(enhancementSpelling, record.MatchedHomecomingBoostSourceId);
        Assert.Equal(1, result.Summary.CaseInsensitiveProductJoinsUsed);
    }

    [Fact]
    public void Create_CaseInsensitiveProductCollision_FailsExplicitly()
    {
        var exception = Assert.Throws<HomecomingRecipeCandidateException>(() => Create(
            [Recipe("Recipe_Collision", "P_RECIPE", "boosts.fixture")],
            [
                Enhancement("ENH-00001", "One", "Boosts.Fixture"),
                Enhancement("ENH-00002", "Two", "boosts.fixture")
            ],
            [Salvage("SAL-00001", "S_Fixture")]));

        Assert.Contains("collision", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Boosts.Fixture", exception.Message, StringComparison.Ordinal);
        Assert.Contains("boosts.fixture", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_UnknownProduct_DoesNotUseFuzzyMatching()
    {
        var exception = Assert.Throws<HomecomingRecipeCandidateException>(() => Create(
            [Recipe("Recipe_Unknown", "P_RECIPE", "Boosts.Fixture-Typo")],
            [Enhancement("ENH-00001", "Fixture", "Boosts.Fixture")],
            [Salvage("SAL-00001", "S_Fixture")]));

        Assert.Contains("does not resolve", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Create_UnresolvedDisplayMessage_FailsWithoutFallback()
    {
        var messages = HomecomingMessageStoreReader.Read(
            HomecomingBinaryFixtureBuilder.CreateMessageStore(("P_OTHER", "Other")));

        var exception = Assert.Throws<HomecomingRecipeCandidateException>(() =>
            HomecomingRecipeCandidateGenerator.Create(
                [Recipe("Recipe_Message", "P_MISSING", Product)],
                messages,
                SalvageDocument([Salvage("SAL-00001", "S_Fixture")]),
                EnhancementDocument([Enhancement("ENH-00001", "Fixture", Product)]),
                [],
                "Issue 28",
                "1.2.3"));

        Assert.Contains("P_MISSING", exception.Message, StringComparison.Ordinal);
        Assert.Contains("unresolved", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Create_MissingEnhancementIds_AssignsAfterMaximumInLogicalOrdinalOrder()
    {
        var result = Create(
            [
                Recipe("Recipe_Z", "P_RECIPE", "Boosts.Z"),
                Recipe("Recipe_A", "P_RECIPE", "Boosts.A")
            ],
            [
                Enhancement("ENH-00010", "Existing", "Boosts.Existing"),
                Enhancement(null, "Zed", "Boosts.Z"),
                Enhancement(null, "Alpha", "Boosts.A")
            ],
            [Salvage("SAL-00001", "S_Fixture")]);

        Assert.Equal(["ENH-00011", "ENH-00012"],
            result.AssignedEnhancementIds.Select(value => value.AppOwnedId));
        Assert.Equal(["Alpha", "Zed"],
            result.AssignedEnhancementIds.Select(value => value.DisplayName));
        Assert.Equal(["Boosts.A", "Boosts.Z"],
            result.AssignedEnhancementIds.Select(value => value.LogicalIdentityKey));
        Assert.Equal(["Boosts.A"], result.AssignedEnhancementIds[0].HomecomingSourceIds);
        Assert.Equal(["Boosts.Z"], result.AssignedEnhancementIds[1].HomecomingSourceIds);
        Assert.Equal(
            [
                ("Recipe_A", "REC-00001", "ENH-00011"),
                ("Recipe_Z", "REC-00002", "ENH-00012")
            ],
            result.Records.Select(record => (
                record.HomecomingSourceId,
                record.AppOwnedId,
                record.ProducedEnhancementAppOwnedId)));
        Assert.Equal(2, result.Summary.RecipesUsingAssignedEnhancementIds);
        Assert.Equal(2, result.Summary.NewlyAssignedEnhancementIds);
    }

    [Fact]
    public void Create_RepeatedRun_IsByteDeterministic()
    {
        var recipes = new[] { Recipe("Recipe_Deterministic", "P_RECIPE", Product) };
        var enhancements = new[] { Enhancement(null, "Fixture", Product) };
        var salvage = new[] { Salvage("SAL-00001", "S_Fixture") };

        var first = Create(recipes, enhancements, salvage);
        var second = Create(recipes.Reverse().ToArray(), enhancements, salvage);

        Assert.Equal(
            HomecomingRecipeCandidateWriter.Serialize(first),
            HomecomingRecipeCandidateWriter.Serialize(second));
        Assert.DoesNotContain(
            "generatedAt",
            Encoding.UTF8.GetString(HomecomingRecipeCandidateWriter.Serialize(first)),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Create_UnknownSalvage_FailsClearly()
    {
        var exception = Assert.Throws<HomecomingRecipeCandidateException>(() => Create(
            [Recipe("Recipe_Salvage", "P_RECIPE", Product)],
            [Enhancement("ENH-00001", "Fixture", Product)],
            []));

        Assert.Contains("S_Fixture", exception.Message, StringComparison.Ordinal);
        Assert.Contains("unresolved", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Create_InvalidQuantityRarityAndCost_FailClearly()
    {
        var enhancements = new[] { Enhancement("ENH-00001", "Fixture", Product) };
        var salvage = new[] { Salvage("SAL-00001", "S_Fixture") };

        Assert.Contains(
            "non-positive",
            Assert.Throws<HomecomingRecipeCandidateException>(() => Create(
                [Recipe("Recipe_Quantity", "P_RECIPE", Product, requirements: [new("S_Fixture", 0)])],
                enhancements,
                salvage)).Message,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "rarity",
            Assert.Throws<HomecomingRecipeCandidateException>(() => Create(
                [Recipe("Recipe_Rarity", "P_RECIPE", Product, rarity: 9)],
                enhancements,
                salvage)).Message,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "crafting cost",
            Assert.Throws<HomecomingRecipeCandidateException>(() => Create(
                [Recipe("Recipe_Cost", "P_RECIPE", Product, craftingCosts: ["free"])],
                enhancements,
                salvage)).Message,
            StringComparison.OrdinalIgnoreCase);
    }

    private const string Product = "Boosts.Crafted_Fixture_A.Crafted_Fixture_A";

    private static HomecomingRecipeCandidateDocument Create(
        IReadOnlyList<HomecomingBaseRecipeRecord> recipes,
        IReadOnlyList<HomecomingEnhancementCandidateRecord> enhancements,
        IReadOnlyList<HomecomingSalvageCandidateRecord> salvage)
    {
        var messages = HomecomingMessageStoreReader.Read(
            HomecomingBinaryFixtureBuilder.CreateMessageStore(("P_RECIPE", "Fixture Recipe")));
        return HomecomingRecipeCandidateGenerator.Create(
            recipes,
            messages,
            SalvageDocument(salvage),
            EnhancementDocument(enhancements),
            [],
            "Issue 28",
            "1.2.3");
    }

    private static HomecomingBaseRecipeRecord Recipe(
        string sourceId,
        string messageKey,
        string product,
        uint rarity = 3,
        IReadOnlyList<HomecomingBaseRecipeRequirement>? requirements = null,
        IReadOnlyList<string>? craftingCosts = null) =>
        new(
            sourceId,
            messageKey,
            "recipe_fixture.tga",
            ["Worktable_Invention"],
            requirements ?? [new("S_Fixture", 1)],
            product,
            rarity,
            25,
            craftingCosts ?? ["3400"]);

    private static HomecomingEnhancementCandidateRecord Enhancement(
        string? appOwnedId,
        string displayName,
        string sourceId) =>
        new(
            appOwnedId,
            displayName,
            null,
            null,
            appOwnedId is null ? "Ambiguous" : "MatchedExisting",
            [],
            [new(sourceId, "P_ENHANCEMENT", "Crafted")]);

    private static HomecomingSalvageCandidateRecord Salvage(string appOwnedId, string sourceId) =>
        new(
            appOwnedId,
            sourceId,
            "P_SALVAGE",
            sourceId,
            "Common",
            "Invention",
            "salvage.tga",
            "MatchedExisting",
            [appOwnedId]);

    private static HomecomingSalvageCandidateDocument SalvageDocument(
        IReadOnlyList<HomecomingSalvageCandidateRecord> records) =>
        new(
            "salvage-test",
            new("build", "package", "archive", "member"),
            new(records.Count, records.Count, records.Count, 0, 0, 0, [], []),
            records,
            []);

    private static HomecomingEnhancementCandidateDocument EnhancementDocument(
        IReadOnlyList<HomecomingEnhancementCandidateRecord> records) =>
        new(
            "enhancement-test",
            new("build", "package", "archive", "member", "archive", "member"),
            new(0, 0, 0, 0, 0, 0, [], []),
            new(0, 0, records.Count, 0, 0, 0, 0, 0),
            [],
            records,
            [],
            []);
}
