using CoHAnalytics.Models;

namespace CoHAnalytics.Services;

/// <summary>
/// Temporary mapping from <see cref="CanonicalCombatEvent"/> to today's <see cref="CombatEvent"/>.
/// Does not re-parse text or amounts.
/// </summary>
internal static class CanonicalToLegacyAdapter
{
    public static CombatEvent ToLegacy(CanonicalCombatEvent canonical)
    {
        ArgumentNullException.ThrowIfNull(canonical);

        return new CombatEvent
        {
            ContextId = canonical.Provenance.ContextId,
            ParserSequence = canonical.Provenance.ParserSequence,
            ObservedAt = canonical.ObservedAt,
            SourceTimestamp = canonical.SourceTimestamp,
            Kind = ToLegacyKind(canonical.Family),
            GrammarId = canonical.GrammarId,
            ActorRole = ToLegacyActorRole(canonical.Actor.Type),
            TargetName = ToLegacyTargetName(canonical),
            SourceName = ToLegacySourceName(canonical),
            PowerName = canonical.PowerName,
            Amount = canonical.Amount,
            DamageType = canonical.DamageType?.Text,
            IsOverTime = canonical.Delivery.HasFlag(DeliveryFlags.DoT),
            EffectSuffix = canonical.EffectSuffix,
            AttackOutcome = canonical.Outcome,
            DisplayedChanceHundredths = canonical.DisplayedChanceHundredths,
            RollHundredths = canonical.RollHundredths,
            WasRolled = IsRolled(canonical),
            WasForced = canonical.Delivery.HasFlag(DeliveryFlags.Forced),
            IsAutohit = canonical.Delivery.HasFlag(DeliveryFlags.Autohit)
        };
    }

    private static CombatEventKind ToLegacyKind(CombatEventFamily family) =>
        family switch
        {
            CombatEventFamily.DamageDealt => CombatEventKind.DamageDealt,
            CombatEventFamily.DamageReceived => CombatEventKind.DamageReceived,
            CombatEventFamily.HealDealt => CombatEventKind.HealingDealt,
            CombatEventFamily.HealReceived => CombatEventKind.HealingReceived,
            CombatEventFamily.AttackResolution => CombatEventKind.AttackResolution,
            CombatEventFamily.Activation => CombatEventKind.PowerActivation,
            CombatEventFamily.Defeat => CombatEventKind.Defeat,
            _ => throw new InvalidOperationException($"No legacy CombatEvent mapping for {family}.")
        };

    private static CombatActorRole ToLegacyActorRole(ActorType type) =>
        type switch
        {
            ActorType.Self => CombatActorRole.Self,
            ActorType.OwnPet => CombatActorRole.OwnPet,
            _ => CombatActorRole.Other
        };

    private static string? ToLegacyTargetName(CanonicalCombatEvent canonical) =>
        canonical.Family switch
        {
            CombatEventFamily.DamageReceived or CombatEventFamily.HealReceived or CombatEventFamily.Activation
                => null,
            _ => canonical.Target?.DisplayName
        };

    private static string? ToLegacySourceName(CanonicalCombatEvent canonical) =>
        canonical.Actor.Type == ActorType.Self ? null : canonical.Actor.DisplayName;

    private static bool IsRolled(CanonicalCombatEvent canonical) =>
        canonical.Family == CombatEventFamily.AttackResolution
        && canonical.Outcome is not null
        && !canonical.Delivery.HasFlag(DeliveryFlags.Forced)
        && !canonical.Delivery.HasFlag(DeliveryFlags.Autohit);
}
