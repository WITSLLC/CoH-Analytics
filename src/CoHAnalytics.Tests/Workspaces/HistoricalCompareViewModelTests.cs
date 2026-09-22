using CoHAnalytics.Models;
using CoHAnalytics.Services;
using CoHAnalytics.ViewModels.Workspaces;

namespace CoHAnalytics.Tests.Workspaces;

public sealed class HistoricalCompareViewModelTests
{
    [Fact]
    public void Categories_are_exactly_the_five_compare_tabs()
    {
        var vm = new HistoricalCompareViewModel(null, null, null, new AccountAnonymityService());
        Assert.Equal(
            new[] { "Key Metrics", "Damage", "Healing", "Damage Taken", "Power Usage" },
            vm.Categories.Select(c => c.Label));
        Assert.DoesNotContain(vm.Categories, c => c.Label is "Targets" or "Misc" or "Pets");
        Assert.Equal(CompareCategoryId.KeyMetrics, vm.SelectedCategory);
        vm.SelectCategoryCommand.Execute(CompareCategoryId.Healing);
        Assert.True(vm.IsHealingSelected);
        Assert.Equal("Healing", Assert.Single(vm.Categories, c => c.IsActive).Label);
    }

    [Fact]
    public void Side_selectors_are_independent_across_accounts_characters_and_segments()
    {
        using var f = new HistoricalCombatViewModelTests.Fixture();
        var vm = new HistoricalCompareViewModel(f.Reader, f.Characters, null, new AccountAnonymityService());
        vm.Refresh();
        Assert.Equal("RivenForest", vm.SideA.SelectedAccount!.Id);
        Assert.Equal("RivenForest", vm.SideB.SelectedAccount!.Id);
        var bAccount = vm.SideB.SelectedAccount!.Id;
        var bCharacter = vm.SideB.SelectedCharacter!.Id;
        var bSegment = vm.SideB.SelectedSegment!.Header.SegmentId;

        vm.SideA.SelectedAccount = vm.SideA.AccountsChoices.Single(a => a.Id == "Adelbert");
        Assert.Equal("Adelbert", vm.SideA.SelectedAccount!.Id);
        Assert.Equal(bAccount, vm.SideB.SelectedAccount!.Id);
        Assert.Equal(bCharacter, vm.SideB.SelectedCharacter!.Id);
        Assert.Equal(bSegment, vm.SideB.SelectedSegment!.Header.SegmentId);
        Assert.All(vm.SideA.CharacterChoices, c => Assert.Equal("Adelbert", c.AccountId));
        Assert.Equal(f.Adelbert, vm.SideA.SelectedCharacter!.Id);

        var aAccount = vm.SideA.SelectedAccount!.Id;
        var aCharacter = vm.SideA.SelectedCharacter!.Id;
        var aSegment = vm.SideA.SelectedSegment!.Header.SegmentId;
        vm.SideB.SelectedAccount = vm.SideB.AccountsChoices.Single(a => a.Id == "Adelbert");
        Assert.Equal(aAccount, vm.SideA.SelectedAccount!.Id);
        Assert.Equal(aCharacter, vm.SideA.SelectedCharacter!.Id);
        Assert.Equal(aSegment, vm.SideA.SelectedSegment!.Header.SegmentId);

        vm.SideA.SelectedCharacter = vm.SideA.CharacterChoices.Single(c => c.Id == f.EmptyCharacter);
        Assert.Equal(f.Adelbert, vm.SideB.SelectedCharacter!.Id);
        vm.SideB.SelectedCharacter = vm.SideB.CharacterChoices.Single(c => c.Id == f.EmptyCharacter);
        Assert.Equal(f.EmptyCharacter, vm.SideA.SelectedCharacter!.Id);

        vm.SideA.SelectedAccount = vm.SideA.AccountsChoices.Single(a => a.Id == "Adelbert");
        vm.SideA.SelectedCharacter = vm.SideA.CharacterChoices.Single(c => c.Id == f.Adelbert);
        vm.SideB.SelectedAccount = vm.SideB.AccountsChoices.Single(a => a.Id == "RivenForest");
        Assert.Equal("Hell's Vengence", vm.SideA.SelectedCharacter!.Label);
        Assert.Equal("Hell's Vengence", vm.SideB.SelectedCharacter!.Label);
        Assert.NotEqual(vm.SideA.SelectedCharacter.Id, vm.SideB.SelectedCharacter.Id);
        Assert.NotEqual(vm.SideA.SelectedAccount!.Id, vm.SideB.SelectedAccount!.Id);

        var keptB = vm.SideB.SelectedSegment!.Header.SegmentId;
        vm.SideA.SelectedSegment = vm.SideA.SegmentChoices.Last();
        Assert.Equal(keptB, vm.SideB.SelectedSegment!.Header.SegmentId);
        var keptA = vm.SideA.SelectedSegment!.Header.SegmentId;
        vm.SideB.SelectedSegment = vm.SideB.SegmentChoices.First();
        Assert.Equal(keptA, vm.SideA.SelectedSegment!.Header.SegmentId);
    }

    [Fact]
    public void Missing_is_em_dash_and_authoritative_zero_remains_zero()
    {
        var vm = new HistoricalCompareViewModel(null, null, null, new AccountAnonymityService());
        vm.ApplyProjections(
            View(Metrics(damage: Amount(125_000), taken: Metric<CombatScaledAmount>.NotCaptured())),
            View(Metrics(damage: Amount(0), taken: Amount(0))));

        var total = Row(vm, "Total Damage");
        Assert.Equal("125,000.00", total.SideA);
        Assert.Equal("0.00", total.SideB);
        Assert.Equal("A +125,000.00", total.Difference);
        Assert.Equal("—", total.Percent);
        Assert.Equal(CompareTrend.Higher, total.Trend);
        Assert.Equal("Brush.Accent", total.TrendBrushKey);

        var taken = Row(vm, "Damage Taken");
        Assert.Equal("—", taken.SideA);
        Assert.Equal("0.00", taken.SideB);
        Assert.Equal("—", taken.Difference);
        Assert.Equal("—", taken.Percent);
        Assert.Equal("Brush.TextMuted", taken.TrendBrushKey);
    }

    [Fact]
    public void Asymmetric_power_rows_keep_the_missing_side_visible()
    {
        var vm = new HistoricalCompareViewModel(null, null, null, new AccountAnonymityService());
        vm.ApplyProjections(
            View(CombatAnalyticsProjection.Empty with
            {
                Powers = [Outgoing("Burn", CombatAnalyticsScope.Self, 5001)]
            }),
            View(CombatAnalyticsProjection.Empty with
            {
                Powers =
                [
                    Outgoing("Burn", CombatAnalyticsScope.OwnPetsAggregate, 7344),
                    Outgoing("Fire Ball", CombatAnalyticsScope.Self, 2000)
                ]
            }));
        vm.SelectCategoryCommand.Execute(CompareCategoryId.Damage);
        Assert.Contains(vm.CurrentRows, r => r.Label.Contains("Burn (Player)", StringComparison.Ordinal) && r.SideB == "—");
        Assert.Contains(vm.CurrentRows, r => r.Label.Contains("Burn (Owned pets)", StringComparison.Ordinal) && r.SideA == "—");
        Assert.Contains(vm.CurrentRows, r => r.Label.Contains("Fire Ball (Player)", StringComparison.Ordinal) && r.SideA == "—");
        Assert.DoesNotContain(vm.CurrentRows, r => r.SideA != "—" && r.SideB != "—" && r.Label.Contains("Burn (Player)", StringComparison.Ordinal) && r.Label.Contains("Owned pets", StringComparison.Ordinal));
    }

    [Fact]
    public void Key_metrics_use_supported_session_fields_and_omit_old_mockup_metrics()
    {
        var vm = new HistoricalCompareViewModel(null, null, null, new AccountAnonymityService());
        vm.ApplyProjections(
            View(Metrics(
                damage: Amount(85_200),
                player: Amount(58_320),
                pets: Amount(12_480),
                taken: Amount(18_300),
                healing: Amount(5_630),
                endurance: Amount(4_210),
                proc: Amount(14_400),
                duration: TimeSpan.FromSeconds(2_517),
                dps: 3_390)),
            View(Metrics(
                damage: Amount(72_100),
                player: Amount(50_440),
                pets: Amount(8_910),
                taken: Amount(24_900),
                healing: Amount(3_310),
                endurance: Amount(2_870),
                proc: Amount(12_750),
                duration: TimeSpan.FromSeconds(2_334),
                dps: 3_090)));

        Assert.Equal(
            new[]
            {
                "Total Damage", "Player Damage", "Pet Damage", "Proc Damage", "Damage Taken",
                "Healing Dealt", "Endurance Granted", "Session Duration", "DPS (Total)"
            },
            vm.CurrentRows.Select(r => r.Label));
        foreach (var forbidden in new[]
                 {
                     "Healing Received", "HPS Received", "Interrupts", "Team Resurrections",
                     "Number of Targets", "HPS", "Overheal"
                 })
        {
            Assert.DoesNotContain(vm.CurrentRows, r => r.Label.Contains(forbidden, StringComparison.Ordinal));
        }

        var total = Row(vm, "Total Damage");
        Assert.Equal("85,200.00", total.SideA);
        Assert.Equal("72,100.00", total.SideB);
        Assert.Equal("A +13,100.00", total.Difference);
        Assert.Equal("A +18.2%", total.Percent);
        Assert.Equal("Brush.Accent", total.TrendBrushKey);
        var taken = Row(vm, "Damage Taken");
        Assert.Equal("B +6,600.00", taken.Difference);
        Assert.Equal("B +26.5%", taken.Percent);
        Assert.Equal("Brush.InspirationResistance", taken.TrendBrushKey);
        Assert.DoesNotContain(vm.LargestDifferences, d =>
            d.Detail.Contains("better", StringComparison.OrdinalIgnoreCase)
            || d.Detail.Contains("worse", StringComparison.OrdinalIgnoreCase)
            || d.Detail.Contains("winner", StringComparison.OrdinalIgnoreCase)
            || d.Detail.Contains("stronger", StringComparison.OrdinalIgnoreCase)
            || d.Detail.Contains("weaker", StringComparison.OrdinalIgnoreCase)
            || d.Detail.Contains("improved", StringComparison.OrdinalIgnoreCase)
            || d.Detail.Contains("regressed", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(vm.LargestDifferences, d => d.Label == "Total Damage" && d.Detail == "Segment A recorded more.");
        Assert.Contains(vm.LargestDifferences, d => d.Label == "Damage Taken" && d.Detail == "Segment A recorded less.");
        Assert.Contains(vm.LargestDifferences, d => d.Label == "Total Damage" && d.Difference.StartsWith("A +", StringComparison.Ordinal));
        Assert.Contains(vm.LargestDifferences, d => d.Label == "Damage Taken" && d.Difference.StartsWith("B +", StringComparison.Ordinal));
        Assert.DoesNotContain(vm.LargestDifferences, d => d.Label == "Session Duration");
        Assert.All(vm.LargestDifferences, d =>
            Assert.True(d.Detail is "Segment A recorded more." or "Segment A recorded less."));
        Assert.All(vm.CurrentRows, r =>
            Assert.False(
                r.Difference.Contains("better", StringComparison.OrdinalIgnoreCase)
                || r.Difference.Contains("worse", StringComparison.OrdinalIgnoreCase)
                || r.Difference.Contains("winner", StringComparison.OrdinalIgnoreCase)
                || r.Percent.Contains("better", StringComparison.OrdinalIgnoreCase)
                || r.Percent.Contains("worse", StringComparison.OrdinalIgnoreCase)
                || r.Percent.Contains("winner", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void Difference_labels_the_numerically_higher_side()
    {
        var vm = new HistoricalCompareViewModel(null, null, null, new AccountAnonymityService());
        vm.ApplyProjections(
            View(Metrics(damage: Amount(100), taken: Amount(50), duration: TimeSpan.FromSeconds(60), dps: 200)),
            View(Metrics(damage: Amount(80), taken: Amount(50), duration: TimeSpan.FromSeconds(60), dps: 200)));

        var total = Row(vm, "Total Damage");
        Assert.Equal("A +20.00", total.Difference);
        Assert.Equal("A +25.0%", total.Percent);
        Assert.Equal(CompareTrend.Higher, total.Trend);
        Assert.Equal("Brush.Accent", total.TrendBrushKey);
        Assert.All(vm.LargestDifferences, d => Assert.Equal("Brush.Accent", d.TrendBrushKey));

        vm.ApplyProjections(
            View(Metrics(damage: Amount(80))),
            View(Metrics(damage: Amount(100))));
        total = Row(vm, "Total Damage");
        Assert.Equal("B +20.00", total.Difference);
        Assert.Equal("B +20.0%", total.Percent);
        Assert.Equal(CompareTrend.Lower, total.Trend);
        Assert.Equal("Brush.InspirationResistance", total.TrendBrushKey);
        Assert.Equal("Brush.InspirationResistance", Assert.Single(vm.LargestDifferences, d => d.Label == "Total Damage").TrendBrushKey);

        vm.ApplyProjections(
            View(Metrics(damage: Amount(100), taken: Amount(40))),
            View(Metrics(damage: Amount(100), taken: Amount(40))));
        total = Row(vm, "Total Damage");
        Assert.Equal("Equal", total.Difference);
        Assert.Equal("Equal", total.Percent);
        Assert.Equal("Brush.TextPrimary", total.TrendBrushKey);
        Assert.Equal("Equal", Row(vm, "Damage Taken").Difference);
        Assert.Equal("Equal", Row(vm, "Damage Taken").Percent);
        Assert.Equal("Brush.TextPrimary", Row(vm, "Damage Taken").TrendBrushKey);

        vm.ApplyProjections(
            View(Metrics(damage: Amount(125_000))),
            View(Metrics(damage: Amount(0))));
        total = Row(vm, "Total Damage");
        Assert.Equal("A +125,000.00", total.Difference);
        Assert.Equal("—", total.Percent);
        Assert.Equal("Brush.Accent", total.TrendBrushKey);
    }

    [Fact]
    public void Uncaptured_pet_damage_stays_missing_against_captured_pet_damage()
    {
        var vm = new HistoricalCompareViewModel(null, null, null, new AccountAnonymityService());
        vm.ApplyProjections(
            View(Metrics(damage: Amount(267_874.46m), player: Amount(267_874.46m))),
            View(Metrics(damage: Amount(75_972.14m), pets: Amount(75_972.14m))));

        var pet = Row(vm, "Pet Damage");
        Assert.Equal("—", pet.SideA);
        Assert.Equal("75,972.14", pet.SideB);
        Assert.Equal("—", pet.Difference);
        Assert.Equal("—", pet.Percent);
        Assert.Equal(CompareTrend.None, pet.Trend);
        Assert.Equal("Brush.TextMuted", pet.TrendBrushKey);
    }

    [Fact]
    public void Session_duration_keeps_absolute_difference_and_suppresses_percent()
    {
        var vm = new HistoricalCompareViewModel(null, null, null, new AccountAnonymityService());
        vm.ApplyProjections(
            View(Metrics(duration: TimeSpan.FromSeconds(2_517))),
            View(Metrics(duration: TimeSpan.FromSeconds(2_334))));

        var duration = Row(vm, "Session Duration");
        Assert.Equal("41:57", duration.SideA);
        Assert.Equal("38:54", duration.SideB);
        Assert.Equal("A +3:03", duration.Difference);
        Assert.Equal("—", duration.Percent);

        vm.ApplyProjections(
            View(Metrics(duration: TimeSpan.FromSeconds(60))),
            View(Metrics(duration: TimeSpan.FromSeconds(90))));
        duration = Row(vm, "Session Duration");
        Assert.Equal("B +0:30", duration.Difference);
        Assert.Equal("—", duration.Percent);

        vm.ApplyProjections(
            View(Metrics(duration: TimeSpan.FromSeconds(60))),
            View(Metrics(duration: TimeSpan.FromSeconds(60))));
        duration = Row(vm, "Session Duration");
        Assert.Equal("Equal", duration.Difference);
        Assert.Equal("—", duration.Percent);
    }

    [Fact]
    public void Healing_compares_player_originated_support_only()
    {
        var vm = new HistoricalCompareViewModel(null, null, null, new AccountAnonymityService());
        vm.ApplyProjections(
            View(CombatAnalyticsProjection.Empty with
            {
                Session = CombatSessionSummary.Empty with
                {
                    Metrics = CombatSessionMetricSet.Empty with
                    {
                        HealingDealt = Amount(9_999),
                        HealingReceived = Amount(800)
                    }
                },
                Powers =
                [
                    Heal("Transfusion"),
                    Heal("Siphon Life") with { Scope = CombatAnalyticsScope.OwnPetsAggregate },
                    Heal("Teammate Heal") with { Direction = CombatAnalyticsDirection.Incoming }
                ]
            }),
            View(CombatAnalyticsProjection.Empty with
            {
                Powers = [Heal("Transfusion") with { HealingMagnitudeMetric = Metric<CombatScaledAmount>.Available(new(400)) }]
            }));
        vm.SelectCategoryCommand.Execute(CompareCategoryId.Healing);
        Assert.Contains(vm.CurrentRows, r => r.Label.Contains("Transfusion (Player)", StringComparison.Ordinal));
        Assert.DoesNotContain(vm.CurrentRows, r => r.Label.Contains("Siphon Life", StringComparison.Ordinal));
        Assert.DoesNotContain(vm.CurrentRows, r => r.Label.Contains("Teammate Heal", StringComparison.Ordinal));
        Assert.DoesNotContain(vm.CurrentRows, r => r.Label == "Healing Dealt");
        Assert.Equal("50.01", RowContains(vm, "Transfusion").SideA);
        Assert.Equal("4.00", RowContains(vm, "Transfusion").SideB);
    }

    [Fact]
    public void Damage_taken_and_power_usage_use_authoritative_rows_only()
    {
        var vm = new HistoricalCompareViewModel(null, null, null, new AccountAnonymityService());
        vm.ApplyProjections(
            View(CombatAnalyticsProjection.Empty with
            {
                Session = CombatSessionSummary.Empty with
                {
                    Metrics = CombatSessionMetricSet.Empty with
                    {
                        ActivationCount = Metric<long>.Available(12),
                        AttackResolutionCount = Metric<long>.Available(10),
                        ConfirmedRechargeCompletedCount = Metric<long>.Available(4)
                    }
                },
                Powers =
                [
                    Outgoing("Burn", CombatAnalyticsScope.Self, 5001) with { ActivationCount = 6, EventCount = 8, LargestHit = new(1234) },
                    Incoming("Bone Shard", 2215)
                ],
                IncomingDamageTypeBreakdown = MetricRef<IReadOnlyList<CombatDamageTypeTotal>>.Available(
                    [new() { DamageType = new("Lethal"), Amount = new(2215), EventCount = 2 }]),
                IncomingDamageTypes = [new() { DamageType = new("Lethal"), Amount = new(2215), EventCount = 2 }]
            }),
            View(CombatAnalyticsProjection.Empty with
            {
                Session = CombatSessionSummary.Empty with
                {
                    Metrics = CombatSessionMetricSet.Empty with
                    {
                        ActivationCount = Metric<long>.Available(7),
                        AttackResolutionCount = Metric<long>.Available(7),
                        ConfirmedRechargeCompletedCount = Metric<long>.Available(1)
                    }
                },
                Powers = [Incoming("Bone Shard", 400)],
                IncomingDamageTypeBreakdown = MetricRef<IReadOnlyList<CombatDamageTypeTotal>>.Available(
                    [new() { DamageType = new("Lethal"), Amount = new(400), EventCount = 1 }]),
                IncomingDamageTypes = [new() { DamageType = new("Lethal"), Amount = new(400), EventCount = 1 }]
            }));

        vm.SelectCategoryCommand.Execute(CompareCategoryId.DamageTaken);
        Assert.Contains(vm.CurrentRows, r => r.Label.Contains("Bone Shard", StringComparison.Ordinal));
        Assert.Contains(vm.CurrentRows, r => r.Label.Contains("Largest hit", StringComparison.Ordinal));
        Assert.Contains(vm.CurrentRows, r => r.Label.Contains("Lethal", StringComparison.Ordinal));
        Assert.DoesNotContain(vm.CurrentRows, r =>
            r.Label.Contains("resistance", StringComparison.OrdinalIgnoreCase)
            || r.Label.Contains("mitigation", StringComparison.OrdinalIgnoreCase)
            || r.Label.Contains("survivability", StringComparison.OrdinalIgnoreCase));

        vm.SelectCategoryCommand.Execute(CompareCategoryId.PowerUsage);
        Assert.Equal("12", Row(vm, "Activations").SideA);
        Assert.Equal("7", Row(vm, "Activations").SideB);
        Assert.Contains(vm.CurrentRows, r => r.Label.Contains("Burn (Player) · Activations", StringComparison.Ordinal));
        Assert.DoesNotContain(vm.CurrentRows, r => r.Label.Contains("Unmatched", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Compare_xaml_has_independent_selectors_and_no_swap_or_extra_tabs()
    {
        var root = FindRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src/CoHAnalytics/Workspaces/HistoricalCompareView.xaml"));
        foreach (var name in new[]
                 {
                     "CompareAccountSelectorA", "CompareCharacterSelectorA", "CompareSegmentSelectorA",
                     "CompareAccountSelectorB", "CompareCharacterSelectorB", "CompareSegmentSelectorB",
                     "CompareCategoryHost", "CompareMetricTable", "CompareLargestDifferences", "CompareLegend",
                     "CompareVsLabel"
                 })
        {
            Assert.Contains(name, xaml, StringComparison.Ordinal);
        }

        Assert.Contains("CompareCategoryHost", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectCategoryCommand", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Swap", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Button", xaml[(xaml.IndexOf("CompareVsLabel", StringComparison.Ordinal) - 400)..(xaml.IndexOf("CompareVsLabel", StringComparison.Ordinal) + 80)], StringComparison.Ordinal);
        Assert.DoesNotContain("Targets", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Misc", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("winner", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("improved", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("regressed", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Difference (A", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"Difference\"", xaml, StringComparison.Ordinal);
        Assert.Contains("% Difference", xaml, StringComparison.Ordinal);
        Assert.Contains("Higher value", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Lower value", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Brush.Success", xaml, StringComparison.Ordinal);
        Assert.Contains("Foreground=\"{StaticResource Brush.Accent}\" FontWeight=\"Bold\" Text=\"A\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Foreground=\"{StaticResource Brush.InspirationResistance}\" FontWeight=\"Bold\" Text=\"B\"", xaml, StringComparison.Ordinal);
        Assert.Contains("No data available", xaml, StringComparison.Ordinal);
        Assert.Contains("Value is zero (recorded)", xaml, StringComparison.Ordinal);
        Assert.Contains("do not imply better or worse performance", xaml, StringComparison.Ordinal);
        Assert.All(
            xaml.Split('\n').Select(line => line.TrimEnd('\r'))
                .Where(line => line.Contains("Brush.Error", StringComparison.Ordinal)),
            line => Assert.Contains("ErrorMessage", line, StringComparison.Ordinal));

        var workspace = File.ReadAllText(Path.Combine(root, "src/CoHAnalytics/Workspaces/AnalyticsView.xaml"));
        Assert.Contains("HistoricalCompareView", workspace, StringComparison.Ordinal);
        Assert.DoesNotContain("Swap", workspace, StringComparison.OrdinalIgnoreCase);
        var nav = File.ReadAllText(Path.Combine(root, "src/CoHAnalytics/Navigation/NavigationItem.cs"));
        Assert.DoesNotContain("COMPARE", nav, StringComparison.Ordinal);
    }

    private static CompareMetricRow Row(HistoricalCompareViewModel vm, string label) =>
        Assert.Single(vm.CurrentRows, r => r.Label == label);

    private static CompareMetricRow RowContains(HistoricalCompareViewModel vm, string text) =>
        Assert.Single(vm.CurrentRows, r => r.Label.Contains(text, StringComparison.Ordinal));

    private static AnalyticalProjectionView View(CombatAnalyticsProjection projection) => new()
    {
        SourceKind = AnalyticalProjectionSourceKind.HistoricalDurable,
        Projection = projection
    };

    private static CombatAnalyticsProjection Metrics(
        Metric<CombatScaledAmount>? damage = null,
        Metric<CombatScaledAmount>? player = null,
        Metric<CombatScaledAmount>? pets = null,
        Metric<CombatScaledAmount>? taken = null,
        Metric<CombatScaledAmount>? healing = null,
        Metric<CombatScaledAmount>? endurance = null,
        Metric<CombatScaledAmount>? proc = null,
        TimeSpan? duration = null,
        long? dps = null) =>
        CombatAnalyticsProjection.Empty with
        {
            Session = CombatSessionSummary.Empty with
            {
                Metrics = CombatSessionMetricSet.Empty with
                {
                    DamageDealt = damage ?? Metric<CombatScaledAmount>.NotCaptured(),
                    DamageDealtSelf = player ?? Metric<CombatScaledAmount>.NotCaptured(),
                    DamageDealtOwnedPets = pets ?? Metric<CombatScaledAmount>.NotCaptured(),
                    DamageReceived = taken ?? Metric<CombatScaledAmount>.NotCaptured(),
                    HealingDealt = healing ?? Metric<CombatScaledAmount>.NotCaptured(),
                    EnduranceGranted = endurance ?? Metric<CombatScaledAmount>.NotCaptured()
                }
            },
            Clock = SegmentClock.Empty with
            {
                WallClockDuration = duration is { } value
                    ? Metric<TimeSpan>.Available(value, MetricEvidence.DerivedFromObserved, denominator: RateDenominatorKind.WallClock)
                    : Metric<TimeSpan>.NotCaptured(),
                WallClockDamagePerSecondHundredths = dps is { } rate
                    ? Metric<long>.Available(rate, MetricEvidence.DerivedFromObserved, denominator: RateDenominatorKind.WallClock)
                    : Metric<long>.NotCaptured()
            },
            Attribution = CombatProcAttributionSummary.Empty with
            {
                ProcDamage = proc ?? Metric<CombatScaledAmount>.NotCaptured(),
                DirectCount = proc is { Availability: MetricAvailability.Available } ? 1 : 0
            }
        };

    private static Metric<CombatScaledAmount> Amount(decimal whole) =>
        Metric<CombatScaledAmount>.Available(new CombatScaledAmount((long)(whole * CombatScaledAmount.Scale)));

    private static CombatPowerAnalysisRow Outgoing(string name, CombatAnalyticsScope scope, long hundredths) =>
        CombatOffenseViewModelTests.Power(name) with
        {
            Scope = scope,
            Direction = CombatAnalyticsDirection.Outgoing,
            DamageMagnitudeMetric = Metric<CombatScaledAmount>.Available(new(hundredths))
        };

    private static CombatPowerAnalysisRow Incoming(string name, long hundredths) =>
        CombatOffenseViewModelTests.Power(name) with
        {
            Direction = CombatAnalyticsDirection.Incoming,
            DamageMagnitudeMetric = Metric<CombatScaledAmount>.Available(new(hundredths)),
            LargestHit = new(hundredths)
        };

    private static CombatPowerAnalysisRow Heal(string name) =>
        CombatOffenseViewModelTests.Power(name) with
        {
            HealingMagnitudeMetric = Metric<CombatScaledAmount>.Available(new(5001)),
            DamageMagnitudeMetric = Metric<CombatScaledAmount>.NotCaptured()
        };

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "src/CoHAnalytics.slnx"))) return dir.FullName;
        throw new InvalidOperationException("Repository not found.");
    }
}
