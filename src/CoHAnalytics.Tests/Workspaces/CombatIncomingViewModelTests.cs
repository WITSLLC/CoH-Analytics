using System.Windows.Media;
using System.Windows.Media.Imaging;
using CoHAnalytics.Homecoming;
using CoHAnalytics.Models;
using CoHAnalytics.ReferenceData;
using CoHAnalytics.Services;
using CoHAnalytics.Tests.Homecoming;
using CoHAnalytics.ViewModels.Workspaces;

namespace CoHAnalytics.Tests.Workspaces;

public sealed class CombatIncomingViewModelTests
{
    [Fact]
    public void Incoming_uses_incoming_direction_and_suppresses_outgoing_and_zero_rows()
    {
        var vm = new CombatIncomingViewModel();
        vm.SetProjection(CombatAnalyticsProjection.Empty with
        {
            Session = CombatSessionSummary.Empty with
            {
                Metrics = CombatSessionMetricSet.Empty with
                {
                    DamageReceived = Metric<CombatScaledAmount>.Available(new(2215)),
                    DamageReceivedOwnedPets = Metric<CombatScaledAmount>.Available(new(400)),
                    DamageDealt = Metric<CombatScaledAmount>.Available(new(99999))
                }
            },
            Powers =
            [
                Incoming("Bone Shard"),
                Incoming("Bone Shard") with { Scope = CombatAnalyticsScope.OwnPetsAggregate, DamageMagnitudeMetric = Metric<CombatScaledAmount>.Available(new(400)) },
                Incoming("Whispered") with { DamageMagnitudeMetric = Metric<CombatScaledAmount>.Available(CombatScaledAmount.Zero) },
                Incoming("Missing") with { DamageMagnitudeMetric = Metric<CombatScaledAmount>.NotCaptured() },
                Incoming("Scratch") with { DamageMagnitudeMetric = Metric<CombatScaledAmount>.Unsupported() },
                Incoming("Per pet") with { Scope = CombatAnalyticsScope.PerPet },
                CombatOffenseViewModelTests.Power("Burn")
            ],
            IncomingDamageTypeBreakdown = MetricRef<IReadOnlyList<CombatDamageTypeTotal>>.Available(
                [new() { DamageType = new("Lethal"), Amount = new(2215), EventCount = 4 }])
        });
        Assert.Equal("22.15", vm.Summary.Single(v => v.Label == "Damage taken").Value);
        Assert.Equal("4.00", vm.Summary.Single(v => v.Label == "Pet damage taken").Value);
        Assert.DoesNotContain(vm.Summary, v => v.Label.Contains("DPS", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(vm.Summary, v => v.Label.Contains("Resistance", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(vm.Summary, v => v.Label.Contains("Mitigation", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(vm.Summary, v => v.Label.Contains("Defense", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(2, vm.Powers.Count);
        Assert.All(vm.Powers, p => Assert.Equal(CombatAnalyticsDirection.Incoming, p.Source.Direction));
        Assert.Equal("Bone Shard", vm.Powers[0].Name);
        Assert.Equal("Player", vm.Powers[0].Scope);
        Assert.Equal("50.01", vm.Powers[0].Damage);
        Assert.Equal("8", vm.Powers[0].Events);
        Assert.Equal("12.34", vm.Powers[0].Details.Single(d => d.Label == "Maximum hit").Value);
        Assert.Equal("Owned pets", vm.Powers[1].Scope);
        Assert.Equal("Target", vm.TargetColumnHeader);
        Assert.DoesNotContain(vm.Powers.SelectMany(p => p.Details), d => d.Label.Contains("Distinct target", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(vm.Powers.SelectMany(p => p.Details), d => d.Label.Contains("Attacker", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(vm.Powers.SelectMany(p => p.Details), d => d.Label.Contains("Source", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(vm.Powers, p => p.Name is "Burn" or "Whispered" or "Missing" or "Scratch" or "Per pet");
        Assert.Equal("Lethal", Assert.Single(vm.DamageTypes).Label);
        Assert.Equal(2, vm.LargestHits.Count);
        Assert.All(vm.LargestHits, h => Assert.Equal("Bone Shard", h.Label));
        Assert.Equal("12.34", vm.LargestHits[0].Damage);
        Assert.Same(vm.Powers[0], vm.SelectedPower);
    }

    [Fact]
    public void Missing_incoming_metrics_stay_suppressed_and_clearing_projection_clears_selection()
    {
        var vm = new CombatIncomingViewModel();
        vm.SetProjection(CombatAnalyticsProjection.Empty with
        {
            Session = CombatSessionSummary.Empty with
            {
                DamageReceived = new(8888),
                Metrics = CombatSessionMetricSet.Empty with { DamageReceived = Metric<CombatScaledAmount>.NotCaptured() }
            },
            Powers = [Incoming("Bone Shard") with { DamageMagnitudeMetric = Metric<CombatScaledAmount>.Unsupported() }]
        });
        Assert.Empty(vm.Summary);
        Assert.Empty(vm.Powers);
        Assert.True(vm.ShowEmptyMessage);
        vm.SetProjection(CombatAnalyticsProjection.Empty with { Powers = [Incoming("Bone Shard")] });
        Assert.NotNull(vm.SelectedPower);
        vm.SetProjection(null);
        Assert.Null(vm.SelectedPower);
        Assert.False(vm.HasSelectedPower);
        Assert.Empty(vm.Powers);
        Assert.Empty(vm.Summary);
    }

    [Fact]
    public void Incoming_does_not_bind_player_frozen_powers_or_invent_attackers()
    {
        var catalog = new Catalog();
        var assets = new Assets();
        var vm = new CombatIncomingViewModel(catalog, assets);
        vm.SetProjection(CombatAnalyticsProjection.Empty with { Powers = [Incoming("Burn")] }, CombatOffenseViewModelTests.Manifest());
        Assert.Null(catalog.Last);
        AssertGenericFallback(vm.Powers.Single().Icon);
        Assert.Equal("Burn", vm.Powers.Single().Name);
        Assert.Equal("Player", vm.Powers.Single().Scope);
        Assert.DoesNotContain(vm.Powers.Single().Details, d => d.Label.Contains("Attacker", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(vm.Summary, v => v.Label.Contains("Threat", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Target", vm.TargetColumnHeader);
        Assert.DoesNotContain(vm.Powers.Single().Details, d => d.Label.Contains("Distinct target", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Incoming_details_omit_distinct_targets_even_when_the_outgoing_metric_is_populated()
    {
        var vm = new CombatIncomingViewModel();
        vm.SetProjection(CombatAnalyticsProjection.Empty with
        {
            Powers = [Incoming("Bone Shard") with { DistinctTargetCountMetric = Metric<long>.Available(7), DistinctTargetCount = 7 }]
        });
        Assert.Equal("Player", vm.SelectedPower!.Scope);
        Assert.DoesNotContain(vm.SelectedPower.Details, d => d.Label.Contains("Distinct target", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Target", vm.TargetColumnHeader);
        vm.SortPowersCommand.Execute("Target");
        Assert.Equal("Target ▾", vm.TargetColumnHeader);
        Assert.Equal("Player", vm.Powers.Single().Scope);
    }

    [Fact]
    public void Unique_incoming_power_icon_wins_over_generic_fallback()
    {
        using var fixture = HomecomingPowerReferenceFixture.Create();
        var catalog = fixture.CreateCatalog();
        var assets = new Assets();
        var compositor = new RecordingCompositor();
        var vm = new CombatIncomingViewModel(catalog, assets, compositor: compositor);
        vm.SetProjection(CombatAnalyticsProjection.Empty with { Powers = [Incoming("Brawl"), Incoming("Unknown Incoming")] });
        Assert.Equal("Inherent_Brawl.tga", assets.Last);
        Assert.Same(assets.Image, vm.Powers.Single(p => p.Name == "Brawl").Icon);
        Assert.NotSame(CombatOffenseViewModel.GenericDamageIcon, vm.Powers.Single(p => p.Name == "Brawl").Icon);
        AssertGenericFallback(vm.Powers.Single(p => p.Name == "Unknown Incoming").Icon);
        Assert.Empty(compositor.Requests);
    }

    [Fact]
    public void Exact_incoming_proc_identity_uses_composed_icon()
    {
        var catalog = ItemReferenceCatalogFactory.LoadEmbeddedProduction();
        var compositor = new RecordingCompositor();
        var assets = new Assets();
        var vm = new CombatIncomingViewModel(
            items: catalog, assets: assets, compositor: compositor, boostMetadata: new FakeBoostMetadata());
        vm.SetProjection(CombatAnalyticsProjection.Empty with
        {
            Powers = [Incoming("Scirocco's Dervish: Chance for Lethal Damage")]
        });
        Assert.Null(assets.Last);
        Assert.Same(compositor.Image, vm.Powers.Single().Icon);
        Assert.NotSame(CombatOffenseViewModel.GenericDamageIcon, vm.Powers.Single().Icon);
        Assert.Single(compositor.Requests);
    }

    [Fact]
    public void Incoming_pet_rows_use_generic_fallback()
    {
        var compositor = new RecordingCompositor();
        var vm = new CombatIncomingViewModel(compositor: compositor);
        vm.SetProjection(CombatAnalyticsProjection.Empty with
        {
            Powers = [Incoming("Bone Shard") with { Scope = CombatAnalyticsScope.OwnPetsAggregate }]
        });
        AssertGenericFallback(vm.Powers.Single().Icon);
        Assert.Empty(compositor.Requests);
    }

    [Fact]
    public void Headers_sort_incoming_events_without_changing_row_identity()
    {
        var vm = new CombatIncomingViewModel();
        vm.SetProjection(CombatAnalyticsProjection.Empty with
        {
            Powers =
            [
                Incoming("Zulu") with { DamageMagnitudeMetric = Metric<CombatScaledAmount>.Available(new(1000)), EventCount = 2 },
                Incoming("Alpha") with { DamageMagnitudeMetric = Metric<CombatScaledAmount>.Available(new(3000)), EventCount = 9 }
            ]
        });
        var selected = vm.SelectedPower;
        Assert.Equal("Alpha", selected!.Name);
        vm.SortPowersCommand.Execute("Events");
        Assert.Equal("Events ▾", vm.EventsColumnHeader);
        Assert.Equal(["Alpha", "Zulu"], vm.Powers.Select(p => p.Name));
        Assert.Same(selected, vm.SelectedPower);
        vm.SortPowersCommand.Execute("Power");
        Assert.Equal(["Zulu", "Alpha"], vm.Powers.Select(p => p.Name));
    }

    [Fact]
    public void Segment_selection_refreshes_incoming_without_changing_other_chip_state()
    {
        using var f = new HistoricalCombatViewModelTests.Fixture();
        var historical = f.Create(); historical.Refresh();
        historical.SelectSectionCommand.Execute(CombatSectionId.Incoming);
        Assert.True(historical.IsIncomingSelected);
        Assert.DoesNotContain(historical.Incoming.Summary, v => v.Label == "Damage taken");
        historical.SelectedAccount = historical.AccountsChoices.Single(a => a.Id == "Adelbert");
        historical.SelectedSegment = historical.SegmentChoices.Single(s => s.Header.CaptureKind == HistoricalCaptureKind.LegacyObservation);
        Assert.Equal(CombatSectionId.Incoming, historical.SelectedSection);
        Assert.Empty(historical.Incoming.Powers);
        historical.SelectedSegment = null;
        Assert.Empty(historical.Incoming.Summary);
        Assert.Null(historical.Incoming.SelectedPower);
        Assert.Equal(new[] { "Offense", "Incoming", "Healing" }, historical.Sections.Select(s => s.Label));
        Assert.DoesNotContain(historical.Sections, s => s.Label == "Pets");
        Assert.DoesNotContain(historical.Sections, s => s.Label == "Defense");
    }

    private static CombatPowerAnalysisRow Incoming(string name) =>
        CombatOffenseViewModelTests.Power(name) with { Direction = CombatAnalyticsDirection.Incoming };

    private static void AssertGenericFallback(ImageSource? icon)
    {
        Assert.Same(CombatOffenseViewModel.GenericDamageIcon, icon);
        var bitmap = Assert.IsType<BitmapImage>(icon);
        Assert.Equal(AssetUri.ForResource(CombatOffenseViewModel.GenericDamageIconResource), bitmap.UriSource);
    }

    private sealed class Catalog : IHomecomingPowerReferenceCatalog
    {
        public bool IsLoaded => true;
        public (string, string, string)? Last { get; private set; }
        public bool TryResolve(string categoryId, string powersetId, string powerId, out HomecomingPowerReference power)
        {
            Last = (categoryId, powersetId, powerId);
            power = new(categoryId, powersetId, powerId, "Fire Melee", "Burn", "existing-icon", false, false, HomecomingPowerType.Click);
            return true;
        }
        public bool TryResolveUniqueDisplayName(string? displayName, out HomecomingPowerReference power)
        {
            power = default;
            return false;
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
