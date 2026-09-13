using CoHAnalytics.Models;
using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Services;

/// <summary>
/// Independently specified canonical-field goldens. Expected values are authored in this file
/// and are not derived from CanonicalToLegacyAdapter or Normalizer mapping helpers.
/// </summary>
public sealed class Slice2CanonicalFieldGoldenTests
{
    [Theory]
    [MemberData(nameof(GoldenData))]
    public void Canonical_fields_match_independent_golden(CanonicalFieldGolden expected)
    {
        AssertCanonical(expected);
    }

    [Fact]
    public void Independent_goldens_cover_every_current_CombatGrammarId_exactly_once()
    {
        var covered = Goldens.Select(row => row.GrammarId).ToArray();
        Assert.Equal(covered.Length, covered.Distinct().Count());
        Assert.Equal(Enum.GetValues<CombatGrammarId>().Order().ToArray(), covered.Order().ToArray());
    }

    [Fact]
    public void Incoming_damage_actor_is_the_source_and_target_is_self()
    {
        var actual = Normalize(
            "2026-08-04 12:00:00 Crey Thorn Mook hits you with Bone Shard for 22.15 points of Lethal damage.");
        Assert.Equal(CombatGrammarId.Dmg03SourceHitsYouWithPower, actual.GrammarId);
        Assert.Equal(CombatEventFamily.DamageReceived, actual.Family);
        Assert.Equal(ActorType.Unknown, actual.Actor.Type);
        Assert.Equal("Crey Thorn Mook", actual.Actor.DisplayName);
        Assert.Equal(ActorType.Self, actual.Target!.Type);
        Assert.Null(actual.Target.DisplayName);
        Assert.Equal(EventFacets.DamageReceived, actual.Facets);
    }

    [Fact]
    public void Incoming_heal_actor_is_the_source_and_target_is_self()
    {
        var actual = Normalize(
            "2026-08-04 12:00:00 Example Medic heals you for 42.25 hit points with Aid.");
        Assert.Equal(CombatGrammarId.Heal03SourceHealsYou, actual.GrammarId);
        Assert.Equal(CombatEventFamily.HealReceived, actual.Family);
        Assert.Equal(ActorType.Unknown, actual.Actor.Type);
        Assert.Equal("Example Medic", actual.Actor.DisplayName);
        Assert.Equal(ActorType.Self, actual.Target!.Type);
        Assert.Null(actual.Target.DisplayName);
        Assert.Equal(EventFacets.HealReceived, actual.Facets);
    }

    [Fact]
    public void Self_heal_actor_and_target_are_both_self()
    {
        var actual = Normalize(
            "2026-08-04 12:00:00 You heal yourself for 15.00 hit points with Regeneration.");
        Assert.Equal(CombatGrammarId.Heal02YouHealYourself, actual.GrammarId);
        Assert.Equal(CombatEventFamily.HealDealt, actual.Family);
        Assert.Equal(ActorType.Self, actual.Actor.Type);
        Assert.Null(actual.Actor.DisplayName);
        Assert.Equal(ActorType.Self, actual.Target!.Type);
        Assert.Equal("yourself", actual.Target.DisplayName);
        Assert.Equal(EventFacets.HealDelivered, actual.Facets);
    }

    [Fact]
    public void Dot_delivery_is_flagged_without_containment()
    {
        var actual = Normalize(
            "2026-08-04 12:00:01 You hit Lusca with your Fire Cages for 5.59 points of Fire damage over time.");
        Assert.Equal(CombatGrammarId.Dmg01YouHitWithPower, actual.GrammarId);
        Assert.Equal(CombatEventFamily.DamageDealt, actual.Family);
        Assert.Equal(ActorType.Self, actual.Actor.Type);
        Assert.Equal(ActorType.Unknown, actual.Target!.Type);
        Assert.Equal("Lusca", actual.Target.DisplayName);
        Assert.Equal(DeliveryFlags.DoT, actual.Delivery);
        Assert.Null(actual.EffectSuffix);
        Assert.Equal(new CombatScaledAmount(559), actual.Amount);
        Assert.True(actual.DamageType is { Text: "Fire", IsUnresistable: false, IsUnique: false });
    }

    [Fact]
    public void Containment_suffix_is_preserved_with_dot_flag()
    {
        var actual = Normalize(
            "2026-08-04 12:00:02 You hit Lusca with your Fire Cages for 10.36 points of Fire damage over time (CONTAINMENT).");
        Assert.Equal(CombatGrammarId.Dmg01YouHitWithPower, actual.GrammarId);
        Assert.Equal(CombatEventFamily.DamageDealt, actual.Family);
        Assert.Equal(DeliveryFlags.DoT | DeliveryFlags.Containment, actual.Delivery);
        Assert.Equal("CONTAINMENT", actual.EffectSuffix);
        Assert.Equal(new CombatScaledAmount(1036), actual.Amount);
    }

    public static TheoryData<CanonicalFieldGolden> GoldenData
    {
        get
        {
            var data = new TheoryData<CanonicalFieldGolden>();
            foreach (var row in Goldens)
            {
                data.Add(row);
            }

            return data;
        }
    }

    private static readonly CanonicalFieldGolden[] Goldens =
    [
        new(
            Line: "2026-08-04 12:00:00 You hit Lusca with your Hot Feet for 13.88 points of Fire damage.",
            GrammarId: CombatGrammarId.Dmg01YouHitWithPower,
            Family: CombatEventFamily.DamageDealt,
            ActorType: ActorType.Self,
            ActorDisplayName: null,
            TargetType: ActorType.Unknown,
            TargetDisplayName: "Lusca",
            PowerName: "Hot Feet",
            AmountHundredths: 1388,
            Magnitude: MagnitudeKind.HitPoints,
            DamageTypeText: "Fire",
            Delivery: DeliveryFlags.None,
            EffectSuffix: null,
            Outcome: null,
            DisplayedChanceHundredths: null,
            RollHundredths: null,
            SourceTimestamp: new DateTime(2026, 8, 4, 12, 0, 0, DateTimeKind.Unspecified),
            Facets: EventFacets.DamageDealt),
        new(
            Line: "2026-08-04 12:00:00 You hit Training Dummy for 12 points of Fire damage.",
            GrammarId: CombatGrammarId.Dmg02YouHitWithoutPower,
            Family: CombatEventFamily.DamageDealt,
            ActorType: ActorType.Self,
            ActorDisplayName: null,
            TargetType: ActorType.Unknown,
            TargetDisplayName: "Training Dummy",
            PowerName: null,
            AmountHundredths: 1200,
            Magnitude: MagnitudeKind.HitPoints,
            DamageTypeText: "Fire",
            Delivery: DeliveryFlags.None,
            EffectSuffix: null,
            Outcome: null,
            DisplayedChanceHundredths: null,
            RollHundredths: null,
            SourceTimestamp: new DateTime(2026, 8, 4, 12, 0, 0, DateTimeKind.Unspecified),
            Facets: EventFacets.DamageDealt),
        new(
            Line: "2026-08-04 12:00:00 Crey Thorn Mook hits you with Bone Shard for 22.15 points of Lethal damage.",
            GrammarId: CombatGrammarId.Dmg03SourceHitsYouWithPower,
            Family: CombatEventFamily.DamageReceived,
            ActorType: ActorType.Unknown,
            ActorDisplayName: "Crey Thorn Mook",
            TargetType: ActorType.Self,
            TargetDisplayName: null,
            PowerName: "Bone Shard",
            AmountHundredths: 2215,
            Magnitude: MagnitudeKind.HitPoints,
            DamageTypeText: "Lethal",
            Delivery: DeliveryFlags.None,
            EffectSuffix: null,
            Outcome: null,
            DisplayedChanceHundredths: null,
            RollHundredths: null,
            SourceTimestamp: new DateTime(2026, 8, 4, 12, 0, 0, DateTimeKind.Unspecified),
            Facets: EventFacets.DamageReceived),
        new(
            Line: "2026-08-04 12:00:00 Fictional Target hits you for 15 points of lethal damage.",
            GrammarId: CombatGrammarId.Dmg04SourceHitsYouWithoutPower,
            Family: CombatEventFamily.DamageReceived,
            ActorType: ActorType.Unknown,
            ActorDisplayName: "Fictional Target",
            TargetType: ActorType.Self,
            TargetDisplayName: null,
            PowerName: null,
            AmountHundredths: 1500,
            Magnitude: MagnitudeKind.HitPoints,
            DamageTypeText: "Lethal",
            Delivery: DeliveryFlags.None,
            EffectSuffix: null,
            Outcome: null,
            DisplayedChanceHundredths: null,
            RollHundredths: null,
            SourceTimestamp: new DateTime(2026, 8, 4, 12, 0, 0, DateTimeKind.Unspecified),
            Facets: EventFacets.DamageReceived),
        new(
            Line: "2026-08-04 12:00:00 Lieutenant Skull critically hits you with Sniper Rifle for 55.0 points of Lethal damage.",
            GrammarId: CombatGrammarId.Dmg05SourceCriticallyHitsYouWithPower,
            Family: CombatEventFamily.DamageReceived,
            ActorType: ActorType.Unknown,
            ActorDisplayName: "Lieutenant Skull",
            TargetType: ActorType.Self,
            TargetDisplayName: null,
            PowerName: "Sniper Rifle",
            AmountHundredths: 5500,
            Magnitude: MagnitudeKind.HitPoints,
            DamageTypeText: "Lethal",
            Delivery: DeliveryFlags.Critical,
            EffectSuffix: null,
            Outcome: null,
            DisplayedChanceHundredths: null,
            RollHundredths: null,
            SourceTimestamp: new DateTime(2026, 8, 4, 12, 0, 0, DateTimeKind.Unspecified),
            Facets: EventFacets.DamageReceived),
        new(
            Line: "2026-08-04 12:00:00 You heal Example Ally for 78.50 hit points with Healing Aura.",
            GrammarId: CombatGrammarId.Heal01YouHealTarget,
            Family: CombatEventFamily.HealDealt,
            ActorType: ActorType.Self,
            ActorDisplayName: null,
            TargetType: ActorType.Unknown,
            TargetDisplayName: "Example Ally",
            PowerName: "Healing Aura",
            AmountHundredths: 7850,
            Magnitude: MagnitudeKind.HitPoints,
            DamageTypeText: null,
            Delivery: DeliveryFlags.None,
            EffectSuffix: null,
            Outcome: null,
            DisplayedChanceHundredths: null,
            RollHundredths: null,
            SourceTimestamp: new DateTime(2026, 8, 4, 12, 0, 0, DateTimeKind.Unspecified),
            Facets: EventFacets.HealDelivered),
        new(
            Line: "2026-08-04 12:00:00 You heal yourself for 15.00 hit points with Regeneration.",
            GrammarId: CombatGrammarId.Heal02YouHealYourself,
            Family: CombatEventFamily.HealDealt,
            ActorType: ActorType.Self,
            ActorDisplayName: null,
            TargetType: ActorType.Self,
            TargetDisplayName: "yourself",
            PowerName: "Regeneration",
            AmountHundredths: 1500,
            Magnitude: MagnitudeKind.HitPoints,
            DamageTypeText: null,
            Delivery: DeliveryFlags.None,
            EffectSuffix: null,
            Outcome: null,
            DisplayedChanceHundredths: null,
            RollHundredths: null,
            SourceTimestamp: new DateTime(2026, 8, 4, 12, 0, 0, DateTimeKind.Unspecified),
            Facets: EventFacets.HealDelivered),
        new(
            Line: "2026-08-04 12:00:00 Example Medic heals you for 42.25 hit points with Aid.",
            GrammarId: CombatGrammarId.Heal03SourceHealsYou,
            Family: CombatEventFamily.HealReceived,
            ActorType: ActorType.Unknown,
            ActorDisplayName: "Example Medic",
            TargetType: ActorType.Self,
            TargetDisplayName: null,
            PowerName: "Aid",
            AmountHundredths: 4225,
            Magnitude: MagnitudeKind.HitPoints,
            DamageTypeText: null,
            Delivery: DeliveryFlags.None,
            EffectSuffix: null,
            Outcome: null,
            DisplayedChanceHundredths: null,
            RollHundredths: null,
            SourceTimestamp: new DateTime(2026, 8, 4, 12, 0, 0, DateTimeKind.Unspecified),
            Facets: EventFacets.HealReceived),
        new(
            Line: "2026-08-04 12:00:00 You activate Fire Cages.",
            GrammarId: CombatGrammarId.Act01YouActivate,
            Family: CombatEventFamily.Activation,
            ActorType: ActorType.Self,
            ActorDisplayName: null,
            TargetType: null,
            TargetDisplayName: null,
            PowerName: "Fire Cages",
            AmountHundredths: 0,
            Magnitude: MagnitudeKind.None,
            DamageTypeText: null,
            Delivery: DeliveryFlags.None,
            EffectSuffix: null,
            Outcome: null,
            DisplayedChanceHundredths: null,
            RollHundredths: null,
            SourceTimestamp: new DateTime(2026, 8, 4, 12, 0, 0, DateTimeKind.Unspecified),
            Facets: EventFacets.Activation),
        new(
            Line: "2026-08-04 12:00:00 You activated the Fire Cages power.",
            GrammarId: CombatGrammarId.Act02YouActivatedThePower,
            Family: CombatEventFamily.Activation,
            ActorType: ActorType.Self,
            ActorDisplayName: null,
            TargetType: null,
            TargetDisplayName: null,
            PowerName: "Fire Cages",
            AmountHundredths: 0,
            Magnitude: MagnitudeKind.None,
            DamageTypeText: null,
            Delivery: DeliveryFlags.None,
            EffectSuffix: null,
            Outcome: null,
            DisplayedChanceHundredths: null,
            RollHundredths: null,
            SourceTimestamp: new DateTime(2026, 8, 4, 12, 0, 0, DateTimeKind.Unspecified),
            Facets: EventFacets.Activation),
        new(
            Line: "2026-08-04 12:00:00 You have defeated Lusca",
            GrammarId: CombatGrammarId.Def01YouHaveDefeated,
            Family: CombatEventFamily.Defeat,
            ActorType: ActorType.Self,
            ActorDisplayName: null,
            TargetType: ActorType.Unknown,
            TargetDisplayName: "Lusca",
            PowerName: null,
            AmountHundredths: 0,
            Magnitude: MagnitudeKind.None,
            DamageTypeText: null,
            Delivery: DeliveryFlags.None,
            EffectSuffix: null,
            Outcome: null,
            DisplayedChanceHundredths: null,
            RollHundredths: null,
            SourceTimestamp: new DateTime(2026, 8, 4, 12, 0, 0, DateTimeKind.Unspecified),
            Facets: EventFacets.Defeat),
        new(
            Line: "2026-08-04 12:00:00 Psiche has defeated Prototype Oscillator",
            GrammarId: CombatGrammarId.Def02OtherPlayerDefeated,
            Family: CombatEventFamily.Defeat,
            ActorType: ActorType.Unknown,
            ActorDisplayName: "Psiche",
            TargetType: ActorType.Unknown,
            TargetDisplayName: "Prototype Oscillator",
            PowerName: null,
            AmountHundredths: 0,
            Magnitude: MagnitudeKind.None,
            DamageTypeText: null,
            Delivery: DeliveryFlags.None,
            EffectSuffix: null,
            Outcome: null,
            DisplayedChanceHundredths: null,
            RollHundredths: null,
            SourceTimestamp: new DateTime(2026, 8, 4, 12, 0, 0, DateTimeKind.Unspecified),
            Facets: EventFacets.Defeat),
        new(
            Line: "2026-08-06 12:00:00 HIT Rikti Pylon! Your Flashfire power had a 95.00% chance to hit, you rolled a 51.51.",
            GrammarId: CombatGrammarId.Acc01RolledHit,
            Family: CombatEventFamily.AttackResolution,
            ActorType: ActorType.Self,
            ActorDisplayName: null,
            TargetType: ActorType.Unknown,
            TargetDisplayName: "Rikti Pylon",
            PowerName: "Flashfire",
            AmountHundredths: 0,
            Magnitude: MagnitudeKind.None,
            DamageTypeText: null,
            Delivery: DeliveryFlags.None,
            EffectSuffix: null,
            Outcome: CombatAttackOutcome.Hit,
            DisplayedChanceHundredths: 9500,
            RollHundredths: 5151,
            SourceTimestamp: new DateTime(2026, 8, 6, 12, 0, 0, DateTimeKind.Unspecified),
            Facets: EventFacets.AttackResolution),
        new(
            Line: "2026-08-06 12:00:00 MISSED Spirit!! Your Fire Cages power had a 95.00% chance to hit, you rolled a 97.54.",
            GrammarId: CombatGrammarId.Acc02RolledMiss,
            Family: CombatEventFamily.AttackResolution,
            ActorType: ActorType.Self,
            ActorDisplayName: null,
            TargetType: ActorType.Unknown,
            TargetDisplayName: "Spirit",
            PowerName: "Fire Cages",
            AmountHundredths: 0,
            Magnitude: MagnitudeKind.None,
            DamageTypeText: null,
            Delivery: DeliveryFlags.None,
            EffectSuffix: null,
            Outcome: CombatAttackOutcome.Miss,
            DisplayedChanceHundredths: 9500,
            RollHundredths: 9754,
            SourceTimestamp: new DateTime(2026, 8, 6, 12, 0, 0, DateTimeKind.Unspecified),
            Facets: EventFacets.AttackResolution),
        new(
            Line: "2026-08-06 12:00:00 HIT Lieutenant Skull! Your Fire Bolt power was forced to hit by streakbreaker.",
            GrammarId: CombatGrammarId.Acc03ForcedHit,
            Family: CombatEventFamily.AttackResolution,
            ActorType: ActorType.Self,
            ActorDisplayName: null,
            TargetType: ActorType.Unknown,
            TargetDisplayName: "Lieutenant Skull",
            PowerName: "Fire Bolt",
            AmountHundredths: 0,
            Magnitude: MagnitudeKind.None,
            DamageTypeText: null,
            Delivery: DeliveryFlags.Forced,
            EffectSuffix: null,
            Outcome: CombatAttackOutcome.Hit,
            DisplayedChanceHundredths: null,
            RollHundredths: null,
            SourceTimestamp: new DateTime(2026, 8, 6, 12, 0, 0, DateTimeKind.Unspecified),
            Facets: EventFacets.AttackResolution),
        new(
            Line: "2026-08-06 12:00:00 HIT Training Dummy! Your Siphon Power power is autohit.",
            GrammarId: CombatGrammarId.Acc04Autohit,
            Family: CombatEventFamily.AttackResolution,
            ActorType: ActorType.Self,
            ActorDisplayName: null,
            TargetType: ActorType.Unknown,
            TargetDisplayName: "Training Dummy",
            PowerName: "Siphon Power",
            AmountHundredths: 0,
            Magnitude: MagnitudeKind.None,
            DamageTypeText: null,
            Delivery: DeliveryFlags.Autohit,
            EffectSuffix: null,
            Outcome: CombatAttackOutcome.Hit,
            DisplayedChanceHundredths: null,
            RollHundredths: null,
            SourceTimestamp: new DateTime(2026, 8, 6, 12, 0, 0, DateTimeKind.Unspecified),
            Facets: EventFacets.AttackResolution)
    ];

    private static void AssertCanonical(CanonicalFieldGolden expected)
    {
        var actual = Normalize(expected.Line);
        Assert.Equal(expected.GrammarId, actual.GrammarId);
        Assert.Equal(expected.Family, actual.Family);
        Assert.Equal(expected.ActorType, actual.Actor.Type);
        Assert.Equal(expected.ActorDisplayName, actual.Actor.DisplayName);
        if (expected.TargetType is null)
        {
            Assert.Null(actual.Target);
        }
        else
        {
            Assert.Equal(expected.TargetType, actual.Target!.Type);
            Assert.Equal(expected.TargetDisplayName, actual.Target.DisplayName);
        }

        Assert.Equal(expected.PowerName, actual.PowerName);
        Assert.Equal(new CombatScaledAmount(expected.AmountHundredths), actual.Amount);
        Assert.Equal(expected.Magnitude, actual.Magnitude);
        if (expected.DamageTypeText is null)
        {
            Assert.Null(actual.DamageType);
        }
        else
        {
            Assert.True(actual.DamageType.HasValue);
            Assert.Equal(expected.DamageTypeText, actual.DamageType.Value.Text);
            Assert.False(actual.DamageType.Value.IsUnresistable);
            Assert.False(actual.DamageType.Value.IsUnique);
        }

        Assert.Equal(expected.Delivery, actual.Delivery);
        Assert.Equal(expected.EffectSuffix, actual.EffectSuffix);
        Assert.Equal(expected.Outcome, actual.Outcome);
        Assert.Equal(expected.DisplayedChanceHundredths, actual.DisplayedChanceHundredths);
        Assert.Equal(expected.RollHundredths, actual.RollHundredths);
        Assert.Equal(expected.SourceTimestamp, actual.SourceTimestamp);
        Assert.Equal(expected.Facets, actual.Facets);
        Assert.Null(actual.SourceChannel);
        Assert.Null(actual.DuplicateOf);
        Assert.Equal(expected.Family, actual.MirrorClass.Family);
        Assert.Equal(expected.GrammarId, actual.MirrorClass.GrammarId);
        Assert.Null(actual.MirrorClass.SourceChannel);
    }

    private static CanonicalCombatEvent Normalize(string line)
    {
        var input = CombatEventParserTestSupport.Classify(line);
        Assert.True(ParserLineEnvelope.TryGetBody(input.RawLine, input.SourceId.LogDate, out var body));
        foreach (var match in GrammarMatcher.EnumerateMatches(body))
        {
            if (Normalizer.TryNormalize(match, input, out var canonical))
            {
                return canonical;
            }
        }

        Assert.Fail($"No canonical event for: {line}");
        return null!;
    }

    public sealed record CanonicalFieldGolden(
        string Line,
        CombatGrammarId GrammarId,
        CombatEventFamily Family,
        ActorType ActorType,
        string? ActorDisplayName,
        ActorType? TargetType,
        string? TargetDisplayName,
        string? PowerName,
        long AmountHundredths,
        MagnitudeKind Magnitude,
        string? DamageTypeText,
        DeliveryFlags Delivery,
        string? EffectSuffix,
        CombatAttackOutcome? Outcome,
        long? DisplayedChanceHundredths,
        long? RollHundredths,
        DateTime? SourceTimestamp,
        EventFacets Facets)
    {
        public override string ToString() => GrammarId.ToString();
    }
}
