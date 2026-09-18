using System.Windows.Media;
using CoHAnalytics.Homecoming;
using CoHAnalytics.Models;
using CoHAnalytics.ViewModels.Workspaces;

namespace CoHAnalytics.Tests.Workspaces;

public sealed class CombatOffenseViewModelTests
{
    [Fact]
    public void Summary_and_power_details_use_supplied_values_without_recomputing_rates()
    {
        var projection = Sample();
        var vm = new CombatOffenseViewModel();
        vm.SetProjection(projection);
        Assert.Equal("123.45", vm.Summary.Single(v => v.Label == "Total outgoing damage").Value);
        Assert.Equal("98.76", vm.Summary.Single(v => v.Label == "Session DPS").Value);
        var power = vm.Powers.Single(p => p.Name == "Burn" && p.Source.Scope == CombatAnalyticsScope.Self);
        Assert.Same(projection.Powers[0], power.Source);
        Assert.Equal("50.01", power.Damage);
        Assert.Equal("3", power.Activations);
        Assert.Equal("40", power.Details.Single(v => v.Label == "Direct damage").Value);
        Assert.Equal("10.01", power.Details.Single(v => v.Label == "DoT damage").Value);
        Assert.Equal("12.34", power.Details.Single(v => v.Label == "Maximum hit").Value);
        Assert.Equal("8", power.Details.Single(v => v.Label == "Events").Value);
        Assert.Equal("2", power.Details.Single(v => v.Label == "Still recharging").Value);
        Assert.DoesNotContain(power.Details, v => v.Label.Contains("Attempt", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(power.Details, v => v.Label.Contains('%') || v.Label.Contains("DPS") || v.Label.Contains("Average"));
    }

    [Fact]
    public void Rows_rank_damage_and_keep_player_pet_identity_without_double_counting()
    {
        var vm = new CombatOffenseViewModel();
        vm.SetProjection(Sample());
        Assert.Equal(2, vm.Powers.Count);
        Assert.Equal("Owned pets", vm.Powers[0].Scope);
        Assert.Equal("Player", vm.Powers[1].Scope);
        Assert.Same(vm.Powers[0], vm.SelectedPower);
        vm.SelectedPower = vm.Powers[1];
        Assert.Equal("Burn", vm.SelectedPower.Name);
        Assert.Same(vm.Powers[1], vm.SelectedPower);
        Assert.Equal(CombatAnalyticsScope.Self, vm.SelectedPower.Source.Scope);
        Assert.Equal("50.01", vm.SelectedPower.Damage);
        vm.SelectedPower = vm.Powers[0];
        Assert.Equal("Burn", vm.SelectedPower.Name);
        Assert.Same(vm.Powers[0], vm.SelectedPower);
        Assert.Equal(CombatAnalyticsScope.OwnPetsAggregate, vm.SelectedPower.Source.Scope);
        Assert.Equal("73.44", vm.SelectedPower.Damage);
        vm.SetProjection(null);
        Assert.Null(vm.SelectedPower);
        Assert.Empty(vm.Powers);
        Assert.Empty(vm.Summary);
        Assert.True(vm.ShowEmptyMessage);
    }

    [Fact]
    public void Missing_typed_metrics_never_fall_back_to_compatibility_scalars_or_reconstructed_values()
    {
        var vm = new CombatOffenseViewModel();
        vm.SetProjection(CombatAnalyticsProjection.Empty with
        {
            Session = CombatSessionSummary.Empty with { DamageDealt = new(999999), ActivationCount = 99 },
            Powers = [Power("Suppressed") with { DamageMagnitude = new(888888), DamageMagnitudeMetric = Metric<CombatScaledAmount>.Unsupported(), ActivationCount = 0, ConfirmedStillRechargingCount = 0 }],
            DamageTypes = [new() { DamageType = new("Fire"), Amount = new(222) }],
            Attribution = CombatProcAttributionSummary.Empty with { BuildConfirmedProcDamage = new(444) }
        });
        Assert.Empty(vm.Summary);
        Assert.Empty(vm.Powers);
        Assert.Empty(vm.DamageTypes);
        Assert.Empty(vm.Procs);
        Assert.True(vm.ShowEmptyMessage);
    }

    [Fact]
    public void Incomplete_values_and_overflow_rows_remain_visible_with_player_caveats()
    {
        var row = Power("Burn") with
        {
            DamageMagnitudeMetric = Metric<CombatScaledAmount>.Incomplete(new(1234)),
            DamageTypeBreakdown = MetricRef<IReadOnlyList<CombatDamageTypeTotal>>.Incomplete(
                [new() { DamageType = new("Fire"), Amount = new(1234) }], new CoverageInfo { MissingDamageType = true })
        };
        var vm = new CombatOffenseViewModel();
        vm.SetProjection(Sample() with { Powers = [row], CoverageLimited = true });
        Assert.Contains("Partial", vm.CoverageNote);
        Assert.Equal("12.34", vm.SelectedPower!.Damage);
        Assert.Contains("Partial", vm.SelectedPower.Note);
        Assert.Contains("Partial", vm.SelectedPower.DamageTypesNote);
        Assert.Single(vm.SelectedPower.DamageTypes);
        Assert.Contains(vm.Targets, t => t.Label == "Additional targets" && t.Note is not null);
        Assert.Contains(vm.Summary, v => v.Label == "Proc damage" && v.Note!.Contains("Partial"));
    }

    [Fact]
    public void Lifecycle_only_rows_do_not_manufacture_damage_zero_or_attempt_totals()
    {
        var vm = new CombatOffenseViewModel();
        vm.SetProjection(CombatAnalyticsProjection.Empty with
        {
            Powers = [new() { Scope = CombatAnalyticsScope.Self, PowerName = "Build Up", ActivationCount = 4, ConfirmedStillRechargingCount = 2 }]
        });
        var power = Assert.Single(vm.Powers);
        Assert.Null(power.Damage);
        Assert.Equal(new[] { "Activations", "Still recharging" }, power.Details.Select(d => d.Label));
        Assert.DoesNotContain(power.Details, d => d.Label.Contains("Attempt"));
    }

    [Fact]
    public void Zero_available_damage_is_displayed_without_claiming_zero_unobserved_activations()
    {
        var vm = new CombatOffenseViewModel();
        vm.SetProjection(CombatAnalyticsProjection.Empty with
        {
            Powers = [new() { Scope = CombatAnalyticsScope.Self, PowerName = "Burn", DamageMagnitudeMetric = Metric<CombatScaledAmount>.Available(new(0)) }]
        });
        Assert.Equal("0", vm.SelectedPower!.Damage);
        Assert.Null(vm.SelectedPower.Activations);
        Assert.DoesNotContain(vm.SelectedPower.Details, v => v.Label == "Activations");
    }

    [Fact]
    public void Icons_reuse_build_catalog_tokens_and_asset_provider_with_empty_fallback()
    {
        var catalog = new Catalog(); var assets = new Assets();
        var vm = new CombatOffenseViewModel(catalog, assets);
        vm.SetProjection(Sample(), Manifest());
        Assert.Equal(("Brute_Melee", "Fire_Melee", "Burn"), catalog.Last);
        Assert.Equal("existing-icon", assets.Last);
        Assert.Same(assets.Image, vm.Powers.Single(p => p.Scope == "Player").Icon);
        Assert.Null(vm.Powers.Single(p => p.Scope == "Owned pets").Icon);
        catalog.Found = false;
        vm.SetProjection(Sample(), Manifest());
        Assert.All(vm.Powers, p => Assert.Null(p.Icon));
        catalog.Found = true;
        vm.SetProjection(Sample());
        Assert.All(vm.Powers, p => Assert.Null(p.Icon));
    }

    [Fact]
    public void Ambiguous_frozen_names_do_not_guess_icons_or_proc_parents()
    {
        var catalog = new Catalog(); var assets = new Assets();
        var manifest = Manifest();
        manifest = manifest with { Powers = [manifest.Powers[0], manifest.Powers[0] with { RawPowerSetToken = "Other", SourceOrder = 2 }] };
        var vm = new CombatOffenseViewModel(catalog, assets);
        vm.SetProjection(Sample(), manifest);
        Assert.Null(catalog.Last);
        Assert.All(vm.Powers, p => { Assert.Null(p.Icon); Assert.Empty(p.Procs); });
        Assert.Single(vm.Procs);
    }

    [Fact]
    public void Selected_power_procs_require_exact_parent_identity_and_never_match_pet_names()
    {
        var vm = new CombatOffenseViewModel();
        vm.SetProjection(Sample(), Manifest());
        Assert.Single(vm.Powers.Single(p => p.Scope == "Player").Procs);
        Assert.Empty(vm.Powers.Single(p => p.Scope == "Owned pets").Procs);
        Assert.Equal("4.56", vm.Procs[0].Damage);
        Assert.Equal("3", vm.Procs[0].Events);
    }

    [Fact]
    public void Segment_selection_populates_and_refreshes_offense_without_changing_other_chip_state()
    {
        using var f = new HistoricalCombatViewModelTests.Fixture();
        var historical = f.Create(); historical.Refresh();
        var damage = historical.ProjectionView!.Projection.Session.Metrics.DamageDealt;
        Assert.Equal(MetricAvailability.NotCaptured, damage.Availability);
        Assert.Null(damage.Value);
        Assert.DoesNotContain(historical.Offense.Summary, v => v.Label == "Total outgoing damage");
        historical.SelectSectionCommand.Execute(CombatSectionId.Defense);
        historical.SelectedAccount = historical.AccountsChoices.Single(a => a.Id == "Adelbert");
        historical.SelectedSegment = historical.SegmentChoices.Single(s => s.Header.CaptureKind == HistoricalCaptureKind.LegacyObservation);
        Assert.Equal(CombatSectionId.Defense, historical.SelectedSection);
        Assert.DoesNotContain(historical.Offense.Summary, v => v.Label == "Session DPS");
        Assert.Empty(historical.Offense.Powers);
        historical.SelectedSegment = null;
        Assert.Empty(historical.Offense.Summary);
        Assert.Null(historical.Offense.SelectedPower);
        Assert.Equal(new[] { "Offense", "Defense", "Healing", "Pets" }, historical.Sections.Select(s => s.Label));
    }

    internal static CombatPowerAnalysisRow Power(string name) => new()
    {
        Scope = CombatAnalyticsScope.Self, PowerName = name, DamageMagnitudeMetric = Metric<CombatScaledAmount>.Available(new(5001)),
        DirectAmount = new(4000), DotAmount = new(1001), LargestHit = new(1234), ActivationCount = 3, EventCount = 8,
        ConfirmedStillRechargingCount = 2,
        DamageTypeBreakdown = MetricRef<IReadOnlyList<CombatDamageTypeTotal>>.Available([new() { DamageType = new("Fire"), Amount = new(5001) }])
    };

    internal static FrozenBuildManifest Manifest() => new()
    {
        ManifestHash = "fixture", Powers = [new() { RawCategoryToken = "Brute_Melee", RawPowerSetToken = "Fire_Melee", RawPowerToken = "Burn", SourceOrder = 0, AcquisitionLevel = 1 }]
    };

    internal static CombatAnalyticsProjection Sample() => CombatAnalyticsProjection.Empty with
    {
        Session = CombatSessionSummary.Empty with
        { Metrics = CombatSessionMetricSet.Empty with { DamageDealt = Metric<CombatScaledAmount>.Available(new(12345)), DamageDealtSelf = Metric<CombatScaledAmount>.Available(new(5001)) } },
        Clock = SegmentClock.Empty with { WallClockDamagePerSecondHundredths = Metric<long>.Available(9876, denominator: RateDenominatorKind.WallClock) },
        Powers = [Power("Burn"), Power("Burn") with { Scope = CombatAnalyticsScope.OwnPetsAggregate, DamageMagnitudeMetric = Metric<CombatScaledAmount>.Available(new(7344)) },
            Power("Burn") with { Scope = CombatAnalyticsScope.PerPet }, Power("Incoming") with { Direction = CombatAnalyticsDirection.Incoming }],
        DamageTypeBreakdown = MetricRef<IReadOnlyList<CombatDamageTypeTotal>>.Available([new() { DamageType = new("Fire"), Amount = new(12345), EventCount = 12 }]),
        Targets = [new() { NormalizedTargetName = "enemy", DisplayName = "Council War Walker", DamageDealt = new(10000), EventCount = 6 },
            new() { NormalizedTargetName = "overflow", IsOverflow = true, DamageDealt = new(2345), EventCount = 2 }],
        Attribution = CombatProcAttributionSummary.Empty with
        {
            ProcDamage = Metric<CombatScaledAmount>.Incomplete(new(456)),
            ByParent = [new() { Mode = ProcAttributionMode.BuildConfirmed, ParentPowerId = "Brute_Melee.Fire_Melee.Burn", ParentPowerName = "Burn", ExactProcIdentity = "Annihilation: Chance for Fire", ProcDamageMetric = Metric<CombatScaledAmount>.Available(new(456)), EventCount = 3 }]
        }
    };

    private sealed class Catalog : IHomecomingPowerReferenceCatalog
    {
        public bool IsLoaded => true;
        public bool Found { get; set; } = true;
        public (string, string, string)? Last { get; private set; }
        public bool TryResolve(string categoryId, string powersetId, string powerId, out HomecomingPowerReference power)
        {
            Last = (categoryId, powersetId, powerId);
            power = new(categoryId, powersetId, powerId, "Fire Melee", "Burn", "existing-icon", false, false, HomecomingPowerType.Click);
            return Found;
        }
    }
    private sealed class Assets : IInstalledGameAssetProvider
    {
        public string? Last { get; private set; }
        public ImageSource Image { get; } = new DrawingImage();
        public ImageSource? TryResolve(string? iconIdentity) { Last = iconIdentity; return Image; }
    }
}
