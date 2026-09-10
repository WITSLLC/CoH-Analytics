using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Services;

public sealed class CombatEnemyScopePresentationTests
{
    [Fact]
    public void FormatMyDefeatsLabel_normal_share_uses_rounded_integer_percent()
    {
        Assert.Equal("42 (35%)", CombatEnemyScopePresentation.FormatMyDefeatsLabel(42, 120));
    }

    [Fact]
    public void FormatMyDefeatsLabel_zero_total_defeats_omits_percentage()
    {
        Assert.Equal("0", CombatEnemyScopePresentation.FormatMyDefeatsLabel(0, 0));
    }

    [Fact]
    public void FormatMyDefeatsLabel_all_personal_defeats_shows_one_hundred_percent()
    {
        Assert.Equal("5 (100%)", CombatEnemyScopePresentation.FormatMyDefeatsLabel(5, 5));
    }

    [Fact]
    public void FormatMyDefeatsLabel_no_personal_defeats_shows_zero_percent()
    {
        Assert.Equal("0 (0%)", CombatEnemyScopePresentation.FormatMyDefeatsLabel(0, 10));
    }

    [Fact]
    public void FormatMyDefeatsLabel_rounds_half_up_to_nearest_whole_percent()
    {
        Assert.Equal("17 (40%)", CombatEnemyScopePresentation.FormatMyDefeatsLabel(17, 42));
        Assert.Equal("63 (34%)", CombatEnemyScopePresentation.FormatMyDefeatsLabel(63, 184));
    }

    [Fact]
    public void Available_includes_share_in_my_defeats_label()
    {
        var presentation = CombatEnemyScopePresentation.Available(totalDefeated: 120, myDefeats: 42);

        Assert.Equal("120", presentation.TotalDefeatedLabel);
        Assert.Equal("42 (35%)", presentation.MyDefeatsLabel);
    }
}
