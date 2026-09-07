using CoHAnalytics.ReferenceData;

namespace CoHAnalytics.Tests.ReferenceData;

public sealed class BadgeRewardTextSupportTests
{
    [Fact]
    public void NormalizeAccoladeRewardPower_strips_awards_prefix()
    {
        Assert.Equal(
            "+Max HP, +Max Endurance (Labyrinth of Fog Only)",
            BadgeRewardTextSupport.NormalizeAccoladeRewardPower(
                "Awards +Max HP, +Max Endurance (Labyrinth of Fog Only)"));
    }

    [Fact]
    public void NormalizeAccoladeRewardPower_replaces_day_job_charge_placeholders()
    {
        Assert.Equal(
            "Smoke Bomb — Ranged (Target AoE), Foe -Perception, -ACC. Additional charges are earned while logged out in the appropriate Day Job location.",
            BadgeRewardTextSupport.NormalizeAccoladeRewardPower(
                "Ranged (Target AoE), Foe -Perception, -ACC; For every X hours logged out, a character gains 1 charge, up to a maximum of Z charges.",
                heroDescription: null,
                villainDescription: "While logged out in an Arachnos controlled area or inside a Vault, you will earn charges for your Smoke Bomb power."));
    }

    [Fact]
    public void NormalizeRequirementText_removes_internal_markdown_reference()
    {
        Assert.Equal(
            "Earn a source-defined set of 8 linked prerequisite badges",
            BadgeRewardTextSupport.NormalizeRequirementText(
                "Earn a source-defined set of 8 linked prerequisite badges; see `08 - Accolade Requirements.md` for names and logic warnings."));
    }

    [Fact]
    public void NormalizeAccoladeRewardPower_returns_null_for_unconfirmed_sentinel()
    {
        Assert.Null(BadgeRewardTextSupport.NormalizeAccoladeRewardPower(
            "No separate power or reward confirmed in the consulted page."));
    }

    [Fact]
    public void ResolveRewardText_uses_zone_exploration_accolade_merit_data()
    {
        var completionNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Atlas Tour Guide" };

        Assert.Equal(
            "5 Reward Merits",
            BadgeRewardTextSupport.ResolveRewardText(
                "AtlasParkExplorer",
                "Atlas Tour Guide",
                "Atlas Tour Guide",
                "No separate power or reward confirmed in the consulted page.",
                completionNames));
    }

    [Fact]
    public void ResolveRewardText_prefers_explicit_accolade_reward_over_merit_default()
    {
        var completionNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Conqueror of the Labyrinth" };

        Assert.Equal(
            "+Max HP, +Max Endurance (Labyrinth of Fog Only)",
            BadgeRewardTextSupport.ResolveRewardText(
                "LabyrinthAccolade",
                "Conqueror of the Labyrinth",
                "Conqueror of the Labyrinth",
                "Awards +Max HP, +Max Endurance (Labyrinth of Fog Only)",
                completionNames));
    }

    [Fact]
    public void ResolveRewardText_does_not_fabricate_reward_for_non_completion_badges()
    {
        Assert.Null(BadgeRewardTextSupport.ResolveRewardText(
            "AtlasParkTour1",
            "Undefeated",
            "Undefeated",
            null,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Atlas Tour Guide" }));
    }

    [Fact]
    public void ResolveRewardText_leaves_unresolved_completion_badges_without_merit_pattern()
    {
        var completionNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "The Constant" };

        Assert.Null(BadgeRewardTextSupport.ResolveRewardText(
            "Loyalty2011",
            "The Constant",
            "The Constant",
            "No separate power or reward confirmed in the consulted page.",
            completionNames));
    }
}
