using System.Globalization;
using CoHAnalytics.Models;
using CoHAnalytics.Services;

namespace CoHAnalytics.Tests.Services;

public sealed class PrimaryPerformancePresentationTests
{
    private static readonly DateTimeOffset SessionStart =
        new(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Beta_descriptor_resolves_only_to_damage_dps_and_damage_dealt()
    {
        var descriptor = PrimaryPerformanceMetric.BetaDamage;

        Assert.Equal(PrimaryPerformanceMetricKind.Damage, descriptor.Metric);
        Assert.Equal("DPS", descriptor.RateLabel);
        Assert.Equal("Damage Dealt", descriptor.TotalLabel);
    }

    [Fact]
    public void Session_row_uses_authoritative_domain_dps_and_total()
    {
        var elapsed = TimeSpan.FromSeconds(60);
        var damageDealt = new CombatScaledAmount(120_000 * CombatScaledAmount.Scale);
        var combat = new CombatSnapshot
        {
            DamageDealt = damageDealt,
            LastCombatAt = SessionStart.AddSeconds(30),
            SessionDamagePerSecondHundredths =
                CombatAggregator.CalculateSessionDamagePerSecondHundredths(damageDealt, elapsed)
        };

        var metric = PrimaryPerformancePresentation.BuildSession(combat, elapsed, hasCombatData: true);

        Assert.Equal(PrimaryPerformanceAvailabilityState.Available, metric.AvailabilityState);
        Assert.Equal("2K DPS", metric.RateValue);
        Assert.Equal("120K", metric.TotalValue);
        Assert.Equal(
            metric.RateValue,
            PrimaryPerformancePresentation.FormatDamagePerSecond(combat.SessionDamagePerSecondHundredths));
        Assert.Equal(
            metric.TotalValue,
            PrimaryPerformancePresentation.FormatCombatDamageTotal(combat.DamageDealt));
    }

    [Fact]
    public void Session_warm_up_shows_placeholder_dps_before_one_minute()
    {
        var elapsed = TimeSpan.FromSeconds(45);
        var combat = new CombatSnapshot
        {
            DamageDealt = new CombatScaledAmount(5_000 * CombatScaledAmount.Scale),
            LastCombatAt = SessionStart.AddSeconds(20),
            SessionDamagePerSecondHundredths = 0
        };

        var metric = PrimaryPerformancePresentation.BuildSession(combat, elapsed, hasCombatData: true);

        Assert.Equal(PrimaryPerformanceAvailabilityState.WarmUp, metric.AvailabilityState);
        Assert.Equal("—", metric.RateValue);
        Assert.Equal("5K", metric.TotalValue);
    }

    [Fact]
    public void Session_without_combat_data_uses_no_data_state()
    {
        var elapsed = TimeSpan.FromMinutes(5);
        var combat = CombatSnapshot.Empty;

        var metric = PrimaryPerformancePresentation.BuildSession(combat, elapsed, hasCombatData: false);

        Assert.Equal(PrimaryPerformanceAvailabilityState.NoData, metric.AvailabilityState);
        Assert.Equal("—", metric.RateValue);
        Assert.Equal("—", metric.TotalValue);
    }

    [Fact]
    public void Tracked_inactive_uses_idle_convention()
    {
        var metric = PrimaryPerformancePresentation.BuildTracked(TrackedCombatScopeSnapshot.Empty);

        Assert.Equal(PrimaryPerformanceAvailabilityState.Inactive, metric.AvailabilityState);
        Assert.Equal("—", metric.RateValue);
        Assert.Equal("—", metric.TotalValue);
    }

    [Fact]
    public void Tracked_running_reflects_domain_snapshot()
    {
        var tracked = new TrackedCombatScopeSnapshot
        {
            IsTracking = true,
            ActiveElapsed = TimeSpan.FromMinutes(2),
            DamageDealt = new CombatScaledAmount(750 * CombatScaledAmount.Scale),
            DamagePerSecondHundredths = 625
        };

        var metric = PrimaryPerformancePresentation.BuildTracked(tracked);

        Assert.Equal(PrimaryPerformanceAvailabilityState.Available, metric.AvailabilityState);
        Assert.Equal("6 DPS", metric.RateValue);
        Assert.Equal("750", metric.TotalValue);
    }

    [Theory]
    [InlineData(84_200, "842 DPS")]
    [InlineData(240_000, "2.4K DPS")]
    [InlineData(1_870_000, "18.7K DPS")]
    public void Dps_formatting_uses_compact_per_second_values(long dpsHundredths, string expected)
    {
        Assert.Equal(expected, PrimaryPerformancePresentation.FormatDamagePerSecond(dpsHundredths));
    }
}
