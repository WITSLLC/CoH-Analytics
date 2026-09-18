using System.Globalization;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CoHAnalytics.Homecoming;
using CoHAnalytics.Models;
using CoHAnalytics.ReferenceData;
using CoHAnalytics.Services;
using CoHAnalytics.Tests.Homecoming;
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
        Assert.Equal("Player", power.Scope);
        Assert.Equal("3", power.Activations);
        Assert.Equal("40.00", power.Details.Single(v => v.Label == "Direct damage").Value);
        Assert.Equal("10.01", power.Details.Single(v => v.Label == "DoT damage").Value);
        Assert.Equal("12.34", power.Details.Single(v => v.Label == "Maximum hit").Value);
        Assert.Equal("8", power.Details.Single(v => v.Label == "Events").Value);
        Assert.Equal("2", power.Details.Single(v => v.Label == "Still recharging").Value);
        Assert.DoesNotContain(power.Details, v => v.Label.Contains("Attempt", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(power.Details, v => v.Label.Contains('%') || v.Label.Contains("DPS") || v.Label.Contains("Average"));
        Assert.Null(vm.CoverageNote);
        Assert.Contains("Partial", vm.Summary.Single(v => v.Label == "Proc damage").Note);
        Assert.DoesNotContain(vm.Summary.Where(v => v.Label != "Proc damage"), v => v.Note is not null && v.Note.Contains("Partial"));
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
        Assert.DoesNotContain(vm.Summary, v => v.Note is not null && v.Note.Contains("Partial"));
        Assert.Equal("Based on total elapsed session time.", vm.Summary.Single(v => v.Label == "Session DPS").Note);
    }

    [Fact]
    public void Damage_table_omits_zero_unavailable_and_activation_only_rows_without_mutating_source_data()
    {
        var hasten = new CombatPowerAnalysisRow
        {
            Scope = CombatAnalyticsScope.Self, PowerName = "Hasten", ActivationCount = 4, ConfirmedStillRechargingCount = 2
        };
        var zero = Power("Siphon Speed") with { DamageMagnitudeMetric = Metric<CombatScaledAmount>.Available(new(0)), ActivationCount = 6 };
        var unavailable = Power("Fulcrum Shift") with
        {
            DamageMagnitudeMetric = Metric<CombatScaledAmount>.NotCaptured(), DamageMagnitude = new(888888), ActivationCount = 3
        };
        var source = CombatAnalyticsProjection.Empty with
        {
            Powers = [Power("Burn"), Power("Burn") with { Scope = CombatAnalyticsScope.OwnPetsAggregate, DamageMagnitudeMetric = Metric<CombatScaledAmount>.Available(new(7344)) },
                hasten, zero, unavailable]
        };
        var vm = new CombatOffenseViewModel();
        vm.SetProjection(source);
        Assert.Equal(2, vm.Powers.Count);
        Assert.DoesNotContain(vm.Powers, p => p.Name is "Hasten" or "Siphon Speed" or "Fulcrum Shift");
        Assert.Equal("Owned pets", vm.Powers[0].Scope);
        Assert.Equal("Player", vm.Powers[1].Scope);
        Assert.Same(source.Powers[1], vm.Powers[0].Source);
        Assert.Same(source.Powers[0], vm.Powers[1].Source);
        Assert.Equal(5, source.Powers.Count);
        Assert.Equal(4, source.Powers.Single(p => p.PowerName == "Hasten").ActivationCount);
        Assert.Same(vm.Powers[0], vm.SelectedPower);
        vm.SelectedPower = vm.Powers[1];
        Assert.Equal("50.01", vm.SelectedPower.Damage);
        Assert.Equal("3", vm.SelectedPower.Activations);
        Assert.DoesNotContain(vm.SelectedPower.Details, d => d.Label.Contains("Attempt"));
    }

    [Fact]
    public void Damage_display_uses_grouped_two_decimals_and_counts_stay_integers()
    {
        var row = Power("Burn") with
        {
            DamageMagnitudeMetric = Metric<CombatScaledAmount>.Available(new(1_951_390)),
            DirectAmount = new(1_951_390),
            ActivationCount = 1234, EventCount = 12
        };
        var vm = new CombatOffenseViewModel();
        vm.SetProjection(CombatAnalyticsProjection.Empty with
        {
            Session = CombatSessionSummary.Empty with
            {
                Metrics = CombatSessionMetricSet.Empty with
                {
                    DamageDealt = Metric<CombatScaledAmount>.Available(new(6_664_216)),
                    DamageDealtSelf = Metric<CombatScaledAmount>.Available(new(1_951_390))
                }
            },
            Powers = [row],
            DamageTypeBreakdown = MetricRef<IReadOnlyList<CombatDamageTypeTotal>>.Available(
                [new() { DamageType = new("Fire"), Amount = new(1_951_390), EventCount = 12 }])
        });
        Assert.Equal("66,642.16", vm.Summary.Single(v => v.Label == "Total outgoing damage").Value);
        Assert.Equal("19,513.90", vm.Powers[0].Damage);
        Assert.Equal("19,513.90", vm.SelectedPower!.Details.Single(d => d.Label == "Damage").Value);
        Assert.Equal("19,513.90", vm.DamageTypes[0].Damage);
        Assert.Equal(1234.ToString("N0", CultureInfo.CurrentCulture), vm.Powers[0].Activations);
        Assert.Equal(1234.ToString("N0", CultureInfo.CurrentCulture), vm.SelectedPower.Details.Single(d => d.Label == "Activations").Value);
        Assert.Equal(12.ToString("N0", CultureInfo.CurrentCulture), vm.SelectedPower.Details.Single(d => d.Label == "Events").Value);
        Assert.Equal(1_951_390, row.DamageMagnitudeMetric.Value!.Value.Hundredths);
    }

    [Fact]
    public void Power_breakdown_headers_sort_descending_by_source_values_and_keep_selection()
    {
        var low = Power("Alpha") with { ActivationCount = 9, DamageMagnitudeMetric = Metric<CombatScaledAmount>.Available(new(1000)) };
        var high = Power("Zulu") with { ActivationCount = 2, DamageMagnitudeMetric = Metric<CombatScaledAmount>.Available(new(3000)) };
        var pets = Power("Alpha") with
        {
            Scope = CombatAnalyticsScope.OwnPetsAggregate, ActivationCount = 5,
            DamageMagnitudeMetric = Metric<CombatScaledAmount>.Available(new(2000))
        };
        var vm = new CombatOffenseViewModel();
        vm.SetProjection(CombatAnalyticsProjection.Empty with { Powers = [low, high, pets] });
        Assert.Equal("Damage ▾", vm.DamageColumnHeader);
        Assert.Equal("Power", vm.PowerColumnHeader);
        Assert.Equal(["Zulu", "Alpha", "Alpha"], vm.Powers.Select(p => p.Name));
        Assert.Equal(["Player", "Owned pets", "Player"], vm.Powers.Select(p => p.Scope));
        Assert.Equal([3000L, 2000L, 1000L], vm.Powers.Select(p => p.Source.DamageMagnitudeMetric.Value!.Value.Hundredths));
        var selected = vm.SelectedPower;
        Assert.Equal("Zulu", selected!.Name);
        vm.SortPowersCommand.Execute("Activations");
        Assert.Equal("Activations ▾", vm.ActivationsColumnHeader);
        Assert.Equal("Damage", vm.DamageColumnHeader);
        Assert.Equal([9L, 5L, 2L], vm.Powers.Select(p => p.Source.ActivationCount));
        Assert.Same(selected, vm.SelectedPower);
        Assert.Equal("2", vm.SelectedPower!.Activations);
        Assert.Equal("30.00", vm.SelectedPower.Damage);
        vm.SortPowersCommand.Execute("Power");
        Assert.Equal("Zulu", vm.Powers[0].Name);
        Assert.Equal(["Owned pets", "Player"], vm.Powers.Skip(1).Select(p => p.Scope));
        vm.SortPowersCommand.Execute("Source");
        Assert.Equal(["Player", "Player", "Owned pets"], vm.Powers.Select(p => p.Scope));
        Assert.Equal(["Zulu", "Alpha", "Alpha"], vm.Powers.Select(p => p.Name));
        vm.SortPowersCommand.Execute("Damage");
        Assert.Equal([3000L, 2000L, 1000L], vm.Powers.Select(p => p.Source.DamageMagnitudeMetric.Value!.Value.Hundredths));
        Assert.Same(selected, vm.SelectedPower);
        Assert.Equal(3, vm.Powers.Select(p => p.Source).Distinct().Count());
    }

    [Fact]
    public void Icons_reuse_build_catalog_tokens_and_asset_provider_with_generic_fallback()
    {
        var catalog = new Catalog(); var assets = new Assets();
        var vm = new CombatOffenseViewModel(catalog, assets);
        vm.SetProjection(Sample(), Manifest());
        Assert.Equal(("Brute_Melee", "Fire_Melee", "Burn"), catalog.Last);
        Assert.Equal("existing-icon", assets.Last);
        Assert.Same(assets.Image, vm.Powers.Single(p => p.Scope == "Player").Icon);
        Assert.NotSame(CombatOffenseViewModel.GenericDamageIcon, vm.Powers.Single(p => p.Scope == "Player").Icon);
        AssertGenericFallback(vm.Powers.Single(p => p.Scope == "Owned pets").Icon);
        catalog.PowerDisplayName = "Not Burn";
        vm.SetProjection(Sample(), Manifest());
        Assert.Same(assets.Image, vm.Powers.Single(p => p.Scope == "Player").Icon);
        catalog.Found = false;
        vm.SetProjection(Sample(), Manifest());
        Assert.All(vm.Powers, p => AssertGenericFallback(p.Icon));
        catalog.Found = true;
        vm.SetProjection(Sample());
        Assert.All(vm.Powers, p => AssertGenericFallback(p.Icon));
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
        Assert.All(vm.Powers, p => { AssertGenericFallback(p.Icon); Assert.Empty(p.Procs); });
        Assert.Single(vm.Procs);
    }

    [Fact]
    public void Catalog_display_name_resolves_unique_frozen_self_power_without_fuzzy_or_pet_fallback()
    {
        var catalog = new Catalog { PowerDisplayName = "Super Jump" };
        var assets = new Assets();
        var manifest = new FrozenBuildManifest
        {
            ManifestHash = "fixture",
            Powers =
            [
                new()
                {
                    RawCategoryToken = "Pool", RawPowerSetToken = "Leaping", RawPowerToken = "Long_Jump",
                    SourceOrder = 0, AcquisitionLevel = 4
                }
            ]
        };
        var player = Power("Super Jump");
        var pet = player with { Scope = CombatAnalyticsScope.OwnPetsAggregate, DamageMagnitudeMetric = Metric<CombatScaledAmount>.Available(new(7344)) };
        var miss = Power("Super Jumps");
        var procLike = Power("Scirocco's Dervish: Chance for Lethal Damage");
        var vm = new CombatOffenseViewModel(catalog, assets);
        vm.SetProjection(CombatAnalyticsProjection.Empty with { Powers = [player, pet, miss, procLike] }, manifest);
        Assert.Equal(("Pool", "Leaping", "Long_Jump"), catalog.Last);
        Assert.Equal("existing-icon", assets.Last);
        Assert.Same(assets.Image, vm.Powers.Single(p => p.Name == "Super Jump" && p.Scope == "Player").Icon);
        AssertGenericFallback(vm.Powers.Single(p => p.Name == "Super Jump" && p.Scope == "Owned pets").Icon);
        vm.SelectedPower = vm.Powers.Single(p => p.Name == "Super Jump" && p.Scope == "Player");
        Assert.Same(assets.Image, vm.SelectedPower.Icon);
        AssertGenericFallback(vm.Powers.Single(p => p.Name == "Super Jumps").Icon);
        AssertGenericFallback(vm.Powers.Single(p => p.Name.StartsWith("Scirocco", StringComparison.Ordinal)).Icon);
        catalog.PowerDisplayName = "Super Jump";
        var colliding = manifest with
        {
            Powers =
            [
                manifest.Powers[0],
                manifest.Powers[0] with { RawPowerToken = "High_Jump", SourceOrder = 1 }
            ]
        };
        vm.SetProjection(CombatAnalyticsProjection.Empty with { Powers = [player] }, colliding);
        AssertGenericFallback(vm.Powers.Single().Icon);
        Assert.Empty(vm.Powers.Single().Procs);
    }

    [Fact]
    public void Inherent_and_incarnate_unique_display_names_resolve_without_frozen_power()
    {
        using var fixture = HomecomingPowerReferenceFixture.Create();
        var catalog = fixture.CreateCatalog();
        var assets = new Assets();
        var compositor = new RecordingCompositor();
        var vm = new CombatOffenseViewModel(catalog, assets, compositor: compositor);
        vm.SetProjection(CombatAnalyticsProjection.Empty with { Powers = [Power("Brawl"), Power("Scorch")] });
        Assert.Equal("Inherent_Brawl.tga", assets.Last);
        Assert.Same(assets.Image, vm.Powers.Single(p => p.Name == "Brawl").Icon);
        AssertGenericFallback(vm.Powers.Single(p => p.Name == "Scorch").Icon);
        Assert.Empty(compositor.Requests);
        var incarnate = new Catalog
        {
            PowerDisplayName = "Pyronic Radial Final Judgement",
            UniqueIdentity = ("Incarnate", "Judgement", "Pyronic_Radial_Final_Judgement")
        };
        var interfaceCatalog = new Catalog
        {
            PowerDisplayName = "Reactive Interface",
            UniqueIdentity = ("Incarnate", "Interface", "Reactive")
        };
        var global = new Catalog
        {
            PowerDisplayName = "Doublehit",
            UniqueIdentity = ("Incarnate", "Global", "Doublehit")
        };
        Assert.Same(assets.Image, IconFor(incarnate, assets, "Pyronic Radial Final Judgement"));
        Assert.Same(assets.Image, IconFor(interfaceCatalog, assets, "Reactive Interface"));
        Assert.Same(assets.Image, IconFor(global, assets, "Doublehit"));
        AssertGenericFallback(IconFor(global, assets, "Doublehit", CombatAnalyticsScope.OwnPetsAggregate));
    }

    [Theory]
    [InlineData("Armageddon: Chance for Fire Damage")]
    [InlineData("Eradication: Chance for Energy Damage")]
    [InlineData("Obliteration: Chance for Smashing Damage")]
    [InlineData("Scirocco's Dervish: Chance for Lethal Damage")]
    public void Attributed_enhancement_procs_use_reference_composed_icons(string procName)
    {
        var catalog = ItemReferenceCatalogFactory.LoadEmbeddedProduction();
        Assert.True(catalog.TryResolve(procName, out var resolution));
        EnhancementSetReferenceRecord? parentSet = null;
        if (!string.IsNullOrWhiteSpace(resolution.Item.EnhancementSetId))
            Assert.True(catalog.TryGetEnhancementSetById(resolution.Item.EnhancementSetId, out parentSet));
        var expected = ReferenceEnhancementBrowseSupport.TryBuildCompositionRequest(
            catalog, resolution.Item, parentSet, new FakeBoostMetadata());
        Assert.NotNull(expected);
        var compositor = new RecordingCompositor();
        var assets = new Assets();
        var vm = new CombatOffenseViewModel(
            items: catalog, assets: assets, compositor: compositor, boostMetadata: new FakeBoostMetadata());
        vm.SetProjection(CombatAnalyticsProjection.Empty with { Powers = [Power(procName)] });
        Assert.Null(assets.Last);
        Assert.NotSame(assets.Image, vm.Powers.Single().Icon);
        Assert.Same(compositor.Image, vm.Powers.Single().Icon);
        Assert.Same(compositor.Image, vm.SelectedPower!.Icon);
        Assert.NotSame(CombatOffenseViewModel.GenericDamageIcon, vm.SelectedPower.Icon);
        Assert.Equal(expected, Assert.Single(compositor.Requests));
        vm.SetProjection(CombatAnalyticsProjection.Empty with
        {
            Powers = [Power(procName) with { Scope = CombatAnalyticsScope.OwnPetsAggregate }]
        });
        AssertGenericFallback(vm.Powers.Single().Icon);
        Assert.Single(compositor.Requests);
    }

    [Fact]
    public void Enhancement_procs_do_not_surface_raw_item_artwork_without_composition()
    {
        var catalog = ItemReferenceCatalogFactory.LoadEmbeddedProduction();
        Assert.True(catalog.TryResolve("Scirocco's Dervish: Chance for Lethal Damage", out var resolution));
        Assert.False(string.IsNullOrWhiteSpace(resolution.Item.Icon));
        var assets = new Assets();
        var vm = new CombatOffenseViewModel(items: catalog, assets: assets);
        vm.SetProjection(CombatAnalyticsProjection.Empty with
        {
            Powers = [Power("Scirocco's Dervish: Chance for Lethal Damage")]
        });
        Assert.Null(assets.Last);
        AssertGenericFallback(vm.Powers.Single().Icon);
        AssertGenericFallback(vm.SelectedPower!.Icon);
    }

    [Fact]
    public void Owned_pet_powers_use_generic_fallback_without_canonical_pet_identity()
    {
        using var fixture = HomecomingPowerReferenceFixture.Create();
        var catalog = fixture.CreateCatalog();
        var assets = new Assets();
        var items = ItemReferenceCatalogFactory.LoadEmbeddedProduction();
        var compositor = new RecordingCompositor();
        var vm = new CombatOffenseViewModel(catalog, assets, items, compositor, new FakeBoostMetadata());
        vm.SetProjection(CombatAnalyticsProjection.Empty with
        {
            Powers =
            [
                Power("Frigid Slam") with { Scope = CombatAnalyticsScope.OwnPetsAggregate },
                Power("Enervating Storm") with { Scope = CombatAnalyticsScope.OwnPetsAggregate }
            ]
        });
        Assert.All(vm.Powers, p => AssertGenericFallback(p.Icon));
        Assert.Null(assets.Last);
        Assert.Empty(compositor.Requests);
    }

    private static ImageSource? IconFor(
        Catalog catalog, Assets assets, string name, CombatAnalyticsScope scope = CombatAnalyticsScope.Self)
    {
        var vm = new CombatOffenseViewModel(catalog, assets);
        vm.SetProjection(CombatAnalyticsProjection.Empty with { Powers = [Power(name) with { Scope = scope }] });
        return vm.Powers.Single().Icon;
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

    [Fact]
    public void Generic_fallback_uses_packaged_app_resource_not_external_path()
    {
        var vm = new CombatOffenseViewModel();
        vm.SetProjection(CombatAnalyticsProjection.Empty with { Powers = [Power("Unknown Damage")] });
        AssertGenericFallback(vm.SelectedPower!.Icon);
        Assert.Equal(
            AssetUri.ForResource(CombatOffenseViewModel.GenericDamageIconResource),
            ((BitmapImage)vm.SelectedPower.Icon!).UriSource);
        vm.SetProjection(null);
        Assert.Null(vm.SelectedPower);
        Assert.False(vm.HasSelectedPower);
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

    private static void AssertGenericFallback(ImageSource? icon)
    {
        Assert.Same(CombatOffenseViewModel.GenericDamageIcon, icon);
        var bitmap = Assert.IsType<BitmapImage>(icon);
        Assert.Equal(AssetUri.ForResource(CombatOffenseViewModel.GenericDamageIconResource), bitmap.UriSource);
        Assert.StartsWith("pack://application:,,,/CoHAnalytics;component/", bitmap.UriSource!.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("Master Icons", bitmap.UriSource.OriginalString, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("generic_icon.png", bitmap.UriSource.OriginalString, StringComparison.Ordinal);
    }

    private sealed class Catalog : IHomecomingPowerReferenceCatalog
    {
        public bool IsLoaded => true;
        public bool Found { get; set; } = true;
        public string PowerDisplayName { get; set; } = "Burn";
        public (string Category, string Set, string Power)? UniqueIdentity { get; set; }
        public (string, string, string)? Last { get; private set; }
        public bool TryResolve(string categoryId, string powersetId, string powerId, out HomecomingPowerReference power)
        {
            Last = (categoryId, powersetId, powerId);
            power = new(categoryId, powersetId, powerId, "Fire Melee", PowerDisplayName, "existing-icon", false, false, HomecomingPowerType.Click);
            return Found;
        }
        public bool TryResolveUniqueDisplayName(string? displayName, out HomecomingPowerReference power)
        {
            power = default;
            if (UniqueIdentity is not { } id
                || !Found
                || !string.Equals(displayName, PowerDisplayName, StringComparison.OrdinalIgnoreCase))
                return false;
            return TryResolve(id.Category, id.Set, id.Power, out power);
        }
    }
    private sealed class Assets : IInstalledGameAssetProvider
    {
        public string? Last { get; private set; }
        public ImageSource Image { get; } = new DrawingImage();
        public ImageSource? TryResolve(string? iconIdentity) { Last = iconIdentity; return Image; }
    }

    private sealed class RecordingCompositor : IEnhancementIconCompositor
    {
        public List<EnhancementIconCompositionRequest> Requests { get; } = [];
        public ImageSource Image { get; } = new DrawingImage();
        public ImageSource? TryCompose(EnhancementIconCompositionRequest request)
        {
            Requests.Add(request);
            return Image;
        }
    }

    private sealed class FakeBoostMetadata : IHomecomingBoostMetadataProvider
    {
        public IReadOnlyList<string>? TryGetBoostsAllowed(string? homecomingSourceId) =>
            string.IsNullOrWhiteSpace(homecomingSourceId) ? null : ["Damage"];
    }
}
