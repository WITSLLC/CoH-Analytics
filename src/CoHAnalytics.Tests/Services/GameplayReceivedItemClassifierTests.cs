using CoHAnalytics.Models;
using CoHAnalytics.ReferenceData;
using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Services;

public sealed class GameplayReceivedItemClassifierTests
{
  private static InMemoryReceivedItemTaxonomyCatalog CreateCatalog()
  {
    var catalog = new InMemoryReceivedItemTaxonomyCatalog();
    catalog.AddSalvage(
        "Luck Charm",
        new ReceivedItemClassificationMetadata
        {
          SalvageRarity = "Common",
          SalvageLevelMin = 1,
          SalvageLevelMax = 52
        });
    catalog.AddEnhancement(
        "Invention: Accuracy",
        new ReceivedItemClassificationMetadata
        {
          EnhancementTypeLabel = "Invention",
          EnhancementLevelMin = 15,
          EnhancementLevelMax = 52,
          EnhancementRarity = "Regular"
        });
    return catalog;
  }

  [Fact]
  public void Recipe_suffix_classifies_as_recipe()
  {
    var classifier = new GameplayReceivedItemClassifier(CreateCatalog());
    Assert.True(classifier.TryClassify(
        "Armageddon: Damage (Recipe)",
        out var category,
        out var metadata));
    Assert.Equal(GameplaySessionRewardCategory.Recipe, category);
    Assert.Null(metadata);
  }

  [Fact]
  public void Known_salvage_lookup_classifies_as_salvage()
  {
    var classifier = new GameplayReceivedItemClassifier(CreateCatalog());
    Assert.True(classifier.TryClassify("Luck Charm", out var category, out var metadata));
    Assert.Equal(GameplaySessionRewardCategory.Salvage, category);
    Assert.Equal("Common", metadata!.SalvageRarity);
  }

  [Fact]
  public void Known_enhancement_lookup_classifies_as_enhancement()
  {
    var classifier = new GameplayReceivedItemClassifier(CreateCatalog());
    Assert.True(classifier.TryClassify("Invention: Accuracy", out var category, out var metadata));
    Assert.Equal(GameplaySessionRewardCategory.Enhancement, category);
    Assert.Equal("Invention", metadata!.EnhancementTypeLabel);
  }

  [Fact]
  public void Unknown_item_remains_unclassified()
  {
    var classifier = new GameplayReceivedItemClassifier(CreateCatalog());
    Assert.False(classifier.TryClassify("Mystery Thing", out _, out _));
  }

  [Fact]
  public void Lookup_is_case_insensitive()
  {
    var classifier = new GameplayReceivedItemClassifier(CreateCatalog());
    Assert.True(classifier.TryClassify("luck charm", out var category, out _));
    Assert.Equal(GameplaySessionRewardCategory.Salvage, category);
  }

    [Fact]
    public void Verified_ordinary_enhancement_receipt_classifies_through_production_catalog()
    {
        var taxonomy = new ItemReferenceReceivedItemTaxonomyCatalog(
            ItemReferenceCatalogFactory.LoadProductionDatabase());
        var classifier = new GameplayReceivedItemClassifier(taxonomy);

        Assert.True(classifier.TryClassify(
            "Power of Grey (Endurance Reduction)",
            out var category,
            out var metadata));
        Assert.Equal(GameplaySessionRewardCategory.Enhancement, category);
        Assert.Equal("OriginOrTraining", metadata!.EnhancementTypeLabel);
    }

    [Fact]
    public void Essence_of_the_Earth_classifies_as_special_inspiration_through_production_catalog()
    {
        var taxonomy = new ItemReferenceReceivedItemTaxonomyCatalog(
            ItemReferenceCatalogFactory.LoadProductionDatabase());
        var classifier = new GameplayReceivedItemClassifier(taxonomy);

        Assert.True(classifier.TryClassify("Essence of the Earth", out var category, out var metadata));
        Assert.Equal(GameplaySessionRewardCategory.Inspiration, category);
        Assert.Equal("Special", metadata!.InspirationForm);
    }

    [Fact]
    public void Set_piece_parentheses_lookup_matches_colon_catalog_name()
  {
    var catalog = new InMemoryReceivedItemTaxonomyCatalog();
    catalog.AddEnhancement(
        "Bands of Hermes: Immobilize",
        new ReceivedItemClassificationMetadata
        {
          EnhancementTypeLabel = "SetIO",
          EnhancementSetName = "Bands of Hermes",
          EnhancementRarity = "Regular"
        });
    var classifier = new GameplayReceivedItemClassifier(catalog);

    Assert.True(classifier.TryClassify(
        "Bands of Hermes (Immobilize)",
        out var category,
        out _));
    Assert.Equal(GameplaySessionRewardCategory.Enhancement, category);
  }
}
