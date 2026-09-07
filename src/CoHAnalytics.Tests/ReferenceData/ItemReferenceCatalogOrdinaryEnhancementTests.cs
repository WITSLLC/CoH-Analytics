using CoHAnalytics.Models;
using CoHAnalytics.ReferenceData;
using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.ReferenceData;

public sealed class ItemReferenceCatalogOrdinaryEnhancementTests
{
    public static readonly string[] VerifiedOrdinaryEnhancementReceiptStrings =
    [
        "Power of Grey (Endurance Reduction)",
        "Insight of Grey (Accuracy)",
        "Extension of Joule (Range)",
        "Boron Exposure (Endurance Reduction)",
        "Shield of Joule (Defense Buff)",
        "Fury of Joule (Damage)",
        "Grace of Joule (Heal/Absorb)",
        "Shield of Hermes (Resist Damage)",
        "Gamma Particle Irradiation (Accuracy)",
        "Renewing of Hermes (Recharge)",
        "Barium Irradiation (Defense Buff)",
        "Alpha Particle Exposure (Recharge)",
        "Paralyzation of Joule (Hold)",
        "Argon Experiment (Resist Damage)",
        "Xenon Exposure (Damage)",
        "Alpha Wave Bombardment (Stun)",
        "Neodymium Irradiation (Range)",
        "Auroral Particle Bombardment (Immobilize)",
        "Impervium Exposure (Endurance Modification)",
        "Ionic Bombardment (Fly)",
        "Pacification of Hermes (Slow)",
        "Polonium Irradiation (Heal/Absorb)",
        "WetWare Eng Cyberheart (Endurance Reduction)",
        "Aim of Joule (ToHit Buff)",
        "Bewildering of Hermes (Stun)",
        "Dragon Rage (Damage)",
        "Nitrogen Exposure (ToHit Buff)",
        "Portacio Ind Internal Munitions (Damage)",
        "Resolution of Grey (Endurance Modification)",
        "Tellurium Bombardment (ToHit DeBuff)",
        "Thallium Exposure (Hold)",
        "Theta Wave Bombardment (Confuse)",
        "WetWare Eng Neuralparalyzer (Immobilize)"
    ];

    [Fact]
    public void Production_catalog_retains_historical_ordinary_enhancement_gate_as_homecoming_historical()
    {
        var catalog = (ItemReferenceCatalog)ItemReferenceCatalogFactory.LoadEmbeddedProduction();
        var authored = catalog.DebugItems.Values
            .Where(i => i.Family == ReferenceItemFamily.Enhancement
                && i.CatalogItemId.CompareTo("ENH-00847") >= 0
                && i.CatalogItemId.CompareTo("ENH-00879") <= 0)
            .ToList();

        Assert.Equal(33, authored.Count);
        Assert.All(authored, i => Assert.Equal("CommonIO", i.Subtype));
        Assert.All(authored, i => Assert.Equal(ReferenceVerificationStatus.VerifiedDirect, i.VerificationStatus));
        Assert.All(authored, i => Assert.Null(i.EnhancementSetId));
        Assert.All(
            authored,
            i => Assert.Equal(
                ReferenceServerAvailabilityStatus.Historical,
                ReferenceServerAvailabilitySupport.TryGetHomecomingStatus(
                    i.ServerAvailability,
                    out var status)
                    ? status
                    : throw new InvalidOperationException(
                        $"Missing Homecoming availability on '{i.CatalogItemId}'.")));
    }

    [Fact]
    public void All_verified_ordinary_enhancement_receipt_strings_resolve_to_current_homecoming_records()
    {
        var catalog = ItemReferenceCatalogFactory.LoadEmbeddedProduction();
        var unresolved = new List<string>();

        foreach (var receipt in VerifiedOrdinaryEnhancementReceiptStrings)
        {
            if (!catalog.TryResolve(receipt, out var resolution))
            {
                unresolved.Add(receipt);
                continue;
            }

            Assert.Equal(ReferenceItemFamily.Enhancement, resolution.Item.Family);
            Assert.Equal(receipt, resolution.MatchedAliasText);
            Assert.True(ReferenceServerAvailabilitySupport.IsCurrentHomecoming(resolution.Item.ServerAvailability));
            Assert.True(ItemReferenceIdRules.TryValidateItemId(
                resolution.Item.CatalogItemId,
                ReferenceItemFamily.Enhancement,
                out _));
        }

        Assert.True(
            unresolved.Count == 0,
            "Unresolved verified ordinary enhancement receipts: " + string.Join(", ", unresolved));
    }

    [Fact]
    public void Verified_ordinary_enhancement_ids_are_unique_and_sequential_for_retained_historical_gate()
    {
        var catalog = (ItemReferenceCatalog)ItemReferenceCatalogFactory.LoadEmbeddedProduction();
        var ids = catalog.DebugItems.Values
            .Where(i => i.Family == ReferenceItemFamily.Enhancement
                && i.CatalogItemId.CompareTo("ENH-00847") >= 0
                && i.CatalogItemId.CompareTo("ENH-00879") <= 0)
            .Select(i => i.CatalogItemId)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            Enumerable.Range(847, 33).Select(n => $"ENH-{n:D5}").ToArray(),
            ids);
    }

    [Fact]
    public void Representative_verified_ordinary_enhancements_classify_as_enhancement()
    {
        var taxonomy = new ItemReferenceReceivedItemTaxonomyCatalog(
            ItemReferenceCatalogFactory.LoadEmbeddedProduction());
        var classifier = new GameplayReceivedItemClassifier(taxonomy);

        foreach (var receipt in new[]
                 {
                     "Power of Grey (Endurance Reduction)",
                     "Shield of Joule (Defense Buff)",
                     "Shield of Hermes (Resist Damage)",
                     "Boron Exposure (Endurance Reduction)",
                     "WetWare Eng Cyberheart (Endurance Reduction)",
                     "Portacio Ind Internal Munitions (Damage)"
                 })
        {
            Assert.True(classifier.TryClassify(receipt, out var category, out var metadata));
            Assert.Equal(GameplaySessionRewardCategory.Enhancement, category);
            Assert.NotNull(metadata);
            Assert.Equal("OriginOrTraining", metadata!.EnhancementTypeLabel);
        }
    }

    [Fact]
    public void All_verified_ordinary_enhancement_receipt_strings_classify_as_enhancement()
    {
        var taxonomy = new ItemReferenceReceivedItemTaxonomyCatalog(
            ItemReferenceCatalogFactory.LoadEmbeddedProduction());
        var classifier = new GameplayReceivedItemClassifier(taxonomy);
        var failures = new List<string>();

        foreach (var receipt in VerifiedOrdinaryEnhancementReceiptStrings)
        {
            if (!classifier.TryClassify(receipt, out var category, out _)
                || category != GameplaySessionRewardCategory.Enhancement)
            {
                failures.Add(receipt);
            }
        }

        Assert.True(
            failures.Count == 0,
            "Verified ordinary enhancements that did not classify as Enhancement: "
            + string.Join(", ", failures));
    }
}
