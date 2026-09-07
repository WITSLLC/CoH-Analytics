using CoHAnalytics.ReferenceData;

namespace CoHAnalytics.Tests.ReferenceData;

public sealed class ItemReferenceCatalogTests
{
    [Fact]
    public void Embedded_bootstrap_catalog_loads_successfully()
    {
        var catalog = ItemReferenceCatalogFactory.LoadEmbeddedBootstrap();

        Assert.True(catalog.IsLoaded);
        Assert.Null(catalog.LoadFailureReason);
        Assert.NotNull(catalog.Manifest);
    }

    [Fact]
    public void Manifest_version_is_parsed()
    {
        var catalog = ItemReferenceCatalogFactory.LoadEmbeddedBootstrap();

        Assert.Equal("item-ref-1.0.0", catalog.Manifest!.CatalogVersion);
        Assert.Equal("28.3.7927", catalog.Manifest.HomecomingCompatibility.BuildMin);
        Assert.Equal("28.3.7927", catalog.Manifest.HomecomingCompatibility.BuildMax);
        Assert.Equal("reference-1-bootstrap", catalog.Manifest.SourceRevision);
    }

    [Fact]
    public void Preferred_display_name_resolves()
    {
        var catalog = ItemReferenceCatalogFactory.LoadEmbeddedBootstrap();

        Assert.True(catalog.TryResolve("Fixture Salvage Sample", out var resolution));
        Assert.Equal("SAL-00001", resolution.Item.CatalogItemId);
        Assert.Equal(ReferenceItemFamily.Salvage, resolution.Item.Family);
        Assert.Equal("Fixture Salvage Sample", resolution.Item.CurrentDisplayName);
        Assert.Equal("item-ref-1.0.0", resolution.CatalogVersion);
    }

    [Fact]
    public void Alias_resolution_resolves_to_same_item()
    {
        var catalog = ItemReferenceCatalogFactory.LoadEmbeddedBootstrap();

        Assert.True(catalog.TryResolve("Fixture Salvage Receipt Alias", out var resolution));
        Assert.Equal("SAL-00001", resolution.Item.CatalogItemId);
        Assert.Equal("Fixture Salvage Receipt Alias", resolution.MatchedAliasText);
    }

    [Fact]
    public void Lookup_is_case_insensitive()
    {
        var catalog = ItemReferenceCatalogFactory.LoadEmbeddedBootstrap();

        Assert.True(catalog.TryResolve("fixture salvage sample", out var resolution));
        Assert.Equal("SAL-00001", resolution.Item.CatalogItemId);
    }

    [Fact]
    public void Leading_and_trailing_whitespace_is_ignored()
    {
        var catalog = ItemReferenceCatalogFactory.LoadEmbeddedBootstrap();

        Assert.True(catalog.TryResolve("  Fixture Salvage Sample  ", out var resolution));
        Assert.Equal("SAL-00001", resolution.Item.CatalogItemId);
    }

    [Fact]
    public void Punctuation_is_preserved_for_lookup()
    {
        var catalog = LoadCatalog("""
            {
              "manifest": {
                "catalogVersion": "item-ref-test",
                "homecomingCompatibility": { "buildMin": "1", "buildMax": "1" }
              },
              "items": [
                {
                  "catalogItemId": "ENH-00002",
                  "family": "Enhancement",
                  "subtype": "CommonIO",
                  "currentDisplayName": "Armageddon: Damage Enhancement",
                  "activeStatus": "Active",
                  "serverAvailability": [
                    { "serverKey": "Homecoming", "status": "Current" }
                  ],
                  "verificationStatus": "VerifiedMultiSource",
                  "variant": "Regular"
                },
                {
                  "catalogItemId": "REC-00002",
                  "family": "Recipe",
                  "subtype": "CommonIO",
                  "currentDisplayName": "Armageddon: Damage",
                  "activeStatus": "Active",
                  "verificationStatus": "VerifiedMultiSource",
                  "rarity": "Rare",
                  "producedItemId": "ENH-00002"
                }
              ],
              "aliases": [
                {
                  "catalogItemId": "REC-00002",
                  "locale": "en",
                  "text": "Armageddon: Damage",
                  "nameKind": "LogReceipt",
                  "isPreferred": true
                }
              ],
              "enhancementSets": []
            }
            """);

        Assert.True(catalog.TryResolve("Armageddon: Damage", out var exact));
        Assert.Equal("REC-00002", exact.Item.CatalogItemId);

        Assert.False(catalog.TryResolve("Armageddon Damage", out _));
        Assert.False(catalog.TryResolve("Armageddon:Damage", out _));
    }

    [Fact]
    public void Unknown_item_returns_no_match()
    {
        var catalog = ItemReferenceCatalogFactory.LoadEmbeddedBootstrap();

        Assert.False(catalog.TryResolve("Mystery Thing", out _));
        Assert.False(catalog.TryResolve(string.Empty, out _));
        Assert.False(catalog.TryResolve("   ", out _));
    }

    [Fact]
    public void Search_is_deterministic_and_does_not_fuzzy_match()
    {
        var catalog = ItemReferenceCatalogFactory.LoadEmbeddedBootstrap();

        Assert.Equal("SAL-00001", Assert.Single(catalog.Search("SAL-00001")).CatalogItemId);
        Assert.Equal("SAL-00001", Assert.Single(catalog.Search("Fixture Salvage")).CatalogItemId);
        Assert.Equal("SAL-00001", Assert.Single(catalog.Search("Receipt Alias")).CatalogItemId);
        Assert.Empty(catalog.Search("Fxiture Slavage"));
    }

    [Fact]
    public void Recipe_without_produced_enhancement_identity_is_rejected()
    {
        var catalog = LoadCatalog("""
            {
              "manifest": {
                "catalogVersion": "item-ref-test",
                "homecomingCompatibility": { "buildMin": "1", "buildMax": "1" }
              },
              "items": [
                {
                  "catalogItemId": "REC-00003",
                  "family": "Recipe",
                  "subtype": "SetIO",
                  "currentDisplayName": "Incomplete Recipe",
                  "activeStatus": "Active",
                  "verificationStatus": "VerifiedDirect",
                  "rarity": "Rare"
                }
              ],
              "aliases": [],
              "enhancementSets": []
            }
            """);

        Assert.False(catalog.IsLoaded);
        Assert.Contains("ProducedItemId", catalog.LoadFailureReason, StringComparison.Ordinal);
    }

    [Fact]
    public void Duplicate_alias_assigned_to_two_ids_is_rejected()
    {
        var catalog = LoadCatalog("""
            {
              "manifest": {
                "catalogVersion": "item-ref-test",
                "homecomingCompatibility": { "buildMin": "1", "buildMax": "1" }
              },
              "items": [
                {
                  "catalogItemId": "SAL-00002",
                  "family": "Salvage",
                  "subtype": "Invention",
                  "currentDisplayName": "First",
                  "activeStatus": "Active",
                  "verificationStatus": "VerifiedMultiSource"
                },
                {
                  "catalogItemId": "SAL-00003",
                  "family": "Salvage",
                  "subtype": "Invention",
                  "currentDisplayName": "Second",
                  "activeStatus": "Active",
                  "verificationStatus": "VerifiedMultiSource"
                }
              ],
              "aliases": [
                {
                  "catalogItemId": "SAL-00002",
                  "locale": "en",
                  "text": "Shared Alias",
                  "nameKind": "Display",
                  "isPreferred": true
                },
                {
                  "catalogItemId": "SAL-00003",
                  "locale": "en",
                  "text": "Shared Alias",
                  "nameKind": "Display",
                  "isPreferred": true
                }
              ],
              "enhancementSets": []
            }
            """);

        Assert.False(catalog.IsLoaded);
        Assert.Contains("Shared Alias", catalog.LoadFailureReason!, StringComparison.Ordinal);
    }

    [Fact]
    public void Invalid_catalog_item_id_is_rejected()
    {
        var catalog = LoadCatalog("""
            {
              "manifest": {
                "catalogVersion": "item-ref-test",
                "homecomingCompatibility": { "buildMin": "1", "buildMax": "1" }
              },
              "items": [
                {
                  "catalogItemId": "BAD-00001",
                  "family": "Salvage",
                  "subtype": "Invention",
                  "currentDisplayName": "Bad Id",
                  "activeStatus": "Active",
                  "verificationStatus": "VerifiedMultiSource"
                }
              ],
              "aliases": [],
              "enhancementSets": []
            }
            """);

        Assert.False(catalog.IsLoaded);
        Assert.Contains("SAL-", catalog.LoadFailureReason!, StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_catalog_item_id_is_rejected()
    {
        var catalog = LoadCatalog("""
            {
              "manifest": {
                "catalogVersion": "item-ref-test",
                "homecomingCompatibility": { "buildMin": "1", "buildMax": "1" }
              },
              "items": [
                {
                  "family": "Salvage",
                  "subtype": "Invention",
                  "currentDisplayName": "Missing Id",
                  "activeStatus": "Active",
                  "verificationStatus": "VerifiedMultiSource"
                }
              ],
              "aliases": [],
              "enhancementSets": []
            }
            """);

        Assert.False(catalog.IsLoaded);
        Assert.Contains("CatalogItemId", catalog.LoadFailureReason!, StringComparison.Ordinal);
    }

    [Fact]
    public void Family_prefix_mismatch_is_rejected()
    {
        var catalog = LoadCatalog("""
            {
              "manifest": {
                "catalogVersion": "item-ref-test",
                "homecomingCompatibility": { "buildMin": "1", "buildMax": "1" }
              },
              "items": [
                {
                  "catalogItemId": "ENH-00002",
                  "family": "Salvage",
                  "subtype": "Invention",
                  "currentDisplayName": "Wrong Prefix",
                  "activeStatus": "Active",
                  "verificationStatus": "VerifiedMultiSource"
                }
              ],
              "aliases": [],
              "enhancementSets": []
            }
            """);

        Assert.False(catalog.IsLoaded);
        Assert.Contains("ENH-", catalog.LoadFailureReason!, StringComparison.Ordinal);
    }

    [Fact]
    public void Multiple_aliases_resolve_to_one_catalog_item_id()
    {
        var catalog = ItemReferenceCatalogFactory.LoadEmbeddedBootstrap();

        Assert.True(catalog.TryResolve("Invention: Fixture Enhancement Sample", out var longForm));
        Assert.True(catalog.TryResolve("Fixture Enhancement Sample", out var shortForm));

        Assert.Equal("ENH-00001", longForm.Item.CatalogItemId);
        Assert.Equal("ENH-00001", shortForm.Item.CatalogItemId);
    }

    [Fact]
    public void Historical_rename_aliases_resolve_to_same_currency_id()
    {
        var catalog = ItemReferenceCatalogFactory.LoadEmbeddedBootstrap();

        Assert.True(catalog.TryResolve("Unstable Aether", out var current));
        Assert.True(catalog.TryResolve("Monstrous Aether", out var historical));

        Assert.Equal("CUR-00009", current.Item.CatalogItemId);
        Assert.Equal("CUR-00009", historical.Item.CatalogItemId);
        Assert.Equal(ReferenceItemFamily.RewardCurrency, current.Item.Family);
        Assert.Equal("Unstable Aether", current.Item.CurrentDisplayName);
    }

    [Fact]
    public void Failed_catalog_try_resolve_returns_false_without_throwing()
    {
        var catalog = LoadCatalog("{ invalid json }");

        Assert.False(catalog.IsLoaded);
        Assert.False(catalog.TryResolve("Anything", out _));
    }

  private static IItemReferenceCatalog LoadCatalog(string json) =>
      ItemReferenceCatalogFactory.LoadFromString(json);
}
