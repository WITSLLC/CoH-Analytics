namespace CoHAnalytics.Homecoming;

internal static class EnhancementPogIdentity
{
    private static readonly IReadOnlyDictionary<string, string> BoostTypeToPogIdentity =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Accuracy"] = "E_POG_ACCURACY",
            ["Buff_Defense"] = "e_pog_buff_defense",
            ["Buff_ToHit"] = "E_POG_BUFF_TO_HIT",
            ["Confuse"] = "E_POG_CONFUSION_DURATION",
            ["Damage"] = "E_POG_DAMAGE",
            ["Debuff_Defense"] = "E_POG_DEBUFF_DEFENSE",
            ["Debuff_ToHit"] = "E_POG_DEBUFF_TO_HIT",
            ["Fear"] = "E_POG_FEAR_DURATION",
            ["SpeedFlying"] = "E_POG_FLY_SPEED",
            ["Heal"] = "E_POG_HEAL",
            ["Immobilize"] = "E_POG_IMMOBILIZATION_DURATION",
            ["Jump"] = "E_POG_JUMP_DISTANCE",
            ["Knockback"] = "E_POG_KNOCKBACK_DISTANCE",
            ["Recharge"] = "E_POG_RECHARGE_TIME",
            ["SpeedRunning"] = "E_POG_RUN_SPEED",
            ["Sleep"] = "E_POG_SLEEP_DURATION",
            ["Stun"] = "E_POG_STUN_DURATION",
            ["Range"] = "E_POG_RANGE_INCREASE",
            ["EnduranceDiscount"] = "E_POG_END_DISCOUNT",
            ["Buff_Damage"] = "E_POG_BUFF_DAMAGE",
            ["Debuff_Damage"] = "E_POG_DEBUFF_DAMAGE",
            ["Radius"] = "E_POG_RADIUS",
            ["Cone"] = "E_POG_CONE_RANGE",
            ["Taunt"] = "E_POG_TAUNT_DURATION",
            ["Slow"] = "E_POG_SLOW_MOVEMENT",
            ["Hold"] = "E_POG_HOLD_DURATION",
            ["Intangible"] = "E_POG_INTAGIBILITY_DURATION",
            ["Interrupt"] = "E_POG_INTERRUPT_TIMES",
            ["Recovery"] = "E_POG_RECOVERY",
            ["Endurance_Drain"] = "E_POG_END_DRAIN",
            ["Res_Damage"] = "E_POG_DAMAGE_RESIST"
        };

    internal static string? TryResolveIdentity(string? pogBoostType)
    {
        if (string.IsNullOrWhiteSpace(pogBoostType))
        {
            return null;
        }

        return BoostTypeToPogIdentity.TryGetValue(pogBoostType.Trim(), out var identity)
            ? identity
            : null;
    }
}
