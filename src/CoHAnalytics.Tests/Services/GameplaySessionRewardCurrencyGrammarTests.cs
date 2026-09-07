using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Services;

public sealed class GameplaySessionRewardCurrencyGrammarTests
{
    [Fact]
    public void Units_of_enhancement_unslotter_parses_quantity_and_name()
    {
        Assert.True(GameplayRewardCurrencyGrammar.TryParseReceivedItemText(
            "10 units of Enhancement Unslotter",
            out var name,
            out var quantity));
        Assert.Equal("Enhancement Unslotter", name);
        Assert.Equal(10, quantity);
    }

    [Fact]
    public void Received_enhancement_unslotter_without_units_parses_as_one()
    {
        Assert.True(GameplayRewardCurrencyGrammar.TryParseReceivedItemText(
            "Enhancement Unslotter",
            out var name,
            out var quantity));
        Assert.Equal("Enhancement Unslotter", name);
        Assert.Equal(1, quantity);
    }

    [Fact]
    public void Received_reward_merits_quantity_parses_from_received_item_text()
    {
        Assert.True(GameplayRewardCurrencyGrammar.TryParseReceivedItemText(
            "40 reward merits",
            out var name,
            out var quantity));
        Assert.Equal("Reward Merit", name);
        Assert.Equal(40, quantity);
    }

    [Fact]
    public void Received_reward_merits_quantity_parses_with_terminal_period()
    {
        Assert.True(GameplayRewardCurrencyGrammar.TryParseReceivedItemText(
            "40 reward merits.",
            out var name,
            out var quantity));
        Assert.Equal("Reward Merit", name);
        Assert.Equal(40, quantity);
    }

    [Fact]
    public void Standalone_reward_merits_body_still_parses()
    {
        Assert.True(GameplayRewardCurrencyGrammar.TryParseStandaloneMeritBody(
            "3 reward merits",
            out var name,
            out var quantity));
        Assert.Equal("Reward Merit", name);
        Assert.Equal(3, quantity);
    }

    [Fact]
    public void Units_of_reward_merit_still_parses()
    {
        Assert.True(GameplayRewardCurrencyGrammar.TryParseReceivedItemText(
            "2 units of Reward Merit",
            out var name,
            out var quantity));
        Assert.Equal("Reward Merit", name);
        Assert.Equal(2, quantity);
    }
}
