using CoHAnalytics.Models;
using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Services;

public sealed class GameplayTelemetryParserTests
{
    private readonly GameplayTelemetryParser _parser = new();
    private readonly ParserClassifier _classifier = new();

    [Fact]
    public void Badge_receipt_preserves_concrete_awarded_title()
    {
        Assert.True(TryParseLine("Congratulations! You earned the Defiler badge.", out var observation));
        Assert.Equal(GameplayTelemetryGrammarId.Oth02BadgeEarned, observation.GrammarId);
        Assert.Equal(GameplaySessionRewardCategory.Badge, observation.RewardCategory);
        Assert.Equal("Defiler", observation.RewardDisplayName);
    }

    [Fact]
    public void Untimestamped_xp01_parses_experience_and_influence()
    {
        var line = "You gain 1,234 experience and 567 influence.";
        Assert.True(TryParseLine(line, out var observation));
        Assert.Equal(GameplayTelemetryGrammarId.Xp01ExperienceAndInfluence, observation.GrammarId);
        Assert.Equal(1234, observation.ExperienceGained);
        Assert.Equal(567, observation.GameplayInfluenceGained);
    }

    [Fact]
    public void Untimestamped_xp03_parses_experience_only()
    {
        var line = "You gain 500 experience.";
        Assert.True(TryParseLine(line, out var observation));
        Assert.Equal(GameplayTelemetryGrammarId.Xp03ExperienceOnly, observation.GrammarId);
        Assert.Equal(500, observation.ExperienceGained);
    }

    [Fact]
    public void Full_timestamp_xp01_parses_experience_and_influence()
    {
        var line = "2026-08-07 03:57:00 You gain 1,234 experience and 567 influence.";
        Assert.True(TryParseLine(line, out var observation));
        Assert.Equal(1234, observation.ExperienceGained);
        Assert.Equal(567, observation.GameplayInfluenceGained);
    }

    [Fact]
    public void Bracket_timestamp_xp01_parses_experience_and_influence()
    {
        var line = "[03:57] You gain 1,234 experience and 567 influence.";
        Assert.True(TryParseLine(line, out var observation));
        Assert.Equal(1234, observation.ExperienceGained);
        Assert.Equal(567, observation.GameplayInfluenceGained);
    }

    [Fact]
    public void Bracket_timestamp_xp03_parses_experience_only()
    {
        var line = "[03:58] You gain 500 experience.";
        Assert.True(TryParseLine(line, out var observation));
        Assert.Equal(GameplayTelemetryGrammarId.Xp03ExperienceOnly, observation.GrammarId);
        Assert.Equal(500, observation.ExperienceGained);
        Assert.Equal(0, observation.GameplayInfluenceGained);
    }

    [Fact]
    public void Bracket_timestamp_inf01_parses_influence_only()
    {
        var line = "[03:59] You gain 250 influence.";
        Assert.True(TryParseLine(line, out var observation));
        Assert.Equal(GameplayTelemetryGrammarId.Inf01GameplayInfluenceOnly, observation.GrammarId);
        Assert.Equal(250, observation.GameplayInfluenceGained);
        Assert.Equal(0, observation.ExperienceGained);
    }

    [Fact]
    public void Bracket_timestamp_xp02_parses_experience_debt_and_influence()
    {
        var line = "[04:00] You gain 1,234 experience, work off 456 debt, and gain 789 influence.";
        Assert.True(TryParseLine(line, out var observation));
        Assert.Equal(GameplayTelemetryGrammarId.Xp02ExperienceDebtAndInfluence, observation.GrammarId);
        Assert.Equal(1234, observation.ExperienceGained);
        Assert.Equal(456, observation.DebtWorkedOff);
        Assert.Equal(789, observation.GameplayInfluenceGained);
    }

    [Fact]
    public void Malformed_bracket_timestamp_telemetry_line_is_rejected()
    {
        var line = "[99:99] You gain 500 experience.";
        Assert.False(TryParseLine(line, out _));
    }

    [Fact]
    public void Xp01_parses_experience_and_influence()
    {
        var line = "2026-08-01 12:34:56 You gain 1,234 experience and 567 influence.";
        Assert.True(TryParseLine(line, out var observation));
        Assert.Equal(GameplayTelemetryGrammarId.Xp01ExperienceAndInfluence, observation.GrammarId);
        Assert.Equal(1234, observation.ExperienceGained);
        Assert.Equal(567, observation.GameplayInfluenceGained);
        Assert.Equal(0, observation.DebtWorkedOff);
    }

    [Fact]
    public void Xp02_parses_experience_debt_and_influence()
    {
        var line = "2026-08-01 12:34:56 You gain 1,234 experience, work off 456 debt, and gain 789 influence.";
        Assert.True(TryParseLine(line, out var observation));
        Assert.Equal(GameplayTelemetryGrammarId.Xp02ExperienceDebtAndInfluence, observation.GrammarId);
        Assert.Equal(1234, observation.ExperienceGained);
        Assert.Equal(456, observation.DebtWorkedOff);
        Assert.Equal(789, observation.GameplayInfluenceGained);
    }

    [Fact]
    public void Xp03_parses_experience_only()
    {
        var line = "2026-08-01 12:34:56 You gain 875 experience.";
        Assert.True(TryParseLine(line, out var observation));
        Assert.Equal(GameplayTelemetryGrammarId.Xp03ExperienceOnly, observation.GrammarId);
        Assert.Equal(875, observation.ExperienceGained);
        Assert.Equal(0, observation.GameplayInfluenceGained);
    }

    [Fact]
    public void Xp04_parses_zero_experience_with_debt()
    {
        var line = "2026-08-01 12:34:56 You gain 0 experience and work off 125 debt.";
        Assert.True(TryParseLine(line, out var observation));
        Assert.Equal(GameplayTelemetryGrammarId.Xp04ExperienceAndDebt, observation.GrammarId);
        Assert.Equal(0, observation.ExperienceGained);
        Assert.Equal(125, observation.DebtWorkedOff);
        Assert.Equal(0, observation.GameplayInfluenceGained);
    }

    [Fact]
    public void Inf01_parses_gameplay_influence_only()
    {
        var line = "2026-08-01 12:34:56 You gain 42 influence.";
        Assert.True(TryParseLine(line, out var observation));
        Assert.Equal(GameplayTelemetryGrammarId.Inf01GameplayInfluenceOnly, observation.GrammarId);
        Assert.Equal(42, observation.GameplayInfluenceGained);
        Assert.Equal(0, observation.ExperienceGained);
    }

    [Fact]
    public void Large_comma_formatted_values_parse()
    {
        var line = "2026-08-01 12:34:56 You gain 176,784 experience and 12,345 influence.";
        Assert.True(TryParseLine(line, out var observation));
        Assert.Equal(176784, observation.ExperienceGained);
        Assert.Equal(12345, observation.GameplayInfluenceGained);
    }

    [Fact]
    public void Malformed_numeric_value_is_rejected()
    {
        var line = "2026-08-01 12:34:56 You gain 12,34 experience.";
        Assert.False(TryParseLine(line, out _));
    }

    [Fact]
    public void Combat_buff_you_gain_line_is_rejected()
    {
        var line = "2026-08-01 12:34:56 You gain 50 points of Endurance from Example Power.";
        Assert.False(TryParseLine(line, out _));
    }

    [Fact]
    public void Consignment_proceeds_line_is_rejected()
    {
        var line = "2026-08-01 12:34:56 You got 12,345 influence from the Consignment House.";
        Assert.False(TryParseLine(line, out _));
    }

    [Fact]
    public void Received_reward_merit_currency_parses_name_and_quantity()
    {
        var line = "2026-08-04 06:27:10 You received Reward Merit.";
        Assert.True(TryParseLine(line, out var observation));
        Assert.Equal(GameplayTelemetryGrammarId.Recv01GenericReceivedItem, observation.GrammarId);
        Assert.Equal(GameplaySessionRewardCategory.RewardCurrency, observation.RewardCategory);
        Assert.Equal("Reward Merit", observation.RewardDisplayName);
        Assert.Equal(1, observation.RewardQuantity);
    }

    [Fact]
    public void Received_units_of_enhancement_unslotter_parses_quantity()
    {
        var line = "2026-08-04 06:27:10 You received 10 units of Enhancement Unslotter.";
        Assert.True(TryParseLine(line, out var observation));
        Assert.Equal(GameplayTelemetryGrammarId.Recv01GenericReceivedItem, observation.GrammarId);
        Assert.Equal(GameplaySessionRewardCategory.RewardCurrency, observation.RewardCategory);
        Assert.Equal("Enhancement Unslotter", observation.RewardDisplayName);
        Assert.Equal(10, observation.RewardQuantity);
        Assert.Equal("10 units of Enhancement Unslotter", observation.ReceivedItemText);
    }

    [Fact]
    public void Received_reward_merits_quantity_parses_as_currency_not_received_item()
    {
        var line = "2026-08-04 06:27:10 You received 40 reward merits.";
        Assert.True(TryParseLine(line, out var observation));
        Assert.Equal(GameplayTelemetryGrammarId.Recv01GenericReceivedItem, observation.GrammarId);
        Assert.Equal(GameplaySessionRewardCategory.RewardCurrency, observation.RewardCategory);
        Assert.Equal("Reward Merit", observation.RewardDisplayName);
        Assert.Equal(40, observation.RewardQuantity);
        Assert.NotEqual(GameplaySessionRewardCategory.ReceivedItem, observation.RewardCategory);
    }

    [Fact]
    public void Received_units_of_currency_parses_quantity()
    {
        var line = "2026-08-04 06:27:10 You received 5 units of Enhancement Converter.";
        Assert.True(TryParseLine(line, out var observation));
        Assert.Equal(GameplaySessionRewardCategory.RewardCurrency, observation.RewardCategory);
        Assert.Equal("Enhancement Converter", observation.RewardDisplayName);
        Assert.Equal(5, observation.RewardQuantity);
    }

    [Fact]
    public void Received_nightmare_obol_units_parse_exact_observed_grammar()
    {
        var line = "[04:44] You received 15 units of Nightmare Obol.";

        Assert.True(TryParseLine(line, out var observation));
        Assert.Equal(GameplayTelemetryGrammarId.Recv01GenericReceivedItem, observation.GrammarId);
        Assert.Equal(GameplaySessionRewardCategory.RewardCurrency, observation.RewardCategory);
        Assert.Equal("Nightmare Obol", observation.RewardDisplayName);
        Assert.Equal(15, observation.RewardQuantity);
    }

    [Fact]
    public void Standalone_reward_merits_line_parses_currency()
    {
        var line = "2026-08-04 06:27:10 3 reward merits";
        Assert.True(TryParseLine(line, out var observation));
        Assert.Equal(GameplayTelemetryGrammarId.Cur01RewardMeritStandalone, observation.GrammarId);
        Assert.Equal("Reward Merit", observation.RewardDisplayName);
        Assert.Equal(3, observation.RewardQuantity);
    }

    [Fact]
    public void Generic_received_item_preserves_unknown_item_text()
    {
        var line = "2026-08-04 06:27:10 You received Impervium Armor.";
        Assert.True(TryParseLine(line, out var observation));
        Assert.Equal(GameplaySessionRewardCategory.ReceivedItem, observation.RewardCategory);
        Assert.Equal("Impervium Armor", observation.RewardDisplayName);
        Assert.Equal("Impervium Armor", observation.ReceivedItemText);
        Assert.Equal(1, observation.RewardQuantity);
    }

    [Fact]
    public void Untimestamped_received_item_parses()
    {
        var line = "You received Some Recipe.";
        Assert.True(TryParseLine(line, out var observation));
        Assert.Equal(GameplaySessionRewardCategory.ReceivedItem, observation.RewardCategory);
        Assert.Equal("Some Recipe", observation.RewardDisplayName);
    }

    [Fact]
    public void Bracket_timestamp_received_item_parses()
    {
        var line = "[04:15] You received Astral Merit.";
        Assert.True(TryParseLine(line, out var observation));
        Assert.Equal(GameplaySessionRewardCategory.RewardCurrency, observation.RewardCategory);
        Assert.Equal("Astral Merit", observation.RewardDisplayName);
    }

    [Fact]
    public void Architect_ticket_status_line_is_rejected()
    {
        var line = "2026-08-04 06:27:10 You can claim 5 tickets from Architect Entertainment.";
        Assert.False(TryParseLine(line, out _));
    }

    [Fact]
    public void Malformed_you_received_without_period_is_rejected()
    {
        var line = "2026-08-04 06:27:10 You received Reward Merit";
        Assert.False(TryParseLine(line, out _));
    }

    private bool TryParseUnknownLine(string line, out GameplayTelemetryObservation observation)
    {
        var classified = _classifier.Classify(new ParserRawEvent
        {
            ContextId = MonitoringContextId.CreateNew(),
            SourceId = LogSourceId.Create("acct-1", "acct-1", "C:\\fake\\chatlog.txt", new DateOnly(2026, 8, 4)),
            SourceSegmentId = ParserSourceSegmentId.CreateNew(),
            BindingGeneration = 1,
            SourceTransitionKind = MonitoringSourceTransitionKind.SourceAssigned,
            Sequence = 1,
            ObservedAt = DateTimeOffset.UtcNow,
            RawLine = line,
            SourceByteStart = 0,
            SourceByteEnd = line.Length,
            LineStatus = ParserLineStatus.Complete
        });

        Assert.Equal(ParserEventKind.Unknown, classified.EventKind);
        return _parser.TryParse(classified, out observation);
    }

    [Fact]
    public void Character_level_improvement_parses_observed_level()
    {
        var line = "Your combat improves to level 50! Seek a trainer to further your abilities.";
        Assert.True(TryParseLine(line, out var observation));
        Assert.Equal(GameplayTelemetryGrammarId.Chr01CharacterLevelImprovement, observation.GrammarId);
        Assert.Equal(50, observation.CharacterObservedLevel);
    }

    [Fact]
    public void Oth02_badge_award_line_parses_as_badge_reward()
    {
        var line = "Congratulations! You earned the Atlas Tour Guide badge.";
        Assert.True(TryParseLine(line, out var observation));
        Assert.Equal(GameplayTelemetryGrammarId.Oth02BadgeEarned, observation.GrammarId);
        Assert.Equal(GameplaySessionRewardCategory.Badge, observation.RewardCategory);
        Assert.Equal("Atlas Tour Guide", observation.RewardDisplayName);
    }

    [Fact]
    public void Combat_scaling_line_is_not_character_level_improvement()
    {
        var line = "You are now fighting at level 50.";
        Assert.False(TryParseLine(line, out _));
    }

    private bool TryParseLine(string line, out GameplayTelemetryObservation observation)
    {
        var classified = _classifier.Classify(new ParserRawEvent
        {
            ContextId = MonitoringContextId.CreateNew(),
            SourceId = LogSourceId.Create("acct-1", "acct-1", "C:\\fake\\chatlog.txt", new DateOnly(2026, 8, 1)),
            SourceSegmentId = ParserSourceSegmentId.CreateNew(),
            BindingGeneration = 1,
            SourceTransitionKind = MonitoringSourceTransitionKind.SourceAssigned,
            Sequence = 1,
            ObservedAt = DateTimeOffset.UtcNow,
            RawLine = line,
            SourceByteStart = 0,
            SourceByteEnd = line.Length,
            LineStatus = ParserLineStatus.Complete
        });

        return _parser.TryParse(classified, out observation);
    }
}
