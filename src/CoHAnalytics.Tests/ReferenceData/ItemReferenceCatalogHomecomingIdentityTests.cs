using CoHAnalytics.ReferenceData;

namespace CoHAnalytics.Tests.ReferenceData;

/// <summary>
/// Reference 2C — Homecoming-observed enhancement identity resolution.
/// Identity only; no mechanics assertions.
/// </summary>
public sealed class ItemReferenceCatalogHomecomingIdentityTests
{
    [Fact]
    public void Ordinary_set_piece_resolves_to_stable_id()
    {
        var catalog = ItemReferenceCatalogFactory.LoadEmbeddedProduction();

        Assert.True(catalog.TryResolve("Scirocco's Dervish: Chance for Lethal Damage", out var resolution));
        Assert.Equal(ReferenceItemFamily.Enhancement, resolution.Item.Family);
        Assert.Equal("SET-00018", resolution.Item.EnhancementSetId);
        Assert.Equal(
            "Scirocco's Dervish: Chance for Lethal Damage",
            resolution.Item.CurrentDisplayName);
        Assert.Equal(ReferenceVerificationStatus.VerifiedDirect, resolution.Item.VerificationStatus);
    }

    [Fact]
    public void Abbreviated_and_expanded_observed_forms_resolve_to_same_item()
    {
        var catalog = ItemReferenceCatalogFactory.LoadEmbeddedProduction();

        Assert.True(catalog.TryResolve("Annihilation: Accuracy/Damage/Recharge", out var expanded));
        Assert.True(catalog.TryResolve("Annihilation: Acc/Dmg/Rech", out var abbreviated));

        Assert.Equal(expanded.Item.CatalogItemId, abbreviated.Item.CatalogItemId);
        Assert.Equal("Annihilation: Accuracy/Damage/Recharge", expanded.Item.CurrentDisplayName);
    }

    [Fact]
    public void Component_ordering_observed_form_is_preserved_for_current_homecoming_records()
    {
        var catalog = ItemReferenceCatalogFactory.LoadEmbeddedProduction();

        Assert.True(catalog.TryResolve("Gladiator's Armor: Resistance/Recharge", out var resolution));
        Assert.Equal("Gladiator's Armor: Resistance/Recharge", resolution.Item.CurrentDisplayName);
        Assert.True(ReferenceServerAvailabilitySupport.IsCurrentHomecoming(resolution.Item.ServerAvailability));
        Assert.False(catalog.TryResolve("Gladiator's Armor: Rech/Res", out _));
        Assert.True(catalog.TryResolve(
            "Gladiator's Armor: Rech/Res",
            out var historical,
            ReferenceCatalogQueryScope.AllHomecomingIdentities));
        Assert.True(ReferenceServerAvailabilitySupport.IsHistoricalHomecoming(historical.Item.ServerAvailability));
    }

    [Fact]
    public void Proc_special_piece_uses_homecoming_identity_not_mechanical_label()
    {
        var catalog = ItemReferenceCatalogFactory.LoadEmbeddedProduction();

        Assert.True(
            catalog.TryResolve(
                "Luck of the Gambler: Defense/Increased Global Recharge Speed",
                out var identity));
        Assert.Equal(
            "Luck of the Gambler: Defense/Increased Global Recharge Speed",
            identity.Item.CurrentDisplayName);

        // Mechanical / shorthand labels must not silently resolve.
        Assert.False(catalog.TryResolve("Luck of the Gambler: +7.5% Global Recharge", out _));
        Assert.False(catalog.TryResolve("Luck of the Gambler: Def/7.5% Rech Time", out _));

        // Ordinary Defense/Recharge is a distinct piece already in the catalog.
        Assert.True(catalog.TryResolve("Luck of the Gambler: Defense/Recharge", out var ordinary));
        Assert.NotEqual(ordinary.Item.CatalogItemId, identity.Item.CatalogItemId);
    }

    [Fact]
    public void Homecoming_canonical_plus_mids_typo_alias_resolve_together()
    {
        var catalog = ItemReferenceCatalogFactory.LoadEmbeddedProduction();

        Assert.True(
            catalog.TryResolve(
                "Numina's Convalescence: Healing/Absorb/Endurance/Recharge",
                out var canonical));
        Assert.True(
            catalog.TryResolve(
                "Numina's Convalesence: Healing/Absorb/Endurance/Recharge",
                out var typo));

        Assert.Equal(canonical.Item.CatalogItemId, typo.Item.CatalogItemId);
        Assert.Equal(
            "Numina's Convalescence: Healing/Absorb/Endurance/Recharge",
            canonical.Item.CurrentDisplayName);
    }

    [Fact]
    public void Multiple_observed_homecoming_aliases_resolve_to_one_id()
    {
        var catalog = ItemReferenceCatalogFactory.LoadEmbeddedProduction();

        Assert.True(catalog.TryResolve("Aegis: Psionic/Status Resistance", out var received));
        Assert.True(catalog.TryResolve("Aegis: +Psionic and Mez Resist", out var recipe));

        Assert.Equal(received.Item.CatalogItemId, recipe.Item.CatalogItemId);
        Assert.Equal("Aegis: Psionic/Status Resistance", received.Item.CurrentDisplayName);
    }

    [Fact]
    public void Distinct_observed_pieces_do_not_share_identity()
    {
        var catalog = ItemReferenceCatalogFactory.LoadEmbeddedProduction();

        Assert.True(catalog.TryResolve("Impervium Armor: Resistance", out var resistance));
        Assert.True(catalog.TryResolve("Impervium Armor: Psionic Resistance", out var psi));

        Assert.NotEqual(resistance.Item.CatalogItemId, psi.Item.CatalogItemId);
        Assert.Equal("SET-00138", resistance.Item.EnhancementSetId);
        Assert.Equal("SET-00138", psi.Item.EnhancementSetId);

        Assert.True(catalog.TryResolve("Aegis: Resistance", out var aegisRes));
        Assert.True(catalog.TryResolve("Aegis: Psionic/Status Resistance", out var aegisPsi));
        Assert.NotEqual(aegisRes.Item.CatalogItemId, aegisPsi.Item.CatalogItemId);
    }

    [Fact]
    public void Unresolved_names_still_return_false_rather_than_guessing()
    {
        var catalog = ItemReferenceCatalogFactory.LoadEmbeddedProduction();

        Assert.False(catalog.TryResolve("Pounding Slugfest: Chance for Stun", out _));
        Assert.True(catalog.TryResolve("Pounding Slugfest: Disorient Bonus", out var disorient));
        Assert.True(ReferenceServerAvailabilitySupport.IsCurrentHomecoming(disorient.Item.ServerAvailability));
        Assert.False(catalog.TryResolve("Winter's Bite: Dam/Rech", out _));
        Assert.False(catalog.TryResolve("Winter's Bite: Damage/RechargeTime", out _));
        Assert.False(catalog.TryResolve("Hecatomb: Chance for Neg Energy Dam", out _));
        Assert.False(catalog.TryResolve("Hecatomb: Chance of Damage(Negative)", out _));
    }

    [Fact]
    public void Accepted_homecoming_identity_audit_resolves_deterministically()
    {
        var catalog = ItemReferenceCatalogFactory.LoadEmbeddedProduction();
        var accepted = new[]
        {
            "Hecatomb: Chance for Negative Energy Damage",
            "Scirocco's Dervish: Chance for Lethal Damage",
            "Fury of the Gladiator: Chance for -Res",
            "Annihilation: Accuracy/Damage/Recharge",
            "Annihilation: Acc/Dmg/Rech",
            "Annihilation: Accuracy/Damage/Recharge/Endurance",
            "Annihilation: Acc/Dmg/End/Rech",
            "Annihilation: Chance for -Res",
            "Extreme Measures: Acc/Int/Range",
            "Extreme Measures: Dam/Int/Rech",
            "Will of the Controller: Accuracy/Control Duration",
            "Command of the Mastermind: Recharge/Pet +AoE Defense Aura",
            "Touch of the Nictus: Healing/Absorb",
            "Touch of the Nictus: Heal/Absorb",
            "Touch of the Nictus: Healing/Absorb/Recharge",
            "Touch of the Nictus: Heal/Absorb/Rech",
            "Touch of the Nictus: Accuracy/Endurance/Healing/Absorb",
            "Touch of the Nictus: Acc/End/Heal/Absorb",
            "Doctored Wounds: End/Heal/Absorb",
            "Numina's Convalescence: Healing/Absorb/Endurance/Recharge",
            "Numina's Convalesence: Healing/Absorb/Endurance/Recharge",
            "Panacea: Healing/Absorb/Endurance/Recharge",
            "Panacea: End/Heal/Absorb/Rech",
            "Kismet: +ToHit",
            "Reactive Defenses: Scaling Damage Resistance",
            "Luck of the Gambler: Defense/Increased Global Recharge Speed",
            "Shield Wall: +Res (Teleportation), +5% Res (All)",
            "Impervium Armor: Psionic Resistance",
            "Aegis: Psionic/Status Resistance",
            "Aegis: +Psionic and Mez Resist",
            "Gladiator's Armor: Rech/Res",
            "Coercive Persuasion: Confuse Duration/Rech/Acc (Superior)",
            "Gravitational Anchor: Immobilize Duration/Rech (Superior)",
            "Gravitational Anchor: Immobilize Duration/Rech/Acc (Superior)",
            "Mocking Beratement: Taunt/Placate/Rech",
            "Mocking Beratement: Taunt/Placate/Rech/Range",
            "Perfect Zinger: Taunt/Placate/Recharge",
            "Perfect Zinger: Taunt/Placate/Rech",
            "Perfect Zinger: Taunt/Placate/Rech/Range",
            "Soaring: Fly/Endurance",
            "Unbounded Leap: Jump",
            "Unbounded Leap: Stealth",
            "Time & Space Manipulation: Stealth",
            "Blessing of the Zephyr: Run Speed, Jump, Flight Speed, Range/Endurance",
            "Blessing of the Zephyr: Knockback Reduction (4 points)",
            "Undermined Defenses: Defense Debuff",
            "Undermined Defenses: Defense Debuff/Endurance Reduction",
            "Performance Shifter: Endurance Modification/Recharge/Accuracy",
            "Performance Shifter: End Mod/Rech/Acc",
            "Performance Shifter: Recharge/Accuracy",
            "Performance Shifter: Rech/Acc",
            "Adjusted Targeting: To Hit Buff/Rech/End Reduction",
            "Gaussian's Synchronized Fire-Control: To Hit Buff",
            "Dampened Spirits: To Hit DeBuff/End Reduction",
            "Dark Watcher's Despair: To Hit Debuff",
            "Dark Watcher's Despair: To Hit Debuff/Recharge",
            "Dark Watcher's Despair: To Hit Debuff/Recharge/Endurance Reduction",
            "Dark Watcher's Despair: To Hit Debuff/Endurance Reduction",
            "Dark Watcher's Despair: Chance for Recharge Slow",
        };

        var ids = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in accepted)
        {
            if (!catalog.TryResolve(name, out var resolution))
            {
                Assert.True(
                    catalog.TryResolve(name, out resolution, ReferenceCatalogQueryScope.AllHomecomingIdentities),
                    $"Failed to resolve '{name}'");
            }

            Assert.StartsWith("ENH-", resolution.Item.CatalogItemId);
            ids[name] = resolution.Item.CatalogItemId;
        }

        // Alias pairs that must share one ID
        Assert.Equal(ids["Annihilation: Accuracy/Damage/Recharge"], ids["Annihilation: Acc/Dmg/Rech"]);
        Assert.Equal(
            ids["Annihilation: Accuracy/Damage/Recharge/Endurance"],
            ids["Annihilation: Acc/Dmg/End/Rech"]);
        Assert.Equal(ids["Aegis: Psionic/Status Resistance"], ids["Aegis: +Psionic and Mez Resist"]);
        Assert.Equal(
            ids["Numina's Convalescence: Healing/Absorb/Endurance/Recharge"],
            ids["Numina's Convalesence: Healing/Absorb/Endurance/Recharge"]);

        // Distinct pieces must not collide
        Assert.NotEqual(
            ids["Gravitational Anchor: Immobilize Duration/Rech (Superior)"],
            ids["Gravitational Anchor: Immobilize Duration/Rech/Acc (Superior)"]);
        Assert.NotEqual(ids["Unbounded Leap: Jump"], ids["Unbounded Leap: Stealth"]);
    }

    [Fact]
    public void Dominating_Grasp_client_ui_names_resolve()
    {
        var catalog = ItemReferenceCatalogFactory.LoadEmbeddedProduction();
        var names = new[]
        {
            "Dominating Grasp: Accuracy/Control Duration (Dominator)",
            "Dominating Grasp: Control Duration/Recharge (Dominator)",
            "Dominating Grasp: Endurance/Recharge (Dominator)",
            "Dominating Grasp: Accuracy/Control Duration/Endurance (Dominator)",
            "Dominating Grasp: Accuracy/Control Duration/Endurance/Recharge (Dominator)",
            "Dominating Grasp: Recharge/Chance for Fiery Orb (Dominator)",
        };

        var ids = new HashSet<string>();
        foreach (var name in names)
        {
            Assert.True(catalog.TryResolve(name, out var resolution), $"Failed to resolve '{name}'");
            Assert.Equal("SET-00087", resolution.Item.EnhancementSetId);
            Assert.Equal(ReferenceVerificationStatus.VerifiedDirect, resolution.Item.VerificationStatus);
            ids.Add(resolution.Item.CatalogItemId);
        }

        Assert.Equal(6, ids.Count);

        // Wiki/Mids shorthand without client UI evidence must not guess.
        Assert.False(catalog.TryResolve("Dominating Grasp: Acc/Control", out _));
        Assert.False(catalog.TryResolve("Dominating Grasp: Control Duration/RechargeTime", out _));
    }
}
