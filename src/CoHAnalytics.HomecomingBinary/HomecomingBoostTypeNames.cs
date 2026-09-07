namespace CoHAnalytics.HomecomingBinary;

internal static class HomecomingBoostTypeNames
{
    internal static readonly IReadOnlyList<string> OriginTypes =
    [
        "Science",
        "Mutation",
        "Magic",
        "Technology",
        "Natural"
    ];

    private static readonly IReadOnlyDictionary<uint, string> HomecomingMap = new Dictionary<uint, string>
    {
        [0] = "Science",
        [1] = "Mutation",
        [2] = "Magic",
        [3] = "Technology",
        [4] = "Natural",
        [5] = "Accuracy",
        [6] = "Buff_Defense",
        [7] = "Buff_ToHit",
        [8] = "Confuse",
        [9] = "Damage",
        [10] = "Debuff_Defense",
        [11] = "Debuff_ToHit",
        [12] = "Fear",
        [13] = "SpeedFlying",
        [14] = "Heal",
        [15] = "Immobilize",
        [16] = "Jump",
        [17] = "Knockback",
        [18] = "Recharge",
        [19] = "SpeedRunning",
        [20] = "Sleep",
        [21] = "Stun",
        [22] = "Range",
        [23] = "EnduranceDiscount",
        [24] = "Buff_Damage",
        [25] = "Debuff_Damage",
        [26] = "Radius",
        [27] = "Cone",
        [28] = "Taunt",
        [29] = "Slow",
        [30] = "Hold",
        [31] = "Intangible",
        [32] = "Interrupt",
        [33] = "Recovery",
        [34] = "Endurance_Drain",
        [35] = "Res_Damage",
        [36] = "Hamidon",
        [37] = "Incarnate_Judgement",
        [38] = "Incarnate_Interface",
        [39] = "Incarnate_Lore",
        [40] = "Incarnate_Destiny"
    };

    internal static string Resolve(uint value) =>
        HomecomingMap.TryGetValue(value, out var name)
            ? name
            : $"Unknown({value})";

    internal static IReadOnlyList<string> ResolveMany(IReadOnlyList<uint> values) =>
        values.Select(Resolve).ToArray();

    internal static bool IsOriginType(string boostTypeName) =>
        OriginTypes.Contains(boostTypeName, StringComparer.Ordinal);

    internal static IReadOnlyList<string> NonOriginTypes(IReadOnlyList<string> boostsAllowed) =>
        boostsAllowed
            .Where(value => !IsOriginType(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
}
