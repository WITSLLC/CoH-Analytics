using CoHAnalytics.Models;
using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Services;

public sealed class AnalyticsOverviewPresentationTests
{
    [Fact]
    public void No_character_and_no_history_are_distinct_states()
    {
        var character = CharacterRecordId.CreateNew();

        var noCharacter = AnalyticsOverviewPresentation.BuildNoCharacter();
        var noHistory = AnalyticsOverviewPresentation.Build(
            CharacterHistoricalPerformanceSnapshot.Empty(character));

        Assert.True(noCharacter.ShowNoCharacter);
        Assert.False(noCharacter.ShowNoHistory);
        Assert.True(noHistory.ShowNoHistory);
        Assert.False(noHistory.ShowNoCharacter);
        Assert.Equal("—", noHistory.HistoricalDpsLabel);
        Assert.Equal("—", noHistory.ExperiencePerHourLabel);
    }

    [Fact]
    public void Historical_snapshot_formats_all_three_cards_and_coverage()
    {
        var snapshot = new CharacterHistoricalPerformanceSnapshot
        {
            CharacterRecordId = CharacterRecordId.CreateNew(),
            ObservationCount = 3,
            ObservedDuration = TimeSpan.FromHours(2),
            DamageDealt = new CombatScaledAmount(1_200_000),
            DamagePerSecondHundredths = 167,
            Accuracy = new CombatAccuracyScopeSnapshot
            {
                Attempts = 100,
                Hits = 55,
                Misses = 45,
                RolledAttempts = 80,
                DisplayedChanceSumHundredths = 480_000,
                RollSumHundredths = 400_000,
                ForcedHits = 4,
                Autohits = 7
            },
            HitPercent = 55.0m,
            AverageDisplayedChance = 60.0m,
            AverageRoll = 50.0m,
            TotalDefeated = 30,
            MyDefeats = 24,
            ExperienceGained = 12_000,
            ExperiencePerHour = 6_000,
            GameplayInfluenceGained = 4_000,
            GameplayInfluencePerHour = 2_000
        };

        var overview = AnalyticsOverviewPresentation.Build(snapshot);

        Assert.True(overview.ShowHistoricalMetrics);
        Assert.Equal(PrimaryPerformancePresentation.FormatDamagePerSecond(167), overview.HistoricalDpsLabel);
        Assert.Equal("12K", overview.TotalDamageDealtLabel);
        Assert.Equal("30", overview.TotalDefeatedLabel);
        Assert.Equal("24", overview.MyDefeatsLabel);
        Assert.Equal("55.0%", overview.HitPercentLabel);
        Assert.Equal("100", overview.AttemptsLabel);
        Assert.Equal("55", overview.HitsLabel);
        Assert.Equal("45", overview.MissesLabel);
        Assert.Equal("60.0%", overview.AverageChanceLabel);
        Assert.Equal("50.0", overview.AverageRollLabel);
        Assert.Equal("4", overview.ForcedHitsLabel);
        Assert.Equal("7", overview.AutohitsLabel);
        Assert.Equal(GameplaySessionTelemetryPresentation.FormatCompactRate(6_000), overview.ExperiencePerHourLabel);
        Assert.Equal("12,000", overview.TotalExperienceLabel);
        Assert.Equal(GameplaySessionTelemetryPresentation.FormatCompactRate(2_000), overview.InfluencePerHourLabel);
        Assert.Equal("4,000", overview.TotalGameplayInfluenceLabel);
        Assert.Equal("3", overview.ObservationCountLabel);
        Assert.Equal("02:00:00", overview.ObservedDurationLabel);
    }

    [Fact]
    public void Valid_zero_activity_history_shows_measured_zero_and_unobserved_accuracy()
    {
        var overview = AnalyticsOverviewPresentation.Build(
            new CharacterHistoricalPerformanceSnapshot
            {
                CharacterRecordId = CharacterRecordId.CreateNew(),
                ObservationCount = 1,
                ObservedDuration = TimeSpan.FromMinutes(30),
                DamagePerSecondHundredths = 0,
                ExperiencePerHour = 0,
                GameplayInfluencePerHour = 0
            });

        Assert.True(overview.ShowHistoricalMetrics);
        Assert.Equal("0 DPS", overview.HistoricalDpsLabel);
        Assert.Equal("—", overview.HitPercentLabel);
        Assert.Equal("0/hr", overview.ExperiencePerHourLabel);
        Assert.Equal("0", overview.TotalExperienceLabel);
        Assert.Equal("0/hr", overview.InfluencePerHourLabel);
        Assert.Equal("0", overview.TotalGameplayInfluenceLabel);
    }

    [Fact]
    public void Coverage_duration_retains_days_instead_of_wrapping_at_twenty_four_hours()
    {
        Assert.Equal(
            "2d 03:04:05",
            AnalyticsOverviewPresentation.FormatObservedDuration(
                TimeSpan.FromDays(2) + TimeSpan.FromHours(3) + TimeSpan.FromMinutes(4) + TimeSpan.FromSeconds(5)));
    }
}
