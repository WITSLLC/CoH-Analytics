namespace CoHAnalytics.ReferenceDataGenerator;

/// <summary>
/// Analytics-owned UI presentation labels for Homecoming structural keys.
/// Labels are presentation metadata only and never replace canonical identity.
/// </summary>
internal static class HomecomingPresentationLabels
{
    private const string UnresolvedToHitDebuffCategoryCode = "ECToHitDeBuff";
    private const string ToHitDebuffPresentation = "To-Hit Debuff";

    private static readonly IReadOnlyDictionary<string, string> CommonIoBoostTypeLabels =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Accuracy"] = "Accuracy",
            ["Buff_Damage"] = "Damage Buff",
            ["Buff_Defense"] = "Defense Buff",
            ["Buff_ToHit"] = "To-Hit Buff",
            ["Confuse"] = "Confuse",
            ["Damage"] = "Damage",
            ["Debuff_Damage"] = "Damage Debuff",
            ["Debuff_Defense"] = "Defense Debuff",
            ["Debuff_ToHit"] = "To-Hit Debuff",
            ["EnduranceDiscount"] = "Endurance Reduction",
            ["Endurance_Drain"] = "Endurance Drain",
            ["Fear"] = "Fear",
            ["Heal"] = "Healing",
            ["Hold"] = "Hold",
            ["Immobilize"] = "Immobilize",
            ["Intangible"] = "Intangible",
            ["Interrupt"] = "Interrupt",
            ["Jump"] = "Jump",
            ["Knockback"] = "Knockback",
            ["Range"] = "Range",
            ["Recharge"] = "Recharge Reduction",
            ["Recovery"] = "Recovery",
            ["Res_Damage"] = "Damage Resistance",
            ["Sleep"] = "Sleep",
            ["Slow"] = "Slow",
            ["SpeedFlying"] = "Flight Speed",
            ["SpeedRunning"] = "Run Speed",
            ["Stun"] = "Stun",
            ["Taunt"] = "Threat"
        };

    internal static string? TryGetCommonIoBoostTypeDisplayText(string structuralKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(structuralKey);
        return CommonIoBoostTypeLabels.TryGetValue(structuralKey, out var label)
            ? label
            : null;
    }

    internal static string ResolveCategoryDisplayText(
        string categoryCode,
        string? messageStoreText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(categoryCode);
        if (string.Equals(categoryCode, UnresolvedToHitDebuffCategoryCode, StringComparison.Ordinal))
        {
            return ToHitDebuffPresentation;
        }

        if (string.IsNullOrWhiteSpace(messageStoreText))
        {
            return categoryCode;
        }

        const string categoryPrefix = "Category: ";
        return messageStoreText.StartsWith(categoryPrefix, StringComparison.Ordinal)
            ? messageStoreText[categoryPrefix.Length..]
            : messageStoreText;
    }

    internal static string? ResolveRarityDisplayText(string? messageStoreText)
    {
        if (string.IsNullOrWhiteSpace(messageStoreText))
        {
            return null;
        }

        const string rarityPrefix = "Rarity: ";
        return messageStoreText.StartsWith(rarityPrefix, StringComparison.Ordinal)
            ? messageStoreText[rarityPrefix.Length..]
            : messageStoreText;
    }
}
